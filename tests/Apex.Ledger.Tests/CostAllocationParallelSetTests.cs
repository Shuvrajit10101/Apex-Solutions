using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// G-2 — cost categories are <b>parallel allocation axes</b>, not a partition (spec §4.2, rule C-27).
/// The corpus's worked example enters a ₹5,000 travelling expense and allocates the <b>whole</b> ₹5,000 to
/// Branch → Kolkata, then the <b>whole</b> ₹5,000 again to Department → Marketing, then "the same for other
/// Categories and Centres if required" — i.e. Executive → Sales Executive 1 for the whole ₹5,000 as well
/// (TALLY PRIME STUDY GUIDE pp.101–102 <b>[verified-A1]</b>).
/// <para>The invariant is therefore <b>per category</b>: within one cost category the allocations must total
/// the line amount; across categories they must never be added together. These tests pin both halves — the
/// accept side (the corpus example must post) and the reject side (a "split" that only foots when the axes
/// are added together must be refused) — and the reports, which must not double-count parallel axes.</para>
/// <para>Every money figure carries odd paisa on purpose: a ±₹0.50 defect once lived in this codebase under
/// six round-number assertions.</para>
/// </summary>
public class CostAllocationParallelSetTests
{
    /// <summary>The corpus's ₹5,000 with odd paisa, so a halving/rounding defect cannot hide.</summary>
    private static readonly Money Travel = Money.FromRupees(5000.37m);

    /// <summary>
    /// The corpus scenario: a Travelling Expenses ledger, Cash, and three cost categories
    /// (Branch / Department / Executive) with centres under each.
    /// </summary>
    private static Company Seed(
        out Domain.Ledger cash,
        out Domain.Ledger travelling,
        out CostCategory branch, out CostCentre kolkata, out CostCentre delhi,
        out CostCategory department, out CostCentre marketing,
        out CostCategory executive, out CostCentre salesExec1,
        out VoucherType payment)
    {
        var c = CompanyFactory.CreateSeeded("Cost Axis Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        travelling = new Domain.Ledger(Guid.NewGuid(), "Travelling Expenses",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(travelling);

        branch = new CostCategory(Guid.NewGuid(), "Branch");
        department = new CostCategory(Guid.NewGuid(), "Department");
        executive = new CostCategory(Guid.NewGuid(), "Executive");
        c.AddCostCategory(branch);
        c.AddCostCategory(department);
        c.AddCostCategory(executive);

        kolkata = new CostCentre(Guid.NewGuid(), "Kolkata", branch.Id);
        delhi = new CostCentre(Guid.NewGuid(), "Delhi", branch.Id);
        marketing = new CostCentre(Guid.NewGuid(), "Marketing", department.Id);
        salesExec1 = new CostCentre(Guid.NewGuid(), "Sales Executive 1", executive.Id);
        c.AddCostCentre(kolkata);
        c.AddCostCentre(delhi);
        c.AddCostCentre(marketing);
        c.AddCostCentre(salesExec1);

        payment = c.FindVoucherTypeByName("Payment")!;
        return c;
    }

    private static Voucher TravelVoucher(
        Guid typeId, Guid travellingId, Guid cashId, Money amount, params CostAllocation[] allocations) =>
        new(Guid.NewGuid(), typeId, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(travellingId, amount, DrCr.Debit, billAllocations: null, costAllocations: allocations),
            new EntryLine(cashId, amount, DrCr.Credit),
        });

    private static readonly DateOnly WindowFrom = new(2024, 4, 1);
    private static readonly DateOnly WindowTo = new(2024, 4, 30);

    // ------------------------------------------------------------------ the accept side

