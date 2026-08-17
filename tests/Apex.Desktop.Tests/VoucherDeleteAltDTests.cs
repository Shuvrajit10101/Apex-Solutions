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
/// on an unrelated master screen destroy a voucher; (e) persistence and the pre-flight.</para>
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

            AltD(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Answer(window, PhysicalKey.Y);

            Assert.Null(c.FindVoucher(k.Receipt.Id));
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
}
