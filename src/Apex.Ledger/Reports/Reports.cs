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

    // ------------------------------------------------------------------ batch reports (Phase 6 Cluster 1)

    /// <summary>The Batch-wise report over a period (Phase 6 Cluster 1; RQ-8) — per item/batch:
    /// inwards/outwards/closing with mfg &amp; expiry. <paramref name="from"/> defaults to
    /// <see cref="Company.BooksBeginFrom"/>; set <paramref name="onlyItemId"/> for a single item and
    /// <paramref name="includeNonBatch"/> to also show the item's non-batch stock.</summary>
    public static BatchwiseReport BuildBatchwiseReport(
        Company c, DateOnly to, DateOnly? from = null, Guid? onlyItemId = null, bool includeNonBatch = false)
        => BatchwiseReport.Build(c, to, from, onlyItemId, includeNonBatch);

    /// <summary>The batch Age Analysis as of a date (Phase 6 Cluster 1; RQ-8) — batches expiring within
    /// <paramref name="withinDays"/> days plus every already-expired batch still holding stock (past-expiry
    /// flagged distinctly).</summary>
    public static BatchAgeAnalysis BuildBatchAgeAnalysis(
        Company c, DateOnly asOf, int withinDays = 30, Guid? onlyItemId = null)
        => BatchAgeAnalysis.Build(c, asOf, withinDays, onlyItemId);

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

    // ------------------------------------------------------------------ TDS/TCS exception reports (slice 8)

    /// <summary>R1 — TDS Outstandings as of a date (Phase 7 slice 8): deducted-but-not-deposited per section.</summary>
    public static TdsOutstandingReport BuildTdsOutstanding(Company c, DateOnly asOf)
        => TdsOutstandingReport.Build(c, asOf);

    /// <summary>R2 — TDS Not Deducted as of a date (Phase 7 slice 8): applicable-but-below-threshold assessments.</summary>
    public static TdsNotDeductedReport BuildTdsNotDeducted(Company c, DateOnly asOf)
        => TdsNotDeductedReport.Build(c, asOf);

    /// <summary>R3 — TDS Interest u/s 201(1A) as of a date (Phase 7 slice 8): 1.5%/month late-deposit interest.</summary>
    public static TdsInterestReport BuildTdsInterest201(Company c, DateOnly asOf)
        => TdsInterestReport.Build(c, asOf);

    /// <summary>R4 — TDS Nature-of-Payment-wise summary as of a date (Phase 7 slice 8).</summary>
    public static TdsNatureSummaryReport BuildTdsNatureSummary(Company c, DateOnly asOf)
        => TdsNatureSummaryReport.Build(c, asOf);

    /// <summary>R5 — TCS Outstandings as of a date (Phase 7 slice 8): collected-but-not-deposited per code.</summary>
    public static TcsOutstandingReport BuildTcsOutstanding(Company c, DateOnly asOf)
        => TcsOutstandingReport.Build(c, asOf);

    /// <summary>R6 — TCS Not Collected as of a date (Phase 7 slice 8): applicable-but-below-threshold sales.</summary>
    public static TcsNotCollectedReport BuildTcsNotCollected(Company c, DateOnly asOf)
        => TcsNotCollectedReport.Build(c, asOf);

    /// <summary>R7 — TCS Interest u/s 206C(7) as of a date (Phase 7 slice 8): 1%/month late-deposit interest.</summary>
    public static TcsInterestReport BuildTcsInterest206C7(Company c, DateOnly asOf)
        => TcsInterestReport.Build(c, asOf);

    /// <summary>R8 — TCS Nature-of-Goods-wise summary as of a date (Phase 7 slice 8).</summary>
    public static TcsNatureSummaryReport BuildTcsNatureSummary(Company c, DateOnly asOf)
        => TcsNatureSummaryReport.Build(c, asOf);

    /// <summary>R9 — Ledgers / parties without PAN as of a date (Phase 7 slice 8).</summary>
    public static LedgersWithoutPanReport BuildLedgersWithoutPan(Company c, DateOnly asOf)
        => LedgersWithoutPanReport.Build(c, asOf);

    // ------------------------------------------------------------------ payroll reports (Phase 8 slice 8)

    /// <summary>A single employee's Payslip for a period (Phase 8 slice 8; RQ-16) — identity, earnings/deductions,
    /// gross/net, employer contributions (informational), attendance summary and YTD; a projection of the posted
    /// Payroll voucher, so it reconciles to the books to the paisa.</summary>
    public static Payslip BuildPayslip(Company c, Guid employeeId, DateOnly from, DateOnly to)
        => Payslip.Build(c, employeeId, from, to);

    /// <summary>The Pay Sheet — an employees × pay-heads matrix with footing column/row totals (Phase 8 slice 8;
    /// RQ-16), projected from the posted Payroll voucher.</summary>
    public static PaySheet BuildPaySheet(Company c, IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to)
        => PaySheet.Build(c, employeeIds, from, to);

    /// <summary>The Payroll Register / Statement — a columnar per-employee salary summary with statutory deductions
    /// broken out + period totals (Phase 8 slice 8; RQ-16), projected from the posted Payroll voucher.</summary>
    public static PayrollRegister BuildPayrollRegister(Company c, IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to)
        => PayrollRegister.Build(c, employeeIds, from, to);

    /// <summary>The Attendance / Production Register — an employees × attendance-types matrix over a period (Phase 8
    /// slice 8; RQ-16).</summary>
    public static AttendanceRegister BuildAttendanceRegister(
        Company c, IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to)
        => AttendanceRegister.Build(c, employeeIds, from, to);

    /// <summary>The Payment / Bank Advice — net pay per employee for a bank transfer (Phase 8 slice 8; RQ-16),
    /// projected from the posted Payroll voucher.</summary>
    public static PaymentAdvice BuildPaymentAdvice(Company c, IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to)
        => PaymentAdvice.Build(c, employeeIds, from, to);
}
