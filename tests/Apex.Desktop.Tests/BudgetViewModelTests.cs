using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Budgets UI surfaced in the cascade (catalog §7): a budget + its lines are
/// created through the Budget master (Masters → Create → Budget) and persist; vouchers posted through the
/// engine drive the actuals; the Budget Variance report (Reports → Statements of Accounts → Budgets)
/// shows the correct Budget / Actual / Variance (with over/under colouring flags); and every nav path keeps
/// the cascade correct (master nested under Masters → Create as one page column; report nested under
/// Reports → Statements of Accounts → Budgets as one replacing page column). Drives the real shell VMs
/// over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class BudgetViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BudgetViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBudgetTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Creates a P&amp;L expense ledger (used as a ledger budget target + a posting target).</summary>
    private DomainLedger EnsureExpenseLedger(MainWindowViewModel vm, string name)
    {
        if (vm.Company!.FindLedgerByName(name) is { } existing) return existing;
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.True(master.Create());
        return vm.Company!.FindLedgerByName(name)!;
    }

    /// <summary>Posts a Payment (Dr expense / Cr Cash) for the amount through the voucher-entry VM.</summary>
    private void PostExpense(MainWindowViewModel vm, DomainLedger expense, decimal amount)
    {
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;
        var l0 = entry.Lines[0];
        l0.SelectedLedger = expense;
        l0.Side = DrCr.Debit;
        l0.AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var l1 = entry.Lines[1];
        l1.SelectedLedger = cash;
        l1.Side = DrCr.Credit;
        l1.AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(entry.Accept());
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ---------------------------------------------------------------- (1) master create + persist

    [Fact]
    public void Budget_and_lines_are_created_through_the_master_and_persist()
    {
        const string companyName = "Budget Masters Co";
        var vm = NewSeededCompany(companyName);
        var salaries = EnsureExpenseLedger(vm, "Salaries");

        vm.ShowBudgetMaster();
        Assert.Equal(Screen.BudgetMaster, vm.CurrentScreen);
        var master = vm.BudgetMaster!;
        master.Name = "Annual Budget";

        // Line 1: a GROUP target (Indirect Expenses) on nett transactions, 50,000.
        var group = master.Targets.Single(t => t.IsGroup && t.Display.StartsWith("Indirect Expenses"));
        master.SelectedTarget = group;
        master.SelectedType = BudgetType.OnNettTransactions;
        master.LineAmountText = "50000";
        Assert.True(master.AddLine());

        // Line 2: a LEDGER target (Salaries) on nett transactions, 20,000.
        var ledger = master.Targets.Single(t => !t.IsGroup && t.Display.StartsWith("Salaries"));
        master.SelectedTarget = ledger;
        master.SelectedType = BudgetType.OnNettTransactions;
        master.LineAmountText = "20000";
        Assert.True(master.AddLine());

        Assert.Equal(2, master.PendingLines.Count);
        Assert.True(master.Create());

        // In memory: the budget carries both lines targeting the right group/ledger.
        var budget = vm.Company!.FindBudgetByName("Annual Budget");
        Assert.NotNull(budget);
        Assert.Equal(2, budget!.Lines.Count);
        Assert.Contains(budget.Lines, l => l.IsGroupTarget && l.GroupId == vm.Company!.FindGroupByName("Indirect Expenses")!.Id);
        Assert.Contains(budget.Lines, l => l.IsLedgerTarget && l.LedgerId == salaries.Id);
        Assert.Contains(master.Existing, r => r.Name == "Annual Budget");

        // PERSISTED: reload the .db and the budget + both lines survive.
        var reloaded = Reload(companyName);
        var rb = reloaded.FindBudgetByName("Annual Budget");
        Assert.NotNull(rb);
        Assert.Equal(2, rb!.Lines.Count);
    }

    [Fact]
    public void Budget_master_validates_name_period_and_at_least_one_line()
    {
        var vm = NewSeededCompany("Budget Validate Co");
        vm.ShowBudgetMaster();
        var master = vm.BudgetMaster!;

        // Blank name rejected.
        master.Name = "  ";
        Assert.False(master.Create());

        // Valid name but no lines rejected.
        master.Name = "Empty Budget";
        Assert.False(master.Create());
        Assert.Null(vm.Company!.FindBudgetByName("Empty Budget"));

        // Add a line, then a bad period (To before From) is rejected.
        var target = master.Targets.First();
        master.SelectedTarget = target;
        master.LineAmountText = "1000";
        Assert.True(master.AddLine());
        master.PeriodFromText = "01-Apr-2024";
        master.PeriodToText = "01-Jan-2024";
        Assert.False(master.Create());

        // Fix the period → now valid and persists.
        master.PeriodToText = "31-Mar-2025";
        Assert.True(master.Create());
        Assert.NotNull(vm.Company!.FindBudgetByName("Empty Budget"));
    }

    // ---------------------------------------------------------------- (2) variance report is correct

    [Fact]
    public void Variance_report_shows_correct_budget_actual_and_variance()
    {
        var vm = NewSeededCompany("Budget Variance Co");
        var salaries = EnsureExpenseLedger(vm, "Salaries");

        // Budget Salaries at 20,000 (on nett transactions), Indirect Expenses group at 50,000.
        vm.ShowBudgetMaster();
        var master = vm.BudgetMaster!;
        master.Name = "FY Budget";
        master.SelectedTarget = master.Targets.Single(t => !t.IsGroup && t.Display.StartsWith("Salaries"));
        master.SelectedType = BudgetType.OnNettTransactions;
        master.LineAmountText = "20000";
        Assert.True(master.AddLine());
        master.SelectedTarget = master.Targets.Single(t => t.IsGroup && t.Display.StartsWith("Indirect Expenses"));
        master.SelectedType = BudgetType.OnNettTransactions;
        master.LineAmountText = "50000";
        Assert.True(master.AddLine());
        Assert.True(master.Create());

        // Post actuals: Salaries 25,000 (over its 20,000 budget; Indirect Expenses group actual = 25,000, under 50,000).
        PostExpense(vm, salaries, 25000m);

        // Open the Budget Variance report.
        vm.OpenBudgetVariance();
        Assert.Equal(Screen.BudgetVariance, vm.CurrentScreen);
        var report = vm.BudgetVariance!;
        Assert.Equal(vm.Company!.FindBudgetByName("FY Budget"), report.SelectedBudget);

        // Salaries line: Budget 20,000; Actual 25,000; Variance +5,000 (OVER budget → red flag).
        var salaryRow = report.Rows.Single(r => r.Target.StartsWith("Salaries"));
        Assert.Contains("20,000", salaryRow.Budget);
        Assert.Contains("25,000", salaryRow.Actual);
        Assert.Contains("5,000", salaryRow.Variance);
        Assert.True(salaryRow.IsOver);
        Assert.False(salaryRow.IsUnder);

        // Indirect Expenses group line: Budget 50,000; Actual 25,000; Variance −25,000 (UNDER budget → green flag).
        var groupRow = report.Rows.Single(r => r.Target.StartsWith("Indirect Expenses"));
        Assert.Contains("50,000", groupRow.Budget);
        Assert.Contains("25,000", groupRow.Actual);
        Assert.True(groupRow.IsUnder);
        Assert.False(groupRow.IsOver);

        // Grand total: Budget 70,000; Actual 50,000.
        var total = report.Rows.Single(r => r.IsTotal);
        Assert.Contains("70,000", total.Budget);
        Assert.Contains("50,000", total.Actual);
    }

    [Fact]
    public void Variance_report_empty_state_when_no_budgets()
    {
        var vm = NewSeededCompany("Budget Empty Co");
        vm.OpenBudgetVariance();
        var report = vm.BudgetVariance!;
        Assert.True(report.HasNoBudgets);
        Assert.Empty(report.Rows);
        Assert.Null(report.SelectedBudget);
    }

    // ---------------------------------------------------------------- (3) cascade correctness

    [Fact]
    public void Budget_master_nests_under_masters_create_and_opens_as_a_single_page_column()
    {
        var vm = NewSeededCompany("Budget Nav Masters Co");

        // Masters → Create lists "Budget" as a page item.
        vm.ShowCreateMenu();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);
        var createLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("Budget", createLabels);

        // Opening the Budget master adds exactly ONE page column.
        vm.ShowBudgetMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.BudgetMaster);
        Assert.Same(vm.BudgetMaster, vm.Columns[^1].BudgetMaster);

        // Opening a different master (Ledger) REPLACES it — still exactly one page column.
        vm.ShowLedgerMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.BudgetMaster);
    }

    [Fact]
    public void Budget_variance_nests_under_reports_statements_budgets_and_opens_as_single_page_column()
    {
        var vm = NewSeededCompany("Budget Nav Reports Co");

        // Reports → Statements of Accounts is a hub that now also offers Budgets and Interest Calculation.
        vm.ShowStatementsOfAccountsMenu();
        Assert.Equal(GatewayMenu.StatementsOfAccounts, vm.CurrentGatewayMenu);
        var hubLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Outstandings", "Cost Centres", "Budgets", "Interest Calculation" }, hubLabels);

        // Statements of Accounts → Budgets lists the Budget Variance report.
        vm.ShowBudgetsMenu();
        Assert.Equal(GatewayMenu.Budgets, vm.CurrentGatewayMenu);
        var budgetLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Budget Variance" }, budgetLabels);
        Assert.All(vm.Menu.Where(m => m.IsSelectable), m => Assert.True(m.IsSubItem));

        // Drilling into "Budget Variance" (the highlighted first item) adds exactly ONE page column.
        vm.DrillIn();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.BudgetVariance);
        Assert.Same(vm.BudgetVariance, vm.Columns[^1].BudgetVariance);

        // Opening a plain report (Balance Sheet) then the budget report keeps exactly one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.BudgetVariance);
        vm.OpenBudgetVariance();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.Reports);
    }

    [Fact]
    public void Esc_steps_back_from_budgets_submenu_through_the_hub_to_root()
    {
        var vm = NewSeededCompany("Budget Esc Co");
        vm.ShowBudgetsMenu();                                         // [root, Budgets]
        Assert.Equal(GatewayMenu.Budgets, vm.CurrentGatewayMenu);

        vm.DrillIn();                                                 // open Budget Variance page column
        Assert.Equal(Screen.BudgetVariance, vm.CurrentScreen);

        vm.Back();                                                    // back to the Budgets submenu
        Assert.Equal(GatewayMenu.Budgets, vm.CurrentGatewayMenu);

        vm.Back();                                                    // back to root Gateway
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Root, vm.CurrentGatewayMenu);
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
