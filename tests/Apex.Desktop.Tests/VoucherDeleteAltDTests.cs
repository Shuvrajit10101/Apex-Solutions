using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S4 — Alt+D DELETES a posted voucher or a master.</b> The engine half
/// (<c>LedgerService.Delete</c>, <c>Company.RemoveLedger/RemoveGroup/RemoveStockItem</c>) already existed and is
/// untouched by this slice; the guards are unit-tested in <c>MasterDeletionRulesTests</c> in the engine suite.
/// What ships HERE is the WIRING, the ROUTING and the CONFIRMATION, and that is what these prove — end to end,
/// through the REAL <see cref="MainWindow"/> tunnel handler, never by asserting that a binding exists.
///
/// <para>🔴 <b>THIS IS THE SLICE THAT MAKES DELETION REACHABLE AT ALL.</b> <c>LedgerService.Delete</c> has sat in
/// the codebase since Phase 1 with no caller. Every one of its consequences arrives with the eight-line key arm
/// these tests drive, which is why the refusals are tested at least as heavily as the successes.</para>
///
/// <para><b>🔴 FIDELITY — read before changing any string below (R7).</b> The corpus settles that Alt+D is Delete,
/// and that a ledger carrying transactions cannot be deleted (STUDY-GUIDE PDF p.67). Everything else here is
/// <b>UNVERIFIED-BY-DESIGN — ours, corpus silent</b>: the referential guard, the numbering guard, offering Cancel
/// as the remedy, the five surfaces, and every prompt and notice string. <b>The SINGLE confirmation is a decision,
/// not fidelity:</b> the corpus's published DOUBLE prompt is attested for a master and for a group company and is
/// NOT attested for a voucher, and the absence of that attestation is the finding — we decline to copy it across
/// by analogy (decision D-6). That is a decline-to-EXTEND, which is a different R7 claim from a narrowing of an
/// attested scope; the two are not merged anywhere in this file.</para>
///
/// <para><b>What each group locks.</b> (a) the arm and its keyboard guards; (b) the five routing surfaces;
/// (c) the refusals, including the numbering refusal that is the whole reason this slice is dangerous; (d) the
/// confirmation channel and its disarm paths — a pending DELETION that outlived its prompt would let a plain "Y"
/// on an unrelated master screen destroy a voucher; (e) persistence and the pre-flight; (f) the two master routes
/// that had no refusal test at all; (g) the arm's own flags and clauses; (h) the remedy the refusal names;
/// (i) ER-13.</para>
///
/// <para>🔴 <b>THE MUTATION ACCOUNTING, RECORDED SO "N GUARDS MUTATION-PROVED" IS NEVER READ AS "COVERED".</b> The
/// slice shipped claiming fourteen guards mutation-proved. Both of those claims re-verified true — and they
/// described a MINORITY: of 82 distinct guard/clause sites across the three S4 files, 57 reddened under mutation
/// and <b>25 did not</b>. Worst of them, the GROUP route and the STOCK-ITEM route could each have BOTH of their
/// guard call sites removed in one build with all 2258 Desktop tests green, while the identical compound on the
/// LEDGER route reddened two — a clean positive control proving the method worked and that those two routes simply
/// had no refusal test anywhere in the application.
/// <br/><b>Closed by groups (f)–(i) and by the engine suite's own new cases:</b> both master routes' refusals;
/// both routes' pre-act re-asks (plus the ledger's); <c>e.Handled</c> and the <c>IsDeleteTargetPage</c> clause on
/// the key arm; <c>!IsPickerOpen</c>; the teardown ORDER; the notice clear; the <c>Notice</c>-vs-dead-key
/// distinction; every stock-item and voucher tally category; the group refusal's singular head; and the voucher
/// NUMBER in the label.
/// <br/><b>Deliberately left unproved, each with its reason stated where it lives:</b> the
/// <c>Company is not null</c> precondition on <c>IsDeleteTargetPage</c> (unreachable — nothing in the application
/// sets <c>Company</c> back to null, and <c>RequestDeleteHighlighted</c> re-tests it; kept as a labelled
/// precondition, not deleted for a score), and <c>_pendingDeleteId = Guid.Empty</c> in the teardown (paired-state
/// hygiene: the id is only ever read when the KIND is armed, which IS pinned, so no behavioural test can separate
/// them — clearing one and not the other would be the trap). The <c>when IsLiveReportPage</c> clause on the
/// <c>Screen.Report</c> switch arm was NOT kept: it reduced to a null test inside a switch that had already
/// established the screen, and is now written as a null-conditional so nothing dead remains.</para>
/// </summary>
public sealed class VoucherDeleteAltDTests
{
    private static readonly DateOnly InvoiceDate = new(2024, 6, 10);

    // ============================================================ harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexDeleteAltD_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private sealed record Kit(DomainLedger Cash, DomainLedger Capital, Voucher Receipt);

    /// <summary>
    /// A company carrying ONE posted Receipt (Dr Cash 50,000 / Cr Capital 50,000), created and accepted through the
    /// real screens so the voucher is genuinely posted and persisted — not hand-built.
    /// </summary>
    private static Kit SeedOneReceipt(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var on = vm.Company!.FinancialYearStart.AddDays(5);

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = on;
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "50000";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "50000";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(vm.Company!.Vouchers);

        return new Kit(cash, capital, vm.Company!.Vouchers[0]);
    }

