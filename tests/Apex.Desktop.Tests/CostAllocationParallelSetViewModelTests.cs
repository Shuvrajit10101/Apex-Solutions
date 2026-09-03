using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// G-2 through the real shell VMs: the corpus's multi-category cost allocation must be <b>enterable</b>
/// (TALLY PRIME STUDY GUIDE pp.101–102 — one ₹5,000 travelling expense allocated in full to
/// Branch → Kolkata AND in full to Department → Marketing), must persist to SQLite and reload, and the
/// Category Summary must not add the parallel axes together. Drives the shell over a throwaway .db — no UI
/// toolkit. Odd paisa throughout so a halving/rounding defect cannot hide behind round numbers.
/// </summary>
public sealed class CostAllocationParallelSetViewModelTests : IDisposable
{
    private const string TravelText = "5000.37";
    private const decimal Travel = 5000.37m;

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CostAllocationParallelSetViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCostAxisTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private static CostCategory CreateCategory(MainWindowViewModel vm, string name)
    {
        vm.ShowCostCategoryMaster();
        var master = vm.CostCategoryMaster!;
        master.Name = name;
        master.AllocateRevenueItems = true;
        Assert.True(master.Create());
        return vm.Company!.FindCostCategoryByName(name)!;
    }

    private static CostCentre CreateCentre(MainWindowViewModel vm, CostCategory category, string name)
    {
        vm.ShowCostCentreMaster();
        var master = vm.CostCentreMaster!;
        master.SelectedCategory = master.Categories.Single(c => c.Id == category.Id);
        master.Name = name;
        Assert.True(master.Create());
        return vm.Company!.FindCostCentreByName(name)!;
    }

