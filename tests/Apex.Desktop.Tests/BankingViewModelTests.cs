using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Banking UI surfaced in the cascade (catalog §8): a bank-ledger voucher line
/// captures a Bank Allocation (transaction type / instrument / instrument date) and posts through the engine
/// + SQLite; the Bank Reconciliation page lists the transaction and setting a Bank Date reconciles it so the
/// Balance-as-per-Books vs -Bank pair updates; the statement-import page auto-matches a matching CSV row and
/// applies its bank date; a Ctrl+T post-dated voucher is excluded from current balances until its date; and
/// every path keeps the cascade correct (Banking nests under Transactions; the two pages open as ONE page
/// column that REPLACES any prior page). Drives the real shell VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class BankingViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BankingViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBankingTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>Creates a bank ledger "HDFC Bank" under Bank Accounts, with an opening 5,00,000 Dr.</summary>
    private DomainLedger CreateBankLedger(MainWindowViewModel vm, string name = "HDFC Bank", decimal opening = 500000m)
    {
        var bank = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(opening), openingIsDebit: true);
        vm.Company!.AddLedger(bank);
        return bank;
    }

    /// <summary>Creates a "Rent" expense ledger under Indirect Expenses.</summary>
    private DomainLedger CreateExpenseLedger(MainWindowViewModel vm, string name = "Rent")
    {
        var rent = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        vm.Company!.AddLedger(rent);
        return rent;
    }

    /// <summary>
    /// Enters a Payment voucher paying rent by cheque out of the bank, capturing the Bank Allocation on the
    /// bank line, and accepts it. Returns the entry VM (already accepted).
    /// </summary>
    private void PostBankPayment(
        MainWindowViewModel vm, DomainLedger rent, DomainLedger bank,
        decimal amount, string instrument, DateOnly date, bool postDated = false)
    {
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;
        entry.Date = date;

        // Line 0 = Dr rent ; line 1 = Cr bank (with a bank allocation).
        var l0 = entry.Lines[0];
        l0.SelectedLedger = rent;
        l0.Side = DrCr.Debit;
        l0.AmountText = amount.ToString();
        Assert.False(l0.IsBankLine); // an expense ledger is not a bank line

        var l1 = entry.Lines[1];
        l1.SelectedLedger = bank;
        l1.Side = DrCr.Credit;
        l1.AmountText = amount.ToString();

        // Selecting the bank ledger turns the Bank Allocation sub-panel on.
        Assert.True(l1.IsBankLine);
        l1.BankTransactionType = BankTransactionType.ChequeOrDD;
        l1.InstrumentNumber = instrument;
        l1.InstrumentDateText = date.ToString("dd-MMM-yyyy");

        if (postDated) vm.TogglePostDated();

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ---------------------------------------------------------------- (1) bank-line capture posts

    [Fact]
    public void Bank_line_voucher_captures_a_bank_allocation_and_posts_and_persists()
    {
        const string companyName = "Bank Post Co";
        var vm = NewSeededCompany(companyName);
        var bank = CreateBankLedger(vm);
        var rent = CreateExpenseLedger(vm);

        PostBankPayment(vm, rent, bank, 20000m, "100123", new DateOnly(2026, 4, 10));

        // Posted in memory: the bank line carries a cheque/DD allocation, unreconciled.
        var paymentType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == paymentType.Id);
        var bankLine = posted.Lines.Single(l => l.LedgerId == bank.Id);
        Assert.True(bankLine.HasBankAllocation);
        Assert.Equal(BankTransactionType.ChequeOrDD, bankLine.BankAllocation!.TransactionType);
        Assert.Equal("100123", bankLine.BankAllocation.InstrumentNumber);
        Assert.Equal(new DateOnly(2026, 4, 10), bankLine.BankAllocation.InstrumentDate);
        Assert.False(bankLine.BankAllocation.IsReconciled);

        // PERSISTED: the allocation survives a reload (schema v5 bank_allocations).
        var reloaded = Reload(companyName);
        var reloadedBank = reloaded.FindLedgerByName("HDFC Bank")!;
        var reloadedType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment);
        var reloadedVoucher = reloaded.Vouchers.Single(v => v.TypeId == reloadedType.Id);
        var reloadedLine = reloadedVoucher.Lines.Single(l => l.LedgerId == reloadedBank.Id);
        Assert.True(reloadedLine.HasBankAllocation);
        Assert.Equal("100123", reloadedLine.BankAllocation!.InstrumentNumber);
    }

    // ---------------------------------------------------------------- (2) BRS lists + reconcile

    [Fact]
    public void Brs_lists_the_transaction_and_setting_a_bank_date_reconciles_it()
    {
        const string companyName = "Bank BRS Co";
        var vm = NewSeededCompany(companyName);
        var bank = CreateBankLedger(vm);
        var rent = CreateExpenseLedger(vm);

        // An uncleared 40,000 cheque out of the bank.
        PostBankPayment(vm, rent, bank, 40000m, "100200", new DateOnly(2026, 4, 5));

        vm.OpenBankReconciliation();
        Assert.Equal(Screen.BankReconciliation, vm.CurrentScreen);
        var brs = vm.BankReconciliation!;
        Assert.Same(bank, brs.SelectedBank);

        // The transaction is listed; books already reflect it, the bank does not yet.
        var row = Assert.Single(brs.Rows);
        Assert.Equal("100200", row.Instrument);
        Assert.Equal(string.Empty, row.BankDateText);              // unreconciled
        Assert.Contains("4,60,000", brs.BalanceAsPerBooksText);     // 5,00,000 − 40,000
        Assert.Contains("5,00,000", brs.BalanceAsPerBankText);      // bank still shows opening
        Assert.Contains("40,000", brs.AmountNotReflectedText);

        // Type a Bank Date on the row (on/before the as-of date so it clears within the window) and
        // reconcile → book-vs-bank now agree, nothing outstanding.
        row.BankDateText = "05-Apr-2026";
        var changed = brs.Reconcile();
        Assert.Equal(1, changed);
        Assert.Contains("4,60,000", brs.BalanceAsPerBankText);      // bank now matches books
        Assert.Contains("0.00", brs.AmountNotReflectedText);

        // Persisted: the Bank Date survives a reload.
        var reloaded = Reload(companyName);
        var reloadedBank = reloaded.FindLedgerByName("HDFC Bank")!;
        var report = BankReconciliation.Build(reloaded, reloadedBank, brs.AsOf);
        Assert.Empty(report.Unreconciled);
        Assert.Equal(new DateOnly(2026, 4, 5), report.Transactions.Single().BankDate);
    }

    // ---------------------------------------------------------------- (3) statement import auto-match

    [Fact]
    public void Statement_import_auto_match_reconciles_a_matching_row_and_applies_the_bank_date()
    {
        const string companyName = "Bank Import Co";
        var vm = NewSeededCompany(companyName);
        var bank = CreateBankLedger(vm);
        var rent = CreateExpenseLedger(vm);

        // A 40,000 cheque out (matches the statement) and a 7,000 cheque out (not in the statement).
        PostBankPayment(vm, rent, bank, 40000m, "100200", new DateOnly(2026, 4, 5));
        PostBankPayment(vm, rent, bank, 7000m, "100201", new DateOnly(2026, 4, 9));

        vm.OpenBankStatementImport();
        Assert.Equal(Screen.BankStatementImport, vm.CurrentScreen);
        var import = vm.BankStatementImport!;
        Assert.Same(bank, import.SelectedBank);

        // Statement: the 40,000 cheque (matches by amount+instrument) + an unrelated charge row.
        var csv = string.Join("\n",
            "Date,Description,Amount,Instrument",
            "2026-04-06,Cheque 100200,-40000,100200",
            "2026-04-08,Bank charge,-150,");

        var matched = import.ImportFromText(csv);
        Assert.Equal(1, matched);

        // One matched result, one unmatched statement row (charge), one unmatched book (the 7,000 cheque).
        Assert.Equal(1, import.Results.Count(r => r.Kind == StatementRowKind.Matched));
        Assert.Equal(1, import.Results.Count(r => r.Kind == StatementRowKind.UnmatchedStatement));
        Assert.Equal(1, import.Results.Count(r => r.Kind == StatementRowKind.UnmatchedBook));

        // The matched book line now carries the statement's Bank Date (2026-04-06), persisted.
        var reloaded = Reload(companyName);
        var reloadedBank = reloaded.FindLedgerByName("HDFC Bank")!;
        var report = BankReconciliation.Build(reloaded, reloadedBank, import.AsOf);
        var reconciled = report.Transactions.Single(t => t.InstrumentNumber == "100200");
        Assert.Equal(new DateOnly(2026, 4, 6), reconciled.BankDate);
        // The 7,000 cheque is still outstanding.
        Assert.Single(report.Unreconciled);
        Assert.Equal("100201", report.Unreconciled.Single().InstrumentNumber);
    }

    // ---------------------------------------------------------------- (4) post-dated excluded

    [Fact]
    public void Post_dated_voucher_is_excluded_from_current_balances()
    {
        var vm = NewSeededCompany("Bank PostDated Co");
        var bank = CreateBankLedger(vm, opening: 500000m);
        var rent = CreateExpenseLedger(vm);

        // A post-dated cheque dated in the future relative to the other (current) activity.
        PostBankPayment(vm, rent, bank, 30000m, "200055", new DateOnly(2026, 5, 1), postDated: true);

        var pd = vm.Company!.Vouchers.Single();
        Assert.True(pd.PostDated);

        // Before the cheque date the bank is unaffected — still the opening 5,00,000 Dr.
        var before = LedgerBalances.Closing(vm.Company!, bank, new DateOnly(2026, 4, 20));
        Assert.Equal(DrCr.Debit, before.Side);
        Assert.Equal(Money.FromRupees(500000m), before.Amount);
        Assert.False(LedgerBalances.CountsAsOf(pd, new DateOnly(2026, 4, 20)));

        // On/after its date it takes effect: 5,00,000 − 30,000 = 4,70,000 Dr.
        var onDate = LedgerBalances.Closing(vm.Company!, bank, new DateOnly(2026, 5, 1));
        Assert.Equal(Money.FromRupees(470000m), onDate.Amount);
        Assert.True(LedgerBalances.CountsAsOf(pd, new DateOnly(2026, 5, 1)));
    }

    // ---------------------------------------------------------------- (5) cascade correctness

    [Fact]
    public void Banking_nests_under_transactions_and_pages_open_as_a_single_replacing_column()
    {
        var vm = NewSeededCompany("Bank Nav Co");
        var bank = CreateBankLedger(vm);
        var rent = CreateExpenseLedger(vm);
        PostBankPayment(vm, rent, bank, 10000m, "100300", new DateOnly(2026, 4, 3));

        // Transactions → Banking is a Group item that opens a submenu column with the two pages.
        vm.ShowBankingMenu();
        Assert.Equal(GatewayMenu.Banking, vm.CurrentGatewayMenu);
        var submenu = vm.Columns[^1];
        Assert.True(submenu.IsMenu);
        var labels = submenu.Items.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Bank Reconciliation", "Import Bank Statement" }, labels);

        // Drilling into the first item (Bank Reconciliation) adds exactly ONE page column.
        vm.DrillIn();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.BankReconciliation);
        Assert.Same(vm.BankReconciliation, vm.Columns[^1].BankReconciliation);

        // Opening the Import page REPLACES the BRS page — still exactly one page column.
        vm.OpenBankStatementImport();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.BankReconciliation);
        Assert.NotNull(vm.BankStatementImport);

        // Opening a report REPLACES the Banking page — still exactly one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.BankStatementImport);
        Assert.NotNull(vm.Reports);

        // The root Gateway lists Banking under the TRANSACTIONS section (professional hierarchy).
        vm.ShowGateway();
        var root = vm.Columns[0];
        var items = root.Items.ToList();
        var txHeaderIdx = items.FindIndex(i => i.IsHeader && i.Label == "Transactions");
        var bankingIdx = items.FindIndex(i => i.IsSelectable && i.Label == "Banking");
        var reportsHeaderIdx = items.FindIndex(i => i.IsHeader && i.Label == "Reports");
        Assert.True(txHeaderIdx >= 0 && bankingIdx > txHeaderIdx && bankingIdx < reportsHeaderIdx);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