    /// <summary>Opens the Day Book and highlights the row that drills to <paramref name="voucherId"/>.</summary>
    private static void OpenDayBookOn(MainWindow window, MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == voucherId);
    }

    /// <summary>Attaches a <c>Generated</c> e-invoice — the state that makes the voucher a FILED statutory
    /// document and therefore refuses Delete under decision D-3.</summary>
    private static void AttachGeneratedIrn(Company c, Guid voucherId)
        => c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), voucherId, "RCPT-1", EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112210000123456", ackDate: InvoiceDate,
            signedQr: "eyJhbGciOi", signedJson: Array.Empty<byte>(),
            cancelledOn: null, cancelReasonCode: null));

    private static void AltD(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
        Pump(window);
    }

    private static void Answer(MainWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        Pump(window);
    }

    // =====================================================================================================
    //  (a) THE ARM AND ITS KEYBOARD GUARDS
    // =====================================================================================================

    /// <summary>
    /// 🔴 THE DRIVING TEST. Alt+D on a highlighted Day-Book voucher row raises ONE confirmation naming the
    /// document — and, the half that matters most, deletes NOTHING until it is answered. A destructive verb that
    /// acted on the keystroke would pass a weaker test that only checked the voucher was gone afterwards.
    /// </summary>
    [AvaloniaFact]
    public void AltD_on_a_day_book_voucher_row_raises_one_confirmation_and_deletes_nothing_yet()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Prompt Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Delete", vm.AcceptPromptText);
            Assert.Contains("Receipt", vm.AcceptPromptText);      // names the document, not just "voucher"
            Assert.Contains("(Y/N)", vm.AcceptPromptText);
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));   // nothing happened yet
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE SINGLE PROMPT (decision D-6). ONE "Y" completes the deletion — there is no second "are you sure".
    /// Written as an explicit assertion rather than left implicit, because the corpus DOES publish a double prompt
    /// for a master and for a group company, and someone reading that will be tempted to add one here. It is
    /// deliberately not copied across: no voucher attestation exists.
    /// </summary>
    [AvaloniaFact]
    public void One_Y_completes_the_deletion_there_is_no_second_confirmation()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Single Prompt Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.Y);

            Assert.False(vm.IsAcceptPromptOpen);                       // no second question
            Assert.Null(vm.Company!.FindVoucher(k.Receipt.Id));        // and it is already gone
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE DRIVING TEST FOR THE VERB ITSELF. "Y" deletes: the voucher leaves the books, its amounts leave every
    /// balance it touched, the live report rebuilds without the row, and the change is PERSISTED — the store is a
    /// snapshot, so a delete that is not saved is a delete that comes back on the next open.
    /// </summary>
    [AvaloniaFact]
    public void Y_deletes_the_voucher_empties_the_books_and_persists()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Y Co");
            var c = vm.Company!;
            var asOf = c.FinancialYearStart.AddYears(1);
            Assert.Equal(50000m, LedgerBalances.SignedClosing(c, k.Cash, asOf));

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindVoucher(k.Receipt.Id));
            Assert.Empty(c.Vouchers);
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Cash, asOf));
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Capital, asOf));

            // The live report rebuilt itself — the row is GONE, not merely greyed (that is Cancel's behaviour).
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.DrillVoucherId == k.Receipt.Id);
            Assert.Contains("deleted", vm.Notice, StringComparison.OrdinalIgnoreCase);

            // …and it survives a reload, which is the only proof the snapshot was written.
            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == c.Name));
            Assert.Null(reopened.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>"N" answers the question with a no: the voucher stays posted and the books are untouched.</summary>
    [AvaloniaFact]
    public void N_leaves_the_voucher_posted()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete N Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);
            Answer(window, PhysicalKey.N);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
            Assert.Equal(50000m, LedgerBalances.SignedClosing(
                vm.Company!, k.Cash, vm.Company!.FinancialYearStart.AddYears(1)));
        }
        finally { Close(window, dir); }
    }

    /// <summary>Escape is the same "no" — the documented dismissal of this confirmation channel.</summary>
    [AvaloniaFact]
    public void Escape_leaves_the_voucher_posted()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Esc Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);
            Answer(window, PhysicalKey.Escape);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE MODIFIER IS AN EXACT MATCH. Ctrl+Alt+D is a different chord and must not reach the app's second
    /// destructive accelerator. Same doctrine the Alt+X arm and the bare-letter quick-jumps already carry: a
    /// <c>HasFlag(Alt)</c> match plus a Ctrl exclusion would admit Alt+Shift+D and Alt+Win+D as well.
    /// </summary>
    [AvaloniaFact]
    public void CtrlAltD_and_AltShiftD_do_not_delete()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Modifier Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control | RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Shift | RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Alt+D on a surface that is NOT a delete target is inert — and critically, the bare-letter Day Book jump
    /// underneath it is not disturbed either way, because that arm requires no modifiers at all.
    /// </summary>
    [AvaloniaFact]
    public void AltD_on_the_Gateway_deletes_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Gateway Co");
            while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
            Pump(window);
            Assert.False(vm.IsDeleteTargetPage);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Alt+D cannot STACK a second question over a live one, and cannot re-point a live CANCEL confirmation at a
    /// delete. The prompt already up stays up, unchanged, and still means what it said.
    /// </summary>
    [AvaloniaFact]
    public void AltD_while_a_cancel_confirmation_is_up_changes_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Over Cancel Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            var cancelPrompt = vm.AcceptPromptText;
            Assert.Contains("Cancel", cancelPrompt);

            AltD(window);
            Assert.Equal(cancelPrompt, vm.AcceptPromptText);      // the question is unchanged

            // …and answering Y still CANCELS (keeps the voucher), it does not delete it.
            Answer(window, PhysicalKey.Y);
            var still = vm.Company!.FindVoucher(k.Receipt.Id);
            Assert.NotNull(still);
            Assert.True(still!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (b) THE FIVE ROUTING SURFACES (§6.4 item 6)
    // =====================================================================================================

    /// <summary>Alt+D from the REGISTER DRILL (the ledger-vouchers column) deletes the highlighted posting's
    /// voucher — the second of the three voucher surfaces.</summary>
    [AvaloniaFact]
    public void AltD_deletes_from_the_register_drill_column()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Register Co");
            var c = vm.Company!;

            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            vm.OpenLedgerVouchers(k.Cash.Id, c.FinancialYearStart, c.FinancialYearStart.AddYears(1));
            Pump(window);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);

            vm.LedgerVouchers!.SelectedRow =
                vm.LedgerVouchers!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);

            var rowsBefore = vm.LedgerVouchers!.Rows.Count;

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindVoucher(k.Receipt.Id));

            // 🔴 THE HALF THIS TEST USED TO OMIT — and the omission is why the drill column was never refreshed.
            // Asserting only that the voucher left the BOOKS passes against a screen that still shows it: the
            // deleted row stayed on the drill with its amount still in the running balance, SelectedRow still
            // pointing at it, and a second Alt+D on the stale row a silent dead key.
            Assert.DoesNotContain(vm.LedgerVouchers!.Rows, r => r.DrillVoucherId == k.Receipt.Id);
            Assert.Equal(rowsBefore - 1, vm.LedgerVouchers!.Rows.Count);
            Assert.Null(vm.LedgerVouchers!.SelectedRow);
            Assert.StartsWith("0.00", vm.LedgerVouchers!.Rows.Last().Amount);   // the closing line moved with it
        }
        finally { Close(window, dir); }
    }

    /// <summary>Alt+D from the VOUCHER DETAIL column deletes the voucher that column is showing — the third
    /// voucher surface, and the only one whose target is the pane itself rather than a highlighted row.</summary>
    [AvaloniaFact]
    public void AltD_deletes_from_the_voucher_detail_column()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Detail Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            vm.OpenVoucherDetail(k.Receipt.Id);
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.Y);

            Assert.Null(vm.Company!.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Alt+D from the CHART OF ACCOUNTS deletes the highlighted LEDGER — resolved through the same
    /// <c>HighlightedRow</c> Enter uses for alteration, so the two verbs can never disagree about which master the
    /// highlight means. The tree rebuilds, so the deleted account leaves the screen.
    /// </summary>
    [AvaloniaFact]
    public void AltD_deletes_an_unused_ledger_from_the_Chart_of_Accounts()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Delete Chart Ledger Co");
            var c = vm.Company!;
            var spare = new DomainLedger(Guid.NewGuid(), "Spare Debtor",
                c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
            c.AddLedger(spare);

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == spare.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Spare Debtor", vm.AcceptPromptText);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindLedger(spare.Id));
            Assert.DoesNotContain(vm.ChartOfAccounts!.Rows, r => r.LedgerId == spare.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Alt+D from the Chart of Accounts deletes the highlighted GROUP too — the other row kind that
    /// surface carries.</summary>
    [AvaloniaFact]
    public void AltD_deletes_an_empty_custom_group_from_the_Chart_of_Accounts()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Delete Chart Group Co");
            var c = vm.Company!;
            var spare = new Group(Guid.NewGuid(), "Spare Group", GroupNature.Asset,
                                  c.FindGroupByName("Current Assets")!.Id);
            c.AddGroup(spare);

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.GroupId == spare.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);
            Assert.Contains("Spare Group", vm.AcceptPromptText);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindGroup(spare.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>Alt+D from the STOCK ITEM master's existing-items list deletes the highlighted item — the fifth
    /// surface, and the list rebuilds so the row leaves the screen.</summary>
    [AvaloniaFact]
    public void AltD_deletes_an_unused_stock_item_from_the_Stock_Item_list()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete Stock Co", "Widget");

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(master.HighlightedIndex >= 0);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Widget", vm.AcceptPromptText);
            Answer(window, PhysicalKey.Y);

            Assert.Null(vm.Company!.FindStockItem(item.Id));
            Assert.DoesNotContain(vm.StockItemMaster!.Existing, r => r.StockItemId == item.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Creates a company with the stock prerequisites plus one named item, through the engine's own
    /// master service (the screens are exercised by their own suites).</summary>
    private static StockItem SeedStockItem(MainWindow window, MainWindowViewModel vm, string company, string item)
    {
        vm.NewCompanyName = company;
        vm.CreateCompany();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Hardware");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var created = masters.CreateStockItem(item, grp.Id, nos.Id);
        Pump(window);
        return created;
    }

    /// <summary>
    /// 🔴 <c>!IsTyping</c> IS LOAD-BEARING ON THE MASTER SURFACES, and this is the test that proves it rather than
    /// asserting it. With the caret in the Stock Item master's Name box — where an operator's caret actually sits
    /// while keying an item — a bare Alt+D must NOT delete the item highlighted in the list behind the form.
    ///
    /// <para>This is the difference between S4's clause and S3's: on the report page nothing takes text, so there
    /// the same predicate was honestly labelled un-pinnable defence in depth. Here it is falsifiable, so it is
    /// pinned.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltD_while_typing_in_the_stock_item_name_does_not_delete()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete Typing Co", "Widget");

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(master.HighlightedIndex >= 0);

            // Put the caret in a real TextBox on the open master, the way an operator keying a name does.
            var box = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible);
            Assert.NotNull(box);
            box!.Focus();
            Pump(window);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindStockItem(item.Id));
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (c) THE REFUSALS — including the one that is the reason this slice is dangerous
    // =====================================================================================================

    /// <summary>
    /// 🔴🔴 <b>THE NUMBERING REFUSAL, END TO END (decision D-3).</b> Alt+D on a voucher carrying a live IRN raises
    /// NO confirmation at all: it refuses on the notice bar, and the refusal OFFERS CANCEL.
    ///
    /// <para><b>Why refusing matters here specifically.</b> <c>LedgerService.NextNumber</c> is <c>max + 1</c>
    /// computed by scanning the vouchers, with no stored counter anywhere in the schema. Deleting the
    /// highest-numbered voucher hands its number to the next post — and this voucher's number is legally frozen,
    /// because the document reached the IRP. Refusing costs no schema and no new state, and Cancel already
    /// preserves the number by keeping the voucher in the collection.</para>
    ///
    /// <para><b>The question is not even asked.</b> Guarding before the prompt rather than after the answer is
    /// deliberate: a confirmation for something that cannot happen teaches an operator to answer prompts without
    /// reading them.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltD_refuses_a_FILED_statutory_document_and_offers_Cancel_instead()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Filed Co");
            AttachGeneratedIrn(vm.Company!, k.Receipt.Id);
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);                     // no question was put
            Assert.Contains("filed statutory document", vm.Notice);
            Assert.Contains("Cancel it instead (Alt+X)", vm.Notice);  // the remedy is NAMED
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
            Assert.Equal(1, vm.Company!.FindVoucher(k.Receipt.Id)!.Number);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE REFERENTIAL REFUSAL, END TO END, AND IT NAMES THE COUNT. A voucher pointed at by two challan links
    /// refuses with "2 documents reference this voucher" — the figure is what makes the message actionable.
    /// </summary>
    [AvaloniaFact]
    public void AltD_refuses_a_referenced_voucher_and_the_notice_names_the_count()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Referenced Co");
            var c = vm.Company!;
            var on = c.FinancialYearStart.AddDays(5);

            var a = new TdsChallan(Guid.NewGuid(), "0001111", "0510308", on, Money.FromRupees(10m), "194C", "200");
            var b = new TdsChallan(Guid.NewGuid(), "0002222", "0510308", on, Money.FromRupees(20m), "194C", "200");
            c.AddTdsChallan(a);
            c.AddTdsChallan(b);
            c.LinkChallanToVoucher(a.Id, k.Receipt.Id);
            c.LinkChallanToVoucher(b.Id, k.Receipt.Id);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("2 documents reference this voucher", vm.Notice);
            Assert.Contains("2 TDS challan links", vm.Notice);
            Assert.NotNull(c.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE MASTER-SIDE REFUSAL, END TO END, AND IT NAMES THE COUNT — the corpus rule (STUDY-GUIDE PDF p.67)
    /// reaching the operator. "Cash" carries the seeded Receipt, so the Chart of Accounts refuses it and says how
    /// many vouchers stand in the way.
    /// </summary>
    [AvaloniaFact]
    public void AltD_refuses_a_ledger_with_transactions_and_the_notice_names_the_voucher_count()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Used Ledger Co");
            var c = vm.Company!;

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == k.Capital.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("1 voucher has already been posted against it", vm.Notice);
            Assert.NotNull(c.FindLedger(k.Capital.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>A predefined ledger is refused from the same surface — "Cash" can never be deleted, transactions
    /// or not, and the refusal says which rule applied.</summary>
    [AvaloniaFact]
    public void AltD_refuses_a_predefined_ledger()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Predefined Co");
            var c = vm.Company!;
            // Delete the voucher first, so the refusal under test is the PREDEFINED rule and not the in-use one.
            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Answer(window, PhysicalKey.Y);
            Assert.Empty(c.Vouchers);

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == k.Cash.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("predefined ledger and cannot be deleted", vm.Notice);
            Assert.NotNull(c.FindLedger(k.Cash.Id));
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (d) THE CONFIRMATION CHANNEL AND ITS DISARM PATHS
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>A PENDING DELETION MUST NOT OUTLIVE ITS PROMPT.</b> Raise the delete confirmation, dismiss it with N,
    /// then open an ordinary master screen and answer ITS Accept prompt with Y. The master must save and the
    /// voucher must still be there.
    ///
    /// <para>This is the exact defect S3's own comment describes one verb earlier — an armed action surviving the
    /// teardown lets a plain "Y" anywhere in the app execute it. With DELETE behind the channel it is a voucher
    /// destroyed by a keystroke aimed at a ledger master, and no message anywhere.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_dismissed_deletion_cannot_be_executed_by_a_later_unrelated_Y()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Disarm Co");
            var c = vm.Company!;

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.N);
            Assert.False(vm.IsAcceptPromptOpen);

            // A completely unrelated master, accepted through its own confirmation.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Later Ledger";
            vm.LedgerMaster!.SelectedGroup = c.FindGroupByName("Sundry Debtors");
            Pump(window);
            Assert.True(vm.RequestMasterAccept());
            Answer(window, PhysicalKey.Y);

            Assert.NotNull(c.FindLedgerByName("Later Ledger"));      // the master saved
            Assert.NotNull(c.FindVoucher(k.Receipt.Id));             // and the voucher is UNTOUCHED
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The same invariant for the other teardown path: NAVIGATING AWAY from the armed prompt. Any change of screen
    /// runs the one teardown, so a later Y on a different screen cannot inherit the armed deletion.
    /// </summary>
    [AvaloniaFact]
    public void Navigating_away_disarms_a_pending_deletion()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Navigate Co");
            var c = vm.Company!;

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);

            vm.ShowChartOfAccounts();       // a real navigation
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            Answer(window, PhysicalKey.Y);
            Assert.NotNull(c.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The confirmation names the voucher it ARMED, and deletes that one — not "whatever is highlighted when Y is
    /// pressed". Arm the prompt on one voucher, move the highlight to another, answer Y: the armed one goes and the
    /// other survives.
    /// </summary>
    [AvaloniaFact]
    public void Y_deletes_the_voucher_the_confirmation_NAMED_not_the_current_highlight()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Named Co");
            var c = vm.Company!;

            // A second posted Receipt on a later date.
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var e = vm.VoucherEntry!;
            e.Date = c.FinancialYearStart.AddDays(9);
            e.Lines[0].SelectedLedger = k.Cash;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "7000";
            e.Lines[1].SelectedLedger = k.Capital;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "7000";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var second = c.Vouchers.First(v => v.Id != k.Receipt.Id);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);

            // Move the highlight AFTER the question was asked.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == second.Id);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindVoucher(k.Receipt.Id));    // the ARMED one went
            Assert.NotNull(c.FindVoucher(second.Id));    // the highlighted one did not
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE GUARDS ARE RE-ASKED IMMEDIATELY BEFORE THE IRREVERSIBLE ACT, not only before the question.</b>
    /// The confirmation sits on screen for an unbounded time and nothing freezes the book underneath it, so a
    /// voucher that was deletable when asked can have become undeletable by the time Y is pressed. Here the
    /// voucher acquires a live IRN between the question and the answer: Y must REFUSE, not delete.
    ///
    /// <para>Written after a mutation run showed the re-ask was unfalsifiable by every other test in this file —
    /// each of which arms and answers with nothing happening in between. Rather than label the re-ask "defence in
    /// depth" and leave it unprovable, this test creates the window it defends.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_voucher_that_becomes_undeletable_between_the_question_and_the_answer_is_refused()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Reask Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);        // deletable at the moment the question was put

            // The book changes underneath the live confirmation — the document is filed at the IRP.
            AttachGeneratedIrn(vm.Company!, k.Receipt.Id);

            Answer(window, PhysicalKey.Y);

            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
            Assert.Contains("Cannot delete", vm.Notice);
            Assert.Contains("filed statutory document", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE S3 HOLE, REPLAYED FOR Alt+D AND SHUT.</b> With the F12 report-configuration column open OVER the
    /// Day Book — the report still bound beneath it, the voucher row still highlighted — Alt+D must be INERT.
    ///
    /// <para><c>IsReportContext</c> is deliberately TRUE in exactly this state, because the report-PARAMETER
    /// shortcuts must keep acting on the report underneath a config panel. S3's first cut wrote its destructive
    /// verb on it and a single Y voided the voucher BEHIND the column the operator was standing in.
    /// <c>IsPickerOpen</c> cannot see it — it looks for an open ComboBox popup, not a Miller column. So
    /// <c>IsDeleteTargetPage</c> asks the narrower question (<c>IsLiveReportPage</c>), and this test is what makes
    /// the difference falsifiable: substituting <c>IsReportContext</c> turns it red.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltD_from_a_config_column_stacked_over_the_report_is_inert()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Stacked Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            vm.OpenReportConfig();          // F12 — leaves Reports bound beneath its own column
            Pump(window);
            Assert.NotEqual(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                       // …the report really is still live underneath
            Assert.True(vm.IsReportContext);                  // …and the WIDER predicate really is true here
            Assert.False(vm.IsDeleteTargetPage);              // …but the one Alt+D uses is not

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindVoucher(k.Receipt.Id));
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (e) THE PRE-FLIGHT
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>A SAVE THAT WAS NEVER GOING TO COMMIT MUST NOT COST THE VOUCHER.</b> <c>CompanyStorage.Save</c> opens
    /// with <c>Company.EnsureValid()</c>, which throws <see cref="ArgumentException"/> on a bad PIN — a state a book
    /// can be LOADED in and only discovers on its next save. A delete cannot be rolled back the way S3's cancel can
    /// (nothing outside the engine assembly can put a voucher back at its original, persisted list index), so the
    /// check runs BEFORE anything is removed.
    ///
    /// <para>Without the pre-flight this test's voucher is gone from memory, absent from the screen, and still on
    /// disk — the aggregate silently ahead of the store, which is the state every later save then carries.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_company_that_cannot_be_saved_refuses_the_delete_and_keeps_the_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Preflight Co");
            var c = vm.Company!;

            // A book in the state CompanyStorage.Save refuses: a PIN that is not six digits.
            c.Pin = "12";

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);      // the guards passed — this is a SAVE problem, not a guard one
            Answer(window, PhysicalKey.Y);

            Assert.NotNull(c.FindVoucher(k.Receipt.Id));         // the voucher survived
            Assert.Contains("Cannot delete", vm.Notice);
            Assert.Contains("PIN", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>A WRITE THAT FAILS AFTER THE PRE-FLIGHT MUST REPORT, NOT ESCAPE.</b> The summary on
    /// <c>PerformPendingDeletion</c> promises the residue "is reported as a named failure telling the operator to
    /// re-open the company; it is not silently swallowed". It was neither: the catch filter was the narrow
    /// <c>is InvalidOperationException or ArgumentException</c> shape that <c>SaveFailure.IsReportable</c> exists to
    /// replace, so with the <c>.db</c> made read-only SQLite Error 8 escaped the window's key handler with the row
    /// already gone from memory and the notice bar EMPTY — on the one destructive verb in the application.
    ///
    /// <para>The test makes the company file genuinely read-only and presses Y. It fails without the filter fix by
    /// throwing out of the key press.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_write_that_fails_after_the_preflight_reports_a_named_failure_instead_of_escaping()
    {
        var (window, vm, dir) = NewWindow();
        var locked = Array.Empty<string>();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Readonly Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            locked = Directory.GetFiles(dir, "*.db", SearchOption.AllDirectories);
            Assert.NotEmpty(locked);                      // otherwise this test proves nothing
            foreach (var f in locked) File.SetAttributes(f, FileAttributes.ReadOnly);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);           // the guards and the pre-flight both passed
            Answer(window, PhysicalKey.Y);                // must NOT throw out of the handler

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotEqual(string.Empty, vm.Notice);
            Assert.Contains("Cannot delete", vm.Notice);
            Assert.Contains("Re-open the company", vm.Notice);
        }
        finally
        {
            foreach (var f in locked)
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch (IOException) { /* best effort */ }
            }
            Close(window, dir);
        }
    }

    // =====================================================================================================
    //  (f) 🔴 THE ROUTES THAT HAD NO REFUSAL TEST AT ALL
    //
    //  Measured with a line-exact mutation harness: BOTH guard call sites on the GROUP route, and BOTH on the
    //  STOCK-ITEM route, could be removed in ONE build with all 2258 Desktop tests green — while the identical
    //  compound on the LEDGER route reddened two. The two master routes S4 ADDED simply had no refusal test
    //  anywhere in the application; the only group test was a success case. These are those tests.
    // =====================================================================================================

    /// <summary>Alt+D on a GROUP that still has children raises NO prompt and shows the count-bearing refusal —
    /// the group twin of <c>AltD_refuses_a_ledger_with_transactions…</c>, which the route never had.</summary>
    [AvaloniaFact]
    public void AltD_refuses_a_group_with_children_and_the_notice_names_the_master_count()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Delete Used Group Co");
            var c = vm.Company!;
            var parent = new Group(Guid.NewGuid(), "Regional", GroupNature.Asset,
                                   c.FindGroupByName("Current Assets")!.Id);
            c.AddGroup(parent);
            c.AddLedger(new DomainLedger(Guid.NewGuid(), "Regional Debtor", parent.Id, Money.Zero,
                                         openingIsDebit: true));

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.GroupId == parent.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("1 master is filed under it", vm.Notice);
            Assert.Contains("1 ledger", vm.Notice);
            Assert.NotNull(c.FindGroup(parent.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>Alt+D on a PREDEFINED group refuses — with both guard sites gone this ran
    /// <c>Company.RemoveGroup</c> unconditionally on a primary Balance-Sheet head, leaving every child pointing at
    /// a parent that does not exist and the report classification walking up through a missing ancestor.</summary>
    [AvaloniaFact]
    public void AltD_refuses_a_predefined_group()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Delete Predefined Group Co");
            var c = vm.Company!;
            var predefined = c.FindGroupByName("Sundry Debtors")!;
            Assert.True(predefined.IsPredefined);

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.GroupId == predefined.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("predefined group and cannot be deleted", vm.Notice);
            Assert.NotNull(c.FindGroup(predefined.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>Alt+D on a STOCK ITEM that is in use raises NO prompt and shows the count-bearing refusal — the
    /// stock-item twin the route never had. With both of its guard sites gone this ran
    /// <c>Company.RemoveStockItem</c> on an item that entries still held by Guid.</summary>
    [AvaloniaFact]
    public void AltD_refuses_a_stock_item_in_use_and_the_notice_names_the_entry_count()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete Used Stock Co", "Widget");
            var c = vm.Company!;
            new InventoryService(c).AddOpeningBalance(
                item.Id, c.MainLocation!.Id, 3m, Money.FromRupees(50m));

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(master.HighlightedIndex >= 0);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("1 entry references it", vm.Notice);
            Assert.Contains("1 opening balance", vm.Notice);
            Assert.NotNull(c.FindStockItem(item.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE PRE-ACT RE-ASK, for each MASTER kind. Only the voucher site was pinned; the ledger, group and
    /// stock-item calls could each be deleted with the whole Desktop suite green, while the comment they share
    /// applies by its own wording to all four ("the confirmation has been on screen for an unbounded time and
    /// nothing stops another surface changing the book meanwhile").
    ///
    /// <para>Each case arms the prompt on a deletable master, makes it UNDELETABLE while the question is up, then
    /// answers Y — and the master must survive with a named refusal.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData("ledger")]
    [InlineData("group")]
    [InlineData("stockitem")]
    public void A_master_that_becomes_undeletable_between_the_question_and_the_answer_is_refused(string kind)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            if (kind == "stockitem")
            {
                var item = SeedStockItem(window, vm, "Delete Race Stock Co", "Widget");
                var c0 = vm.Company!;
                vm.ShowStockItemMaster();
                Pump(window);
                var master = vm.StockItemMaster!;
                master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
                AltD(window);
                Assert.True(vm.IsAcceptPromptOpen);

                // …and now it is in use.
                new InventoryService(c0).AddOpeningBalance(item.Id, c0.MainLocation!.Id, 1m, Money.FromRupees(5m));
                Answer(window, PhysicalKey.Y);

                Assert.NotNull(c0.FindStockItem(item.Id));
                Assert.Contains("Cannot delete stock item", vm.Notice);
                return;
            }

            SeedOneReceipt(window, vm, "Delete Race Master Co");
            var c = vm.Company!;
            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;

            if (kind == "ledger")
            {
                var spare = new DomainLedger(Guid.NewGuid(), "Spare Debtor",
                    c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
                c.AddLedger(spare);
                chart.Refresh();
                chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == spare.Id);
                Assert.True(chart.HighlightedIndex >= 0);

                AltD(window);
                Assert.True(vm.IsAcceptPromptOpen);

                // …and now a pay head names it.
                c.AddPayHead(new PayHead(Guid.NewGuid(), "Basic", PayHeadType.Earnings,
                    PayHeadCalculationType.OnAttendance) { LedgerId = spare.Id });
                Answer(window, PhysicalKey.Y);

                Assert.NotNull(c.FindLedger(spare.Id));
                Assert.Contains("Cannot delete ledger", vm.Notice);
                return;
            }

            var group = new Group(Guid.NewGuid(), "Spare Group", GroupNature.Asset,
                                  c.FindGroupByName("Current Assets")!.Id);
            c.AddGroup(group);
            chart.Refresh();
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.GroupId == group.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);

            // …and now a ledger is filed under it.
            c.AddLedger(new DomainLedger(Guid.NewGuid(), "Late Child", group.Id, Money.Zero, openingIsDebit: true));
            Answer(window, PhysicalKey.Y);

            Assert.NotNull(c.FindGroup(group.Id));
            Assert.Contains("Cannot delete group", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (g) 🔴 THE ARM'S OWN FLAGS AND CLAUSES
    // =====================================================================================================

    /// <summary>
    /// The Alt+D keystroke comes back <c>Handled</c> on a delete-capable surface, and NOT handled where the arm
    /// declines. This is the analogue of <c>AltX_on_a_report_row_comes_back_Handled</c>, which S3 wrote one slice
    /// earlier after <c>e.Handled = true</c> survived its own deletion against the whole Desktop suite — and which
    /// S4 copied the ARM and its justification comment from without copying the test. Measured here first:
    /// deleting the flag left all 2258 tests green, and so did dropping <c>vm.IsDeleteTargetPage</c> from the arm,
    /// which is what the negative leg pins.
    /// </summary>
    [AvaloniaFact]
    public void AltD_comes_back_Handled_on_a_delete_surface_and_not_elsewhere()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            // Registered for BOTH phases with handledEventsToo: a Tunnel-only observer does not see the flag the
            // window's own tunnel handler set, so the bubble leg is what reads the outcome.
            bool? handled = null;
            window.AddHandler(
                InputElement.KeyDownEvent,
                (object? _, KeyEventArgs e) => { if (e.Key == Key.D) handled = e.Handled; },
                Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);

            var k = SeedOneReceipt(window, vm, "Delete Handled Co");

            // NEGATIVE — on the Gateway the arm declines, so nothing consumes the key.
            handled = null;
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(handled ?? true, "Alt+D off a delete surface should not be consumed by this arm");

            // POSITIVE — on the Day Book row it is consumed.
            OpenDayBookOn(window, vm, k.Receipt.Id);
            handled = null;
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.True(handled ?? false, "Alt+D on a highlighted voucher row must come back Handled");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <c>!IsPickerOpen</c> ON THE ALT+D ARM, PINNED. The arm's comment claims that on the two master surfaces
    /// both <c>!IsTyping(e)</c> and <c>!IsPickerOpen(e)</c> "ARE independently falsifiable and are tested as such",
    /// and then names one test for two clauses — dropping <c>!IsPickerOpen</c> left all 2258 tests green. Unlike
    /// S3's pair the clause really is falsifiable here, because the Stock Item master carries real ComboBoxes over
    /// a live existing-items list, so it is pinned rather than re-labelled.
    /// </summary>
    [AvaloniaFact]
    public void AltD_with_a_picker_open_on_the_stock_item_master_does_not_delete()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete Picker Co", "Widget");

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(master.HighlightedIndex >= 0);

            var picker = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(cb => cb.IsEffectivelyVisible);
            Assert.NotNull(picker);                       // otherwise this test proves nothing
            picker!.Focus();
            Pump(window);
            picker.IsDropDownOpen = true;
            Pump(window);
            Assert.True(picker.IsDropDownOpen);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindStockItem(item.Id));

            // POSITIVE CONTROL — the same keystroke with the dropdown shut DOES raise the question, so the test
            // is pinning the picker clause and not some other refusal.
            picker.IsDropDownOpen = false;
            Pump(window);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 CTRL+A IS INERT OVER AN ARMED DELETE. The Ctrl+A arm sits above the WI-11 confirmation arm in the tunnel
    /// chain, and <c>ActivateSelected</c> used to call <c>ResetMasterAcceptPrompt</c> at its top — so one Ctrl+A
    /// over <c>Delete stock item 'Widget'?</c> silently discarded the delete question and CREATED the unrelated
    /// item typed into the form instead, with nothing on the notice bar. A destructive question replaced by a
    /// write, which is the S1 Alt+Y hole shape on the destructive channel.
    /// </summary>
    [AvaloniaFact]
    public void CtrlA_over_an_armed_delete_neither_deletes_nor_creates()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete CtrlA Co", "Widget");
            var c = vm.Company!;

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.Name = "Gizmo";                        // an unrelated item half-keyed into the form
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(master.HighlightedIndex >= 0);

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Delete stock item 'Widget'?", vm.AcceptPromptText);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);                       // the question is STILL up
            Assert.NotNull(c.FindStockItem(item.Id));                 // Widget was not deleted
            Assert.Null(c.FindStockItemByName("Gizmo"));              // …and Gizmo was not created
            Assert.Contains("Answer the question on screen first", vm.Notice);

            // …and answering it still works, so nothing is stranded.
            Answer(window, PhysicalKey.Y);
            Assert.Null(c.FindStockItem(item.Id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE TEARDOWN ORDER, pinned by the condition it exists for. <c>ConfirmMasterAccept</c> reads both armed
    /// slots, tears the prompt down, and only THEN runs the action — and the doc comment calls that ordering the
    /// thing that makes "the channel is disarmed no matter what the action does" true of the destructive verb.
    /// Reversing it left all 2258 tests green.
    ///
    /// <para>The order is observable without inducing a crash: the observer records
    /// <see cref="MainWindowViewModel.IsAcceptPromptOpen"/> at the instant the ACTION reports its outcome. Under
    /// the shipped order the prompt is already down; under the reversed order it is still up.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_confirmation_is_torn_down_BEFORE_the_deletion_runs()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Teardown Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            bool? promptOpenWhenTheActionSpoke = null;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.Notice)
                    && vm.Notice.Length > 0
                    && promptOpenWhenTheActionSpoke is null)
                    promptOpenWhenTheActionSpoke = vm.IsAcceptPromptOpen;
            };

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.Y);

            Assert.Null(vm.Company!.FindVoucher(k.Receipt.Id));
            Assert.False(promptOpenWhenTheActionSpoke ?? true,
                "the deletion ran while its own confirmation was still armed");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 A PREVIOUS OUTCOME NEVER SHARES THE STATUS-BAR ROW WITH A LIVE QUESTION. Deleting the clear left a
    /// refusal about one document sitting beside a live confirmation about a different one, on the app's only
    /// destructive verb, and all 2258 tests stayed green.
    ///
    /// <para>The second half is the reason the clear moved out of <c>RequestDeleteHighlighted</c> and into
    /// <c>Arm</c>: clearing on every Alt+D wiped the refusal the operator was reading whenever the route then
    /// returned false because nothing was highlighted.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_new_question_clears_the_previous_outcome_but_a_dead_key_does_not()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Notice Co");
            var c = vm.Company!;
            AttachGeneratedIrn(c, k.Receipt.Id);

            // A second, ordinary voucher that IS deletable.
            var on = c.FinancialYearStart.AddDays(6);
            new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, on, new[]
            {
                new EntryLine(k.Cash.Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(k.Capital.Id, Money.FromRupees(100m), DrCr.Credit),
            }));
            var plain = c.Vouchers.Last();

            OpenDayBookOn(window, vm, k.Receipt.Id);
            AltD(window);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("filed statutory document", vm.Notice);   // the refusal is on the bar

            // A DEAD KEY must not wipe it: no row highlighted at all, which is the quiet no-op route.
            vm.Reports!.SelectedRow = null;
            AltD(window);
            Assert.Contains("filed statutory document", vm.Notice);   // still readable

            // A NEW QUESTION does clear it.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == plain.Id);
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Equal(string.Empty, vm.Notice);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 ALT+D IS INERT WHILE THE STOCK ITEM MASTER IS OPEN FOR ALTERATION. <c>ShowStockItemAlter</c> opens the
    /// alteration column under the SAME screen id as creation, so the surface predicate could not tell them apart
    /// and Alt+D deleted the very master the open form was editing — leaving the caption reading "Stock Item
    /// Alteration", the operator's keyed changes in the form, and the Ctrl+A that would have saved them a
    /// completely silent no-op afterwards.
    /// </summary>
    [AvaloniaFact]
    public void AltD_on_an_open_stock_item_alteration_does_not_delete_the_item_being_altered()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var item = SeedStockItem(window, vm, "Delete Altering Co", "Widget");

            vm.ShowStockItemMaster();
            Pump(window);
            var master = vm.StockItemMaster!;
            master.HighlightedIndex = master.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);
            Assert.True(vm.AlterHighlightedStockItemRow());
            Pump(window);
            Assert.True(vm.StockItemMaster!.IsAltering);
            vm.StockItemMaster!.Name = "Widget MK2";
            vm.StockItemMaster!.HighlightedIndex =
                vm.StockItemMaster!.Existing.ToList().FindIndex(r => r.StockItemId == item.Id);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(vm.Company!.FindStockItem(item.Id));
            Assert.True(vm.StockItemMaster!.IsAltering);              // the form is untouched
            Assert.Equal("Widget MK2", vm.StockItemMaster!.Name);
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (h) 🔴 THE REMEDY THE REFUSAL NAMES MUST BE REACHABLE FROM WHERE THE OPERATOR IS STANDING
    // =====================================================================================================

    /// <summary>
    /// Both voucher refusals end in "Cancel it instead (Alt+X)", but the Alt+X arm is gated on
    /// <c>IsLiveReportPage</c> while Alt+D is offered on the register drill and the voucher-detail column as well.
    /// Measured on both: the refusal appears, a real Alt+X on the same surface does nothing at all, and the
    /// destructive verb is then the ONLY lifecycle verb available there. The refusal now says where the key works.
    /// </summary>
    [AvaloniaFact]
    public void On_a_drill_column_the_refusal_says_where_Alt_X_actually_works()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Routing Co");
            var c = vm.Company!;
            // Referential (not filed), so Cancel really is available — just not from here.
            var challan = new TdsChallan(Guid.NewGuid(), "0001111", "0510308",
                c.FinancialYearStart.AddDays(5), Money.FromRupees(10m), "194C", "200");
            c.AddTdsChallan(challan);
            c.LinkChallanToVoucher(challan.Id, k.Receipt.Id);

            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            vm.OpenLedgerVouchers(k.Cash.Id, c.FinancialYearStart, c.FinancialYearStart.AddYears(1));
            Pump(window);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            Assert.False(vm.IsLiveReportPage);            // …so the Alt+X arm will not fire here
            vm.LedgerVouchers!.SelectedRow =
                vm.LedgerVouchers!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("1 document references this voucher", vm.Notice);
            Assert.Contains("Alt+X works on the Day Book", vm.Notice);

            // And the claim is true: Alt+X here really does nothing.
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(c.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The worse half: on the surface where Alt+X DOES work, the commonest filed case is refused by Cancel too —
    /// a live IRN must be cancelled at the portal first. The refusal used to send the operator to a key that then
    /// refused them, with no explanation of the order the two steps come in.
    /// </summary>
    [AvaloniaFact]
    public void A_filed_document_refusal_says_the_portal_cancellation_comes_first()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Delete Portal Order Co");
            AttachGeneratedIrn(vm.Company!, k.Receipt.Id);
            OpenDayBookOn(window, vm, k.Receipt.Id);

            AltD(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("filed statutory document", vm.Notice);
            Assert.Contains("Cancel it instead (Alt+X)", vm.Notice);
            Assert.Contains("cancel that at the portal first", vm.Notice);

            // The claim is true: Alt+X on the same row is refused for exactly that reason.
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("live IRN", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    // =====================================================================================================
    //  (i) 🔴 ER-13 — A BOOK THAT NEVER USES THESE VERBS IS UNCHANGED
    //
    //  The design's §8.3 stated this as "the .db bytes match the pre-change baseline", and shipped no test.
    //  MEASURED, that assertion is unachievable for ANY book, independent of S4: entry_lines.id is
    //  `INTEGER PRIMARY KEY AUTOINCREMENT`, SQLite keeps its high-water mark in sqlite_sequence, and Save is a
    //  delete-all + full re-insert — so a plain load-then-save renumbers those surrogate ids and the file bytes
    //  change for a book nobody touched. The INSTRUMENT is therefore the canonical export, which carries the
    //  semantic model and no surrogate ids. The substance of ER-13 does hold, and these are the tests that say so.
    // =====================================================================================================

    /// <summary>ER-13, part 1: a populated company's canonical export is byte-identical across a load → save round
    /// trip. This is the assertion §8.3 should have made about the <c>.db</c> and could not.</summary>
    [AvaloniaFact]
    public void The_canonical_export_is_byte_identical_across_a_load_and_save()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "ER13 RoundTrip Co");
            var storage = new CompanyStorage(dir);
            var before = Apex.Ledger.Io.CanonicalXml.Export(vm.Company!);

            storage.Save(vm.Company!);
            var reloaded = storage.Load(storage.ListCompanies().Single(e => e.Name == "ER13 RoundTrip Co"));
            var after = Apex.Ledger.Io.CanonicalXml.Export(reloaded);

            Assert.Equal(before, after);
        }
        finally { Close(window, dir); }
    }

    /// <summary>ER-13, part 2: a REFUSED Alt+D and a DECLINED one (answered N) each leave the canonical export
    /// byte-identical — the delete verb touches nothing until it is both permitted and confirmed.</summary>
    [AvaloniaFact]
    public void A_refused_and_a_declined_delete_both_leave_the_book_byte_identical()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "ER13 Untouched Co");
            var c = vm.Company!;
            OpenDayBookOn(window, vm, k.Receipt.Id);
            var baseline = Apex.Ledger.Io.CanonicalXml.Export(c);

            // DECLINED — the question is put and answered N.
            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.N);
            Assert.NotNull(c.FindVoucher(k.Receipt.Id));
            Assert.Equal(baseline, Apex.Ledger.Io.CanonicalXml.Export(c));

            // REFUSED — the guards answer before the question is put. (The IRN record is itself book content, so
            // the baseline is re-taken after attaching it; what is under test is the refusal, not the fixture.)
            AttachGeneratedIrn(c, k.Receipt.Id);
            var withIrn = Apex.Ledger.Io.CanonicalXml.Export(c);
            AltD(window);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("filed statutory document", vm.Notice);
            Assert.Equal(withIrn, Apex.Ledger.Io.CanonicalXml.Export(c));
        }
        finally { Close(window, dir); }
    }
}
