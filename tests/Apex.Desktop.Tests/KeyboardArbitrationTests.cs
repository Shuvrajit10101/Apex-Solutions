using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Apex.Desktop.Converters;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE KEYSTROKE-ARBITRATION LOCK — the window tunnel handler must YIELD to an OPEN picker.
///
/// <para><b>The two defects locked here, both reproduced by measurement before any code changed.</b>
/// <c>MainWindow.OnKeyDown</c> is a first-match-wins tunnel chain registered on the Window
/// (<c>MainWindow.axaml.cs:29-30</c>). Because it is a TUNNEL, it sees every keystroke <i>before</i> the
/// control the operator is actually driving. Two of its arms claimed keys that belong to an open dropdown:</para>
///
/// <list type="number">
/// <item><b>D1 — Enter raised the Accept prompt instead of committing the pick.</b> Measured on the real
/// <c>Ledger Creation</c> screen with a real ledger-group picker open:
/// <c>AFTER enter: sel=Apex.Ledger.Domain.Group open=True promptOpen=True promptText='Accept Ledger? (Y/N)'</c>.
/// The operator opened a picker, pressed Enter to take the highlighted row, and got
/// <i>"Accept Ledger? (Y/N)"</i> over a dropdown that was still open. Live on all 24
/// <see cref="MainWindowViewModel.IsMasterAcceptScreen"/> screens.</item>
///
/// <item><b>D2 — Escape popped the Miller column AND discarded the in-progress master, in ONE press.</b>
/// Arm 45 (<c>:708</c>) was completely unguarded. Measured:
/// <c>BEFORE esc: columns=2 screen=LedgerMaster</c> → <c>AFTER esc: columns=1 screen=Gateway
/// ledgerMasterNull=True</c>. One Escape aimed at closing a dropdown destroyed the half-typed ledger. This
/// violates the settled two-press contract: <b>Escape must never also pop the column in the same press.</b></item>
/// </list>
///
/// <para><b>The fix is the narrowest possible guard</b> — three arms gain <c>!IsPickerOpen(e)</c>, a walk up
/// <c>e.Source</c>'s parent chain looking for a <c>ComboBox</c> whose dropdown is open. Nothing else moves.
/// <c>IsTyping</c> is deliberately NOT widened: that one-line edit reaches ~157 screens and belongs to the
/// filter slices.</para>
///
/// <para><b>Why the guard is <c>IsPickerOpen</c> and not "a picker is focused".</b> Arms 44/45 are the only two
/// keyboard exits from a form column. A merely-FOCUSED, closed picker must still let Escape reach
/// <see cref="MainWindowViewModel.Back"/>, or ~157 screens lose their keyboard route out.
/// <see cref="Escape_on_a_CLOSED_picker_still_pops_the_column_in_one_press"/> locks that direction.</para>
///
/// <para><b>⚠ MEASURED LIMIT OF THIS HARNESS — read before extending these tests.</b> Avalonia's headless
/// platform returns <c>null</c> from <c>CreatePopup()</c>, which forces an <c>OverlayPopupHost</c> in the SAME
/// top-level; Win32 supplies a real popup impl and very likely uses a separate <c>PopupRoot</c>. So headless is
/// the WORST case for the window tunnel (it definitely sees the key), which is exactly the case worth locking —
/// but a green run here does not prove the Win32 routing. Separately, a bare <c>ComboBox</c> in a bare
/// <c>Window</c> was measured NOT to commit or close on Enter at all
/// (<c>P2 AFTER enter: sel=Alpha open=True</c>), so <b>no test here may assert that Enter commits the
/// highlighted item</b> — the framework will not do it in this harness whatever the window does. These tests
/// assert only the decidable half: the window must not STEAL the key.</para>
/// </summary>
public sealed class KeyboardArbitrationTests
{
    // ---------------------------------------------------------------- scaffolding

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Opens the REALISTICALLY POPULATED fixture company (38 ledgers, 28 stock items, 51 vouchers). The thin
    /// 2-ledger seed is what made a previous sweep undecidable: a picker with two rows cannot demonstrate that
    /// Enter took the wrong one.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) OpenPopulated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexKeyArb_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);
        return (window, vm, dir);
    }

    /// <summary>A company created through the ordinary path, for the accelerator-shadowing sweep.</summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexKeyArb_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>
    /// Opens Ledger Creation and puts a REAL multi-row picker into the REAL open state — focused, then
    /// <c>IsDropDownOpen = true</c>, which is the property the operator's own F4/click/Alt+Down sets. After the
    /// pump the focused element is a <c>ComboBoxItem</c> inside the popup, so <c>e.Source</c> is the popup row
    /// and NOT the ComboBox — the state in which a naive <c>e.Source is ComboBox</c> test would miss entirely.
    /// </summary>
    private static ComboBox OpenPickerOnLedgerMaster(MainWindow window, MainWindowViewModel vm)
    {
        vm.ShowLedgerMaster();
        Pump(window);

        var picker = Descendants(window).OfType<ComboBox>()
            .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
        picker.Focus();
        Pump(window);
        picker.IsDropDownOpen = true;
        Pump(window);

        Assert.True(picker.IsDropDownOpen, "the picker did not open — the rest of this test would be vacuous");
        return picker;
    }

    // ================================================================ D1 — Enter must not steal the pick

    /// <summary>
    /// 🔴 D1, THE DEFECT. With a picker OPEN on a master screen, Enter belongs to the dropdown. The window must
    /// not raise "Accept Ledger? (Y/N)".
    /// <para><b>Pre-fix measurement:</b> <c>promptOpen=True promptText='Accept Ledger? (Y/N)'</c> with the
    /// dropdown still open — the operator's pick was replaced by a save confirmation.</para>
    /// <para>Asserting <c>IsAcceptPromptOpen == false</c> is the whole of the decidable claim; see the harness
    /// limit in the class remarks for why the commit half cannot be asserted here.</para>
    /// </summary>
    [AvaloniaFact]
    public void Enter_with_a_picker_open_does_not_raise_the_master_accept_prompt()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            Assert.True(vm.IsMasterAcceptScreen);
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);                 // the defect, inverted
            Assert.Equal(string.Empty, vm.AcceptPromptText);
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);  // still on the master, not torn down
            Assert.NotNull(vm.LedgerMaster);
            GC.KeepAlive(picker);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// D1's second half: yielding Enter must not let it fall THROUGH to the next Enter arm
    /// (<c>:698 vm.ActivateSelected()</c>) and silently save the half-typed master. The ledger count is the
    /// money assertion — a fall-through would commit a ledger the operator never confirmed.
    /// </summary>
    [AvaloniaFact]
    public void Enter_with_a_picker_open_does_not_silently_save_the_master()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var before = vm.Company!.Ledgers.Count;
            var picker = OpenPickerOnLedgerMaster(window, vm);
            vm.LedgerMaster!.Name = "Arbitration Probe Ledger";

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(before, vm.Company!.Ledgers.Count);
            Assert.Null(vm.Company!.FindLedgerByName("Arbitration Probe Ledger"));
            GC.KeepAlive(picker);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 THE NARROWNESS LOCK for D1. With NO picker open, Enter on a master screen must STILL raise the WI-11
    /// confirmation. This is the arm the guard sits on; if the guard is written too wide (e.g. "a picker is
    /// focused" rather than "a picker is OPEN", or a widened <c>IsTyping</c>), the shipped Accept prompt
    /// disappears from 24 screens and this test is what catches it.
    /// </summary>
    [AvaloniaFact]
    public void Enter_with_no_picker_open_still_raises_the_master_accept_prompt()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            Assert.True(vm.IsMasterAcceptScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Equal("Accept Ledger? (Y/N)", vm.AcceptPromptText);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 V3 (b) — THE ORDERING PROOF, and a DELIBERATE reversal of this slice's earlier contract.
    ///
    /// <para><b>Supersession, stated plainly.</b> An earlier form of this exact test
    /// (<c>Enter_on_a_focused_but_CLOSED_picker_still_raises_the_accept_prompt</c>) asserted that Enter on a
    /// focused-but-CLOSED picker on a master screen must STILL raise "Accept …? (Y/N)". The USER then DECIDED
    /// the opposite — <i>"Enter opens it."</i> — so on a focused, closed picker Enter now OPENS the dropdown and
    /// the accept prompt must NOT appear. This test replaces the old one to lock the new, user-decided behaviour.
    /// The genuine no-picker-focused case (Enter still prompts) is unchanged and is locked by
    /// <see cref="Enter_with_no_picker_open_still_raises_the_master_accept_prompt"/> (measured: with nothing
    /// focused, <c>e.Source</c> is the <c>MainWindow</c>, so the new V3 arm cannot match).</para>
    ///
    /// <para><b>Why it is the ordering proof.</b> On a master screen a focused closed picker's Enter is claimed
    /// by TWO arms below the new one — the master-accept arm (<c>RequestMasterAccept</c>) and
    /// <c>ActivateSelected</c>. The V3 arm must sit AHEAD of both, or the prompt would win. Asserting
    /// <c>IsAcceptPromptOpen == false</c> alongside <c>IsDropDownOpen == true</c> is exactly that ordering.</para>
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_focused_but_CLOSED_picker_on_a_master_OPENS_it_and_does_not_prompt()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            Assert.True(vm.IsMasterAcceptScreen);
            var picker = Descendants(window).OfType<ComboBox>()
                .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            picker.Focus();
            Pump(window);
            Assert.False(picker.IsDropDownOpen);
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(picker.IsDropDownOpen);                   // V3: Enter OPENED the focused closed picker
            Assert.False(vm.IsAcceptPromptOpen);                  // …and the master-accept prompt did NOT open
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);  // still on the master, nothing torn down
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// ⚪ NOT A REPRODUCED DEFECT — a NON-REGRESSION GUARD, kept because the fix could create one.
    ///
    /// <para>It is natural to assume D1 extends to any screen whose Enter is claimed by a later arm: on the
    /// report Configuration panel <c>IsMasterAcceptScreen</c> is false, so Enter reaches arm 49
    /// (<c>:698 vm.ActivateSelected()</c>). <b>Measured: it does not apply or close the panel</b> — this test
    /// passed BEFORE any code changed. <c>ActivateSelected</c> acts on the cascade selection, not on the panel.
    /// So there is no second live defect here and none was "fixed".</para>
    ///
    /// <para><b>Why it is kept anyway.</b> The D1 fix makes Enter fall OUT of the master-accept arm; without a
    /// matching guard on arm 49 the key would simply be stolen one arm later — prompt suppressed, key still
    /// consumed, dropdown still ignored. That is the concrete reason both Enter arms carry the guard.</para>
    ///
    /// <para><b>⚠ ATTRIBUTION CORRECTED — this test is NOT what locks arm 49.</b> It previously claimed to be.
    /// Measured by stripping the <c>!IsPickerOpen</c> guard from arm 49: this test stayed GREEN, and the test
    /// that actually went red was <see cref="Enter_with_a_picker_open_does_not_silently_save_the_master"/>
    /// (<c>Expected 38, Actual 39</c> ledgers — the fall-through committing a master the operator never
    /// confirmed). That ledger-count assertion is arm 49's real lock. This test remains a genuine
    /// non-regression guard for the report panel, which is all it ever proved.</para>
    /// </summary>
    [AvaloniaFact]
    public void Enter_with_a_picker_open_on_a_report_panel_does_not_apply_and_close_it()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            vm.OpenReportConfig();
            Pump(window);
            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

            var picker = Descendants(window).OfType<ComboBox>()
                .FirstOrDefault(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            Assert.NotNull(picker);                              // otherwise this test proves nothing
            picker!.Focus();
            Pump(window);
            picker.IsDropDownOpen = true;
            Pump(window);
            Assert.True(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen); // the panel is still open
            Assert.NotNull(vm.ReportConfig);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ D2 — Escape is two presses

    /// <summary>
    /// 🔴 D2, THE DEFECT. Press ONE of Escape, with a picker open, must close the dropdown and NOTHING else.
    /// <para><b>Pre-fix measurement:</b> <c>columns 2 → 1</c>, <c>screen LedgerMaster → Gateway</c>,
    /// <c>ledgerMasterNull=True</c> — one press discarded the in-progress ledger.</para>
    /// </summary>
    [AvaloniaFact]
    public void Escape_press_one_closes_the_dropdown_and_never_pops_the_column()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            var columnsBefore = vm.Columns.Count;
            var master = vm.LedgerMaster;

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(columnsBefore, vm.Columns.Count);        // the column did NOT pop
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.Same(master, vm.LedgerMaster);                 // the in-progress master survived
            Assert.False(picker.IsDropDownOpen);                  // and the dropdown DID close
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// D2's other half — the settled TWO-PRESS contract end to end. Press one closes the dropdown; press two,
    /// with no dropdown left open, pops the column. Both presses are asserted separately so a merged
    /// implementation (one press doing both) fails at press one rather than passing on the net result.
    /// </summary>
    [AvaloniaFact]
    public void Escape_press_two_then_pops_the_column()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.False(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(columnsBefore - 1, vm.Columns.Count);
            Assert.Null(vm.LedgerMaster);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 THE NARROWNESS LOCK for D2 — the one that makes the guard <c>IsPickerOpen</c> and not anything wider.
    /// A merely-focused, CLOSED picker must let Escape pop the column in a SINGLE press. Arms 44/45 are the only
    /// two keyboard exits from a form column; guard them on focus (or on a widened <c>IsTyping</c>) and ~157
    /// screens lose their keyboard route out. Measured groundwork: a closed ComboBox leaves Escape unhandled
    /// (<c>P3b closed-picker escape handledAtBubble=False</c>), so the window arm is the only thing that can act.
    /// </summary>
    [AvaloniaFact]
    public void Escape_on_a_CLOSED_picker_still_pops_the_column_in_one_press()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            var columnsBefore = vm.Columns.Count;
            var picker = Descendants(window).OfType<ComboBox>()
                .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            picker.Focus();
            Pump(window);
            Assert.False(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(columnsBefore - 1, vm.Columns.Count);
            Assert.Null(vm.LedgerMaster);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Ordering lock: the WI-11 accept prompt still owns Escape. The prompt arm sits far above the navigation
    /// switch (<c>:261-269</c>), so Escape dismisses the confirmation rather than popping the column — and the
    /// new picker guard, which is on the navigation arm only, must not disturb that.
    /// </summary>
    [AvaloniaFact]
    public void Escape_still_dismisses_the_accept_prompt_before_any_navigation()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            var columnsBefore = vm.Columns.Count;
            Assert.True(vm.RequestMasterAccept());
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Equal(columnsBefore, vm.Columns.Count);       // dismissed the prompt, did NOT navigate
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ D2b — Left is the OTHER keyboard exit

    /// <summary>
    /// 🔴 D2b, THE DEFECT — the twin of D2, and the more dangerous one because it was silent.
    ///
    /// <para>Left (<c>:712</c>) and Escape (<c>:726</c>) are documented in the source itself as the SAME PAIR —
    /// <i>"Left / Esc removes the rightmost column"</i>, <i>"the only two keyboard exits"</i>. D2 guarded Escape
    /// and left Left carrying only <c>!IsTyping(e)</c>, which tests <c>e.Source is TextBox</c>. With a dropdown
    /// open <c>e.Source</c> is a <c>ComboBoxItem</c>, so the guard does not fire and Left falls through to
    /// <see cref="MainWindowViewModel.Back"/>.</para>
    ///
    /// <para><b>Pre-fix measurement, byte-identical to the D2 work-loss this slice exists to close:</b>
    /// <c>BEFORE left: columns=2 screen=LedgerMaster ledgerMasterNull=False dropDownOpen=True</c> →
    /// <c>AFTER left: columns=1 screen=Gateway ledgerMasterNull=True dropDownOpen=False</c>. One Left press,
    /// aimed at moving inside the dropdown, discarded the half-typed ledger.</para>
    /// </summary>
    [AvaloniaFact]
    public void Left_with_a_picker_open_never_pops_the_column()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            var columnsBefore = vm.Columns.Count;
            var master = vm.LedgerMaster;

            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(columnsBefore, vm.Columns.Count);        // the column did NOT pop
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.Same(master, vm.LedgerMaster);                 // the in-progress master survived
            GC.KeepAlive(picker);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 THE NARROWNESS LOCK for D2b, exactly mirroring
    /// <see cref="Escape_on_a_CLOSED_picker_still_pops_the_column_in_one_press"/>. A merely-focused, CLOSED
    /// picker must let Left pop the column in a single press — Left and Escape are the only two keyboard exits
    /// from a form column, so a guard written on focus rather than on OPEN would strand ~157 screens.
    /// </summary>
    [AvaloniaFact]
    public void Left_on_a_CLOSED_picker_still_pops_the_column_in_one_press()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            var columnsBefore = vm.Columns.Count;
            var picker = Descendants(window).OfType<ComboBox>()
                .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            picker.Focus();
            Pump(window);
            Assert.False(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(columnsBefore - 1, vm.Columns.Count);
            Assert.Null(vm.LedgerMaster);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ arrows must reach an open dropdown

    /// <summary>
    /// The index of the row currently HIGHLIGHTED inside an open dropdown — i.e. which <c>ComboBoxItem</c> holds
    /// focus. This, and not <c>SelectedIndex</c>, is what an arrow key moves: a ComboBox commits the selection
    /// only on Enter/click, so <c>SelectedIndex</c> sits still at 25 throughout a keyboard walk and asserting on
    /// it would have made this test vacuous in both directions.
    /// </summary>
    private static int HighlightedRow(MainWindow window, ComboBox picker)
    {
        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
        return focused is ComboBoxItem item ? picker.IndexFromContainer(item) : -1;
    }

    /// <summary>
    /// 🔴 THE USER CONTRACT. The settled keyboard requirement is that arrows work on every screen
    /// <b>including inside dropdowns</b>. They did not: Up/Down (<c>:672</c>/<c>:676</c>) carried only
    /// <c>!IsTyping(e)</c>, so with a dropdown open — where <c>e.Source</c> is a <c>ComboBoxItem</c>, not a
    /// <c>TextBox</c> — the window tunnel consumed both keys before the popup could see them.
    ///
    /// <para><b>Pre-fix measurement, on a real 26-row ledger-group picker:</b> the highlight did not move on
    /// <i>any</i> press — <c>focus=ComboBoxItem#25</c> before Down, <c>#25</c> after, with
    /// <c>bubble: Down handled=True src=ComboBoxItem</c> proving the window had claimed it. The highlight could
    /// not be moved by keyboard <b>at all</b>, which made the Enter and Escape yields this slice adds very nearly
    /// pointless: an operator could reach a dropdown but never navigate it.</para>
    ///
    /// <para><b>Post-fix, same measurement:</b> <c>#25 → #26 → #27 → #26</c>. That walk is what is asserted
    /// here — the positive property, not merely the absence of damage.</para>
    /// </summary>
    [AvaloniaFact]
    public void Arrows_with_a_picker_open_move_the_dropdown_highlight()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            var terminal = vm.Columns[^1];
            var cascadeBefore = terminal.SelectedIndex;

            var start = HighlightedRow(window, picker);
            Assert.True(start >= 0, "no dropdown row holds focus — the rest of this test would be vacuous");
            Assert.True(picker.ItemCount > start + 2, "too few rows below the highlight to demonstrate movement");

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(start + 1, HighlightedRow(window, picker));      // Down moved the DROPDOWN highlight

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(start + 2, HighlightedRow(window, picker));

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(start + 1, HighlightedRow(window, picker));      // …and Up walked it back

            // The other half of the contract: moving inside the dropdown must NOT move the cascade behind it,
            // and must not disturb the column or the in-progress master.
            Assert.Equal(cascadeBefore, terminal.SelectedIndex);
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerMaster);
            Assert.True(picker.IsDropDownOpen);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 THE REGRESSION LOCK for the arrow guard — the one that matters most, because Up/Down ARE the
    /// Miller-column navigation. With NO dropdown open, Up/Down must still move the cascade highlight exactly as
    /// they always did. Measured on the Gateway: <c>selIdx 1 → 2</c> on Down, back to <c>1</c> on Up.
    /// <para>If the guard is ever written wider than "a picker is OPEN" — on focus, or by widening
    /// <c>IsTyping</c> — this test is what fails, and it fails on the single most-used key pair in the app.</para>
    /// </summary>
    [AvaloniaFact]
    public void Arrows_with_no_picker_open_still_move_the_cascade_selection()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var column = vm.Columns[^1];
            Assert.True(column.Items.Count > 2, "a one-row column cannot demonstrate movement");
            var start = column.SelectedIndex;

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(start + 1, column.SelectedIndex);        // Down moved the cascade highlight

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(start, column.SelectedIndex);            // Up moved it back
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ D3 — the quick-jump swallow

    /// <summary>
    /// D3, recorded as MEASURED FACT rather than as a fix.
    ///
    /// <para><b>The claim was</b> that bare B/P/T/D routed through <c>CanQuickJump</c> (<c>:733-736</c>) are
    /// silently swallowed "pre-company". <b>The mechanism is real:</b> the arm sets <c>e.Handled = true</c>
    /// unconditionally, while <c>Fire</c> (<c>:790-798</c>) does nothing when the matching button is DISABLED —
    /// and on Company Select the bar is exactly <c>…,B!,P!,T!,D!,…</c> (measured; <c>!</c> = disabled), so all
    /// four keys are consumed with no action.</para>
    ///
    /// <para><b>But the claimed CONSEQUENCE cannot happen.</b> The only thing downstream of that arm is the
    /// bare-letter arm at <c>:750</c>, which calls <c>HandleMenuLetter</c>; that method returns false unless
    /// <c>IsGatewayCascade</c>, and <c>CanQuickJump</c> requires <c>IsMenuScreen</c>, which is DEFINED as
    /// <c>!IsGatewayCascade &amp;&amp; …</c> (<c>MainWindowViewModel.cs:558</c>). The two are mutually exclusive
    /// by construction, so the quick-jump arm can never shadow a cascade hotkey or a cascade type-ahead. The
    /// other conceivable victim — picker type-to-jump on a menu screen — does not exist either: Company Select
    /// and Company Creation were measured to carry <b>zero</b> visible ComboBoxes.</para>
    ///
    /// <para><b>Therefore no fix is applied.</b> This test pins the measured truth in both directions so the
    /// question is not re-opened from a code reading.</para>
    ///
    /// <para><b>⚠ NAME CORRECTION — this test does NOT exercise the quick-jump arm.</b> It was previously called
    /// <c>…_and_live_on_the_gateway</c>, which was false. On the Gateway <c>IsGatewayCascade=True</c> and
    /// <c>IsMenuScreen=False</c> (measured), so <see cref="MainWindow"/>'s <c>CanQuickJump</c> is false and a
    /// bare B is claimed instead by the LAST arm, <c>HandleMenuLetter</c>, which opens the Banking <i>submenu</i>.
    /// Part (b) below therefore checks only that the BUTTON BAR enables the four keys there — it never proves
    /// the arm fires. The state in which the arm genuinely fires is a RE-ENTERED Company Select
    /// (<c>Company != null</c> and <c>IsMenuScreen=True</c> together), and that is covered by
    /// <see cref="Quickjump_letters_reach_their_reports_where_the_arm_actually_fires"/>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Quickjump_letters_are_inert_pre_company_and_enabled_on_the_gateway()
    {
        // Deliberately NOT OpenPopulated(): the bar enables B/P/T/D as soon as a company is open, so a window
        // that has already loaded one cannot exhibit the pre-company state at all. This is the fresh-boot path.
        var dir = Path.Combine(Path.GetTempPath(), "ApexKeyArb_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            // (a) pre-company: the bar disables B/P/T/D, so each key is consumed with no action.
            vm.ShowCompanySelect();
            Pump(window);
            Assert.Null(vm.Company);
            Assert.True(vm.IsMenuScreen);
            Assert.False(vm.IsGatewayCascade);                    // the mutual exclusion, asserted not assumed
            foreach (var key in new[] { "B", "P", "T", "D" })
                Assert.False(vm.ButtonBar.Single(b => b.Key == key).Enabled);

            foreach (var key in new[] { PhysicalKey.B, PhysicalKey.P, PhysicalKey.T, PhysicalKey.D })
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            }

            // (b) on the Gateway cascade the bar enables them and each still reaches its shipped action.
            vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
            Pump(window);
            Assert.True(vm.IsGatewayCascade);
            Assert.False(vm.IsMenuScreen);
            foreach (var key in new[] { "B", "P", "T", "D" })
                Assert.True(vm.ButtonBar.Single(b => b.Key == key).Enabled);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 THE ARM ITSELF — the coverage the suite was missing entirely. Deleting all four quick-jump arms
    /// (<c>:751-754</c>) left the whole Desktop suite green, because no test drove a bare B/P/T/D in a state
    /// where <c>CanQuickJump</c> is true. It requires <c>IsMenuScreen</c>, which is defined as
    /// <c>!IsGatewayCascade &amp;&amp; …</c>, while <c>Fire</c> needs the button bar ENABLED, which needs an open
    /// company — so the arm only ever fires with a company open AND the cascade left behind.
    ///
    /// <para><b>That state is a RE-ENTERED Company Select</b>, measured: <c>companyNull=False IsMenuScreen=True
    /// IsGatewayCascade=False</c>, all four bar buttons <c>Enabled=True</c>, and each key reaching its report:
    /// <c>B → BalanceSheet · P → ProfitAndLoss · T → TrialBalance · D → DayBook</c>. Each is asserted by
    /// <see cref="ReportsViewModel.Kind"/> and not merely by <c>CurrentScreen == Report</c>, so a mis-wired arm
    /// that opened the wrong report still fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void Quickjump_letters_reach_their_reports_where_the_arm_actually_fires()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var expected = new (PhysicalKey Key, string Name, ReportKind Kind)[]
            {
                (PhysicalKey.B, "B", ReportKind.BalanceSheet),
                (PhysicalKey.P, "P", ReportKind.ProfitAndLoss),
                (PhysicalKey.T, "T", ReportKind.TrialBalance),
                (PhysicalKey.D, "D", ReportKind.DayBook),
            };

            foreach (var (key, name, kind) in expected)
            {
                // Re-enter Company Select with the company still open: this is the ONLY state in which
                // CanQuickJump (IsMenuScreen) and an enabled button bar (Company != null) are true together.
                vm.ShowCompanySelect();
                Pump(window);
                Assert.NotNull(vm.Company);
                Assert.True(vm.IsMenuScreen);
                Assert.False(vm.IsGatewayCascade);
                Assert.True(vm.ButtonBar.Single(b => b.Key == name).Enabled);

                window.KeyPressQwerty(key, RawInputModifiers.None);
                Pump(window);

                Assert.Equal(Screen.Report, vm.CurrentScreen);
                Assert.NotNull(vm.Reports);
                Assert.Equal(kind, vm.Reports!.Kind);
            }
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ Tab containment

    /// <summary>Which cascade column does this element live in? Walks up to the first GatewayColumn DataContext.</summary>
    private static GatewayColumn? ColumnOf(object? element)
    {
        for (var c = element as StyledElement; c is not null; c = c.Parent)
            if (c.DataContext is GatewayColumn col) return col;
        return null;
    }

    /// <summary>
    /// 🔴 TAB ESCAPED THE ACTIVE COLUMN. Push a Ledger Creation column over a live Payment Voucher with Alt+C,
    /// then Tab: focus walked off the end of the new column and into the <b>voucher column behind it</b>, where
    /// the operator would edit fields of an inactive column with no cue that focus had moved.
    ///
    /// <para><b>Pre-fix measurement, 60 Tab presses on the real window:</b>
    /// <c>TOTAL tabs landing OUTSIDE the terminal column = 20</c>. Post-fix: 0.</para>
    ///
    /// <para><b>The assertion is deliberately about IDENTITY, not count.</b> Every Tab is resolved to the
    /// GatewayColumn that actually owns the focused control, and every landing must be either the terminal
    /// column or the F-key button bar (which lives outside the columns and must stay reachable — asserting
    /// "never leaves the terminal column" alone would pass a fix that stranded the bar, so the bar is asserted
    /// reachable in the same sweep).</para>
    ///
    /// <para><b>⚠ WHY THE POSITIVE ASSERTION IS HERE, AND WHY IT MAY NOT BE DELETED.</b> An earlier form of this
    /// test asserted only <c>outside == 0</c> — that nothing landed in the wrong column — and was therefore
    /// satisfied by Tab reaching NOTHING AT ALL. Proven by mutation: forcing
    /// <see cref="TerminalColumnTabNavigationConverter"/> to return <c>None</c> unconditionally (Tab reaches zero
    /// fields in <i>any</i> column, i.e. the feature completely destroyed) left it GREEN, with
    /// <c>inside=0 distinctInside=0 outside=0</c>. A containment test that passes when the container is empty
    /// proves nothing. So the sweep now also asserts that Tab actually REACHES the terminal column, and reaches
    /// a spread of distinct controls in it rather than ping-ponging between two.</para>
    ///
    /// <para><b>Healthy baseline, measured on this fixture:</b>
    /// <c>inside=32 distinctInside=16 outside=0 nullFocus=0 otherNonColumn=28</c>. The floors below are set well
    /// under those numbers so ordinary layout churn does not make the test brittle, while still being far above
    /// the <c>0</c> the broken mutation produces.</para>
    ///
    /// <para><b>Setup note that cost a probe run:</b> Alt+C on a voucher is context-aware (WI-1) and is INERT
    /// unless a <c>CreateField</c>-tagged picker is focused. Without that focus the column is never pushed and
    /// the whole test is vacuous, so the tagged picker is asserted present before the key is driven.</para>
    /// </summary>
    [AvaloniaFact]
    public void Tab_never_leaves_the_terminal_column_for_a_column_behind_it()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Pump(window);

            var tagged = Descendants(window).OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && CreateField.GetMaster(c) is { Length: > 0 })
                .ToList();
            Assert.NotEmpty(tagged);                       // else Alt+C is inert and the test proves nothing
            tagged[0].Focus();
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(3, vm.Columns.Count);             // Gateway | Payment Voucher | Ledger Creation
            Assert.Equal("Ledger Creation", vm.Columns[^1].Title);
            var terminal = vm.Columns[^1];
            var behind = vm.Columns[1];

            var landedOutside = new List<string>();
            var landedOnButtonBar = 0;
            var landedNowhere = 0;
            var landedInside = 0;
            var distinctInside = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < 60; i++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                Pump(window);
                var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
                // Focus-lost is counted SEPARATELY from the button bar. ColumnOf(null) returns null, which is the
                // same answer it gives for the bar — so folding the two together would let a run that simply
                // dropped focus satisfy the "bar still reachable" assertion below. Measured nullFocus=0 both
                // before and after the fix, so this is a latent hazard being closed, not the live one.
                if (focused is null) { landedNowhere++; continue; }
                var col = ColumnOf(focused);
                if (col is null) { landedOnButtonBar++; continue; }
                if (!ReferenceEquals(col, terminal))
                    landedOutside.Add($"Tab {i:00} -> {focused.GetType().Name} in '{col.Title}'");
                else { landedInside++; distinctInside.Add(focused); }
            }

            var census = $"(inside={landedInside} distinctInside={distinctInside.Count} "
                       + $"outside={landedOutside.Count} buttonBar={landedOnButtonBar} "
                       + $"nullFocus={landedNowhere})";

            // THE NEGATIVE property — the defect, inverted.
            Assert.True(landedOutside.Count == 0,
                $"Tab left the terminal column {landedOutside.Count} time(s) {census}:\n"
                + string.Join("\n", landedOutside));

            // THE POSITIVE property — without these two the test is satisfied by Tab reaching nothing at all.
            // Baseline is inside=32 / distinctInside=16; the always-None mutation drives both to 0.
            Assert.True(landedInside >= 8,
                $"Tab barely reached the terminal column, so containment proves nothing {census}");
            Assert.True(distinctInside.Count >= 6,
                $"Tab reached too few DISTINCT controls in the terminal column — focus is stuck, not "
                + $"traversing {census}");

            Assert.True(landedOnButtonBar > 0, $"the F-key button bar became unreachable by Tab {census}");
            GC.KeepAlive(behind);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ the converter's failure direction

    /// <summary>
    /// Pins <see cref="TerminalColumnTabNavigationConverter"/>'s behaviour on every input a XAML binding can
    /// actually deliver, including the degenerate ones.
    ///
    /// <para><b>Why this exists.</b> The converter is bound to <c>IsLast</c>. If that binding ever fails to
    /// resolve, Avalonia hands the converter <see cref="AvaloniaProperty.UnsetValue"/> and otherwise carries on
    /// SILENTLY — no exception, no crash, just a column with the wrong Tab policy. Which way it fails is
    /// therefore a real design decision, and it is asserted here rather than left to whatever a single
    /// <c>is true</c> test happens to do.</para>
    ///
    /// <para><b>The decision: fail CLOSED (<c>None</c>).</b> Failing toward <c>Continue</c> would silently
    /// re-open the defect the converter exists to close — Tab escaping into an inactive column, where the
    /// operator writes real data into the wrong screen with no cue. Failing toward <c>None</c> only costs Tab
    /// traversal, leaves every other key and the F-key bar working, and is noticed at once. Non-destructive and
    /// loud beats destructive and silent.</para>
    /// </summary>
    [Theory]
    [InlineData(true, KeyboardNavigationMode.Continue)]     // the terminal column — the ONLY open case
    [InlineData(false, KeyboardNavigationMode.None)]        // a column behind — contained, the whole point
    public void Tab_navigation_converter_maps_IsLast(bool isLast, KeyboardNavigationMode expected)
        => Assert.Equal(expected, TerminalColumnTabNavigationConverter.Instance
            .Convert(isLast, typeof(KeyboardNavigationMode), null, CultureInfo.InvariantCulture));

    /// <summary>
    /// The degenerate inputs — a broken or unresolved binding, or a retyped <c>IsLast</c>. Every one must fail
    /// CLOSED. If someone later "tidies" the converter into <c>value is not false</c> or similar, these are what
    /// catch it before a silently keyboard-porous column ships.
    /// </summary>
    [Theory]
    [InlineData(null)]          // binding resolved to null
    [InlineData("true")]        // wrong type — a string, not a bool
    [InlineData(1)]             // wrong type — an int
    [InlineData(0)]
    public void Tab_navigation_converter_fails_closed_on_a_broken_binding(object? value)
        => Assert.Equal(KeyboardNavigationMode.None, TerminalColumnTabNavigationConverter.Instance
            .Convert(value, typeof(KeyboardNavigationMode), null, CultureInfo.InvariantCulture));

    /// <summary>
    /// <see cref="AvaloniaProperty.UnsetValue"/> deserves its own test: it is what Avalonia actually passes when
    /// a binding path does not resolve, which is the realistic way this converter breaks in production.
    /// </summary>
    [Fact]
    public void Tab_navigation_converter_fails_closed_on_UnsetValue()
        => Assert.Equal(KeyboardNavigationMode.None, TerminalColumnTabNavigationConverter.Instance
            .Convert(AvaloniaProperty.UnsetValue, typeof(KeyboardNavigationMode), null,
                     CultureInfo.InvariantCulture));

    // ================================================================ no shipped accelerator is shadowed

    /// <summary>
    /// Ctrl+A still reaches the cascade and opens a column. Driven on the Gateway, where it routes to
    /// <c>ActivateSelected</c>.
    ///
    /// <para><b>⚠ SCOPE — this test is deliberately named for what it can prove, and no more.</b> It asserts
    /// only that the keystroke reached a cascade action and a column appeared. It does NOT discriminate
    /// <c>ActivateSelected</c> from a neighbouring action: measured by rebinding Ctrl+A to <c>DrillIn</c>, this
    /// test stayed GREEN, because on the Gateway both push a column. It was previously called
    /// <c>CtrlA_still_activates</c>, which promised a discrimination it never performed.</para>
    ///
    /// <para><b>The binding is nonetheless protected</b> — 16 other tests in the Desktop suite go red on that
    /// same rebinding. What this test adds is the cheap, direct check that Ctrl+A is not swallowed outright by
    /// anything the arbitration slice introduced, which is exactly what it is here for.</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_still_reaches_the_cascade_and_opens_a_column()
    {
        var (window, vm, dir) = NewWindow("Arb CtrlA Co");
        try
        {
            var columnsBefore = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.Columns.Count > columnsBefore);        // it drilled — the arm ran
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Alt+X still cancels the in-progress voucher and returns to the Gateway.</summary>
    [AvaloniaFact]
    public void AltX_still_cancels_the_voucher()
    {
        var (window, vm, dir) = NewWindow("Arb AltX Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.Null(vm.VoucherEntry);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Alt+C still opens Ledger Creation.</summary>
    [AvaloniaFact]
    public void AltC_still_opens_ledger_creation()
    {
        var (window, vm, dir) = NewWindow("Arb AltC Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerMaster);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Alt+A on POS Billing still surfaces the per-rate Tax Analysis.</summary>
    [AvaloniaFact]
    public void AltA_still_shows_pos_tax_analysis()
    {
        var (window, vm, dir) = NewWindow("Arb AltA Co");
        try
        {
            vm.OpenPosBilling();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.PosBilling!.IsTaxAnalysisVisible);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Alt+F5 still opens Debit Note; Alt+F6 still opens Credit Note.</summary>
    [AvaloniaFact]
    public void AltF5_and_AltF6_still_open_the_debit_and_credit_notes()
    {
        var (window, vm, dir) = NewWindow("Arb Notes Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(VoucherBaseType.DebitNote, vm.VoucherEntry!.Type.BaseType);

            vm.CancelVoucher();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(VoucherBaseType.CreditNote, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 F4 IS CONTRA. The single most-guarded binding in this slice — nothing added here may turn F4 into a
    /// dropdown opener.
    /// </summary>
    [AvaloniaFact]
    public void F4_is_still_Contra()
    {
        var (window, vm, dir) = NewWindow("Arb F4 Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Contra, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// F4 stays Contra even with a picker OPEN on the voucher — the guards added by this slice are on Enter and
    /// Escape only, and must not spread to the F-key bar.
    /// </summary>
    [AvaloniaFact]
    public void F4_is_still_Contra_with_a_picker_open()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            Assert.True(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Contra, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Bare F2 on a report still sets the as-of date; Alt+F1/F2/F12 still drive the report panels.</summary>
    [AvaloniaFact]
    public void Report_function_keys_are_unshadowed()
    {
        var (window, vm, dir) = NewWindow("Arb Report Co");
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.True(vm.IsReportContext);

            var detailedBefore = vm.Reports!.Detailed;
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
            Pump(window);
            Assert.NotEqual(detailedBefore, vm.Reports!.Detailed);          // Alt+F1 toggled detail

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);             // Alt+F2 opened config…
            Assert.True(vm.ReportConfig!.UsePeriod);                         // …on the PERIOD window
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ReportSortFilter, vm.CurrentScreen);        // Alt+F12 opened sort/filter
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);            // bare F2 = as-of…
            Assert.False(vm.ReportConfig!.UsePeriod);                       // …i.e. config with NO period window
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The bare-letter panel accelerators — P (print preview), E (export), O (import), Y (export data),
    /// M (e-mail) — each still reaches its shipped panel from the context that owns it.
    /// </summary>
    [AvaloniaFact]
    public void Bare_letter_panel_accelerators_are_unshadowed()
    {
        var (window, vm, dir) = NewWindow("Arb Letters Co");
        try
        {
            // O and Y belong to the bare Gateway.
            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.ImportData, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.ExportData, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            // P, E and M belong to an open report.
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Export, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ V3 — "Enter opens it."
    //
    // USER DECISION: Enter on a focused, CLOSED picker OPENS its dropdown. The MASTER-screen ordering proof lives
    // above in Enter_on_a_focused_but_CLOSED_picker_on_a_master_OPENS_it_and_does_not_prompt (which replaced the
    // superseded "…still_raises_the_accept_prompt"). The tests below cover the non-master open, the two
    // non-regressions the new arm must not break (cascade drill, no-picker prompt is already locked), and the
    // full open→navigate→commit sequence.

    /// <summary>
    /// 🔴 V3 (a) — a focused, CLOSED picker on a NON-master screen (a Payment voucher line's ledger picker)
    /// opens on Enter. Before the fix this Enter fell to <c>ActivateSelected</c> (<c>VoucherEntry.Accept()</c>)
    /// and the dropdown stayed shut; after it, the new arm sets <c>IsDropDownOpen = true</c> and consumes the key
    /// so the ComboBox cannot toggle it back. Proves the gesture is NOT scoped to master screens.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_focused_CLOSED_picker_on_a_voucher_line_OPENS_it()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.False(vm.IsMasterAcceptScreen);                // the non-master half of the contract

            var picker = Descendants(window).OfType<ComboBox>()
                .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            picker.Focus();
            Pump(window);
            Assert.False(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(picker.IsDropDownOpen);                   // V3: Enter opened the closed voucher picker
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// V3 (d) — NON-REGRESSION: on a menu/cascade column (no ComboBox in focus) Enter still DRILLS the cascade
    /// exactly as before. The new V3 arm is scoped to <c>IsPickerFocusedClosed</c>; on the Gateway the focused
    /// element is a menu row, not a ComboBox, so the arm cannot match and Enter falls through to
    /// <c>ActivateSelected</c>, which pushes a column. Asserted by the column count rising, mirroring
    /// <see cref="CtrlA_still_reaches_the_cascade_and_opens_a_column"/>. Green before AND after the fix.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_menu_column_still_drills_the_cascade()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            Assert.True(vm.IsGatewayCascade);
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.Columns.Count > columnsBefore);        // it drilled — the cascade Enter path is intact
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 V3 (e) — THE FULL SEQUENCE: focus a closed picker → Enter OPENS it → Down highlights a row → Enter
    /// COMMITS the highlighted row and CLOSES. Proves the V3 open gesture composes with the existing arrow- and
    /// Enter-yields (<c>!IsPickerOpen</c>): once open, the window steps aside and the ComboBox owns Down (move
    /// highlight) and Enter (commit + close).
    ///
    /// <para><b>Harness note.</b> The class remarks warn that a BARE ComboBox in a BARE window would not commit
    /// on Enter. The real <see cref="MainWindow"/> was MEASURED to differ: with a row highlighted the open
    /// dropdown consumes Enter and closes (commit is conditional on a highlight, which the Down press supplies).
    /// This test asserts that measured behaviour end-to-end; if a future framework change breaks the commit, the
    /// two <c>Assert</c>s at the tail localise it precisely.</para>
    /// </summary>
    [AvaloniaFact]
    public void Enter_opens_then_Down_highlights_then_Enter_commits_and_closes()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            var picker = Descendants(window).OfType<ComboBox>()
                .First(c => c.IsEffectivelyVisible && c.ItemCount > 2);
            picker.Focus();
            Pump(window);
            Assert.False(picker.IsDropDownOpen);

            // Enter OPENS (V3).
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.True(picker.IsDropDownOpen);

            // Down HIGHLIGHTS a row inside the open dropdown (arrows yield to the open picker).
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            var highlighted = HighlightedRow(window, picker);
            Assert.True(highlighted >= 0, "no dropdown row holds focus after Down — the commit half would be vacuous");
            var expected = picker.Items[highlighted];

            // Enter COMMITS the highlighted row and CLOSES (measured real-MainWindow behaviour).
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.False(picker.IsDropDownOpen);                  // committed and closed
            Assert.Equal(expected, picker.SelectedItem);          // the HIGHLIGHTED row is what committed
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ V8 — a stray Y must not save behind a dropdown

    /// <summary>
    /// 🔴 V8 (f) — THE STRAY-Y BUG, inverted. With the accept prompt open AND a dropdown open, a bare Y used to
    /// reach the WI-11 confirm arm (<c>:261</c>) and SAVE the master — measured
    /// <c>promptOpen=True dropdownOpen=True ledgers 38 → 39 created=True</c>. A Y the operator meant as
    /// type-ahead into the dropdown silently committed the ledger. The fix guards that arm with
    /// <c>!IsPickerOpen(e)</c> so it YIELDS while a dropdown is up.
    ///
    /// <para>The test proves BOTH halves: (1) with both open, Y does NOT save (ledger count unchanged) and the
    /// prompt survives; (2) after the dropdown closes, Y saves — the guard yields WITHOUT stranding the prompt,
    /// so there is always a keyboard way to answer it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Y_with_prompt_and_dropdown_open_does_not_save_but_saves_once_the_dropdown_closes()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);     // dropdown OPEN on the master
            var before = vm.Company!.Ledgers.Count;
            vm.LedgerMaster!.Name = "V8 Stray-Y Probe Ledger";     // a valid, unique name so a save WOULD add one
            Assert.True(vm.RequestMasterAccept());                 // prompt OPEN, over the open dropdown
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.True(picker.IsDropDownOpen);

            // (1) Y with BOTH open — the confirm arm must yield; nothing is saved and the prompt stays up.
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(before, vm.Company!.Ledgers.Count);       // the V8 bug, inverted: NOT saved
            Assert.Null(vm.Company!.FindLedgerByName("V8 Stray-Y Probe Ledger"));
            Assert.True(vm.IsAcceptPromptOpen);                    // prompt not consumed by Y
            Assert.True(picker.IsDropDownOpen);                    // Y reached the dropdown, not the confirm

            // (2) close the dropdown, then Y — now the guard admits it and the master saves. Prompt not stranded.
            picker.IsDropDownOpen = false;
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(before + 1, vm.Company!.Ledgers.Count);   // saved
            Assert.NotNull(vm.Company!.FindLedgerByName("V8 Stray-Y Probe Ledger"));
            Assert.False(vm.IsAcceptPromptOpen);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// V8 (g) — NON-REGRESSION: with the accept prompt open and NO dropdown open, Y still SAVES and N still
    /// DISMISSES, exactly as before. This is the arm the guard sits on; if <c>!IsPickerOpen</c> were mis-written
    /// so the guard failed closed on a closed picker, this is what catches it.
    /// </summary>
    [AvaloniaFact]
    public void Y_saves_and_N_dismisses_when_the_prompt_is_open_with_no_dropdown()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            // Y saves.
            vm.ShowLedgerMaster();
            Pump(window);
            var before = vm.Company!.Ledgers.Count;
            vm.LedgerMaster!.Name = "V8 Bare-Prompt Save Ledger";
            Assert.True(vm.RequestMasterAccept());
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(before + 1, vm.Company!.Ledgers.Count);   // Y saved
            Assert.False(vm.IsAcceptPromptOpen);

            // N dismisses without saving.
            vm.ShowLedgerMaster();
            Pump(window);
            var before2 = vm.Company!.Ledgers.Count;
            vm.LedgerMaster!.Name = "V8 Bare-Prompt Dismiss Ledger";
            Assert.True(vm.RequestMasterAccept());
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);                   // N dismissed the prompt
            Assert.Equal(before2, vm.Company!.Ledgers.Count);      // …without saving
            Assert.Null(vm.Company!.FindLedgerByName("V8 Bare-Prompt Dismiss Ledger"));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 V8 + two-press Escape COMPOSITION. With the accept prompt open AND a dropdown open, the FIRST Escape
    /// must close the DROPDOWN and leave the prompt up (the confirm arm yields via <c>!IsPickerOpen</c>, and the
    /// navigation Escape arm yields too, so the ComboBox closes itself); a SECOND Escape, now that no dropdown is
    /// open, reaches the prompt and DISMISSES it. This is the "do not strand the prompt" guarantee for Escape,
    /// the twin of what V8 (f) proves for Y.
    /// </summary>
    [AvaloniaFact]
    public void Escape_with_prompt_and_dropdown_open_closes_dropdown_first_then_dismisses_the_prompt()
    {
        var (window, vm, dir) = OpenPopulated();
        try
        {
            var picker = OpenPickerOnLedgerMaster(window, vm);
            var columnsBefore = vm.Columns.Count;
            Assert.True(vm.RequestMasterAccept());
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.True(picker.IsDropDownOpen);

            // Press one: closes the dropdown, prompt survives, column not popped.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.False(picker.IsDropDownOpen);                  // dropdown closed
            Assert.True(vm.IsAcceptPromptOpen);                   // prompt NOT dismissed by press one
            Assert.Equal(columnsBefore, vm.Columns.Count);        // and the column did NOT pop

            // Press two: no dropdown left, so Escape reaches the prompt and dismisses it (still no column pop).
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);                  // prompt dismissed
            Assert.Equal(columnsBefore, vm.Columns.Count);        // dismiss, not navigate
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        }
        finally { Cleanup(window, dir); }
    }
}
