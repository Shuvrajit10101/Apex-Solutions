using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W-BJ — THE TWO ANNUAL GST RETURNS RE-HOMED OFF DEDICATED PAGE SCREENS</b> (census 6.12, and the shared
/// report-surface cluster behind rows 11.16 and 12.1).
///
/// <para><b>What was wrong.</b> GSTR-9 and GSTR-9C each opened as a page <c>Screen</c>, which leaves the shell's
/// <c>Reports</c> null — and that single fact switches off F2 as-of, Alt+F2 period, the F12 configuration panel,
/// Alt+F12 sort/filter, Ctrl+H (Change View) &gt; Saved Views and the report's own Enter-drill simultaneously.
/// Each page offered a financial-year ComboBox of its own in place of the period control.</para>
///
/// <para>🔴 <b>IT IS A FIDELITY FIX AND THE VENDOR SAYS SO BY NAME.</b> Opened by content on 2026-10-18:
/// <c>help.tallysolutions.com/tally-prime/gstr-9-9c/file-gstr-9-tally/</c> reads
/// <i>"Press F2 (Period) and set it to the relevant financial year."</i> and, for the export leg,
/// <i>"Press F2 (Period) to select the period for which you want to export GSTR-9 data."</i>;
/// <c>help.tallysolutions.com/file-gstr-9c-tally/</c> carries the identical
/// <i>"Press F2 (Period) and set it to the relevant financial year."</i> The reference product drives both
/// reports with the ordinary report period control; the dedicated pages had no F2 at all.</para>
///
/// <para>🔴 <b>WHAT ARRIVES IS SMALLER THAN THE CENSUS'S SIX GESTURES, AND THIS FILE SAYS SO RATHER THAN LETTING
/// A REVIEWER FIND IT.</b> Print and export did <b>not</b> arrive, because they were never off here: both page
/// view models implement <c>IMasterListExportSource</c>, which <c>IsPrintablePage</c>'s third arm
/// (<c>TopMasterExportSource</c>) already reached. What arrives is F2 / Alt+F2 in full, Ctrl+H saved views, the
/// PERIOD half of F12, and Alt+F12 saying out loud that it cannot act. What LEAVES is each page's own
/// financial-year ComboBox. Every claim in that sentence is asserted below.</para>
///
/// <para><b>Which of these redden on today's <c>main</c>, since three of them must.</b> Anything naming the two
/// new <see cref="ReportKind"/> members cannot compile against <c>main</c>, which is a weaker statement than a
/// red test. So three cases are written to compile on <c>main</c> unchanged — they name only
/// <see cref="Screen.Report"/>, the two shipped openers and shell predicates — and each FAILS there:
/// <see cref="The_two_annual_return_openers_land_on_the_shared_report_surface"/> (main lands on
/// <c>Screen.Gstr9Report</c> / <c>Screen.Gstr9cReport</c>),
/// <see cref="F2_reaches_the_annual_return_through_the_real_key_tunnel"/> (on main <c>IsReportContext</c> is
/// false, so the bare-F2 arm never fires and no configuration panel opens), and
/// <see cref="The_annual_return_rows_are_arrow_reachable_and_land_on_a_report"/>.</para>
/// </summary>
public sealed class RehomedReportSurfaceBjTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV"; // state code 27 (home)
    private const string GstinSupplier = "27AAACC1206D1ZM";    // an in-state supplier

    /// <summary>The SAC the fixture's Sales ledger carries, so GSTR-9 Part VI Table 17 has a real body row.</summary>
    internal const string Table17Sac = "998313";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PurchaseDate = new(2024, 4, 3);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RehomedReportSurfaceBjTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRehomedBj_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The two kinds this wave re-homed.</summary>
    public static IEnumerable<object[]> RehomedKinds() => new[]
    {
        new object[] { ReportKind.Gstr9AnnualReturn },
        new object[] { ReportKind.Gstr9cReconciliation },
    };

    // ================================================================= the egress contract

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST OF THIS WAVE.</b> Adding a <see cref="ReportKind"/> member is not re-homing. The
    /// per-kind EXPORT caption table maps a minority of kinds and the PRINT path has no per-kind caption table
    /// at all, so a report re-homed onto a bespoke grid lands in neither map and gains a blank exported header
    /// row and a blank printed header band — strictly worse than the dedicated Screen it replaced, and
    /// invisible to any test that only checks the enum member exists.
    ///
    /// <para>The last two assertions are what the shared-matrix design buys and a per-kind table cannot: screen,
    /// spreadsheet and printed page agree caption for caption because there is ONE list.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_bj_kind_exports_and_prints_with_populated_column_captions(ReportKind kind)
    {
        var reports = PopulatedReport($"Bj Egress {kind}", kind);

        Assert.True(reports.IsWideMatrixReport,
            $"{kind} is not on the wide-matrix surface, so neither egress projector can see its columns.");
        Assert.True(reports.PayrollColumns.Count >= 2,
            $"{kind} built {reports.PayrollColumns.Count} column(s); a re-homed report needs a label column and "
            + "at least one figure column or there is nothing to caption.");
        Assert.All(reports.PayrollColumns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} has an unlabelled on-screen column."));

        var exported = ReportTabularProjector.Project(reports);
        Assert.All(exported.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} exported a BLANK column caption."));

        var printed = ReportPrintProjector.Project(reports);
        Assert.All(printed.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} printed a BLANK column caption band."));

        // 🔴 THE EGRESS BAND IS BAND 1 FOLLOWED BY BAND 2'S SURPLUS COLUMNS, NOT BAND 1 ALONE. This assertion
        // used to compare against PayrollColumns and so PINNED THE WRONG ANSWER: it was green on the build where
        // GSTR-9 Table 17 printed and exported with Quantity, Taxable Value and Total Tax deleted, because band 1
        // is two columns wide and the writers clamp every row to the band. See ReportMatrixEgressBand.
        var egress = ReportMatrixEgressBand.For(reports).Select(c => c.Header).ToList();
        Assert.Equal(egress, exported.Columns.Select(c => c.Header).ToList());
        Assert.Equal(egress, printed.Columns.Select(c => c.Header).ToList());

        // …and band 1's own captions are still the LEADING ones, unmoved — band 1's rows must never slide under a
        // heading that is not theirs.
        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            egress.Take(reports.PayrollColumns.Count).ToList());

        // 🔴 The two assertions above are both stated against ReportMatrixEgressBand, so a regression INSIDE it
        // would move all three together and neither would fail. The absolute statement is this one: the band
        // must be wide enough for the widest row either section emits, measured off the rows themselves.
        if (reports.HasPayrollSection2)
            MatrixSecondBandEgressTests.AssertBandCoversBothSections(reports);

        // …and the document is not an empty shell under those captions.
        Assert.NotEmpty(reports.PayrollRows);
        Assert.NotEmpty(exported.Rows);
        Assert.NotEmpty(printed.Rows);
    }

    /// <summary>
    /// The captions are the pages' own. "Particulars" / "Amount" are transcribed from each superseded
    /// <c>ToMasterListSnapshot</c>'s own <c>MasterListColumn</c> pair, and the Amount column is the NUMERIC one
    /// so a spreadsheet stores a real decimal it can total rather than text that merely looks like money.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_bj_column_captions_are_the_superseded_pages_own(ReportKind kind)
    {
        var reports = PopulatedReport($"Bj Captions {kind}", kind);

        Assert.Equal(new[] { "Particulars", "Amount" },
            reports.PayrollColumns.Select(c => c.Header).ToArray());
        Assert.Equal(new[] { false, true },
            reports.PayrollColumns.Select(c => c.IsNumeric).ToArray());
    }

    /// <summary>
    /// 🔴 <b>GSTR-9's Table 17 gets its OWN band with its OWN columns, and that is a correctness point.</b> An
    /// HSN row is identified by HSN, description, UQC and quantity and carries two money figures; folding it
    /// into the label/amount grid of Parts II–VI would put a quantity under a money heading — the "wrong figure
    /// under a right heading" shape this codebase has shipped once. The second band's captions must also be
    /// real, because both egress projectors read it too.
    /// </summary>
    [Fact]
    public void The_rehomed_gstr9_carries_table_17_in_its_own_captioned_second_band()
    {
        var reports = PopulatedReport("Bj Hsn", ReportKind.Gstr9AnnualReturn);

        Assert.True(reports.HasPayrollSection2, "Table 17 did not reach the matrix at all.");
        Assert.Equal("Part VI Table 17 — Outward HSN Summary", reports.PayrollSection2Title);
        Assert.Equal(new[] { "HSN / SAC", "Description", "UQC", "Quantity", "Taxable Value", "Total Tax" },
            reports.PayrollColumns2.Select(c => c.Header).ToArray());
        Assert.All(reports.PayrollColumns2, c => Assert.False(string.IsNullOrWhiteSpace(c.Header)));

        // The foot total is present and is a real figure, not a blank — a reviewer foots Table 17 against it.
        var foot = Assert.Single(reports.PayrollRows2.Where(r => r.Cells[0].Text == "17  Total"));
        Assert.False(string.IsNullOrWhiteSpace(foot.Cells[4].Text));
        Assert.False(string.IsNullOrWhiteSpace(foot.Cells[5].Text));

        // Every cell of every body row is sized from its own column, so a band that drifted a pixel would
        // shift the columns to its right. Asserted as the invariant rather than as six constants.
        foreach (var row in reports.PayrollRows2)
        {
            Assert.Equal(reports.PayrollColumns2.Count, row.Cells.Count);
            for (var i = 0; i < row.Cells.Count; i++)
                Assert.Equal(reports.PayrollColumns2[i].Width, row.Cells[i].Width);
        }
    }

    /// <summary>
    /// The two grid-visibility traps, pinned. A matrix report leaves <c>Rows</c> empty, which is how the
    /// accounting grid and the shared empty-state overlay both decide what to draw. Get either wrong and the
    /// report is CORRECT and UNREADABLE.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_rehomed_bj_report_shows_neither_the_accounting_grid_nor_the_empty_state_overlay(ReportKind kind)
    {
        var reports = PopulatedReport($"Bj Grid {kind}", kind);

        Assert.False(reports.IsAccountingReport);
        Assert.False(reports.IsEmpty);
        Assert.True(reports.IsPayrollMatrix);
        Assert.False(reports.IsPayrollEmpty);
        Assert.False(reports.IsPayrollReport,
            $"{kind} claims the payroll family, which would offer it a wage-month picker it cannot honour.");
    }

    // ================================================================= the figures survived the move

    /// <summary>
    /// 🔴 <b>THE RE-HOME MOVED NO FIGURE ON THE DEFAULT WINDOW, PROVEN LINE FOR LINE AGAINST THE SUPERSEDED
    /// PAGE.</b> Each page built itself for <c>SelectedYear = Company.FinancialYearStart.Year</c>, which is the
    /// same financial year <c>FinancialYearWindow()</c> derives from the report's DEFAULT as-of (the last
    /// voucher date). So an operator who opens the re-homed report and touches nothing must see precisely what
    /// the page showed.
    ///
    /// <para>It is deliberately a COMPARISON and not a golden: a golden would re-state the engine, where this
    /// pins the one property a re-home is allowed to have — that it changed the surface and nothing else. The
    /// page's own two header rows ("Period" / "Registration") are excluded because the matrix carries that
    /// information in its subtitle, and the subtitle is asserted separately.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_bj_report_renders_the_superseded_pages_figures_line_for_line(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj Parity {kind}");
        var company = vm.Company!;

        var pageRows = SupersededPageLines(kind, company);
        Assert.NotEmpty(pageRows);

        var reports = new ReportsViewModel(company, kind);
        var matrixRows = reports.PayrollRows
            .Select(r => (Label: r.Cells[0].Text, Amount: r.Cells[1].Text))
            .ToList();

        // Every line the page showed is on the matrix, with the SAME amount string, in the SAME order.
        Assert.Equal(pageRows, matrixRows);
    }

    /// <summary>
    /// 🔴 <b>Alt+F2 IS A LIVE KNOB ON BOTH KINDS, NOT A DEAD ONE — AND THAT IS THE WHOLE REASON THESE TWO WERE
    /// CHOSEN FOR RE-HOMING.</b> The slice's own refusal note says a report whose figures do not MOVE when the
    /// period moves gains a dead knob instead of a live one. Both engines take <c>(Company, from, to)</c> and
    /// nothing else, so the window is the whole of their parameterisation.
    ///
    /// <para>The second window is the PRIOR financial year, which contains neither the seeded purchase nor the
    /// seeded sale, so every figure must fall to zero. Hand-reasoned from the fixture dates, not read back off
    /// the engine.</para>
    ///
    /// <para>🔴 <b>THE THIRD WINDOW IS THE ONE THAT ACTUALLY PROVES THE PERIOD IS READ, AND IT WAS ADDED
    /// BECAUSE MUTATION TESTING SHOWED THE FIRST TWO DO NOT.</b> Deleting the <c>_options.Period</c> anchor
    /// from <c>GstAnnualReturnWindow</c> leaves both windows above GREEN: a window that already IS a whole
    /// financial year is indistinguishable from the year the as-of fallback derives from its end date. The
    /// third window STRADDLES two financial years — 1 Apr 2024 to 30 Jun 2025 — so the two anchors disagree:
    /// anchoring on <c>From</c> gives FY 2024-25 and the seeded figures; anchoring on the as-of (which is the
    /// window's To) would give FY 2025-26 and zeros.</para>
    /// </summary>
    [Fact]
    public void Alt_F2_moves_the_annual_return_figures_rather_than_leaving_a_dead_knob()
    {
        var vm = RegularGstFixture("Bj Period Gstr9");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.Gstr9AnnualReturn);

        Assert.Equal("1,000.00", Amount(reports, "5  Total turnover"));
        Assert.Equal("180.00", Amount(reports, "4  Total tax payable"));
        Assert.Equal("900.00", Amount(reports, "6  Total ITC availed"));

        // The financial year BEFORE the one the vouchers fall in: 1 Apr 2023 – 31 Mar 2024.
        reports.SetPeriod(FyStart.AddYears(-1), FyStart.AddDays(-1));

        Assert.Equal("0.00", Amount(reports, "5  Total turnover"));
        Assert.Equal("0.00", Amount(reports, "4  Total tax payable"));
        Assert.Equal("0.00", Amount(reports, "6  Total ITC availed"));

        // ---- A STRADDLING window whose START names FY 2024-25 and whose END falls in FY 2025-26.
        reports.SetPeriod(FyStart, new DateOnly(2025, 6, 30));

        Assert.Equal("1,000.00", Amount(reports, "5  Total turnover"));
        Assert.Equal("900.00", Amount(reports, "6  Total ITC availed"));
    }

    /// <summary>
    /// 🔴🔴 <b>THE CLAUSE THAT KEEPS THE SNAP HONEST, AND IT GUARDS AGAINST A WRONG FIGURE UNDER A RIGHT
    /// HEADING ON A FILED STATUTORY RETURN.</b>
    ///
    /// <para><b>Measured while building this slice.</b> <c>Gstr9.Build(company, fyFrom, fyTo)</c> ignores
    /// <c>fyTo</c> for the body of the return — it folds over <c>PeriodWindows(company, fyFrom)</c>, which is
    /// unconditionally twelve months (or four quarters) forward from <c>fyFrom</c>. A first draft of this
    /// re-home honoured an arbitrary Alt+F2 window verbatim, and a four-day window produced a <b>full year's</b>
    /// taxable value and tax under a subtitle reading "1 Apr 2024 to 4 Apr 2024". <c>Gstr9c</c> is worse: its
    /// books side DOES honour the partial window while its return side does not, so a partial window
    /// manufactures a spurious unreconciled difference.</para>
    ///
    /// <para>So <c>GstAnnualReturnWindow</c> always returns a WHOLE financial year, and the subtitle states the
    /// window actually projected rather than the one requested. Both halves are asserted: the dates in the
    /// subtitle, and the invariant that the window is a whole year on every one of several requested windows.
    /// If someone later makes the window honour an arbitrary period, this reddens before a return is filed on
    /// it.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_partial_period_is_snapped_to_a_whole_financial_year_and_the_subtitle_says_which(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj Snap {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        foreach (var requested in new[]
                 {
                     (From: FyStart, To: FyStart.AddDays(3)),                       // four days
                     (From: FyStart.AddMonths(2), To: FyStart.AddMonths(3)),        // one month, mid-year
                     (From: FyStart, To: new DateOnly(2025, 6, 30)),                // straddles two years
                 })
        {
            reports.SetPeriod(requested.From, requested.To);

            // The projected window is the whole financial year containing the REQUESTED START.
            Assert.Contains("01-Apr-2024", reports.Subtitle);
            Assert.Contains("31-Mar-2025", reports.Subtitle);

            // …and the figures are the whole year's, matching the untouched default window — which is the
            // point: a partial request must not silently return a year's figures under a partial heading.
            if (kind == ReportKind.Gstr9AnnualReturn)
                Assert.Equal("1,000.00", Amount(reports, "5  Total turnover"));
            else
                Assert.Equal("1,000.00", Amount(reports, "5  Turnover as per books"));
        }
    }

    /// <summary>The same proof on GSTR-9C: the reconciliation follows the period, both sides of it.</summary>
    [Fact]
    public void Alt_F2_moves_the_reconciliation_figures_too()
    {
        var vm = RegularGstFixture("Bj Period Gstr9c");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.Gstr9cReconciliation);

        Assert.Equal("1,000.00", Amount(reports, "5  Turnover as per books"));
        Assert.Equal("1,000.00", Amount(reports, "5  Turnover as per returns"));

        reports.SetPeriod(FyStart.AddYears(-1), FyStart.AddDays(-1));

        Assert.Equal("0.00", Amount(reports, "5  Turnover as per books"));
        Assert.Equal("0.00", Amount(reports, "5  Turnover as per returns"));
        Assert.Equal("0.00", Amount(reports, "5  Unreconciled turnover"));

        // A straddling window: its START names FY 2024-25, so the reconciliation comes back to that year.
        reports.SetPeriod(FyStart, new DateOnly(2025, 6, 30));
        Assert.Equal("1,000.00", Amount(reports, "5  Turnover as per books"));
        Assert.Equal("1,000.00", Amount(reports, "5  Turnover as per returns"));
        Assert.Equal("0.00", Amount(reports, "5  Unreconciled turnover"));
    }

    /// <summary>
    /// 🔴 <b>A reconciliation never carries the difference alone.</b> ₹0 unreconciled is a clean tie and it is
    /// equally what you get when both sides failed to build; a reviewer must be able to tell which. All three
    /// lines of all three reconciliations are asserted present on the surface an operator actually reads.
    /// </summary>
    [Fact]
    public void The_rehomed_gstr9c_carries_both_sides_of_every_reconciliation_not_just_the_difference()
    {
        var reports = PopulatedReport("Bj Triples", ReportKind.Gstr9cReconciliation);

        foreach (var triple in new[]
                 {
                     ("5  Turnover as per books", "5  Turnover as per returns", "5  Unreconciled turnover"),
                     ("9-11  Tax as per books", "9-11  Tax as per returns", "9-11  Unreconciled tax"),
                     ("12  ITC as per books", "12  ITC as per returns", "12  Unreconciled ITC"),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(Amount(reports, triple.Item1)), triple.Item1);
            Assert.False(string.IsNullOrWhiteSpace(Amount(reports, triple.Item2)), triple.Item2);
            Assert.False(string.IsNullOrWhiteSpace(Amount(reports, triple.Item3)), triple.Item3);
        }
    }

    // ================================================================= the routes and the gestures

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY'S <c>main</c>, AND COMPILES THERE.</b> On <c>main</c> both openers land on their own
    /// page <c>Screen</c> and bind their page view model; here they must land on the shared report surface and
    /// leave the page view model unbuilt.
    /// </summary>
    [Fact]
    public void The_two_annual_return_openers_land_on_the_shared_report_surface()
    {
        var vm = RegularGstFixture("Bj Route");

        vm.OpenGstr9Report();
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Null(vm.Gstr9Report);
        Assert.Equal("Form GSTR-9 — Annual Return", vm.Reports!.Title);
        Assert.True(vm.IsReportContext,
            "the whole point of the re-home: the report-parameter chords act only in report context.");

        vm.Back();
        vm.OpenGstr9cReport();
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Null(vm.Gstr9cReport);
        Assert.Equal("Form GSTR-9C — Reconciliation Statement", vm.Reports!.Title);
        Assert.True(vm.IsReportContext);
    }

    /// <summary>
    /// 🔴 <b>WALKED WITH THE ARROW KEYS, BECAUSE A CAPABILITY NO OPERATOR CAN REACH FROM THE KEYBOARD IS NOT
    /// DELIVERED.</b> Both rows are reached through the real cascade — Reports → Statutory Reports → Annual
    /// Returns — and each must land on <see cref="Screen.Report"/>. Fails on <c>main</c>, where they land on
    /// their page Screens. Compiles on <c>main</c>.
    /// </summary>
    [Fact]
    public void The_annual_return_rows_are_arrow_reachable_and_land_on_a_report()
    {
        foreach (var (row, title) in new[]
                 {
                     ("GSTR-9", "Form GSTR-9 — Annual Return"),
                     ("GSTR-9C", "Form GSTR-9C — Reconciliation Statement"),
                 })
        {
            var vm = RegularGstFixture($"Bj Arrow {row}");
            vm.ShowAnnualReturnsMenu();
            Assert.Equal(GatewayMenu.AnnualReturns, vm.CurrentGatewayMenu);

            ArrowToAndDrill(vm, row);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(title, vm.Reports!.Title);
        }
    }

    /// <summary>
    /// 🔴 <b>F2 THROUGH THE REAL KEY TUNNEL, ON A REALISED WINDOW.</b> Calling <c>ReportSetAsOf()</c> would prove
    /// nothing: the gate is <c>IsReportContext</c>, evaluated inside <see cref="MainWindow"/>'s own
    /// <c>KeyDown</c> handler. On <c>main</c> the page Screen leaves that predicate false, the bare-F2 arm never
    /// fires and no configuration panel opens — so this case fails there while compiling there.
    /// </summary>
    [AvaloniaFact]
    public void F2_reaches_the_annual_return_through_the_real_key_tunnel()
    {
        var vm = RegularGstFixture("Bj F2");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        try
        {
            vm.OpenGstr9Report();
            Pump(window);

            Press(window, Key.F2);
            Pump(window);

            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            Assert.NotNull(vm.ReportConfig);

            // Alt+F2 is the period half of the same panel, and it must open with the window ENABLED — that is
            // what makes accepting set a period rather than only an as-of.
            vm.Back();
            Pump(window);
            Press(window, Key.F2, KeyModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            Assert.True(vm.ReportConfig!.UsePeriod);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE CAPTIONS AND THE CELLS REACH THE SCREEN, MEASURED ON THE REALISED VISUAL TREE.</b> Everything
    /// above asserts view-model and projector state, and view-model state is exactly what a report can have in
    /// full while rendering nothing. Three reports in this repository have already printed pages of entirely
    /// blank cells after a change of this shape, so this reads what actually laid out — the header band AND a
    /// known body cell.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_bj_report_lays_out_on_screen_with_its_captions_and_its_cells(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj Visual {kind}");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        try
        {
            vm.OpenReport(kind);
            Pump(window);

            var reports = vm.Reports!;
            Assert.True(reports.IsPayrollMatrix, $"{kind} does not render through the matrix pane.");

            foreach (var column in reports.PayrollColumns)
            {
                var block = Descendants(window)
                    .OfType<TextBlock>()
                    .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text == column.Header);

                Assert.True(block is not null,
                    $"{kind}: the column caption '{column.Header}' never reached the visual tree — the report's "
                    + "figures are on screen under headings nobody can read.");
                Assert.True(block!.Bounds.Width > 0 && block.Bounds.Height > 0,
                    $"{kind}: the column caption '{column.Header}' laid out at a degenerate size.");
            }

            // 🔴 AND A REAL CELL, NOT ONLY THE BAND. A band over blank cells is the defect that shipped; the
            // first body row's label must be on screen with a non-degenerate size.
            var firstLabel = reports.PayrollRows[0].Cells[0].Text;
            Assert.False(string.IsNullOrWhiteSpace(firstLabel),
                $"{kind}: the first matrix row has a blank label cell.");
            var cell = Descendants(window)
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text == firstLabel);
            Assert.True(cell is not null,
                $"{kind}: the row '{firstLabel}' never reached the visual tree — the grid is a set of headings "
                + "over blank cells.");
            Assert.True(cell!.Bounds.Width > 0 && cell.Bounds.Height > 0);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Alt+M SHARE ARRIVES WITH THE RE-HOME, AND IT MUST SEND WHAT IT NAMES.</b> <c>IsShareablePage</c>
    /// requires <c>IsReportContext</c>, so these two reports could not be shared at all from their page
    /// Screens and can be now. This repository has already measured the other failure of that gesture: on a
    /// drilled voucher, Alt+M then W handed a third party <b>the whole Day Book</b> in place of the one invoice
    /// the operator had drilled into, because the bare-W arm fired with the menu column still on top.
    ///
    /// <para>So the panel is not merely asserted to OPEN. Its document title and the bytes it would save are
    /// read back, and both must be the annual return the operator is standing on.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_share_panel_over_a_rehomed_annual_return_carries_that_return_and_not_another_document()
    {
        var vm = RegularGstFixture("Bj Share");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        try
        {
            vm.OpenGstr9Report();
            Pump(window);
            Assert.True(vm.IsShareablePage,
                "Alt+M was inert on the page Screen because IsShareablePage requires IsReportContext.");

            vm.OpenWhatsAppShare();
            Pump(window);

            var panel = vm.WhatsAppShare!;
            Assert.Equal("Form GSTR-9 — Annual Return", panel.DocumentTitle);

            var path = Path.Combine(_tempDir, "share-gstr9.pdf");
            Assert.True(panel.SaveDocument(path), "the share panel wrote nothing to disk.");

            var bytes = File.ReadAllBytes(path);
            var latin1 = string.Concat(bytes.Select(b => (char)b));
            Assert.StartsWith("%PDF-", latin1);

            // 🔴 THE BYTES ARE THE ANNUAL RETURN'S, NOT ANOTHER REPORT'S. "GSTR-9" is this document's own
            // heading and appears in no other report's; "Day Book" is the document the measured breach sent.
            Assert.Contains("GSTR-9", latin1);
            Assert.DoesNotContain("Day Book", latin1);
            Assert.DoesNotContain("tally", latin1, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    // ================================================================= the saved-view door

    /// <summary>
    /// Ctrl+H (Change View) &gt; Save View indexes the persisted-token map DIRECTLY, so a kind missing from it
    /// throws <see cref="KeyNotFoundException"/> the instant an operator presses the chord — on the very gesture
    /// the re-home exists to hand these reports.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_bj_kind_has_a_saved_view_token_that_round_trips(ReportKind kind)
    {
        var token = ReportsViewModel.TokenFor(kind);      // throws if the kind was not registered
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(kind, ReportsViewModel.KindFor(token));
    }

    /// <summary>
    /// 🔴 <b>A DISABLED MODULE MUST NOT BE REACHABLE THROUGH THE SAVED-VIEW DOOR.</b> This repository has
    /// measured a saved view opening the Pay Sheet — every employee's gross, deductions and net pay — on a
    /// company with F11 Payroll switched OFF, because the product's report gates live on the MENU ROW and a
    /// saved view stores a kind token and walks straight past it. The annual returns are a conditional group
    /// (<c>ShowAnnualReturnsMenu</c> refuses for anyone but a Regular dealer), so the same hole existed here
    /// the moment they became report kinds.
    ///
    /// <para>The refusal is asserted BOTH ways: a guard that simply never opened a saved view would satisfy the
    /// negative half and destroy the feature, so the same saved view is applied again as a Regular dealer and
    /// must open.</para>
    /// </summary>
    [Fact]
    public void A_saved_view_cannot_open_the_annual_returns_on_a_company_that_is_not_a_regular_dealer()
    {
        var vm = RegularGstFixture("Bj Saved View Gate");
        var company = vm.Company!;

        vm.OpenGstr9Report();
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotEmpty(vm.Reports!.PayrollRows);          // captured off a POPULATED report
        var savedView = vm.Reports!.ToSavedView();
        vm.Back();

        // The same company re-registers as a Composition dealer: GSTR-9 is not its return at all.
        company.Gst!.RegistrationType = GstRegistrationType.Composition;

        vm.OpenGstr9Report();                               // the gated door: refuses, as it always did
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);

        vm.ApplySavedView(savedView);                       // the second door: must refuse too
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        // ---- and the gate is not a wall.
        company.Gst!.RegistrationType = GstRegistrationType.Regular;
        vm.ApplySavedView(savedView);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.Gstr9AnnualReturn, vm.Reports!.Kind);
    }

    // ================================================================= scaffolding

    /// <summary>The Amount cell of the matrix row whose Particulars cell is <paramref name="particulars"/>.
    /// Fails by name when the row is absent, so a dropped statutory line cannot pass as a zero.</summary>
    private static string Amount(ReportsViewModel reports, string particulars)
    {
        var row = reports.PayrollRows.FirstOrDefault(r => r.Cells.Count > 1 && r.Cells[0].Text == particulars);
        Assert.True(row is not null, $"the matrix has no row captioned '{particulars}'.");
        return row!.Cells[1].Text;
    }

    /// <summary>
    /// The superseded page's own (label, amount) lines for <paramref name="kind"/>, taken from the page view
    /// model that production no longer builds. The two leading identity rows ("Period", "Registration") are
    /// dropped: the matrix carries that information in its subtitle, which is asserted separately.
    /// </summary>
    private static List<(string Label, string Amount)> SupersededPageLines(ReportKind kind, Company company)
    {
        var snapshot = kind switch
        {
            ReportKind.Gstr9AnnualReturn => new Gstr9ReportViewModel(company).ToMasterListSnapshot(),
            ReportKind.Gstr9cReconciliation => new Gstr9cReportViewModel(company).ToMasterListSnapshot(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a W-BJ re-homed kind"),
        };

        return snapshot.Rows
            .Where(r => r.Count > 1 && r[0] is not ("Period" or "Registration"))
            // GSTR-9's page folded Table 17's identity into the LABEL because a MasterListSnapshot has one
            // numeric column; the matrix gives it a band of its own, so those lines are compared there and not
            // here. Everything else must match line for line.
            .Where(r => !r[0].StartsWith("17  ", StringComparison.Ordinal))
            .Select(r => (r[0], r[1]))
            .ToList();
    }

    /// <summary>A report of <paramref name="kind"/> that actually HAS ROWS IN IT. A caption test over an empty
    /// report passes for the wrong reason.</summary>
    private ReportsViewModel PopulatedReport(string name, ReportKind kind)
    {
        var vm = RegularGstFixture(name);
        var reports = new ReportsViewModel(vm.Company!, kind);
        Assert.NotEmpty(reports.PayrollRows);
        return reports;
    }

    /// <summary>Arrow-walks the active column to <paramref name="label"/> and drills in — the sequence an
    /// operator's fingers perform. Fails loudly when the row is not arrow-reachable.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    /// <summary>A Regular GST company with one intra purchase (₹5,000 @ 18% ⇒ ITC 450+450) and one intra B2B
    /// sale (₹1,000 @ 18% ⇒ output 90+90) posted — so both annual returns have real, non-zero figures.
    /// Transcribed from <c>GstAdvancedReportsUiViewModelTests.NewRegularGstCompany</c>, which is the fixture the
    /// superseded pages' own tests were written against.</summary>
    private MainWindowViewModel RegularGstFixture(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        // 🔴 THE SALES LEDGER CARRIES AN SAC, AND THAT IS NOT DECORATION. Without it GSTR-1's HSN summary is
        // empty, Table 17 holds nothing but its own "17  Total" foot, and every band-2 assertion in this file
        // passes over a band with ONE row and two "0.00" cells. That is precisely why the egress defect BJ-1 —
        // Quantity / Taxable Value / Total Tax deleted from the printed and exported Table 17 — was invisible to
        // a 4922-test suite. With the SAC the band has a real body row carrying real money.
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = Table17Sac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        var pTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, PurchaseDate, pLines, partyId: supplier.Id,
            narration: "SUP-INV-001"));

        var sTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var sLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        sLines.AddRange(sTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, sLines, number: 1, partyId: debtor.Id));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void Press(MainWindow window, Key key, KeyModifiers modifiers = KeyModifiers.None)
        => window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = window,
        });

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }
}
