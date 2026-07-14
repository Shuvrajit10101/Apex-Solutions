using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>e-Invoice engine</b> (Phase 9 slice 4a; RQ-5, RQ-18; ER-5). A pure, framework-/clock-/DB-free service over a
/// <see cref="Company"/> that decides applicability (<see cref="CoverageOf"/>), assembles the per-voucher IRP request
/// record (<see cref="PrepareRecord"/>), and records the IRP's response (<see cref="RecordIrpResponse"/> /
/// <see cref="RecordFailure"/>) and a 24-h full-document cancel (<see cref="Cancel"/>).
/// <para>
/// <b>Mode-agnostic (RQ-30):</b> the engine NEVER branches on <see cref="GstConfig.ConnectorMode"/> — it builds the
/// request and records whatever the connector hands back — so the offline default cannot be "leaked into" by the
/// live/GSP seams. <b>ER-5:</b> the engine has <b>no</b> method that computes an IRN or signs a QR; the 64-char IRN and
/// the signed QR only ever arrive as parameters on <see cref="RecordIrpResponse"/> (the connector's inbound artefacts),
/// then are stored verbatim on the <see cref="EInvoiceRecord"/>.
/// </para>
/// </summary>
public sealed class EInvoiceService
{
    private readonly Company _company;

    public EInvoiceService(Company company) => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// The e-invoice applicability verdict for a voucher (§2.2). <see cref="EInvoiceCoverage.NotApplicable"/> when
    /// e-invoicing is off / the company is Composition (a Bill of Supply is never e-invoiced — short-circuited BEFORE the
    /// covered check) / the voucher pre-dates applicability / the voucher is not an outward supply (an inward
    /// purchase/import/ISD). <see cref="EInvoiceCoverage.Exempt"/> when the supplier's typed business class is exempt.
    /// <see cref="EInvoiceCoverage.Excluded"/> for a B2C supply (which takes the B2C-QR path instead) and for a domestic
    /// B2B supply that carries NO forward tax (an exempt/nil-only supply is itself a Bill of Supply, never e-invoiced).
    /// Otherwise <see cref="EInvoiceCoverage.Covered"/> (B2B / export / SEZ / RCM-supplier-liable / a B2B
    /// credit-/debit-note).
    /// </summary>
    public EInvoiceCoverage CoverageOf(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var gst = _company.Gst;
        if (gst is not { Enabled: true } || !gst.EInvoicingEnabled)
            return EInvoiceCoverage.NotApplicable;

        // Composition dealers issue a Bill of Supply — never e-invoiced. Short-circuit BEFORE the covered check
        // (mirrors the Gstr1.Build composition early-return, risk #7).
        if (gst.RegistrationType == GstRegistrationType.Composition)
            return EInvoiceCoverage.NotApplicable;

        if (gst.EInvoiceApplicableFrom is { } from && voucher.Date < from)
            return EInvoiceCoverage.NotApplicable;

        var type = _company.FindVoucherType(voucher.TypeId);
        if (type is null) return EInvoiceCoverage.NotApplicable;

        // e-Invoicing covers only OUTWARD supplies (Sales = INV, Credit-Note = CRN). Inward purchases / imports / ISD
        // are inward documents and never carry a supplier IRN ⇒ Not-Applicable (excluded by construction).
        if (GstReportSupport.DirectionOf(type.BaseType) != GstTaxDirection.Output)
            return EInvoiceCoverage.NotApplicable;

        // A typed exemption on the supplier's business class exempts every document, regardless of turnover (§2.2).
        if (gst.ExemptionClasses != EInvoiceExemptionClass.None)
            return EInvoiceCoverage.Exempt;

        // Supply category from the party GST block + voucher. A B2C domestic supply is EXCLUDED (it takes the
        // self-generated B2C-QR path, not the IRP).
        var category = ResolveSupplyCategory(voucher);
        if (category is null)
            return EInvoiceCoverage.Excluded;

        // A domestic B2B supply that carries NO forward taxable value is an exempt/nil/non-GST supply — a Bill of
        // Supply, which is never e-invoiced (an IRN request + zero-value INV-01 must never be minted for it). A
        // zero-rated EXPORT and an outward RCM supply are legitimately zero-forward-tax yet covered, so this exclusion
        // is scoped to the ordinary domestic B2B (Regular) category — the only covered category whose zero-tax reading
        // means "exempt". SEZ / deemed-export are not minted here yet. Reuses the GSTR-1/3B taxable-value projection
        // (which already ring-fences cess and RCM lines), matching how a Bill of Supply is detected elsewhere.
        if (category == EInvoiceSupplyCategory.Regular && GstReportSupport.InvoiceTaxableValue(voucher).Amount == 0m)
            return EInvoiceCoverage.Excluded;

        return EInvoiceCoverage.Covered;
    }

