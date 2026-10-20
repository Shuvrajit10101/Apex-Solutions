namespace Apex.Ledger.Domain;

/// <summary>
/// The vendor's <b>Type of Calculation</b> on a <see cref="VoucherClassAdditionalEntry"/> (census 2.6; schema
/// v62) — how the additional ledger's amount is DERIVED from the voucher the class is used on. Stored as the
/// enum ordinal.
/// </summary>
/// <remarks>
/// <para>🔴 <b>THIS ENUM IS DELIBERATELY SHORT, AND THE SHORTNESS IS THE POINT.</b> ~~The vendor's Type of
/// Calculation list is longer than the three members below.~~ <b>That sentence was itself unsourced and is
/// struck 2026-10-18 — see the MEASUREMENT block below.</b> Only these three are named by a vendor page that
/// could actually be retrieved and read, so only these three ship. The rest are NOT approximated, NOT guessed
/// from the older product's screen, and NOT inferred from what a tax engine "must" need — an additional-ledger
/// rule posts money onto every voucher that uses the class without prompting the operator, so a member whose
/// semantics were invented would post a wrong figure silently on every one of them. A class that needs an
/// unshipped calculation type cannot be expressed here, and that is the correct failure: it is visible.</para>
///
/// <para><b>R7 — WHAT IS ATTESTED, MEMBER BY MEMBER.</b></para>
/// <list type="bullet">
/// <item><see cref="AsTotalAmountRounding"/> — <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c>,
/// which instructs the operator to select a ledger under Indirect Expenses whose <i>Type of Ledger</i> is
/// <i>Invoice Rounding</i>, set <i>Use ledger for auto calculation of</i> to <i>Not Applicable</i>, and choose
/// <i>"As Total Amount Rounding"</i> as the Type of Calculation; the ledger then carries <i>"the difference
/// between the original and rounded amounts as a positive or negative value, automatically balancing the
/// invoice"</i>.</item>
/// <item><see cref="BasedOnQuantity"/> — <c>help.tallysolutions.com/tally-prime/importer-excise-masters/</c>
/// <c>ei-configure-vch-class-customs-duty-tally/</c>, which sets a duty ledger in <b>Additional Accounting
/// Entries</b> with <i>Type of Calculation</i> = <i>"Based on Quantity"</i> and a <i>Value Basis</i>, alongside
/// <i>Rounding Method</i>, <i>Rounding Limit</i> and <i>Remove if Zero</i>.</item>
/// <item><see cref="NotApplicable"/> — the vendor's own <i>"Not Applicable"</i>, used on the same round-off page
/// for the ledger's auto-calculation field. Here it means the class names the ledger but derives no amount for
/// it, which is the honest representation of an additional entry the operator keys by hand.</item>
/// </list>
///
/// <para>🔴 <b>MEASUREMENT 2026-10-18 — "ON ITEM RATE" AND "ON VAT RATE" ARE NOT TALLYPRIME CLAIMS, AND THE
/// CENSUS SAID THEY WERE.</b> Census row 2.6 justified its <c>PARTIAL</c> grade with <i>"the vendor also ships
/// On Item Rate (its own help page) and On VAT Rate"</i>, and concluded from that that a DISCOUNT <i>"cannot be
/// expressed at all"</i> here. Every page naming either value was re-fetched by content. The result does not
/// support the claim:</para>
/// <list type="number">
/// <item><b>"On Item Rate" on a TallyPrime page is a DIFFERENT FIELD.</b> The one TallyPrime page that carries
/// the phrase is <c>help.tallysolutions.com/tally-prime/india-vat/vat-faq/</c>, where it is an option of
/// <i>Calculation type</i> in the <b>stock item master's</b> VAT details (<i>"Set the option Calculation type to
/// On Item Rate"</i>). That page does not mention voucher classes at all. The page that does put "On Item Rate"
/// in a voucher class is <c>help.tallysolutions.com/article/Tally.ERP9/…/On_Item_Rate_Calculation_Method.htm</c>
/// — <b>Tally.ERP 9</b>, which is not ground truth for this clone.</item>
/// <item><b>"On VAT Rate" surfaces only on <c>te9rel*</c> Tally.ERP 9 VAT pages.</b> No TallyPrime page names
/// it. (The ERP-9-only <i>"As User Defined Value"</i> is in the same position.)</item>
/// <item><b>No TallyPrime page enumerates "Type of Calculation" at all.</b> <c>voucher-types-tally/</c> — the
/// product's own Voucher Class page — does not contain the phrase. So there is no retrievable TallyPrime list
/// to be short of, which is why the struck sentence above could not be sourced either.</item>
/// <item><b>TallyPrime does not express a discount through this field.</b> The vendor documents discount two
/// ways, neither of them a class calculation type: a per-item <i>Discount</i> column behind
/// <c>F12 &gt; Provide Cash/Trade Discount</c> against a ledger whose <i>Type of Ledger</i> is <i>Discount</i>,
/// and a whole-invoice discount ledger keyed as a negative amount. The percentage itself is a <b>ledger</b>
/// property, unlocked by the <b>group</b> flag <i>"Used for calculation (for example: taxes, discounts)"</i> —
/// <c>help.tallysolutions.com/groups-in-tallyprime/</c>: <i>"To use percentage-based calculations for ledgers
/// under this Group, enable"</i>. That is census row 2.2's field, not this enum's.</item>
/// </list>
/// <para><b>So the three members below are not a truncation of a known vendor list; they are every value a
/// TallyPrime page names.</b> Adding <c>OnItemRate</c> or <c>OnVatRate</c> here would import ERP-9 semantics into
/// a surface that posts money without prompting — exactly what the paragraph above forbids. They stay out.</para>
/// </remarks>
public enum VoucherClassCalculationType
{
    /// <summary>Vendor <i>"Not Applicable"</i> — the class names the ledger on the voucher but computes nothing
    /// for it. Posts NO leg (see <see cref="Apex.Ledger.Services.VoucherClassPosting"/>): a zero-amount leg would
    /// be indistinguishable from a computed zero, and the vendor's <i>Remove if Zero</i> exists precisely so a
    /// nil line does not appear.</summary>
    NotApplicable = 0,

    /// <summary>Vendor <i>"Based on Quantity"</i> — the amount is <c>Value Basis × total base-unit quantity</c> of
    /// the voucher's item lines, i.e. the Value Basis is a rate PER UNIT. Used for a per-unit duty or a per-unit
    /// freight.</summary>
    BasedOnQuantity = 1,

    /// <summary>Vendor <i>"As Total Amount Rounding"</i> — the amount is the DIFFERENCE between the voucher total
    /// and that total put through the entry's <see cref="VoucherClassRoundingMethod"/> and rounding limit, so the
    /// leg is exactly what makes the rounded total balance. The Value Basis is not used. 🔴 This is the one member
    /// that can post a NEGATIVE amount, and it must: a downward rounding removes money from the total, and the
    /// balancing leg is the negative of what was removed.</summary>
    AsTotalAmountRounding = 2,
}
