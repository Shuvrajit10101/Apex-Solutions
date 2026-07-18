using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-9 (bare-letter menu hotkeys, the letter drawn red), WI-11 (the "Accept? (Y/N)" confirmation) and the
/// WI-2/WI-9 conflict rule that decides what a bare letter MEANS on a given column.
///
/// <para>Every key binding here is proven by driving the REAL tunnel handler through
/// <c>window.KeyPressQwerty</c> — never by asserting that a binding exists in isolation. The first-match-wins
/// ORDER inside that handler is load-bearing, so the suite proves it in both directions: no earlier arm
/// shadows the new ones, and the new ones shadow nothing that already shipped.</para>
/// </summary>
public sealed class MenuHotKeyAndAcceptTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexHotKey_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        return (window, vm, dir);
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

    // ================================================================ WI-9: computed hotkey letters

    /// <summary>
    /// Letters are COMPUTED per column, not hand-assigned: every selectable row on the Gateway gets one, they
    /// are unique within the column, and each is a letter that actually occurs in that row's own label.
    /// </summary>
    [AvaloniaFact]
    public void Every_gateway_row_gets_a_unique_hotkey_drawn_from_its_own_label()
    {
        var (window, vm, dir) = NewWindow("HotKey Unique Co");
        try
        {
            var rows = vm.Columns[0].Items.Where(i => i.IsSelectable).ToList();
            Assert.NotEmpty(rows);

            foreach (var row in rows)
            {
                Assert.True(row.HasHotKey, $"row '{row.Label}' got no hotkey");
                Assert.InRange(row.HotKeyIndex, 0, row.Label.Length - 1);
                Assert.Equal(row.Label[row.HotKeyIndex], row.HotKey!.Value);
                Assert.True(char.IsLetter(row.HotKey!.Value));
            }

            var letters = rows.Select(r => char.ToUpperInvariant(r.HotKey!.Value)).ToList();
            Assert.Equal(letters.Count, letters.Distinct().Count());   // no two rows answer to one key
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The rule is "first letter unless taken, then the next free letter in the label" — deterministic, so a
    /// column's letters never depend on hand-maintenance. "Create" keeps C; "Chart of Accounts", arriving
    /// second, falls through to its 'h'.
    /// </summary>
    [AvaloniaFact]
    public void The_first_free_letter_wins_and_a_collision_falls_through_to_the_next()
    {
        var (window, vm, dir) = NewWindow("HotKey Rule Co");
        try
        {
            var create = vm.Columns[0].Items.First(i => i.Label == "Create");
            var chart = vm.Columns[0].Items.First(i => i.Label == "Chart of Accounts");

            Assert.Equal(0, create.HotKeyIndex);            // C — first letter, free
            Assert.Equal('C', create.HotKey);
            Assert.Equal(1, chart.HotKeyIndex);             // C taken → 'h'
            Assert.Equal('h', chart.HotKey);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The hotkey lives BESIDE the label, never inside it. Shell code dispatches on <c>Label</c> text
    /// (<c>OpenGroupOf</c> switches on it), so a label mutated to carry a marker would break navigation
    /// silently. Labels must stay byte-identical to what the builders wrote.
    /// </summary>
    [AvaloniaFact]
    public void The_hotkey_is_a_separate_property_and_never_mutates_the_label()
    {
        var (window, vm, dir) = NewWindow("HotKey Label Co");
        try
        {
            var rows = vm.Columns[0].Items.Where(i => i.IsSelectable).ToList();

            foreach (var row in rows)
            {
                // The three render Runs reassemble the label EXACTLY — no character added, dropped or moved.
                // This is the real invariant: whatever the renderer splits, the label itself is untouched.
                Assert.Equal(row.Label, row.HotKeyBefore + row.HotKeyText + row.HotKeyAfter);
            }

            // The exact strings OpenGroupOf switches on are still present verbatim — including a label that
            // legitimately contains '&', which a mnemonic-marker scheme would have had to escape or mangle.
            Assert.Contains(rows, r => r.Label == "Create");
            Assert.Contains(rows, r => r.Label == "Vouchers");
            Assert.Contains(rows, r => r.Label == "GST & Taxation");
            Assert.Contains(rows, r => r.Label == "Chart of Accounts");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// O and Y are never handed out: both are already bound as bare accelerators on every cascade menu column
    /// (Import / Export Data) and their arms sit EARLIER in the first-match-wins chain, so a row painting one
    /// of them red would advertise an accelerator that does something else.
    /// </summary>
    [AvaloniaFact]
    public void The_letters_already_bound_on_the_cascade_are_never_assigned_as_hotkeys()
    {
        var (window, vm, dir) = NewWindow("HotKey Reserved Co");
        try
        {
            foreach (var row in vm.Columns[0].Items.Where(i => i.HasHotKey))
            {
                var letter = char.ToUpperInvariant(row.HotKey!.Value);
                Assert.True(letter != 'O' && letter != 'Y',
                    $"row '{row.Label}' was given reserved letter '{letter}'");
            }

            // "Account Books" is the proof the fall-through respects the reservation: A and C are taken by
            // earlier rows and O is reserved, so it lands on 'u'.
            var accountBooks = vm.Columns[0].Items.First(i => i.Label == "Account Books");
            Assert.Equal('u', accountBooks.HotKey);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// THE DRIVING TEST for WI-9: a bare "V" on the real window opens the Vouchers submenu, exactly as
    /// arrowing to that row and pressing Enter would. (Before this arm the keystroke did nothing at all.)
    /// </summary>
    [AvaloniaFact]
    public void A_bare_letter_activates_the_row_that_owns_it()
    {
        var (window, vm, dir) = NewWindow("HotKey Activate Co");
        try
        {
            var columnsBefore = vm.Columns.Count;
            var vouchers = vm.Columns[0].Items.First(i => i.Label == "Vouchers");
            Assert.Equal('V', vouchers.HotKey);   // vacuity guard: the letter under test really is V

            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);

            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.Equal(GatewayMenu.Vouchers, vm.CurrentGatewayMenu);
        }
        finally { Close(window, dir); }
    }

    /// <summary>A letter no row claims is simply not consumed — the cascade is left exactly as it was.</summary>
    [AvaloniaFact]
    public void An_unclaimed_letter_changes_nothing()
    {
        var (window, vm, dir) = NewWindow("HotKey Unclaimed Co");
        try
        {
            Assert.DoesNotContain(vm.Columns[0].Items,
                i => i.HasHotKey && char.ToUpperInvariant(i.HotKey!.Value) == 'Z');

            var columnsBefore = vm.Columns.Count;
            var menuBefore = vm.CurrentGatewayMenu;

            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);

            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.Equal(menuBefore, vm.CurrentGatewayMenu);
        }
        finally { Close(window, dir); }
    }

    // ================================================================ the WI-2 / WI-9 conflict rule

    /// <summary>
    /// THE CONFLICT RULE, proven in BOTH directions on the two column kinds.
    /// <para>
    /// An AUTHORED column (the Gateway) treats a bare letter as ACTIVATE. A DATA-DRIVEN picker column (the
    /// Account Books ledger list, built from the company's own ledgers) treats the same class of keystroke as
    /// FILTER: the highlight moves to the matching party and the type-ahead prefix accumulates, while no
    /// column is opened.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void A_bare_letter_activates_on_an_authored_column_and_filters_on_a_data_driven_picker()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexConflict_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.NewCompanyName = "Conflict Co";
            vm.CreateCompany();
            foreach (var name in new[] { "Zenith Traders", "Aarti Steel" })
            {
                vm.ShowLedgerMaster();
                vm.LedgerMaster!.Name = name;
                vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
                vm.LedgerMaster!.Create();
            }

            // ---- AUTHORED: the Gateway. A bare letter ACTIVATES.
            vm.ShowGateway();
            Assert.Equal(GatewayColumnKind.Authored, vm.ActiveColumnKind);
            var columnsBefore = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);          // a column opened → activated

            // ---- DATA-DRIVEN: the Account Books ledger picker. The same class of key FILTERS.
            vm.ShowLedgerBooksMenu();
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);
            var pickerColumns = vm.Columns.Count;

            // Its rows carry NO hotkeys — a letter cannot mean "activate" here.
            Assert.DoesNotContain(vm.Columns[^1].Items, i => i.HasHotKey);

            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);

            Assert.Equal(pickerColumns, vm.Columns.Count);              // nothing opened → filtered, not activated
            Assert.Equal("Z", vm.ActiveTypeAheadPrefix);
            Assert.Equal("Zenith Traders", vm.Columns[^1].Selected!.Label);
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
        }
    }

    // ================================================================ WI-11: Accept? (Y/N)

    /// <summary>
    /// Enter on a master screen ASKS instead of saving, and "Y" then performs the save — the master only
    /// exists after the confirmation is answered.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_master_raises_the_prompt_and_Y_saves()
    {
        var (window, vm, dir) = NewWindow("Accept Yes Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Aarti Steel";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Equal("Accept Ledger? (Y/N)", vm.AcceptPromptText);
            Assert.Null(vm.Company!.FindLedgerByName("Aarti Steel"));    // not saved yet — it only ASKED

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindLedgerByName("Aarti Steel"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>"N" dismisses the confirmation and saves NOTHING — the operator is back on the form.</summary>
    [AvaloniaFact]
    public void N_dismisses_the_prompt_without_saving()
    {
        var (window, vm, dir) = NewWindow("Accept No Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Bharat Motors";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Null(vm.Company!.FindLedgerByName("Bharat Motors"));
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);         // still on the form
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// THE CTRL+A CONTRACT. Ctrl+A accepts as-is and must BYPASS the confirmation entirely: the ledger is
    /// created and the prompt is never raised. Gating Ctrl+A behind Y/N would break the ~40 screens already
    /// regression-locked on it, and contradicts the reference product (answer Yes under Accept OR press
    /// Ctrl+A). This is the arm-ordering proof: Ctrl+A sits BEFORE the Y/N arm in the handler.
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_still_accepts_directly_and_never_raises_the_prompt()
    {
        var (window, vm, dir) = NewWindow("Accept CtrlA Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Zenith Traders";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.False(vm.IsAcceptPromptOpen);                          // never asked
            Assert.NotNull(vm.Company!.FindLedgerByName("Zenith Traders")); // and saved anyway
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Ctrl+A keeps working even while the confirmation happens to be up: it is matched by its own earlier arm
    /// and accepts outright, which is exactly the reference product's "Yes under Accept OR Ctrl+A".
    /// <para>
    /// It must ALSO close the confirmation. This test originally asserted only that the ledger was created, and
    /// that gap hid a real defect: the flag stayed TRUE, the still-live Y/N arm kept swallowing keystrokes, and
    /// the stale confirmation bar stayed on screen. Both are asserted now.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_accepts_even_while_the_prompt_is_open()
    {
        var (window, vm, dir) = NewWindow("Accept CtrlA Open Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Amar Textiles";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.NotNull(vm.Company!.FindLedgerByName("Amar Textiles"));
            Assert.False(vm.IsAcceptPromptOpen);              // the confirmation is CLOSED, not merely bypassed
            Assert.Equal(string.Empty, vm.AcceptPromptText);  // and no stale bar text left behind
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// THE ANTI-SHADOWING LOCK, driven end to end. Ctrl+A bypasses the confirmation, so it is the exit that most
    /// easily LEAKS the open flag; if it does, the Y/N arm — which sits EARLIER in the first-match-wins chain
    /// than the bare-Y arm — swallows the next Y on the Gateway and drills the highlighted row instead of
    /// opening Export Data. Here the operator does exactly that: accepts with Ctrl+A, walks back to the Gateway,
    /// and presses Y.
    /// </summary>
    [AvaloniaFact]
    public void After_CtrlA_a_bare_Y_on_the_gateway_still_opens_export_data()
    {
        var (window, vm, dir) = NewWindow("Accept LeakCtrlA Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Kaveri Chemicals";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);                       // vacuity guard: it really was up

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Assert.False(vm.IsAcceptPromptOpen);                      // the leak, caught at the source

            vm.ShowGateway();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.Equal(Screen.ExportData, vm.CurrentScreen);        // the SHIPPED accelerator, not the prompt
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The same lock for the CANCEL exit: Alt+X leaves the master without answering Y/N, so it must clear the
    /// confirmation too — and a bare Y back on the Gateway must still open Export Data.
    /// </summary>
    [AvaloniaFact]
    public void After_AltX_the_prompt_is_cleared_and_a_bare_Y_still_opens_export_data()
    {
        var (window, vm, dir) = NewWindow("Accept LeakAltX Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Nalanda Papers";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Equal(string.Empty, vm.AcceptPromptText);
            Assert.Null(vm.Company!.FindLedgerByName("Nalanda Papers"));   // cancel really did NOT save
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.Equal(Screen.ExportData, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The third leaking exit: NAVIGATING AWAY with the confirmation still up. Esc is a poor probe here because
    /// the Y/N arm consumes it as "No" (which clears the flag by the answered path); Left/Back is not in that
    /// arm, so it walks off the master with the prompt genuinely open — the state that used to leak. A bare Y
    /// back on the Gateway must still open Export Data.
    /// </summary>
    [AvaloniaFact]
    public void Navigating_off_the_master_while_the_prompt_is_open_clears_it()
    {
        var (window, vm, dir) = NewWindow("Accept LeakNav Co");
        try
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Pushpa Foods";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);   // Back, prompt still up

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Equal(string.Empty, vm.AcceptPromptText);
            Assert.NotEqual(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.Null(vm.Company!.FindLedgerByName("Pushpa Foods"));   // walking away saved nothing

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.Equal(Screen.ExportData, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// SCOPING / NO-SHADOWING. The Y/N arm is ordered before the bare-Y (Gateway → Export Data) arm, so it
    /// must not steal Y when no confirmation is up. On the Gateway, Y still opens the Export Data panel.
    /// </summary>
    [AvaloniaFact]
    public void Bare_Y_on_the_gateway_still_opens_export_data()
    {
        var (window, vm, dir) = NewWindow("Accept ScopeY Co");
        try
        {
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.Equal(Screen.ExportData, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The confirmation only exists on master screens: Enter on the Gateway still drills the highlighted row
    /// rather than raising a prompt, so cascade navigation is untouched.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_the_gateway_still_navigates_and_never_prompts()
    {
        var (window, vm, dir) = NewWindow("Accept ScopeEnter Co");
        try
        {
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);   // it drilled, as before
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Alt+N keeps its meaning: the Y/N arm ignores modified keys, so the report "Auto Columns" accelerator
    /// (which sits later in the chain) is never shadowed.
    /// </summary>
    [AvaloniaFact]
    public void AltN_still_opens_auto_columns_on_a_report()
    {
        var (window, vm, dir) = NewWindow("Accept AltN Co");
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Alt);

            Assert.Equal(Screen.AutoColumns, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    // ================================================================ WI-2: type-ahead reset + cycling

    /// <summary>
    /// Seeds a company with the given ledgers and returns the window + view model, Gateway shown.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindowWithLedgers(
        string company, params string[] ledgers)
    {
        var (window, vm, dir) = NewWindow(company);
        foreach (var name in ledgers)
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = name;
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            vm.LedgerMaster!.Create();
        }
        vm.ShowGateway();
        return (window, vm, dir);
    }

    /// <summary>
    /// CONVENTIONAL TYPE-AHEAD: pressing the SAME letter again steps to the NEXT row starting with it, wrapping
    /// at the end. Before this, "A" always re-selected the first "A" row (the extended prefix "AA" matched
    /// nothing and the fallback restarted the search from index 0), so a second party sharing a first letter
    /// could not be reached by typing at all.
    /// <para>The expected order is READ FROM THE COLUMN rather than hard-coded, so a seeded default ledger that
    /// also begins with "A" cannot make this pass vacuously.</para>
    /// </summary>
    [AvaloniaFact]
    public void Pressing_the_same_letter_again_cycles_to_the_next_match_and_wraps()
    {
        var (window, vm, dir) = NewWindowWithLedgers(
            "TypeAhead Cycle Co", "Zenith Traders", "Aarti Steel", "Amar Textiles");
        try
        {
            vm.ShowLedgerBooksMenu();
            var picker = vm.Columns[^1];
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);

            var aRows = picker.Items
                .Where(i => i.IsSelectable && i.Label.StartsWith("A", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Label)
                .ToList();
            Assert.True(aRows.Count >= 2, "the fixture must offer at least two 'A' rows to cycle between");

            // One press per matching row, then one more — the highlight walks them in order and WRAPS.
            foreach (var expected in aRows)
            {
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
                Assert.Equal(expected, picker.Selected!.Label);
                Assert.Equal("A", vm.ActiveTypeAheadPrefix);   // still a one-letter prefix, not "AA"
            }

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            Assert.Equal(aRows[0], picker.Selected!.Label);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Typing a longer prefix still NARROWS — the cycle arm only fires on a repeat of the same single letter, so
    /// "A" then "m" reaches "Amar Textiles" exactly as before.
    /// </summary>
    [AvaloniaFact]
    public void A_longer_prefix_still_narrows_rather_than_cycling()
    {
        var (window, vm, dir) = NewWindowWithLedgers(
            "TypeAhead Narrow Co", "Zenith Traders", "Aarti Steel", "Amar Textiles");
        try
        {
            vm.ShowLedgerBooksMenu();
            var picker = vm.Columns[^1];

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);

            // The prefix accumulates the keystrokes as the handler reports them (bare keys arrive upper-cased);
            // matching is case-insensitive, which is what makes "AM" reach "Amar Textiles".
            Assert.Equal("AM", vm.ActiveTypeAheadPrefix);
            Assert.Equal("Amar Textiles", picker.Selected!.Label);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// THE RESET, which had no caller at all before this: leaving the picker column (Esc/Back) clears its
    /// prefix, so re-entering starts a fresh search instead of silently extending the old one. Asserted on the
    /// column object itself — <c>ActiveTypeAheadPrefix</c> alone would pass merely because focus moved.
    /// </summary>
    [AvaloniaFact]
    public void Leaving_and_re_entering_a_picker_column_clears_the_type_ahead_prefix()
    {
        var (window, vm, dir) = NewWindowWithLedgers(
            "TypeAhead Reset Co", "Zenith Traders", "Aarti Steel");
        try
        {
            vm.ShowLedgerBooksMenu();
            var picker = vm.Columns[^1];

            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
            Assert.Equal("Z", picker.TypeAheadPrefix);          // vacuity guard: a prefix really accumulated

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(string.Empty, picker.TypeAheadPrefix); // the column that was LEFT is clean
            Assert.Equal(string.Empty, vm.ActiveTypeAheadPrefix);

            // Re-entering starts from scratch: "A" alone selects an "A" party, not "ZA…" (which matches nothing).
            vm.ShowLedgerBooksMenu();
            var reopened = vm.Columns[^1];
            Assert.Equal(string.Empty, reopened.TypeAheadPrefix);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            Assert.Equal("A", vm.ActiveTypeAheadPrefix);
            Assert.StartsWith("A", reopened.Selected!.Label, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// A completed selection also resets: drilling a picked ledger opens its book column, and the prefix left
    /// behind on the picker is cleared rather than waiting for the column to be rebuilt.
    /// </summary>
    [AvaloniaFact]
    public void A_completed_selection_clears_the_type_ahead_prefix()
    {
        var (window, vm, dir) = NewWindowWithLedgers(
            "TypeAhead Select Co", "Zenith Traders", "Aarti Steel");
        try
        {
            vm.ShowLedgerBooksMenu();
            var picker = vm.Columns[^1];

            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
            Assert.Equal("Zenith Traders", picker.Selected!.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(string.Empty, picker.TypeAheadPrefix);
        }
        finally { Close(window, dir); }
    }

    // ================================================================ the DataDriven column set, pinned

    /// <summary>
    /// THE GUARD on the WI-2/WI-9 rule "a column built from COMPANY DATA is DataDriven; a hand-authored menu is
    /// Authored". Pins the full DataDriven set — Cash Book, Bank Book, Ledger and the Day-Book Alt+A voucher-type
    /// picker — so a new picker added later cannot quietly ship as Authored and hand a user-created row an
    /// arbitrary mid-word red hotkey.
    /// </summary>
    [AvaloniaFact]
    public void Every_company_data_column_is_data_driven_and_the_authored_menus_are_not()
    {
        var (window, vm, dir) = NewWindowWithLedgers("ColumnKind Set Co", "Aarti Steel");
        try
        {
            // ---- the DataDriven set: rows come from the company, so a bare letter must FILTER.
            vm.ShowCashBookMenu();
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);

            vm.ShowBankBookMenu();
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);

            vm.ShowLedgerBooksMenu();
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);

            // The Day-Book Alt+A picker is built from Company.VoucherTypes — a COMPANY-CONFIGURABLE list, so a
            // user-created type would otherwise be given a computed hotkey nobody authored.
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal(GatewayColumnKind.DataDriven, vm.ActiveColumnKind);
            Assert.DoesNotContain(vm.Columns[^1].Items, i => i.HasHotKey);   // no red letters over user data

            // ---- and the authored menus keep ACTIVATE.
            vm.ShowGateway();
            Assert.Equal(GatewayColumnKind.Authored, vm.ActiveColumnKind);
            vm.ShowVouchersMenu();
            Assert.Equal(GatewayColumnKind.Authored, vm.ActiveColumnKind);
            vm.ShowCreateMenu();
            Assert.Equal(GatewayColumnKind.Authored, vm.ActiveColumnKind);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The consequence of the kind change, driven: a bare letter in the Alt+A voucher-type picker FILTERS to
    /// that type instead of activating an arbitrary computed hotkey — and opens nothing on its own.
    /// </summary>
    [AvaloniaFact]
    public void A_bare_letter_filters_the_add_voucher_picker()
    {
        var (window, vm, dir) = NewWindowWithLedgers("ColumnKind AddVoucher Co", "Aarti Steel");
        try
        {
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);

            var picker = vm.Columns[^1];
            var columnsBefore = vm.Columns.Count;
            var journal = picker.Items.FirstOrDefault(i => i.IsSelectable && i.Label == "Journal");
            Assert.NotNull(journal);   // vacuity guard: the row the keystroke targets really is listed

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.None);

            Assert.Equal(columnsBefore, vm.Columns.Count);          // filtered — nothing was activated
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal("Journal", picker.Selected!.Label);
            Assert.Equal("J", vm.ActiveTypeAheadPrefix);
        }
        finally { Close(window, dir); }
    }

    // ================================================================ WI-9: the red letter, statically

    /// <summary>
    /// The shared cascade menu row paints the hotkey with three Runs inside ONE TextBlock, the middle one red.
    /// Asserted against the XAML source rather than a rendered frame: a render-based assertion would be
    /// flaky on the 3-OS CI. (The colour and kerning were confirmed once by an actual Skia render during
    /// development; this test locks the structure that produced it.)
    /// <para>
    /// The single-TextBlock shape is load-bearing: a horizontal StackPanel measures at infinite width and
    /// would kill the TextTrimming on the same element.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void The_menu_row_paints_the_hotkey_as_a_red_run_inside_one_trimming_textblock()
    {
        var xamlPath = Path.Combine(RepoRoot(), "src", "Apex.Desktop", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(xamlPath), xamlPath);
        var xaml = File.ReadAllText(xamlPath);

        var runMarkup = "<Run Text=\"{Binding HotKeyBefore}\"/>"
            + "<Run Text=\"{Binding HotKeyText}\" Foreground=\"#C62828\" FontWeight=\"Bold\"/>"
            + "<Run Text=\"{Binding HotKeyAfter}\"/>";

        Assert.Contains(runMarkup, xaml);

        // The Runs sit in a TextBlock that still trims — and there is no StackPanel wrapping them.
        var runsAt = xaml.IndexOf(runMarkup, StringComparison.Ordinal);
        var openingTag = xaml.LastIndexOf("<TextBlock", runsAt, StringComparison.Ordinal);
        var element = xaml[openingTag..runsAt];
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", element);
        Assert.DoesNotContain("StackPanel", element);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
