using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>e-Way Bill engine</b> (Phase 9 slice 5; RQ-6, RQ-19; ER-5). The outbound-artefact twin of
/// <see cref="EInvoiceService"/>: a pure, framework-/clock-/DB-free service over a <see cref="Company"/> that decides
/// Rule-138 applicability (<see cref="CoverageOf"/>), computes the dated consignment value (<see cref="ConsignmentValue"/>),
/// assembles the per-voucher EWB request record (<see cref="PrepareRecord"/> + <see cref="SetPartB"/>), and records the
/// portal's response (<see cref="RecordPortalResponse"/> / <see cref="RecordFailure"/>), a 24-h full-document cancel
/// (<see cref="Cancel"/>) and a portal-granted extension (<see cref="Extend"/>).
/// <para>
/// <b>Mode-agnostic (RQ-30):</b> the engine NEVER branches on <see cref="GstConfig.ConnectorMode"/> — it builds the
/// request and records whatever the connector hands back — so the offline default cannot be "leaked into" by the
/// live/GSP seams. <b>ER-5 twin:</b> the engine has <b>no</b> method that derives the 12-digit EWB number or the
/// validity; both only ever arrive as parameters on <see cref="RecordPortalResponse"/> (the connector's inbound
/// artefacts), then are stored verbatim on the <see cref="EWayBillRecord"/>. The validity <i>arithmetic</i> lives in the
/// pure clock-free <see cref="EWayValidity"/> helper (used to advise the user), never to mint a portal value.
/// </para>
/// </summary>
public sealed class EWayBillService
{
    /// <summary>The date the Ship-To GSTIN requirement + the voluntary-closure operation activate (§2.5, DP-12). Before
    /// this date the fields are inert/optional (byte-identical when unused, ER-13).</summary>
    public static readonly DateOnly EWayShipToMandatoryFrom = new(2026, 8, 1);

    /// <summary>Rule 138E (blocked generation on 2-period non-filing) cannot be enforced offline — surfaced as a
    /// warning-only advisory (never a hard block), mirroring how the 2A/2B recon and reporting-age stay advisory.</summary>
    public const string Rule138EAdvisory =
        "Rule 138E (blocked generation on two-period non-filing) cannot be verified offline — confirm the counter-party " +
        "GSTIN is not blocked on the portal before generating.";

    /// <summary>The other-party accept/reject is a portal-side action the offline clone cannot perform — surfaced as a
    /// 72-h deemed-accept advisory note, never an enforced state transition.</summary>
    public const string OtherPartyDeemedAcceptNote =
        "The recipient may accept/reject within 72 hours of generation; no action within the window is deemed acceptance " +
        "(advisory — portal-side, not enforced offline).";

    private readonly Company _company;

    public EWayBillService(Company company) => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ================================================================ applicability + value

    /// <summary>
    /// The Rule-138 applicability verdict for a voucher (§2.6). <see cref="EWayCoverage.NotApplicable"/> when e-Way is off
    /// / the movement pre-dates applicability / it is not a goods-movement document (only Sales / Purchase / Credit- /
    /// Debit-Note / Delivery- / Receipt-Note documents that actually carry an inventory movement — a pure-service invoice
    /// does not). <see cref="EWayCoverage.MandatoryIrrespectiveOfValue"/> for an inter-state principal↔job-worker or
    /// inter-state handicraft movement (short-circuits the threshold). <see cref="EWayCoverage.NotRequired"/> for an
    /// intra-state movement in a company that exempts intra-state e-Way, or when the consignment value is at/below the
    /// effective threshold. Otherwise <see cref="EWayCoverage.Required"/> — the consignment value <b>strictly exceeds</b>
    /// the effective threshold (₹50,000.00 exactly is NOT covered; the STRICT <c>&gt;</c> boundary, §2.6 verifier).
    /// </summary>
    public EWayCoverage CoverageOf(Voucher voucher, EWayTransactionType txnType = EWayTransactionType.Regular)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var gst = _company.Gst;
        if (gst is not { Enabled: true } || !gst.EWayBillEnabled)
            return EWayCoverage.NotApplicable;
        if (gst.EWayApplicableFrom is { } from && voucher.Date < from)
            return EWayCoverage.NotApplicable;

