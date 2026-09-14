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

    /// <summary>
    /// The MILLIONS alternative to <see cref="Culture"/> — a flat group size of 3, so ₹1000000 renders
    /// <c>1,000,000.00</c> rather than <c>10,00,000.00</c>.
    ///
    /// <para>🔴 <b>Sourced (R7, ruling 14) — the vendor's own documentation, and the two vendor options behind
    /// this one rule are named SEPARATELY because they are not the same option.</b> An earlier draft of this
    /// comment spliced them into a single sentence attributed to "help.tallysolutions.com — the F1 Settings /
    /// Date and Number Format documentation", which is a bare domain that resolves to no page and to which
    /// neither of the quoted phrases belongs. Both citations below were re-fetched first-hand and resolve by
    /// content:</para>
    /// <list type="number">
    /// <item><b>The application setting this build ships</b>, at <b>F1 (Help) &gt; Settings &gt; Country &gt;
    /// Date and Number Format</b>, captioned <i>"Show Quantity and Number in millions"</i> = Yes —
    /// <c>help.tallysolutions.com/stock-items-faq/</c>. That page gives the path and the caption; its stated
    /// effect is that quantities and numbers render in the millions format throughout the product.</item>
    /// <item><b>A DIFFERENT vendor option, quoted only for what millions grouping looks like and how wide it
    /// reaches</b>: <i>"Show amount in millions?"</i> on the company's additional base-currency details (Alt+K
    /// Company &gt; Alter &gt; F12 &gt; Provide Additional Base Currency details) —
    /// <c>help.tallysolutions.com/cheque-payments-set-up/</c>, which is where <i>"1,000,000 instead of
    /// 10,00,000"</i> and the "in the book as well as" on printed cheques wording actually come from.</item>
    /// </list>
    /// <para><b>Why one rule serves both.</b> Every vendor route to millions grouping is a single setting applied
    /// to a whole rendering surface — never per report or per document — and the second citation shows the vendor
    /// carrying it onto printed cheques as well as the book. This class is the one grouping rule the report
    /// cells, the tax invoice, the POS receipt, the printed voucher, the certificates AND <c>ChequePdf</c> all
    /// format through, so resolving the switch HERE reaches exactly those surfaces and nothing is gated twice.
    /// <b>The divergence:</b> this build exposes the switch on the F1 page only — there is no per-currency copy
    /// of it — so the two vendor options collapse into one here.</para>
    ///
    /// <para><b>Frozen for the same reason <see cref="Culture"/> is</b> — a writable clone published as a static
    /// would let any assembly, or any earlier-running test in the process, rewrite the grouping for every later
    /// render. What the setting switches is WHICH frozen culture is handed out (see <see cref="ActiveCulture"/>),
    /// never the contents of either one.</para>
    /// </summary>
    public static readonly CultureInfo MillionsCulture = CreateMillionsCulture();

    private static CultureInfo CreateMillionsCulture()
    {
        var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        ci.NumberFormat.CurrencyGroupSizes = new[] { 3 };
        ci.NumberFormat.NumberGroupSizes = new[] { 3 };
        ci.NumberFormat.NumberGroupSeparator = ",";
        ci.NumberFormat.NumberDecimalSeparator = ".";
        return CultureInfo.ReadOnly(ci); // frozen, exactly like the Indian one above
    }

    /// <summary>
    /// The culture the app is CURRENTLY grouping with — <see cref="Culture"/> by default, and
    /// <see cref="MillionsCulture"/> only while <see cref="AmountDisplay.Grouping"/> has been switched to
    /// <see cref="AmountDigitGrouping.Millions"/> from the F1 (Help) &gt; Settings &gt; Country page.
    ///
    /// <para><b>Read this, not <see cref="Culture"/>, from a display path.</b> <see cref="Culture"/> remains what
    /// its name says — the Indian rule, unconditionally — and the drift-lock tests still hold it to {3,2}. A call
    /// site that hard-codes it is not wrong, it is simply DEAF to the setting, which is how a knob quietly
    /// becomes cosmetic.</para>
    /// </summary>
    public static CultureInfo ActiveCulture =>
        AmountDisplay.Grouping == AmountDigitGrouping.Millions ? MillionsCulture : Culture;

    /// <summary>Two-decimal rupees, grouped the way the app is currently set — <c>1,00,000.00</c> by default,
    /// <c>100,000.00</c> under Millions. Zero renders <c>0.00</c>.</summary>
    public static string Amount(decimal value) => value.ToString("#,##0.00", ActiveCulture);

    /// <summary>Two-decimal rupees for a <see cref="Money"/>, grouped per <see cref="ActiveCulture"/>.</summary>
    public static string Amount(Money money) => Amount(money.Amount);

    /// <summary>
    /// A quantity with up to six optional decimals — <c>1,00,000.5</c>. Quantities share the grouping rule with
    /// money (the vendor's own caption is "Show Quantity <b>and</b> Number in millions"), so they follow
    /// <see cref="ActiveCulture"/> too; only the decimal handling differs.
    /// </summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.######", ActiveCulture);
}

