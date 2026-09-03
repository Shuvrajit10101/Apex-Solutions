using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 10.11 slice S1 — THE MODIFIER HOLE in the bare-letter quick-jumps (VL-2 step 1).
///
/// <para><b>The defect.</b> <c>MainWindow.axaml.cs</c>'s <c>CanQuickJump</c> tested only
/// <c>IsMenuScreen &amp;&amp; !IsTyping(e)</c> and the enclosing <c>switch (e.Key)</c> tests no modifiers
/// either, so the four report quick-jumps (B Balance Sheet · P Profit &amp; Loss · T Trial Balance ·
/// D Day Book) fired for EVERY modifier combination that no earlier arm had already claimed. Alt+D
/// therefore opened the Day Book on the Company Select screen — the one screen the quick-jumps are
/// reachable on at all (once a company is open <c>IsGatewayCascade</c> is true and
/// <see cref="MainWindowViewModel.IsMenuScreen"/> is false).</para>
///
/// <para><b>Why it had to be closed on its own, first.</b> A later slice in this phase binds Alt+D to
/// DELETE. Binding a destructive verb on top of a chord that already fires a navigation would make a
/// stray Alt+D both destructive and ambiguous — which arm wins would depend on the screen.</para>
///
/// <para><b>Every binding here is driven through the REAL tunnel handler</b> (<c>window.KeyPressQwerty</c>),
/// never by calling a handler or a view-model method directly, because the whole defect lives in the
/// first-match-wins ORDER of that handler.</para>
/// </summary>
public sealed class QuickJumpModifierGuardTests
{
    /// <summary>
    /// A window sitting on Company Select with a company still OPEN — the exact state the defect is
    /// reachable in, reached the way an operator reaches it (Gateway → "Quit — Change Company").
    /// The company carries one posted Receipt of ₹98,765.43 (odd paise, deliberately: the Day Book row it
    /// projects is what proves the report really opened, and a round figure would let a formatting or
    /// projection slip pass unseen).
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir, Guid VoucherId) OnCompanySelect(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJump_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();

        vm.NewCompanyName = company;
        vm.CreateCompany();

        // One posted Receipt: Dr Cash ₹98,765.43 / Cr Capital A/c ₹98,765.43.
        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var entry = vm.VoucherEntry!;
        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "98765.43";
        entry.Lines[1].SelectedLedger = capital;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "98765.43";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var voucherId = Assert.Single(vm.Company!.Vouchers).Id;

        // Gateway → F3 "Quit — Change Company" is the operator's route here; ShowCompanySelect is what that
        // menu row calls. It leaves the cascade (IsMenuScreen becomes true) but does NOT null the company.
        vm.ShowCompanySelect();

        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.True(vm.IsMenuScreen);        // vacuity guard: the quick-jump arms are LIVE on this screen…
        Assert.NotNull(vm.Company);          // …and their button-bar items are Enabled (hasCompany).
        Assert.Null(vm.Reports);
        return (window, vm, dir, voucherId);
    }

    private static void Close(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>Asserts no report opened at all and the operator is still on Company Select.</summary>
    private static void AssertStillOnCompanySelect(MainWindowViewModel vm)
    {
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.Null(vm.Reports);
    }

    // ============================================================ (a) the bare letters still work

    /// <summary>
    /// THE NO-REGRESSION LOCK. Bare D still opens the Day Book from Company Select, and the report really is
    /// projected — the ₹98,765.43 Receipt is on it, drillable. Closing the modifier hole must not cost the
    /// quick-jump its actual job.
    /// </summary>
    [AvaloniaFact]
    public void Bare_D_on_company_select_still_opens_the_day_book()
    {
        var (window, vm, dir, voucherId) = OnCompanySelect("QuickJump Bare D Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);
            // The ₹98,765.43 Receipt is really on it — the report was projected, not merely instantiated.
            Assert.Contains(vm.Reports!.Rows, r => r.DrillVoucherId == voucherId);
            Assert.Contains(vm.Reports!.Rows, r =>
                r.Debit.Contains("98,765.43") || r.Credit.Contains("98,765.43") || r.Amount.Contains("98,765.43"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>The other three bare quick-jumps are untouched too — B, P and T still open their reports.</summary>
    [AvaloniaFact]
    public void Bare_B_P_and_T_on_company_select_still_open_their_reports()
    {
        foreach (var (key, kind) in new[]
                 {
                     (PhysicalKey.B, ReportKind.BalanceSheet),
                     (PhysicalKey.P, ReportKind.ProfitAndLoss),
                     (PhysicalKey.T, ReportKind.TrialBalance),
                 })
        {
            var (window, vm, dir, _) = OnCompanySelect($"QuickJump Bare {key} Co");
            try
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);

                Assert.Equal(Screen.Report, vm.CurrentScreen);
                Assert.NotNull(vm.Reports);
                Assert.Equal(kind, vm.Reports!.Kind);
            }
            finally { Close(window, dir); }
        }
    }

    // ============================================================ (b) the hole itself

    /// <summary>
    /// THE HEADLINE DRIVING TEST. Alt+D must NOT open the Day Book. Before the fix it did: the bare-letter
    /// arm matched because <c>CanQuickJump</c> never looked at <c>e.KeyModifiers</c>, so the chord a later
    /// slice binds to DELETE silently navigated instead.
    /// </summary>
    [AvaloniaFact]
    public void Alt_D_on_company_select_does_not_open_the_day_book()
    {
        var (window, vm, dir, _) = OnCompanySelect("QuickJump Alt D Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            AssertStillOnCompanySelect(vm);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The three survivors a per-arm fix would have left behind. The hole was in <c>CanQuickJump</c>, not in
    /// the D arm, so Alt+B, Alt+P and Alt+T fired their reports too — and only fixing the shared guard
    /// closes all four at once.
    /// </summary>
    [AvaloniaFact]
    public void Alt_B_Alt_P_and_Alt_T_on_company_select_do_not_open_their_reports()
    {
        foreach (var key in new[] { PhysicalKey.B, PhysicalKey.P, PhysicalKey.T })
        {
            var (window, vm, dir, _) = OnCompanySelect($"QuickJump Alt {key} Co");
            try
            {
                window.KeyPressQwerty(key, RawInputModifiers.Alt);
                AssertStillOnCompanySelect(vm);
            }
            finally { Close(window, dir); }
        }
    }

    /// <summary>
    /// Ctrl and Shift were holes too, not just Alt. Ctrl+D and Ctrl+P reached the quick-jumps because no
    /// earlier arm claims those chords (Ctrl+P's own arm at the P/Print-preview block requires
    /// <c>IsPrintablePage</c>, false on Company Select); Shift+D is what an operator gets holding Shift for a
    /// capital letter. The guard is <c>KeyModifiers.None</c> — the same predicate the WI-2/WI-9 bare-letter
    /// menu arm already uses — so all three are inert.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_and_Shift_over_the_quick_jump_letters_are_inert_on_company_select()
    {
        foreach (var (key, mods) in new[]
                 {
                     (PhysicalKey.D, RawInputModifiers.Control),
                     (PhysicalKey.P, RawInputModifiers.Control),
                     (PhysicalKey.D, RawInputModifiers.Shift),
                     (PhysicalKey.B, RawInputModifiers.Shift),
                 })
        {
            var (window, vm, dir, _) = OnCompanySelect($"QuickJump {key}{mods} Co");
            try
            {
                window.KeyPressQwerty(key, mods);
                AssertStillOnCompanySelect(vm);
            }
            finally { Close(window, dir); }
        }
    }

    /// <summary>
    /// The other direction of the ordering proof: a chord an EARLIER arm already owns keeps its owner. Ctrl+T
    /// is claimed by the post-dated toggle far above the quick-jump switch, so it never reached the T arm
    /// before this change and must not start reaching it after — the Trial Balance stays shut.
    ///
    /// <para><b>Ownership is asserted WHERE IT IS OBSERVABLE (review fix, finding 6).</b> The first version of
    /// this test only pressed Ctrl+T on Company Select and asserted "no Trial Balance opened". That assertion
    /// cannot fail: <c>CanQuickJump</c>'s new <c>== KeyModifiers.None</c> guard alone keeps the report shut,
    /// and <see cref="MainWindowViewModel.TogglePostDated"/> is a guaranteed no-op off a voucher screen — so
    /// deleting the whole Ctrl+T arm the test is NAMED for left it green. It now presses Ctrl+T on a live
    /// Receipt, where the owner arm's action is visible, and flips the post-dated flag on and back off. Delete
    /// or shadow the arm and this fails immediately.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_T_stays_with_the_arm_that_already_owns_it_and_never_opens_the_trial_balance()
    {
        // (i) ownership, where the owner's action is observable: on a voucher Ctrl+T toggles post-dated.
        {
            var (window, vm, dir, _) = OnCompanySelect("QuickJump Ctrl T Owner Co");
            try
            {
                vm.ShowGateway();
                vm.OpenVoucher(VoucherBaseType.Receipt);
                Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
                Assert.False(vm.VoucherEntry!.IsPostDated);

                window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
                Assert.True(vm.VoucherEntry!.IsPostDated);   // the :507 arm ran — it still owns Ctrl+T
                Assert.Null(vm.Reports);                     // …and the quick-jump did NOT also fire

                window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
                Assert.False(vm.VoucherEntry!.IsPostDated);  // and it is a real toggle, not a one-way latch
            }
            finally { Close(window, dir); }
        }

        // (ii) the inert direction: on Company Select the same chord opens nothing at all.
        {
            var (window, vm, dir, _) = OnCompanySelect("QuickJump Ctrl T Co");
            try
            {
                window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
                AssertStillOnCompanySelect(vm);
            }
            finally { Close(window, dir); }
        }
    }

    // ============================================================ (c) the settled bare-letter rules are undisturbed

    /// <summary>
    /// The WI-2 / WI-9 contract this change sits next to, re-locked in BOTH directions because closing a
    /// bare-letter hole is exactly the kind of edit that could disturb it: on an AUTHORED menu column a bare
    /// letter still ACTIVATES its row, and on a DATA-DRIVEN picker column the same class of key still
    /// FILTERS. Neither path runs through <c>CanQuickJump</c> (they are mutually exclusive —
    /// <c>IsMenuScreen</c> requires <c>!IsGatewayCascade</c> and <c>HandleMenuLetter</c> returns false unless
    /// <c>IsGatewayCascade</c>), and this test is what proves that separation held.
    /// </summary>
    [AvaloniaFact]
    public void Bare_letter_activate_and_picker_filter_are_undisturbed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpWi2_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.NewCompanyName = "QuickJump WI2 Co";
            vm.CreateCompany();
            foreach (var name in new[] { "Zenith Traders", "Aarti Steel" })
            {
                vm.ShowLedgerMaster();
                vm.LedgerMaster!.Name = name;
                vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
                vm.LedgerMaster!.Create();
            }

            // AUTHORED column: a bare letter still ACTIVATES.
            vm.ShowGateway();
            Assert.Equal(GatewayColumnKind.Authored, vm.ActiveColumnKind);
            var columnsBefore = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);

            // DATA-DRIVEN column: the same class of key still FILTERS.
            vm.ShowLedgerBooksMenu();
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);
            var pickerColumns = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
            Assert.Equal(pickerColumns, vm.Columns.Count);
            Assert.Equal("Z", vm.ActiveTypeAheadPrefix);
            Assert.Equal("Zenith Traders", vm.Columns[^1].Selected!.Label);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (d) the SECOND hole the sweep found

    /// <summary>
    /// Ledger Creation, fully keyed (name + group + an odd-paise opening balance), with the WI-11
    /// "Accept Ledger? (Y/N)" confirmation raised through the REAL Enter arm. This is the exact state both
    /// halves of the Alt-modifier question live in.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) LedgerCreationWithPromptUp(
        string company, string ledgerName, string openingBalanceText)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpPrompt_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();

        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = ledgerName;
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
        vm.LedgerMaster!.OpeningBalanceText = openingBalanceText;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(vm.IsAcceptPromptOpen);
        Assert.Equal("Accept Ledger? (Y/N)", vm.AcceptPromptText);
        return (window, vm, dir);
    }

    /// <summary>
    /// Asserts the whole point of the Alt narrowing: the chord did NOT answer the confirmation in either
    /// direction, and — the review fix — did not destroy the half-typed master on the way past either. The
    /// operator is left looking at exactly the question they were asked, with everything they keyed intact.
    /// </summary>
    private static void AssertPromptAndTypingBothSurvived(
        MainWindowViewModel vm, string ledgerName, string openingBalanceText)
    {
        // NOT saved — the chord did not confirm.
        Assert.Null(vm.Company!.FindLedgerByName(ledgerName));
        // NOT torn down — the chord did not navigate away and take the master with it.
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        Assert.NotNull(vm.LedgerMaster);
        Assert.Equal(ledgerName, vm.LedgerMaster!.Name);
        Assert.Equal("Sundry Debtors", vm.LedgerMaster!.SelectedGroup!.Name);
        Assert.Equal(openingBalanceText, vm.LedgerMaster!.OpeningBalanceText);
        // NOT dismissed — the question is still on screen, waiting for a real answer.
        Assert.True(vm.IsAcceptPromptOpen);
        Assert.Equal("Accept Ledger? (Y/N)", vm.AcceptPromptText);
        Assert.NotEqual(GatewayMenu.Data, vm.CurrentGatewayMenu);
    }

    /// <summary>
    /// THE SECOND HOLE, and the more dangerous of the two. The WI-11 "Accept? (Y/N)" arm excluded Control but
    /// NOT Alt, so with the prompt up a stray <b>Alt+Y</b> — the Data / Backup-Restore accelerator, live on
    /// every screen a company is open on — reached <c>ConfirmMasterAccept</c> and SAVED the master. The
    /// operator asked for a menu and silently committed a record.
    ///
    /// <para><b>REVIEW FIX — the first cut of this slice traded a bad SAVE for a worse DESTROY.</b> Narrowing
    /// the arm with <c>when !altHeld</c> made Alt+Y YIELD, and the arm it yielded to is the Alt+Y owner
    /// (<c>MainWindow.axaml.cs:633</c>) → <c>ShowDataMenu</c> → <c>SelectRootItem</c> →
    /// <c>TrimColumnsAfter(0)</c> + <c>OpenSubmenuColumn</c> → <c>ClearSubScreens</c>, which NULLS
    /// <see cref="MainWindowViewModel.LedgerMaster"/>. Measured: the ledger was not saved (the fix worked) but
    /// the whole half-typed master was silently discarded and the operator landed on Backup / Restore — the
    /// identical D2 work-loss class this arm already exempts Escape for. Alt+Y and Alt+N are now CONSUMED AND
    /// INERT while a confirmation is up: nothing is saved, nothing is destroyed, the question stays on screen.
    /// Two presses (answer N/Esc, then Alt+Y) — the same doctrine already settled for Escape.</para>
    ///
    /// <para>This matters far beyond WI-11: this one prompt is the confirmation channel a later slice hangs
    /// DELETE on, so the same hole would have made Alt+Y confirm a deletion the operator never answered.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_Y_with_the_accept_prompt_open_neither_saves_the_master_nor_discards_it()
    {
        var (window, vm, dir) = LedgerCreationWithPromptUp("QuickJump AltY Co", "Bharat Motors", "47382.19");
        try
        {
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Alt);
            AssertPromptAndTypingBothSurvived(vm, "Bharat Motors", "47382.19");

            // …and the prompt is not stranded: a real bare Y still confirms straight afterwards.
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindLedgerByName("Bharat Motors"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The N twin, which had ZERO coverage (review finding 4): the nine tests of the first cut never pressed
    /// Alt+N at all, and the only Alt+N press anywhere in this project asserts the prompt is CLOSED before it
    /// presses. Revert the N narrowing and this fails on the very first assertion, because bare-N's
    /// <c>DismissMasterAccept</c> would have swallowed the Alt chord and closed the confirmation.
    /// </summary>
    [AvaloniaFact]
    public void Alt_N_with_the_accept_prompt_open_leaves_the_prompt_up_and_saves_nothing()
    {
        var (window, vm, dir) = LedgerCreationWithPromptUp("QuickJump AltN Co", "Chetan Alloys", "68204.37");
        try
        {
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Alt);
            AssertPromptAndTypingBothSurvived(vm, "Chetan Alloys", "68204.37");

            // …and a real bare N still dismisses it, saving nothing.
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Null(vm.Company!.FindLedgerByName("Chetan Alloys"));
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Review finding 5 — the outcome must NOT depend on where the caret happens to be. The Alt+Y owner arm
    /// at <c>:633</c> requires <c>!IsTyping(e)</c> (which tests only <c>e.Source is TextBox</c>), so a
    /// fall-through design gives two different outcomes for the same chord: inert when the caret sits in the
    /// Name box, work-destroying when it does not. Consuming the chord in the WI-11 arm — which checks no
    /// focus condition at all — collapses both to the same safe answer, and this pins the half that the
    /// fall-through design never covered.
    /// </summary>
    [AvaloniaFact]
    public void Alt_Y_is_inert_with_the_prompt_open_whether_or_not_the_caret_is_in_the_name_field()
    {
        var (window, vm, dir) = LedgerCreationWithPromptUp("QuickJump AltY Caret Co", "Deepak Castings", "91735.61");
        try
        {
            // Put REAL focus in the REAL Name TextBox (the one carrying the typed name), so the tunnel
            // handler sees e.Source is TextBox — the operator's actual state after keying the field.
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var nameBox = window.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && b.Text == "Deepak Castings");
            Assert.NotNull(nameBox);
            nameBox!.Focus();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Alt);
            AssertPromptAndTypingBothSurvived(vm, "Deepak Castings", "91735.61");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// THE WORST FORM of the fall-through, and the reason this could not be left as a "trade-off". Alt+C
    /// create-on-the-fly pushes Ledger Creation NON-DESTRUCTIVELY over a live voucher, so the accept prompt
    /// can be raised with a half-keyed invoice sitting in the column behind it. A fall-through Alt+Y runs
    /// <c>TrimColumnsAfter(0)</c>, which removes the VOUCHER column too, and <c>ClearSubScreens</c> nulls
    /// <see cref="MainWindowViewModel.VoucherEntry"/> — the entire invoice, gone to a menu chord. The odd
    /// paise (₹1,23,456.78) are deliberate: they are what proves the surviving voucher is the SAME one, not a
    /// freshly re-opened blank that a round figure would let pass.
    /// </summary>
    [AvaloniaFact]
    public void Alt_Y_over_a_create_on_the_fly_master_leaves_the_live_voucher_intact()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpAltYFly_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.NewCompanyName = "QuickJump AltY Fly Co";
            vm.CreateCompany();
            var cash = vm.Company!.FindLedgerByName("Cash")!;

            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.ChangeMode();                 // out of Single Entry: Lines[0]'s amount is typed, not derived
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].Side = DrCr.Debit;
            entry.Lines[0].AmountText = "123456.78";

            // Alt+C on the party field opens Ledger Creation OVER the live voucher (WI-1, non-destructive).
            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, entry.Lines[1]));
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.True(vm.IsCreateOnTheFlyOpen);
            Assert.Same(entry, vm.VoucherEntry);

            vm.LedgerMaster!.Name = "Bright Traders";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Alt);

            // The invoice survived — same instance, same odd-paise line, still the create-on-the-fly context.
            Assert.NotNull(vm.VoucherEntry);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("123456.78", vm.VoucherEntry!.Lines[0].AmountText);
            Assert.Same(cash, vm.VoucherEntry!.Lines[0].SelectedLedger);
            Assert.True(vm.IsCreateOnTheFlyOpen);
            // …and so did the master and its unanswered question.
            Assert.NotNull(vm.LedgerMaster);
            Assert.Equal("Bright Traders", vm.LedgerMaster!.Name);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Null(vm.Company!.FindLedgerByName("Bright Traders"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The narrowing is exactly Y and N with Alt, and nothing else. Bare Y still confirms; bare N still
    /// dismisses; and <b>Alt+Escape still dismisses</b> — Escape is not a letter, owns no Alt accelerator,
    /// and narrowing it too would have sent Alt+Escape to <c>Back()</c>, popping the column and discarding
    /// the half-typed master. That is the D2 work-loss class, so it is pinned here rather than left to luck.
    /// </summary>
    [AvaloniaFact]
    public void Bare_Y_and_N_still_answer_the_prompt_and_Alt_Escape_still_dismisses_it()
    {
        // bare Y confirms
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpY_" + Guid.NewGuid().ToString("N"));
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            var window = new MainWindow { DataContext = vm };
            window.Show();
            try
            {
                vm.NewCompanyName = "QuickJump Y Co";
                vm.CreateCompany();
                vm.ShowLedgerMaster();
                vm.LedgerMaster!.Name = "Aarti Steel";
                vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.True(vm.IsAcceptPromptOpen);

                window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

                Assert.False(vm.IsAcceptPromptOpen);
                Assert.NotNull(vm.Company!.FindLedgerByName("Aarti Steel"));
            }
            finally { Close(window, dir); }
        }

        // bare N dismisses, saving nothing
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpN_" + Guid.NewGuid().ToString("N"));
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            var window = new MainWindow { DataContext = vm };
            window.Show();
            try
            {
                vm.NewCompanyName = "QuickJump N Co";
                vm.CreateCompany();
                vm.ShowLedgerMaster();
                vm.LedgerMaster!.Name = "Chetan Alloys";
                vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.True(vm.IsAcceptPromptOpen);

                window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);

                Assert.False(vm.IsAcceptPromptOpen);
                Assert.Null(vm.Company!.FindLedgerByName("Chetan Alloys"));
                Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            }
            finally { Close(window, dir); }
        }

        // Alt+Escape still dismisses the prompt AND leaves the half-typed master on screen
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApexQuickJumpEsc_" + Guid.NewGuid().ToString("N"));
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            var window = new MainWindow { DataContext = vm };
            window.Show();
            try
            {
                vm.NewCompanyName = "QuickJump Esc Co";
                vm.CreateCompany();
                vm.ShowLedgerMaster();
                vm.LedgerMaster!.Name = "Deepak Castings";
                vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.True(vm.IsAcceptPromptOpen);

                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Alt);

                Assert.False(vm.IsAcceptPromptOpen);
                Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);       // NOT popped — the form survived
                Assert.Equal("Deepak Castings", vm.LedgerMaster!.Name);    // …with the typing intact
                Assert.Null(vm.Company!.FindLedgerByName("Deepak Castings"));
            }
            finally { Close(window, dir); }
        }
    }
}
