using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// Drives the new voucher-entry + ledger-master view models end-to-end against a real seeded
/// company on a throwaway <c>.db</c>. Proves the Phase-1 UI actually posts and persists through the
/// same engine + SQLite path the reports read from: create a Capital ledger, enter a Receipt
/// (Dr Cash 100000 / Cr Capital 100000), accept, then assert it POSTED, PERSISTED (reload the .db),
/// shows in the Day Book, and moved the Trial Balance (which still balances). A second test proves
/// an unbalanced voucher is rejected — accept blocked, error surfaced, nothing persisted.
/// </summary>
public sealed class VoucherEntryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany(); // seeds 28 groups / 2 ledgers / 24 voucher types and persists
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static DateOnly AsOf(Company c)
    {
        DateOnly? last = null;
        foreach (var v in c.Vouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        return last ?? c.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    // ---------------------------------------------------------------- (a) happy path

    [Fact]
    public void CreateLedger_then_Receipt_posts_persists_and_moves_reports()
    {
        const string companyName = "Voucher Co";
        var vm = NewSeededCompany(companyName);

        // ---- 1. Create a "Capital" ledger through the ledger-master screen ----
        vm.ShowLedgerMaster();
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        var master = vm.LedgerMaster!;
        master.Name = "Capital A/c";
        master.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        Assert.True(master.Create());

        // It exists on the in-memory company now.
        var capital = vm.Company!.FindLedgerByName("Capital A/c");
        Assert.NotNull(capital);

        // ---- 2. Enter a Receipt: Dr Cash 100000 / Cr Capital 100000 ----
        vm.OpenVoucher(VoucherBaseType.Receipt);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        var entry = vm.VoucherEntry!;
        Assert.Equal("Receipt", entry.TypeName);

        var cash = vm.Company!.FindLedgerByName("Cash");
        Assert.NotNull(cash);

        // Two starter lines are present; fill them (line 0 = Dr Cash, line 1 = Cr Capital).
        var line0 = entry.Lines[0];
        line0.SelectedLedger = cash;
        line0.Side = DrCr.Debit;
        line0.AmountText = "100000";

        var line1 = entry.Lines[1];
        line1.SelectedLedger = capital;
        line1.Side = DrCr.Credit;
        line1.AmountText = "100000";

        // Live balance indicator reflects a balanced voucher and enables accept.
        Assert.True(entry.IsBalanced);
        Assert.True(entry.CanAccept);
        Assert.Equal("Balanced", entry.DifferenceText);

        // ---- 3. Accept (Ctrl+A) ----
        Assert.True(entry.Accept());
        // The engine assigned the automatic Receipt number (1 for the first Receipt).
        Assert.True(entry.SavedNumber >= 1);
        // Accepting returns to the Gateway.
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        // ---- 4. POSTED in memory ----
        var receiptType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        var postedInMemory = vm.Company!.Vouchers.Where(v => v.TypeId == receiptType.Id).ToList();
        Assert.Single(postedInMemory);
        Assert.Equal(100000m, postedInMemory[0].TotalDebit.Amount);
        Assert.Equal(100000m, postedInMemory[0].TotalCredit.Amount);

        // ---- 5. PERSISTED: reload the .db and the voucher is there ----
        var reloaded = Reload(companyName);
        var reloadedType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        var reloadedVouchers = reloaded.Vouchers.Where(v => v.TypeId == reloadedType.Id).ToList();
        Assert.Single(reloadedVouchers);
        // The Capital ledger persisted too.
        Assert.NotNull(reloaded.FindLedgerByName("Capital A/c"));

        // ---- 6. Appears in the Day Book ----
        var asOf = AsOf(reloaded);
        var dayBook = DayBook.Build(reloaded, reloaded.BooksBeginFrom, asOf);
        Assert.Contains(dayBook, r => r.VoucherTypeName == "Receipt");

        // ---- 7. Moved the Trial Balance, which still balances ----
        var tb = TrialBalance.Build(reloaded, asOf);
        Assert.True(tb.Balanced);
        Assert.Equal(tb.TotalDebit.Amount, tb.TotalCredit.Amount);
        // Cash now carries a 100000 debit balance in the TB.
        Assert.Contains(tb.Rows, r =>
            r.LedgerName == "Cash" && r.Debit.Amount == 100000m);
        Assert.Contains(tb.Rows, r =>
            r.LedgerName == "Capital A/c" && r.Credit.Amount == 100000m);
    }

    // ---------------------------------------------------------------- (b) unbalanced rejected

    [Fact]
    public void Unbalanced_voucher_is_rejected_accept_blocked_and_nothing_persisted()
    {
        const string companyName = "Reject Co";
        var vm = NewSeededCompany(companyName);

        // Need a second ledger to post against; reuse the seeded Cash + a fresh Sales ledger.
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = "Sales A/c";
        master.SelectedGroup = vm.Company!.FindGroupByName("Sales Accounts");
        Assert.True(master.Create());

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var entry = vm.VoucherEntry!;

        var cash = vm.Company!.FindLedgerByName("Cash");
        var sales = vm.Company!.FindLedgerByName("Sales A/c");

        // Deliberately unbalanced: Dr Cash 100000 / Cr Sales 90000.
        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "100000";

        entry.Lines[1].SelectedLedger = sales;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "90000";

        // The live indicator flags the imbalance and blocks accept.
        Assert.False(entry.IsBalanced);
        Assert.False(entry.CanAccept);
        Assert.Contains("10,000", entry.DifferenceText);

        // Accept returns false and surfaces an error; the screen does NOT advance to the Gateway.
        Assert.False(entry.Accept());
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));

        // Nothing posted in memory.
        var receiptType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        Assert.DoesNotContain(vm.Company!.Vouchers, v => v.TypeId == receiptType.Id);

        // Nothing persisted: reload the .db and there are no Receipt vouchers.
        var reloaded = Reload(companyName);
        var reloadedType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        Assert.DoesNotContain(reloaded.Vouchers, v => v.TypeId == reloadedType.Id);
    }

    // ---------------------------------------------------------------- (c) each accounting type opens

    [Theory]
    [InlineData(VoucherBaseType.Contra)]
    [InlineData(VoucherBaseType.Payment)]
    [InlineData(VoucherBaseType.Receipt)]
    [InlineData(VoucherBaseType.Journal)]
    [InlineData(VoucherBaseType.Sales)]
    [InlineData(VoucherBaseType.Purchase)]
    public void Each_accounting_voucher_type_opens_its_entry_screen(VoucherBaseType baseType)
    {
        var vm = NewSeededCompany($"Open {baseType}");
        vm.OpenVoucher(baseType);

        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherEntry);
        Assert.Equal(baseType, vm.VoucherEntry!.Type.BaseType);
        // Opens with two starter Dr/Cr lines and a live (unbalanced/nil) indicator.
        Assert.Equal(2, vm.VoucherEntry!.Lines.Count);
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
