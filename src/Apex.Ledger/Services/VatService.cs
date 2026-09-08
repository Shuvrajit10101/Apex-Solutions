using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The write side of <b>State VAT and Central Sales Tax</b> (census 15.1 State VAT · 15.2 the Tax Rate on the
/// masters · 15.6 CST declaration forms) — the one place a VAT registration, a class of goods, a VAT rate or a
/// declaration form is set.
///
/// <para>🔴 <b>THIS TYPE EXISTS TO REFUSE, NOT MERELY TO SET.</b> Every other master service here is a
/// convenience over properties an operator could set directly. This one carries the rule that keeps a repealed
/// tax from being computed on goods it no longer reaches: VAT and CST survive GST only for alcoholic liquor for
/// human consumption (Constitution Art. 366(12A); CGST Act s.9(1)) and the five petroleum products (CGST Act
/// s.9(2)). <see cref="SetItemVatRate"/> and <see cref="SetLedgerVat"/> therefore throw rather than quietly
/// accept a rate on ordinary GST goods, and <see cref="NonGstGoods.VatRefusalReason"/> supplies the sentence the
/// operator reads. Hiding the field in the UI is not enough — a hidden-but-settable field is exactly how an
/// ordinary item acquires a liquor rate.</para>
///
/// <para><b>R7.</b> Field labels and screens are quoted on <see cref="VatConfig"/>,
/// <see cref="Ledger.VatTaxRateBasisPoints"/> and <see cref="StockItem.VatTaxRateBasisPoints"/>. Vendor pages:
/// <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-enable-vat-tally/</c>,
/// <c>…/getting-started/configuring-vat-masters-tally/</c>,
/// <c>…/vat-masters/india-vat-party-ledger-tally/</c> and
/// <c>…/reports/forms-receivables-tally/</c>.</para>
///
/// <para>🔴 <b>NO TAX IS POSTED BY ANY METHOD HERE, AND THAT IS A DELIBERATE, LABELLED LIMIT.</b> Unlike
/// <c>GstService.EnableGst</c>, this service creates NO tax ledgers and writes NO entry line. VAT liability is
/// <b>computed and reported</b> by <c>VatComputation</c> from the transaction values, exactly as the vendor's
/// own VAT Computation report is described; it never appears as a posted leg in the books. An operator who owes
/// VAT records the payment as an ordinary voucher. Read <c>VatComputation</c>'s remarks before assuming a
/// figure on that report is in the Trial Balance — it is not.</para>
/// </summary>
public sealed class VatService
{
    private readonly Company _company;

    /// <summary>Creates the service over <paramref name="company"/>.</summary>
    public VatService(Company company) =>
        _company = company ?? throw new ArgumentNullException(nameof(company));

    // ================================================================= 15.1 — the company registration

    /// <summary>
    /// Turns on State VAT for the company (the vendor's F11 <i>"Enable Value Added Tax (VAT)"</i>, which opens
    /// the <i>Company VAT Details</i> screen) and records the registration.
    ///
    /// <para>🔴 <b>Enabling VAT applies VAT to NOTHING on its own.</b> It makes the masters and reports
    /// reachable; whether any given line bears VAT is decided item by item by
    /// <see cref="NonGstGoods.IsOutsideGst"/>. A liquor wholesaler enables this and still has most of its
    /// catalogue outside VAT's reach.</para>
    ///
    /// <para>🔴 <b><paramref name="cstRateAgainstFormCBasisPoints"/> HAS NO DEFAULT AND NONE MAY BE ADDED.</b>
    /// The concessional Form-C rate could not be retrieved from an official source for this slice
    /// (<c>indiacode.nic.in</c> refused every copy of the CST Act 1956), and this project already has an open,
    /// unresolved user decision about a statutory rate it shipped on a source that did not hold up. Passing
    /// <c>null</c> leaves it unset and the CST Payable section of the VAT Computation report says so, rather
    /// than showing a figure derived from a number nobody sourced.</para>
    /// </summary>
    /// <param name="tin">Vendor field <i>"TIN"</i>, or <c>null</c>.</param>
    /// <param name="interstateSalesTaxNumber">Vendor field <i>"Interstate sales tax number"</i>, or <c>null</c>.</param>
    /// <param name="applicableFrom">Vendor field <i>"VAT applicable from"</i>, or <c>null</c>.</param>
    /// <param name="dealerType">Vendor field <i>"Type of Dealer"</i>; defaults to Regular.</param>
    /// <param name="periodicity">The return periodicity; defaults to Monthly.</param>
    /// <param name="cstRateAgainstFormCBasisPoints">Vendor field <i>"CST Rate Against Form C"</i> in basis
    /// points, or <c>null</c> when the operator has not supplied one.</param>
    public VatConfig EnableVat(
        string? tin = null,
        string? interstateSalesTaxNumber = null,
        DateOnly? applicableFrom = null,
        VatDealerType dealerType = VatDealerType.Regular,
        VatReturnPeriodicity periodicity = VatReturnPeriodicity.Monthly,
        int? cstRateAgainstFormCBasisPoints = null)
    {
        if (cstRateAgainstFormCBasisPoints is { } bp && bp < 0)
            throw new ArgumentOutOfRangeException(
                nameof(cstRateAgainstFormCBasisPoints), bp, "A CST rate cannot be negative.");

        var config = _company.Vat ?? new VatConfig();
        config.Enabled = true;
        config.Tin = Trimmed(tin);
        config.InterstateSalesTaxNumber = Trimmed(interstateSalesTaxNumber);
        config.ApplicableFrom = applicableFrom;
        config.DealerType = dealerType;
        config.Periodicity = periodicity;
        config.CstRateAgainstFormCBasisPoints = cstRateAgainstFormCBasisPoints;
        _company.Vat = config;
        return config;
    }

    /// <summary>
    /// Turns State VAT off. The registration details are KEPT, so re-enabling does not make the operator re-key
    /// a TIN — only <see cref="VatConfig.Enabled"/> flips, and every VAT master field and report goes inert.
    /// A company that never enabled VAT has a <c>null</c> <see cref="Company.Vat"/> and is untouched.
    /// </summary>
    public void DisableVat()
    {
        if (_company.Vat is { } config) config.Enabled = false;
    }

    // ================================================================= 15.2 — the masters

    /// <summary>
    /// Sets a stock item's <b>class of goods</b> — the fact that decides whether VAT and CST can reach it at
    /// all (see <see cref="NonGstGoodsClass"/>).
    ///
    /// <para>🔴 <b>Moving an item BACK inside GST clears its VAT rate.</b> Leaving a stale rate on an item that
    /// is no longer a VAT good would leave a figure the reports must then decide whether to believe; clearing
    /// it makes "inside GST" and "no VAT rate" the same state, which is what the schema's own
    /// <c>non_gst_goods_class DEFAULT 0</c> / <c>vat_tax_rate_bp NULL</c> pair already says about every
    /// untouched item.</para>
    /// </summary>
    public void SetItemGoodsClass(StockItem item, NonGstGoodsClass goodsClass)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.NonGstGoodsClass = goodsClass;
        if (!NonGstGoods.IsOutsideGst(goodsClass)) item.VatTaxRateBasisPoints = null;
    }

    /// <summary>
    /// Sets the vendor's <i>"Tax rate"</i> on a stock item's VAT block, in basis points; <c>null</c> clears it.
    ///
    /// <para>🔴 <b>REFUSES on an item whose goods are inside GST</b> — with
    /// <see cref="NonGstGoods.VatRefusalReason"/>'s sentence, so the operator is told WHY and what to change,
    /// not merely that they cannot. Clearing a rate (<c>null</c>) is always allowed: taking a wrong figure off
    /// an item must never be blocked by the rule that stopped it going on.</para>
    /// </summary>
    public void SetItemVatRate(StockItem item, int? rateBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (rateBasisPoints is null) { item.VatTaxRateBasisPoints = null; return; }
        if (rateBasisPoints < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rateBasisPoints), rateBasisPoints, "A VAT rate cannot be negative.");
        if (NonGstGoods.VatRefusalReason(item.NonGstGoodsClass) is { } reason)
            throw new InvalidOperationException(
                $"Cannot set a VAT rate on \"{item.Name}\". {reason}");
        item.VatTaxRateBasisPoints = rateBasisPoints;
    }

    /// <summary>
    /// Sets the vendor's <i>"VAT Applicable"</i> and <i>"Tax Rate"</i> on a <b>sales/purchase</b> ledger — the
    /// accounts-only route the vendor documents separately at
    /// <c>…/vat-masters/india-vat-configuring-accounts-only-company-tally/</c>, where there is no stock item to
    /// ask for a rate.
    ///
    /// <para>🔴 <b>The gate cannot be enforced here and the honest consequence is stated rather than
    /// hidden.</b> A ledger has no class of goods, so this method cannot check one; a company that keys a VAT
    /// rate onto a sales ledger is asserting that what it sells is outside GST. <c>VatComputation</c> therefore
    /// uses a ledger rate ONLY for a voucher that carries no item lines — the moment there are items, the
    /// items' own classes decide and the ledger rate is ignored. That asymmetry is deliberate: it means the
    /// unguarded path can never override the guarded one.</para>
    /// </summary>
    public void SetLedgerVat(Domain.Ledger ledger, bool vatApplicable, int? rateBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (rateBasisPoints is { } bp && bp < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rateBasisPoints), bp, "A VAT rate cannot be negative.");
        ledger.VatApplicable = vatApplicable;
        ledger.VatTaxRateBasisPoints = vatApplicable ? rateBasisPoints : null;
    }

    /// <summary>
    /// Records the counterparty's VAT identity on a <b>party</b> ledger — the vendor's <i>"Type of Dealer"</i>,
    /// <i>"TIN/Sales Tax No."</i> and <i>"CST No."</i>. Shown on the two Declaration Forms reports so a pending
    /// form can be tied to the dealer who owes it.
    /// </summary>
    public void SetPartyVatDetails(
        Domain.Ledger ledger, VatDealerType? dealerType, string? vatTin, string? cstNumber)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ledger.PartyVatDealerType = dealerType;
        ledger.PartyVatTin = Trimmed(vatTin);
        ledger.PartyCstNumber = Trimmed(cstNumber);
    }

    // ================================================================= 15.6 — the declaration forms

    /// <summary>
    /// Records (or clears) the CST declaration form covering a voucher — the vendor's <i>Set/Alter Form No</i>
    /// action (Alt+S) on the Forms Receivable / Forms Issuable reports, which is where the vendor already has
    /// an operator filling the series, number and date.
    ///
    /// <para>⚠️ <b>The form TYPE is keyed here too, and that is a DOCUMENTED DIVERGENCE labelled as ours.</b>
    /// The reference product derives it from a <i>nature of transaction</i> master this product does not have.
    /// See <see cref="Voucher.CstFormType"/>.</para>
    ///
    /// <para>🔴 <b>Clearing the TYPE clears the whole block.</b> A series or a number hanging off a voucher
    /// with no form type is an orphan neither Declaration Forms report can place, so "no form" means all four
    /// fields are null and there is no half-state to reason about.</para>
    ///
    /// <para>A blank <paramref name="formNumber"/> is the vendor's own "pending" state and is preserved as
    /// <c>null</c> — <see cref="Voucher.CstFormReceived"/> is exactly its negation.</para>
    /// </summary>
    public void SetCstDeclarationForm(
        Voucher voucher,
        CstDeclarationForm? formType,
        string? seriesNumber = null,
        string? formNumber = null,
        DateOnly? formDate = null)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (formType is null)
        {
            voucher.CstFormType = null;
            voucher.CstFormSeriesNumber = null;
            voucher.CstFormNumber = null;
            voucher.CstFormDate = null;
            return;
        }

        voucher.CstFormType = formType;
        voucher.CstFormSeriesNumber = Trimmed(seriesNumber);
        voucher.CstFormNumber = Trimmed(formNumber);
        voucher.CstFormDate = formDate;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