/// <summary>Which digit grouping the application renders money and quantities with.</summary>
public enum AmountDigitGrouping
{
    /// <summary>The Indian lakh/crore system, 3;2;2 — <c>10,00,000.00</c>. <b>The default</b>, because the
    /// vendor's millions switch is an explicit opt-in whose "No" answer is the lakh rendering.</summary>
    Indian = 0,

    /// <summary>A flat group of 3 — <c>1,000,000.00</c>. The vendor's "Show Quantity and Number in millions" = Yes.</summary>
    Millions = 1,
}

/// <summary>
/// The application-wide amount-display setting behind <b>F1 (Help) &gt; Settings &gt; Country &gt; Date and
/// Number Format</b>. It is deliberately APPLICATION state, not company state: the vendor puts it on the F1
/// Settings popup, which is not scoped to the open company, and the same book renders in whichever grouping the
/// installation is set to.
///
/// <para>🔴 <b>It does NOT persist across a restart, and that is a stated limit rather than an oversight.</b>
/// Persisting it would need a store this slice was given no schema budget for, so the setting lives for the
/// session — the same in-memory precedent the F11 company-feature flags ship on. Anything that claims otherwise
/// on the settings page would be a false caption.</para>
///
/// <para><b>Why a settable static and not an injected service.</b> The nine formatting call sites this reaches
/// are static helpers on three assemblies (<c>IndianFormat</c>, the four PDF writers, <c>ChequePdf</c>,
/// <c>CertificatePdfSupport</c>); threading a service through all of them would be a far larger change than the
/// row is, and would still end in one process-wide value. The risk a static carries is ORDER-DEPENDENT TEST
/// CONTAMINATION — a test that switches to Millions and never switches back silently re-groups every later test
/// in the same assembly — so the only safe way to change it in a test is <see cref="Scoped"/>, which restores
/// the prior value on dispose even if the test throws.</para>
///
/// <para>🔴 <b>AND <see cref="Scoped"/> IS NOT ENOUGH ON ITS OWN — the test must also live in an assembly that
/// does not run its classes in parallel.</b> <c>Apex.Ledger.Tests</c> and <c>Apex.Ledger.Io.Tests</c> carry no
/// <c>DisableTestParallelization</c>, so a scope opened there would re-group every amount that a CONCURRENT
/// class happened to format — silently, and only sometimes. <c>Apex.Desktop.Tests</c> does disable it
/// (<c>AssemblyInfo.cs</c>), which is why every test that exercises the Millions rendering is written there.</para>
/// </summary>
public static class AmountDisplay
{
    /// <summary>The grouping every display path formats through. Defaults to <see cref="AmountDigitGrouping.Indian"/>.</summary>
    public static AmountDigitGrouping Grouping { get; set; } = AmountDigitGrouping.Indian;

    /// <summary>Restores the shipped default. Called by the settings page's "reset" path and by tests.</summary>
    public static void ResetToDefault() => Grouping = AmountDigitGrouping.Indian;

    /// <summary>
    /// Sets <see cref="Grouping"/> for the lifetime of the returned token and restores the PREVIOUS value on
    /// dispose. The only supported way for a test to exercise the Millions rendering.
    /// </summary>
    public static IDisposable Scoped(AmountDigitGrouping grouping)
    {
        var restore = new GroupingScope(Grouping);
        Grouping = grouping;
        return restore;
    }

    private sealed class GroupingScope : IDisposable
    {
        private readonly AmountDigitGrouping _previous;
        private bool _disposed;
        internal GroupingScope(AmountDigitGrouping previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;   // a double dispose must not resurrect a value a later scope replaced
            _disposed = true;
            Grouping = _previous;
        }
    }
}
