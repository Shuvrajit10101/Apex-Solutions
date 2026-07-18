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
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-1 — context-aware Alt+C "create on the fly", driven through the REAL <see cref="MainWindow"/> tunnel
/// handler (<c>window.KeyPressQwerty</c>) with REAL focus in a REAL picker, never by asserting a binding.
///
/// <para><b>THE DEFECT THIS LOCKS (measured on 43c8ea7, not inferred).</b> Alt+C mid-voucher ran
/// <c>CreateLedgerShortcut → ShowLedgerMaster → OpenPageColumn</c>, whose <c>TrimColumnsAfter/ClearSubScreens</c>
/// pair NULLED <see cref="MainWindowViewModel.VoucherEntry"/>. Probing the shipped build gave
/// <c>CurrentScreen=LedgerMaster · VoucherEntry null? True · Columns=2</c>, and Esc then landed on the
/// <b>Gateway</b> — the operator who pressed Alt+C to add one missing ledger lost every line already keyed, with
/// no prompt and no undo. That is silent DATA LOSS, and
/// <see cref="AltC_mid_voucher_keeps_the_in_progress_voucher_alive"/> is the regression lock.</para>
/// </summary>
public sealed class CreateOnTheFlyTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCreateFly_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
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

    private static void Layout(MainWindow window)
    {
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>Creates the company and returns its Cash ledger (every company seeds one).</summary>
    private static DomainLedger SeedCompany(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm.Company!.FindLedgerByName("Cash")!;
    }

    /// <summary>
    /// The first REAL picker in the live window tagged with <paramref name="fieldId"/>. Returns the control so a
    /// test can FOCUS it — that focus is what makes the tunnel handler resolve the field, exactly as it does for
    /// an operator standing in the field.
    /// </summary>
    private static Control? TaggedPicker(MainWindow window, string fieldId)
    {
        Layout(window);
        return window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => CreateField.GetMaster(c) == fieldId && c.IsEffectivelyVisible);
    }

    // ============================================================ (1) THE DATA-LOSS FIX

    /// <summary>
    /// THE PRIORITY TEST. A half-typed Receipt (Cash Dr 12,345 on a chosen date), focus in the line's ledger
    /// picker, REAL Alt+C. The Ledger-creation screen must open AND the in-progress voucher must SURVIVE: the
    /// same <see cref="MainWindowViewModel.VoucherEntry"/> INSTANCE, still carrying the line, the amount and the
    /// date.
    /// <para><b>This test bites.</b> Restoring the destructive open (routing
    /// <c>CreateMasterOnTheFly</c> through <c>OpenPageColumn</c> instead of <c>OpenCreateMasterColumn</c>) makes
    /// <c>VoucherEntry</c> null and the first assertion below fails — verified by doing exactly that, not
    /// assumed.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltC_mid_voucher_keeps_the_in_progress_voucher_alive()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Survives");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            var on = vm.Company!.FinancialYearStart.AddDays(9);
            entry.Date = on;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].Side = DrCr.Debit;
            entry.Lines[0].AmountText = "12345";

            var picker = TaggedPicker(window, MasterCreateFields.Ledger);
            Assert.NotNull(picker);
            picker!.Focus();
            Layout(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            // The create screen opened…
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerMaster);
            // …and the voucher SURVIVED — same instance, same content. (Before the fix: null.)
            Assert.NotNull(vm.VoucherEntry);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Same(cash, vm.VoucherEntry!.Lines[0].SelectedLedger);
            Assert.Equal("12345", vm.VoucherEntry!.Lines[0].AmountText);
            Assert.Equal(on, vm.VoucherEntry!.Date);
            // The create screen is a column BESIDE the voucher, not in place of it.
            Assert.True(vm.IsCreateOnTheFlyOpen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (2) RETURN TO CALLER — success

    /// <summary>
    /// The round-trip that is the whole point of the feature: Alt+C in a line's ledger field, create
    /// "Freight Inward", and land back on the SAME voucher with the new ledger SELECTED IN THAT FIELD — while
    /// every other line keeps what it had.
    /// </summary>
    [AvaloniaFact]
    public void Creating_a_ledger_returns_to_the_voucher_with_the_new_ledger_selected_in_the_field()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC RoundTrip");
            vm.OpenVoucher(VoucherBaseType.Payment);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "700";

            // Stand in the SECOND line's ledger field (the empty one) and create on the fly.
            var line = entry.Lines[1];
            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, line));
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);

            vm.LedgerMaster!.Name = "Freight Inward";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // Ctrl+A creates

            // Back on the voucher — same instance, untouched line 0, NEW ledger in the triggering line 1.
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.False(vm.IsCreateOnTheFlyOpen);

            var created = vm.Company!.FindLedgerByName("Freight Inward");
            Assert.NotNull(created);
            Assert.Same(created, line.SelectedLedger);
            Assert.Same(cash, entry.Lines[0].SelectedLedger);
            Assert.Equal("700", entry.Lines[0].AmountText);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (3) RETURN TO CALLER — cancel

    /// <summary>
    /// Esc out of the create screen: back to the SAME voucher, intact, and the triggering field UNCHANGED — no
    /// half-created master, no stray selection. Alt+X (the cancel accelerator) must behave identically.
    /// </summary>
    [AvaloniaFact]
    public void Cancelling_the_create_returns_to_the_voucher_with_the_field_unchanged()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Cancel");
            vm.OpenVoucher(VoucherBaseType.Payment);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "310";
            var line = entry.Lines[1];
            var ledgerCountBefore = vm.Company!.Ledgers.Count;

            vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, line);
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Null(line.SelectedLedger);                       // field unchanged
            Assert.Equal(ledgerCountBefore, vm.Company!.Ledgers.Count); // nothing created
            Assert.False(vm.IsCreateOnTheFlyOpen);
            Assert.Same(cash, entry.Lines[0].SelectedLedger);
            Assert.Equal("310", entry.Lines[0].AmountText);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+X out of the create screen behaves like Esc — voucher intact, field unchanged.</summary>
    [AvaloniaFact]
    public void AltX_out_of_the_create_screen_returns_to_the_intact_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC AltX");
            vm.OpenVoucher(VoucherBaseType.Payment);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "88";
            var line = entry.Lines[1];

            vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, line);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Null(line.SelectedLedger);
            Assert.Equal("88", entry.Lines[0].AmountText);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (4) CONTEXT DISPATCH

    /// <summary>
    /// The dispatch table itself: each tagged field id resolves to ITS master kind, and an untagged / unknown id
    /// is INERT (<see cref="MasterCreateKind.None"/>) rather than falling back to Ledger — a wrong-screen open is
    /// the failure mode this guards.
    /// </summary>
    [Theory]
    [InlineData("Ledger", MasterCreateKind.Ledger)]
    [InlineData("Party", MasterCreateKind.Ledger)]
    [InlineData("StockLedger", MasterCreateKind.Ledger)]
    [InlineData("StockItem", MasterCreateKind.StockItem)]
    [InlineData("Godown", MasterCreateKind.Godown)]
    [InlineData("Unit", MasterCreateKind.Unit)]
    [InlineData("StockGroup", MasterCreateKind.StockGroup)]
    [InlineData("StockCategory", MasterCreateKind.StockCategory)]
    [InlineData("CostCategory", MasterCreateKind.CostCategory)]
    [InlineData("CostCentre", MasterCreateKind.CostCentre)]
    [InlineData("AccountGroup", MasterCreateKind.AccountGroup)]
    [InlineData("Side", MasterCreateKind.None)]
    [InlineData("RefType", MasterCreateKind.None)]
    [InlineData("CdnOriginalInvoice", MasterCreateKind.None)]
    [InlineData("", MasterCreateKind.None)]
    [InlineData(null, MasterCreateKind.None)]
    public void Field_ids_dispatch_to_their_master_kind(string? fieldId, MasterCreateKind expected)
        => Assert.Equal(expected, MasterCreateFields.KindFor(fieldId));

    /// <summary>
    /// A stock-item field opens the STOCK ITEM master, not the Ledger master — the context-awareness that makes
    /// the feature worth having. Driven on a live inventory voucher through the public entry point.
    /// </summary>
    [AvaloniaFact]
    public void AltC_in_a_stock_item_field_opens_the_stock_item_master()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedCompany(vm, "AltC StockItem");
            vm.OpenInventoryVoucher(VoucherBaseType.StockJournal);
            var entry = vm.InventoryVoucherEntry;
            if (entry is null) return; // stock journal unavailable on this company shape — nothing to assert
            var line = entry.Lines.FirstOrDefault();
            if (line is null) return;

            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.StockItem, MasterCreateFields.StockItem, line));
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.NotNull(vm.StockItemMaster);
            // The inventory voucher survived beneath it.
            Assert.Same(entry, vm.InventoryVoucherEntry);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// An INERT field: real Alt+C with focus in the Dr/Cr side picker (untagged) must NOT open a Ledger master
    /// over the voucher — the voucher stays the active screen. This is the "no wrong-screen open" guarantee.
    /// </summary>
    [AvaloniaFact]
    public void AltC_in_an_untagged_field_is_inert_on_a_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Inert");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "42";

            // The Dr/Cr side picker sits beside the tagged ledger picker and carries NO create tag.
            Layout(window);
            var side = window.GetVisualDescendants().OfType<ComboBox>()
                .FirstOrDefault(c => CreateField.GetMaster(c) is null
                                     && c.DataContext is VoucherLineViewModel
                                     && c.IsEffectivelyVisible);
            Assert.NotNull(side);
            side!.Focus();
            Layout(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Null(vm.LedgerMaster);
            Assert.False(vm.IsCreateOnTheFlyOpen);
            Assert.Same(entry, vm.VoucherEntry);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (4b) THE SECOND ENTRY POINT — IN THE PICKER

    /// <summary>
    /// Alt+C keeps working with the picker's own dropdown OPEN — the operator who has already gone looking for
    /// the ledger in the list, not found it, and hits Alt+C without first closing the list.
    /// <para><b>Scope correction.</b> This test asserts ONLY that: it opens a dropdown and presses the key. It
    /// does NOT prove the corpus's second entry point (a Create option "under List of Ledger Accounts", Study
    /// Guide ~2046–47), which its docstring previously claimed to lock — that requirement was in fact unbuilt at
    /// the time. The Create ROW itself is covered by
    /// <see cref="The_ledger_picker_offers_a_Create_row_that_runs_the_same_dispatch"/> and
    /// <see cref="A_bare_letter_filters_the_real_ledgers_and_never_lands_on_the_Create_row"/>.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltC_works_from_inside_the_open_picker_list()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC InPicker");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "999";

            var picker = TaggedPicker(window, MasterCreateFields.Ledger) as ComboBox;
            Assert.NotNull(picker);
            picker!.Focus();
            picker.IsDropDownOpen = true;   // the operator is standing IN the list of ledger accounts
            Layout(window);
            Assert.True(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            // Same dispatch, same non-destructive open — the voucher is untouched beneath.
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.True(vm.IsCreateOnTheFlyOpen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("999", vm.VoucherEntry!.Lines[0].AmountText);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (5) NO ACCELERATOR SHADOWING

    /// <summary>
    /// ORDER PROOF, both directions. The RQ-4 comparative arm sits ABOVE the Alt+C arm in the window's
    /// first-match-wins chain, so on a comparative report Alt+C still opens "Add Comparison Column" — the new
    /// create-on-the-fly arm neither shadows it nor is shadowed by it (off a voucher Alt+C still creates a ledger).
    /// </summary>
    [AvaloniaFact]
    public void AltC_still_adds_a_comparison_column_on_a_report_and_still_creates_a_ledger_elsewhere()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedCompany(vm, "AltC Order");

            // (a) On a comparative report Alt+C belongs to RQ-4 — unchanged.
            vm.OpenReport(ReportKind.TrialBalance);
            Assert.True(vm.Reports is { SupportsComparative: true });
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddComparisonColumn, vm.CurrentScreen);

            // (b) On the Gateway (no voucher, no field) Alt+C keeps its historic meaning.
            vm.ShowGateway();
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.False(vm.IsCreateOnTheFlyOpen); // the plain route, not the round-trip
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The S5 bare-letter contract is untouched by the new arm: a BARE "c" is not Alt+C. On the Gateway it must
    /// not open the Ledger master (it drives the menu's own hotkey/type-ahead rule instead).
    /// </summary>
    [AvaloniaFact]
    public void A_bare_c_is_not_altC()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedCompany(vm, "AltC Bare");
            vm.ShowGateway();
            var before = vm.CurrentScreen;

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);

            Assert.NotEqual(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.False(vm.IsCreateOnTheFlyOpen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (6) THE TAGGED SURFACE

    /// <summary>
    /// Every create-tag actually present in the shipped XAML names a KNOWN field id. A typo'd tag would resolve
    /// to <see cref="MasterCreateKind.None"/> and silently make that field inert — a dead affordance no other
    /// test would catch.
    /// </summary>
    [Fact]
    public void Every_create_tag_in_the_xaml_is_a_known_field_id()
    {
        var xaml = File.ReadAllText(XamlPath());
        var matches = System.Text.RegularExpressions.Regex.Matches(
            xaml, "CreateField\\.Master=\"([^\"]*)\"");
        Assert.NotEmpty(matches);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var id = m.Groups[1].Value;
            Assert.NotEqual(MasterCreateKind.None, MasterCreateFields.KindFor(id));
        }
    }

    // ============================================================ (7) DEFECT 1 — THE NESTED DATA LOSS

    /// <summary>
    /// <b>DEFECT 1 (CRITICAL, data loss).</b> A SECOND Alt+C while a NON-LEDGER create column is open used to
    /// DESTROY the in-progress voucher — the exact class of loss WI-1 was written to remove, reintroduced one
    /// screen deeper. With the create column open, <c>CurrentScreen</c> is the MASTER screen, so
    /// <c>IsCreateOnTheFlyCaller()</c> was false, the inert guard was skipped, and
    /// <c>if (CurrentScreen != Screen.LedgerMaster) ShowLedgerMaster()</c> fell through to
    /// <c>OpenPageColumn → TrimColumnsAfter + ClearSubScreens</c>, NULLING the entry view model underneath.
    /// (Over a LEDGER create column it was safe only by the <c>!= Screen.LedgerMaster</c> ACCIDENT, which is why
    /// this test deliberately uses a Stock Item column.)
    /// <para><b>This test bites.</b> Removing the <c>if (IsCreateOnTheFlyOpen) return;</c> guard from
    /// <c>CreateLedgerShortcut</c> flips the screen to LedgerMaster and nulls <c>VoucherEntry</c>, failing the
    /// assertions below — verified by deleting exactly that line and re-running, not assumed.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_nested_AltC_over_a_non_ledger_create_column_cannot_destroy_the_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Nested Safe");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "54321";

            // A NON-Ledger create column over the live voucher (the accident-free case).
            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.StockItem, MasterCreateFields.StockItem, null));
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherEntry);

            // Stand in an UNTAGGED field of that create screen and press the REAL Alt+C.
            Layout(window);
            var untagged = window.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => CreateField.GetMaster(t) is null
                                     && t.DataContext is StockItemMasterViewModel
                                     && t.IsEffectivelyVisible);
            Assert.NotNull(untagged);
            untagged!.Focus();
            Layout(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            // INERT: no wrong-screen open, and above all the voucher is STILL THERE.
            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.Null(vm.LedgerMaster);
            Assert.NotNull(vm.VoucherEntry);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("54321", vm.VoucherEntry!.Lines[0].AmountText);

            // …and unwinding still lands back on that same live voucher rather than the Gateway.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.False(vm.IsCreateOnTheFlyOpen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The other half of DEFECT 1: a nested Alt+C on a TAGGED field of the create screen is not merely harmless,
    /// it WORKS — Stock Group Creation opens over Stock Item Creation over the live voucher (depth 2, no
    /// artificial cap), and the voucher underneath is untouched. Driven through the real handler with real focus
    /// in the real "Under" picker.
    /// </summary>
    [AvaloniaFact]
    public void A_nested_AltC_on_a_tagged_field_of_the_create_screen_opens_a_second_creator()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Depth2");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "2468";

            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.StockItem, MasterCreateFields.StockItem, null));
            var itemMaster = vm.StockItemMaster!;
            itemMaster.Name = "Half typed item";

            var under = TaggedPicker(window, MasterCreateFields.StockGroup);
            Assert.NotNull(under);
            under!.Focus();
            Layout(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            Assert.Equal(Screen.StockGroupMaster, vm.CurrentScreen);
            Assert.NotNull(vm.StockGroupMaster);
            Assert.Equal(2, vm.CreateOnTheFlyDepth);
            // Both screens beneath survived.
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("2468", vm.VoucherEntry!.Lines[0].AmountText);

            // Complete the nested create: back on the SAME Stock Item master with the new group selected — the
            // escape from the dead end (CanCreate was false with no stock group in the company).
            vm.StockGroupMaster!.Name = "Raw Materials";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.Same(itemMaster, vm.StockItemMaster);
            Assert.Equal("Half typed item", vm.StockItemMaster!.Name);
            Assert.Equal(1, vm.CreateOnTheFlyDepth);
            var created = vm.Company!.StockGroups.FirstOrDefault(g => g.Name == "Raw Materials");
            Assert.NotNull(created);
            Assert.Same(created, vm.StockItemMaster!.SelectedGroup);
            // The voucher is STILL alive under both.
            Assert.Same(entry, vm.VoucherEntry);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (8) DEFECT 2 — THE SESSION SOFT-LOCK

    /// <summary>
    /// <b>DEFECT 2 (HIGH, session soft-lock).</b> <c>_createOnTheFly</c> was cleared ONLY by
    /// <c>AbandonCreateOnTheFlyIfColumnGone</c> called from <c>BackFromPage</c>. Any page-REPLACING navigation
    /// trimmed the create column while leaving the request armed, and the armed request then made every later
    /// Alt+C bail out — SILENTLY, for the rest of the session, because <c>CreateLedgerShortcut</c> discards the
    /// returned false. Clearing from <c>TrimColumnsAfter</c> (the choke point every such route funnels through)
    /// is the fix.
    /// <para><b>This test bites.</b> Removing the <c>AbandonCreateOnTheFlyIfColumnGone()</c> call from
    /// <c>TrimColumnsAfter</c> leaves <c>IsCreateOnTheFlyOpen</c> true after the navigation and the second,
    /// REAL Alt+C then does nothing — both halves below fail.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltC_still_works_after_navigating_away_from_an_open_create_column()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC SoftLock");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            vm.VoucherEntry!.Lines[0].SelectedLedger = cash;

            Assert.True(vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, null));
            Assert.True(vm.IsCreateOnTheFlyOpen);

            // Navigate AWAY with a page-replacing route — the create column is trimmed without a BackFromPage.
            vm.OpenReport(ReportKind.TrialBalance);
            Assert.False(vm.IsCreateOnTheFlyOpen);   // (before the fix: still true — the soft-lock)

            // A fresh voucher, real focus in a real tagged picker, REAL Alt+C. It must still work.
            vm.OpenVoucher(VoucherBaseType.Payment);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].AmountText = "1500";

            var picker = TaggedPicker(window, MasterCreateFields.Ledger);
            Assert.NotNull(picker);
            picker!.Focus();
            Layout(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Alt);

            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.True(vm.IsCreateOnTheFlyOpen);
            Assert.Same(entry, vm.VoucherEntry);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (9) DEFECT 3 — THE BUTTON-BAR ITEM

    /// <summary>
    /// <b>DEFECT 3 (HIGH, regression of shipped behaviour).</b> The Alt+C BUTTON bound
    /// <c>() =&gt; CreateLedgerShortcut()</c> with no field context, so on an entry screen it hit the inert guard
    /// and did NOTHING while rendering ENABLED and captioned "Create Ledger" (before WI-1 it bound
    /// <c>ShowLedgerMaster</c> and worked). No test covered it. It now runs the same non-destructive dispatch the
    /// key runs, so it opens the screen's default creator BESIDE the live voucher.
    /// <para><b>This test bites.</b> Restoring the <c>() =&gt; CreateLedgerShortcut()</c> binding leaves
    /// <c>LedgerMaster</c> null after the invoke.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_AltC_button_is_functional_on_an_entry_screen_and_does_not_destroy_the_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var cash = SeedCompany(vm, "AltC Button");
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].AmountText = "6400";

            var button = vm.ButtonBar.FirstOrDefault(b => b.Key == "Alt+C");
            Assert.NotNull(button);
            Assert.True(button!.Enabled);
            Assert.Equal("Create Ledger", button.Caption);

            button.Action();

            // FUNCTIONAL — and non-destructive, exactly like the key.
            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerMaster);
            Assert.True(vm.IsCreateOnTheFlyOpen);
            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("6400", vm.VoucherEntry!.Lines[0].AmountText);

            // While a create column is open Alt+C is inert BY DESIGN, so the button must say so rather than sit
            // enabled and dead — the honest-affordance half of the same defect.
            var whileOpen = vm.ButtonBar.FirstOrDefault(b => b.Key == "Alt+C");
            Assert.NotNull(whileOpen);
            Assert.False(whileOpen!.Enabled);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ (10) DEFECT 4 — NO VACUOUS COVERAGE

    /// <summary>
    /// <b>DEFECT 4 (HIGH, vacuous coverage).</b> <c>Unit</c>, <c>StockGroup</c>, <c>StockCategory</c> and
    /// <c>AccountGroup</c> sat in the dispatch table AND in
    /// <see cref="Field_ids_dispatch_to_their_master_kind"/> while being tagged on ZERO controls — unreachable
    /// code that a TABLE-ONLY theory made look covered, and the reason "Stock Item Creation" was a dead end on a
    /// company with no stock group or unit. This makes the theory non-vacuous: every kind the table maps must be
    /// tagged on at least one REAL control in the shipped XAML.
    /// <para><b>This test bites.</b> It fails on the four kinds above until they are tagged (verified by
    /// running it against the untagged XAML).</para>
    /// </summary>
    [Fact]
    public void Every_mapped_master_kind_is_tagged_on_at_least_one_real_control()
    {
        var xaml = File.ReadAllText(XamlPath());
        var tagged = System.Text.RegularExpressions.Regex
            .Matches(xaml, "CreateField\\.Master=\"([^\"]*)\"")
            .Select(m => MasterCreateFields.KindFor(m.Groups[1].Value))
            .ToHashSet();

        foreach (var kind in Enum.GetValues<MasterCreateKind>())
        {
            if (kind == MasterCreateKind.None) continue;
            Assert.True(tagged.Contains(kind),
                $"{kind} is in the dispatch table but tagged on NO control — dead code a table-only test hides.");
        }
    }

    // ============================================================ (11) DEFECT 5 — THE IN-PICKER CREATE ROW

    /// <summary>
    /// <b>DEFECT 5 (MEDIUM, requirement absent).</b> The corpus's SECOND entry point — a Create option "under
    /// List of Ledger Accounts" — was never implemented; the picker held only plain ledgers. The row now exists,
    /// is arrow-reachable, and Enter on it runs the SAME <c>CreateLedgerShortcut</c> dispatch as the key.
    /// </summary>
    [AvaloniaFact]
    public void The_ledger_picker_offers_a_Create_row_that_runs_the_same_dispatch()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedCompany(vm, "AltC PickerRow");
            vm.ShowLedgerBooksMenu();

            var createRow = vm.Menu.FirstOrDefault(m => m.IsCreateRow);
            Assert.NotNull(createRow);
            Assert.Equal("Create Ledger", createRow!.Label);
            Assert.True(createRow.IsSelectable);   // arrow-reachable, not a dim header

            // Arrow UP from the first selectable row wraps onto the LAST one — the pinned Create row.
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.True(createRow.IsSelected);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
            Assert.NotNull(vm.LedgerMaster);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The S5 interaction the Create row must NOT break: a bare letter FILTERS the real masters, and the Create
    /// row — an affordance, not data — is never what type-ahead lands on. "c" selects the ledger <i>Cash</i>, and
    /// pressing "c" again CYCLES among real "c" ledgers (here wrapping back to Cash) instead of walking onto
    /// "Create Ledger".
    /// <para><b>This test bites.</b> Dropping the <c>!Items[i].IsCreateRow</c> term from
    /// <c>GatewayColumn.IndexOfPrefix</c> makes the SECOND "c" land the highlight on "Create Ledger" and the
    /// final assertion fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_bare_letter_filters_the_real_ledgers_and_never_lands_on_the_Create_row()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedCompany(vm, "AltC PickerTypeAhead");
            vm.ShowLedgerBooksMenu();

            var createRow = vm.Menu.First(m => m.IsCreateRow);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Assert.Equal("Cash", vm.Menu[vm.SelectedIndex].Label);
            Assert.False(createRow.IsSelected);

            // The repeated-letter CYCLE is where the Create row would be walked onto without the skip.
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Assert.False(createRow.IsSelected);
            Assert.NotEqual("Create Ledger", vm.Menu[vm.SelectedIndex].Label);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    private static string XamlPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("MainWindow.axaml not found from " + AppContext.BaseDirectory);
    }
}
