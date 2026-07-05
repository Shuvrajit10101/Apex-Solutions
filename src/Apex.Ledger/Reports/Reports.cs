using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The pure report façade (design §8.4). None of these mutates the company; each returns
/// an immutable record computed over (masters, posted vouchers, as-of date). The methods
/// are named with a <c>Build</c> prefix to avoid colliding with the report record types
/// that share this namespace; callers may equally call each record's own <c>Build</c>.
/// </summary>
public static class Report
{
    public static TrialBalance BuildTrialBalance(Company c, DateOnly asOf)
        => TrialBalance.Build(c, asOf);

    public static ProfitAndLoss BuildProfitAndLoss(
        Company c, DateOnly to, ClosingStockMode mode = ClosingStockMode.AsPostedLedger)
        => ProfitAndLoss.Build(c, to, mode);

    public static BalanceSheet BuildBalanceSheet(Company c, DateOnly asOf)
        => BalanceSheet.Build(c, asOf);

    public static IReadOnlyList<DayBookRow> BuildDayBook(Company c, DateOnly from, DateOnly to)
        => DayBook.Build(c, from, to);

    public static LedgerBook BuildLedgerBook(Company c, Guid ledgerId, DateOnly from, DateOnly to)
        => LedgerBook.Build(c, ledgerId, from, to);

    public static BudgetVarianceReport BuildBudgetVariance(Company c, Budget budget)
        => BudgetVarianceReport.Build(c, budget);

    public static InterestReport BuildInterest(Company c, DateOnly from, DateOnly to)
        => InterestCalculation.Build(c, from, to);

    // ------------------------------------------------------------------ inventory reports (slice 3.4a)

    /// <summary>The Stock Summary over a period (catalog §16; RQ-28). <paramref name="from"/> defaults to
    /// <see cref="Company.BooksBeginFrom"/>.</summary>
    public static StockSummary BuildStockSummary(Company c, DateOnly to, DateOnly? from = null)
        => StockSummary.Build(c, to, from);

    /// <summary>The Godown Summary as of a date (catalog §16; RQ-29).</summary>
    public static GodownSummary BuildGodownSummary(Company c, DateOnly asOf)
        => GodownSummary.Build(c, asOf);

    /// <summary>The Stock Item Movement journal (Stock-Summary drill target) over a period (catalog §16; RQ-28).</summary>
    public static StockItemMovement BuildStockItemMovement(Company c, Guid stockItemId, DateOnly to, DateOnly? from = null)
        => StockItemMovement.Build(c, stockItemId, to, from);

    /// <summary>The Receipt Note (GRN) register over a period (catalog §16; RQ-31).</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildReceiptNoteRegister(Company c, DateOnly from, DateOnly to)
        => InventoryRegisters.BuildReceiptNotes(c, from, to);

    /// <summary>The Delivery Note register over a period (catalog §16; RQ-31).</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildDeliveryNoteRegister(Company c, DateOnly from, DateOnly to)
        => InventoryRegisters.BuildDeliveryNotes(c, from, to);

    /// <summary>The Rejection register (In + Out) over a period (catalog §16; RQ-31).</summary>
    public static IReadOnlyList<InventoryRegisterRow> BuildRejectionRegister(Company c, DateOnly from, DateOnly to)
        => InventoryRegisters.BuildRejections(c, from, to);

    /// <summary>The Physical-Stock register (counted vs book + variance) over a period (catalog §16; RQ-31).</summary>
    public static IReadOnlyList<PhysicalStockRegisterRow> BuildPhysicalStockRegister(Company c, DateOnly from, DateOnly to)
        => InventoryRegisters.BuildPhysicalStock(c, from, to);

    /// <summary>The Order register (Purchase &amp; Sales orders) over a period (catalog §16; RQ-31).</summary>
    public static IReadOnlyList<OrderRegisterRow> BuildOrderRegister(Company c, DateOnly from, DateOnly to)
        => InventoryRegisters.BuildOrders(c, from, to);

    /// <summary>The Reorder Status report as of a date (catalog §16; RQ-33).</summary>
    public static ReorderStatus BuildReorderStatus(Company c, DateOnly asOf)
        => ReorderStatus.Build(c, asOf);

    // ------------------------------------------------------------------ GST reports (slice 4b)

    /// <summary>The GST Tax Analysis period summary (catalog §12; RQ-20) — outward + inward tax by rate/head.</summary>
    public static TaxAnalysis BuildTaxAnalysis(Company c, DateOnly from, DateOnly to)
        => TaxAnalysis.Build(c, from, to);

    /// <summary>GSTR-1 (outward supplies) over a period (catalog §12; RQ-21): B2B/B2C, rate-wise, HSN summary.</summary>
    public static Gstr1 BuildGstr1(Company c, DateOnly from, DateOnly to)
        => Gstr1.Build(c, from, to);

    /// <summary>GSTR-3B (summary return) over a period (catalog §12; RQ-22; DP-9): outward tax, ITC, net payable.</summary>
    public static Gstr3b BuildGstr3b(Company c, DateOnly from, DateOnly to)
        => Gstr3b.Build(c, from, to);
}
