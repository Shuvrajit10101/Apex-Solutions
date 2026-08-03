using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// G-2 (spec §4.2 rule C-27) — the Io fold-in for parallel-set cost allocations.
/// <para>Two things are gated here. <b>(a) Lossless round-trip:</b> a line carrying the corpus's parallel
/// allocation — one ₹5,000.37 travelling expense allocated in FULL to Branch → Kolkata <i>and</i> in FULL to
/// Department → Marketing — must export and re-import exact in JSON <i>and</i> XML, byte-stably, into a fresh
/// (differently-Guid'd) company. No canonical-model change was needed: <c>CostAllocationDto</c> already
/// carries its own <c>CategoryId</c>, so a parallel set is simply two allocation elements on one line, and
/// there is no ER-13 exposure at all (no new keys, attributes or elements).</para>
/// <para><b>(b) The migration story:</b> company import is a rehydration path, so it validates with
/// <see cref="CostAllocationStrictness.Legacy"/> — an export taken from books written under the superseded
/// partition rule must still import rather than being refused as data loss. Odd paisa throughout.</para>
/// </summary>
public sealed class CanonicalCostAllocationRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly Money Travel = Money.FromRupees(5000.37m);

    private static Company BuildCostCompany(
        out CostCategory branch, out CostCentre kolkata,
        out CostCategory department, out CostCentre marketing,
        out DomainLedger travelling, out VoucherType journal)
    {
        var c = CompanyFactory.CreateSeeded("Cost Axis Exporter", FyStart);

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        travelling = new DomainLedger(Guid.NewGuid(), "Travelling Expenses",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(travelling);

        branch = new CostCategory(Guid.NewGuid(), "Branch");
        department = new CostCategory(Guid.NewGuid(), "Department");
        c.AddCostCategory(branch);
        c.AddCostCategory(department);
        kolkata = new CostCentre(Guid.NewGuid(), "Kolkata", branch.Id);
        marketing = new CostCentre(Guid.NewGuid(), "Marketing", department.Id);
        c.AddCostCentre(kolkata);
        c.AddCostCentre(marketing);

        journal = c.FindVoucherTypeByName("Journal")!;
        return c;
    }

    private static Voucher TravelVoucher(
        Company c, Guid journalId, Guid travellingId, Money amount, params CostAllocation[] allocations) =>
        new(Guid.NewGuid(), journalId, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(travellingId, amount, DrCr.Debit, billAllocations: null, costAllocations: allocations),
            new EntryLine(c.FindLedgerByName("Cash")!.Id, amount, DrCr.Credit),
        });

    private static Company ImportInto(CanonicalModel model)
    {
        var target = CompanyFactory.CreateSeeded("Fresh Cost Co", FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        return target;
    }

    // ================================================================= (a) lossless round-trip

    [Fact]
    public void ParallelSetCostAllocations_roundTrip_io()
    {
        var c = BuildCostCompany(out var branch, out var kolkata,
            out var department, out var marketing, out var travelling, out var journal);

        // The corpus entry: the WHOLE amount on each of two axes.
        new LedgerService(c).Post(TravelVoucher(c, journal.Id, travelling.Id, Travel,
            new CostAllocation(branch.Id, kolkata.Id, Travel),
            new CostAllocation(department.Id, marketing.Id, Travel)));

        // JSON: byte-stable, and imports into a fresh company with both axes preserved.
        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));      // byte-stable
        AssertParallelSetSurvived(ImportInto(jsonModel!));

        // XML: byte-stable, and imports into a fresh company with both axes preserved.
        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));         // byte-stable
        AssertParallelSetSurvived(ImportInto(xmlModel!));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The imported line carries BOTH allocations, re-minted into the target, each axis totalling the whole
    /// line amount — and the Category Summary counts the cost once (₹5,000.37, not ₹10,000.74).
    /// </summary>
    private static void AssertParallelSetSurvived(Company target)
    {
        var rBranch = target.FindCostCategoryByName("Branch")!;
        var rDepartment = target.FindCostCategoryByName("Department")!;
        var rTravelling = target.FindLedgerByName("Travelling Expenses")!;

        var line = target.Vouchers
            .SelectMany(v => v.Lines)
            .Single(l => l.LedgerId == rTravelling.Id && l.HasCostAllocations);

        Assert.Equal(2, line.CostAllocations.Count);
        Assert.Equal(Travel, line.Amount);
        Assert.Equal(Travel, line.CostAllocationTotalFor(rBranch.Id));
        Assert.Equal(Travel, line.CostAllocationTotalFor(rDepartment.Id));
        Assert.Empty(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(target));

        var summary = CostReports.BuildCategorySummary(target, FyStart, new DateOnly(2025, 3, 31));
        Assert.Equal(Travel, summary.Categories.Single(r => r.CategoryId == rBranch.Id).Total);
        Assert.Equal(Travel, summary.Categories.Single(r => r.CategoryId == rDepartment.Id).Total);
        Assert.Equal(Travel, summary.GrandTotal);
        Assert.True(summary.CategoryTotalsOverlap);
    }

    // ================================================================= (b) the migration story

    [Fact]
    public void An_export_of_pre_parallel_set_books_still_imports_and_is_flagged_for_review()
    {
        var c = BuildCostCompany(out var branch, out var kolkata,
            out var department, out var marketing, out var travelling, out var journal);

        // Exactly what the old partition rule produced: 3,000.19 + 2,000.18 across TWO axes on a 5,000.37
        // line. Built via Legacy because the strict engine — correctly — no longer accepts it from entry.
        new LedgerService(c).Post(
            TravelVoucher(c, journal.Id, travelling.Id, Travel,
                new CostAllocation(branch.Id, kolkata.Id, Money.FromRupees(3000.19m)),
                new CostAllocation(department.Id, marketing.Id, Money.FromRupees(2000.18m))),
            CostAllocationStrictness.Legacy);

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);

        // Refusing to re-import our own export of an older company would be data loss — it must apply.
        var target = ImportInto(model!);

        var rBranch = target.FindCostCategoryByName("Branch")!;
        var rDepartment = target.FindCostCategoryByName("Department")!;
        var line = target.Vouchers.SelectMany(v => v.Lines).Single(l => l.HasCostAllocations);

        // Preserved verbatim — nothing rescaled, nothing dropped, nothing silently "repaired".
        Assert.Equal(Money.FromRupees(3000.19m), line.CostAllocationTotalFor(rBranch.Id));
        Assert.Equal(Money.FromRupees(2000.18m), line.CostAllocationTotalFor(rDepartment.Id));

        // …and surfaced for a human to re-allocate, because only a person knows what was meant.
        var flagged = Assert.Single(CostAllocationDiagnostics.FindLegacyCrossCategoryLines(target));
        Assert.Equal(Travel, flagged.LineAmount);
        Assert.Equal(2, flagged.ShortCategories.Count);
    }
}
