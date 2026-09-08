namespace Apex.Ledger.Domain;

/// <summary>
/// The company-level <b>State VAT and Central Sales Tax</b> configuration (census 15.1 · 15.6; schema v59) —
/// the vendor's <i>Company VAT Details</i> screen, reached from F11 by setting <i>"Enable Value Added Tax
/// (VAT)"</i> to Yes.
///
/// <para>🔴 <b>WHY A REPEALED-LOOKING TAX IS IN A 2026 PRODUCT AT ALL, AND THE ONE SENTENCE THAT KEEPS IT
/// HONEST.</b> GST subsumed VAT and CST for ordinary goods on 01-Jul-2017. It did NOT subsume them for the
/// goods listed in <see cref="NonGstGoodsClass"/> — alcoholic liquor for human consumption, which the
/// Constitution puts outside GST permanently (Art. 366(12A), and CGST Act s.9(1) verbatim: "<i>except on the
/// supply of alcoholic liquor for human consumption</i>"), and the five petroleum products CGST Act s.9(2)
/// keeps out until the GST Council notifies otherwise. A liquor wholesaler or a fuel dealer still runs on State
/// VAT and still issues Form C. That, and only that, is what this configuration is for — which is why
/// <see cref="Enabled"/> alone is never sufficient to put a VAT figure on a line: the ITEM's class of goods
/// decides, item by item.</para>
///
/// <para><b>R7 — every field below is a vendor field label, quoted.</b> The Company VAT Details screen is
/// enumerated on
/// <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-configuring-accounts-only-company-tally/</c>
/// ("Press F11 (Feature) and enable … <i>Enable Value Added Tax (VAT)</i> … enter the 11-digit TIN … the
/// <i>Interstate sales tax number</i> … <i>VAT applicable from</i> date … Periodicity … Monthly or Quarterly …
/// <i>CST Rate Against Form C</i>") and on
/// <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-enable-vat-tally/</c> ("Press F11 (Features)
/// &gt; set the option <i>Enable Value Added Tax (VAT)</i> to Yes, to open the <i>Company VAT Details</i>
/// screen … Enter the company TIN and <i>Interstate sales tax number</i>"). <i>Type of Dealer</i> is
/// <c>help.tallysolutions.com/tally-prime/getting-started/configuring-vat-masters-tally/</c>.</para>
///
/// <para>🔴 <b>WHAT IS DELIBERATELY NOT HERE.</b> The vendor's screen also carries state-specific filing
/// details (LVO/VSO code, authorised person, status/designation, place) and a VAT-commodity master
/// ("<i>Define VAT commodity and tax details as masters</i>"). Neither is built: the filing block only has
/// meaning against a state return form, and the ~30 state return formats are repealed layouts with no live
/// official home — see the type remarks on the VAT Computation report. Capturing a filing block that no
/// document in this build can print would be storage pretending to be a feature.</para>
///
/// <para>Mutable master hung off <see cref="Company"/> as a nullable reference, mirroring
/// <see cref="GstConfig"/>. <c>null</c> — or a config with <see cref="Enabled"/> false — is a non-VAT company,
/// which is what every existing book is (ER-13).</para>
/// </summary>
public sealed class VatConfig
{
    /// <summary>
    /// The F11 gate, vendor caption <i>"Enable Value Added Tax (VAT)"</i>. When false no VAT field, master
    /// block or report is active anywhere.
    ///
    /// <para>🔴 <b>This is a NECESSARY and NOT a SUFFICIENT condition for a VAT figure.</b> Applicability is
    /// decided per item by <see cref="NonGstGoods.IsOutsideGst"/>. A company may lawfully enable VAT (it sells
    /// liquor) and still have most of its catalogue outside VAT's reach.</para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Vendor field <i>"TIN"</i> — the company's own VAT Tax Identification Number. The vendor describes it as
    /// "the 11-digit TIN (starting with state code)"; it is stored as entered and NOT validated to 11 digits
    /// here, because the format was a STATE matter and the states did not agree on one. Validating to a shape
    /// no single source prescribes would reject real numbers.
    /// </summary>
    public string? Tin { get; set; }

    /// <summary>
    /// Vendor field <i>"Interstate sales tax number"</i> — the CST registration number, the identity under
    /// which the declaration forms of census row 15.6 are received and issued.
    /// </summary>
    public string? InterstateSalesTaxNumber { get; set; }

    /// <summary>
    /// Vendor field <i>"VAT applicable from"</i>.
    ///
    /// <para>⚠️ Census row 15.1's own title calls this "registration date". The vendor's field is <b>VAT
    /// applicable from</b>, and the two are not the same thing — a dealer may register on one date and elect a
    /// later date from which the product should apply VAT. The vendor's field is what ships; the row title is
    /// reported as needing a re-word rather than being silently honoured.</para>
    /// </summary>
    public DateOnly? ApplicableFrom { get; set; }

    /// <summary>Vendor field: the return <i>Periodicity</i>, "Monthly or Quarterly". Defaults to
    /// <see cref="VatReturnPeriodicity.Monthly"/>.</summary>
    public VatReturnPeriodicity Periodicity { get; set; } = VatReturnPeriodicity.Monthly;

    /// <summary>Vendor field <i>"Type of Dealer"</i>. Defaults to <see cref="VatDealerType.Regular"/>. See that
    /// enum for why only two values ship.</summary>
    public VatDealerType DealerType { get; set; } = VatDealerType.Regular;

    /// <summary>
    /// Vendor field <i>"CST Rate Against Form C"</i>, in <b>basis points</b> (integer — never a REAL; see
    /// <c>Paisa.cs</c> for why this codebase keeps rates as integers). <c>null</c> means <b>not set</b>.
    ///
    /// <para>🔴 <b>THERE IS NO SEEDED DEFAULT, AND ITS ABSENCE IS THE POINT.</b> The concessional inter-State
    /// rate against Form C is widely stated as 2% (CST Act 1956 s.8(1)). This slice could NOT retrieve that
    /// sentence from an official source — <c>indiacode.nic.in</c> returned HTTP 403 on both PDF copies of the
    /// Act and refused the connection on the third — and this project has an OPEN, unresolved user decision
    /// about a statutory rate it shipped on a source that did not hold up (the 4% cess). So no rate is asserted
    /// anywhere in this build: the operator supplies it, on the vendor's own field, and every figure the VAT
    /// Computation report shows against Form C traces to a number a human entered. If the statute is later
    /// retrieved from an official source, seed it HERE and nowhere else.</para>
    /// </summary>
    public int? CstRateAgainstFormCBasisPoints { get; set; }

    /// <summary>True iff a company-level Form-C rate has been supplied by the operator.</summary>
    public bool HasCstRateAgainstFormC => CstRateAgainstFormCBasisPoints is > 0;
}