    private static DomainLedger CreateExpenseLedger(MainWindowViewModel vm, string name)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.True(master.Create());
        return vm.Company!.FindLedgerByName(name)!;
    }

    /// <summary>Sets one cost-allocation row (adding it first when the row does not exist yet).</summary>
    private static void SetAllocation(
        MainWindowViewModel vm, VoucherLineViewModel line, int index,
        CostCategory category, CostCentre centre, string amount)
    {
        while (line.CostAllocations.Count <= index) vm.AddCostAllocation(line);
        var row = line.CostAllocations[index];
        row.SelectedCategory = row.Categories.Single(c => c.Id == category.Id);
        row.SelectedCentre = row.Centres.Single(c => c.Id == centre.Id);
        row.AmountText = amount;
    }

    /// <summary>The corpus fixture: Branch/Kolkata + Department/Marketing over a Travelling Expenses ledger.</summary>
    private MainWindowViewModel SeedCorpusFixture(
        string companyName, out CostCategory branch, out CostCentre kolkata,
        out CostCategory department, out CostCentre marketing, out DomainLedger travelling)
    {
        var vm = NewSeededCompany(companyName);
        branch = CreateCategory(vm, "Branch");
        department = CreateCategory(vm, "Department");
        kolkata = CreateCentre(vm, branch, "Kolkata");
        marketing = CreateCentre(vm, department, "Marketing");
        travelling = CreateExpenseLedger(vm, "Travelling Expenses");
        return vm;
    }

    /// <summary>Opens a Payment and fills the Travelling/Cash legs, leaving the cost panel to the caller.</summary>
    private static VoucherLineViewModel OpenTravelPayment(
        MainWindowViewModel vm, DomainLedger travelling, string amountText)
    {
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = travelling;
        l0.Side = DrCr.Debit;
        l0.AmountText = amountText;

        var l1 = entry.Lines[1];
        l1.SelectedLedger = vm.Company!.FindLedgerByName("Cash")!;
        l1.Side = DrCr.Credit;
        l1.AmountText = amountText;

        return l0;
    }

    // ---------------------------------------------------------------- (1) the corpus entry works

    [Fact]
    public void Corpus_multi_category_allocation_is_enterable_persists_and_reloads()
    {
        const string companyName = "Cost Axis Shell Co";
        var vm = SeedCorpusFixture(companyName, out var branch, out var kolkata,
            out var department, out var marketing, out var travelling);

        var l0 = OpenTravelPayment(vm, travelling, TravelText);
        Assert.True(l0.IsCostApplicable);

        // The corpus entry: the WHOLE amount under Branch, then the WHOLE amount again under Department.
        SetAllocation(vm, l0, 0, branch, kolkata, TravelText);
        SetAllocation(vm, l0, 1, department, marketing, TravelText);
        Assert.True(l0.CostSplitOk);
        Assert.Contains("Branch", l0.CostSummary);
        Assert.Contains("Department", l0.CostSummary);
        Assert.Contains("parallel", l0.CostSummary);

        var entry = vm.VoucherEntry!;
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var paymentType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == paymentType.Id);
        var line = posted.Lines.Single(l => l.LedgerId == travelling.Id);
        Assert.Equal(2, line.CostAllocations.Count);
        Assert.Equal(Travel, line.CostAllocationTotalFor(branch.Id).Amount);
        Assert.Equal(Travel, line.CostAllocationTotalFor(department.Id).Amount);
        // The line itself is still ₹5,000.37 — the axes are not a split of it.
        Assert.Equal(Travel, line.Amount.Amount);

        // PERSISTED: the .db reopens (the load path re-posts through the engine) with both axes intact.
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));
        var rTravelling = reloaded.FindLedgerByName("Travelling Expenses")!;
        var rBranch = reloaded.FindCostCategoryByName("Branch")!;
        var rDepartment = reloaded.FindCostCategoryByName("Department")!;
        var rLine = reloaded.Vouchers
            .SelectMany(v => v.Lines)
            .Single(l => l.LedgerId == rTravelling.Id && l.HasCostAllocations);
        Assert.Equal(Travel, rLine.CostAllocationTotalFor(rBranch.Id).Amount);
        Assert.Equal(Travel, rLine.CostAllocationTotalFor(rDepartment.Id).Amount);
    }

    // ---------------------------------------------------------------- (2) the reject side at entry

    [Fact]
    public void A_cross_axis_split_is_blocked_at_entry()
    {
        var vm = SeedCorpusFixture("Cost Axis Block Co", out var branch, out var kolkata,
            out var department, out var marketing, out var travelling);

        var l0 = OpenTravelPayment(vm, travelling, TravelText);

        // 3,000.19 Branch + 2,000.18 Department "adds up" to the line — but neither axis foots.
        SetAllocation(vm, l0, 0, branch, kolkata, "3000.19");
        SetAllocation(vm, l0, 1, department, marketing, "2000.18");
        Assert.False(l0.CostSplitOk);

        var entry = vm.VoucherEntry!;
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());

        // Allocating each axis in full fixes it.
        l0.CostAllocations[0].AmountText = TravelText;
        l0.CostAllocations[1].AmountText = TravelText;
        Assert.True(l0.CostSplitOk);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
    }

    [Fact]
    public void Within_one_axis_the_split_across_centres_is_unchanged()
    {
        // The half of the old rule that was RIGHT must not regress: inside one category the amounts still
        // have to add up to the line, and a short split still blocks accept.
        var vm = SeedCorpusFixture("Cost Axis Split Co", out var branch, out var kolkata,
            out _, out _, out var travelling);
        var delhi = CreateCentre(vm, branch, "Delhi");

        var l0 = OpenTravelPayment(vm, travelling, TravelText);
        SetAllocation(vm, l0, 0, branch, kolkata, "3000.19");
        SetAllocation(vm, l0, 1, branch, delhi, "2000.00");     // 1 paisa short of 5,000.37
        Assert.False(l0.CostSplitOk);
        Assert.Contains("unallocated", l0.CostSummary);
        Assert.DoesNotContain("parallel", l0.CostSummary);      // one axis ⇒ the original wording

        l0.CostAllocations[1].AmountText = "2000.18";           // now 5,000.37 within Branch
        Assert.True(l0.CostSplitOk);
        Assert.Contains("fully allocated", l0.CostSummary);
        Assert.True(vm.VoucherEntry!.Accept());
    }

    // ---------------------------------------------------------------- (3) the report must not double-count

    [Fact]
    public void Category_summary_screen_reports_the_cost_once_and_says_the_axes_are_parallel()
    {
        var vm = SeedCorpusFixture("Cost Axis Report Co", out var branch, out var kolkata,
            out var department, out var marketing, out var travelling);

        var l0 = OpenTravelPayment(vm, travelling, TravelText);
        SetAllocation(vm, l0, 0, branch, kolkata, TravelText);
        SetAllocation(vm, l0, 1, department, marketing, TravelText);
        Assert.True(vm.VoucherEntry!.Accept());

        vm.OpenCostReport(CostReportKind.CategorySummary);
        var summary = vm.CostReports!;

        // Each axis shows the WHOLE expense…
        Assert.Contains("5,000.37", summary.Rows.Single(r => r.Particulars == "Branch").Amount);
        Assert.Contains("5,000.37", summary.Rows.Single(r => r.Particulars == "Department").Amount);

        // …and the footer reports one ₹5,000.37 of cost, not ₹10,000.74, with the reason stated on screen.
        var total = summary.Rows.Single(r => r.IsTotal);
        Assert.Equal("Total Cost Allocated", total.Particulars);
        Assert.Contains("5,000.37", total.Amount);
        Assert.DoesNotContain("10,000.74", total.Amount);
        Assert.Contains(summary.Rows, r => r.Particulars.Contains("parallel axes"));

        // The Cost Centre Break-up says the same: each centre carries the whole amount, counted once.
        vm.OpenCostReport(CostReportKind.CostCentreBreakup);
        var breakup = vm.CostReports!;
        Assert.Contains("5,000.37", breakup.Rows.Single(r => r.Particulars == "Kolkata").Amount);
        Assert.Contains("5,000.37", breakup.Rows.Single(r => r.Particulars == "Marketing").Amount);
        var breakupTotal = breakup.Rows.Single(r => r.IsTotal);
        Assert.Equal("Total Cost Allocated", breakupTotal.Particulars);
        Assert.Contains("5,000.37", breakupTotal.Amount);
    }

    [Fact]
    public void A_single_axis_company_still_shows_the_plain_grand_total()
    {
        // Zero-regression guard: a company that only ever allocates along one axis must see the report it
        // has always seen — the label, the number and the absence of any explanatory note.
        var vm = SeedCorpusFixture("Cost Axis Single Co", out var branch, out var kolkata,
            out _, out _, out var travelling);

        var l0 = OpenTravelPayment(vm, travelling, TravelText);
        SetAllocation(vm, l0, 0, branch, kolkata, TravelText);
        Assert.True(vm.VoucherEntry!.Accept());

        vm.OpenCostReport(CostReportKind.CategorySummary);
        var summary = vm.CostReports!;
        var total = summary.Rows.Single(r => r.IsTotal);
        Assert.Equal("Grand Total", total.Particulars);
        Assert.Contains("5,000.37", total.Amount);
        Assert.DoesNotContain(summary.Rows, r => r.Particulars.Contains("parallel"));
    }

    [Fact]
    public void A_legacy_partition_book_still_shows_the_plain_grand_total_and_no_parallel_note()
    {
        // A company saved BEFORE this fix holds a ₹5,000.37 line "split" ₹3,000.19 to Branch → Kolkata and
        // ₹2,000.18 to Department → Marketing. Rehydration admits it (CostAllocationStrictness.Legacy) and
        // its two category rows really DO add up to the grand total — the number on screen did not move by
        // one paisa. So the footer must read exactly as it always did. Printing "the category totals above
        // do not add up to this total" here contradicts the two rows immediately above it, and lands on the
        // one population the migration story exists to protect.
        var vm = SeedCorpusFixture("Cost Axis Legacy Co", out var branch, out var kolkata,
            out var department, out var marketing, out var travelling);

        var company = vm.Company!;
        var cash = company.FindLedgerByName("Cash")!;
        var paymentType = company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var legacy = new Voucher(Guid.NewGuid(), paymentType.Id, company.BooksBeginFrom.AddDays(4), new[]
        {
            new EntryLine(travelling.Id, Money.FromRupees(Travel), DrCr.Debit, billAllocations: null,
                costAllocations: new[]
                {
                    new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
                    new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)),
                }),
            new EntryLine(cash.Id, Money.FromRupees(Travel), DrCr.Credit),
        });
        new LedgerService(company).Post(legacy, CostAllocationStrictness.Legacy);

        vm.OpenCostReport(CostReportKind.CategorySummary);
        var summary = vm.CostReports!;
        Assert.Contains("3,000.19", summary.Rows.Single(r => r.Particulars == "Branch").Amount);
        Assert.Contains("2,000.18", summary.Rows.Single(r => r.Particulars == "Department").Amount);

        var total = summary.Rows.Single(r => r.IsTotal);
        Assert.Equal("Grand Total", total.Particulars);
        Assert.Contains("5,000.37", total.Amount);
        Assert.DoesNotContain(summary.Rows, r => r.Particulars.Contains("parallel"));
        Assert.DoesNotContain(summary.Rows, r => r.Particulars.Contains("do not add up"));

        // The Cost Centre Break-up says the same — 3,000.19 + 2,000.18 is a genuine column sum here.
        vm.OpenCostReport(CostReportKind.CostCentreBreakup);
        var breakup = vm.CostReports!;
        var breakupTotal = breakup.Rows.Single(r => r.IsTotal);
        Assert.Equal("Grand Total", breakupTotal.Particulars);
        Assert.Contains("5,000.37", breakupTotal.Amount);
        Assert.DoesNotContain(breakup.Rows, r => r.Particulars.Contains("parallel"));
    }

    [Fact]
    public void The_line_view_model_exposes_no_cross_category_allocation_sum()
    {
        // The cross-category sum is precisely the quantity rule C-27 forbids as a check: on a parallel set
        // it is a MULTIPLE of the line amount, so comparing it to the line makes the corpus entry
        // impossible. Nothing reads it any more. A public property sitting beside ParsedAmount named as if
        // it were "the amount allocated" is an invitation to re-introduce the partition bug in the UI, so
        // it must not exist at all — only the per-axis accessor may.
        Assert.Null(typeof(VoucherLineViewModel).GetProperty("CostAllocatedTotal"));
        Assert.NotNull(typeof(VoucherLineViewModel).GetMethod("CostAllocatedTotalFor"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
