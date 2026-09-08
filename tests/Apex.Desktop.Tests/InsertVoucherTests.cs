using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
/// Census row 5.5 — <b>Insert Voucher (Alt+I)</b> driven through the REAL <see cref="MainWindow"/> key tunnel
/// (<c>window.KeyPressQwerty</c>), not by calling a view-model method. A row that no user can reach from the
/// keyboard is not shipped, and this project has filed three dead features that a view-model-only test would
/// have passed.
///
/// <para><b>FIDELITY (R7 / RULING 14 tier 1).</b> The chord is the vendor's — <i>"To insert a voucher in a
/// report"</i> (<c>help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/</c>) — and the gesture is the
/// Day Book page's: <i>"Select the entry above which you want to insert the transaction, press <b>Alt</b>+<b>I</b>
/// (Insert Vch)"</i>
/// (<c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c>). The renumbering
/// itself is pinned engine-side by <c>Apex.Ledger.Tests.VoucherInsertionTests</c>; this file proves the operator
/// can actually cause it, and that the incumbent Alt+I owner did not lose its door.</para>
///
/// <para>🔴 <b>THE COLLISION THIS FILE EXISTS TO PIN.</b> Alt+I was already spent on the POS tender-mode toggle,
/// and the owed ruling U-6 asked who owns the chord. Nothing here rebinds either side: the POS arm is scoped to
/// <see cref="Screen.PosBilling"/> and this door is scoped to the Day Book, so the predicates are disjoint.
/// <see cref="AltI_on_the_POS_screen_still_toggles_the_tender_mode"/> is the regression that proves the incumbent
/// still answers, and it is the assertion that would fail if a later agent "simplified" the scoping away.</para>
/// </summary>
public sealed class InsertVoucherTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInsVch_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { /* temp */ }
    }

    /// <summary>Creates a company and posts <paramref name="count"/> Journals on consecutive days, each Dr party
    /// / Cr Sales 1,000.00, through the shipped entry screen — so the numbers are the real numbering path's.</summary>
    private static (DomainLedger Party, DomainLedger Sales, VoucherType Journal) SeedJournals(
        MainWindow window, MainWindowViewModel vm, string name, int count)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;

        // A fresh company seeds Cash but NOT a Sales ledger, so the credit leg needs one created. Measured, not
        // assumed: without it Lines[1] stayed empty, the entry was silently un-saveable, and the seed posted
        // nothing while every assertion still read as if it had.
        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Sales";
        vm.LedgerMaster!.SelectedGroup = c.FindGroupByName("Sales Accounts");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var sales = c.FindLedgerByName("Sales")!;

        // 🔴 THE DEBIT LEG IS CASH, NOT A SUNDRY-DEBTORS PARTY, AND THAT IS DELIBERATE. A party ledger carries
        // bill-wise entry, so a complete, balanced two-line entry against one still reports CanAccept = false
        // until its bill allocations are filled — measured here, and it is exactly the silent un-saveable state
        // that made an earlier draft of this seed post nothing while reading as if it had. Cash keeps this file
        // testing INSERT rather than testing bill-wise allocation.
        var party = c.FindLedgerByName("Cash")!;

        var journal = c.FindVoucherTypeByName("Journal")!;
        journal.Numbering = NumberingMethod.Automatic;

        for (var i = 0; i < count; i++)
        {
            vm.OpenVoucher(journal);
            var e = vm.VoucherEntry!;
            e.Date = c.FinancialYearStart.AddDays(i);
            e.Lines[0].SelectedLedger = party;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "1000.00";
            e.Lines[1].SelectedLedger = sales;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "1000.00";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        }

        Assert.Equal(count, c.Vouchers.Count(v => v.TypeId == journal.Id));
        return (party, sales, journal);
    }

    /// <summary>Opens the Day Book and highlights the row whose voucher carries <paramref name="number"/>.</summary>
    private static void HighlightJournalNumbered(MainWindowViewModel vm, VoucherType journal, int number)
    {
        vm.OpenReport(ReportKind.DayBook);
        var target = vm.Company!.Vouchers.Single(v => v.TypeId == journal.Id && v.Number == number);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == target.Id);
    }

    // ============================================================ (a) THE DRIVING TEST — the key opens the door

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> A real <b>Alt+I</b> on a highlighted Day-Book row opens the <i>Insert
    /// Voucher</i> picker as a cascade column to the RIGHT of the live Day Book — the report survives beneath it
    /// (the operator must be able to Esc back to the row they were standing on), and the column offers the
    /// company's own voucher types.
    ///
    /// <para>On today's <c>main</c> this fails at the first assertion: nothing in the tunnel claims Alt+I outside
    /// the POS screen, so the screen stays <see cref="Screen.Report"/>.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltI_on_a_highlighted_day_book_row_opens_the_Insert_Voucher_picker()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (_, _, journal) = SeedJournals(window, vm, "Ins A", 3);
            HighlightJournalNumbered(vm, journal, 2);
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);

            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal("Insert Voucher", vm.ScreenTitle);

            // Appended, not replacing: the Day Book column is still there beneath the picker.
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.NotNull(vm.Reports);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);

            var picker = vm.Columns[^1];
            Assert.Equal("Insert Voucher", picker.Title);
            Assert.Contains(picker.Items, i => i.Label == "Journal");
            // The header is a header, and the rows are real actions the keyboard can land on.
            Assert.Contains(picker.Items, i => i.Label == "Select Voucher Type");
            Assert.True(picker.Items.Any(i => i.IsSelectable));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE END-TO-END TEST — the keystroke actually RENUMBERS A STATUTORY SERIES.</b> Five Journals
    /// numbered 1..5; Alt+I above the one numbered 3, pick Journal, key a balanced entry, Ctrl+A. The vendor's
    /// example then holds on the real book: the newcomer carries 3, the old 3/4/5 became 4/5/6, and the series is
    /// a contiguous run 1..6 with no repeat.
    ///
    /// <para>The contiguity assertion is the load-bearing one. "The newcomer got 3" would pass on an engine that
    /// also left two vouchers holding 6 — a duplicated document number on a filed series, which is the defect
    /// this row can produce and the reason it is not a cosmetic verb.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltI_then_saving_renumbers_the_whole_series_with_no_gap_and_no_duplicate()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (party, sales, journal) = SeedJournals(window, vm, "Ins B", 5);
            var c = vm.Company!;
            var idsBefore = c.Vouchers.Where(v => v.TypeId == journal.Id).Select(v => v.Id).ToHashSet();

            HighlightJournalNumbered(vm, journal, 3);
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);

            // Choose "Journal" in the picker exactly as the keyboard would — the row's own action.
            vm.Columns[^1].Items.Single(i => i.Label == "Journal").Activate();

            var e = vm.VoucherEntry!;
            Assert.NotNull(e);
            e.Lines[0].SelectedLedger = party;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "250.00";
            e.Lines[1].SelectedLedger = sales;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "250.00";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            var series = c.Vouchers.Where(v => v.TypeId == journal.Id).ToList();
            Assert.Equal(6, series.Count);

            var newcomer = series.Single(v => !idsBefore.Contains(v.Id));
            Assert.Equal(3, newcomer.Number);                       // "will attain voucher number 3"
            Assert.Equal(250m, newcomer.TotalDebit.Amount);         // and it is the entry we actually keyed

            var numbers = series.Select(v => v.Number).OrderBy(n => n).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, numbers);      // contiguous …
            Assert.Equal(6, numbers.Distinct().Count());            // … and unique
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Cancelling the inserted entry (Esc out of it) leaves the series EXACTLY as it was. Renumbering to make
    /// room for a voucher that was never posted would open a real gap in a statutory series — so the plan is
    /// applied only from the entry screen's post-save hook, never at pick time.
    /// </summary>
    [AvaloniaFact]
    public void Abandoning_the_inserted_entry_leaves_every_existing_number_untouched()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (_, _, journal) = SeedJournals(window, vm, "Ins C", 4);
            var c = vm.Company!;

            HighlightJournalNumbered(vm, journal, 2);
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);
            vm.Columns[^1].Items.Single(i => i.Label == "Journal").Activate();
            Assert.NotNull(vm.VoucherEntry);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(4, c.Vouchers.Count(v => v.TypeId == journal.Id));
            Assert.Equal(new[] { 1, 2, 3, 4 },
                c.Vouchers.Where(v => v.TypeId == journal.Id).Select(v => v.Number).OrderBy(n => n).ToArray());
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (b) THE STATUTORY REFUSAL, THROUGH THE SHELL

    /// <summary>
    /// The engine's filed-document refusal reaches the OPERATOR: with a generated IRN on the voucher numbered 4,
    /// an insert above 2 would rewrite that filed number, so the picker refuses by name on the notice bar,
    /// returns to the live Day Book, and moves nothing. The sentence asserted is the shipped constant, so the
    /// shell and the engine cannot drift into telling the operator two different things.
    /// </summary>
    [AvaloniaFact]
    public void An_insert_that_would_renumber_a_filed_document_is_refused_on_the_notice_bar()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (_, _, journal) = SeedJournals(window, vm, "Ins D", 5);
            var c = vm.Company!;
            var filed = c.Vouchers.Single(v => v.TypeId == journal.Id && v.Number == 4);
            c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
                Guid.NewGuid(), filed.Id, "JV/4", EInvoiceStatus.Generated,
                irn: new string('a', 64), ackNo: "112400000000001", ackDate: filed.Date,
                signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

            HighlightJournalNumbered(vm, journal, 2);
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);
            vm.Columns[^1].Items.Single(i => i.Label == "Journal").Activate();

            // No entry screen opened, the Day Book is live again, and the refusal names the remedy.
            Assert.Null(vm.VoucherEntry);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(VoucherInsertion.FiledDocumentRefusal, vm.Notice);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 },
                c.Vouchers.Where(v => v.TypeId == journal.Id).Select(v => v.Number).OrderBy(n => n).ToArray());
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (c) THE COLLISION — the incumbent keeps its door

    /// <summary>
    /// 🔴 <b>THE REGRESSION THAT GUARDS THE OWED U-6 RULING.</b> Alt+I on the POS Billing screen still toggles
    /// the tender mode — the incumbent was NOT rebound to make room for Insert, it was left scoped where it
    /// always was. If a later agent widens either predicate, this reddens.
    /// </summary>
    [AvaloniaFact]
    public void AltI_on_the_POS_screen_still_toggles_the_tender_mode()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Ins E";
            vm.CreateCompany();
            vm.OpenPosBilling();
            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);

            var before = vm.PosBilling!.IsMultiTender;
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);
            Assert.NotEqual(before, vm.PosBilling!.IsMultiTender);

            // And it is still a TOGGLE — a second press returns it.
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);
            Assert.Equal(before, vm.PosBilling!.IsMultiTender);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The button bar advertises the chord, and emits EXACTLY ONE Alt+I row per context — the rule this file's
    /// sibling Alt+A comment states, because the shell's key/hint lookup takes the first match and a second row
    /// would shadow the first. On the Day Book the row reads <i>Insert Vch</i> and is enabled; on POS it is still
    /// <i>Payment Mode</i>.
    /// </summary>
    [AvaloniaFact]
    public void The_button_bar_carries_exactly_one_context_correct_AltI_row()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var (_, _, journal) = SeedJournals(window, vm, "Ins F", 2);
            HighlightJournalNumbered(vm, journal, 1);

            var onDayBook = vm.ButtonBar.Where(b => b.Key == "Alt+I").ToList();
            Assert.Single(onDayBook);
            Assert.Equal("Insert Vch", onDayBook[0].Caption);
            Assert.True(onDayBook[0].Enabled);

            vm.OpenPosBilling();
            var onPos = vm.ButtonBar.Where(b => b.Key == "Alt+I").ToList();
            Assert.Single(onPos);
            Assert.Equal("Payment Mode", onPos[0].Caption);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Alt+I is inert where the vendor does not put it: on the Gateway there is no report, so the keystroke is
    /// not claimed and nothing opens. An enabled-looking chord that fires nothing is register defect IV-31.
    /// </summary>
    [AvaloniaFact]
    public void AltI_does_nothing_on_the_Gateway()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Ins G";
            vm.CreateCompany();
            var screen = vm.CurrentScreen;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Alt);

            Assert.Equal(screen, vm.CurrentScreen);
            Assert.DoesNotContain(vm.ButtonBar, b => b.Key == "Alt+I" && b.Enabled);
        }
        finally { Close(window, dir); }
    }
}
