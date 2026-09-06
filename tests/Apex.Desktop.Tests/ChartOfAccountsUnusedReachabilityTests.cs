using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 2.13 — the LAST mile for the derived "Unused" view: a chord an operator can press and a realised
/// control they can click.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS AND WHY IT REALISES THE REAL WINDOW.</b> <c>UnusedMastersTests</c> pins the
/// predicate and would stay green if <c>Ctrl+J</c> were never bound, if <c>ExceptionReportsViewModel</c> were never
/// templated, or if the two radios were deleted from <c>MainWindow.axaml</c> — leaving a correct, tested
/// engine that <b>no user can reach</b>. That is the exact shape this project keeps re-finding
/// (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>: written, correct, tested, called by
/// nobody). So every test below drives the shipped <see cref="MainWindow"/> through real keystrokes and reads the
/// realised visual tree, never a view-model flag on its own.</para>
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com, <c>/tally-prime/charts-of-accounts-tally/</c>).</b>
/// The route and the chord are the vendor's: <c>Ctrl+J</c> (Exception Reports) &gt; "Show Unused" produces the
/// "List of Ledgers (Unused)". The panel's two-radio shape and the pruning of empty heads are OURS (ruling 9).</para>
/// </summary>
public sealed class ChartOfAccountsUnusedReachabilityTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The shipped window on a real, populated company with the Chart of Accounts genuinely open.</summary>
    private static MainWindow OpenChart(out MainWindowViewModel vm, string dir)
    {
        vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        vm.ShowChartOfAccounts();
        Assert.True(vm.ChartOfAccounts is not null,
            "ShowChartOfAccounts() produced no page — the route itself is broken, so nothing below is meaningful.");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

    private static void Cleanup(Window? window, string dir)
    {
        window?.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string TempDir(string tag) => Path.Combine(Path.GetTempPath(), tag + Guid.NewGuid().ToString("N"));

    // ------------------------------------------------------------------ the chord reaches the shell

    /// <summary>A real Ctrl+J on the Chart of Accounts opens the Exception Reports column. Fails on today's main:
    /// the chord is unbound and the screen does not exist.</summary>
    [AvaloniaFact]
    public void Ctrl_J_on_the_chart_opens_the_exception_column()
    {
        var dir = TempDir("ApexUnusedOpen_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            var depth = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.ExceptionReports, vm.CurrentScreen);
            Assert.NotNull(vm.ExceptionReports);
            Assert.Equal(depth + 1, vm.Columns.Count);
            Assert.NotNull(vm.Columns[^1].ExceptionReports);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Ctrl+J off the Chart of Accounts must NOT be swallowed and must open nothing — the key arm carries the same
    /// guard as <c>OpenExceptionReports</c>, so a chord that fires nowhere is never consumed.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_J_off_the_chart_opens_nothing()
    {
        var dir = TempDir("ApexUnusedElsewhere_");
        MainWindow? window = null;
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);

            window = new MainWindow { DataContext = vm };
            window.Show();
            Pump(window);

            var depth = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);

            Assert.Null(vm.ExceptionReports);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(depth, vm.Columns.Count);
        }
        finally { Cleanup(window, dir); }
    }

    // ------------------------------------------------------------------ the realised controls

    private static RadioButton[] RealisedViewRadios(Window window)
        => window.GetVisualDescendants()
                 .OfType<RadioButton>()
                 .Where(r => r.GroupName == "ExceptionReportView")
                 .ToArray();

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> The realised panel offers a clickable choice for each of the two
    /// views. Deleting either radio from <c>MainWindow.axaml</c> makes the view unreachable while every
    /// view-model test stays green; only this bites.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_panel_offers_both_views()
    {
        var dir = TempDir("ApexUnusedRadios_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);

            var radios = RealisedViewRadios(window);

            // Non-vacuity first: with an empty scan every Contains below would pass on an empty premise.
            Assert.True(radios.Length == 2,
                $"the realised Exception Reports panel offers {radios.Length} view radio(s) "
              + $"({string.Join(" | ", radios.Select(r => r.Content as string ?? "?"))}) — expected exactly 2.");

            foreach (var expected in new[] { "All groups and ledgers", "Show Unused" })
                Assert.True(radios.Any(r => string.Equals(r.Content as string, expected, StringComparison.Ordinal)),
                    $"no realised radio offers '{expected}'. The predicate may exist in Apex.Ledger, but with no "
                  + $"control bound to it the view is unreachable and census row 2.13 has not moved. Realised "
                  + $"captions were: {string.Join(" | ", radios.Select(r => r.Content as string ?? "?"))}");

            Assert.All(radios, r => Assert.True(r.IsEffectivelyVisible, "a radio that is not visible is not clickable."));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE END-TO-END REACH.</b> A real Ctrl+J, a real click on the realised "Show Unused" radio, a real
    /// Ctrl+A — and the tree beneath genuinely narrows to unused ledgers and takes the vendor's caption. Nothing
    /// here sets <c>ShowUnusedOnly</c> directly: a radio that renders but is bound to nothing would satisfy the
    /// caption test above, and only driving it proves the wire.
    /// </summary>
    [AvaloniaFact]
    public void Show_Unused_filters_the_tree_to_unused_ledgers_and_renames_the_pane()
    {
        var dir = TempDir("ApexUnusedDrive_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            var company = vm.Company!;
            var chart = vm.ChartOfAccounts!;

            // Non-vacuity premise: this fixture must hold BOTH kinds of ledger, or "the list got shorter" is
            // not evidence of anything.
            Assert.Contains(company.Ledgers, l => UnusedMasters.IsLedgerUnused(company, l));
            Assert.Contains(company.Ledgers, l => !UnusedMasters.IsLedgerUnused(company, l));

            var fullLedgerRows = chart.Rows.Count(r => r.LedgerId is not null);
            Assert.Equal("Chart of Accounts", chart.Title);

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);

            var showUnused = RealisedViewRadios(window)
                .Single(r => string.Equals(r.Content as string, "Show Unused", StringComparison.Ordinal));
            showUnused.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.ExceptionReports!.IsShowUnused,
                "checking the realised radio did not move the view model — the radio renders but is bound to nothing.");

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            // Back on the tree, filtered and re-captioned.
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
            Assert.True(chart.ShowUnusedOnly);
            Assert.Equal("List of Ledgers (Unused)", chart.Title);
            Assert.Equal("List of Ledgers (Unused)", vm.ScreenTitle);

            var shownLedgers = chart.Rows.Where(r => r.LedgerId is not null).ToList();
            Assert.True(shownLedgers.Count > 0, "the fixture has unused ledgers, so the filtered pane must not be empty.");
            Assert.True(shownLedgers.Count < fullLedgerRows,
                $"the filter removed nothing: {shownLedgers.Count} of {fullLedgerRows} ledger rows survived.");

            // EVERY surviving ledger row is genuinely unused — the filter is not merely "shorter".
            foreach (var row in shownLedgers)
            {
                var ledger = company.Ledgers.Single(l => l.Id == row.LedgerId!.Value);
                Assert.True(UnusedMasters.IsLedgerUnused(company, ledger),
                    $"'{ledger.Name}' is listed under 'List of Ledgers (Unused)' but has been transacted with.");
            }

            // No head survives with nothing unused beneath it (the pruning half).
            for (var i = 0; i < chart.Rows.Count; i++)
                if (chart.Rows[i].IsGroup)
                    Assert.True(i + 1 < chart.Rows.Count && chart.Rows[i + 1].Depth > chart.Rows[i].Depth,
                        $"group row '{chart.Rows[i].Name}' survived the filter with nothing beneath it.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Choosing "All groups and ledgers" again restores the full tree and the ordinary caption — the
    /// filter is a view, and leaving it must cost nothing.</summary>
    [AvaloniaFact]
    public void Choosing_all_masters_again_restores_the_full_tree()
    {
        var dir = TempDir("ApexUnusedRestore_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            var chart = vm.ChartOfAccounts!;
            var fullRows = chart.Rows.Count;

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);
            RealisedViewRadios(window)
                .Single(r => string.Equals(r.Content as string, "Show Unused", StringComparison.Ordinal))
                .IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.True(chart.Rows.Count < fullRows, "premise: the filter narrowed the tree.");

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Pump(window);
            RealisedViewRadios(window)
                .Single(r => string.Equals(r.Content as string, "All groups and ledgers", StringComparison.Ordinal))
                .IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.False(chart.ShowUnusedOnly);
            Assert.Equal("Chart of Accounts", chart.Title);
            Assert.Equal(fullRows, chart.Rows.Count);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The "nothing survived the filter" notice must be RENDERED, not merely computed.</b> The view model
    /// carried <c>ShowsEmptyUnusedNotice</c> with a doc comment promising the pane "must SAY so rather than render
    /// an empty box the operator reads as a broken screen" — and nothing in <c>MainWindow.axaml</c> was bound to
    /// it, so the promise was not kept. This test pins the binding target's existence and that its visibility
    /// genuinely follows the property; the truth table of the property itself is
    /// <see cref="The_empty_notice_is_shown_only_when_the_filter_survives_nothing"/>.
    /// </summary>
    [AvaloniaFact]
    public void The_empty_unused_notice_is_realised_and_tracks_the_view_model()
    {
        var dir = TempDir("ApexUnusedEmptyNotice_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            var chart = vm.ChartOfAccounts!;

            var notice = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text is { } s
                                  && s.Contains("nothing is unused", StringComparison.Ordinal));

            Assert.True(notice is not null,
                "the empty-Unused notice is not in the realised tree at all. ShowsEmptyUnusedNotice would then be "
              + "a dead property and an operator whose filter matches nothing sees a blank white pane they cannot "
              + "tell from a broken screen.");

            // The demo book HAS unused ledgers, so the notice must be computed false and rendered hidden. If the
            // binding were missing the TextBlock would sit visible over the tree in every state.
            Assert.False(chart.ShowsEmptyUnusedNotice, "premise: this fixture has unused ledgers.");
            Assert.False(notice!.IsEffectivelyVisible,
                "the notice is visible while the pane has rows — the IsVisible binding is missing or inverted.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>The notice's truth table: it appears only with the filter ON and nothing left to show, and never
    /// on the ordinary unfiltered tree.</summary>
    [AvaloniaFact]
    public void The_empty_notice_is_shown_only_when_the_filter_survives_nothing()
    {
        var dir = TempDir("ApexUnusedEmptyTruth_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            var chart = vm.ChartOfAccounts!;

            Assert.False(chart.ShowsEmptyUnusedNotice);          // filter off

            chart.ShowUnusedOnly = true;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(chart.Rows);
            Assert.False(chart.ShowsEmptyUnusedNotice);          // filter on, rows survived

            // Now genuinely reach the third state: transact against every remaining unused ledger, so nothing is
            // unused any more. Posted through the real engine, so "used" means what the predicate means.
            var company = vm.Company!;
            var svc = new Apex.Ledger.Services.LedgerService(company);
            var journal = company.FindVoucherTypeByName("Journal")!;
            var counter = company.FindLedgerByName("Cash")!;
            var day = company.BooksBeginFrom;

            foreach (var l in company.Ledgers.Where(l => UnusedMasters.IsLedgerUnused(company, l)).ToList())
            {
                if (l.Id == counter.Id) continue;
                svc.Post(new Apex.Ledger.Domain.Voucher(Guid.NewGuid(), journal.Id, day, new[]
                {
                    new Apex.Ledger.Domain.EntryLine(l.Id, Apex.Ledger.Money.FromRupees(1m),
                        Apex.Ledger.DrCr.Debit),
                    new Apex.Ledger.Domain.EntryLine(counter.Id, Apex.Ledger.Money.FromRupees(1m),
                        Apex.Ledger.DrCr.Credit),
                }));
            }

            Assert.DoesNotContain(company.Ledgers, l => UnusedMasters.IsLedgerUnused(company, l));

            chart.Refresh();
            Dispatcher.UIThread.RunJobs();
            Pump(window);

            Assert.Empty(chart.Rows);
            Assert.True(chart.ShowsEmptyUnusedNotice,
                "with the filter on and nothing unused, the pane must state it — otherwise the operator sees a "
              + "blank white box they cannot tell from a broken screen.");

            var notice = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text is { } s && s.Contains("nothing is unused", StringComparison.Ordinal));
            Assert.True(notice.IsEffectivelyVisible, "the notice is computed but not rendered.");

            // …and turning the filter off restores the full tree and hides the notice again.
            chart.ShowUnusedOnly = false;
            Dispatcher.UIThread.RunJobs();
            Pump(window);
            Assert.NotEmpty(chart.Rows);
            Assert.False(chart.ShowsEmptyUnusedNotice);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>The Ctrl+J badge is enabled on exactly the screen the key arm fires on, and dimmed elsewhere — an
    /// enabled badge that fires nothing is register defect IV-31.</summary>
    [AvaloniaFact]
    public void The_button_bar_badge_agrees_with_the_key_arm()
    {
        var dir = TempDir("ApexUnusedBadge_");
        MainWindow? window = null;
        try
        {
            window = OpenChart(out var vm, dir);
            Assert.True(vm.ButtonBar.First(b => b.Key == "Ctrl+J").Enabled,
                "the badge must be live where the chord fires.");

            vm.ShowGateway();
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.ButtonBar.First(b => b.Key == "Ctrl+J").Enabled,
                "the badge must be dimmed on the Gateway, where the chord opens nothing.");
        }
        finally { Cleanup(window, dir); }
    }
}
