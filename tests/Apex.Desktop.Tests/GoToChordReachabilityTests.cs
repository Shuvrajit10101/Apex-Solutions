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
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-14 (census 14.1) — <b>Go To reached the way an operator reaches it: by pressing Alt+G</b>, through the
/// real <see cref="MainWindow"/> key tunnel, with the real overlay realised in the visual tree.
///
/// <para>🔴 <b>Why this file exists separately from <see cref="GoToOverlayTests"/>.</b> That suite drives
/// <c>OpenGoTo()</c> / <c>ActivateGoTo()</c> directly and proves the INDEX and the JUMP are right. It would
/// pass in full against a view model no key could reach and no window could show — which is exactly the
/// "432 lines of print code with zero references" failure this project has already paid for once. A capability
/// counts as closed only when a user can reach it end to end, so the chord and the rendered overlay are pinned
/// here, on the real window.</para>
/// </summary>
public sealed class GoToChordReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewCompany()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexGoToChord_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();

        vm.NewCompanyName = "GoTo Chord Co";
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private static System.Collections.Generic.IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>The realised Go To search box, or null when the overlay is not on screen.</summary>
    private static TextBox? SearchBox(MainWindow w) =>
        Descendants(w).OfType<TextBox>().FirstOrDefault(t => t.Name == "GoToSearchBox");

    // ------------------------------------------------------------------ the chord

    /// <summary>
    /// Alt+G raises the overlay AND realises it — the search box is in the visual tree and holds focus, so the
    /// operator's next keystroke lands in the search and not in the menu behind it.
    /// </summary>
    [AvaloniaFact]
    public void AltG_raises_the_Go_To_overlay_and_focuses_its_search_box()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            Assert.False(vm.IsGoToOpen);
            Assert.Null(SearchBox(window));

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);

            Assert.True(vm.IsGoToOpen);
            var box = SearchBox(window);
            Assert.NotNull(box);
            Assert.True(box!.IsFocused, "the Go To search box must take focus, or Alt+G is followed by keystrokes "
                                        + "landing on the screen the operator is trying to leave.");
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Alt+G again closes it — the same chord toggles, so an accidental press costs one keystroke.</summary>
    [AvaloniaFact]
    public void AltG_a_second_time_dismisses_the_overlay()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsGoToOpen);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsGoToOpen);
            Assert.Null(SearchBox(window));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The end-to-end run an operator actually performs: Alt+G, type, Enter — and the screen changes. Driven
    /// entirely by keys and a bound text edit; no view model method is called by hand.
    /// </summary>
    [AvaloniaFact]
    public void AltG_then_typing_then_Enter_opens_the_report()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);

            var box = SearchBox(window)!;
            box.Text = "trial";                       // what typing into the focused box produces
            Pump(window);
            Assert.Equal("trial", vm.GoTo!.SearchText);
            Assert.Contains(vm.GoTo!.Results, r => r.Label == "Trial Balance");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsGoToOpen);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 While the overlay is up it OWNS Down. Without that arm the arrow would step the cascade column hidden
    /// BEHIND the overlay — the operator would be driving a menu they cannot see, and Enter would then drill
    /// that menu instead of opening the highlighted result.
    /// </summary>
    [AvaloniaFact]
    public void Down_moves_the_Go_To_highlight_and_never_the_menu_behind_it()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            var menuSelectionBefore = vm.SelectedIndex;

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            SearchBox(window)!.Text = "balance";
            Pump(window);
            Assert.Equal(0, vm.GoTo!.SelectedIndex);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(1, vm.GoTo!.SelectedIndex);
            Assert.Equal(menuSelectionBefore, vm.SelectedIndex);      // the cascade never moved
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Escape dismisses the overlay and leaves the screen underneath exactly as it was.</summary>
    [AvaloniaFact]
    public void Escape_dismisses_the_overlay_and_leaves_the_screen_behind_it_untouched()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            SearchBox(window)!.Text = "trial";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsGoToOpen);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.BalanceSheet, vm.Reports!.Kind);   // did NOT jump
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The feature's own premise: Go To works from a page column, not just the Gateway. Alt+G over an open
    /// Balance Sheet reaches a master-creation screen in three keystrokes plus a word.
    /// </summary>
    [AvaloniaFact]
    public void AltG_works_from_an_open_report_and_reaches_a_master()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            SearchBox(window)!.Text = "godown";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.GodownMaster, vm.CurrentScreen);
            Assert.NotNull(vm.GodownMaster);
        }
        finally { Close(window, tempDir); }
    }
}