    [Fact]
    public void Corpus_worked_example_allocates_the_whole_amount_under_every_category()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out var executive, out var se1, out var payment);
        var svc = new LedgerService(c);

        // SG pp.101–102: ₹5,000.37 travelling expense → Branch/Kolkata ₹5,000.37 AND
        // Department/Marketing ₹5,000.37 AND Executive/Sales Executive 1 ₹5,000.37.
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),
            new CostAllocation(department.Id, marketing.Id, Travel),
            new CostAllocation(executive.Id, se1.Id, Travel)));

        var line = Assert.Single(c.Vouchers).Lines.Single(l => l.LedgerId == travelling.Id);
        Assert.Equal(3, line.CostAllocations.Count);
        // The line itself is still ₹5,000.37 — the axes are not a split of it.
        Assert.Equal(Travel, line.Amount);
    }

    [Fact]
    public void One_axis_may_be_split_across_its_centres_while_another_takes_the_whole_amount()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out var delhi,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        // Branch axis split 3,000.19 Kolkata + 2,000.18 Delhi = 5,000.37; Department axis whole 5,000.37.
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(branch.Id, delhi.Id, Money.FromRupees(2000.18m)),
            new CostAllocation(department.Id, marketing.Id, Travel)));

        Assert.Equal(3, Assert.Single(c.Vouchers).Lines.Single(l => l.LedgerId == travelling.Id)
            .CostAllocations.Count);
    }

    [Fact]
    public void Allocating_along_a_single_axis_only_is_still_valid()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out _, out _, out _, out _, out var payment);

        new LedgerService(c).Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel)));

        Assert.Single(c.Vouchers);
    }

    // ------------------------------------------------------------------ the reject side

    [Fact]
    public void A_split_that_only_foots_when_the_axes_are_added_together_is_rejected()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        // The old (partition) rule accepted this: 3,000.19 + 2,000.18 = 5,000.37 across TWO axes.
        // Under parallel sets neither axis foots — Branch has 3,000.19, Department has 2,000.18.
        var bad = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));

        var ex = Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
        // C-27: the corpus's own failure text names the ledger AND the category.
        Assert.Contains("Travelling Expenses", ex.Message);
        Assert.Contains("Branch", ex.Message);
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void A_short_axis_is_rejected_even_when_another_axis_is_complete()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        var bad = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),                            // complete
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m))); // short

        var ex = Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
        Assert.Contains("Department", ex.Message);
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void An_over_allocated_axis_is_rejected()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out var delhi,
            out _, out _, out _, out _, out var payment);

        var bad = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),
            new CostAllocation(branch.Id, delhi.Id, Money.FromRupees(0.01m)));

        Assert.Throws<InvalidVoucherException>(() => new LedgerService(c).Post(bad));
        Assert.Empty(c.Vouchers);
    }

    // ------------------------------------------------------------------ reports must not double-count

    private static Company SeedWithParallelPosting(
        out CostCategory branch, out CostCentre kolkata,
        out CostCategory department, out CostCentre marketing,
        out CostCategory executive, out CostCentre salesExec1,
        out Domain.Ledger travelling)
    {
        var c = Seed(out var cash, out travelling, out branch, out kolkata, out _,
            out department, out marketing, out executive, out salesExec1, out var payment);
        new LedgerService(c).Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),
            new CostAllocation(department.Id, marketing.Id, Travel),
            new CostAllocation(executive.Id, salesExec1.Id, Travel)));
        return c;
    }

    [Fact]
    public void Category_summary_shows_the_full_amount_on_every_axis_and_does_not_add_the_axes_up()
    {
        var c = SeedWithParallelPosting(out var branch, out _, out var department, out _,
            out var executive, out _, out _);
        var report = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);

        Assert.Equal(3, report.Categories.Count);
        Assert.Equal(Travel, report.Categories.Single(r => r.CategoryId == branch.Id).Total);
        Assert.Equal(Travel, report.Categories.Single(r => r.CategoryId == department.Id).Total);
        Assert.Equal(Travel, report.Categories.Single(r => r.CategoryId == executive.Id).Total);

        // The one expense was ₹5,000.37 — NOT ₹15,001.11. Adding parallel axes together is meaningless.
        Assert.Equal(Travel, report.GrandTotal);
    }

    [Fact]
    public void Cost_centre_breakup_counts_each_line_once_across_parallel_axes()
    {
        var c = SeedWithParallelPosting(out _, out var kolkata, out _, out var marketing,
            out _, out var salesExec1, out _);
        var report = CostReports.BuildCostCentreBreakup(c, WindowFrom, WindowTo);

        // Every axis's centre carries the whole amount…
        Assert.Equal(Travel, report.Centres.Single(l => l.CentreId == kolkata.Id).OwnTotal);
        Assert.Equal(Travel, report.Centres.Single(l => l.CentreId == marketing.Id).OwnTotal);
        Assert.Equal(Travel, report.Centres.Single(l => l.CentreId == salesExec1.Id).OwnTotal);
        // …but the cost allocated is still one ₹5,000.37 expense.
        Assert.Equal(Travel, report.GrandTotal);
    }

    [Fact]
    public void Ledger_breakup_reports_the_whole_line_against_each_axis_centre()
    {
        var c = SeedWithParallelPosting(out _, out var kolkata, out _, out var marketing,
            out _, out _, out var travelling);
        var report = CostReports.BuildLedgerBreakup(c, WindowFrom, WindowTo);

        var k = Assert.Single(report.ForCentre(kolkata.Id));
        Assert.Equal(travelling.Id, k.LedgerId);
        Assert.Equal(Travel, k.Total);

        var m = Assert.Single(report.ForCentre(marketing.Id));
        Assert.Equal(travelling.Id, m.LedgerId);
        Assert.Equal(Travel, m.Total);
    }

    [Fact]
    public void Single_axis_books_keep_the_grand_total_they_always_had()
    {
        // Regression guard for the ordinary (one-category) case: the grand total is still the allocated
        // cost, so nothing about existing single-axis reporting moves.
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out var delhi,
            out _, out _, out _, out _, out var payment);
        var svc = new LedgerService(c);

        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(branch.Id, delhi.Id, Money.FromRupees(2000.18m))));
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Money.FromRupees(1234.56m),
            new CostAllocation(branch.Id, delhi.Id, Money.FromRupees(1234.56m))));

        var summary = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);
        Assert.Equal(Money.FromRupees(6234.93m), Assert.Single(summary.Categories).Total);
        Assert.Equal(Money.FromRupees(6234.93m), summary.GrandTotal);

        var breakup = CostReports.BuildCostCentreBreakup(c, WindowFrom, WindowTo);
        Assert.Equal(Money.FromRupees(6234.93m), breakup.GrandTotal);
    }

    [Fact]
    public void Multi_axis_is_flagged_only_when_a_line_really_uses_two_categories()
    {
        // Two SEPARATE single-axis lines are not multi-axis: the flag is per line, not per window.
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel)));
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Money.FromRupees(1234.56m),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(1234.56m))));

        var summary = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);
        Assert.False(summary.CategoryTotalsOverlap);
        // Two different axes, two different lines — the costs DO add up here.
        Assert.Equal(Money.FromRupees(6234.93m), summary.GrandTotal);

        Assert.True(CostReports.BuildCategorySummary(
            SeedWithParallelPosting(out _, out _, out _, out _, out _, out _, out _),
            WindowFrom, WindowTo).CategoryTotalsOverlap);
    }

    [Fact]
    public void A_legacy_partition_book_does_not_claim_the_category_totals_overlap()
    {
        // A book saved under the SUPERSEDED partition rule: ₹3,000.19 Branch + ₹2,000.18 Department on one
        // ₹5,000.37 line. Rehydration admits it (Legacy), and its category rows really DO add up to the
        // grand total — nothing is double-counted. The report must therefore NOT announce that the rows are
        // parallel views that do not add up, because for this book they do.
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);

        new LedgerService(c).Post(
            TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
                new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
                new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m))),
            CostAllocationStrictness.Legacy);

        var summary = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);
        Assert.Equal(Money.FromRupees(3000.19m),
            summary.Categories.Single(r => r.CategoryId == branch.Id).Total);
        Assert.Equal(Money.FromRupees(2000.18m),
            summary.Categories.Single(r => r.CategoryId == department.Id).Total);
        Assert.Equal(Travel, summary.GrandTotal);
        // The rows sum to the total exactly — 3,000.19 + 2,000.18 = 5,000.37.
        Assert.Equal(Travel.Amount, summary.Categories.Sum(r => r.Total.Amount));
        Assert.False(summary.CategoryTotalsOverlap);

        var breakup = CostReports.BuildCostCentreBreakup(c, WindowFrom, WindowTo);
        Assert.Equal(Travel, breakup.GrandTotal);
        Assert.Equal(Travel.Amount, breakup.Centres.Sum(l => l.OwnTotal.Amount));
        Assert.False(breakup.CategoryTotalsOverlap);
    }

    [Fact]
    public void A_window_mixing_a_legacy_partition_with_a_real_parallel_set_still_flags_the_overlap()
    {
        // One legacy line (rows add up) plus one conforming parallel line (rows do not). The window as a
        // whole double-counts, so the flag must fire — the boundary a per-book "legacy ⇒ quiet" rule
        // would get wrong.
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        svc.Post(
            TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
                new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
                new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m))),
            CostAllocationStrictness.Legacy);
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Money.FromRupees(777.77m),
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(777.77m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(777.77m))));

        var summary = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);
        Assert.True(summary.CategoryTotalsOverlap);
        // Cost counted once per line: 5,000.37 + 777.77.
        Assert.Equal(Money.FromRupees(5778.14m), summary.GrandTotal);
        // …and the rows really do NOT add up to it: 3,777.96 + 2,777.95 = 6,555.91.
        Assert.Equal(6555.91m, summary.Categories.Sum(r => r.Total.Amount));
    }

    [Fact]
    public void Ledger_breakup_carries_the_same_once_per_line_total_and_overlap_flag()
    {
        // The third report of the family must not drift from the other two. Its rows are per
        // (centre, ledger) and on a parallel set they sum to ₹15,001.11 for one ₹5,000.37 expense, so it
        // needs the same once-per-line total and the same warning flag — otherwise the next person to add
        // a footer here reproduces exactly the 3x overstatement this change removed.
        var c = SeedWithParallelPosting(out _, out _, out _, out _, out _, out _, out _);
        var report = CostReports.BuildLedgerBreakup(c, WindowFrom, WindowTo);

        Assert.Equal(15001.11m, report.Rows.Sum(r => r.Total.Amount));
        Assert.Equal(Travel, report.GrandTotal);
        Assert.True(report.CategoryTotalsOverlap);
    }

    [Fact]
    public void Cancelled_parallel_set_vouchers_leave_the_reports_empty()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        var v = svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),
            new CostAllocation(department.Id, marketing.Id, Travel)));
        Assert.Equal(Travel, CostReports.BuildCategorySummary(c, WindowFrom, WindowTo).GrandTotal);

        svc.Cancel(v.Id);
        var after = CostReports.BuildCategorySummary(c, WindowFrom, WindowTo);
        Assert.Equal(Money.Zero, after.GrandTotal);
        Assert.False(after.CategoryTotalsOverlap);
        Assert.Empty(after.Categories);
    }

    // ------------------------------------------------------------------ migration: legacy books still open

    [Fact]
    public void Legacy_strictness_still_accepts_a_pre_parallel_set_cross_category_split()
    {
        // Books saved under the OLD partition rule contain lines whose axes only foot when added together.
        // Rehydration (SQLite Load / company import) must never brick such a file, so those paths validate
        // with CostAllocationStrictness.Legacy.
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);

        var legacy = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));

        new LedgerService(c).Post(legacy, CostAllocationStrictness.Legacy);
        Assert.Single(c.Vouchers);
    }

    [Fact]
    public void Legacy_strictness_still_rejects_an_allocation_that_foots_under_neither_rule()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);

        // 1,000.11 + 2,000.18 = 3,000.29 ≠ 5,000.37 — wrong under BOTH rules, so still rejected.
        var junk = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(1000.11m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));

        Assert.Throws<InvalidVoucherException>(
            () => new LedgerService(c).Post(junk, CostAllocationStrictness.Legacy));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Legacy_strictness_is_never_granted_on_the_default_entry_path()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);

        var legacy = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));

        // The parameterless Post — what every UI/service entry path calls — must still refuse it.
        Assert.Throws<InvalidVoucherException>(() => new LedgerService(c).Post(legacy));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Legacy_cross_category_lines_are_listed_for_review()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out _,
            out var department, out var marketing, out _, out _, out var payment);
        var svc = new LedgerService(c);

        var legacy = TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m)));
        svc.Post(legacy, CostAllocationStrictness.Legacy);

        // A conforming parallel-set voucher must NOT be flagged.
        svc.Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Money.FromRupees(777.77m),
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(777.77m)),
            new CostAllocation(department.Id, marketing.Id, Money.FromRupees(777.77m))));

        var row = Assert.Single(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(c));
        Assert.Equal(legacy.Id, row.VoucherId);
        Assert.Equal(travelling.Id, row.LedgerId);
        Assert.Equal(Travel, row.LineAmount);
        Assert.Equal(2, row.ShortCategories.Count);
        Assert.Equal(Money.FromRupees(3000.19m),
            row.ShortCategories.Single(s => s.CategoryId == branch.Id).Allocated);
        Assert.Equal(Money.FromRupees(2000.18m),
            row.ShortCategories.Single(s => s.CategoryId == department.Id).Allocated);
    }

    [Fact]
    public void Single_axis_books_are_never_flagged_for_review()
    {
        var c = Seed(out var cash, out var travelling, out var branch, out var kolkata, out var delhi,
            out _, out _, out _, out _, out var payment);

        new LedgerService(c).Post(TravelVoucher(payment.Id, travelling.Id, cash.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
            new CostAllocation(branch.Id, delhi.Id, Money.FromRupees(2000.18m))));

        Assert.Empty(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(c));
    }
}
