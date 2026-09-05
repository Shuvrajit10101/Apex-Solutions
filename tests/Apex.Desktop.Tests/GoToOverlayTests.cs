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
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W2-14 / census row 14.1 LOCK — Go To (Alt+G) exists, and a user can actually reach every destination
/// on it with the keyboard alone.</b>
///
/// <para><b>The defect these lock.</b> The census recorded row 14.1 as ABSENT on the strength of
/// <c>Key.G</c> returning <b>zero occurrences</b> across the whole of <c>src/Apex.Desktop</c> — no view model,
/// no <c>Screen</c> member, no menu row, no key arm. Every screen in this product is reachable only by walking
/// the Miller cascade from the Gateway; there was no way to jump.</para>
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com is the source).</b> The vendor's keyboard-shortcut
/// page gives <b>Alt+G</b> as <i>"To primarily open a report, and create masters and vouchers in the flow of
/// work"</i>, and <b>Ctrl+G</b> as a DIFFERENT verb — <i>"To switch to a different report…"</i>. Only Alt+G is
/// built here; Ctrl+G (census row 14.2, Switch To) is left alone, and 14.2 stays ABSENT.</para>
///
/// <para><b>Why these tests are written this way.</b> The project's standing rule is that a view model with no
/// keystroke and no button behind it is not a shipped capability. So the reach assertions drive a <b>real
/// keystroke</b> through <see cref="MainWindow"/>'s tunnel handler and then assert the shell actually changed
/// screen — nothing here calls the overlay directly to "prove" it works.</para>
/// </summary>
public sealed class GoToOverlayTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexGoTo_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Alt+G — the Go To chord — driven as a real keystroke through the window's tunnel handler.</summary>
    private static void PressGoTo(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ============================================================ the chord

    /// <summary>Alt+G opens Go To from the Gateway — the headline of row 14.1.</summary>
    [AvaloniaFact]
    public void Alt_G_opens_the_Go_To_overlay_from_the_Gateway()
    {
        var (window, vm, dir) = NewWindow("Go To Co");
        try
        {
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.Null(vm.GoTo);

            PressGoTo(window);

            Assert.Equal(Screen.GoTo, vm.CurrentScreen);
            Assert.NotNull(vm.GoTo);
            Assert.NotEmpty(vm.GoTo!.Results);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// Alt+G works from a REPORT too — "in the flow of work" is the vendor's own phrase, and a jump chord that
    /// only fires on the Gateway is the cascade walk with extra steps.
    /// </summary>
    [AvaloniaFact]
    public void Alt_G_opens_Go_To_from_an_open_report_not_only_from_the_Gateway()
    {
        var (window, vm, dir) = NewWindow("Flow Of Work Co");
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            PressGoTo(window);

            Assert.Equal(Screen.GoTo, vm.CurrentScreen);
            Assert.NotNull(vm.GoTo);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A second Alt+G must not stack a second overlay column.</summary>
    [AvaloniaFact]
    public void Pressing_Alt_G_twice_does_not_stack_two_overlays()
    {
        var (window, vm, dir) = NewWindow("No Stack Co");
        try
        {
            PressGoTo(window);
            var depth = vm.Columns.Count;
            PressGoTo(window);

            Assert.Equal(depth, vm.Columns.Count);
            Assert.Equal(Screen.GoTo, vm.CurrentScreen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The panel is REALISED — a live query box and a live result list exist in the visual tree after Alt+G.
    /// A view model the shell sets but no template renders is the "service method with no caller" defect with
    /// an extra step, so this asserts against the real control tree rather than against the view model.
    /// </summary>
    [AvaloniaFact]
    public void The_Go_To_panel_renders_a_real_query_box_and_a_real_result_list()
    {
        var (window, vm, dir) = NewWindow("Realised Co");
        try
        {
            PressGoTo(window);
            Pump(window);

            var queryBox = window.GetVisualDescendants().OfType<TextBox>()
                                 .FirstOrDefault(b => b.Classes.Contains("go-to-query"));
            Assert.NotNull(queryBox);
            Assert.True(queryBox!.IsEffectivelyVisible);

            var list = window.GetVisualDescendants().OfType<ListBox>()
                             .FirstOrDefault(l => l.ItemsSource is System.Collections.IEnumerable src
                                                  && ReferenceEquals(src, vm.GoTo!.Results));
            Assert.NotNull(list);
            Assert.True(list!.IsEffectivelyVisible);

            var goButton = window.GetVisualDescendants().OfType<Button>()
                                 .FirstOrDefault(b => b.Content is string s
                                                      && s.StartsWith("Go", StringComparison.Ordinal));
            Assert.NotNull(goButton);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+G is ADVERTISED on the button bar — a chord nobody can find is not a feature.</summary>
    [AvaloniaFact]
    public void Alt_G_is_advertised_on_the_button_bar()
    {
        var (window, vm, dir) = NewWindow("Advertised Co");
        try
        {
            Assert.Equal(1, vm.ButtonBar.Count(b => b.Key == "Alt+G"));
            var row = vm.ButtonBar.First(b => b.Key == "Alt+G");
            Assert.Equal("Go To", row.Caption);
            Assert.True(row.Enabled);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ the index

    /// <summary>
    /// Every destination is nested under a parent section — the standing UI contract is "never a flat dump", and
    /// the sections are the Gateway root's OWN headers so the two surfaces cannot disagree.
    /// </summary>
    [AvaloniaFact]
    public void Every_Go_To_destination_is_nested_under_a_parent_section()
    {
        var (window, vm, dir) = NewWindow("Sections Co");
        try
        {
            PressGoTo(window);
            var all = vm.GoTo!.All;

            Assert.All(all, d => Assert.False(string.IsNullOrWhiteSpace(d.Section)));

            var sections = all.Select(d => d.Section).Distinct().ToList();
            Assert.Contains("Masters", sections);
            Assert.Contains("Transactions", sections);
            Assert.Contains("Reports", sections);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The index carries no duplicate label within a section — a Go To list with two "Ledger" rows that
    /// go to different places is worse than no Go To at all.</summary>
    [AvaloniaFact]
    public void The_Go_To_index_has_no_duplicate_entries()
    {
        var (window, vm, dir) = NewWindow("Unique Co");
        try
        {
            PressGoTo(window);
            var keys = vm.GoTo!.All.Select(d => d.Section + "/" + d.Label).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ type-to-filter

    /// <summary>
    /// Typing filters by WORD PREFIX, case-insensitively — the settled keyboard contract for this product is
    /// prefix filtering with the typed text visible, not substring search. "bal" therefore reaches BOTH
    /// "Balance Sheet" (whole-label prefix) and "Trial Balance" (second-word prefix), and does NOT reach
    /// "Payment Advice" — no word there begins with "bal".
    /// </summary>
    [AvaloniaFact]
    public void Typing_filters_the_index_by_word_prefix()
    {
        var (window, vm, dir) = NewWindow("Filter Co");
        try
        {
            PressGoTo(window);
            var go = vm.GoTo!;

            go.Query = "bal";
            var labels = go.Results.Select(r => r.Label).ToList();
            Assert.Contains("Balance Sheet", labels);
            Assert.Contains("Trial Balance", labels);
            Assert.DoesNotContain("Day Book", labels);

            go.Query = "BAL";                     // case-insensitive
            Assert.Equal(labels.Count, go.Results.Count);

            go.Query = "alance";                  // mid-word — prefix filtering, so no match
            Assert.Empty(go.Results);

            go.Query = "";                        // cleared — the whole index comes back
            Assert.Equal(go.All.Count, go.Results.Count);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A query that matches nothing leaves an empty list and says so, rather than silently showing all.</summary>
    [AvaloniaFact]
    public void A_query_that_matches_nothing_yields_an_empty_result_list()
    {
        var (window, vm, dir) = NewWindow("No Match Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "zzzznotathing";
            Assert.Empty(vm.GoTo.Results);
            Assert.Null(vm.GoTo.Selected);
            Assert.False(string.IsNullOrWhiteSpace(vm.GoTo.Status));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Filtering always leaves a highlighted row, so Enter never fires into nothing.</summary>
    [AvaloniaFact]
    public void Filtering_keeps_the_first_result_highlighted()
    {
        var (window, vm, dir) = NewWindow("Highlight Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "trial";
            Assert.NotNull(vm.GoTo.Selected);
            Assert.Same(vm.GoTo.Results[0], vm.GoTo.Selected);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ it actually goes somewhere

    /// <summary>
    /// The whole row: type, choose, and the shell is ON that screen. A Go To that lists destinations but does
    /// not travel to them is the "432 lines of print code with zero references" failure in another costume.
    /// </summary>
    [AvaloniaFact]
    public void Choosing_a_report_destination_opens_that_report()
    {
        var (window, vm, dir) = NewWindow("Travel Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "trial balance";
            vm.RunSelectedGoTo();
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
            Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);
            Assert.Null(vm.GoTo);            // the overlay closed behind the jump
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>…and to a MASTER, which is the half of the vendor sentence a report-only jump would drop.</summary>
    [AvaloniaFact]
    public void Choosing_a_master_destination_opens_that_master()
    {
        var (window, vm, dir) = NewWindow("Master Travel Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "stock item";
            vm.RunSelectedGoTo();
            Pump(window);

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.NotNull(vm.StockItemMaster);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>…and to a VOUCHER, the third verb in the vendor's sentence.</summary>
    [AvaloniaFact]
    public void Choosing_a_voucher_destination_opens_that_voucher_entry()
    {
        var (window, vm, dir) = NewWindow("Voucher Travel Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "payment";
            var payment = vm.GoTo.Results.First(r => r.Label == "Payment" && r.Section == "Transactions");
            vm.GoTo.Selected = payment;
            vm.RunSelectedGoTo();
            Pump(window);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherEntry);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Enter drives the SAME door as the button — key and button must never do two different things
    /// (this file's shell has been bitten by exactly that before).</summary>
    [AvaloniaFact]
    public void Enter_on_the_overlay_travels_the_same_route_as_the_button()
    {
        var (window, vm, dir) = NewWindow("Enter Co");
        try
        {
            PressGoTo(window);
            vm.GoTo!.Query = "balance sheet";
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Esc abandons the jump and leaves the surface underneath exactly as it was.</summary>
    [AvaloniaFact]
    public void Escape_abandons_Go_To_and_restores_the_surface_underneath()
    {
        var (window, vm, dir) = NewWindow("Escape Co");
        try
        {
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            var depth = vm.Columns.Count;

            PressGoTo(window);
            Assert.Equal(Screen.GoTo, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Pump(window);

            Assert.Null(vm.GoTo);
            Assert.Equal(depth, vm.Columns.Count);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ feature gating

    /// <summary>
    /// A destination whose feature is switched OFF must not be listed. Payroll is off on a fresh company, so
    /// the payroll reports are absent from the index — a Go To that offers a dead door is a worse lie than a
    /// menu that omits it, because the user typed the name and got nothing.
    /// </summary>
    [AvaloniaFact]
    public void Feature_gated_destinations_are_absent_until_their_feature_is_on()
    {
        var (window, vm, dir) = NewWindow("Gated Co");
        try
        {
            Assert.False(vm.Company!.PayrollEnabled);

            PressGoTo(window);
            var labels = vm.GoTo!.All.Select(d => d.Label).ToList();
            Assert.DoesNotContain("Pay Sheet", labels);
            Assert.DoesNotContain("Payslip", labels);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
