using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>THE SHELL CHORD TABLE</b> — the single, testable list of top-level navigation chords, and the three
/// arbitration defects it exists to close.
///
/// <para><b>The defects, all three measured on <c>main</c> before this table.</b>
/// <list type="number">
/// <item><b><c>Ctrl+I</c> was swallowed app-wide.</b> Its arm in <c>MainWindow.OnKeyDown</c> read
/// <c>if (e.Key == Key.I &amp;&amp; e.KeyModifiers.HasFlag(Control)) { vm.ToggleItemInvoice(); e.Handled = true; }</c>
/// — <b>no context guard of any kind</b>. On the ~157 screens where the toggle is a no-op the keystroke was
/// still consumed, and the chord the vendor gives to More Details was spent on a verb that already has its own
/// vendor-attested chord (<c>Ctrl+H</c>).</item>
/// <item><b><c>Alt+F3</c> and <c>Ctrl+F3</c> were silently aliased to bare <c>F3</c>.</b> Neither the Control
/// F-key block nor the Alt F-key block carries an <c>F3</c> case, so both fell through to
/// <c>case Key.F3: Fire(vm, "F3")</c> in the trailing switch, which has no modifier guard. Nothing documented
/// that alias. Claiming the two chords is therefore a <b>NARROWING</b>, and the test below proves bare
/// <c>F3</c> is untouched by it.</item>
/// <item><b>Re-pointing a chord was not a one-line change.</b> It meant inserting an arm at exactly the right
/// index in a ~55-arm first-match-wins chain whose ordering is load-bearing and documented in ~40 comment
/// blocks. Two census rows were blocked on that.</item>
/// </list></para>
///
/// <para>The chords are driven through <see cref="MainWindow"/>'s REAL key handler, not by calling the view
/// model methods — the arbitration IS the subject, so a test that called <c>vm.OpenSwitchTo()</c> directly
/// would pass on a build where the chord reaches nothing.</para>
/// </summary>
public sealed class ShellChordTableTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ShellChordTableTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexShellChord_" + Guid.NewGuid().ToString("N"));
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

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindow(string name)
    {
        var vm = NewCompany(name);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        return (window, vm);
    }

    /// <summary>
    /// Raises one key-down through the window's REAL handler and reports whether the shell CONSUMED it.
    ///
    /// <para>🔴 Observing <c>e.Handled</c> is the point, not a convenience. A chord that is swallowed while its
    /// action self-guards into a no-op leaves every view-model flag exactly as it was, so a test written
    /// against the flag passes on the broken build — the vacuity that
    /// <c>ServiceAccountingInvoiceKeyboardTests.CtrlH_is_unhandled_on_a_voucher_with_no_alternative_mode</c>
    /// records having shipped once already. Copied from that file rather than shared, because a helper this
    /// small is cheaper duplicated than coupled across two test classes.</para>
    /// </summary>
    private static bool KeyWasHandled(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = window,
        };
        window.RaiseEvent(args);
        return args.Handled;
    }

    // ================================================================ the table's own invariants

    /// <summary>
    /// 🔴 <b>Every entry matches its modifiers EXACTLY.</b> The table's <c>Match</c> compares with <c>==</c>,
    /// never <c>HasFlag</c>, and this test pins the consequence rather than the implementation: a chord's
    /// SUPERSET must not fire it. <c>HasFlag</c> matching is what made <c>Ctrl+Alt+I</c> fire the
    /// <c>Ctrl+I</c> arm, and exact matching is what makes claiming <c>Alt+F3</c> safe.
    /// </summary>
    [Fact]
    public void No_chord_fires_on_a_superset_of_its_own_modifiers()
    {
        var vm = NewCompany("Exact Modifiers Co");

        foreach (var chord in ShellChordTable.Table)
        {
            var superset = chord.Modifiers | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control;
            if (superset == chord.Modifiers) continue;   // nothing bigger to try

            Assert.True(
                ShellChordTable.Match(vm, chord.Key, superset) is null,
                $"{chord.Id} fired on the modifier superset {superset} — the table is matching with HasFlag "
                + "semantics, which is exactly what silently aliased Alt+F3 and Ctrl+F3 onto bare F3.");
        }
    }

    /// <summary>
    /// 🔴 <b>THE ANTI-COLLISION INVARIANT.</b> Two entries may share a (key, modifiers) pair only if their
    /// context predicates are disjoint in every state this test can build. Anything else is a chord whose
    /// meaning depends on table order — the class of defect that let <c>Alt+F3</c> mean "Company" for years
    /// without anyone choosing it.
    /// </summary>
    [Fact]
    public void No_two_entries_claim_the_same_keystroke_in_the_same_context()
    {
        var vm = NewCompany("No Collision Co");

        var clashes = ShellChordTable.Table
            .GroupBy(c => (c.Key, c.Modifiers))
            .Where(g => g.Count() > 1)
            .Where(g => g.Count(c => c.CanFire(vm)) > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.True(clashes.Length == 0,
            "Two table entries claim the same keystroke in the same context: "
            + string.Join(", ", clashes.Select(c => $"{c.Modifiers}+{c.Key}")));
    }

    /// <summary>Every entry carries a canonical, non-empty id — tests and messages name chords by it.</summary>
    [Fact]
    public void Every_entry_is_named()
    {
        Assert.All(ShellChordTable.Table, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        Assert.Equal(
            ShellChordTable.Table.Select(c => c.Id).Distinct().Count(),
            ShellChordTable.Table.Count);
    }

    // ================================================================ Ctrl+I — the chord this table does NOT take

    /// <summary>
    /// 🔴 <b>THE LOCK THAT KEEPS 14.4 HONEST.</b> <c>Ctrl+I</c> is the vendor's More Details chord and this
    /// table does not claim it, because claiming it deletes the two-way item-invoice toggle from the keyboard
    /// (see the reasoning beside <c>ShellChordTable.Table</c>) and the census records that ruling as OPEN.
    ///
    /// <para>This asserts the table stays out of the way EVEN ON A VOUCHER, which is the one context where a
    /// More Details entry would be tempting. It is the guard that stops 14.4 being re-landed by chord alone,
    /// without the ruling and without a keyboard door for whatever the toggle becomes — and it is why
    /// <c>ServiceAccountingInvoiceKeyboardTests.CtrlI_stays_a_two_way_item_toggle</c> still passes.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_table_does_not_claim_Ctrl_I_from_the_item_invoice_toggle()
    {
        var (window, vm) = OpenWindow("Ctrl I Incumbent Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            Assert.False(entry.IsItemInvoice);

            Assert.True(ShellChordTable.Match(vm, Key.I, KeyModifiers.Control) is null,
                "The shell chord table has claimed Ctrl+I. That chord belongs to the item-invoice toggle "
                + "until the OPEN U-6 chord ruling says otherwise — see ShellChordTable.Table.");

            // And the incumbent still runs, through its own legacy arm.
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Assert.True(entry.IsItemInvoice);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE APP-WIDE SWALLOW, CLOSED — and this is the half of 14.4 that needed no ruling.</b> On
    /// <c>main</c> the <c>Ctrl+I</c> arm carried NO context guard whatever
    /// (<c>e.Key == Key.I &amp;&amp; e.KeyModifiers.HasFlag(Control)</c>), so it set <c>e.Handled = true</c> on
    /// all ~157 screens while <c>ToggleItemInvoice()</c> self-guards on <c>Screen.VoucherEntry</c> — the chord
    /// was consumed and silently dead on every report, every master and the Gateway. That is WHY census 14.4
    /// grades unreachable rather than mis-keyed: any later-placed arm could never have fired.
    ///
    /// <para><b>This test bites on <c>e.Handled</c>, deliberately</b>, following the remarks on
    /// <c>ServiceAccountingInvoiceKeyboardTests.CtrlH_is_unhandled_on_a_voucher_with_no_alternative_mode</c> —
    /// its own earlier version asserted only the mode flag, which the view-model method guards on its own, so
    /// deleting the tunnel gate left it GREEN. Observing consumption is what actually locks the behaviour.</para>
    ///
    /// <para>🔴 <b>FAILS ON TODAY <c>main</c></b>, where the first assertion below sees <c>true</c>. It is
    /// ruling-neutral: the incumbent keeps the chord on every screen where it does anything, which the second
    /// half asserts so the first cannot pass vacuously by disabling the chord outright.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_I_is_no_longer_swallowed_where_the_item_invoice_toggle_is_a_no_op()
    {
        var (window, vm) = OpenWindow("Ctrl I Swallow Co");
        try
        {
            // 1. The Gateway. ToggleItemInvoice() cannot do anything here, so the key must fall through.
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.False(vm.IsInvoiceableEntry);
            Assert.False(KeyWasHandled(window, Key.I, KeyModifiers.Control),
                "Ctrl+I was CONSUMED on the Gateway, where the item-invoice toggle is a no-op. That is the "
                + "app-wide swallow: it is what makes census 14.4's chord unreachable no matter where a "
                + "More Details arm is later placed.");

            // 2. A report — the surface the vendor's More Details would have to reach.
            vm.OpenReport(ReportKind.BalanceSheet);
            Assert.True(vm.IsReportContext);
            Assert.False(vm.IsInvoiceableEntry);
            Assert.False(KeyWasHandled(window, Key.I, KeyModifiers.Control),
                "Ctrl+I was CONSUMED on a report, where the item-invoice toggle is a no-op.");

            // 3. A Journal — a voucher with no item-invoice mode at all. The screen matches, the TYPE does not,
            //    so this separates the Screen.VoucherEntry half of the guard from the CanBeItemInvoice half.
            vm.OpenVoucher(VoucherBaseType.Journal);
            Assert.False(vm.VoucherEntry!.CanBeItemInvoice);
            Assert.False(vm.IsInvoiceableEntry);
            Assert.False(KeyWasHandled(window, Key.I, KeyModifiers.Control),
                "Ctrl+I was CONSUMED on a Journal, which has no item-invoice mode to toggle into.");

            // 4. 🔴 THE NON-VACUITY CONTRAST. On a Sales the SAME keystroke is still consumed and still flips
            //    the mode — so the three assertions above cannot be passing because the chord was disabled.
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            Assert.True(vm.IsInvoiceableEntry);
            Assert.False(entry.IsItemInvoice);

            Assert.True(KeyWasHandled(window, Key.I, KeyModifiers.Control),
                "Ctrl+I stopped being consumed on a Sales voucher — the incumbent toggle has lost its "
                + "keyboard door, which is exactly what the OPEN U-6 ruling was meant to decide first.");
            Assert.True(entry.IsItemInvoice);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// <c>Ctrl+H</c> "Change Mode" — the vendor's own chord for changing voucher mode — reaches the invoice
    /// modes. Kept because it is the measurement that DISPROVED the premise for releasing <c>Ctrl+I</c>:
    /// Ctrl+H cycles three ways, so it is not a substitute for a two-way toggle.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_H_still_changes_mode_on_an_invoiceable_voucher()
    {
        var (window, vm) = OpenWindow("Ctrl H Preserved Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            Assert.True(vm.IsChangeModeEntry);
            var before = entry.Mode;

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);

            Assert.NotEqual(before, entry.Mode);
        }
        finally { window.Close(); }
    }

    // ================================================================ the F3 family

    /// <summary>
    /// 🔴 <b>THE NARROWING REGRESSION.</b> Bare <c>F3</c> keeps the meaning it always had — the button bar's
    /// "Company" action, i.e. Company Select — even though <c>Alt+F3</c> and <c>Ctrl+F3</c> are now claimed
    /// above it. Exact modifier matching is the whole reason this holds.
    /// </summary>
    [AvaloniaFact]
    public void Bare_F3_still_reaches_the_button_bars_company_action()
    {
        var (window, vm) = OpenWindow("Bare F3 Co");
        try
        {
            Assert.True(ShellChordTable.Match(vm, Key.F3, KeyModifiers.None) is null,
                "The table claimed BARE F3 — that is a theft, not a narrowing.");

            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);

            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.NotNull(vm.Company);   // Company Select does NOT release the open book
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// <c>Alt+F3</c> — vendor: <i>"To select and open another company located in the same folder or other data
    /// paths."</i>
    ///
    /// <para>⚠️ <b>This is a formalisation, not a behaviour change, and saying so is the honest report.</b> On
    /// <c>main</c> Alt+F3 already reached Company Select — by ACCIDENT, through the unguarded bare-F3 arm. The
    /// observable outcome is identical; what changed is that the chord is now DECIDED, by a named table entry
    /// that a reviewer can see. The claim this test can honestly make is that the entry exists, is exact, and
    /// lands where the vendor says.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_F3_opens_company_select_through_a_named_table_entry()
    {
        var (window, vm) = OpenWindow("Alt F3 Co");
        try
        {
            var chord = ShellChordTable.Match(vm, Key.F3, KeyModifiers.Alt);
            Assert.True(chord is not null, "Alt+F3 is not claimed by the table.");
            Assert.Equal("Alt+F3", chord!.Id);

            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Alt);

            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY <c>main</c>.</b> There, <c>Ctrl+F3</c> fell into the bare-F3 arm and merely showed
    /// Company Select with the book still open. Vendor: <i>"To shut the currently loaded companies."</i>
    /// Shutting means the company is RELEASED — that is the assertion <c>main</c> cannot pass.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_F3_shuts_the_open_company_and_returns_to_company_select()
    {
        var (window, vm) = OpenWindow("Ctrl F3 Shut Co");
        try
        {
            Assert.NotNull(vm.Company);

            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Control);

            Assert.Null(vm.Company);                              // 🔴 the release — red on main
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.Equal("No company loaded", vm.StatusCompany);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE WORK-LOSS GUARD ON THE DESTRUCTIVE CHORD — a review finding, and RED ON THIS BRANCH BEFORE THE
    /// FIX.</b> <c>Ctrl+F3</c> shipped predicated on <c>Company is not null</c> alone — no screen guard, no
    /// typing guard — and Shut reaches <c>ClearSubScreens</c>, which nulls <c>VoucherEntry</c> unconditionally.
    /// One keystroke therefore destroyed a half-keyed voucher with no prompt, no notice and no message: the exact
    /// class this codebase spent a campaign closing, re-introduced by the chord that was meant to be a
    /// formalisation.
    ///
    /// <para><b>The three assertions are one claim each, and the first is the one that is easy to get wrong.</b>
    /// The keystroke must still be CONSUMED — a chord that declines instead of refusing falls through to
    /// <c>case Key.F3:</c> in the trailing switch, which has no modifier guard and runs the very
    /// <c>ShowCompanySelect</c> teardown the guard exists to prevent, so "not handled" would be the same data
    /// loss by a longer route. Then the voucher must be the SAME instance with its text intact, and the operator
    /// must be told why nothing happened.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_F3_refuses_over_a_half_keyed_voucher_and_says_why()
    {
        var (window, vm) = OpenWindow("Ctrl F3 Dirty Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

            vm.VoucherEntry!.Narration = "half keyed — must survive the chord";
            Assert.True(vm.VoucherEntry.HasUnsavedWork);
            var entry = vm.VoucherEntry;

            Assert.True(KeyWasHandled(window, Key.F3, KeyModifiers.Control),
                "Ctrl+F3 was NOT consumed — it falls through to the unguarded bare-F3 arm, which tears the "
                + "screen down anyway.");

            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("half keyed — must survive the chord", vm.VoucherEntry!.Narration);
            Assert.NotNull(vm.Company);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Contains("discard", vm.Notice, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The complement of the guard above, and it is what keeps that guard from quietly becoming a way to DISABLE
    /// the chord. On a report page there is nothing keyed to lose, so <c>Ctrl+F3</c> must still shut.
    ///
    /// <para><b>Measured while writing this, and worth recording rather than assuming:</b> a freshly opened
    /// Payment voucher already reports <c>HasUnsavedWork</c> true — its first line is not blank on arrival
    /// (measured: <c>lines=1</c>, narration empty, not altering). So the guard refuses on ANY open voucher-entry
    /// screen in practice, not only a typed-into one. That is inherited behaviour, not a decision taken here:
    /// <c>OpenVoucherFromTypeKey</c>'s shipped guard reads the identical property and therefore already refuses
    /// on exactly the same screens. Narrowing it belongs with that property, not with this chord.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_F3_still_shuts_from_a_report_page()
    {
        var (window, vm) = OpenWindow("Ctrl F3 Report Co");
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Control);

            Assert.Null(vm.Company);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>With no company open, <c>Ctrl+F3</c> has nothing to shut and must not be claimed.</summary>
    [Fact]
    public void Ctrl_F3_is_not_claimed_with_no_company_open()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.True(ShellChordTable.Match(vm, Key.F3, KeyModifiers.Control) is null);
    }
}
