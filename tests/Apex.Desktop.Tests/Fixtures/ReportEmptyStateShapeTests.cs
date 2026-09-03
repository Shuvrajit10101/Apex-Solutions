using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests.Fixtures;

/// <summary>
/// 🔴 WHAT MAKES <see cref="ReportContentGuard"/> UNFOOLABLE — the empty state of every report those guards
/// drive must be STRUCTURALLY detectable, not merely recognisable by its wording.
///
/// <para><b>Why this test and not a list of literals.</b> The previous non-vacuity predicate pattern-matched the
/// prefix <c>"No "</c>, and <c>BuildReorderStatus</c>'s default branch says <c>"All items are above their
/// reorder levels."</c> — so the predicate went silently inert on that report the moment the fixture's reorder
/// levels were gone. Any list of sentences has that failure mode: the next builder reworded, or the next report
/// added, re-opens the hole with nothing failing. This test removes the dependency on prose entirely by
/// asserting the invariant the guards actually rely on: <b>a report with no data produces no DATA row</b> —
/// every "nothing here" placeholder is <see cref="ReportRow.IsHeader"/>, and every unconditional structural row
/// is <c>IsHeader</c> or <see cref="ReportRow.IsTotal"/>.</para>
///
/// <para>Drive it over a company that has been seeded with a chart of accounts and nothing else. If a future
/// builder signals emptiness with a bare data row, this fails here — loudly, naming the kind — rather than
/// quietly making the fixture's coverage locks vacuous.</para>
/// </summary>
public sealed class ReportEmptyStateShapeTests
{
    /// <summary>Every kind <see cref="ReportContentGuard"/> carries a floor for: the guards' whole surface.</summary>
    public static TheoryData<ReportKind> GuardedKinds()
    {
        var data = new TheoryData<ReportKind>();
        foreach (var k in ReportContentGuard.DataRowFloors.Keys.OrderBy(k => k.ToString())) data.Add(k);
        return data;
    }

    [Theory]
    [MemberData(nameof(GuardedKinds))]
    public void An_empty_company_produces_no_data_row_in_any_guarded_report(ReportKind kind)
    {
        // A chart of accounts and nothing else — no stock item, no voucher, no batch, no price list.
        var bare = CompanyFactory.CreateSeeded(
            "Empty Book Private Limited", PopulatedCompanyFixture.FyStart, PopulatedCompanyFixture.FyStart);

        var vm = new ReportsViewModel(bare, kind);
        var data = ReportContentGuard.DataRows(vm.Rows);

        Assert.True(
            data.Count == 0,
            $"{kind} renders {data.Count} DATA row(s) on a company with no data at all. Its empty state is "
            + "therefore INDISTINGUISHABLE from real content by the structural signal every non-vacuity guard "
            + "in this suite relies on, so those guards are inert on it. Mark the fallback row IsHeader = true "
            + $"(as every other builder does). First offending row: "
            + $"\"{data.FirstOrDefault()?.Particulars}{data.FirstOrDefault()?.Col1}{data.FirstOrDefault()?.Col2}\".");
    }

    /// <summary>
    /// The converse, so this file cannot pass by the reports being empty for an unrelated reason: on the
    /// POPULATED fixture every one of the same kinds must clear its floor. This is the pair that makes the
    /// structural signal a genuine discriminator — 0 data rows with no data, ≥ floor data rows with data.
    /// </summary>
    [Theory]
    [MemberData(nameof(GuardedKinds))]
    public void The_populated_fixture_clears_the_floor_for_the_same_report(ReportKind kind)
    {
        var vm = new ReportsViewModel(PopulatedCompanyFixture.BuildRegular(), kind);
        ReportContentGuard.RequireRealRows(vm.Rows, kind, "the populated fixture (in memory)");
    }
}