    /// <summary>
    /// The <see cref="EInvoiceSupplyCategory"/> of an outward voucher, or <c>null</c> when it is an excluded B2C supply
    /// (§2.5). Resolution (from the data the party GST block currently expresses): an outward reverse-charge supply ⇒
    /// <see cref="EInvoiceSupplyCategory.RcmSupplierLiable"/>; an overseas place of supply (GST code 96/97) ⇒
    /// <see cref="EInvoiceSupplyCategory.Export"/>; a registered (GSTIN-bearing) recipient ⇒
    /// <see cref="EInvoiceSupplyCategory.Regular"/>; else a domestic consumer ⇒ <c>null</c> (B2C, excluded). SEZ /
    /// deemed-export are modelled on the enum for the INV-01 writer but not minted here until a party SEZ flag exists.
    /// </summary>
    public EInvoiceSupplyCategory? ResolveSupplyCategory(Voucher voucher)
    {
        if (GstReportSupport.IsOutwardReverseChargeSupply(_company, voucher))
            return EInvoiceSupplyCategory.RcmSupplierLiable;

        var partyGst = voucher.PartyId is Guid pid ? _company.FindLedger(pid)?.PartyGst : null;

        // Export: an overseas place of supply (GST convention: 96 = Other Country, 97 = Other Territory).
        if (partyGst?.StateCode is "96" or "97")
            return EInvoiceSupplyCategory.Export;

        // A registered recipient (a real GSTIN) ⇒ ordinary B2B.
        if (partyGst is { IsB2C: false })
            return EInvoiceSupplyCategory.Regular;

        // Domestic unregistered/consumer ⇒ B2C (excluded from e-invoicing).
        return null;
    }

    /// <summary>The printed document number used to build the IRN request — the voucher's number, <b>uppercased</b>
    /// (invariant-culture) BEFORE submission so the request, the stored artefact and any later cancel reference the
    /// identical doc-no (§2.4; IRP is case-insensitive from 01-Jun-2025).</summary>
    public static string DocumentNumberOf(Voucher voucher) =>
        (voucher.Number > 0 ? voucher.Number.ToString(CultureInfo.InvariantCulture) : voucher.Id.ToString("N"))
            .ToUpperInvariant();

    /// <summary>
    /// Assembles a fresh <see cref="EInvoiceRecord"/> (status Pending) for a <b>covered</b> voucher and attaches it to the
    /// company. Refuses a non-covered voucher (ER-15 — B2C/excluded never enters the IRP path), a voucher that already has
    /// a record, and a document number already used by ANY record (even a cancelled one — a cancelled doc-no is never
    /// reusable, §2.5). The caller hands the request to the selected connector; there is <b>no local IRN computation</b>.
    /// </summary>
    public EInvoiceRecord PrepareRecord(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (CoverageOf(voucher) != EInvoiceCoverage.Covered)
            throw new InvalidOperationException("Only a covered document can be prepared for an IRN request.");

        if (_company.FindEInvoiceRecordForVoucher(voucher.Id) is not null)
            throw new InvalidOperationException("An e-invoice record already exists for this voucher.");

        var docNoUpper = DocumentNumberOf(voucher);
        if (_company.HasEInvoiceDocumentNumber(docNoUpper))
            throw new InvalidOperationException(
                $"Document number '{docNoUpper}' is already used by an e-invoice record and cannot be reused (a cancelled doc-no is not reusable).");

        var record = new EInvoiceRecord(Guid.NewGuid(), voucher.Id, docNoUpper);
        record.MarkPending();
        _company.AddEInvoiceRecord(record);
        return record;
    }

    /// <summary>Records the IRP's <b>signed response</b> on a Pending record — stores the IRN/Ack/QR/signed-JSON verbatim
    /// (ER-5) and flips it to <see cref="EInvoiceStatus.Generated"/>. The values are the connector's inbound artefacts;
    /// none is computed here.</summary>
    public void RecordIrpResponse(EInvoiceRecord record, string irn, string ackNo, DateOnly ackDate, string signedQr, byte[] signedJson)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.RecordIrpResponse(irn, ackNo, ackDate, signedQr, signedJson);
    }

    /// <summary>Records an IRP rejection on a record (status Failed).</summary>
    public void RecordFailure(EInvoiceRecord record, string errorCode, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.MarkFailed(errorCode, errorMessage);
    }

    /// <summary>
    /// Cancels a Generated IRN within the 24-h window (§2.5) — <b>full document only</b> (no partial-line cancel). The
    /// engine is clock-free, so <paramref name="today"/> is supplied; the window is <c>today ≤ AckDate + 1 day</c>. The
    /// cancelled document number is NOT reusable (enforced by <see cref="PrepareRecord"/>). There is no amend-on-IRP.
    /// </summary>
    public void Cancel(EInvoiceRecord record, DateOnly today, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != EInvoiceStatus.Generated)
            throw new InvalidOperationException("Only a Generated e-invoice can be cancelled.");
        if (record.AckDate is not { } ackDate)
            throw new InvalidOperationException("A Generated e-invoice must carry an acknowledgement date to be cancelled.");
        if (today > ackDate.AddDays(1))
            throw new InvalidOperationException("The 24-hour e-invoice cancellation window has elapsed.");

        record.MarkCancelled(today, reasonCode);
    }

    /// <summary>
    /// True iff the 30-day reporting-age limit is BREACHED for a covered voucher (§2.2) — fires ONLY when
    /// <see cref="GstConfig.ReportingAgeLimitApplies"/> (AATO ≥ ₹10 cr), computed against the document date INDEPENDENTLY
    /// of the ₹5 cr applicability threshold. An advisory warning surfaced by the service (not a hard block on the covered
    /// set). A company below ₹10 cr always returns false.
    /// </summary>
    public bool IsReportingAgeExceeded(Voucher voucher, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (_company.Gst is not { ReportingAgeLimitApplies: true }) return false;
        return today > voucher.Date.AddDays(30);
    }
}
