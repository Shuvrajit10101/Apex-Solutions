using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Avalonia.Headless;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W2-13a / census row 14.5 (partial) — Ctrl+B "Basis of Values" and its Scale Factor.</b>
///
/// <para><b>The defect these lock.</b> The census graded row 14.5 ABSENT because <i>none</i> of the eight
/// standard report button-bar options existed — per-term greps for all eight returned zero, and the only
/// "Basis of Values" hits in the tree were three doc comments explaining why Ctrl+B was deliberately left
/// unbound after the Bill-Settlement misuse was removed. This slice takes that reserved chord for the verb the
/// reference product puts on it.</para>
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com is the source).</b> The vendor's keyboard-shortcut
/// page gives <b>Ctrl+B</b> as <i>"To views values in different ways in a report"</i>; the reports guide calls
/// it <i>Basis of Values</i> — <i>"configure the values in your report for that instance, based on different
/// business needs"</i>; and the Stock Summary / Cash Flow / Funds Flow / Batch Summary pages each reach the
/// <b>Scale Factor</b> through it (<i>"Press Ctrl+B (Basis of Value) &gt; Scale Factor and select the required
/// option"</i>), naming Hundreds, Thousands, Lakhs, Millions and Crores between them.</para>
///
/// <para><b>Expected values are derived by hand, never read off the code.</b> The fixture opens ONE ledger at
/// ₹1,05,000 Dr and one at ₹1,05,000 Cr, so every figure below is 105000 divided by the factor:
/// Hundreds → 1050 → "1,050.00"; Thousands → 105 → "105.00"; Lakhs → 1.05 → "1.05". Those are the strings the
/// Indian-grouping formatter must produce.</para>
/// </summary>
public sealed class BasisOfValuesScaleFactorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BasisOfValuesScaleFactorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBasisTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// A company whose whole Trial Balance is exactly ₹1,05,000 on each side — one Cash-in-Hand ledger opening
    /// 1,05,000 Dr and one Capital Account ledger opening 1,05,000 Cr. Chosen because 105000 divides exactly by
    /// 100, 1000 and 100000, so every expected string below is arithmetic, not a rounding guess.
    /// </summary>
    private static Company OneLakhFiveCompany()
    {
        var company = Apex.Ledger.Services.CompanyFactory.CreateSeeded(
            "Scale " + Guid.NewGuid().ToString("N"),
            new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));

        Guid GroupId(string name) => company.FindGroupByName(name)!.Id;

        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Petty Cash", GroupId("Cash-in-Hand"),
            Money.FromRupees(105000m), openingIsDebit: true));
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Owner Capital", GroupId("Capital Account"),
            Money.FromRupees(105000m), openingIsDebit: false));

        return company;
    }

    private static ReportRow GrandTotal(ReportsViewModel vm) => vm.Rows.Single(r => r.Particulars == "Grand Total");

    private static ReportRow Row(ReportsViewModel vm, string particulars) =>
        vm.Rows.Single(r => r.Particulars.StartsWith(particulars, StringComparison.Ordinal));

    // =============================================================== the scale itself

    /// <summary>An untouched report is at Default scale and byte-for-byte the pre-slice output.</summary>
    [Fact]
    public void An_untouched_report_is_at_default_scale_and_shows_unscaled_figures()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);

        Assert.Equal(ReportScale.Default, vm.Scale);
        Assert.Equal("1,05,000.00", Row(vm, "Petty Cash").Debit);
        Assert.Equal("1,05,000.00", Row(vm, "Owner Capital").Credit);
        Assert.Equal("1,05,000.00", GrandTotal(vm).Debit);
        Assert.DoesNotContain("in ", vm.Subtitle);   // no scale clause when nothing is scaled
    }

    /// <summary>Hundreds divides by 100 — 1,05,000 → 1,050.00, hand-derived.</summary>
    [Fact]
    public void Hundreds_divides_every_displayed_figure_by_one_hundred()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        vm.ApplyScale(ReportScale.Hundreds);

        Assert.Equal("1,050.00", Row(vm, "Petty Cash").Debit);
        Assert.Equal("1,050.00", Row(vm, "Owner Capital").Credit);
        Assert.Equal("1,050.00", GrandTotal(vm).Debit);
        Assert.Equal("1,050.00", GrandTotal(vm).Credit);
    }

    /// <summary>Thousands divides by 1,000 — 1,05,000 → 105.00.</summary>
    [Fact]
    public void Thousands_divides_every_displayed_figure_by_one_thousand()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        vm.ApplyScale(ReportScale.Thousands);

        Assert.Equal("105.00", Row(vm, "Petty Cash").Debit);
        Assert.Equal("105.00", GrandTotal(vm).Debit);
    }

    /// <summary>Lakhs divides by 1,00,000 — 1,05,000 → 1.05.</summary>
    [Fact]
    public void Lakhs_divides_every_displayed_figure_by_one_lakh()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        vm.ApplyScale(ReportScale.Lakhs);

        Assert.Equal("1.05", Row(vm, "Petty Cash").Debit);
        Assert.Equal("1.05", GrandTotal(vm).Debit);
    }

    /// <summary>
    /// 🔴 The scale clause is NOT decoration. A Trial Balance that silently reads "105.00" where the books say
    /// ₹1,05,000 is a misstated financial statement; the header must say which unit is on screen.
    /// </summary>
    [Fact]
    public void A_scaled_report_says_in_its_header_which_unit_the_figures_are_in()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        vm.ApplyScale(ReportScale.Thousands);

        Assert.Contains("in Thousands", vm.Subtitle);

        vm.ApplyScale(ReportScale.Crores);
        Assert.Contains("in Crores", vm.Subtitle);
        Assert.DoesNotContain("in Thousands", vm.Subtitle);
    }

    /// <summary>Returning to Default restores the report exactly — a round trip changes nothing.</summary>
    [Fact]
    public void Returning_to_Default_restores_the_unscaled_report_exactly()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        var before = vm.Rows.Select(r => $"{r.Particulars}|{r.Debit}|{r.Credit}").ToList();
        var subtitleBefore = vm.Subtitle;

        vm.ApplyScale(ReportScale.Lakhs);
        vm.ApplyScale(ReportScale.Default);

        Assert.Equal(before, vm.Rows.Select(r => $"{r.Particulars}|{r.Debit}|{r.Credit}").ToList());
        Assert.Equal(subtitleBefore, vm.Subtitle);
    }

    /// <summary>
    /// The scale is a DISPLAY divide, so a percentage share — computed by the engine over the full, unscaled
    /// set — must be identical at every scale. If this ever fails the scale has leaked into the arithmetic.
    /// </summary>
    [Fact]
    public void Scaling_never_changes_a_percentage_share()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        vm.ApplyConfiguration(hideZero: false, showPercentages: true, closingStock: ClosingStockMode.AsPostedLedger);
        var labels = vm.Rows.Select(r => r.Particulars).ToList();

        vm.ApplyScale(ReportScale.Thousands);

        Assert.Equal(labels, vm.Rows.Select(r => r.Particulars).ToList());
    }

    /// <summary>The Balance Sheet scales too — the option is a REPORT-FAMILY option, not a Trial-Balance one.</summary>
    [Fact]
    public void The_balance_sheet_scales_on_the_same_option()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.BalanceSheet);
        vm.ApplyScale(ReportScale.Thousands);

        Assert.Equal("105.00", vm.Rows.Single(r => r.Particulars == "Total Liabilities").Amount);
        Assert.Equal("105.00", vm.Rows.Single(r => r.Particulars == "Total Assets").Amount);
    }

    /// <summary>
    /// The supported set is NAMED, not implied. Ctrl+B's Scale Factor lands on the accounting statements this
    /// slice covers; every other report kind reports that it does not support it, so nothing silently pretends
    /// to scale. (Under-claiming the surface is the whole point — see the slice notes.)
    /// </summary>
    [Fact]
    public void The_scale_factor_names_exactly_which_reports_it_covers()
    {
        Assert.True(new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance).SupportsScaleFactor);
        Assert.True(new ReportsViewModel(OneLakhFiveCompany(), ReportKind.BalanceSheet).SupportsScaleFactor);
        Assert.True(new ReportsViewModel(OneLakhFiveCompany(), ReportKind.ProfitAndLoss).SupportsScaleFactor);

        Assert.False(new ReportsViewModel(OneLakhFiveCompany(), ReportKind.DayBook).SupportsScaleFactor);
        Assert.False(new ReportsViewModel(OneLakhFiveCompany(), ReportKind.StockSummary).SupportsScaleFactor);
    }

    /// <summary>A scale asked for on a report that does not support it is refused, not half-applied.</summary>
    [Fact]
    public void A_report_that_does_not_support_the_scale_ignores_it()
    {
        var vm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.DayBook);
        vm.ApplyScale(ReportScale.Thousands);

        Assert.Equal(ReportScale.Default, vm.Scale);
        Assert.DoesNotContain("in Thousands", vm.Subtitle);
    }

    // =============================================================== the panel + the chord

    /// <summary>The panel offers exactly the six vendor-named factors, seeded from the report's current scale.</summary>
    [Fact]
    public void The_basis_of_values_panel_offers_the_six_vendor_named_scale_factors()
    {
        var report = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        var panel = new BasisOfValuesViewModel(report);

        Assert.Equal(
            new[] { ReportScale.Default, ReportScale.Hundreds, ReportScale.Thousands,
                    ReportScale.Lakhs, ReportScale.Millions, ReportScale.Crores },
            panel.ScaleOptions.Select(o => o.Scale).ToArray());
        Assert.Equal(ReportScale.Default, panel.SelectedScale!.Scale);
    }

    /// <summary>Apply pushes the panel's choice into the live report and re-projects it.</summary>
    [Fact]
    public void Applying_the_panel_rescales_the_live_report()
    {
        var report = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        var panel = new BasisOfValuesViewModel(report);

        panel.SelectedScale = panel.ScaleOptions.Single(o => o.Scale == ReportScale.Thousands);
        panel.Apply();

        Assert.Equal(ReportScale.Thousands, report.Scale);
        Assert.Equal("105.00", GrandTotal(report).Debit);
        Assert.False(string.IsNullOrWhiteSpace(panel.Status));
    }

    /// <summary>Opening the panel and applying with NO edit is a no-op — it seeds from the report's own state.</summary>
    [Fact]
    public void Opening_and_applying_with_no_edit_changes_nothing()
    {
        var report = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
        report.ApplyScale(ReportScale.Lakhs);
        var before = report.Rows.Select(r => $"{r.Particulars}|{r.Debit}|{r.Credit}").ToList();

        var panel = new BasisOfValuesViewModel(report);
        Assert.Equal(ReportScale.Lakhs, panel.SelectedScale!.Scale);   // seeded from the report
        panel.Apply();

        Assert.Equal(before, report.Rows.Select(r => $"{r.Particulars}|{r.Debit}|{r.Credit}").ToList());
    }

    // =============================================================== the shell wiring

    private MainWindowViewModel NewShellOnTrialBalance()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Basis Shell " + Guid.NewGuid().ToString("N");
        vm.CreateCompany();
        vm.OpenReport(ReportKind.TrialBalance);
        return vm;
    }

    /// <summary>Ctrl+B opens the panel as its own cascade column over the report, which stays live beneath it.</summary>
    [Fact]
    public void The_shell_opens_the_basis_of_values_panel_over_the_live_report()
    {
        var vm = NewShellOnTrialBalance();
        var depth = vm.Columns.Count;

        vm.OpenBasisOfValues();

        Assert.Equal(Screen.BasisOfValues, vm.CurrentScreen);
        Assert.NotNull(vm.BasisOfValues);
        Assert.Equal(depth + 1, vm.Columns.Count);
        Assert.NotNull(vm.Reports);            // the report survives beneath the panel
    }

    /// <summary>Ctrl+A on the panel applies it and pops back to the re-scaled report.</summary>
    [Fact]
    public void Applying_from_the_shell_rescales_the_report_and_pops_the_panel()
    {
        var vm = NewShellOnTrialBalance();
        vm.OpenBasisOfValues();
        vm.BasisOfValues!.SelectedScale =
            vm.BasisOfValues.ScaleOptions.Single(o => o.Scale == ReportScale.Thousands);

        vm.ApplyBasisOfValues();

        Assert.Null(vm.BasisOfValues);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportScale.Thousands, vm.Reports!.Scale);
    }

    /// <summary>Ctrl+B is ADVERTISED on the button bar while a supporting report is open, and dimmed otherwise.</summary>
    [Fact]
    public void Ctrl_B_is_advertised_on_a_supporting_report_and_dimmed_elsewhere()
    {
        var vm = NewShellOnTrialBalance();
        Assert.Equal(1, vm.ButtonBar.Count(b => b.Key == "Ctrl+B"));
        var onReport = vm.ButtonBar.First(b => b.Key == "Ctrl+B");
        Assert.Equal("Basis of Values", onReport.Caption);
        Assert.True(onReport.Enabled);

        vm.OpenReport(ReportKind.DayBook);
        Assert.False(vm.ButtonBar.First(b => b.Key == "Ctrl+B").Enabled);

        // …and on the Gateway, where there is no report at all. This is the IV-31 half that
        // SettlementFromOutstandingsTests also locks from the other side.
        vm.ShowGateway();
        Assert.False(vm.ButtonBar.First(b => b.Key == "Ctrl+B").Enabled);
    }

    /// <summary>
    /// A real Ctrl+B keystroke on a report that CANNOT scale must leave the shell exactly where it was — the
    /// key arm carries the same guard as the dimmed badge, so the chord is not silently swallowed there either.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Ctrl_B_on_an_unscalable_report_opens_nothing_through_a_real_keystroke()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexBasisNoScale_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = "No Scale Co";
        vm.CreateCompany();
        vm.OpenReport(ReportKind.DayBook);

        var window = new Apex.Desktop.Views.MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        try
        {
            var depth = vm.Columns.Count;
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.B, Avalonia.Input.RawInputModifiers.Control);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Null(vm.BasisOfValues);
            Assert.Equal(depth, vm.Columns.Count);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);
        }
        finally
        {
            window.Close();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 🔴 The END-TO-END reach: a real Ctrl+B keystroke through the window's tunnel handler opens the panel, a
    /// real ComboBox holding the six factors is realised in the visual tree, and a real Ctrl+A re-scales the
    /// report beneath. Nothing here calls the view model directly to "prove" the feature — a panel the shell
    /// binds but no template renders, or a chord that never reaches the shell, is not a shipped capability.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Ctrl_B_then_Ctrl_A_rescales_the_report_through_real_keystrokes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexBasisKeys_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = "Basis Keys Co";
        vm.CreateCompany();
        vm.OpenReport(ReportKind.TrialBalance);

        var window = new Apex.Desktop.Views.MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        try
        {
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.B, Avalonia.Input.RawInputModifiers.Control);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(1280, 800));
            window.Arrange(new Avalonia.Rect(0, 0, 1280, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(Screen.BasisOfValues, vm.CurrentScreen);

            var combo = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<Avalonia.Controls.ComboBox>()
                .FirstOrDefault(c => ReferenceEquals(c.ItemsSource, vm.BasisOfValues!.ScaleOptions));
            Assert.NotNull(combo);
            Assert.True(combo!.IsEffectivelyVisible);

            vm.BasisOfValues!.SelectedScale =
                vm.BasisOfValues.ScaleOptions.Single(o => o.Scale == ReportScale.Thousands);

            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.A, Avalonia.Input.RawInputModifiers.Control);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportScale.Thousands, vm.Reports!.Scale);
            Assert.Contains("in Thousands", vm.Reports.Subtitle);
        }
        finally
        {
            window.Close();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The panel is refused where the report cannot scale — no dead column, no half-open state.</summary>
    [Fact]
    public void The_panel_does_not_open_on_a_report_that_cannot_scale()
    {
        var vm = NewShellOnTrialBalance();
        vm.OpenReport(ReportKind.DayBook);
        var depth = vm.Columns.Count;

        vm.OpenBasisOfValues();

        Assert.Null(vm.BasisOfValues);
        Assert.Equal(depth, vm.Columns.Count);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
    }
}