        var type = _company.FindVoucherType(voucher.TypeId);
        if (type is null || !IsGoodsMovementDocument(type.BaseType) || !voucher.HasInventoryLines)
            return EWayCoverage.NotApplicable;

        var interState = IsInterState(voucher);

        // Inter-state job-work / handicraft ⇒ mandatory regardless of value (short-circuit before the threshold test).
        if (interState && txnType is EWayTransactionType.JobWork or EWayTransactionType.Handicraft)
            return EWayCoverage.MandatoryIrrespectiveOfValue;

        // A company that exempts intra-state e-Way entirely ⇒ an intra-state movement is Not-Required.
        if (!interState && !gst.EWayIntraStateApplicable)
            return EWayCoverage.NotRequired;

        var value = ConsignmentValue(voucher);
        var threshold = EffectiveThreshold(voucher, txnType, interState);
        return value > threshold ? EWayCoverage.Required : EWayCoverage.NotRequired; // STRICT >
    }

    /// <summary>
    /// The Rule-138 <b>consignment value</b> of a movement (§1.3; ER-9). A <b>pure projection over the posted lines</b> —
    /// it never re-runs <c>ComputeInvoiceTax</c> or <c>ResolveCess</c>.
    /// <para>
    /// <b>Regular tax-scheme supply</b> (the movement carries forward CGST/SGST/IGST lines): assessable §15 taxable value
    /// (<see cref="GstReportSupport.InvoiceTaxableValue"/>, which already excludes cess, RCM and the exempt portion) +
    /// posted forward CGST/SGST/IGST + posted ring-fenced cess (<see cref="GstReportSupport.PostedCessTotal"/> —
    /// date-aware by construction, since S1's cess compute already dropped non-tobacco cess on/after 22-Sep-2025). When
    /// the basis is not <see cref="EWayConsignmentBasis.Rule138Default"/> the exempt-supply portion is added on top.
    /// </para>
    /// <para>
    /// <b>No-forward-tax movement</b> (a Composition dealer's Bill of Supply, or any exempt-only / no-tax goods movement,
    /// finding #1): such a document posts <b>no</b> tax lines, so <see cref="GstReportSupport.InvoiceTaxableValue"/> /
    /// <see cref="GstReportSupport.PostedForwardTaxTotal"/> both read tax lines and would collapse the value to ₹0 — the
    /// e-Way Bill could then never be generated for an entire class of registered dealers. The assessable base instead
    /// falls back to the posted stock/sales <b>value</b> (<see cref="GstReportSupport.OutwardSupplyValue"/>, the very
    /// source the composition tax-on-turnover engine reads), so a ₹2,00,000 Bill-of-Supply movement values at ₹2,00,000,
    /// not 0. There is no forward tax or cess to add (the dealer collects none); the whole document travels on the truck.
    /// </para>
    /// </summary>
    public Money ConsignmentValue(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var type = _company.FindVoucherType(voucher.TypeId);
        var baseType = type?.BaseType ?? VoucherBaseType.Sales;

        // No forward tax lines ⇒ a Composition Bill of Supply / exempt-only / no-tax movement. The value is not on any
        // tax line; read it from the posted stock/sales value (the composition-engine source) so it can never be ₹0.
        if (!GstReportSupport.HasForwardTaxLines(voucher))
            return GstReportSupport.OutwardSupplyValue(_company, voucher, baseType).Total;

        var assessable = GstReportSupport.InvoiceTaxableValue(voucher);
        var forwardTax = GstReportSupport.PostedForwardTaxTotal(voucher);
        var cess = GstReportSupport.PostedCessTotal(voucher);
        var value = assessable + forwardTax + cess;

        if (_company.Gst?.ConsignmentBasis is EWayConsignmentBasis.TaxablePlusExempt or EWayConsignmentBasis.InvoiceValue)
        {
            var supply = GstReportSupport.OutwardSupplyValue(_company, voucher, baseType);
            var exemptPortion = supply.Total - supply.Taxable; // the exempt/nil-supply value split out by the helper
            value += exemptPortion;
        }
        return value;
    }

    /// <summary>The effective consignment threshold for a movement — the per-state / per-transaction-type override for the
    /// place-of-supply state (INTRA-state movements only), else the flat <see cref="GstConfig.EWayThreshold"/>. The state
    /// overrides are intra-state-only, so an inter-state consignment always uses the ₹50,000 default (risk #5).</summary>
    private Money EffectiveThreshold(Voucher voucher, EWayTransactionType txnType, bool interState)
    {
        var gst = _company.Gst!;
        if (!interState)
        {
            var pos = GstReportSupport.PlaceOfSupply(_company, voucher);
            var overrideRow = gst.EWayStateThresholds
                .FirstOrDefault(t => string.Equals(t.StateCode, pos, StringComparison.Ordinal) && t.TxnType == txnType);
            if (overrideRow is not null) return overrideRow.Threshold;
        }
        return gst.EWayThreshold;
    }

    private bool IsInterState(Voucher voucher)
    {
        var home = _company.Gst?.HomeStateCode;
        var pos = GstReportSupport.PlaceOfSupply(_company, voucher);
        return pos is not null && home is not null && !string.Equals(pos, home, StringComparison.Ordinal);
    }

    private static bool IsGoodsMovementDocument(VoucherBaseType baseType) => baseType is
        VoucherBaseType.Sales or VoucherBaseType.Purchase or VoucherBaseType.CreditNote or
        VoucherBaseType.DebitNote or VoucherBaseType.DeliveryNote or VoucherBaseType.ReceiptNote;

    // ================================================================ record lifecycle

    /// <summary>
    /// Assembles a fresh <see cref="EWayBillRecord"/> (status Pending) with its locally-computed Part A for a
    /// <b>covered</b> movement (Required / MandatoryIrrespectiveOfValue) and attaches it to the company. Refuses a
    /// non-covered voucher, a voucher that already has an active (non-cancelled) record, a base document more than 180
    /// days old (<paramref name="generationDate"/> − doc date &gt; 180), and — from 01-Aug-2026 — a movement with no
    /// Ship-To GSTIN. There is <b>no local EWB-number computation</b> (ER-5) — the caller hands the request to the
    /// selected connector and later records the portal response.
    /// </summary>
    public EWayBillRecord PrepareRecord(
        Voucher voucher, DateOnly generationDate, EWayTransactionType txnType = EWayTransactionType.Regular,
        string? shipToGstin = null)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var coverage = CoverageOf(voucher, txnType);
        if (coverage is EWayCoverage.NotApplicable or EWayCoverage.NotRequired)
            throw new InvalidOperationException(
                "Only a covered goods movement (Required or MandatoryIrrespectiveOfValue) can be prepared for an e-Way Bill.");

        if (_company.FindEWayBillRecordForVoucher(voucher.Id) is not null)
            throw new InvalidOperationException("An active e-Way Bill record already exists for this voucher.");

        // Base-doc age block: a voucher dated more than 180 days before generation cannot be e-Way-billed (§2.6).
        if (generationDate > voucher.Date.AddDays(180))
            throw new InvalidOperationException(
                "The base document is more than 180 days old; an e-Way Bill cannot be generated for it.");

        // Ship-To GSTIN gating (eff. 01-Aug-2026): required only for a movement dated on/after the cut-over.
        if (voucher.Date >= EWayShipToMandatoryFrom && string.IsNullOrWhiteSpace(shipToGstin))
            throw new InvalidOperationException("A Ship-To GSTIN is mandatory for e-Way Bills from 01-Aug-2026.");

        var type = _company.FindVoucherType(voucher.TypeId)!;
        var record = new EWayBillRecord(
            Guid.NewGuid(), voucher.Id, EInvoiceService.DocumentNumberOf(voucher),
            SupplyTypeOf(type.BaseType), SubSupplyTypeOf(txnType), DocTypeOf(type.BaseType),
            ToPaisa(ConsignmentValue(voucher)),
            _company.Gst!.HomeStateCode, GstReportSupport.PlaceOfSupply(_company, voucher), shipToGstin);
        _company.AddEWayBillRecord(record);
        return record;
    }

    /// <summary>Records the user-entered Part-B transport detail on a record (mode / vehicle / distance / transport-doc /
    /// ODC). The EWB number / validity are never set here (ER-5).</summary>
    public void SetPartB(
        EWayBillRecord record, string? transporterId, EWayTransportMode? mode, string? vehicleNumber,
        int distanceKm, string? transportDocNo = null, bool isOverDimensionalCargo = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.SetPartB(transporterId, mode, vehicleNumber, distanceKm, transportDocNo, isOverDimensionalCargo);
    }

    /// <summary>True iff Part-B (transport mode + vehicle/transport-doc) is required before submission — i.e. NOT the
    /// intra-state ≤ 50 km relaxation (consignor↔transporter or transporter↔consignee, §2.6).</summary>
    public static bool RequiresPartB(EWayBillRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var intra = string.Equals(record.ShipFromStateCode, record.ShipToStateCode, StringComparison.Ordinal);
        return !(intra && record.DistanceKm <= 50);
    }

    /// <summary>Throws unless the record is ready to submit: Part-B present, EXCEPT for the intra-state ≤ 50 km
    /// relaxation (§2.6, test #17). A pure guard the caller runs before handing the request to the connector.</summary>
    public void EnsureReadyToGenerate(EWayBillRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!RequiresPartB(record)) return;
        if (record.Mode is null && string.IsNullOrWhiteSpace(record.VehicleNumber) && string.IsNullOrWhiteSpace(record.TransportDocNo))
            throw new InvalidOperationException(
                "Part-B (transport mode + vehicle number / transport document) is required except for an intra-state movement of ≤ 50 km.");
    }

    /// <summary>Records the portal's response on a Pending/Failed record — stores the 12-digit EWB number + generation
    /// timestamp + validity verbatim (ER-5) and flips it to <see cref="EWayStatus.Generated"/>. None is computed here.</summary>
    public void RecordPortalResponse(EWayBillRecord record, string ewbNumber, DateTimeOffset generatedAt, DateTimeOffset validUpto)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.RecordPortalResponse(ewbNumber, generatedAt, validUpto);
    }

    /// <summary>Records a portal rejection on a record (status Failed).</summary>
    public void RecordFailure(EWayBillRecord record, string errorCode, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.MarkFailed(errorCode, errorMessage);
    }

    /// <summary>Cancels a Generated EWB within the 24-h window (§2.6) — full document only. The engine is clock-free, so
    /// <paramref name="today"/> is supplied; the window is <c>today ≤ generation-date + 1 day</c>. Unlike an IRN, a
    /// cancelled EWB frees the movement to be re-billed (no doc-no reuse block).</summary>
    public void Cancel(EWayBillRecord record, DateOnly today, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != EWayStatus.Generated)
            throw new InvalidOperationException("Only a Generated e-Way Bill can be cancelled.");
        if (record.GeneratedAt is not { } gen)
            throw new InvalidOperationException("A Generated e-Way Bill must carry a generation timestamp to be cancelled.");
        if (today > DateOnly.FromDateTime(gen.Date).AddDays(1))
            throw new InvalidOperationException("The 24-hour e-Way Bill cancellation window has elapsed.");

        record.MarkCancelled(today, reasonCode);
    }

    /// <summary>Extends a Generated EWB's validity within the ±8 h window (<see cref="EWayValidity.CanExtend"/>),
    /// re-computing for the remaining distance and refusing an extension past the 360-day cap. The new validity is
    /// still portal-computed in production; this advisory extension uses the pure <see cref="EWayValidity"/> helper.</summary>
    public void Extend(EWayBillRecord record, DateTimeOffset now, int remainingDistanceKm)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != EWayStatus.Generated)
            throw new InvalidOperationException("Only a Generated e-Way Bill can be extended.");
        if (record.GeneratedAt is not { } gen || record.ValidUpto is not { } validUpto)
            throw new InvalidOperationException("A Generated e-Way Bill must carry a generation timestamp + validity to be extended.");
        if (!EWayValidity.CanExtend(now, validUpto))
            throw new InvalidOperationException("An e-Way Bill can be extended only within 8 hours before/after its expiry.");

        var newValidUpto = EWayValidity.ExtendValidUpto(gen, now, remainingDistanceKm, record.IsOverDimensionalCargo);
        record.MarkExtended(newValidUpto);
    }

    /// <summary>Records a voluntary EWB closure (gated to 01-Aug-2026 by the movement's date). Advisory — no state
    /// transition; the record stays Generated with the closure flag/date set.</summary>
    public void Close(EWayBillRecord record, Voucher voucher, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(voucher);
        if (voucher.Date < EWayShipToMandatoryFrom)
            throw new InvalidOperationException("e-Way Bill voluntary closure is available only from 01-Aug-2026.");
        if (record.Status != EWayStatus.Generated)
            throw new InvalidOperationException("Only a Generated e-Way Bill can be closed.");
        record.MarkClosed(on);
    }

    /// <summary>True iff the other-party 72-h accept/reject window is still open for a Generated EWB as of
    /// <paramref name="now"/> — a pure advisory computation (no stored flag, no auto-transition; §2.3).</summary>
    public bool IsOtherPartyActionWindowOpen(EWayBillRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Status == EWayStatus.Generated && record.GeneratedAt is { } gen && now <= gen.AddHours(72);
    }

    // ================================================================ consolidated EWB-02

    /// <summary>
    /// Assembles a consolidated EWB-02 header over already-<b>Generated</b> children travelling in one conveyance
    /// (§2.4). The CEWB number likewise arrives inbound only; this returns a light header referencing the child EWB
    /// numbers with <b>no</b> monetary recomputation (ER-9). Throws if any child is not Generated / has no EWB number.
    /// </summary>
    public ConsolidatedEWayBill PrepareConsolidated(
        IReadOnlyList<EWayBillRecord> children, EWayTransportMode mode, string vehicleNumber, string fromStateCode)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0)
            throw new InvalidOperationException("A consolidated e-Way Bill requires at least one child EWB.");

        var numbers = new List<string>();
        foreach (var child in children)
        {
            if (child.Status != EWayStatus.Generated || string.IsNullOrWhiteSpace(child.EwbNumber))
                throw new InvalidOperationException(
                    "A consolidated e-Way Bill can group only Generated child EWBs (each with a portal-issued number).");
            numbers.Add(child.EwbNumber!);
        }
        return new ConsolidatedEWayBill(fromStateCode, mode, vehicleNumber, numbers);
    }

    // ================================================================ Part-A code helpers

    private static string SupplyTypeOf(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.Purchase or VoucherBaseType.DebitNote or VoucherBaseType.ReceiptNote => "Inward",
        _ => "Outward",
    };

    private static string SubSupplyTypeOf(EWayTransactionType txnType) => txnType switch
    {
        EWayTransactionType.JobWork => "Job Work",
        EWayTransactionType.Handicraft => "Handicraft",
        _ => "Supply",
    };

    private static string DocTypeOf(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.CreditNote => "CRN",
        VoucherBaseType.DebitNote => "DBN",
        VoucherBaseType.DeliveryNote or VoucherBaseType.ReceiptNote => "CHL",
        _ => "INV",
    };

    /// <summary>Integer-paisa value of a paisa-exact <see cref="Money"/> (consignment values are sums of posted
    /// paisa-exact amounts, so this is exact). Kept in the Ledger layer (no dependency on the Io <c>MoneyCodec</c>).</summary>
    private static long ToPaisa(Money money) =>
        (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);
}

/// <summary>A consolidated EWB-02 header (Phase 9 slice 5; §2.4) — a light join over the child EWB numbers travelling in
/// one conveyance. Carries NO monetary value (a consolidation is a header over generated children, ER-9). The CEWB
/// number, like a child EWB number, arrives inbound from the portal only (never on this record).</summary>
public sealed record ConsolidatedEWayBill(
    string FromStateCode, EWayTransportMode Mode, string VehicleNumber, IReadOnlyList<string> ChildEwbNumbers);
