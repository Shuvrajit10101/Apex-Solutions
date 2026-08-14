using System;
using System.Globalization;

namespace Apex.Ledger;

/// <summary>
/// <b>The ONE home for rupee digit grouping</b> (drift lock D2): the Indian lakh/crore system, 3;2;2 —
/// ₹100000 renders <c>1,00,000.00</c>, ₹10000000 renders <c>1,00,00,000.00</c>.
///
/// <para><b>The divergence this replaces.</b> The same money printed two different ways <i>from the same
/// assembly</i>. <c>Apex.Desktop.Services.IndianFormat</c> and <c>Apex.Ledger.Io.CertificatePdfSupport</c>
/// grouped the Indian way, so a Form-16A / 27D certificate showed <c>1,00,000.00</c>; but
/// <c>InvoicePdf</c>, <c>PosReceiptPdf</c> and <c>VoucherPdf</c> formatted <c>"#,##0.00"</c> against
/// <see cref="CultureInfo.InvariantCulture"/>, whose group size is a flat 3, so the tax invoice, the POS
/// receipt and the printed voucher showed <c>100,000.00</c> for the very same amount.
/// <c>CertificatePdfSupport</c>'s own doc comment claimed it mirrored <c>InvoicePdf</c>; it did not.</para>
///
/// <para><b>Sourced (R7) — the corpus, not memory.</b> Tally Prime exposes digit style as a <b>currency-master
/// property</b>, not a per-report or per-document one: the Company-creation screen and the Currency Create
/// screen both carry the single field <b>"Show Amounts in Millions"</b> —
/// <i>"Show Amounts in Million — Done 'Yes' if you Show 1000000 INR in 1m INR"</i> and <i>"Show Amount in
/// Millions: For Show balance sheet and other reports in Millions example, Rs.1000000 is equal to 1
/// Million."</i> (<c>664311548-Tally-Prime-Book.pdf</c>, Company-creation field list at author page 9, and the
/// Currency-creation field list; read with <c>pdftotext -layout</c>, extracted lines 485–486 and 3756). A
/// worked example at extracted line 3775 fills the field in as <b>"Show Amounts in Million - No"</b>.
/// <para>Two things follow, and they settle this rule. (1) Millions grouping is an explicit <b>opt-in</b>, so
/// the <b>default is the Indian lakh/crore grouping</b> — that is exactly what the toggle toggles away from.
/// (2) The setting lives on the <b>currency</b>, so it applies uniformly to every rendering of that currency.
/// There is no mechanism in Tally by which an invoice would group differently from a certificate or a balance
/// sheet. A single grouping, applied everywhere, is therefore the faithful behaviour, and the Indian grouping
/// is the correct one.</para>
/// <para><b>Corroboration.</b> Across the ten corpus PDFs every rupee amount rendered in a Tally context uses
/// Indian grouping (e.g. <c>1,35,000</c>, <c>5,50,000</c>, <c>2,50,000</c>, <c>10,00,000</c>). The only
/// Western-grouped figures in the whole corpus are author prose arithmetic ("10% of 100,000") and one
/// hand-built worksheet that also prints GST rates as <c>0.18</c> rather than <c>18%</c> — demonstrably the
/// author's own typing, not Tally output.</para></para>
///
/// <para><b>Scope.</b> This is the grouping rule only. Callers keep their own blank-at-zero / sign / currency-
/// symbol conventions, which legitimately differ between a report grid cell and a PDF money column.</para>
/// </summary>
public static class IndianMoneyFormat
{
    /// <summary>
    /// An invariant-based culture carrying Indian digit grouping (3;2;2). Built from
    /// <see cref="CultureInfo.InvariantCulture"/> rather than looked up by name so the output is deterministic
    /// on every host regardless of the machine's installed locales or the OS's ICU version.
    ///
    /// <para><b>Frozen deliberately.</b> <see cref="CultureInfo.Clone"/> returns a culture whose
    /// <see cref="CultureInfo.NumberFormat"/> is WRITABLE — that is the only reason
    /// <see cref="CreateIndianCulture"/> can assign the group sizes at all. Publishing that writable object
    /// would make the one grouping rule a process-wide global: any assembly, or any earlier-running test in the
    /// same process, could execute <c>IndianMoneyFormat.Culture.NumberFormat.NumberGroupSizes = new[]{3}</c> and
    /// silently revert every invoice, receipt, voucher, certificate and report grid in the process to Western
    /// grouping — reintroducing the exact defect this rule exists to fix, and doing it as order-dependent
    /// cross-test contamination. Consolidating nine call sites onto one object is what makes that blast radius
    /// possible, so the object is wrapped in <see cref="CultureInfo.ReadOnly(CultureInfo)"/>: the field is
    /// readonly AND the culture behind it is immutable, and any mutation attempt throws at the offending site
    /// instead of corrupting every later render. The two private copies this replaced were unreachable this way.
    /// </para>
    /// </summary>
    public static readonly CultureInfo Culture = CreateIndianCulture();

    private static CultureInfo CreateIndianCulture()
    {
        var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        ci.NumberFormat.CurrencyGroupSizes = new[] { 3, 2 };
        ci.NumberFormat.NumberGroupSizes = new[] { 3, 2 };
        ci.NumberFormat.NumberGroupSeparator = ",";
        ci.NumberFormat.NumberDecimalSeparator = ".";
        return CultureInfo.ReadOnly(ci); // freeze: the one rule must not be rewritable from anywhere
    }

    /// <summary>Two-decimal rupees with Indian grouping — <c>1,00,000.00</c>. Zero renders <c>0.00</c>.</summary>
    public static string Amount(decimal value) => value.ToString("#,##0.00", Culture);

    /// <summary>Two-decimal rupees with Indian grouping for a <see cref="Money"/>.</summary>
    public static string Amount(Money money) => Amount(money.Amount);

    /// <summary>
    /// A quantity with Indian grouping and up to six optional decimals — <c>1,00,000.5</c>. Quantities share the
    /// grouping rule with money; only the decimal handling differs.
    /// </summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.######", Culture);
}
