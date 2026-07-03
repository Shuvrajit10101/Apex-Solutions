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
/// End-to-end coverage for the Cost Categories &amp; Cost Centres UI surfaced in the cascade (catalog §6):
/// a cost category + cost centre are created through the master screens and persist; a cost-applicable
/// voucher line captures a cost allocation (Category → Centre → Amount) that posts through the engine +
/// SQLite; the Category Summary and Cost Centre Break-up reports show the centre totals; and every nav path
/// keeps the cascade correct (masters nested under Masters → Create; reports nested under Reports →
/// Statements of Accounts → Cost Centres, each ONE page column). Drives the real shell VMs over a
/// throwaway .db — no UI toolkit.
/// </summary>
public sealed class CostCentreViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CostCentreViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCostCentreTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Creates a "Departments" cost category (allocate revenue) through the master screen.</summary>
    private CostCategory CreateCategory(MainWindowViewModel vm, string name = "Departments")
    {
        vm.ShowCostCategoryMaster();
        Assert.Equal(Screen.CostCategoryMaster, vm.CurrentScreen);
        var master = vm.CostCategoryMaster!;
        master.Name = name;
        master.AllocateRevenueItems = true;
        Assert.True(master.Create());
        var cat = vm.Company!.FindCostCategoryByName(name);
        Assert.NotNull(cat);
        return cat!;
    }

    /// <summary>Creates a cost centre under a category (Primary parent) through the master screen.</summary>
    private CostCentre CreateCentre(MainWindowViewModel vm, CostCategory category, string name)
    {
        vm.ShowCostCentreMaster();
        Assert.Equal(Screen.CostCentreMaster, vm.CurrentScreen);
        var master = vm.CostCentreMaster!;
        master.SelectedCategory = master.Categories.Single(c => c.Id == category.Id);
        master.Name = name;
        // Under defaults to Primary (first parent option).
        Assert.True(master.SelectedParent!.IsPrimary);
        Assert.True(master.Create());
        var centre = vm.Company!.FindCostCentreByName(name);
        Assert.NotNull(centre);
        return centre!;
    }

    /// <summary>Ensures a P&amp;L expense ledger exists (cost centres applicable by nature).</summary>
    private DomainLedger EnsureExpenseLedger(MainWindowViewModel vm, string name = "Salaries")
    {
        if (vm.Company!.FindLedgerByName(name) is { } existing) return existing;
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.True(master.Create());
        return vm.Company!.FindLedgerByName(name)!;
    }

    // ---------------------------------------------------------------- (1) masters create + persist

    [Fact]
    public void Cost_category_and_centre_are_created_through_masters_and_persist()
    {
        const string companyName = "Cost Masters Co";
        var vm = NewSeededCompany(companyName);

        var cat = CreateCategory(vm, "Departments");
        // The seeded Primary category + the new one both listed.
        Assert.Contains(vm.CostCategoryMaster!.Existing, r => r.Name == "Departments");

        var centre = CreateCentre(vm, cat, "Delhi Branch");
        Assert.Equal(cat.Id, centre.CategoryId);
        Assert.True(centre.IsPrimary);
        Assert.Contains(vm.CostCentreMaster!.Existing, r => r.Name == "Delhi Branch" && r.Category == "Departments");

        // A second centre can nest UNDER the first (hierarchical parent picker).
        vm.ShowCostCentreMaster();
        var m2 = vm.CostCentreMaster!;
        m2.SelectedCategory = m2.Categories.Single(c => c.Id == cat.Id);
        // The parent picker now offers "Primary" + the existing Delhi Branch.
        var parentDelhi = m2.ParentOptions.Single(p => p.Centre?.Id == centre.Id);
        m2.SelectedParent = parentDelhi;
        m2.Name = "Delhi — Sales";
        Assert.True(m2.Create());
        var child = vm.Company!.FindCostCentreByName("Delhi — Sales")!;
        Assert.Equal(centre.Id, child.ParentId);

        // PERSISTED: reload the .db and the category + both centres survive.
        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindCostCategoryByName("Departments"));
        var rDelhi = reloaded.FindCostCentreByName("Delhi Branch");
        var rChild = reloaded.FindCostCentreByName("Delhi — Sales");
        Assert.NotNull(rDelhi);
        Assert.NotNull(rChild);
        Assert.Equal(rDelhi!.Id, rChild!.ParentId);
    }

    [Fact]
    public void Cost_category_requires_a_name_and_at_least_one_allocation_flag()
    {
        var vm = NewSeededCompany("Cost Validate Co");
        vm.ShowCostCategoryMaster();
        var master = vm.CostCategoryMaster!;

        master.Name = "  ";
        Assert.False(master.Create());               // blank name rejected

        master.Name = "Projects";
        master.AllocateRevenueItems = false;
        master.AllocateNonRevenueItems = false;
        Assert.False(master.Create());               // no allocation flag rejected
        Assert.Null(vm.Company!.FindCostCategoryByName("Projects"));

        master.AllocateNonRevenueItems = true;
        Assert.True(master.Create());                // now valid
        Assert.NotNull(vm.Company!.FindCostCategoryByName("Projects"));
    }

    // ---------------------------------------------------------------- (2) voucher captures + posts

    [Fact]
    public void Cost_applicable_line_captures_an_allocation_and_posts_it()
    {
        const string companyName = "Cost Voucher Co";
        var vm = NewSeededCompany(companyName);
        var cat = CreateCategory(vm, "Departments");
        var delhi = CreateCentre(vm, cat, "Delhi Branch");
        var mumbai = CreateCentre(vm, cat, "Mumbai Branch");
        var salaries = EnsureExpenseLedger(vm, "Salaries");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        // Payment: Dr Salaries 60,000 (cost-applicable) / Cr Cash 60,000, split 40k Delhi + 20k Mumbai.
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = salaries;
        l0.Side = DrCr.Debit;
        l0.AmountText = "60000";

        // Selecting a cost-applicable ledger turns the sub-panel on and seeds a first blank row.
        Assert.True(l0.IsCostApplicable);
        Assert.Single(l0.CostAllocations);

        var a0 = l0.CostAllocations[0];
        a0.SelectedCategory = a0.Categories.Single(c => c.Id == cat.Id);
        a0.SelectedCentre = a0.Centres.Single(c => c.Id == delhi.Id);
        a0.AmountText = "40000";

        // Add the second centre for the remaining 20,000 so the split sums to the line.
        vm.AddCostAllocation(l0);
        var a1 = l0.CostAllocations[1];
        a1.SelectedCategory = a1.Categories.Single(c => c.Id == cat.Id);
        a1.SelectedCentre = a1.Centres.Single(c => c.Id == mumbai.Id);
        a1.AmountText = "20000";
        Assert.True(l0.CostSplitOk);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = cash;
        l1.Side = DrCr.Credit;
        l1.AmountText = "60000";

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        // Posted in memory: the salaries line carries two cost allocations totalling 60,000.
        var paymentType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == paymentType.Id);
        var salaryLine = posted.Lines.Single(l => l.LedgerId == salaries.Id);
        Assert.True(salaryLine.HasCostAllocations);
        Assert.Equal(2, salaryLine.CostAllocations.Count);
        Assert.Equal(60000m, salaryLine.CostAllocationTotal.Amount);

        // PERSISTED: reload the .db and the allocations survive (cost_allocations table).
        var reloaded = Reload(companyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var rVoucher = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        var rSalary = reloaded.FindLedgerByName("Salaries")!;
        var rLine = rVoucher.Lines.Single(l => l.LedgerId == rSalary.Id);
        Assert.Equal(2, rLine.CostAllocations.Count);
        Assert.Equal(60000m, rLine.CostAllocationTotal.Amount);
    }

    [Fact]
    public void Cost_split_that_does_not_sum_to_the_line_blocks_accept()
    {
        var vm = NewSeededCompany("Cost Split Co");
        var cat = CreateCategory(vm, "Departments");
        var delhi = CreateCentre(vm, cat, "Delhi Branch");
        var salaries = EnsureExpenseLedger(vm, "Salaries");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = salaries;
        l0.Side = DrCr.Debit;
        l0.AmountText = "60000";
        Assert.True(l0.IsCostApplicable);

        // Allocate only 40,000 of 60,000 → split invalid → accept blocked.
        var a0 = l0.CostAllocations[0];
        a0.SelectedCategory = a0.Categories.Single(c => c.Id == cat.Id);
        a0.SelectedCentre = a0.Centres.Single(c => c.Id == delhi.Id);
        a0.AmountText = "40000";
        Assert.False(l0.CostSplitOk);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = cash;
        l1.Side = DrCr.Credit;
        l1.AmountText = "60000";

        Assert.False(entry.CanAccept);   // balanced Dr=Cr, but the cost split is short
        Assert.False(entry.Accept());
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

        // Fix the amount to fully allocate → now valid and accepts.
        a0.AmountText = "60000";
        Assert.True(l0.CostSplitOk);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
    }

    [Fact]
    public void Cost_allocation_is_optional_when_left_blank()
    {
        var vm = NewSeededCompany("Cost Optional Co");
        var cat = CreateCategory(vm, "Departments");
        CreateCentre(vm, cat, "Delhi Branch");
        var salaries = EnsureExpenseLedger(vm, "Salaries");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = salaries;
        l0.Side = DrCr.Debit;
        l0.AmountText = "10000";
        Assert.True(l0.IsCostApplicable);
        // Leave the cost panel untouched — it is optional.
        Assert.True(l0.CostSplitOk);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = cash;
        l1.Side = DrCr.Credit;
        l1.AmountText = "10000";

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // Nothing allocated ⇒ the posted line carries no cost allocations.
        var paymentType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == paymentType.Id);
        Assert.False(posted.Lines.Single(l => l.LedgerId == salaries.Id).HasCostAllocations);
    }

    // ---------------------------------------------------------------- (3) reports show centre totals

    [Fact]
    public void Cost_reports_show_the_category_and_centre_totals()
    {
        var vm = NewSeededCompany("Cost Report Co");
        var cat = CreateCategory(vm, "Departments");
        var delhi = CreateCentre(vm, cat, "Delhi Branch");
        var mumbai = CreateCentre(vm, cat, "Mumbai Branch");
        var salaries = EnsureExpenseLedger(vm, "Salaries");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        // Dr Salaries 60,000 split Delhi 40,000 + Mumbai 20,000 / Cr Cash.
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;
        var l0 = entry.Lines[0];
        l0.SelectedLedger = salaries;
        l0.Side = DrCr.Debit;
        l0.AmountText = "60000";
        l0.CostAllocations[0].SelectedCategory = l0.CostAllocations[0].Categories.Single(c => c.Id == cat.Id);
        l0.CostAllocations[0].SelectedCentre = l0.CostAllocations[0].Centres.Single(c => c.Id == delhi.Id);
        l0.CostAllocations[0].AmountText = "40000";
        vm.AddCostAllocation(l0);
        l0.CostAllocations[1].SelectedCategory = l0.CostAllocations[1].Categories.Single(c => c.Id == cat.Id);
        l0.CostAllocations[1].SelectedCentre = l0.CostAllocations[1].Centres.Single(c => c.Id == mumbai.Id);
        l0.CostAllocations[1].AmountText = "20000";
        entry.Lines[1].SelectedLedger = cash;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "60000";
        Assert.True(entry.Accept());

        // Category Summary: Departments totals 60,000.
        vm.OpenCostReport(CostReportKind.CategorySummary);
        Assert.Equal(Screen.CostReport, vm.CurrentScreen);
        var summary = vm.CostReports!;
        Assert.Equal("Category Summary", summary.Title);
        var deptRow = summary.Rows.Single(r => r.Particulars == "Departments");
        Assert.Contains("60,000", deptRow.Amount);

        // Cost Centre Break-up: Delhi 40,000, Mumbai 20,000, grand total 60,000.
        vm.OpenCostReport(CostReportKind.CostCentreBreakup);
        var breakup = vm.CostReports!;
        Assert.Equal("Cost Centre Break-up", breakup.Title);
        var delhiRow = breakup.Rows.Single(r => r.Particulars == "Delhi Branch");
        var mumbaiRow = breakup.Rows.Single(r => r.Particulars == "Mumbai Branch");
        Assert.Contains("40,000", delhiRow.Amount);
        Assert.Contains("20,000", mumbaiRow.Amount);
        var grand = breakup.Rows.Single(r => r.IsTotal);
        Assert.Contains("60,000", grand.Amount);
    }

    // ---------------------------------------------------------------- (4) cascade correctness

    [Fact]
    public void Cost_masters_nest_under_masters_create_and_open_as_a_single_page_column()
    {
        var vm = NewSeededCompany("Cost Nav Masters Co");

        // Masters → Create lists the Cost masters as page items.
        vm.ShowCreateMenu();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);
        var createLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("Cost Category", createLabels);
        Assert.Contains("Cost Centre", createLabels);

        // Opening the Cost Category master adds exactly ONE page column.
        vm.ShowCostCategoryMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.CostCategoryMaster);
        Assert.Same(vm.CostCategoryMaster, vm.Columns[^1].CostCategory);

        // Opening the Cost Centre master REPLACES it — still exactly one page column.
        vm.ShowCostCentreMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.CostCategoryMaster);
        Assert.NotNull(vm.CostCentreMaster);
    }

    [Fact]
    public void Cost_reports_nest_under_reports_statements_cost_centres_and_open_as_single_page_column()
    {
        var vm = NewSeededCompany("Cost Nav Reports Co");

        // Reports → Statements of Accounts is a hub with Outstandings + Cost Centres groups.
        vm.ShowStatementsOfAccountsMenu();
        Assert.Equal(GatewayMenu.StatementsOfAccounts, vm.CurrentGatewayMenu);
        var hubLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Outstandings", "Cost Centres", "Budgets" }, hubLabels);

        // Statements of Accounts → Cost Centres lists the two cost reports.
        vm.ShowCostCentresMenu();
        Assert.Equal(GatewayMenu.CostCentres, vm.CurrentGatewayMenu);
        var costLabels = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Category Summary", "Cost Centre Break-up" }, costLabels);
        Assert.All(vm.Menu.Where(m => m.IsSelectable), m => Assert.True(m.IsSubItem));

        // Drilling into "Category Summary" (the highlighted first item) adds exactly ONE page column.
        vm.DrillIn();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.CostReports);
        Assert.Same(vm.CostReports, vm.Columns[^1].CostReport);
        Assert.Equal(CostReportKind.CategorySummary, vm.CostReports!.Kind);

        // Opening the Break-up REPLACES it — still exactly one page column.
        vm.OpenCostReport(CostReportKind.CostCentreBreakup);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Equal(CostReportKind.CostCentreBreakup, vm.CostReports!.Kind);

        // Opening a plain report (Balance Sheet) then a cost report keeps exactly one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.CostReports);
        vm.OpenCostReport(CostReportKind.CategorySummary);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.Reports);
    }

    [Fact]
    public void Esc_steps_back_from_cost_centres_submenu_through_the_hub_to_root()
    {
        var vm = NewSeededCompany("Cost Esc Co");
        vm.ShowCostCentresMenu();                                    // [root, Cost Centres]
        Assert.Equal(GatewayMenu.CostCentres, vm.CurrentGatewayMenu);

        vm.DrillIn();                                                // open Category Summary page column
        Assert.Equal(Screen.CostReport, vm.CurrentScreen);

        vm.Back();                                                   // back to the Cost Centres submenu
        Assert.Equal(GatewayMenu.CostCentres, vm.CurrentGatewayMenu);

        vm.Back();                                                   // back to root Gateway
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
