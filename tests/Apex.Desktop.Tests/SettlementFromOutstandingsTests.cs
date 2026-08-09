using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 10.11 slice S2 (work item VL-4, register row IV-5) — <b>settlement comes off Ctrl+B</b>.
///
/// <para>The defect: Ctrl+B was bound app-wide and unconditionally to a path that POSTED a real Receipt or
/// Payment for every spacebar-selected bill — always the full pending amount, always through a ledger literally
/// named "Cash", dated at the report's as-of, with no preview, no confirmation and no undo. In TallyPrime Ctrl+B
/// is <i>Basis of Values</i>, a report option that re-bases how figures are DISPLAYED and
/// <b>writes nothing to the books</b> [TallyHelp keyboard-shortcuts, Reports: "Ctrl+B — To view values in
/// different ways in a report"]. TallyPrime's Bills Outstanding carries no settlement action at all: a bill is
/// settled by keying a Receipt/Payment and choosing <i>Against Reference</i> from the List of Pending Bills
/// [CORPUS-SG p.92 §5.5].</para>
///
/// <para>The fix these tests lock: Ctrl+B posts nothing from anywhere and is RESERVED; <b>Alt+A</b> on the
/// Outstandings report (TallyPrime's own bottom-bar "Add voucher in report") opens a Single-Entry
/// Receipt/Payment PRE-LOADED with the selected bills as Against-Reference allocations, which the operator
/// confirms — date, cash/bank ledger and per-bill amounts — before pressing Accept.</para>
///
/// <para><b>Every money figure here is odd-paise on purpose.</b> Round thousands assert nothing: a 50-paisa
/// defect survived this project's entire life under six round-number assertions. The two bills are 47,318.63 and
/// 18,904.31, and their sum 66,222.94 is odd too, so a half-rupee slip cannot hide in the total.</para>
///
/// <para>Everything keyboard-shaped is driven through the REAL <see cref="MainWindow"/> tunnel handler
/// (<c>window.KeyPressQwerty</c>) — never by asserting that a binding exists in isolation.</para>
/// </summary>
public sealed class SettlementFromOutstandingsTests
{
    // ---- the odd-paise fixture -------------------------------------------------------------------
    private const decimal Bill901 = 47318.63m;   // ODD PAISA
    private const decimal Bill902 = 18904.31m;   // ODD PAISA
    private const decimal BothBills = 66222.94m; // …and their sum is odd too
    private const string Ref901 = "INV-901";
    private const string Ref902 = "INV-902";

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexSettleOff_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>
    /// A company with a bill-by-bill debtor "Acme Traders" carrying TWO open odd-paise bills, posted through the
    /// engine. Returns the debtor.
    /// </summary>
    private static DomainLedger SeedTwoOpenBills(MainWindowViewModel vm, string companyName)
    {
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;

        var sales = new DomainLedger(Guid.NewGuid(), "Sales A/c",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var debtor = new DomainLedger(Guid.NewGuid(), "Acme Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(sales);
        c.AddLedger(debtor);

        var salesVt = c.FindVoucherTypeByName("Sales")!;
        var svc = new LedgerService(c);
        PostSale(svc, c, salesVt, debtor, sales, Ref901, Bill901, dayOffset: 3);
        PostSale(svc, c, salesVt, debtor, sales, Ref902, Bill902, dayOffset: 5);
        return debtor;
    }

    private static void PostSale(
        LedgerService svc, Company c, VoucherType salesVt,
        DomainLedger debtor, DomainLedger sales, string reference, decimal amount, int dayOffset)
        => svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, c.FinancialYearStart.AddDays(dayOffset), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(amount), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
        }));

    /// <summary>A bank ledger the operator can steer the settlement through INSTEAD of the "Cash" ledger.</summary>
    private static DomainLedger AddBank(MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var bank = new DomainLedger(Guid.NewGuid(), "HDFC Bank 004411",
            c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(bank);
        return bank;
    }

    /// <summary>Opens Receivables and spacebar-selects the given row indices through the real tunnel.</summary>
    private static void SelectBills(MainWindow window, MainWindowViewModel vm, params int[] rowIndices)
    {
        vm.OpenOutstandings(OutstandingsKind.Receivables);
        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        foreach (var i in rowIndices)
        {
            vm.Outstandings!.HighlightedIndex = i;
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        }
        Assert.Equal(rowIndices.Length, vm.Outstandings!.SelectedRows.Count);
    }

    private static OutstandingRowViewModel Row(MainWindowViewModel vm, string reference)
        => vm.Outstandings!.Rows.Single(r => r.Bill.Reference == reference);

    // ================================================================ (a) Ctrl+B posts NOTHING

    /// <summary>
    /// THE HARM TEST. The operator does exactly what TallyPrime trains them to do — spacebar-select lines on a
    /// report, then Ctrl+B for Basis of Values — and the books must be untouched. Before this slice both bills
    /// were knocked off and two real Receipt vouchers existed that the operator never confirmed and (until the
    /// later cancel/delete slices) could neither cancel nor delete.
    /// </summary>
    [AvaloniaFact]
    public void CtrlB_on_the_Outstandings_report_posts_nothing_and_leaves_every_bill_open()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "CtrlB Posts Nothing Co");
            var before = vm.Company!.Vouchers.Count;
            Assert.Equal(2, before);

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            // Nothing posted, nothing knocked off, and we are still standing on the report.
            Assert.Equal(before, vm.Company!.Vouchers.Count);
            Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
            Assert.Equal(2, vm.Outstandings!.Rows.Count);
            Assert.Equal(Bill901, Row(vm, Ref901).Bill.Pending.Amount);
            Assert.Equal(Bill902, Row(vm, Ref902).Bill.Pending.Amount);
            // No Receipt voucher of any kind came into existence.
            var receipt = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
            Assert.DoesNotContain(vm.Company!.Vouchers, v => v.TypeId == receipt.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>The only assertion here that BITES</b>, split out and named for exactly what it proves. Base
    /// <c>MainWindowViewModel</c> added a <c>Ctrl+B — Settle Bills</c> row to the button bar unconditionally; a
    /// badge for a key that now fires nothing is register defect IV-31, so the row had to go with the binding.
    /// Restoring that row turns this red.
    /// </summary>
    [AvaloniaFact]
    public void No_button_bar_row_advertises_CtrlB_anywhere()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "CtrlB No Badge Co");

            vm.ShowGateway();
            Assert.DoesNotContain(vm.ButtonBar, b => b.Key == "Ctrl+B");

            // …and on the screen that used to own the badge, where it was ENABLED rather than merely hinted.
            vm.OpenOutstandings(OutstandingsKind.Receivables);
            Assert.DoesNotContain(vm.ButtonBar, b => b.Key == "Ctrl+B");
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>CHARACTERIZATION, NOT RED-PROOF — read the caveat before counting this as evidence.</b> It records that
    /// Ctrl+B on the Gateway changes nothing, which is what "reserved for Basis of Values" should look like from
    /// the outside. But these three assertions <i>also passed at base</i>: base <c>SettleBills()</c> was already
    /// screen-scoped (<c>if (IsOutstandingsScreen) Outstandings!.SettleSelected();</c>), so on the Gateway the old
    /// arm swallowed the key and did nothing observable either. Re-binding the arm would leave this green.
    ///
    /// <para>There is no view-model-observable difference between "unbound" and "bound but inert here", because
    /// after slice S1 narrowed <c>CanQuickJump</c> to <c>KeyModifiers.None</c> Ctrl+B genuinely reaches nothing
    /// anywhere — which is the point of the reservation. The real bites for the removal live in
    /// <see cref="CtrlB_on_the_Outstandings_report_posts_nothing_and_leaves_every_bill_open"/> (the harm test) and
    /// <see cref="No_button_bar_row_advertises_CtrlB_anywhere"/> (the badge). This one is kept as a standing guard
    /// against a future Ctrl+B arm that is NOT screen-scoped.</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlB_on_the_Gateway_changes_nothing_observable()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "CtrlB Reserved Co");
            vm.ShowGateway();
            var screen = vm.CurrentScreen;
            var message = vm.Message;
            var before = vm.Company!.Vouchers.Count;

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            Assert.Equal(screen, vm.CurrentScreen);
            Assert.Equal(message, vm.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The un-shadowing check the removal makes possible. Ctrl+B used to be swallowed by the settlement arm
    /// before it could reach the bare-letter report quick-jump (<c>Key.B</c> → Balance Sheet). Slice S1 narrowed
    /// <c>CanQuickJump</c> to <c>KeyModifiers.None</c>; without that, deleting this arm would have handed Ctrl+B
    /// to the Balance Sheet on every menu screen. This pins the interaction between the two slices.
    /// </summary>
    [AvaloniaFact]
    public void CtrlB_does_not_fall_through_to_the_bare_B_Balance_Sheet_quick_jump()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "CtrlB No Fallthrough Co");
            vm.ShowCompanySelect();
            var screen = vm.CurrentScreen;

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            Assert.Equal(screen, vm.CurrentScreen);
            Assert.Null(vm.Reports);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ (b) Alt+A pre-loads

    /// <summary>
    /// THE DRIVING TEST. Alt+A on Outstandings opens a Receipt in Single Entry pre-loaded with EXACTLY the
    /// selected bills as Against-Reference allocations — and posts nothing until the operator accepts.
    ///
    /// <para>Three assertions here are load-bearing and must not be relaxed:</para>
    /// <list type="bullet">
    /// <item><c>SingleEntryAccount</c> IS NULL — defaulting it to a ledger named "Cash" is the IV-5 defect
    /// wearing a new hat. The operator picks cash or bank.</item>
    /// <item><c>BillAllocations.Count == 2</c> EXACTLY, not <c>&gt;= 2</c>. Setting the party ledger seeds one
    /// blank New-Ref row; a tolerated leftover is <c>IsBlank</c>, so <c>BillSplitOk</c> ignores it silently while
    /// it renders on screen as an empty bill row that reads as a bug.</item>
    /// <item>exactly ONE Particulars line — the entry screen opens with a blank starter particular, and a
    /// pre-load that appends rather than reusing it leaves a stray empty row behind.</item>
    /// </list>
    /// </summary>
    [AvaloniaFact]
    public void AltA_on_Outstandings_opens_a_Receipt_preloaded_with_the_selected_bills_and_posts_nothing()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Preload Co");
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var entry = vm.VoucherEntry!;

            // The seeded, predefined Receipt series — receivables settle with money IN.
            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            Assert.Equal(receipt.Id, entry.Type.Id);

            // Single Entry, and the cash/bank Account is deliberately EMPTY.
            Assert.True(entry.IsSingleEntry);
            Assert.Null(entry.SingleEntryAccount);

            // Exactly one Particulars line — the debtor — for the exact sum of the two odd-paise bills.
            var line = Assert.Single(entry.SingleEntryParticulars);
            Assert.Same(debtor, line.SelectedLedger);
            Assert.Equal("66222.94", line.AmountText);
            Assert.Equal(BothBills, line.ParsedAmount);

            // EXACTLY two allocations, both Agst Ref, in selection order, each for its own pending amount.
            Assert.Equal(2, line.BillAllocations.Count);
            Assert.All(line.BillAllocations, a => Assert.Equal(BillRefType.AgstRef, a.RefType));
            Assert.Equal(new[] { Ref901, Ref902 }, line.BillAllocations.Select(a => a.Name).ToArray());
            Assert.Equal(new[] { "47318.63", "18904.31" },
                line.BillAllocations.Select(a => a.AmountText).ToArray());

            // Nothing is posted, and both bills are still open for their full amounts.
            Assert.Equal(before, vm.Company!.Vouchers.Count);
            Assert.DoesNotContain(vm.Company!.Vouchers, v => v.TypeId == receipt.Id);
            var open = Apex.Ledger.Reports.Outstandings.OpenBillsFor(
                vm.Company!, debtor, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1)).ToList();
            Assert.Equal(2, open.Count);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The Payables mirror: Alt+A on Bills Payable pre-loads a <b>Payment</b>, not a Receipt. The polarity
    /// inversion between the two is the documented Single-Entry trap (BOOK pp.29, 32), so it is asserted on the
    /// posted legs rather than on the type name alone.
    /// </summary>
    [AvaloniaFact]
    public void AltA_on_Bills_Payable_preloads_a_Payment_and_debits_the_creditor()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "AltA Payables Co";
            vm.CreateCompany();
            var c = vm.Company!;
            var purchases = new DomainLedger(Guid.NewGuid(), "Purchase A/c",
                c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
            var creditor = new DomainLedger(Guid.NewGuid(), "Bharat Supplies",
                c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false,
                maintainBillByBill: true, defaultCreditPeriodDays: 45);
            c.AddLedger(purchases);
            c.AddLedger(creditor);
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Purchase")!.Id, c.FinancialYearStart.AddDays(4), new[]
                {
                    new EntryLine(purchases.Id, Money.FromRupees(31_557.09m), DrCr.Debit),
                    new EntryLine(creditor.Id, Money.FromRupees(31_557.09m), DrCr.Credit, new[]
                    {
                        new BillAllocation(BillRefType.NewRef, "BILL-77", Money.FromRupees(31_557.09m)),
                    }),
                }));
            var bank = AddBank(vm);

            vm.OpenOutstandings(OutstandingsKind.Payables);
            vm.Outstandings!.HighlightedIndex = 0;
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var entry = vm.VoucherEntry!;
            var payment = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsPredefined);
            Assert.Equal(payment.Id, entry.Type.Id);

            entry.SingleEntryAccount = bank;
            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            var posted = c.Vouchers.Single(v => v.TypeId == payment.Id);
            // Settling a payable DEBITS the creditor and CREDITS the bank (BOOK p.32).
            Assert.Equal(DrCr.Debit, posted.Lines.Single(l => l.LedgerId == creditor.Id).Side);
            Assert.Equal(DrCr.Credit, posted.Lines.Single(l => l.LedgerId == bank.Id).Side);
            Assert.Equal(31_557.09m, posted.Lines.Single(l => l.LedgerId == bank.Id).Amount.Amount);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Alt+A with nothing selected prompts and opens nothing — it must not strand the operator.</summary>
    [AvaloniaFact]
    public void AltA_with_no_bill_selected_prompts_and_opens_no_voucher()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Nothing Selected Co");
            vm.OpenOutstandings(OutstandingsKind.Receivables);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
            Assert.Equal("Select one or more bills with the spacebar, then press Alt+A to settle them.",
                vm.Outstandings!.Message);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ (c) the operator steers off "Cash"

    /// <summary>
    /// The heart of the fix. The shipped path ALWAYS contra'd a ledger literally named "Cash" and refused
    /// outright when none existed. Now the operator chooses, and a bank settlement posts correctly for the exact
    /// odd-paise total — with nothing touching Cash.
    /// </summary>
    [AvaloniaFact]
    public void The_preloaded_settlement_posts_through_a_bank_the_operator_picks_not_a_ledger_named_Cash()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Bank Settle Co");
            var bank = AddBank(vm);
            var cash = vm.Company!.FindLedgerByName("Cash");
            Assert.NotNull(cash);   // Cash EXISTS — the point is that we do not silently use it

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;

            // Until the operator names the cash/bank side the voucher cannot be accepted.
            Assert.False(entry.CanAccept);
            entry.SingleEntryAccount = bank;
            Assert.True(entry.CanAccept);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            var posted = vm.Company!.Vouchers.Single(v => v.TypeId == receipt.Id);

            // Dr HDFC Bank 66,222.94 / Cr Acme Traders 66,222.94, and Cash is untouched.
            Assert.Equal(DrCr.Debit, posted.Lines.Single(l => l.LedgerId == bank.Id).Side);
            Assert.Equal(BothBills, posted.Lines.Single(l => l.LedgerId == bank.Id).Amount.Amount);
            var partyLine = posted.Lines.Single(l => l.LedgerId == debtor.Id);
            Assert.Equal(DrCr.Credit, partyLine.Side);
            Assert.Equal(BothBills, partyLine.Amount.Amount);
            Assert.DoesNotContain(posted.Lines, l => l.LedgerId == cash!.Id);

            // Two Agst-Ref allocations summing to exactly the odd-paise total.
            Assert.Equal(2, partyLine.BillAllocations.Count);
            Assert.All(partyLine.BillAllocations, a => Assert.Equal(BillRefType.AgstRef, a.RefType));
            Assert.Equal(BothBills, partyLine.BillAllocations.Sum(a => a.Amount.Amount));

            // Both bills are knocked off — the refreshed report is empty.
            Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
            Assert.Empty(vm.Outstandings!.Rows);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ (d) a PART payment is expressible

    /// <summary>
    /// The capability the shipped path made unreachable: it always knocked <c>r.Bill.Pending</c>, so a part
    /// payment could not be expressed through the UI at all. Here the operator drops the second bill, types a
    /// partial figure against the first, and the remainder stays open to the paisa.
    /// </summary>
    [AvaloniaFact]
    public void A_part_payment_of_a_preloaded_bill_settles_only_what_the_operator_typed()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Part Payment Co");
            var bank = AddBank(vm);
            const decimal part = 19_999.37m;               // ODD PAISA part payment
            const decimal remainder = 27_319.26m;          // 47,318.63 − 19,999.37

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var line = Assert.Single(entry.SingleEntryParticulars);

            // Drop INV-902 entirely, and knock only part of INV-901.
            var row902 = line.BillAllocations.Single(a => a.Name == Ref902);
            line.RemoveBillAllocation(row902);
            line.BillAllocations.Single(a => a.Name == Ref901).AmountText = "19999.37";
            line.AmountText = "19999.37";
            entry.SingleEntryAccount = bank;

            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            // INV-901 remains open for the exact remainder; INV-902 is untouched.
            var asOf = vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1);
            var open = Apex.Ledger.Reports.Outstandings
                .OpenBillsFor(vm.Company!, debtor, asOf)
                .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, open.Count);
            Assert.Equal(remainder, open[Ref901].Pending.Amount);
            Assert.Equal(Bill902, open[Ref902].Pending.Amount);

            // …and the bank received exactly the part payment, not the full bill.
            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            var posted = vm.Company!.Vouchers.Single(v => v.TypeId == receipt.Id);
            Assert.Equal(part, posted.Lines.Single(l => l.LedgerId == bank.Id).Amount.Amount);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ (e) the surviving validation

    /// <summary>
    /// D5 says the Agst-Ref bill name is a free TextBox in every panel, so the operator can edit the pre-loaded
    /// reference into something that is not a bill. <c>SettleAndPost</c> used to be the ONLY caller of
    /// <c>BuildSettlementAllocations</c> — i.e. the only path that ever checked a reference against a genuinely
    /// open bill — so deleting it without this guard would make settlement LESS safe than before. Here a
    /// capital O typed for a zero is refused at Accept, by name.
    /// </summary>
    [AvaloniaFact]
    public void An_edited_AgstRef_name_that_is_not_an_open_bill_is_refused_at_Accept()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Bad Ref Co");
            var bank = AddBank(vm);
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var line = Assert.Single(entry.SingleEntryParticulars);
            entry.SingleEntryAccount = bank;
            Assert.True(entry.CanAccept);   // it was acceptable before the name was corrupted

            line.BillAllocations[0].Name = "INV-9O1";   // capital letter O for the zero

            Assert.False(entry.CanAccept);
            Assert.False(entry.Accept());
            Assert.Contains("INV-9O1", entry.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The same guard on the other axis: an amount edited ABOVE the bill's pending is an over-settlement and is
    /// refused. 47,318.64 is one paisa more than the bill — the smallest failure the money type can express.
    /// </summary>
    [AvaloniaFact]
    public void An_edited_allocation_that_over_settles_the_bill_by_one_paisa_is_refused_at_Accept()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Over Settle Co");
            var bank = AddBank(vm);
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var line = Assert.Single(entry.SingleEntryParticulars);
            entry.SingleEntryAccount = bank;

            line.BillAllocations[0].AmountText = "47318.64";   // ONE PAISA over the pending
            line.AmountText = "47318.64";

            Assert.False(entry.CanAccept);
            Assert.False(entry.Accept());
            Assert.Contains(Ref901, entry.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// THE AGGREGATE over-settlement, which the per-row cap above does NOT catch. The pre-load stamps two
    /// Agst-Ref rows; the operator retypes the SECOND row's Name from INV-902 to INV-901 — a one-character slip
    /// between adjacent invoice numbers, and the Name is a free TextBox (register defect D5). Both rows are then
    /// individually under INV-901's pending (47,318.63 and 18,904.31 vs 47,318.63) while their SUM, 66,222.94, is
    /// 18,904.31 OVER it.
    ///
    /// <para>What that costs if it posts: INV-901's accumulated pending goes to −18,904.31 and
    /// <c>Outstandings.OpenBillsFor</c> DROPS a non-positive pending, so the over-knocked bill vanishes from the
    /// report while INV-902 stays open at 18,904.31 — the customer is dunned for money they have already paid, the
    /// party's ledger balance nets to zero, and the "Σ open bills == ledger closing balance" invariant is broken
    /// with no message anywhere.</para>
    /// </summary>
    [AvaloniaFact]
    public void Two_AgstRef_rows_naming_the_same_bill_are_refused_when_they_together_over_settle_it()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Duplicate Ref Co");
            var bank = AddBank(vm);
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var line = Assert.Single(entry.SingleEntryParticulars);
            entry.SingleEntryAccount = bank;
            Assert.True(entry.CanAccept);   // acceptable before the second reference is corrupted

            // The slip: the second row now names the FIRST bill. Amounts and the line total are untouched, so
            // BillSplitOk still holds (47,318.63 + 18,904.31 == 66,222.94) and nothing else in the app objects.
            line.BillAllocations[1].Name = Ref901;

            Assert.False(entry.CanAccept);
            Assert.False(entry.Accept());
            Assert.Contains(Ref901, entry.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);

            // Both bills survive at their FULL pending — nothing was knocked off and nothing vanished.
            var open = Apex.Ledger.Reports.Outstandings
                .OpenBillsFor(vm.Company!, debtor, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1))
                .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, open.Count);
            Assert.Equal(Bill901, open[Ref901].Pending.Amount);
            Assert.Equal(Bill902, open[Ref902].Pending.Amount);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The aggregate cap must not become a "one row per bill" rule. Splitting ONE bill across two Agst-Ref rows
    /// is legitimate — the operator itemising two remittances against a single invoice — as long as the total
    /// stays within the pending amount. Here 28,411.37 + 18,907.26 == 47,318.63 exactly, and it posts.
    /// </summary>
    public const decimal SplitA = 28_411.37m;   // ODD PAISA
    public const decimal SplitB = 18_907.26m;   // …and SplitA + SplitB == Bill901 to the paisa

    [AvaloniaFact]
    public void Two_AgstRef_rows_naming_the_same_bill_still_post_when_they_stay_within_its_pending()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Split One Bill Co");
            var bank = AddBank(vm);

            SelectBills(window, vm, 0);      // INV-901 only
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var line = Assert.Single(entry.SingleEntryParticulars);
            entry.SingleEntryAccount = bank;

            // Split the single pre-loaded row into two rows, both against INV-901.
            line.BillAllocations[0].AmountText = "28411.37";
            var second = line.AddBillAllocation(BillRefType.AgstRef);
            second.Name = Ref901;
            second.AmountText = "18907.26";

            Assert.True(entry.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            // INV-901 is fully knocked off by the two rows together; INV-902 is untouched.
            var open = Apex.Ledger.Reports.Outstandings
                .OpenBillsFor(vm.Company!, debtor, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1))
                .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Bill902, Assert.Single(open).Value.Pending.Amount);

            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            var posted = vm.Company!.Vouchers.Single(v => v.TypeId == receipt.Id);
            Assert.Equal(Bill901, posted.Lines.Single(l => l.LedgerId == bank.Id).Amount.Amount);
            Assert.Equal(SplitA + SplitB, Bill901);   // the fixture's own arithmetic, pinned
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Rewrite of <c>VoucherTypeNavigationIdentityTests.Bill_settlement_refuses_to_post_under_a_deactivated_receipt_type</c>
    /// onto Alt+A. The rule it exists to lock — a settlement is an ordinary Receipt the operator could have keyed
    /// by hand, so if they could not have keyed it we must not open one for them — survives the gesture change
    /// verbatim, message included.
    /// </summary>
    [AvaloniaFact]
    public void Settlement_refuses_when_the_only_Receipt_series_is_deactivated()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Inactive Receipt Co");
            var receipt = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
            receipt.IsActive = false;
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal("No active 'Receipt' voucher type is configured for this company.",
                vm.Outstandings!.Message);
            Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
            Assert.Equal(before, vm.Company!.Vouchers.Count);
            Assert.Equal(Bill901, Row(vm, Ref901).Bill.Pending.Amount);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The THIRD route to the same over-settlement, which pooling per LINE would miss: two Particulars lines for
    /// the SAME party, each carrying one Agst-Ref row against the same bill. Validating line-by-line hands the
    /// engine two separate single-knock batches, each of which passes its own aggregate cap; only pooling by
    /// party sees the total. 30,000.11 + 25,000.13 = 55,000.24 against INV-901's 47,318.63.
    /// </summary>
    [AvaloniaFact]
    public void Two_Particulars_lines_for_one_party_cannot_together_over_settle_the_same_bill()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "AltA Two Lines One Party Co");
            var bank = AddBank(vm);
            var before = vm.Company!.Vouchers.Count;

            SelectBills(window, vm, 0);      // INV-901 only — one pre-loaded line, one Agst-Ref row
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            var first = Assert.Single(entry.SingleEntryParticulars);
            first.BillAllocations[0].AmountText = "30000.11";
            first.AmountText = "30000.11";

            // A SECOND Particulars line for the SAME debtor, knocking the SAME bill again.
            var second = entry.AddSingleEntryParticular();
            second.SelectedLedger = debtor;
            second.AmountText = "25000.13";
            Assert.True(second.IsBillWise);
            second.BillAllocations[0].RefType = BillRefType.AgstRef;
            second.BillAllocations[0].Name = Ref901;
            second.BillAllocations[0].AmountText = "25000.13";

            entry.SingleEntryAccount = bank;

            Assert.False(entry.CanAccept);
            Assert.False(entry.Accept());
            Assert.Contains(Ref901, entry.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);

            var open = Apex.Ledger.Reports.Outstandings
                .OpenBillsFor(vm.Company!, debtor, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1))
                .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Bill901, open[Ref901].Pending.Amount);
            Assert.Equal(Bill902, open[Ref902].Pending.Amount);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ (f) the pre-load's own edges

    /// <summary>
    /// <c>PreloadSettlement</c> must RETURN on a party carrying no allocations, not spin.
    /// <c>VoucherLineViewModel.RemoveBillAllocation</c> refuses to drop the last row of a bill-wise line, so the
    /// trailing cleanup <c>while (BillAllocations.Count &gt; party.Allocations.Count)</c> made no progress once it
    /// hit that floor: with a target of zero the condition stayed <c>1 &gt; 0</c> forever and the UI thread hung —
    /// the app would have to be killed. Both types are public, so any caller or test can construct this; today's
    /// sole production caller is safe only by an invariant held one call-site away, with nothing asserting it.
    ///
    /// <para>Driven on a worker thread with a hard join so a regression FAILS here instead of hanging the suite,
    /// and on an UNBOUND view model (never shown in the window) so there is no cross-thread binding traffic.</para>
    /// </summary>
    [AvaloniaFact]
    public void PreloadSettlement_returns_on_a_party_with_no_allocations_instead_of_spinning()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var debtor = SeedTwoOpenBills(vm, "Preload Empty Allocations Co");
            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            var entry = new VoucherEntryViewModel(
                vm.Company!, receipt, new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });

            // A second bill-by-bill debtor that carries NO bills — the empty-allocations party.
            var billless = new DomainLedger(Guid.NewGuid(), "Quiet Traders",
                vm.Company!.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
                maintainBillByBill: true, defaultCreditPeriodDays: 30);
            vm.Company!.AddLedger(billless);

            // The EMPTY party FIRST, a real one SECOND. This also pins that skipping the empty one leaves the
            // blank starter Particulars line free for the real party, instead of stranding it beside a new row —
            // which is what keying the reuse on the loop INDEX rather than on blankness would have done.
            var preload = new SettlementPreload(
                receipt, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1),
                new[]
                {
                    new SettlementPartyPreload(billless, Array.Empty<BillAllocation>()),
                    new SettlementPartyPreload(debtor, new[]
                    {
                        new BillAllocation(BillRefType.AgstRef, Ref901, Money.FromRupees(Bill901)),
                    }),
                });

            Exception? failure = null;
            var worker = new System.Threading.Thread(() =>
            {
                try { entry.PreloadSettlement(preload); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            worker.Start();

            Assert.True(worker.Join(TimeSpan.FromSeconds(10)),
                "PreloadSettlement did not return within 10s — the trailing cleanup loop is spinning again.");
            Assert.Null(failure);

            // EXACTLY ONE Particulars line — the real party, in the reused blank starter. The bill-less party
            // contributed nothing at all: no zero-amount row, and no stranded blank beside the stamped one.
            var line = Assert.Single(entry.SingleEntryParticulars);
            Assert.Same(debtor, line.SelectedLedger);
            Assert.Equal("47318.63", line.AmountText);
            Assert.Equal(Ref901, Assert.Single(line.BillAllocations).Name);
            Assert.DoesNotContain(entry.Lines, l => ReferenceEquals(l.SelectedLedger, billless));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>Characterization, deliberately pinning what the code DOES rather than what a comment once claimed.</b>
    /// Passing <c>date: null</c> is NOT date protection. <c>Outstandings.AsOf</c> is the maximum voucher date in
    /// the company, and <c>VoucherEntryViewModel</c>'s own default is the same expression — so the pre-loaded
    /// settlement opens dated exactly at the report's as-of, including when the newest voucher is a year-end
    /// journal in a period the operator is no longer working in. That is the app-wide voucher-date convention and
    /// it is visible in an editable field the operator confirms; the point of this test is that no future comment
    /// may claim the hazard was handled here when it was not.
    /// </summary>
    [AvaloniaFact]
    public void The_preloaded_date_is_the_ordinary_entry_default_not_a_shielded_one()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Date Default Co");
            var c = vm.Company!;

            // A year-end journal dated far AFTER the two sales — it becomes the max voucher date in the book.
            var yearEnd = c.FinancialYearStart.AddYears(1).AddDays(-1);
            var sales = c.FindLedgerByName("Sales A/c")!;
            var indirect = new DomainLedger(Guid.NewGuid(), "Rounding Off",
                c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
            c.AddLedger(indirect);
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, yearEnd, new[]
                {
                    new EntryLine(indirect.Id, Money.FromRupees(11.17m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(11.17m), DrCr.Credit),
                }));

            SelectBills(window, vm, 0);
            Assert.Equal(yearEnd, vm.Outstandings!.AsOf);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            // EQUAL, not different: the entry default lands on the very date the report was drawn at.
            Assert.Equal(yearEnd, vm.VoucherEntry!.Date);
        }
        finally { Close(window, tempDir); }
    }

    // ================================================================ dispatcher-ordering regressions

    /// <summary>
    /// <b>Characterization of the cancel route, pinned because a reviewer read it as a divergence.</b> Alt+A opens
    /// the settlement voucher through <c>OpenPageColumn</c>, which REPLACES the rightmost page column — so the
    /// Outstandings report goes away and Escape from the voucher lands on the Gateway, not back on the report.
    ///
    /// <para>That is the app-wide convention for every page-opening route, and the Day-Book Alt+A is NOT an
    /// exception to it: its non-destructive append covers only the intermediate voucher-type PICKER column, and
    /// <c>PickAddVoucherType</c> explicitly removes that picker so <c>OpenPageColumn</c>'s trim "leaves exactly one
    /// page column (the new voucher, in the Day Book's place)". This test drives BOTH gestures through the real
    /// window and asserts they land identically, so the two cannot silently drift apart.</para>
    /// </summary>
    [AvaloniaFact]
    public void Escape_from_an_AltA_voucher_lands_the_same_way_from_Outstandings_and_from_the_Day_Book()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Cancel Route Co");

            // (1) Outstandings → Alt+A → Escape.
            SelectBills(window, vm, 0);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            var fromOutstandings = vm.CurrentScreen;

            // (2) Day Book → Alt+A → pick "Receipt" from the picker → Escape.
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            for (var i = 0; i < vm.Menu.Count + 2 && vm.Menu[vm.SelectedIndex].Label != "Receipt"; i++)
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("Receipt", vm.Menu[vm.SelectedIndex].Label);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(fromOutstandings, vm.CurrentScreen);
            Assert.Null(vm.Outstandings);
            Assert.Null(vm.Reports);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Alt+A is claimed by three screens now. The Day-Book "Add voucher in report" arm shipped in an earlier
    /// slice and must NOT be shadowed by the new Outstandings arm — the two guards are disjoint, and this proves
    /// it through the real dispatcher rather than by reading the guards.
    /// </summary>
    [AvaloniaFact]
    public void AltA_on_the_Day_Book_still_opens_the_add_voucher_picker()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Day Book Co");
            vm.OpenReport(ReportKind.DayBook);
            Assert.True(vm.IsDayBookReport);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The button bar advertises exactly ONE Alt+A row, and on Outstandings it must be the settlement one — the
    /// shell's Fire()/hint lookup takes the FIRST key match, so a wrong branch order would make the Outstandings
    /// page advertise "Tax Analysis" and fire the POS handler.
    /// </summary>
    [AvaloniaFact]
    public void The_Outstandings_button_bar_advertises_Alt_A_settlement_exactly_once()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltA Button Bar Co");
            vm.OpenOutstandings(OutstandingsKind.Receivables);

            var altA = Assert.Single(vm.ButtonBar, b => b.Key == "Alt+A");
            Assert.Equal("Settle Bills", altA.Caption);
            Assert.True(altA.Enabled);

            // …and on the Day Book the SAME single row is the add-voucher one.
            vm.OpenReport(ReportKind.DayBook);
            var dayBookAltA = Assert.Single(vm.ButtonBar, b => b.Key == "Alt+A");
            Assert.Equal("Add Voucher", dayBookAltA.Caption);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>STANDING ORDERING GUARD, NOT RED-PROOF for the Ctrl+B removal.</b> Say so plainly, because the name
    /// invites the opposite reading: the deleted arm required <c>KeyModifiers.Control</c> and Alt+B never carries
    /// Control, so the arm's presence or absence is invisible to this keystroke — restore the Ctrl+B arm verbatim
    /// and this still passes. It earns its place as a guard against a FUTURE Ctrl+B (Basis of Values) arm being
    /// written too loosely — e.g. testing <c>Key.B</c> with only a <c>HasFlag(Control)</c>-free guard, or sitting
    /// above these arms with a modifier test that admits Alt — which is exactly how the leaked-prompt-flag defect
    /// shipped. Key.B is crowded: seven Alt+B arms sit below where the Ctrl+B arm used to be.
    ///
    /// <para>Two of the seven are covered: Form 26Q here, and the <c>Screen.VoucherEntry</c> batch-allocation arm
    /// in <see cref="AltB_on_a_preloaded_settlement_voucher_is_a_harmless_no_op"/> — which is the one that matters
    /// most to this slice, because a pre-loaded settlement OPENS on Screen.VoucherEntry.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltB_on_the_Form_26Q_screen_still_fires_after_the_CtrlB_arm_is_removed()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltB Survives Co");

            // Form 26Q is gated on TDS being enabled (ER-13), so turn it on through the real config page.
            vm.ShowGstConfig();
            vm.GstConfig!.TdsEnabled = true;
            vm.GstConfig!.Tan = "MUMA12345B";
            Assert.True(vm.GstConfig!.ApplyTds());
            vm.ShowGateway();

            vm.OpenForm26Q();
            Assert.Equal(Screen.Form26Q, vm.CurrentScreen);
            vm.Form26Q!.ExportFolder = tempDir;   // keep the real save-return write inside the temp dir

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Alt);

            // SaveReturnForm26Q writes the FVU file and returns to the menu — the arm fired.
            Assert.NotEqual(Screen.Form26Q, vm.CurrentScreen);
            Assert.Null(vm.Form26Q);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The second Alt+B owner, and the one this slice actually stands on: the batch-allocation arm scoped to
    /// <c>Screen.VoucherEntry</c>, which is the screen a pre-loaded settlement opens on. A Receipt carries no
    /// batch-tracked item lines, so the arm must be a no-op that leaves the pre-load exactly as it was — not a
    /// stray sub-screen over a half-confirmed settlement.
    /// </summary>
    [AvaloniaFact]
    public void AltB_on_a_preloaded_settlement_voucher_is_a_harmless_no_op()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedTwoOpenBills(vm, "AltB On Settlement Co");
            var bank = AddBank(vm);

            SelectBills(window, vm, 0, 1);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            var entry = vm.VoucherEntry!;
            entry.SingleEntryAccount = bank;
            Assert.True(entry.CanAccept);

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Alt);

            // Still on the settlement, still pre-loaded, still acceptable — and nothing posted.
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);
            var line = Assert.Single(entry.SingleEntryParticulars);
            Assert.Equal(new[] { Ref901, Ref902 }, line.BillAllocations.Select(a => a.Name).ToArray());
            Assert.Equal(BothBills, line.ParsedAmount);
            Assert.True(entry.CanAccept);
            var receipt = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);
            Assert.DoesNotContain(vm.Company!.Vouchers, v => v.TypeId == receipt.Id);
        }
        finally { Close(window, tempDir); }
    }
}
