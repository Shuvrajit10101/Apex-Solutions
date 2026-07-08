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
/// End-to-end coverage for the Scenarios / Reversing / Memoranda / Optional UI surfaced in the cascade
/// (catalog §7): the voucher header's Optional toggle (Ctrl+L) posts a provisional voucher that stays out
/// of the actual Trial Balance but appears under a scenario that includes its type; the Scenario master
/// creates + persists a scenario (name / Include Actuals / included types) that survives a reload; a
/// Memorandum voucher is non-affecting until converted to a real voucher via the engine; a Reversing
/// Journal captures its Applicable-Upto date; and every path keeps the cascade correct (Other Vouchers
/// nests under Vouchers, Scenario under Masters → Create, and pages open as ONE replacing page column).
/// Drives the real shell VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class ScenarioViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ScenarioViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexScenarioTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Creates a "Rent" expense ledger under Indirect Expenses (opening zero).</summary>
    private static DomainLedger AddRent(Company c)
    {
        var rent = new DomainLedger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);
        return rent;
    }

    /// <summary>Enters a voucher of the given base type with a single Dr/Cr pair and returns the entry VM.</summary>
    private static void FillPair(VoucherEntryViewModel entry, DomainLedger dr, DomainLedger cr, decimal amt)
    {
        entry.Lines[0].SelectedLedger = dr;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = amt.ToString();
        entry.Lines[1].SelectedLedger = cr;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = amt.ToString();
    }

    // ---------------------------------------------------------------- (1) Optional voucher

    [Fact]
    public void Optional_voucher_drops_from_actual_TB_but_appears_under_an_including_scenario()
    {
        const string companyName = "Optional Co";
        var vm = NewSeededCompany(companyName);
        var rent = AddRent(vm.Company!);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;
        var capital = new DomainLedger(Guid.NewGuid(), "Capital",
            vm.Company!.FindGroupByName("Capital Account")!.Id, Money.FromRupees(100000m), openingIsDebit: false);
        vm.Company!.AddLedger(capital);

        // Enter a Journal, toggle it Optional (Ctrl+L), and accept.
        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;
        FillPair(entry, rent, cash, 5000m);
        vm.ToggleOptional();
        Assert.True(entry.IsOptional);
        Assert.True(entry.Accept());

        var optional = vm.Company!.Vouchers.Single();
        Assert.True(optional.Optional);

        // Create a scenario including the Journal type.
        var journalType = vm.Company!.FindVoucherTypeByName("Journal")!;
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalType.Id });
        vm.Company!.AddScenario(scenario);

        var asOf = new DateOnly(vm.Company!.FinancialYearStart.Year + 1, 3, 31);

        // Actual Trial Balance: the Optional Rent movement is absent.
        var actualTb = TrialBalance.Build(vm.Company!, asOf);
        Assert.DoesNotContain(actualTb.Rows, r => r.LedgerName == "Rent");

        // Under the scenario it surfaces at 5,000 Dr.
        Assert.Equal(5000m, LedgerBalances.SignedClosing(vm.Company!, rent, asOf, scenario));

        // The report VM's scenario picker offers Actual + the scenario, and switching updates the rows.
        vm.OpenReport(ReportKind.TrialBalance);
        var report = vm.Reports!;
        Assert.True(report.SupportsScenario);
        Assert.Equal(2, report.Scenarios.Count);              // Actual + Provisional
        Assert.DoesNotContain(report.Rows, r => r.Particulars == "Rent"); // actual: no Rent
        report.SelectedScenario = report.Scenarios.Single(o => o.Scenario?.Name == "Provisional");
        Assert.Contains(report.Rows, r => r.Particulars == "Rent"); // scenario: Rent shows
    }

    // ---------------------------------------------------------------- (2) Scenario master + reload

    [Fact]
    public void Scenario_master_creates_and_persists_a_scenario_that_survives_reload()
    {
        const string companyName = "Scenario Master Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowScenarioMaster();
        Assert.Equal(Screen.ScenarioMaster, vm.CurrentScreen);
        var master = vm.ScenarioMaster!;

        master.Name = "What-If";
        master.IncludeActuals = false;
        // Tick exactly the Memorandum type (untick everything else first).
        foreach (var row in master.Types) row.Include = false;
        var memoRow = master.Types.Single(r => r.Name == "Memorandum");
        memoRow.Include = true;

        Assert.True(master.Create());
        var listed = Assert.Single(master.Existing, r => r.Name == "What-If");
        Assert.Equal("No", listed.Actuals);
        Assert.Contains("Memorandum", listed.Includes);

        // Persisted: the scenario (name / include-actuals / included types) survives a reload.
        var reloaded = Reload(companyName);
        var scenario = reloaded.FindScenarioByName("What-If")!;
        Assert.NotNull(scenario);
        Assert.False(scenario.IncludeActuals);
        var memoType = reloaded.FindVoucherTypeByName("Memorandum")!;
        Assert.True(scenario.Includes(memoType.Id));
        Assert.Single(scenario.IncludedTypeIds);
    }

    [Fact]
    public void Scenario_master_rejects_a_blank_name_and_a_duplicate()
    {
        var vm = NewSeededCompany("Scenario Dup Co");
        vm.ShowScenarioMaster();
        var master = vm.ScenarioMaster!;

        master.Name = "   ";
        Assert.False(master.Create());
        Assert.Contains("name is required", master.Message);

        master.Name = "Only One";
        Assert.True(master.Create());
        master.Name = "Only One";
        Assert.False(master.Create());
        Assert.Contains("already exists", master.Message);
    }

    // ---------------------------------------------------------------- (3) Memorandum + convert

    [Fact]
    public void Memorandum_voucher_is_non_affecting_until_converted_to_a_regular_voucher()
    {
        const string companyName = "Memo Co";
        var vm = NewSeededCompany(companyName);
        var rent = AddRent(vm.Company!);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(50000m);
        cash.OpeningIsDebit = true;
        var capital = new DomainLedger(Guid.NewGuid(), "Capital",
            vm.Company!.FindGroupByName("Capital Account")!.Id, Money.FromRupees(50000m), openingIsDebit: false);
        vm.Company!.AddLedger(capital);

        // A Memorandum entry is a provisional type: the Optional toggle is hidden/no-op.
        vm.OpenVoucher(VoucherBaseType.Memorandum);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.IsProvisionalType);
        vm.ToggleOptional();
        Assert.False(entry.IsOptional); // provisional types ignore the Optional toggle
        FillPair(entry, rent, cash, 2500m);
        Assert.True(entry.Accept());

        var memo = vm.Company!.Vouchers.Single();
        var memoType = vm.Company!.FindVoucherType(memo.TypeId)!;
        Assert.Equal(VoucherBaseType.Memorandum, memoType.BaseType);

        var asOf = new DateOnly(vm.Company!.FinancialYearStart.Year + 1, 3, 31);
        // A memo does not touch the real books.
        Assert.Equal(0m, LedgerBalances.SignedClosing(vm.Company!, rent, asOf));

        // Convert it to a real Journal → the memo is gone and Rent now shows Dr 2,500 in the actual books.
        var regular = vm.ConvertMemorandum(memo.Id, VoucherBaseType.Journal);
        Assert.NotNull(regular);
        Assert.Null(vm.Company!.FindVoucher(memo.Id));
        Assert.False(regular!.Optional);
        Assert.Equal(2500m, LedgerBalances.SignedClosing(vm.Company!, rent, asOf));

        // Persisted: the converted (real) voucher survives a reload; no memo remains.
        var reloaded = Reload(companyName);
        var reloadedRent = reloaded.FindLedgerByName("Rent")!;
        Assert.Equal(2500m, LedgerBalances.SignedClosing(reloaded, reloadedRent, asOf));
        Assert.DoesNotContain(reloaded.Vouchers, v =>
            reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Memorandum);
    }

    [Fact]
    public void ConvertMemorandum_rejects_a_non_memorandum_voucher()
    {
        var vm = NewSeededCompany("Memo Reject Co");
        var rent = AddRent(vm.Company!);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(50000m);
        cash.OpeningIsDebit = true;
        var capital = new DomainLedger(Guid.NewGuid(), "Capital",
            vm.Company!.FindGroupByName("Capital Account")!.Id, Money.FromRupees(50000m), openingIsDebit: false);
        vm.Company!.AddLedger(capital);

        vm.OpenVoucher(VoucherBaseType.Journal);
        FillPair(vm.VoucherEntry!, rent, cash, 1000m);
        Assert.True(vm.VoucherEntry!.Accept());
        var journal = vm.Company!.Vouchers.Single();

        Assert.Null(vm.ConvertMemorandum(journal.Id));
        Assert.Contains("not a Memorandum", vm.Message);
    }

    // ---------------------------------------------------------------- (4) Reversing Journal + applicable-upto

    [Fact]
    public void Reversing_journal_captures_applicable_upto_and_counts_only_within_it_under_a_scenario()
    {
        var vm = NewSeededCompany("Reversing Co");
        var rent = AddRent(vm.Company!);
        var provision = new DomainLedger(Guid.NewGuid(), "Provision for Expenses",
            vm.Company!.FindGroupByName("Provisions")!.Id, Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(provision);

        // Use dates within the seeded company's financial year (books-begin = 1-Apr-<FY>).
        var fyStart = vm.Company!.FinancialYearStart;           // 1-Apr-<year>
        var voucherDate = fyStart.AddDays(9);                   // 10-Apr
        var upto = fyStart.AddDays(29);                         // 30-Apr
        var afterUpto = fyStart.AddMonths(1);                   // 1-May

        vm.OpenVoucher(VoucherBaseType.ReversingJournal);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.IsReversing);
        entry.Date = voucherDate;
        entry.ApplicableUptoText = upto.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        FillPair(entry, rent, provision, 3000m);
        Assert.True(entry.Accept());

        var rev = vm.Company!.Vouchers.Single();
        Assert.Equal(upto, rev.ApplicableUpto);

        var revType = vm.Company!.FindVoucherTypeByName("Reversing Journal")!;
        var scenario = new Scenario(Guid.NewGuid(), "With Reversing", includeActuals: true,
            includedTypeIds: new[] { revType.Id });
        vm.Company!.AddScenario(scenario);

        // Within the window it is in force; after it, it has reversed out; never in the actual books.
        Assert.Equal(3000m, LedgerBalances.SignedClosing(vm.Company!, rent, upto, scenario));
        Assert.Equal(0m, LedgerBalances.SignedClosing(vm.Company!, rent, afterUpto, scenario));
        Assert.Equal(0m, LedgerBalances.SignedClosing(vm.Company!, rent, upto));
    }

    [Fact]
    public void Reversing_journal_rejects_an_applicable_upto_before_the_voucher_date()
    {
        var vm = NewSeededCompany("Reversing Bad Co");
        var rent = AddRent(vm.Company!);
        var provision = new DomainLedger(Guid.NewGuid(), "Provision for Expenses",
            vm.Company!.FindGroupByName("Provisions")!.Id, Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(provision);

        var fyStart = vm.Company!.FinancialYearStart;
        vm.OpenVoucher(VoucherBaseType.ReversingJournal);
        var entry = vm.VoucherEntry!;
        entry.Date = fyStart.AddDays(9);   // 10-Apr
        // Applicable-Upto BEFORE the voucher date (5-Apr) — invalid.
        entry.ApplicableUptoText = fyStart.AddDays(4)
            .ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        FillPair(entry, rent, provision, 3000m);

        Assert.False(entry.Accept());
        Assert.Contains("Applicable Upto", entry.Message);
        Assert.Empty(vm.Company!.Vouchers);
    }

    // ---------------------------------------------------------------- (5) cascade correctness

    [Fact]
    public void Other_vouchers_nests_under_vouchers_and_scenario_under_create()
    {
        var vm = NewSeededCompany("Scenario Nav Co");

        // Transactions → Vouchers → Other Vouchers is a Group with Reversing Journal + Memorandum + POS Billing
        // (Phase 6 slice 7 RQ-38: the POS Billing entry is always present — clicking it auto-creates the POS-flagged
        // Sales type on first use, so there is no chicken-and-egg gate).
        vm.ShowOtherVouchersMenu();
        Assert.Equal(GatewayMenu.OtherVouchers, vm.CurrentGatewayMenu);
        var submenu = vm.Columns[^1];
        Assert.True(submenu.IsMenu);
        var labels = submenu.Items.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Reversing Journal", "Memorandum", "POS Billing" }, labels);

        // The Vouchers submenu (one column to the left) lists an "Other Vouchers" group item.
        var vouchers = vm.Columns[^2];
        Assert.Contains(vouchers.Items, m => m.IsSelectable && m.Label == "Other Vouchers" && m.IsGroup);

        // Drilling into Reversing Journal opens exactly ONE page column (a voucher-entry page).
        vm.DrillIn();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.NotNull(vm.VoucherEntry);
        Assert.True(vm.VoucherEntry!.IsReversing);

        // Masters → Create lists a Scenario page item; opening it REPLACES the page column.
        vm.ShowCreateMenu();
        var create = vm.Columns[^1];
        Assert.Contains(create.Items, m => m.IsSelectable && m.Label == "Scenario" && m.IsPage);
        vm.ShowScenarioMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.NotNull(vm.ScenarioMaster);
        Assert.Same(vm.ScenarioMaster, vm.Columns[^1].ScenarioMaster);

        // Opening a report REPLACES the Scenario page — still exactly one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.ScenarioMaster);
        Assert.NotNull(vm.Reports);

        // The root Gateway nests Vouchers under TRANSACTIONS (professional hierarchy).
        vm.ShowGateway();
        var root = vm.Columns[0];
        var items = root.Items.ToList();
        var txHeaderIdx = items.FindIndex(i => i.IsHeader && i.Label == "Transactions");
        var vouchersIdx = items.FindIndex(i => i.IsSelectable && i.Label == "Vouchers");
        Assert.True(txHeaderIdx >= 0 && vouchersIdx > txHeaderIdx);
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
