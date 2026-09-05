using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
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

    /// <summary>The amber the cascade and this overlay both paint the highlighted row with (SelBrush).</summary>
    private static readonly Color HighlightAmber = Color.Parse("#FFD54F");

    /// <summary>
    /// The labels of the Go To result rows that are ACTUALLY PAINTED as highlighted on screen, read out of the
    /// realised visual tree rather than out of the view model. Reading the pixels' own source is the whole
    /// point: an <c>IsSelected</c> flag the view never hears about moves the highlight in the model and leaves
    /// the screen unchanged, and a model-only assertion cannot tell the two apart.
    /// </summary>
    private static string[] HighlightedResultLabels(MainWindow w)
    {
        var list = Descendants(w).OfType<ItemsControl>().FirstOrDefault(c => c.Name == "GoToResults");
        if (list is null) return Array.Empty<string>();

        return Descendants(list)
            .OfType<Border>()
            .Where(b => b.Background is ISolidColorBrush s && s.Color == HighlightAmber)
            .Select(b => Descendants(b).OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty)
            .ToArray();
    }

    /// <summary>
    /// 🔴 The highlight must MOVE ON SCREEN, not merely in the view model.
    ///
    /// <para>Every other arrow assertion in this file reads <c>vm.GoTo.SelectedIndex</c>. That is the model's
    /// own bookkeeping and it is satisfied by a plain property nothing is listening to — so an overlay whose
    /// result rows never repaint would pass all of them while showing the operator a highlight frozen on row
    /// one. They would then press Enter on a row they had no way of knowing was selected. This test reads the
    /// painted background out of the realised tree, so it fails on exactly that shape.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_painted_highlight_follows_the_arrow_keys_and_marks_exactly_one_row()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            SearchBox(window)!.Text = "balance";
            Pump(window);
            Assert.True(vm.GoTo!.Results.Count >= 2, "the search must offer at least two rows to arrow between.");

            var firstLabel = vm.GoTo!.Selected!.Label;
            Assert.Equal(new[] { firstLabel }, HighlightedResultLabels(window));

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);

            var secondLabel = vm.GoTo!.Selected!.Label;
            Assert.NotEqual(firstLabel, secondLabel);
            Assert.Equal(new[] { secondLabel }, HighlightedResultLabels(window));

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(new[] { firstLabel }, HighlightedResultLabels(window));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>Auto-scroll on selection</b> (keyboard-first contract). Go To opens UNFILTERED over the whole
    /// menu — a couple of hundred rows in a 460px-tall panel — so the highlight walks off the bottom within a
    /// dozen presses of Down. The rows are not focusable (focus stays in the search box, which is the point),
    /// so nothing drags the viewport along for free the way it does on a focusable grid: the overlay has to
    /// scroll the highlight into view itself, or the operator is arrowing at a row they cannot see and pressing
    /// Enter on a screen they were never shown the name of.
    /// </summary>
    [AvaloniaFact]
    public void Arrowing_past_the_fold_scrolls_the_highlighted_row_into_view()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);

            var scroller = Descendants(window).OfType<ScrollViewer>()
                .FirstOrDefault(s => Descendants(s).OfType<ItemsControl>().Any(c => c.Name == "GoToResults"));
            Assert.NotNull(scroller);
            Assert.True(scroller!.Extent.Height > scroller.Viewport.Height,
                "the unfiltered index must overflow the panel for this test to mean anything.");
            Assert.Equal(0d, scroller.Offset.Y);

            // Arrow well past the fold.
            for (var i = 0; i < 40; i++) window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(40, vm.GoTo!.SelectedIndex);

            Assert.True(scroller.Offset.Y > 0d,
                "the highlight moved 40 rows down and the panel never scrolled — the operator cannot see it.");

            var list = Descendants(window).OfType<ItemsControl>().First(c => c.Name == "GoToResults");
            var row = (Visual?)list.ContainerFromIndex(40);
            Assert.NotNull(row);
            var top = row!.TranslatePoint(default, scroller)!.Value.Y;
            Assert.InRange(top, 0d - 1d, scroller.Viewport.Height);

            // Retyping re-ranks and puts the highlight back on row one — the panel must come back up with it,
            // or the operator reads an empty stretch of list while row one is selected far above them.
            SearchBox(window)!.Text = "a";
            Pump(window);
            Assert.Equal(0, vm.GoTo!.SelectedIndex);
            Assert.Equal(0d, scroller.Offset.Y);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Retyping re-ranks the list and drops the highlight back to row one — and the SCREEN must agree, or the
    /// operator reads a highlight left over from the previous search while Enter aims at the new row one.
    /// </summary>
    [AvaloniaFact]
    public void Retyping_the_search_moves_the_painted_highlight_back_to_the_first_row()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            var box = SearchBox(window)!;
            box.Text = "balance";
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(1, vm.GoTo!.SelectedIndex);

            box.Text = "godown";
            Pump(window);

            Assert.Equal(0, vm.GoTo!.SelectedIndex);
            Assert.Equal(new[] { vm.GoTo!.Selected!.Label }, HighlightedResultLabels(window));

            // Back to the first search. The rows are the SAME destination objects, so the one that was
            // highlighted a moment ago must not return still wearing its highlight — that would paint two rows
            // amber while only one of them is what Enter opens.
            box.Text = "balance";
            Pump(window);

            Assert.Equal(0, vm.GoTo!.SelectedIndex);
            Assert.Equal(new[] { vm.GoTo!.Selected!.Label }, HighlightedResultLabels(window));
        }
        finally { Close(window, tempDir); }
    }

    // ------------------------------------------------------------------ discoverability

    /// <summary>
    /// 🔴 <b>Alt+G has to be ADVERTISED.</b> Go To has no menu row and no screen of its own — by design, since
    /// it floats over whatever is open — so unless the button bar carries it, the only operator who can ever
    /// use the feature is one who already knew the chord. This shell's own rule, written twice in
    /// <c>BuildButtonBar</c>, is that a chord nobody can find is not a feature.
    ///
    /// <para>And the badge must fire the SAME door as the key: this file records Alt+C and Alt+A each having
    /// once advertised one shortcut and done two different things.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_button_bar_advertises_AltG_and_its_badge_runs_the_same_door_as_the_key()
    {
        var (window, vm, tempDir) = NewCompany();
        try
        {
            var badge = vm.ButtonBar.SingleOrDefault(b => b.Key == "Alt+G");
            Assert.NotNull(badge);
            Assert.Equal("Go To", badge!.Caption);
            Assert.True(badge.Enabled, "a company is open, so the chord works and the badge must not be dim.");

            badge.Action();
            Assert.True(vm.IsGoToOpen);
            badge.Action();
            Assert.False(vm.IsGoToOpen);       // the badge toggles exactly as the key does
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The other half of the same rule: before a company is open the chord genuinely does nothing, so the badge
    /// must be DIM rather than enabled-and-inert (register defect IV-31).
    /// </summary>
    [AvaloniaFact]
    public void The_AltG_badge_is_dim_before_a_company_is_open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexGoToBadge_" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow { DataContext = new MainWindowViewModel(new CompanyStorage(tempDir)) };
        try
        {
            window.Show();
            var vm = (MainWindowViewModel)window.DataContext!;
            Pump(window);

            var badge = vm.ButtonBar.SingleOrDefault(b => b.Key == "Alt+G");
            Assert.NotNull(badge);
            Assert.False(badge!.Enabled);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsGoToOpen);       // and the key really is inert there, so the dim badge is honest
        }
        finally { Close(window, tempDir); }
    }

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
