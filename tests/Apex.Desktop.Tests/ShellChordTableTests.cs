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

    // ================================================================ Ctrl+I — the release

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY <c>main</c>.</b> There, <c>Ctrl+I</c> on an open Sales voucher toggled item-invoice
    /// mode. It is the vendor's More Details chord and now opens that panel; the mode is unchanged.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_I_opens_more_details_and_no_longer_toggles_item_invoice()
    {
        var (window, vm) = OpenWindow("Ctrl I Release Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            var modeBefore = entry.IsItemInvoice;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);

            Assert.Equal(modeBefore, entry.IsItemInvoice);   // the toggle did NOT run
            Assert.NotNull(vm.MoreDetails);
            Assert.Equal(Screen.MoreDetails, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE NO-LOSS PROOF, AND IT MUST NEVER BE DELETED.</b> Releasing <c>Ctrl+I</c> costs no capability
    /// only because <c>Ctrl+H</c> "Change Mode" — the vendor's own chord for changing voucher mode — still
    /// reaches the invoice modes. This passes before and after the release; the day it goes red, the release
    /// has become a deletion.
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

    /// <summary>
    /// 🔴 <b>THE APP-WIDE-SWALLOW REGRESSION.</b> Off a voucher screen <c>Ctrl+I</c> must claim nothing —
    /// no panel, no screen change, no toggle. On <c>main</c> the keystroke was consumed here too.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_I_claims_nothing_on_a_report()
    {
        var (window, vm) = OpenWindow("Ctrl I Report Co");
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            var screenBefore = vm.CurrentScreen;
            var columnsBefore = vm.Columns.Count;

            Assert.True(ShellChordTable.Match(vm, Key.I, KeyModifiers.Control) is null,
                "Ctrl+I is still claimed off a voucher screen — the app-wide swallow is back.");

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);

            Assert.Null(vm.MoreDetails);
            Assert.Equal(screenBefore, vm.CurrentScreen);
            Assert.Equal(columnsBefore, vm.Columns.Count);
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

    /// <summary>With no company open, <c>Ctrl+F3</c> has nothing to shut and must not be claimed.</summary>
    [Fact]
    public void Ctrl_F3_is_not_claimed_with_no_company_open()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.True(ShellChordTable.Match(vm, Key.F3, KeyModifiers.Control) is null);
    }
}
