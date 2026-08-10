using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests.Fixtures;

/// <summary>
/// 🔴 THE ONE NON-VACUITY PREDICATE, shared by every test that measures a report on
/// <see cref="PopulatedCompanyFixture"/>. Two test classes previously each rolled their own and BOTH were blind
/// in a different way; this file exists so they cannot drift apart again.
///
/// <para><b>The defect that produced it (measured, not inferred).</b>
/// <see cref="InventoryReportScrollReachabilityTests"/> guarded with <c>Rows.Count &gt; 0</c> and
/// <c>PopulatedFixtureCoverageTests</c> guarded by matching the prose prefix <c>"No "</c>. Neither sees an
/// empty report:</para>
/// <list type="bullet">
///   <item><b><c>Rows.Count &gt; 0</c> is blind to every placeholder empty state.</b> Six of the eleven
///     inventory builders push a single "nothing here" row rather than leaving <c>Rows</c> empty
///     (<c>BuildStockItemMovement</c>, <c>BuildPhysicalStockRegister</c>, <c>BuildReorderStatus</c>,
///     <c>BuildBatchwise</c>, <c>BuildBatchAgeAnalysis</c>, <c>BuildPriceList</c>), and two more
///     (<c>BuildStockSummary</c>, <c>BuildGodownSummary</c>) add a Grand Total unconditionally, outside the
///     data loop. <b>Measured:</b> with the Physical-Stock voucher moved outside the as-of window and every
///     reorder level removed, all <b>44</b> cases of
///     <c>Inventory_report_rows_are_never_stranded_beyond_a_scroller_that_cannot_scroll</c> passed while eight
///     of them measured a one-row pane.</item>
///   <item><b>Prose matching is blind to any fallback that does not start with "No ".</b>
///     <c>BuildReorderStatus</c>'s DEFAULT branch (<c>ReorderOnlyFilter</c> is false on a freshly-constructed
///     view model) renders <c>"All items are above their reorder levels."</c> <b>Measured:</b> with every
///     reorder level removed, <c>Inventory_and_new_family_reports_render_real_rows_on_the_fixture(ReorderStatus)</c>
///     passed over a report containing that one sentence.</item>
/// </list>
///
/// <para><b>Why the check is STRUCTURAL.</b> Every fallback branch in <c>ReportsViewModel</c> marks its
/// placeholder <c>IsHeader = true</c>, and every unconditional structural row is <c>IsHeader</c> (Opening,
/// group header) or <c>IsTotal</c> (Grand Total, Closing). So "does this report contain real content" is
/// exactly "does it contain a row that is neither" — a signal that cannot be defeated by rewording a literal,
/// which prose matching can. <see cref="ReportEmptyStateShapeTests"/> pins that invariant across every report
/// kind these guards drive, so a future builder that emits a bare data row as its empty state fails there
/// rather than silently re-opening this hole.</para>
/// </summary>
internal static class ReportContentGuard
{
    /// <summary>
    /// The rows carrying actual data: neither a structural row (<see cref="ReportRow.IsHeader"/> — group
    /// headers, Opening lines, and every empty-state placeholder) nor a footing
    /// (<see cref="ReportRow.IsTotal"/> — Grand Total, Closing Balance).
    /// </summary>
    internal static IReadOnlyList<ReportRow> DataRows(IEnumerable<ReportRow> rows) =>
        rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();

    /// <summary>
    /// Every empty-state sentence <c>ReportsViewModel</c> can render, enumerated from its fallback branches.
    /// This is a SECONDARY check kept only so a failure names the sentence it found; the structural
    /// <see cref="DataRows"/> count is what actually decides. Note the second family: the Reorder-Status
    /// default branch does NOT start with "No ", which is precisely what defeated the previous predicate.
    /// </summary>
    private static readonly string[] EmptyStateLiterals =
    {
        "All items are above their reorder levels.",          // BuildReorderStatus, F8 filter OFF (the default)
        "Every deductee / collectee party has a PAN.",        // the PAN-exception report's "nothing to report"
    };

