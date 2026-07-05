using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for RQ-5 (part 2) — the four Exception reports (Negative Stock, Negative Cash / Bank,
/// Memorandum Register, Reversing Journal Register) wired into <see cref="ReportsViewModel"/> and nested under
/// the Reports → Exception Reports cascade in <see cref="MainWindowViewModel"/>. The engine projections are
/// trusted (covered by the engine <c>ExceptionReportsTests</c>); these tests pin the UI wiring: each report
/// opens through the accounting grid, builds the expected rows on a seeded book, and is reachable through the
/// menu. Seeding mirrors the engine test's helpers so the surfaced exceptions actually appear.
/// </summary>
public sealed class ExceptionReportsViewModelTests : IDisposable
{
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ExceptionReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexExceptionTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---- seeding helpers (mirror the engine ExceptionReportsTests so the exceptions really surface) ----

    /// <summary>A trading book with one item driven to −5 on-hand (Widget) and one positive item (Gadget).</summary>
    private static Company SeedInventory(out Guid negItemId)
    {
        var c = CompanyFactory.CreateSeeded("Exc Inv Co", Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");

        var neg = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        neg.StandardCost = Money.FromRupees(100m);
        var ok = masters.CreateStockItem("Gadget", grp.Id, nos.Id);
        ok.StandardCost = Money.FromRupees(50m);
        var godownId = c.MainLocation!.Id;

        masters.AddOpeningBalance(ok.Id, godownId, 10m, Money.FromRupees(50m));

        // Raw outward of 5 units, appended directly to bypass the no-negative posting guard.
        var deliveryType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote);
        var outward = new InventoryVoucher(Guid.NewGuid(), deliveryType.Id, new DateOnly(2024, 4, 10),
            new[] { new InventoryAllocation(neg.Id, godownId, 5m, StockDirection.Outward) });
        c.AddInventoryVoucher(outward);

        negItemId = neg.Id;
        return c;
    }

    /// <summary>A book whose HDFC Bank ledger is overdrawn to Cr 30,000; cash left positive.</summary>
    private static Company SeedOverdrawnBank(out Domain.Ledger bank)
    {
        var c = CompanyFactory.CreateSeeded("Exc Cash Co", Start);

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(bank);

        var expense = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(expense);

        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Payment")!.Id, new DateOnly(2024, 4, 12),
            new[]
            {
                new EntryLine(expense.Id, Money.FromRupees(30000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(30000m), DrCr.Credit),
            }));
        return c;
    }

    /// <summary>A book with the ledgers needed for a memo / reversing journal, plus a positive cash balance.</summary>
    private static Company SeedMemoAndReversing(out Domain.Ledger rent, out Domain.Ledger provision, out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("Exc Memo Co", Start);
        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(500000m);
        cash.OpeningIsDebit = true;

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        provision = new Domain.Ledger(Guid.NewGuid(), "Provision for Expenses", c.FindGroupByName("Provisions")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(rent);
        c.AddLedger(provision);
        return c;
    }

    // =============================================================== ReportsViewModel: each report opens + builds

    [Fact]
    public void NegativeStock_opens_as_an_accounting_report_and_lists_the_negative_item()
    {
        var c = SeedInventory(out _);
        var vm = new ReportsViewModel(c, ReportKind.NegativeStock);

        Assert.Equal(ReportKind.NegativeStock, vm.Kind);
        Assert.Equal("Negative Stock", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.True(vm.ShowSingleAccountingGrid);
        Assert.False(vm.IsInventoryReport);
        Assert.False(vm.IsGstReport);

        // Widget (−5) lists; Gadget (+10) does not. The value renders in the Amount cell, godown+qty in Secondary.
        var row = vm.Rows.Single(r => r.Particulars == "Widget");
        Assert.Contains("Main Location", row.Secondary);
        Assert.Contains("-5", row.Secondary);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(-500m)), row.Amount);
        Assert.DoesNotContain(vm.Rows, r => r.Particulars == "Gadget");
    }

    [Fact]
    public void NegativeStock_shows_empty_state_when_no_item_is_negative()
    {
        var c = CompanyFactory.CreateSeeded("Clean Inv Co", Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Gadget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(50m));

        var vm = new ReportsViewModel(c, ReportKind.NegativeStock);

        Assert.NotEmpty(vm.Rows);
        Assert.Contains(vm.Rows, r => r.IsHeader && r.Particulars.StartsWith("No negative stock"));
    }

    [Fact]
    public void NegativeCashBank_lists_an_overdrawn_bank_as_a_credit_balance()
    {
        var c = SeedOverdrawnBank(out var bank);
        var vm = new ReportsViewModel(c, ReportKind.NegativeCashBank);

        Assert.Equal("Negative Cash / Bank", vm.Title);
        Assert.True(vm.IsAccountingReport);

        var row = vm.Rows.Single(r => r.Particulars == bank.Name);
        // A negative (overdrawn) bank renders its magnitude with a "Cr" suffix.
        Assert.Equal($"{IndianFormat.Amount(Money.FromRupees(30000m))} Cr", row.Amount);
        Assert.DoesNotContain(vm.Rows, r => r.Particulars == "Cash");
    }

    [Fact]
    public void NegativeCashBank_shows_empty_state_when_nothing_is_negative()
    {
        var c = CompanyFactory.CreateSeeded("Clean Cash Co", Start);
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var vm = new ReportsViewModel(c, ReportKind.NegativeCashBank);

        Assert.Contains(vm.Rows, r => r.IsHeader && r.Particulars.StartsWith("No negative cash / bank"));
    }

    [Fact]
    public void MemorandumRegister_lists_a_memo_voucher_with_total_and_omits_a_normal_journal()
    {
        var c = SeedMemoAndReversing(out var rent, out _, out var cash);
        var svc = new LedgerService(c);

        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        var memo = new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        }, number: 7, narration: "Petty cash reminder");
        svc.Post(memo);

        // A normal journal that must NOT list in the memo register.
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2024, 4, 15),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(1000m), DrCr.Credit),
            }));

        var vm = new ReportsViewModel(c, ReportKind.MemorandumRegister);

        Assert.Equal("Memorandum Register", vm.Title);
        Assert.True(vm.IsAccountingReport);

        var row = vm.Rows.Single(r => r.Particulars.Contains("Memo No. 7"));
        Assert.Contains("12-Apr-2024", row.Particulars);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(2500m)), row.Amount);
        // No journal row.
        Assert.DoesNotContain(vm.Rows, r => r.Amount == IndianFormat.Amount(Money.FromRupees(1000m)));
        // Footer Total.
        var total = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Total");
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(2500m)), total.Amount);
    }

    [Fact]
    public void MemorandumRegister_honours_the_slice1_period_window()
    {
        var c = SeedMemoAndReversing(out var rent, out _, out var cash);
        var svc = new LedgerService(c);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;

        svc.Post(new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 5, 20), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(9000m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var vm = new ReportsViewModel(c, ReportKind.MemorandumRegister);
        vm.SetPeriod(Start, AsOf); // April only

        // Only the April memo survives; the footer total is that memo's amount.
        var total = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Total");
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(2500m)), total.Amount);
        Assert.DoesNotContain(vm.Rows, r => r.Amount == IndianFormat.Amount(Money.FromRupees(9000m)));
    }

    [Fact]
    public void ReversingJournalRegister_lists_a_reversing_journal_with_applicable_date()
    {
        var c = SeedMemoAndReversing(out var rent, out var provision, out _);
        var svc = new LedgerService(c);

        var revType = c.FindVoucherTypeByName("Reversing Journal")!;
        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, number: 3, applicableUpto: new DateOnly(2024, 4, 30)));

        // A normal journal that must NOT list in the reversing register.
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2024, 4, 15),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(provision.Id, Money.FromRupees(1000m), DrCr.Credit),
            }));

        var vm = new ReportsViewModel(c, ReportKind.ReversingJournalRegister);

        Assert.Equal("Reversing Journal Register", vm.Title);
        Assert.True(vm.IsAccountingReport);

        var row = vm.Rows.Single(r => r.Particulars.Contains("Rev. Jrnl No. 3"));
        Assert.Contains("10-Apr-2024", row.Particulars);
        Assert.Contains("Applicable upto 30-Apr-2024", row.Secondary);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(3000m)), row.Amount);
        Assert.DoesNotContain(vm.Rows, r => r.Amount == IndianFormat.Amount(Money.FromRupees(1000m)));

        var total = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Total");
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(3000m)), total.Amount);
    }

    [Fact]
    public void ReversingJournalRegister_honours_the_slice1_period_window()
    {
        var c = SeedMemoAndReversing(out var rent, out var provision, out _);
        var svc = new LedgerService(c);
        var revType = c.FindVoucherTypeByName("Reversing Journal")!;

        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 4, 30)));
        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 5, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(7000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(7000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 5, 31)));

        var vm = new ReportsViewModel(c, ReportKind.ReversingJournalRegister);
        vm.SetPeriod(Start, AsOf); // April only

        var total = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Total");
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(3000m)), total.Amount);
        Assert.DoesNotContain(vm.Rows, r => r.Amount == IndianFormat.Amount(Money.FromRupees(7000m)));
    }

    // =============================================================== shell nav wiring (Reports → Exception Reports)

    [Fact]
    public void ExceptionReports_menu_lists_the_four_reports_under_one_section()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        vm.ShowExceptionReportsMenu();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.ExceptionReports, vm.CurrentGatewayMenu);

        // One "Exception Reports" section header, four report items — never a flat dump.
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Exception Reports" }, headers);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[] { "Negative Stock", "Negative Cash / Bank", "Memorandum Register", "Reversing Journal Register" },
            items);
    }

    [Theory]
    [InlineData("Negative Stock", ReportKind.NegativeStock)]
    [InlineData("Negative Cash / Bank", ReportKind.NegativeCashBank)]
    [InlineData("Memorandum Register", ReportKind.MemorandumRegister)]
    [InlineData("Reversing Journal Register", ReportKind.ReversingJournalRegister)]
    public void Activating_an_exception_item_opens_that_report_proving_labels_match_routing(
        string label, ReportKind expected)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowExceptionReportsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
