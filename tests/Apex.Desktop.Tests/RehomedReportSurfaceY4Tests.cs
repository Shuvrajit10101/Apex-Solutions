using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W-Y4 — THREE MORE REPORTS RE-HOMED OFF DEDICATED PAGE SCREENS</b> (census 11.11 Interest Calculation,
/// plus the TDS and TCS Challan Reconciliations in area 6).
///
/// <para><b>What this file exists to stop.</b> Exactly what <see cref="RehomedReportSurfaceTests"/>'s W-V2 wave
/// existed to stop, one layer further on. A report on its own page <c>Screen</c> leaves the shell's report
/// context null, and that single fact switches off Ctrl+P print, Ctrl+E export, F2/Alt+F2 period, the F12
/// configuration panel, Alt+F12 sort/filter and Alt+K saved views simultaneously. The arithmetic behind all three
/// of these reports has had passing unit tests in <c>Apex.Ledger</c> for several phases; what they lacked was a
/// surface.</para>
///
/// <para>🔴 <b>WHAT ARRIVES IS THE SAME FOUR AND A HALF OF SIX, AND ONE THING LEAVES.</b> Ctrl+P, Ctrl+E,
/// F2/Alt+F2 and Alt+K arrive in full; F12 arrives with its PERIOD half, because its three display knobs
/// genuinely do not act on a matrix kind and are hidden; Alt+F12 arrives saying out loud that it cannot act.
/// Leaving: the two reconciliations' own Up/Down row highlight, which the matrix surface has no model for and
/// which had no verb behind it on either page. All of that is asserted below rather than claimed, because this
/// repository has twice withheld a branch for a claim stronger than its code.</para>
///
/// <para>🔴 <b>THE TRAP THESE TESTS ARE SHAPED AROUND.</b> Adding a <see cref="ReportKind"/> member is not
/// re-homing. The per-kind export caption table maps a minority of kinds and the print path has no per-kind
/// caption table at all, so a report re-homed onto a bespoke grid LANDS IN NEITHER PROJECTOR MAP: it gains a
/// blank exported header row and a blank printed header band, which is strictly worse than the dedicated Screen
/// it replaced and invisible to any test that only checks the enum member exists. All three kinds here render
/// through the shared dynamic matrix, whose live column band both egress projectors read — so the tests that
/// matter most are the ones that project to a spreadsheet and to a printed page and read the captions back.</para>
///
/// <para><b>Which of these redden on today's <c>main</c>, since three of them must.</b> Everything that names
/// one of the three new <see cref="ReportKind"/> members cannot compile against <c>main</c> at all, which is a
/// weaker statement than a red test. So the route tests are written to compile on <c>main</c> unchanged — they
/// name only <see cref="Screen.Report"/>, report titles and shell predicates — and each of them FAILS there:
/// <see cref="The_three_rehomed_rows_now_open_reports_rather_than_pages"/> (main lands on
/// <c>Screen.InterestReport</c> / <c>Screen.ChallanReconciliation</c> / <c>Screen.TcsChallanReconciliation</c>),
/// <see cref="The_Int_quick_button_lands_on_the_same_surface_as_the_menu_row"/>, and
/// <see cref="F12_and_Alt_F12_reach_the_reconciliation_through_the_real_key_tunnel"/> (on main
/// <c>IsReportContext</c> is false over Alt+R, so the bare-F12 arm never fires and no panel opens).</para>
/// </summary>
public sealed class RehomedReportSurfaceY4Tests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    // Taken verbatim from TcsStatPaymentViewModelTests rather than invented: the GSTIN carries a Luhn-mod-36
    // checksum that Gstin.Validate enforces, so a plausible-looking one throws at EnableGst.
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RehomedReportSurfaceY4Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRehomedY4_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The three kinds this wave re-homed.</summary>
    public static IEnumerable<object[]> RehomedKinds() => new[]
    {
        new object[] { ReportKind.InterestCalculation },
        new object[] { ReportKind.TdsChallanReconciliation },
        new object[] { ReportKind.TcsChallanReconciliation },
    };

    // ================================================================= the egress contract (the real work)

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST OF THIS WAVE.</b> Every re-homed kind must export WITH COLUMN CAPTIONS and print
    /// WITH COLUMN CAPTIONS, asserted against the SAME report object because the two are different code paths and
    /// a kind can land in one and not the other. A CSV whose header row is <c>,,,,</c> and a printed report whose
    /// header band is empty boxes are both things an operator hands to a bank, an auditor or a tax officer.
    ///
    /// <para>The last assertion is the one the shared-matrix design buys and a per-kind caption table cannot:
    /// screen, spreadsheet and printed page agree caption for caption because there is ONE list. If it ever
    /// fails, someone has re-introduced a second table.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_y4_kind_exports_and_prints_with_populated_column_captions(ReportKind kind)
    {
        var reports = PopulatedReport($"Y4 Egress {kind}", kind);

        Assert.True(reports.IsWideMatrixReport,
            $"{kind} is not on the wide-matrix surface, so neither egress projector can see its columns.");
        Assert.True(reports.PayrollColumns.Count >= 2,
            $"{kind} built {reports.PayrollColumns.Count} column(s); a re-homed report needs a label column and "
            + "at least one figure column or there is nothing to caption.");
        Assert.All(reports.PayrollColumns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} has an unlabelled on-screen column."));

        var exported = ReportTabularProjector.Project(reports);
        Assert.Equal(reports.PayrollColumns.Count, exported.Columns.Count);
        Assert.All(exported.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} exported a BLANK column caption — the re-home would have landed in neither projector map "
            + "and gained a defect the page Screen it replaced did not have."));

        var printed = ReportPrintProjector.Project(reports);
        Assert.Equal(reports.PayrollColumns.Count, printed.Columns.Count);
        Assert.All(printed.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} printed a BLANK column caption band."));

        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            exported.Columns.Select(c => c.Header).ToList());
        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            printed.Columns.Select(c => c.Header).ToList());

        // …and the document is not an empty shell under those captions. A caption test over a report with no
        // rows passes for the wrong reason, which is the anti-vacuity lesson this file inherits.
        Assert.NotEmpty(reports.PayrollRows);
        Assert.NotEmpty(exported.Rows);
        Assert.NotEmpty(printed.Rows);
    }

    /// <summary>
    /// 🔴 <b>THE CAPTIONS ARE THE PAGE'S OWN, VERBATIM — ASSERTED LITERAL BY LITERAL.</b>
    ///
    /// <para>An invented-but-plausible heading over the wrong data is worse than a blank one, because it is
    /// believable. These three lists are transcribed from the three superseded <c>DataTemplate</c>s' own
    /// <c>colHdr</c> rows in <c>MainWindow.axaml</c>, so a reader can diff this test against the grid an operator
    /// used to read. The one deliberate omission is recorded: the TDS reconciliation grid carries a BLANK column
    /// between Section and Deducted which holds its matched/unmatched tick, has no caption and no data, and is
    /// not reproduced — a column captioned nothing is the defect the band exists to remove.</para>
    /// </summary>
    [Fact]
    public void The_rehomed_column_captions_are_the_superseded_pages_own_headings()
    {
        var interest = PopulatedReport("Y4 Captions Interest", ReportKind.InterestCalculation);
        Assert.Equal(
            new[] { "Ledger", "Ref", "Principal", "Rate", "Days", "Interest" },
            interest.PayrollColumns.Select(c => c.Header).ToArray());

        var tds = PopulatedReport("Y4 Captions Tds", ReportKind.TdsChallanReconciliation);
        Assert.Equal(
            new[] { "Section", "Deducted", "Deposited", "Remaining", "Status" },
            tds.PayrollColumns.Select(c => c.Header).ToArray());

        var tcs = PopulatedReport("Y4 Captions Tcs", ReportKind.TcsChallanReconciliation);
        Assert.Equal(
            new[] { "Code", "Collected", "Deposited", "Remaining", "Status" },
            tcs.PayrollColumns.Select(c => c.Header).ToArray());

        // The money columns are the NUMERIC ones on all three, which is what makes a spreadsheet store real
        // decimals and total them rather than storing text that merely looks like money.
        Assert.Equal(new[] { false, false, true, true, true, true },
            interest.PayrollColumns.Select(c => c.IsNumeric).ToArray());
        Assert.Equal(new[] { false, true, true, true, false },
            tds.PayrollColumns.Select(c => c.IsNumeric).ToArray());
        Assert.Equal(new[] { false, true, true, true, false },
            tcs.PayrollColumns.Select(c => c.IsNumeric).ToArray());
    }

    /// <summary>
    /// 🔴🔴 <b>THE TOTALITY GUARD THIS WAVE ADDS, AND IT CLOSES A HOLE THE STANDING ONE CANNOT SEE.</b>
    ///
    /// <para><b>Measured, not theorised.</b> While mutation-verifying this slice I removed the three new kinds
    /// from <c>IsWideMatrixReport</c> — the exact "re-homed into neither projector map" regression this file
    /// warns about — and ran <c>ReportColumnBandCoverageTests</c>, the repository's own standing guard against
    /// blank captions. It passed, 492 of 492. Both of its sweeps are structurally blind to this shape:</para>
    /// <list type="number">
    ///   <item><c>Every_kind_on_the_generic_cell_path_declares_a_column_band</c> gates itself on
    ///   <c>ReportColumnBands.NeedsBand</c>, which is <c>!IsAccountingReport &amp;&amp; !IsPayrollMatrix</c>. A kind
    ///   that falls OUT of the matrix falls INTO <c>IsAccountingReport</c> — that property is a NEGATION over the
    ///   report families — so <c>NeedsBand</c> goes false and the kind is skipped by the assertion written for it.
    ///   That test's own doc comment records this trap in the abstract; this is it in the concrete.</item>
    ///   <item><c>No_populated_cell_is_dropped_from_the_exported_document</c> returns early on
    ///   <c>vm.Rows.Count == 0</c>, and a matrix builder populates <c>PayrollRows</c>, never <c>Rows</c>. So it
    ///   reads the dropped-out kind as a report with no data.</item>
    /// </list>
    ///
    /// <para>The consequence of the hole is the worst-shaped one in this area: the kind renders the empty
    /// accounting Particulars/Dr/Cr grid stacked over its own figures, and BOTH projectors read
    /// <c>Particulars</c>/<c>Amount</c>/<c>Debit</c>/<c>Credit</c> — which a matrix builder never sets — so the
    /// exported spreadsheet and the printed page are pages of <b>entirely blank cells with the row count and the
    /// totals intact</b>. A document that says nothing at all while looking like a real one.</para>
    ///
    /// <para>So this sweeps <b>every</b> <see cref="ReportKind"/> rather than this wave's three, and asserts the
    /// implication in the direction that actually bites: <b>a kind whose builder populated the matrix rows must
    /// claim the matrix surface.</b> It needs no fixture data to bite — each of this wave's three kinds emits its
    /// total row even on an empty book — and it fails by name for a kind nobody has written a test for yet, which
    /// is the whole point of a totality guard over a named list.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllReportKinds))]
    public void A_kind_that_fills_the_matrix_rows_must_claim_the_matrix_surface(ReportKind kind)
    {
        var vm = NewCompany($"Y4 Totality {(int)kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        if (reports.PayrollRows.Count == 0 && reports.PayrollRows2.Count == 0) return;

        Assert.True(reports.IsPayrollMatrix,
            $"{kind} populated the matrix rows but does not claim IsPayrollMatrix, so the matrix pane is hidden "
            + "and both egress projectors read Particulars/Amount/Debit/Credit — cells a matrix builder never "
            + "sets. On screen the empty accounting grid draws over its figures; the exported and printed copies "
            + "are blank cells with the row count and the totals intact.");

        // …and the two documents it produces carry real captions, which is the property the matrix surface is
        // chosen FOR. Asserted on the projected artefacts, never on a map's size: a kind added to a caption
        // table while still emitting blanks is not fixed, and counting entries cannot tell the two apart.
        Assert.All(ReportTabularProjector.Project(reports).Columns,
            c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
                $"{kind} exported a blank column caption."));
        Assert.All(ReportPrintProjector.Project(reports).Columns,
            c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
                $"{kind} printed a blank column caption."));
    }

    /// <summary>Every <see cref="ReportKind"/>, for the totality sweep above.</summary>
    public static TheoryData<ReportKind> AllReportKinds()
    {
        var data = new TheoryData<ReportKind>();
        foreach (var k in Enum.GetValues<ReportKind>()) data.Add(k);
        return data;
    }

    /// <summary>
    /// Alt+K (save this view) indexes the persisted-token map DIRECTLY, so a kind missing from it throws
    /// <see cref="KeyNotFoundException"/> the instant an operator presses the chord — on the very gesture the
    /// re-home exists to hand these reports.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_y4_kind_has_a_saved_view_token_that_round_trips(ReportKind kind)
    {
        var token = ReportsViewModel.TokenFor(kind);      // throws if the kind was not registered
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(kind, ReportsViewModel.KindFor(token));
    }

    /// <summary>
    /// 🔴 <b>THE TWO GRID-VISIBILITY TRAPS, PINNED.</b> A matrix report leaves <c>Rows</c> empty, which is how
    /// the accounting grid and the shared empty-state overlay both decide what to draw. Get either wrong and the
    /// report is CORRECT and UNREADABLE: the accounting Particulars/Dr/Cr table renders stacked on top of the
    /// real grid, or a fully populated report renders under a "No entries for the selected period." banner.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_rehomed_y4_report_shows_neither_the_accounting_grid_nor_the_empty_state_overlay(ReportKind kind)
    {
        var reports = PopulatedReport($"Y4 Grid {kind}", kind);

        Assert.False(reports.IsAccountingReport,
            $"{kind} still claims the accounting grid, which would draw an empty Particulars/Dr/Cr table over "
            + "its own rows — two tables at once.");
        Assert.False(reports.IsEmpty,
            $"{kind} reports IsEmpty, so the shared overlay would cover a populated matrix with "
            + "\"No entries for the selected period.\" — the figures are on screen underneath it.");
        Assert.True(reports.IsPayrollMatrix, $"{kind} does not render through the matrix DataTemplate.");
        Assert.False(reports.IsPayrollEmpty,
            $"{kind} marked itself empty on a populated fixture, which hides the grid it just filled.");

        // And the wage-MONTH picker must stay off: none of these three is scoped to a wage month, they are
        // scoped by the ordinary report period, which is the whole gesture the re-home hands them.
        Assert.False(reports.IsPayrollReport,
            $"{kind} claims the payroll family, which would offer it a wage-month picker it cannot honour.");
    }

    // ================================================================= the routes (arrow-walked)

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY'S <c>main</c>, AND COMPILES THERE.</b> The three menu rows are arrow-walked from the
    /// Gateway and each must land on <see cref="Screen.Report"/>. On <c>main</c> they land on
    /// <c>Screen.InterestReport</c>, <c>Screen.ChallanReconciliation</c> and
    /// <c>Screen.TcsChallanReconciliation</c> respectively, so all three assertions fail by name.
    ///
    /// <para>Walked with the arrow keys rather than by calling the opener, because a capability no operator can
    /// reach from the keyboard is not delivered — this repository has shipped that shape more than once.</para>
    /// </summary>
    [Fact]
    public void The_three_rehomed_rows_now_open_reports_rather_than_pages()
    {
        var interestVm = InterestFixture("Y4 Route Interest", out _);
        AssertRouteOpensReport(interestVm, "Interest Calculation",
            "Statements of Accounts", "Interest Calculation");

        var tdsVm = TdsFixtureWithAWithholding("Y4 Route Tds");
        AssertRouteOpensReport(tdsVm, "Challan Reconciliation", "GST Reports", "Challan Reconciliation");

        var tcsVm = TcsFixtureWithACollection("Y4 Route Tcs");
        AssertRouteOpensReport(tcsVm, "TCS Challan Reconciliation",
            "GST Reports", "TCS Challan Reconciliation");
    }

    /// <summary>
    /// 🔴 <b>THE SECOND DOOR, WHICH IS THE ONE A RE-HOME FORGETS.</b> The Interest Calculation report has three
    /// doors: its menu row, the <b>"Int"</b> quick-button on the button bar, and Alt+K. Re-pointing the menu row
    /// alone leaves one click on the bar opening the superseded page, so the SAME figures would have a printable
    /// copy and an unprintable copy depending on which door the operator used. A prior re-homing on this project
    /// left "one shipped report nobody can reach"; this is that failure in its other direction.
    ///
    /// <para><b>Fails on today's main</b>, where the bar's action lands on <c>Screen.InterestReport</c>.</para>
    /// </summary>
    [Fact]
    public void The_Int_quick_button_lands_on_the_same_surface_as_the_menu_row()
    {
        var vm = InterestFixture("Y4 Button Door", out _);
        vm.ShowGateway();

        var button = Assert.Single(vm.ButtonBar.Where(b => b.Key == "Int"));
        Assert.True(button.Enabled, "the Interest quick-button is disabled on a company with the feature on");
        button.Action();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal("Interest Calculation", vm.Reports!.Title);
        Assert.True(vm.IsReportContext,
            "the quick-button opened a surface with the report-parameter chords still dead");
    }

    /// <summary>
    /// 🔴 <b>THE REALISED WINDOW, WITH REAL KEYSTROKES, AND IT PROVES THE PANELS ACT RATHER THAN RENDER.</b>
    ///
    /// <para>Alt+R opens the TDS reconciliation through <see cref="MainWindow"/>'s own key tunnel. Then bare
    /// <b>F12</b> — whose arm is gated on <c>vm.IsReportContext</c>, the exact predicate a page Screen made false
    /// — must open the configuration panel; a real period is typed into it and <b>Ctrl+A</b> applied, and the
    /// report's FIGURES must move. A knob that renders and changes nothing is a dead knob, and this file's own
    /// sibling records three of those having shipped. Then <b>Alt+F12</b> must open the sort/filter panel and
    /// REFUSE out loud instead of answering "Applied".</para>
    ///
    /// <para><b>Fails on today's main</b> for the plainest reason: over Alt+R the shell is on
    /// <c>Screen.ChallanReconciliation</c> with <c>Reports</c> null, so <c>IsReportContext</c> is false, the
    /// bare-F12 arm never fires, and <c>vm.ReportConfig</c> stays null.</para>
    /// </summary>
    [AvaloniaFact]
    public void F12_and_Alt_F12_reach_the_reconciliation_through_the_real_key_tunnel()
    {
        var vm = TdsFixtureWithAWithholding("Y4 Tunnel");
        var window = Show(vm);
        try
        {
            Press(window, Key.R, KeyModifiers.Alt);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal("Challan Reconciliation", vm.Reports!.Title);
            Assert.True(vm.IsReportContext, "the re-homed reconciliation is still outside the report context");

            // ---- F12: the panel opens AND acts.
            var before = MatrixSnapshot(vm.Reports!);
            Assert.NotEqual(string.Empty, before);

            Press(window, Key.F12);
            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            var config = Assert.IsType<ReportConfigViewModel>(vm.ReportConfig);
            Assert.Equal("Challan Reconciliation", config.ReportTitle);

            // A window that deliberately EXCLUDES the posted withholding: the book's only TDS voucher is dated
            // inside the financial year, so a window ending before it must empty the reconciliation. Proving the
            // knob acts needs the figures to MOVE, not merely to be recomputed to the same thing.
            var c = vm.Company!;
            config.UsePeriod = true;
            config.PeriodFromText = ApexDate.Format(c.FinancialYearStart.AddYears(-1));
            config.PeriodToText = ApexDate.Format(c.FinancialYearStart.AddDays(-1));
            Press(window, Key.A, KeyModifiers.Control);

            Assert.Contains("Applied", config.Status);
            var after = MatrixSnapshot(vm.Reports!);
            Assert.NotEqual(before, after);
            Assert.True(vm.Reports!.IsPayrollEmpty,
                "the reconciliation still shows rows for a window that contains none of its vouchers");

            // ---- Alt+F12: the panel opens and says it cannot act, rather than claiming it applied.
            Press(window, Key.Escape);
            Press(window, Key.F12, KeyModifiers.Alt);
            Assert.Equal(Screen.ReportSortFilter, vm.CurrentScreen);
            var sortFilter = Assert.IsType<ReportSortFilterViewModel>(vm.ReportSortFilter);
            Assert.True(sortFilter.CannotSortOrFilter);

            sortFilter.NameContains = "194J";
            vm.ApplyReportSortFilter();
            Assert.DoesNotContain("Applied", sortFilter.Status);
            Assert.Contains("does not act on this report", sortFilter.Status);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE CAPTIONS REACH THE SCREEN, MEASURED ON THE REALISED VISUAL TREE.</b> Everything above asserts
    /// view-model and projector state, and view-model state is exactly what a report can have in full while
    /// rendering nothing — the matrix pane is gated on <c>IsPayrollMatrix</c> and the accounting pane on
    /// <c>IsAccountingReport</c>, so getting either wrong yields a correct view model behind a blank or
    /// double-drawn pane. This opens the real window and reads the header band that actually laid out.
    ///
    /// <para>The width is cross-checked against the LAYOUT rather than re-derived from the arithmetic that
    /// produced it: the laid-out <c>TextBlock.Bounds.Width</c> comes from the template's
    /// <c>Width="{Binding Width}"</c>, so a template that dropped that binding — which would auto-size the band
    /// out of step with the body rows and shift every column — reddens here and nowhere else. The +1 allowance is
    /// Avalonia rounding a laid-out size up to a whole device pixel.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_y4_header_band_lays_out_on_screen_with_its_captions(ReportKind kind)
    {
        var vm = FixtureFor($"Y4 Visual {kind}", kind);
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
                Assert.InRange(block.Bounds.Width, column.Width, column.Width + 1);
            }

            // The accounting Particulars/Dr/Cr band must not be drawn at the same time. "Particulars" is that
            // grid's own first caption and none of these three uses it, so a visible one means two stacked
            // tables — which is how this defect presented the last time it shipped.
            Assert.DoesNotContain(
                Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
                t => t.Text == "Particulars");
        }
        finally { window.Close(); }
    }

    // ================================================================= the figures survived the move

    /// <summary>
    /// 🔴 <b>THE INTEREST FIGURE, HAND-COMPUTED TWICE, AND THE PERIOD KNOB MEASURED ON IT.</b>
    ///
    /// <para>₹1,00,000 drawn as a credit (loan) balance on the financial-year start, 18% per annum on the
    /// 365-day basis, simple, no rounding. The engine's accrual is <c>principal × rate × days / basis</c> over
    /// the half-open window <c>[from, to)</c>, so:</para>
    /// <list type="bullet">
    ///   <item>over 365 days: <c>100000 × 0.18 × 365 / 365</c> = <b>₹18,000.00</b> exactly;</item>
    ///   <item>over 73 days: <c>100000 × 0.18 × 73 / 365</c> = <c>18000 × 0.2</c> = <b>₹3,600.00</b> exactly.</item>
    /// </list>
    ///
    /// <para>Both are independent arithmetic, not a second reading of the engine, which is what makes this more
    /// than a refactor assertion. The second figure doubles as the proof that Alt+F2 ACTS on this kind: the two
    /// windows differ and so do the figures, by a factor computed by hand.</para>
    /// </summary>
    [Fact]
    public void The_rehomed_interest_report_accrues_the_hand_computed_18000_and_the_period_moves_it()
    {
        var vm = InterestFixture("Y4 Interest Golden", out var company);
        var reports = new ReportsViewModel(company, ReportKind.InterestCalculation);

        var fyStart = company.FinancialYearStart;
        reports.SetPeriod(fyStart, fyStart.AddDays(365));

        var loanRow = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Bank Loan"));
        Assert.Equal("18%", loanRow.Cells[3].Text);
        Assert.Equal("365", loanRow.Cells[4].Text);
        Assert.Equal("1,00,000.00 Cr", loanRow.Cells[2].Text);
        Assert.Equal("18,000.00", loanRow.Cells[5].Text);

        var total = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Total Interest"));
        Assert.Equal("18,000.00", total.Cells[5].Text);

        // ---- Alt+F2 to a 73-day window: the figures must move to the hand-computed ₹3,600.00.
        reports.SetPeriod(fyStart, fyStart.AddDays(73));

        var shortRow = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Bank Loan"));
        Assert.Equal("73", shortRow.Cells[4].Text);
        Assert.Equal("3,600.00", shortRow.Cells[5].Text);
        Assert.Equal("3,600.00",
            Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Total Interest")).Cells[5].Text);
    }

    /// <summary>
    /// 🔴 <b>THE RE-HOME MOVED NO FIGURE ON THE DEFAULT WINDOW, PROVEN CELL FOR CELL AGAINST THE PAGE.</b>
    ///
    /// <para><c>InterestReportViewModel</c> computed its own window as books-begin → "the last voucher date, or
    /// the financial-year end when there are no vouchers", which is the same expression
    /// <c>ReportsViewModel.ComputeAsOf</c> evaluates for the report's DEFAULT as-of. So an operator who opens the
    /// re-homed report and touches nothing must see precisely what the page showed. This compares the superseded
    /// page's own rows against the report's matrix cells, in order, with no window set on either.</para>
    ///
    /// <para>It is deliberately a comparison and not a golden: a golden would re-state the engine, where this
    /// pins the one property a re-home is allowed to have — that it changed the surface and nothing else.</para>
    /// </summary>
    [Fact]
    public void The_rehomed_interest_report_renders_the_superseded_pages_figures_cell_for_cell()
    {
        var vm = InterestFixture("Y4 Interest Parity", out var company);

        var page = new InterestReportViewModel(company);
        var reports = new ReportsViewModel(company, ReportKind.InterestCalculation);

        Assert.Equal(page.Title, reports.Title);
        Assert.Equal(page.Subtitle, reports.Subtitle);

        // The page emits one row per accrual plus a total row; so does the matrix. (The page's "no
        // interest-enabled ledgers" placeholder row has no matrix twin — the matrix says that in its own empty
        // note instead — so the fixture is asserted non-degenerate first.)
        var pageRows = page.Rows.Where(r => !r.Ledger.StartsWith("No interest-enabled", StringComparison.Ordinal))
            .ToList();
        Assert.True(pageRows.Count >= 2, "the parity fixture has no accrual in it, so this compares nothing");
        Assert.Equal(pageRows.Count, reports.PayrollRows.Count);

        for (var i = 0; i < pageRows.Count; i++)
        {
            var expected = new[]
            {
                pageRows[i].Ledger, pageRows[i].Reference, pageRows[i].Principal,
                pageRows[i].Rate, pageRows[i].Days, pageRows[i].Interest,
            };
            Assert.Equal(expected, reports.PayrollRows[i].Cells.Select(c => c.Text).ToArray());
        }
    }

    /// <summary>
    /// 🔴 <b>THE TDS RECONCILIATION'S FIGURES, HAND-COMPUTED, AND THE CAVEAT THAT HAS TO TRAVEL WITH THEM.</b>
    ///
    /// <para>One posted §194J(b) withholding on a ₹1,00,000 professional fee. The seeded §194J(b) with-PAN rate
    /// is 1,000 basis points — 10% — and the ₹50,000 cumulative threshold is exceeded, so the deduction is
    /// <c>100000 × 10%</c> = <b>₹10,000.00</b>. Nothing has been deposited, so Deposited is ₹0.00, Remaining is
    /// the whole ₹10,000.00 and the section reads <b>Short</b>. Those four figures are the report.</para>
    ///
    /// <para>🔴 <b>And the cash-basis footnote must reach BOTH documents.</b> The page carried that sentence into
    /// its export as a trailing row and its own comment records why: without it a compliant deducted-in-March /
    /// deposited-in-April entry reads as an outstanding default. On the matrix surface it travels through
    /// <c>PayrollFootnotes</c>, which both egress projectors append — asserted here on the real projections,
    /// because "it is in a collection" would not have proved it leaves the building.</para>
    /// </summary>
    [Fact]
    public void The_rehomed_tds_reconciliation_shows_the_hand_computed_10000_and_carries_its_cash_basis_caveat()
    {
        var vm = TdsFixtureWithAWithholding("Y4 Tds Golden");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.TdsChallanReconciliation);

        var section = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "194J(b)"));
        Assert.Equal("10,000.00", section.Cells[1].Text);   // Deducted
        Assert.Equal("0.00", section.Cells[2].Text);        // Deposited
        Assert.Equal("10,000.00", section.Cells[3].Text);   // Remaining
        Assert.Equal("Short", section.Cells[4].Text);

        var grand = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Grand Total"));
        Assert.True(grand.IsTotal, "the Grand Total row is not marked as a total, so no document emphasises it");
        Assert.Equal("10,000.00", grand.Cells[1].Text);
        Assert.Equal("0.00", grand.Cells[2].Text);
        Assert.Equal("10,000.00", grand.Cells[3].Text);

        // ---- the caveat, in the spreadsheet and on the printed page.
        var exported = ReportTabularProjector.Project(reports);
        Assert.Contains(exported.Rows,
            r => r.Cells.Any(cell => cell.TextValue.Contains("cash basis", StringComparison.Ordinal)));

        // The printed page folds to ASCII (the PDF writer is ASCII-only), so the em dashes in the sentence do
        // not survive verbatim — "cash basis" does, and it is the clause that carries the meaning.
        var printed = ReportPrintProjector.Project(reports);
        Assert.Contains(printed.Rows,
            r => r.Cells.Any(cell => cell.Contains("cash basis", StringComparison.Ordinal)));

        // One definition of the sentence, read by the page and by the report — not two that can drift.
        Assert.Equal(ChallanReconciliationViewModel.CashBasisNote,
            new ChallanReconciliationViewModel(vm.Company!).BasisNote);
        Assert.Contains(ChallanReconciliationViewModel.CashBasisNote, reports.PayrollFootnotes);
    }

    /// <summary>
    /// The TCS mirror, on the golden scrap sale: ₹1,00,000 of taxable value plus 18% GST is a ₹1,18,000
    /// GST-inclusive base, and the seeded collection code <b>6CE</b> collects 1% of it — <b>₹1,180.00</b>.
    /// Hand-computed, and independently asserted by the fixture itself before it posts, so a changed rate table
    /// reddens at the fixture rather than silently restating this figure. Nothing deposited, so Remaining is the
    /// whole ₹1,180.00 and the code reads Short.
    ///
    /// <para>⚠️ The RATE is stated as the seeded one, not as a statutory claim: the 1% comes from this
    /// repository's own §206C seed table and the engine fixture that mirrors it. Nothing here opens a statutory
    /// source, and nothing here needs to — the re-home is a surface change, and whether 1% is the right rate is
    /// a question for the seed's own citations, not for this test.</para>
    /// </summary>
    [Fact]
    public void The_rehomed_tcs_reconciliation_shows_the_hand_computed_1180()
    {
        var vm = TcsFixtureWithACollection("Y4 Tcs Golden");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.TcsChallanReconciliation);

        var code = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "6CE"));
        Assert.Equal("1,180.00", code.Cells[1].Text);       // Collected
        Assert.Equal("0.00", code.Cells[2].Text);           // Deposited
        Assert.Equal("1,180.00", code.Cells[3].Text);       // Remaining
        Assert.Equal("Short", code.Cells[4].Text);

        var grand = Assert.Single(reports.PayrollRows.Where(r => r.Cells[0].Text == "Grand Total"));
        Assert.Equal("1,180.00", grand.Cells[1].Text);
        Assert.Equal("1,180.00", grand.Cells[3].Text);

        Assert.Contains(TcsChallanReconciliationViewModel.CashBasisNote, reports.PayrollFootnotes);
    }

    // ================================================================= the emitted bytes, on disk

    /// <summary>
    /// 🔴 <b>A FILE, ON DISK, WITH THE CAPTIONS AND THE FIGURES IN IT — FROM THE RE-HOMED SURFACE.</b>
    ///
    /// <para>Everything above this asserts a projection. A projection is the right level for a caption claim,
    /// but "the report exports" is a claim about bytes, and the only way to settle it is to write the file and
    /// read it back. It goes through <see cref="ExportViewModel"/>'s own REPORT constructor — the one the shell
    /// uses on Ctrl+E over a live report — so what is exercised is the real path, not a hand-assembled one.</para>
    ///
    /// <para>The content assertions are chosen to fail on the two things that could go wrong without a size
    /// check noticing: a BLANK header row (the captions), and a report that wrote its headings and no data (the
    /// section, the figure and the grand total). The money is asserted as a <b>bare decimal</b>, because the
    /// money columns are typed <c>Number</c> — a grouped display string in the file would mean a spreadsheet
    /// cannot sum the column an accountant reads this report to total.</para>
    /// </summary>
    [Fact]
    public void Exporting_the_rehomed_tds_reconciliation_writes_a_csv_carrying_its_captions_and_figures()
    {
        var vm = TdsFixtureWithAWithholding("Y4 Csv Bytes");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.TdsChallanReconciliation);

        var outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outDir);

        var panel = new ExportViewModel(reports, outDir, new DateTime(2025, 6, 1, 10, 0, 0), writeBytes: null)
        {
            Format = ExportFormat.Csv,
            FileName = "y4-tds-recon",
            AppendTimestamp = false,
        };
        Assert.True(panel.Apply(), panel.Status);

        var path = Path.Combine(outDir, "y4-tds-recon.csv");
        Assert.True(File.Exists(path), $"nothing was written to {path}");
        var text = File.ReadAllText(path);

        // The captions — the half that was BLANK on every non-accounting kind before the shared column band.
        Assert.Contains("Section", text);
        Assert.Contains("Deducted", text);
        Assert.Contains("Deposited", text);
        Assert.Contains("Remaining", text);
        Assert.Contains("Status", text);

        // …and real data under them, not a header-only file.
        Assert.Contains("194J(b)", text);
        Assert.Contains("Grand Total", text);
        Assert.Contains("Short", text);
        Assert.Contains("10000.00", text);          // a bare decimal: the column is typed Number
        Assert.DoesNotContain("10,000.00", text);   // …and NOT a grouped display string

        // The cash-basis caveat left the building with the figures, which is the whole reason it is a footnote
        // the projectors append rather than a line on the screen.
        Assert.Contains("cash basis", text);
    }

    /// <summary>
    /// The PDF path renders a real document from the same report. A magic-number and length assertion is enough
    /// here only because the CSV case above carries the content claim and the two share
    /// <see cref="ReportPrintProjector"/> — what prints is what exports.
    /// </summary>
    [Fact]
    public void Printing_the_rehomed_interest_report_writes_a_real_pdf()
    {
        var vm = InterestFixture("Y4 Pdf Bytes", out var company);
        var reports = new ReportsViewModel(company, ReportKind.InterestCalculation);

        var outDir = Path.Combine(_tempDir, "pdf");
        Directory.CreateDirectory(outDir);

        var panel = new ExportViewModel(reports, outDir, new DateTime(2025, 6, 1, 10, 0, 0), writeBytes: null)
        {
            Format = ExportFormat.Pdf,
            FileName = "y4-interest",
            AppendTimestamp = false,
        };
        Assert.True(panel.Apply(), panel.Status);

        var bytes = File.ReadAllBytes(Path.Combine(outDir, "y4-interest.pdf"));
        Assert.True(bytes.Length > 500, $"a rendered PDF should not be {bytes.Length} bytes");
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes.Take(4).ToArray());   // "%PDF"
    }

    /// <summary>
    /// 🔴 <b>THE RECONCILIATION WINDOW SNAPS TO THE FINANCIAL YEAR CONTAINING THE AS-OF, AND F2 THEREFORE
    /// ACTS.</b>
    ///
    /// <para>Both pages were pinned to <c>Company.FinancialYearStart</c>'s own year with no way to change it.
    /// The report surface's ordinary window is books-begin → as-of, which over a multi-year book is a different
    /// window and would silently restate a statutory reconciliation. So the builder snaps to the financial year
    /// CONTAINING the as-of, snapped to the company's own start month — the same construction the bonus register
    /// uses, and for the same reason: <c>asOf.Year</c> alone puts a February as-of into the wrong year for an
    /// April-start book.</para>
    ///
    /// <para>Both halves are asserted: the default as-of lands in the year that holds the withholding and sees
    /// it, and an F2 to the PRIOR year's January — a month BEFORE an April start, so the naive
    /// <c>asOf.Year</c> would resolve the wrong year and still see the withholding — must see nothing.</para>
    /// </summary>
    [Fact]
    public void The_reconciliation_window_follows_F2_into_the_financial_year_the_as_of_lands_in()
    {
        var vm = TdsFixtureWithAWithholding("Y4 Window Snap");
        var c = vm.Company!;
        var reports = new ReportsViewModel(c, ReportKind.TdsChallanReconciliation);

        Assert.Contains(reports.PayrollRows, r => r.Cells[0].Text == "194J(b)");
        Assert.Contains(ApexDate.Format(c.FinancialYearStart), reports.Subtitle);

        // January of the calendar year in which the financial year STARTED is one month-number BELOW an April
        // start, so the snap must resolve the PREVIOUS financial year — which holds no withholding.
        reports.SetAsOf(new DateOnly(c.FinancialYearStart.Year, 1, 15));

        Assert.DoesNotContain(reports.PayrollRows, r => r.Cells[0].Text == "194J(b)");
        Assert.Contains(ApexDate.Format(new DateOnly(c.FinancialYearStart.Year - 1, c.FinancialYearStart.Month, 1)),
            reports.Subtitle);
        Assert.True(reports.IsPayrollEmpty);
    }

    // ================================================================= the ER-13 gates (unchanged by the move)

    /// <summary>
    /// 🔴 <b>RE-HOMING MUST NOT WIDEN REACHABILITY, AND A SAVED VIEW IS THE DOOR THAT FORGETS.</b> Each of these
    /// three reports sits behind a company feature whose gate lives on the MENU ROW, so Alt+K — which stores a
    /// kind token and re-opens that kind directly — walks past it. All three openers refuse, and
    /// <c>ReportKindIsPermitted</c> (asked by <c>ApplySavedView</c>) refuses too, evaluated through the SAME named
    /// properties the menu builders branch on.
    /// </summary>
    [Fact]
    public void A_company_without_the_feature_reaches_none_of_the_three_rehomed_reports()
    {
        // ---- TDS off.
        var plain = NewCompany("Y4 Gate Plain");
        plain.OpenTdsChallanReconciliationReport();
        Assert.Null(plain.Reports);
        plain.OpenTcsChallanReconciliationReport();
        Assert.Null(plain.Reports);
        Assert.DoesNotContain(plain.Columns, col => col.IsPage);

        // …and the menu rows are absent as well, so the gate is not merely on the opener.
        plain.ShowGstReportsMenu();
        Assert.DoesNotContain(plain.Columns[^1].Items, m => m.Label == "Challan Reconciliation");
        Assert.DoesNotContain(plain.Columns[^1].Items, m => m.Label == "TCS Challan Reconciliation");

        // ---- Interest Calculation off (F11 → Accounting). The menu row goes, the quick-button disables, and the
        // opener refuses — three doors, one condition.
        var noInterest = InterestFixture("Y4 Gate Interest", out _);
        noInterest.ShowGstConfig();
        noInterest.GstConfig!.EnableInterestCalculation = false;
        noInterest.Back();
        Assert.False(noInterest.Company!.EnableInterestCalculation);

        noInterest.OpenInterestCalculationReport();
        Assert.Null(noInterest.Reports);

        noInterest.ShowStatementsOfAccountsMenu();
        Assert.DoesNotContain(noInterest.Columns[^1].Items, m => m.Label == "Interest Calculation");

        noInterest.ShowGateway();
        Assert.False(Assert.Single(noInterest.ButtonBar.Where(b => b.Key == "Int")).Enabled,
            "the Interest quick-button stays clickable on a company that switched the feature off, which makes "
            + "the F11 gate cosmetic");
    }

    /// <summary>
    /// Each of the three kinds carries an explicit ER-13 decision in the kind → gate table, and the one NEW gate
    /// member this wave adds is reached by a kind. The totality tests in
    /// <see cref="RehomedReportSurfaceTests"/> already enumerate every kind and every gate member; this asserts
    /// the three specific decisions so a future edit that silently re-maps one to <c>None</c> — which would open a
    /// statutory reconciliation on a company that switched the tax off — fails by name here.
    /// </summary>
    [Fact]
    public void The_three_rehomed_kinds_carry_the_gate_their_own_menu_row_branches_on()
    {
        Assert.Equal(ReportFeatureGate.InterestCalculation,
            MainWindowViewModel.SavedViewGateFor(ReportKind.InterestCalculation));
        Assert.Equal(ReportFeatureGate.Tds,
            MainWindowViewModel.SavedViewGateFor(ReportKind.TdsChallanReconciliation));
        Assert.Equal(ReportFeatureGate.Tcs,
            MainWindowViewModel.SavedViewGateFor(ReportKind.TcsChallanReconciliation));
    }

    /// <summary>
    /// 🔴 <b>THE THREE F12 DISPLAY KNOBS CANNOT CHANGE A RE-HOMED REPORT, PROVEN BY APPLYING THEM.</b> The
    /// predicate that hides them is only honest if applying them really does nothing, so all three are applied to
    /// a populated report and the projection must come back identical cell for cell. If a future builder ever
    /// DOES honour one on a matrix kind this reddens, and <c>SupportsDisplayOptions</c> has to be revisited —
    /// which is exactly the notice that should fire.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void The_three_F12_display_knobs_cannot_change_a_rehomed_y4_report(ReportKind kind)
    {
        var reports = PopulatedReport($"Y4 Dead Knobs {kind}", kind);

        Assert.False(reports.SupportsDisplayOptions,
            $"{kind} claims the F12 display knobs act on it; if that became true the panel must show them again.");
        Assert.False(reports.SupportsHideZeroBalances);
        Assert.False(reports.SupportsPercentages);
        Assert.False(reports.SupportsClosingStockBasis);

        Assert.NotEmpty(reports.PayrollRows);        // ← the guard that bites: no rows, nothing compared.
        var before = MatrixSnapshot(reports);
        Assert.NotEqual(string.Empty, before);

        reports.ApplyConfiguration(hideZero: true, showPercentages: true,
            Apex.Ledger.Reports.ClosingStockMode.InventoryDerived);

        Assert.Equal(before, MatrixSnapshot(reports));
        Assert.NotEmpty(reports.PayrollRows);        // and hide-zero did not simply empty the report instead.
    }

    // ================================================================= harness

    /// <summary>Walks the cascade with the arrow keys down <paramref name="path"/> and asserts the leaf opened a
    /// REPORT on <see cref="Screen.Report"/> carrying <paramref name="expectedTitle"/> — not a page Screen.
    /// Deliberately asserts the TITLE rather than the kind so this method compiles against a build that has no
    /// such <see cref="ReportKind"/> member, which is what lets the route tests be run against <c>main</c>.</summary>
    private static void AssertRouteOpensReport(
        MainWindowViewModel vm, string expectedTitle, params string[] path)
    {
        vm.ShowGateway();
        foreach (var label in path) ArrowToAndDrill(vm, label);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expectedTitle, vm.Reports!.Title);
        Assert.True(vm.IsReportContext,
            $"'{expectedTitle}' opened on a surface where the report-parameter chords are still dead");
    }

    /// <summary>Arrows down the ACTIVE column until the highlighted row carries this label, then drills in — the
    /// sequence an operator's fingers perform. Fails loudly when the row is not arrow-reachable, which is the
    /// failure a "does the view model have the method" test cannot see.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    /// <summary>The matrix's ROW text only — never the column band. The band is exactly the half
    /// <c>ApplyConfiguration</c> could not alter under any circumstance, so a snapshot that included it would be
    /// non-empty at zero rows and its own anti-vacuity guard would stop biting.</summary>
    private static string MatrixSnapshot(ReportsViewModel r) => string.Join("|",
        r.PayrollRows.Select(row => string.Join(",", row.Cells.Select(x => x.Text)))
         .Concat(r.PayrollRows2.Select(row => string.Join(",", row.Cells.Select(x => x.Text)))));

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private MainWindowViewModel FixtureFor(string name, ReportKind kind) => kind switch
    {
        ReportKind.InterestCalculation => InterestFixture(name, out _),
        ReportKind.TdsChallanReconciliation => TdsFixtureWithAWithholding(name),
        ReportKind.TcsChallanReconciliation => TcsFixtureWithACollection(name),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a W-Y4 re-homed kind"),
    };

    /// <summary>A report of <paramref name="kind"/> that actually HAS ROWS IN IT. A caption test over an empty
    /// report passes for the wrong reason.</summary>
    private ReportsViewModel PopulatedReport(string name, ReportKind kind)
    {
        var vm = FixtureFor(name, kind);
        var reports = new ReportsViewModel(vm.Company!, kind);
        Assert.NotEmpty(reports.PayrollRows);
        return reports;
    }

    /// <summary>
    /// A company with one interest-bearing loan: ₹1,00,000 drawn on the financial-year start as a CREDIT balance,
    /// 18% per annum on the 365-day basis, simple, unrounded. Deliberately ONE voucher and no stray rupee, so the
    /// principal is a round ₹1,00,000 and the accrual can be computed by hand to the paisa.
    /// </summary>
    private MainWindowViewModel InterestFixture(string name, out Company company)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;

        var cash = Ledger(c, "Cash", "Cash-in-Hand");
        cash.OpeningBalance = Money.FromRupees(10_00_000m);
        cash.OpeningIsDebit = true;

        var loan = new DomainLedger(Guid.NewGuid(), "Bank Loan",
            c.FindGroupByName("Loans (Liability)")!.Id, Money.Zero, openingIsDebit: false)
        {
            Interest = new InterestParameters(
                enabled: true, ratePercent: 18m, per: InterestPer.ThreeSixtyFiveDayYear),
        };
        c.AddLedger(loan);

        var journal = c.FindVoucherTypeByName("Journal")!.Id;
        var post = new LedgerService(c);

        post.Post(new Voucher(Guid.NewGuid(), journal, c.FinancialYearStart, new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(1_00_000m), DrCr.Debit),
            new EntryLine(loan.Id, Money.FromRupees(1_00_000m), DrCr.Credit),
        }));

        // 🔴 A SECOND VOUCHER EXACTLY 365 DAYS LATER THAT DOES NOT TOUCH THE LOAN, AND BOTH HALVES OF THAT ARE
        // DELIBERATE. The report's default as-of is the LAST VOUCHER DATE, so with only the draw posted the
        // window is [FY start, FY start] — a zero-day window, no accrual, and every figure assertion below would
        // be comparing two empty reports. This voucher pushes the as-of out a full year so the DEFAULT window
        // itself accrues the hand-computed ₹18,000. It touches Rent and Cash and NOT the loan, because a
        // movement on the loan would split the accrual into two segments at a non-round principal — which is
        // what the predecessor fixture did with a stray ₹1, and why its assertion had to be a range instead of
        // a figure.
        var rent = Ledger(c, "Rent", "Indirect Expenses");
        post.Post(new Voucher(Guid.NewGuid(), journal, c.FinancialYearStart.AddDays(365), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(5_000m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(5_000m), DrCr.Credit),
        }));

        _storage.Save(c);
        company = c;
        return vm;
    }

    /// <summary>
    /// A TDS-enabled company carrying one posted §194J(b) withholding on a ₹1,00,000 professional fee, so the
    /// reconciliation has exactly one section row. Driven through the real voucher-entry view model, and the
    /// TDS panel's engagement is asserted — a fixture whose withholding never engaged would make every figure
    /// assertion below it pass against a zero.
    /// </summary>
    private MainWindowViewModel TdsFixtureWithAWithholding(string name)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

        var fees = Ledger(c, "Professional Fees", "Indirect Expenses");
        var vendor = Ledger(c, "Acme Consultants", "Sundry Creditors");
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;

        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Lines[0].SelectedLedger = fees; e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = "100000";
        e.Lines[1].SelectedLedger = vendor; e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = "100000";
        e.Recalculate();
        Assert.True(e.ShowTdsPanel, "fixture is vacuous unless the withholding actually engages");
        Assert.True(e.Accept());

        vm.ShowGateway();
        _storage.Save(c);
        return vm;
    }

    /// <summary>
    /// A TCS-enabled company carrying the golden scrap sale that collects ₹1,180 under the seeded collection code
    /// 6CE (1% of the ₹1,18,000 GST-inclusive base). Mirrors the engine's own <c>CollectElevenEighty</c> fixture,
    /// and asserts the collection amount before posting so a changed rate table fails here rather than in a
    /// figure assertion.
    /// </summary>
    private MainWindowViewModel TcsFixtureWithACollection(string name)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;

        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = BuyerPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal",
            inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;

        var sales = Ledger(c, "Scrap Sales", "Sales Accounts");
        var buyer = Ledger(c, "Scrap Buyer", "Sundry Debtors");
        buyer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };
        buyer.TcsApplicable = true;
        buyer.CollecteeType = CollecteeType.Individual;
        buyer.PartyPan = BuyerPan;

        var date = c.FinancialYearStart.AddMonths(1).AddDays(9);
        var value = Money.FromRupees(1_00_000m);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(value, 1800) },
            gst.IsInterState(buyer.PartyGst!.StateCode), GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var collection = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, date);
        Assert.Equal(Money.FromRupees(1_180m), collection.TcsAmount);

        var lines = new List<EntryLine>
        {
            new(buyer.Id, value + tax.TotalTax + collection.TcsAmount, DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(collection.TcsPayableLine!);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, date, lines,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(scrap.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m)),
            }));

        vm.ShowGateway();
        _storage.Save(c);
        return vm;
    }

    /// <summary>The named ledger, created under <paramref name="groupName"/> if the fresh company did not seed
    /// it. Tolerating both is deliberate: which ledgers a new company seeds is not this wave's contract.</summary>
    private static DomainLedger Ledger(Company c, string name, string groupName)
    {
        if (c.FindLedgerByName(name) is { } existing) return existing;
        var created = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(created);
        return created;
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.MinWidth = 640;
        window.MinHeight = 480;
        window.Show();
        Pump(window);
        return window;
    }

    private static void Press(MainWindow window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = window,
        });
        Dispatcher.UIThread.RunJobs();
    }

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