    /// <summary>True when this row is one of <c>ReportsViewModel</c>'s "nothing to show" placeholders.</summary>
    internal static bool IsEmptyStatePlaceholder(ReportRow r)
    {
        foreach (var s in new[] { r.Particulars, r.Col1, r.Col2, r.Col4 })
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (s.StartsWith("No ", StringComparison.Ordinal)) return true;
            if (EmptyStateLiterals.Contains(s, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The per-kind DATA-row floor on <see cref="PopulatedCompanyFixture.BuildRegular"/>, every figure MEASURED
    /// on the fixture rather than estimated. Floors are pinned at the measured count for the deterministic
    /// registers (one voucher in, a known number of rows out — that count is stable data, so a drop is a real
    /// regression, and <c>&gt;=</c> keeps them safe against the book growing). The master-driven reports
    /// (Stock Summary, Reorder Status, Godown Summary, Price List, Batch-wise) are floored below their measured
    /// count, because those track master counts that legitimately churn — the floor still fails loudly if the
    /// surface collapses, which is the only thing that makes the layout arithmetic meaningless.
    ///
    /// <para>Measured data rows on the fixture (2026-08-10): StockSummary 28, GodownSummary 14,
    /// StockItemMovement 2, ReorderStatus 28, PhysicalStockRegister 1, OrderRegister 4, ReceiptNoteRegister 2,
    /// JobWorkInOrderBook 1, Batchwise 12, BatchAgeAnalysis 2, PriceList 64, DeliveryNoteRegister 2,
    /// RejectionRegister 2, JobWorkOutOrderBook 1, MaterialInRegister 2, MaterialOutRegister 2,
    /// MemorandumRegister 1, ReversingJournalRegister 1, PosRegister 1.</para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<ReportKind, int> DataRowFloors = new Dictionary<ReportKind, int>
    {
        [ReportKind.StockSummary] = 20,
        [ReportKind.GodownSummary] = 10,
        [ReportKind.StockItemMovement] = 2,   // at least one movement BETWEEN the Opening and Closing lines
        [ReportKind.ReorderStatus] = 20,
        [ReportKind.PhysicalStockRegister] = 1,
        [ReportKind.OrderRegister] = 4,       // the PO's two lines + the SO's two lines
        [ReportKind.ReceiptNoteRegister] = 2,
        [ReportKind.JobWorkInOrderBook] = 1,  // the order header is IsHeader; the component row is the content
        [ReportKind.Batchwise] = 8,
        [ReportKind.BatchAgeAnalysis] = 2,    // the expired lot + the expiring-within-30-days lot
        [ReportKind.PriceList] = 48,
        [ReportKind.DeliveryNoteRegister] = 2,
        [ReportKind.RejectionRegister] = 2,   // one Rejection In + one Rejection Out
        [ReportKind.JobWorkOutOrderBook] = 1,
        [ReportKind.MaterialInRegister] = 2,  // the consumed component + the produced finished good
        [ReportKind.MaterialOutRegister] = 2, // the source leg + the destination leg
        [ReportKind.MemorandumRegister] = 1,
        [ReportKind.ReversingJournalRegister] = 1,
        [ReportKind.PosRegister] = 1,
    };

    /// <summary>
    /// Fails unless <paramref name="rows"/> carries real content: at least the kind's measured data-row floor,
    /// and no empty-state placeholder anywhere in it. <paramref name="where"/> names the surface under
    /// measurement so a failure says which window size / load path produced it.
    /// </summary>
    internal static void RequireRealRows(IReadOnlyList<ReportRow> rows, ReportKind kind, string where)
    {
        var floor = DataRowFloors.TryGetValue(kind, out var f) ? f : 1;
        var data = DataRows(rows);

        Assert.True(
            data.Count >= floor,
            $"{kind} renders {data.Count} DATA row(s) on {where} (floor {floor}, out of {rows.Count} total "
            + "row(s) — the rest are headers, empty-state placeholders and totals), so every assertion driven "
            + "over it is measuring an empty or near-empty pane and cannot fail however broken the pane is.");

        var placeholders = rows.Where(IsEmptyStatePlaceholder).ToList();
        Assert.True(
            placeholders.Count == 0,
            placeholders.Count == 0
                ? string.Empty
                : $"{kind} fell back to its empty state on {where} "
                  + $"(\"{placeholders[0].Particulars}{placeholders[0].Col1}{placeholders[0].Col2}{placeholders[0].Col4}\"), "
                  + "so it exercises nothing.");
    }
}
