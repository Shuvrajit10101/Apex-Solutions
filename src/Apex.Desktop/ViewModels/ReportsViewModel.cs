using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The report kinds surfaced in the reports viewer — the four Phase-1 accounting reports, the
/// nine Phase-3 inventory reports (slice 3.4b), and the three Phase-4 GST reports (slice 4d).</summary>
public enum ReportKind
{
    TrialBalance,
    BalanceSheet,
    ProfitAndLoss,
    DayBook,

    // ---- inventory reports (slice 3.4b) ----
    StockSummary,
    GodownSummary,
    StockItemMovement,
    ReceiptNoteRegister,
    DeliveryNoteRegister,
    RejectionRegister,
    PhysicalStockRegister,
    OrderRegister,
    ReorderStatus,

    // ---- batch reports (Phase 6 Cluster 1 — Reports → Inventory Books → Batch) ----
    Batchwise,
    BatchAgeAnalysis,

    // ---- price list report (Phase 6 slice 5 — Reports → Inventory Books → Price List; RQ-31) ----
    PriceList,

    // ---- GST reports (slice 4d) ----
    TaxAnalysis,
    Gstr1,
    Gstr3b,

    // ---- statements reports (RQ-5 part 1 — Reports → Statements) ----
    CashFlow,
    FundsFlow,
    RatioAnalysis,

    // ---- exception reports (RQ-5 part 2 — Reports → Exception Reports) ----
    NegativeStock,
    NegativeCashBank,
    MemorandumRegister,
    ReversingJournalRegister,

    // ---- POS (Phase 6 slice 7 — RQ-44): the day-close tender view of POS-flagged Sales vouchers (DP-6). ----
    PosRegister,

    // ---- Job Work (Phase 6 slice 8 — RQ-51): the four Job Work registers (Reports → Job Work Reports). ----
    JobWorkInOrderBook,
    JobWorkOutOrderBook,
    MaterialInRegister,
    MaterialOutRegister,

    // ---- Statutory TDS/TCS exception & outstanding reports (Phase 7 slice 8; R1–R9) ----
    // Reports → Statutory Reports → TDS Reports / TCS Reports. Pure read-only projections over the posted
    // TdsLineTax/TcsLineTax withholdings + recorded challans (no schema); surfaced through the wide statutory grids.
    TdsOutstanding,
    TdsNotDeducted,
    TdsInterest,
    TdsNatureSummary,
    TcsOutstanding,
    TcsNotCollected,
    TcsInterest,
    TcsNatureSummary,
    LedgersWithoutPan,

    // ---- Payroll presentation reports (Phase 8 slice 8; RQ-16; catalog §14) ----
    // Reports → Payroll Reports, gated on Payroll. Pure projections over the same PayrollComputationService the
    // payroll voucher posts (no schema, no new persisted data): the Payslip (a single-employee detail + PDF via the
    // PayslipPdf writer) and the four wide tabular reports (Pay Sheet / Payroll Register / Attendance Register /
    // Payment Advice) rendered through a shared dynamic payroll matrix so every figure reconciles to the books.
    Payslip,
    PaySheet,
    PayrollRegister,
    AttendanceRegister,
    PaymentAdvice,

    // ---- W2-12: the report families of census rows 11.6 / 11.7 / 11.8 ----
    // Reports → Account Books → Registers: the five accounting registers (11.6). Each opens MONTH-WISE and
    // drills to that month's voucher-wise listing — a register is NOT a filtered Day Book, and building one
    // that way is the trap this row sat on for three census passes.
    SalesRegister,
    PurchaseRegister,
    JournalRegister,
    CreditNoteRegister,
    DebitNoteRegister,

    // Reports → Account Books → Groups (11.7). Both are scoped to one group, picked from a cascade column.
    GroupSummary,
    GroupVouchers,

    // The Ledger Monthly Summary — census T1-32, the level the whole Account Books family was missing.
    // It is Group Summary's documented drill target (group → ledger → MONTHLY SUMMARY → vouchers), which is
    // why it lands with 11.7 rather than waiting for 11.5: a flat group→ledger→voucher jump would have
    // shipped 11.5's defect a second time.
    LedgerMonthlySummary,

    // Reports → Statements of Accounts → Statistics (11.8).
    Statistics,

    // ---- Wave 7 D1: Banking documents (census rows 8.4 / 8.7) ----
    // Transactions → Banking. Both are ReportKinds rather than bespoke page Screens ON PURPOSE: a page Screen
    // leaves the report context null, and that single fact switches off Ctrl+P, export, F2 period, F12 config,
    // Alt+F12 sort/filter and Alt+K saved views at once — the defect that hollowed out rows 8.1, 11.9, 11.10
    // and 11.11 (docs/full-clone-census.md:612). A banking document that cannot be PRINTED is not a document.

    /// <summary>help.tallysolutions.com/print-cheques/, "Cheque Printing Report" — the cheques pending for
    /// printing on a bank, drilling to the paying voucher where Ctrl+P inks the leaf.</summary>
    ChequePrinting,

    /// <summary>help.tallysolutions.com/payment-advice/ — the advices for payments made to SUPPLIERS. Distinct
    /// from <see cref="PaymentAdvice"/>, which is the payroll bank advice for EMPLOYEES; the two are different
    /// documents for different counterparties and must not be conflated.</summary>
    SupplierPaymentAdvice,

    // ---- Wave J2: the rest of the banking documents (census rows 8.5 / 8.6) ----

    /// <summary>help.tallysolutions.com/cheque-register/ and
    /// help.tallysolutions.com/docs/te9rel65/Banking/Cheque_Register.htm — one row per cheque BOOK with the six
    /// status counts, drilling to the leaf list.</summary>
    ChequeRegister,

    /// <summary>The leaf-by-leaf half of the Cheque Register ("View More Details in Cheque Register"): one row
    /// per cheque number, showing which bucket it falls in and, when it has been issued, the paying voucher.</summary>
    ChequeRegisterDetail,

    /// <summary>help.tallysolutions.com/deposit-slips/ — the bank pay-in slip, in the vendor's two modes (Cash
    /// Deposit Slip and Cheque Deposit Slip), switched on F5.</summary>
    DepositSlip,

    /// <summary>help.tallysolutions.com/e-payments-report/ (census row 8.10) — the electronic payments waiting to
    /// go to the bank, in the vendor's sections, with Ctrl+A exporting the payment-instruction file.</summary>
    EPayments,

    // ---- W7-D2: the PF statutory forms beyond the ECR (census row 7.20) ----
    // Reports → Statutory Reports → Payroll → Provident Fund. Pure re-presentations of the SAME PfEcr projection
    // the ECR and the challan come from (Apex.Ledger/Reports/PfStatutoryForms.cs) — no new PF arithmetic. Forms 3A
    // and 6A run over the 1-Mar…28/29-Feb CURRENCY PERIOD (not the financial year); Forms 5, 10 and 12A are
    // monthly. Read PfStatutoryForms' type doc before changing any column: several print deliberately blank.
    PfForm3A,
    PfForm5,
    PfForm6A,
    PfForm10,
    PfForm12A,

    // ---- W7-D2: the ESI statutory forms beyond the monthly contribution file (census row 7.21) ----
    // Reports → Statutory Reports → Payroll → Employee State Insurance. Form 3 is monthly (Reg. 14); Forms 5
    // (Reg. 26) and 6 (Reg. 32(1)) run over the Apr–Sep / Oct–Mar CONTRIBUTION PERIOD.
    EsiForm3,
    EsiForm5,
    EsiForm6,

    // ---- W-J1: the five payroll reports of census rows 7.22–7.26 (added by user ruling 19 because they had no
    // census row at all). Every one is a REPORT over payroll data this product already computes — none needs
    // storage and none computes a new figure. ----

    /// <summary>Census 7.22 — the Attendance <b>Sheet</b>: the fixed four-figure per-employee summary (days
    /// present / days absent / units produced / overtime),
    /// help.tallysolutions.com/tally-prime/payroll-reports/attendance-sheet-payroll/.
    /// 🔴 <b>NOT <see cref="AttendanceRegister"/> (row 7.15)</b>, which is the wide per-type matrix. The vendor
    /// publishes the two on separate pages and folding them is the trap row 7.22 exists to close.</summary>
    AttendanceSheet,

    /// <summary>Census 7.23 — Pay Head Employee Breakup: ONE employee, group-wise across their pay heads,
    /// help.tallysolutions.com/tally-prime/payroll-reports/pay-head-employee-breakup-tally/.</summary>
    PayHeadEmployeeBreakup,

    /// <summary>Census 7.24 — Employee Pay Head Breakup: ONE pay head, across all employees,
    /// help.tallysolutions.com/tally-prime/payroll-reports/payroll-employee-pay-head-breakup-tally/. The transpose
    /// of <see cref="PayHeadEmployeeBreakup"/>, over the same engine so neither can answer for the other.</summary>
    EmployeePayHeadBreakup,

    /// <summary>Census 7.25 — Payroll Statutory Summary: the payable/paid roll-up OVER the PF/ESI/PT computations
    /// (rows 7.10/7.11/7.12 ship those),
    /// help.tallysolutions.com/tally-prime/payroll-statutory-reports/payroll-statutory-summary-tally/.</summary>
    PayrollStatutorySummary,

    /// <summary>Census 7.26 — the per-employee Income Tax Computation in Form 16 shape,
    /// help.tallysolutions.com/tally-prime/payroll-income-tax-reports/tax-computation-tally/. Reads the SAME
    /// annual computation that backs Form 16 Part B; it computes no tax of its own.</summary>
    IncomeTaxComputation,

    // ---- W-K1: the inventory costing & tracking reports of census rows 9.8 / 9.7 / 9.6 ----
    // Every one is a ReportKind rather than a bespoke page Screen ON PURPOSE — a page Screen leaves the report
    // context null and switches off Ctrl+P, export, F2 period, F12 config, Alt+F12 sort/filter and Alt+K saved
    // views all at once (docs/full-clone-census.md:612). Reports an operator cannot print or export are the
    // hollowed-out shape rows 8.1/11.9/11.10/11.11 were caught in.

    /// <summary>Census 9.8 — <b>Purchase Bills Pending</b>: Receipt Notes netted against Purchase invoices by
    /// Tracking No., in the vendor's two sections "Goods Recd. but Bills not Recd." and "Bills Recd. but Goods
    /// not Recd.", help.tallysolutions.com/purchase-order-tally/.</summary>
    PurchaseBillsPending,

    /// <summary>Census 9.8 — <b>Sales Bills Pending</b>: the Delivery Note ↔ Sales twin of
    /// <see cref="PurchaseBillsPending"/>, help.tallysolutions.com/sales-order-tally/.</summary>
    SalesBillsPending,

    /// <summary>Census 9.7 — <b>Stock Item Cost Analysis</b>: per item, Cost (Expense) / Revenue (Income) /
    /// Balance at Cost / Profit-Loss over its cost tracking numbers,
    /// help.tallysolutions.com/tally-prime/inventory/track-item-cost-tally/.</summary>
    StockItemCostAnalysis,

    /// <summary>Census 9.7 — <b>Stock Group Cost Analysis</b>: the same four columns rolled up to the stock
    /// group.</summary>
    StockGroupCostAnalysis,

    /// <summary>Census 9.7 — <b>Cost Track Break-up</b>: one row per (item, cost tracking number), the finest
    /// grain the feature has.</summary>
    CostTrackBreakup,

    /// <summary>Census 9.6 — <b>Job Work Analysis</b>: per job/project (a cost centre a godown designates), the
    /// Revenue (Income) and Cost (Expenses) lines and the Nett Profit/Loss,
    /// help.tallysolutions.com/job-costing-tally/.</summary>
    JobWorkAnalysis,
}

/// <summary>
/// Builds the report content for the current company, reading the numbers
/// straight from the <see cref="Apex.Ledger.Reports"/> pure projections. The as-of date is the
/// last voucher date (or the financial-year end when there are no vouchers), so a freshly loaded
/// demo shows its full picture. Exposes the row lists the report views bind to.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly Company _company;

    /// <summary>The books-begin default as-of (last voucher date, or FY end when empty). Used to reset the
    /// period back to the legacy default (RQ-1) and as the fallback whenever no period is chosen.</summary>
    private readonly DateOnly _defaultAsOf;

    /// <summary>
    /// The Phase-5 report parameters (RQ-1 as-of/period, RQ-2 detailed↔summary, RQ-6 F12 config). Every Build*
    /// method reads from here, so changing it and re-running <see cref="Show"/> re-projects the report. It
    /// starts at the legacy default (as-of = last voucher date, detailed, nothing hidden, no percentages,
    /// closing stock as-posted), so an untouched report is byte-for-byte the pre-slice behaviour.
    /// </summary>
    private ReportOptions _options;

    /// <summary>
    /// The Phase-5 slice-2 report VIEW (RQ-3 sort &amp; filter). It is a pure view carried alongside the
    /// already-built rows: sort re-orders the row-bearing sections and filter hides out-of-range/name-mismatched
    /// rows, but neither adds, drops, nor recomputes any figure and it never touches a report's Grand Total
    /// (which is computed by the engine <c>Build</c> over the FULL set, then rendered untouched). It starts at
    /// <see cref="ReportSortFilter.None"/>, so an untouched report is byte-for-byte the pre-slice output.
    /// </summary>
    private ReportSortFilter _sortFilter = ReportSortFilter.None;

    /// <summary>
    /// The W2-13a Ctrl+B "Basis of Values" <b>Scale Factor</b> (census row 14.5). Like
    /// <see cref="_sortFilter"/> it is a pure PRESENTATION value carried alongside the engine options and
    /// never handed to the engine: the projection is built from the unscaled books and the divide is applied
    /// on the way into a row cell, so percentages, sort magnitudes and every Grand Total stay engine-computed.
    /// It starts at <see cref="ReportScale.Default"/>, so an untouched report is byte-for-byte the pre-slice
    /// output.
    /// </summary>
    private ReportScale _scale = ReportScale.Default;

    /// <summary>The effective as-of upper bound for the current report — the chosen period end, or the default.</summary>
    private DateOnly _asOf => _options.Period?.To ?? _options.AsOfDate;

    /// <summary>The stock item a Stock Item Movement report is scoped to (null for the other reports).</summary>
    private Guid? _movementItemId;

    /// <summary>
    /// The master a W2-12 scoped report hangs off: the accounting GROUP for
    /// <see cref="ReportKind.GroupSummary"/> / <see cref="ReportKind.GroupVouchers"/>, or the LEDGER for
    /// <see cref="ReportKind.LedgerMonthlySummary"/>. <see cref="Guid.Empty"/> for every other kind, and the
    /// scoped builders render an explanatory empty state rather than throwing when it is.
    /// </summary>
    private readonly Guid _scopeMasterId;

    /// <summary>
    /// True when a register report is showing its <b>drilled</b> (voucher-wise) level rather than its
    /// month-wise top level. Set only by the shell when it opens a month row's drill target, together with
    /// that month's period — so the two levels are two panes of the Miller cascade and Esc pops back to the
    /// month list for free.
    /// </summary>
    private readonly bool _registerVoucherLevel;

    [ObservableProperty] private ReportKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>
    /// 🔴 <b>RULING 18 provenance seam, producer side.</b> True when <see cref="Title"/> was built by concatenating
    /// a product-authored label with a MASTER NAME the user typed — "Ledger Monthly Summary — {ledger}", "Group
    /// Summary — {group}". Only this class, which builds the title, knows that; a downstream renderer handed the
    /// finished string cannot tell our heading from a customer's legal name, so it is stated here and carried to
    /// <see cref="Apex.Ledger.Io.PrintReport.TitleCarriesMasterName"/> (the PDF) and
    /// <see cref="Apex.Ledger.Io.TabularExport.TitleCarriesMasterName"/> (HTML / XML / JSON / XLSX) by the two
    /// projectors, so one flag decides all five formats and they cannot drift.
    ///
    /// <para><b>It is reset to <see langword="false"/> on every rebuild, before the builder for the new kind runs</b>
    /// — so a report that never sets it keeps the ER-11 guard on its heading. The fail-safe direction is the
    /// guarded one: forgetting the flag scrubs our own text (harmless), never leaks the brand.</para>
    /// </summary>
    [ObservableProperty] private bool _titleCarriesMasterName;
    [ObservableProperty] private bool _isTwoColumn; // Dr/Cr grid (TB, BS) vs single-amount (P&L, DayBook)

    public ObservableCollection<ReportRow> Rows { get; } = new();

    /// <summary>
    /// True when the current report projected NO rows at all — drives the reusable empty-state (UI-defect C7).
    /// Reports that always foot a Grand-Total (TB / BS / P&amp;L) are never empty in this sense; the register
    /// reports that used to park a "No entries…" message into a starved column now leave Rows empty and let the
    /// shared <c>EmptyState</c> component render the message across the whole body instead.
    /// <para>G1 fix: the payroll reports (Pay Sheet / Payroll Register / Attendance / Payment Advice / Payslip)
    /// keep their data in <see cref="PayrollRows"/> / the Payslip* collections, NOT in <see cref="Rows"/>, so a
    /// populated payroll matrix had <c>Rows.Count == 0</c> and the shared C7 overlay wrongly painted "No entries…"
    /// OVER the matrix. Payroll reports carry their OWN in-pane empty note (<see cref="IsPayrollEmpty"/> /
    /// <see cref="IsPayslipEmpty"/>), so they are excluded from the shared overlay entirely — for a payroll report
    /// this is always false and the C7 EmptyState never covers the payroll pane (a genuinely-empty payroll report
    /// still shows its own note). Note <see cref="PayrollRows"/> is unreliable as an emptiness signal because the
    /// matrix always foots a Grand-Total row even when there are no employees.</para>
    /// </summary>
    public bool IsEmpty => !IsPayrollReport && Rows.Count == 0;

    /// <summary>The one-line message the empty-state shows for a report with no data.</summary>
    public string EmptyMessage => "No entries for the selected period.";

    /// <summary>
    /// The highlighted grid row (two-way bound to the accounting/Stock-Summary ListBox <c>SelectedItem</c>).
    /// The shell reads this on Enter so the keyboard drill does not depend on which control holds focus (RQ-7
    /// defect-1): pressing Enter drills <see cref="SelectedRow"/> when it is drillable.
    /// </summary>
    [ObservableProperty] private ReportRow? _selectedRow;

    // ---- inventory-report layout flags (drive which DataTemplate the view shows; slice 3.4b) ----

    /// <summary>True for any of the nine inventory reports (they use the wide inventory grids, not the
    /// accounting Particulars/Dr/Cr/Amount grid).</summary>
    public bool IsInventoryReport => Kind is ReportKind.StockSummary or ReportKind.GodownSummary
        or ReportKind.StockItemMovement or ReportKind.ReceiptNoteRegister or ReportKind.DeliveryNoteRegister
        or ReportKind.RejectionRegister or ReportKind.PhysicalStockRegister or ReportKind.OrderRegister
        or ReportKind.ReorderStatus or ReportKind.Batchwise or ReportKind.BatchAgeAnalysis
        or ReportKind.PriceList
        or ReportKind.JobWorkInOrderBook or ReportKind.JobWorkOutOrderBook
        or ReportKind.MaterialInRegister or ReportKind.MaterialOutRegister
        // W-K1 (census 9.8 / 9.7 / 9.6): the six inventory costing & tracking reports. They belong to THIS
        // family, not the accounting one — omitting them here leaves the accounting Particulars/Dr/Cr grid
        // showing beside their own grid and the screen renders two tables at once.
        or ReportKind.PurchaseBillsPending or ReportKind.SalesBillsPending
        or ReportKind.StockItemCostAnalysis or ReportKind.StockGroupCostAnalysis
        or ReportKind.CostTrackBreakup or ReportKind.JobWorkAnalysis;

    /// <summary>True for either <b>Bills Pending</b> report (census 9.8) — both use the same six-column grid,
    /// because the two are the same netting with the goods and bill sides swapped.</summary>
    public bool IsBillsPending =>
        Kind is ReportKind.PurchaseBillsPending or ReportKind.SalesBillsPending;

    /// <summary>True for any of the three <b>Item Cost Analysis</b> reports (census 9.7) — they share the
    /// vendor's four money columns, so one grid serves all three.</summary>
    public bool IsItemCostAnalysis => Kind is ReportKind.StockItemCostAnalysis
        or ReportKind.StockGroupCostAnalysis or ReportKind.CostTrackBreakup;

    /// <summary>True for <b>Job Work Analysis</b> (census 9.6).</summary>
    public bool IsJobWorkAnalysis => Kind == ReportKind.JobWorkAnalysis;

    /// <summary>True for any of the three Phase-4 GST reports (they use their own wide GST grids, slice 4d).</summary>
    public bool IsGstReport => Kind is ReportKind.TaxAnalysis or ReportKind.Gstr1 or ReportKind.Gstr3b;

    /// <summary>True for any of the nine Phase-7 slice-8 statutory TDS/TCS reports (they use their own wide
    /// statutory grids: outstandings, not-deducted/collected, interest, nature summary, ledgers-without-PAN).</summary>
    public bool IsStatutoryReport => Kind is ReportKind.TdsOutstanding or ReportKind.TdsNotDeducted
        or ReportKind.TdsInterest or ReportKind.TdsNatureSummary
        or ReportKind.TcsOutstanding or ReportKind.TcsNotCollected
        or ReportKind.TcsInterest or ReportKind.TcsNatureSummary or ReportKind.LedgersWithoutPan;

    /// <summary>True to show the accounting (Particulars/Dr/Cr/Amount) grid — a report that is neither
    /// inventory, GST, statutory, nor payroll (TB / BS / P&amp;L / Day Book + the accounting exception reports).</summary>
    public bool IsAccountingReport => !IsInventoryReport && !IsGstReport && !IsStatutoryReport && !IsPayrollReport;

    // ---- statutory-report layout flags (drive which statutory DataTemplate the view shows; Phase 7 slice 8) ----
    // The TDS and TCS reports mirror each other, so each pair shares a column layout; the header labels that differ
    // (Section↔Coll. Code, Deducted↔Collected, Deduction↔Collection Date, TDS↔TCS, @1.5%↔@1%) come from the
    // Stat*Header properties below so one DataTemplate serves both members of a pair.

    /// <summary>True for the TDS/TCS Outstandings reports (R1/R5): Section/Code | Nature | Deducted/Collected |
    /// Deposited | Outstanding | Overdue days.</summary>
    public bool IsStatutoryOutstanding => Kind is ReportKind.TdsOutstanding or ReportKind.TcsOutstanding;

    /// <summary>True for the TDS Not-Deducted / TCS Not-Collected reports (R2/R6): the below-threshold
    /// assessments.</summary>
    public bool IsStatutoryNotWithheld => Kind is ReportKind.TdsNotDeducted or ReportKind.TcsNotCollected;

    /// <summary>True for the TDS §201(1A) / TCS §206C(7) interest reports (R3/R7).</summary>
    public bool IsStatutoryInterest => Kind is ReportKind.TdsInterest or ReportKind.TcsInterest;

    /// <summary>True for the TDS Nature-of-Payment / TCS Nature-of-Goods summary reports (R4/R8).</summary>
    public bool IsStatutoryNatureSummary => Kind is ReportKind.TdsNatureSummary or ReportKind.TcsNatureSummary;

    /// <summary>True for the Ledgers/Parties-without-PAN report (R9) — its own five-column layout.</summary>
    public bool IsLedgersWithoutPan => Kind == ReportKind.LedgersWithoutPan;

    /// <summary>True for the TCS side of a shared statutory template (drives the Stat*Header label choices).</summary>
    private bool IsTcsKind => Kind is ReportKind.TcsOutstanding or ReportKind.TcsNotCollected
        or ReportKind.TcsInterest or ReportKind.TcsNatureSummary;

    /// <summary>The first-column header on a shared statutory grid: "Section" (TDS) or "Coll. Code" (TCS).</summary>
    public string StatCodeHeader => IsTcsKind ? "Coll. Code" : "Section";

    /// <summary>The withheld-amount header on a shared statutory grid: "Deducted" (TDS) or "Collected" (TCS).</summary>
    public string StatWithheldHeader => IsTcsKind ? "Collected" : "Deducted";

    /// <summary>The event-date header on the interest grid: "Ded. Date" (TDS) or "Coll. Date" (TCS) — abbreviated
    /// to fit the narrow date column without clipping.</summary>
    public string StatEventDateHeader => IsTcsKind ? "Coll. Date" : "Ded. Date";

    /// <summary>The tax-amount header on the interest grid: "TDS" or "TCS".</summary>
    public string StatTaxHeader => IsTcsKind ? "TCS" : "TDS";

    /// <summary>The interest header on the interest grid: "Interest @1.5%" (TDS §201(1A)(ii)) or "Interest @1%"
    /// (TCS §206C(7)).</summary>
    public string StatInterestHeader => IsTcsKind ? "Interest @1%" : "Interest @1.5%";

    /// <summary>The one-line footnote shown beneath the TDS interest report — only the §201(1A)(ii) late-deposit
    /// limb is computed; the §201(1A)(i) late-deduction limb (1%) is not computable in this model. Empty for every
    /// other report, including TCS §206C(7) which is a single, fully-computed limb.</summary>
    public string StatutoryFootnote => Kind == ReportKind.TdsInterest ? TdsInterestReport.Footnote : string.Empty;

    /// <summary>True when <see cref="StatutoryFootnote"/> is non-empty (drives the footnote's visibility).</summary>
    public bool HasStatutoryFootnote => !string.IsNullOrEmpty(StatutoryFootnote);

    // The three single-column grids hide when the comparative (RQ-4) multi-column grid is showing; these
    // composite flags keep the XAML visibility bindings simple (a plain flag rather than an "A && !B" expression).

    /// <summary>Show the single-column accounting grid — an accounting report NOT currently in comparative mode.</summary>
    public bool ShowSingleAccountingGrid => IsAccountingReport && !IsComparative;

    /// <summary>Show the single-column inventory grids — an inventory report NOT currently in comparative mode.</summary>
    public bool ShowSingleInventoryGrid => IsInventoryReport && !IsComparative;

    /// <summary>Show the GST grids — a GST report (GST reports are never comparative).</summary>
    public bool ShowGstGrid => IsGstReport;

    public bool IsStockSummary => Kind == ReportKind.StockSummary;
    public bool IsGodownSummary => Kind == ReportKind.GodownSummary;
    public bool IsStockMovement => Kind == ReportKind.StockItemMovement;
    public bool IsReorderStatus => Kind == ReportKind.ReorderStatus;
    public bool IsPhysicalStockRegister => Kind == ReportKind.PhysicalStockRegister;
    public bool IsOrderRegister => Kind == ReportKind.OrderRegister;

    /// <summary>True for the Batch-wise report (Phase 6 Cluster 1; RQ-8) — drives its wide batch DataTemplate.</summary>
    public bool IsBatchwise => Kind == ReportKind.Batchwise;

    /// <summary>True for the batch Age Analysis report (Phase 6 Cluster 1; RQ-8) — drives its wide batch DataTemplate.</summary>
    public bool IsBatchAgeAnalysis => Kind == ReportKind.BatchAgeAnalysis;

    /// <summary>True for the Price List report (Phase 6 slice 5; RQ-31) — drives its wide price-list DataTemplate.</summary>
    public bool IsPriceList => Kind == ReportKind.PriceList;

    // ---- GST-report layout flags (drive which GST DataTemplate the view shows; slice 4d) ----
    public bool IsTaxAnalysis => Kind == ReportKind.TaxAnalysis;
    public bool IsGstr1 => Kind == ReportKind.Gstr1;
    public bool IsGstr3b => Kind == ReportKind.Gstr3b;

    /// <summary>True for the three allocation registers (Receipt Note / Delivery Note / Rejection), which
    /// share the same wide Date | No. | Party | Item | Godown | Qty | Rate | Value | Batch layout.</summary>
    public bool IsAllocationRegister => Kind is ReportKind.ReceiptNoteRegister
        or ReportKind.DeliveryNoteRegister or ReportKind.RejectionRegister
        or ReportKind.MaterialInRegister or ReportKind.MaterialOutRegister;

    /// <summary>True for the two Job Work Order Books (Phase 6 slice 8; RQ-51) — drives the order-book
    /// DataTemplate (order header rows + tracked component rows with their pending figures).</summary>
    public bool IsJobWorkOrderBook => Kind is ReportKind.JobWorkInOrderBook or ReportKind.JobWorkOutOrderBook;

    // ---- Payroll-report layout flags (drive which payroll DataTemplate the view shows; Phase 8 slice 8) ----

    /// <summary>True for any of the five Phase-8 slice-8 payroll presentation reports (Payslip / Pay Sheet /
    /// Payroll Register / Attendance Register / Payment Advice) — they use their own payroll screens, not the
    /// accounting / inventory / GST / statutory grids, and carry the wage-month picker.</summary>
    public bool IsPayrollReport => Kind is ReportKind.Payslip or ReportKind.PaySheet
        or ReportKind.PayrollRegister or ReportKind.AttendanceRegister or ReportKind.PaymentAdvice
        || IsPayrollStatutoryForm || IsPayrollBreakupReport;

    /// <summary>True for the five W-J1 payroll reports of census rows 7.22–7.26 (Attendance Sheet · Pay Head
    /// Employee Breakup · Employee Pay Head Breakup · Payroll Statutory Summary · Income Tax Computation). They
    /// render through the same payroll matrix as everything else in this family and are scoped to a wage month;
    /// three of them additionally carry a scope picker (employee or pay head).</summary>
    public bool IsPayrollBreakupReport => Kind is ReportKind.AttendanceSheet
        or ReportKind.PayHeadEmployeeBreakup or ReportKind.EmployeePayHeadBreakup
        or ReportKind.PayrollStatutorySummary or ReportKind.IncomeTaxComputation;

    /// <summary>True for the eight W7-D2 payroll statutory forms (PF 3A/5/6A/10/12A — census 7.20; ESI 3/5/6 —
    /// census 7.21). They render through the same payroll matrix as the four presentation reports, so one
    /// DataTemplate serves all twelve; they differ only in which period picker they carry.</summary>
    public bool IsPayrollStatutoryForm => Kind is ReportKind.PfForm3A or ReportKind.PfForm5 or ReportKind.PfForm6A
        or ReportKind.PfForm10 or ReportKind.PfForm12A
        or ReportKind.EsiForm3 or ReportKind.EsiForm5 or ReportKind.EsiForm6;

    /// <summary>True for the statutory forms scoped to a MULTI-MONTH statutory period rather than a wage month:
    /// PF Forms 3A and 6A (the 1-Mar…28/29-Feb currency period) and ESI Forms 5 and 6 (the Apr–Sep / Oct–Mar
    /// contribution period). They carry <see cref="StatutoryPeriods"/> instead of the wage-month picker — the
    /// vendor's own pickers offer exactly these windows and no arbitrary date.</summary>
    public bool IsStatutoryPeriodForm => Kind is ReportKind.PfForm3A or ReportKind.PfForm6A
        or ReportKind.EsiForm5 or ReportKind.EsiForm6;

    /// <summary>True for the single-employee Payslip (RQ-16) — its own detail layout + employee picker + PDF.</summary>
    public bool IsPayslipReport => Kind == ReportKind.Payslip;

    /// <summary>True for the four wide tabular payroll reports (Pay Sheet / Payroll Register / Attendance Register /
    /// Payment Advice) — all rendered through the shared, horizontally-scrolling <see cref="PayrollColumns"/> /
    /// <see cref="PayrollRows"/> matrix so one DataTemplate serves every payroll grid.</summary>
    public bool IsPayrollMatrix => Kind is ReportKind.PaySheet or ReportKind.PayrollRegister
        or ReportKind.AttendanceRegister or ReportKind.PaymentAdvice
        || IsPayrollStatutoryForm || IsPayrollBreakupReport;

    /// <summary>Show the wage-month picker — every payroll report is scoped to one wage month, EXCEPT the four
    /// statutory forms that run over a multi-month statutory period (see <see cref="IsStatutoryPeriodForm"/>).
    /// <para>🔴 Also excluded: the one report that carries <see cref="ShowPayrollPayHeadPicker"/>. That picker's
    /// row carries its OWN wage-month combo, and both rows live in the same grid cell — leaving this true would
    /// stack two pickers on top of each other in the same space.</para></summary>
    public bool ShowPayrollMonthPicker =>
        IsPayrollReport && !IsStatutoryPeriodForm && !ShowPayrollPayHeadPicker;

    /// <summary>Show the statutory-period picker (PF currency period / ESI contribution period).</summary>
    public bool ShowStatutoryPeriodPicker => IsStatutoryPeriodForm;

    /// <summary>Show the employee picker — the Payslip, the Pay Head Employee Breakup (census 7.23, whose vendor
    /// page opens on a <i>List of Employees</i>) and the Income Tax Computation (census 7.26, whose vendor page
    /// switches employee with F4) are each scoped to a single employee.</summary>
    public bool ShowPayrollEmployeePicker => IsPayslipReport
        || Kind is ReportKind.PayHeadEmployeeBreakup or ReportKind.IncomeTaxComputation;

    /// <summary>Show the pay-head picker — only the Employee Pay Head Breakup (census 7.24) is scoped to a single
    /// pay head, and its vendor page opens on a <i>List of Pay Heads</i>. It is a separate picker from the
    /// employee one on purpose: 7.23 and 7.24 transpose the same data, and a shared picker would let one silently
    /// answer for the other.</summary>
    public bool ShowPayrollPayHeadPicker => Kind == ReportKind.EmployeePayHeadBreakup;

    /// <summary>The company's pay heads, for the census-7.24 pay-head picker (ordered by display label).</summary>
    public ObservableCollection<PayrollPayHeadOption> PayrollPayHeads { get; } = new();

    /// <summary>The selected pay head; changing it re-projects the Employee Pay Head Breakup.</summary>
    [ObservableProperty] private PayrollPayHeadOption? _selectedPayrollPayHead;

    partial void OnSelectedPayrollPayHeadChanged(PayrollPayHeadOption? value)
    {
        if (Kind == ReportKind.EmployeePayHeadBreakup) Show(Kind);
    }

    /// <summary>The selectable wage months of the report's financial year (Apr … Mar), driving the payroll period.</summary>
    public ObservableCollection<PayrollMonthOption> PayrollMonths { get; } = new();

    /// <summary>The company's employees, for the Payslip employee picker (ordered by name then number).</summary>
    public ObservableCollection<PayrollEmployeeOption> PayrollEmployees { get; } = new();

    /// <summary>The selectable statutory periods for the four multi-month forms — the PF currency periods
    /// (1 Mar … 28/29 Feb) for Forms 3A / 6A and the ESI contribution periods (Apr–Sep / Oct–Mar) for ESI Forms
    /// 5 / 6. Rebuilt on every <see cref="Show"/> because the two families offer different windows.</summary>
    public ObservableCollection<StatutoryPeriodOption> StatutoryPeriods { get; } = new();

    /// <summary>The selected statutory period; changing it re-projects the current statutory form.</summary>
    [ObservableProperty] private StatutoryPeriodOption? _selectedStatutoryPeriod;

    partial void OnSelectedStatutoryPeriodChanged(StatutoryPeriodOption? value)
    {
        if (IsStatutoryPeriodForm && !_rebuildingStatutoryPeriods) Show(Kind);
    }

    /// <summary>Guards the period list rebuild inside <see cref="Show"/> from re-entering it through the selection
    /// change it necessarily causes — the same shape as the ctor's "Kind is not yet payroll" guard below.</summary>
    private bool _rebuildingStatutoryPeriods;

    /// <summary>The selected wage month; changing it re-projects the current payroll report for that month.</summary>
    [ObservableProperty] private PayrollMonthOption? _selectedPayrollMonth;

    /// <summary>The selected employee for the Payslip; changing it re-projects the payslip for that employee.</summary>
    [ObservableProperty] private PayrollEmployeeOption? _selectedPayrollEmployee;

    partial void OnSelectedPayrollMonthChanged(PayrollMonthOption? value) { if (IsPayrollReport) Show(Kind); }
    partial void OnSelectedPayrollEmployeeChanged(PayrollEmployeeOption? value) { if (IsPayslipReport) Show(Kind); }

    // ---- Payroll matrix (Pay Sheet / Payroll Register / Attendance Register / Payment Advice) ----

    /// <summary>The payroll matrix column headers (aligned to each row's <see cref="PayrollMatrixRowVm.Cells"/>);
    /// the first is the employee label column, the rest are per-report figure columns. Empty off a payroll matrix.</summary>
    public ObservableCollection<PayrollMatrixColumnVm> PayrollColumns { get; } = new();

    /// <summary>The payroll matrix rows (one per employee + a footing Grand-Total row). Empty off a payroll matrix.</summary>
    public ObservableCollection<PayrollMatrixRowVm> PayrollRows { get; } = new();

    /// <summary>True while a payroll matrix has no employee rows (drives the "nothing to show" note).</summary>
    [ObservableProperty] private bool _isPayrollEmpty;

    /// <summary>A one-line note shown when a payroll report has nothing to project (no salaried employee this month).</summary>
    [ObservableProperty] private string _payrollEmptyNote = string.Empty;

    /// <summary>
    /// The footnotes a payroll statutory form prints under its grid (W7-D2). These are NOT decoration: several
    /// columns on the PF and ESI forms are printed, ruled and left blank because this book does not maintain their
    /// source (Father's / Husband's Name, reason for leaving, the IP's dispensary) or because they are facts about a
    /// bank challan rather than about our books. The footnote is what stops a blank column reading as a bug — and
    /// what stops anyone inventing a value to fill it. Empty off a statutory form.
    /// </summary>
    public ObservableCollection<string> PayrollFootnotes { get; } = new();

    /// <summary>True while <see cref="PayrollFootnotes"/> has anything to show (drives the footnote panel).</summary>
    [ObservableProperty] private bool _hasPayrollFootnotes;

    // ---- The payroll matrix's optional SECOND section (W7-D2) ----
    // PF Form 6A is a two-PAGE form: page 1 is per member, page 2 is the twelve monthly challan remittances, and the
    // two pages have DIFFERENT columns. Rendering page 2's figures under page 1's headings would be exactly the
    // "wrong figure under a right heading" this track is written to avoid, so the matrix carries a second, fully
    // independent column band + row list, shown only when a report fills it.

    /// <summary>The second section's own column band (aligned to <see cref="PayrollRows2"/>). Empty for every
    /// report that has only one grid.</summary>
    public ObservableCollection<PayrollMatrixColumnVm> PayrollColumns2 { get; } = new();

    /// <summary>The second section's rows.</summary>
    public ObservableCollection<PayrollMatrixRowVm> PayrollRows2 { get; } = new();

    /// <summary>The heading printed above the second section (e.g. Form 6A's page-2 caption).</summary>
    [ObservableProperty] private string _payrollSection2Title = string.Empty;

    /// <summary>True while the second section has rows (drives its visibility).</summary>
    [ObservableProperty] private bool _hasPayrollSection2;

    // ---- Payslip (single-employee detail) presentation properties ----

    /// <summary>The current payslip projection (for the PDF/Print path); null when none is built.</summary>
    private Payslip? _currentPayslip;

    /// <summary>The current payslip projection, exposed so the Print path renders it through the PayslipPdf writer.</summary>
    public Payslip? CurrentPayslip => _currentPayslip;

    [ObservableProperty] private string _payslipEmployee = string.Empty;
    [ObservableProperty] private string _payslipMeta = string.Empty;
    [ObservableProperty] private string _payslipMeta2 = string.Empty;
    [ObservableProperty] private string _payslipPeriodText = string.Empty;
    [ObservableProperty] private string _payslipGross = string.Empty;
    [ObservableProperty] private string _payslipTotalDeductions = string.Empty;
    [ObservableProperty] private string _payslipNet = string.Empty;
    [ObservableProperty] private string _payslipNetWords = string.Empty;
    [ObservableProperty] private string _payslipAttendance = string.Empty;
    [ObservableProperty] private string _payslipYtd = string.Empty;

    /// <summary>The payslip earning lines (name + always-rendered amount); foot to <see cref="PayslipGross"/>.</summary>
    public ObservableCollection<PayslipLineVm> PayslipEarnings { get; } = new();

    /// <summary>The payslip deduction lines; foot to <see cref="PayslipTotalDeductions"/>.</summary>
    public ObservableCollection<PayslipLineVm> PayslipDeductions { get; } = new();

    /// <summary>The employer-contribution lines shown informationally (not part of net pay).</summary>
    public ObservableCollection<PayslipLineVm> PayslipEmployerContributions { get; } = new();

    /// <summary>True when the payslip carries employer contributions (drives that section's visibility).</summary>
    public bool HasPayslipEmployerContributions => PayslipEmployerContributions.Count > 0;

    /// <summary>True while the Payslip has no employee/structure to show (drives the empty note).</summary>
    [ObservableProperty] private bool _isPayslipEmpty;

    /// <summary>
    /// Raised when a Stock-Summary row is drilled into (Enter / double-click a stock item): carries the
    /// stock item id so the shell can open that item's Stock Item Movement report. The shell (not this VM)
    /// owns opening a new page column, so the drill is surfaced as an event.
    /// </summary>
    public event Action<Guid>? DrillToMovementRequested;

    /// <summary>
    /// Raised when a Trial-Balance / Balance-Sheet / Profit-&amp;-Loss ledger row is drilled into (Enter /
    /// double-click a drillable row): carries the owning ledger id, the report's current display window
    /// [<c>From</c>,<c>To</c>], and a <c>movement</c> flag so the shell opens that ledger's vouchers (a
    /// <c>LedgerBook</c>) reconciled to the clicked figure. <c>movement</c> is true for a Profit-&amp;-Loss
    /// (flow) drill — the ledger-book then shows the in-window period movement (running balance from 0) that
    /// equals the P&amp;L line — and false for a Trial-Balance / Balance-Sheet (point-in-time) drill, whose
    /// cumulative closing-as-at-To equals the displayed closing balance. The shell owns opening the column.
    /// </summary>
    public event Action<Guid, DateOnly, DateOnly, bool>? DrillToLedgerRequested;

    /// <summary>
    /// Raised when a Day Book row — or a ledger-vouchers row inside a drilled <c>LedgerBook</c> column — is
    /// drilled into (Enter): carries the underlying voucher id so the shell opens that voucher's read-only
    /// detail as a new cascading column.
    /// </summary>
    public event Action<Guid>? DrillToVoucherRequested;

    /// <summary>
    /// W2-12 (census 11.6). Raised when a register's MONTH row is drilled: carries the register's kind and
    /// that month's window, so the shell opens the voucher-wise listing of exactly the vouchers footed into
    /// the clicked figure — the vendor's documented two-level register shape.
    /// </summary>
    public event Action<ReportKind, DateOnly, DateOnly>? DrillToRegisterMonthRequested;

    /// <summary>
    /// W2-12 (census 11.7). Raised when a Group-Summary SUB-GROUP row is drilled: carries that group's id so
    /// the shell opens its own Group Summary one column to the right.
    /// </summary>
    public event Action<Guid>? DrillToGroupSummaryRequested;

    /// <summary>
    /// W2-12 (census 11.7 / T1-32). Raised when a Group-Summary LEDGER row is drilled: carries the ledger and
    /// the report's window so the shell opens that ledger's <b>Monthly Summary</b> — never the voucher list
    /// directly, because skipping the monthly level is precisely the shape defect row 11.5 already carries.
    /// </summary>
    public event Action<Guid, DateOnly, DateOnly>? DrillToLedgerMonthlyRequested;

    /// <summary>The report's effective display-window start — the chosen period's From, else books-begin (RQ-7).</summary>
    public DateOnly DrillFrom => _options.Period?.From ?? _company.BooksBeginFrom;

    /// <summary>The report's effective display-window end — the chosen period end or the as-of date (RQ-7).</summary>
    public DateOnly DrillTo => _asOf;

    /// <summary>
    /// The scenario picker options: "Actual (no scenario)" first (a null <see cref="ScenarioOption.Scenario"/>),
    /// then every scenario defined on the company. Only meaningful for the balance reports (TB / P&amp;L / BS);
    /// the Day Book always shows the real books. Empty of scenarios ⇒ only the Actual option is offered.
    /// </summary>
    public ObservableCollection<ScenarioOption> Scenarios { get; } = new();

    /// <summary>The chosen scenario option; changing it rebuilds the current report under that scenario.</summary>
    [ObservableProperty] private ScenarioOption? _selectedScenario;

    // =============================================================== RQ-4 comparative / columnar report

    /// <summary>
    /// The extra comparison-column specs added on top of the report's own base column (RQ-4). Empty in the
    /// normal single-column state; each entry is one added period/scenario column (Alt+C) or one member of an
    /// auto-generated axis (Alt+N by month / by scenario). When non-empty the report renders as a horizontal
    /// multi-column comparative grid via <see cref="ComparativeColumns"/> + <see cref="ComparativeRows"/>.
    /// </summary>
    private readonly List<ComparativeReport.ColumnSpec> _extraColumns = new();

    /// <summary>The built comparative columns (header + per-column total text), aligned left→right; empty in
    /// single-column mode. The base column is always first, then the added/auto columns.</summary>
    public ObservableCollection<ComparativeColumnVM> ComparativeColumns { get; } = new();

    /// <summary>The built comparative rows (line label + one formatted value cell per column), aligned to
    /// <see cref="ComparativeColumns"/>; empty in single-column mode. A blank cell = the key is absent there.</summary>
    public ObservableCollection<ComparativeRowVM> ComparativeRows { get; } = new();

    /// <summary>True once at least one extra column has been added — the report renders as the comparative
    /// multi-column grid instead of the plain single-column grid. Clearing all extra columns flips it back.</summary>
    public bool IsComparative => _extraColumns.Count > 0;

    /// <summary>True for the report kinds that can be shown comparatively (TB / BS / P&amp;L / Stock Summary) —
    /// the four families the engine <see cref="ComparativeReport"/> composes. Alt+C / Alt+N are inert elsewhere.</summary>
    public bool SupportsComparative => ComparativeKind is not null;

    /// <summary>The engine comparative kind for the current report, or null when this kind is not comparative.</summary>
    private ComparativeReportKind? ComparativeKind => Kind switch
    {
        ReportKind.TrialBalance => ComparativeReportKind.TrialBalance,
        ReportKind.BalanceSheet => ComparativeReportKind.BalanceSheet,
        ReportKind.ProfitAndLoss => ComparativeReportKind.ProfitAndLoss,
        ReportKind.StockSummary => ComparativeReportKind.StockSummary,
        _ => null,
    };

    /// <summary>True when this report kind can be viewed under a scenario (TB / P&amp;L / Balance Sheet).</summary>
    public bool SupportsScenario => Kind is ReportKind.TrialBalance or ReportKind.BalanceSheet or ReportKind.ProfitAndLoss;

    /// <summary>The scenario currently applied (null = actual books).</summary>
    private Scenario? CurrentScenario => SupportsScenario ? SelectedScenario?.Scenario : null;

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    /// <summary>
    /// Builds the report of <paramref name="kind"/> for <paramref name="company"/>. For a
    /// <see cref="ReportKind.StockItemMovement"/> report, <paramref name="stockItemId"/> names the item to
    /// scope it to (the Stock-Summary drill target); it is ignored by the other report kinds.
    /// </summary>
    /// <param name="scopeMasterId">
    /// W2-12: the accounting GROUP a Group Summary / Group Vouchers report is scoped to, or the LEDGER a
    /// Ledger Monthly Summary is scoped to. Ignored by every other kind.
    /// </param>
    /// <param name="period">
    /// W2-12: an initial period window, applied before the first build. The register month-drill uses it to
    /// open the drilled pane already narrowed to the clicked month.
    /// </param>
    /// <param name="registerVoucherLevel">
    /// W2-12: opens a register at its VOUCHER-WISE level instead of its month-wise top level (the drill
    /// target of a month row).
    /// </param>
    public ReportsViewModel(
        Company company,
        ReportKind kind,
        Guid? stockItemId = null,
        Guid? scopeMasterId = null,
        PeriodRange? period = null,
        bool registerVoucherLevel = false)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _defaultAsOf = ComputeAsOf(company);
        _options = ReportOptions.AsOf(_defaultAsOf);
        if (period is { } p && p.IsValid) _options = _options.WithPeriod(p);
        _movementItemId = stockItemId;
        _scopeMasterId = scopeMasterId ?? Guid.Empty;
        _registerVoucherLevel = registerVoucherLevel;

        Scenarios.Add(ScenarioOption.Actual);
        foreach (var s in company.Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Scenarios.Add(new ScenarioOption(s));
        _selectedScenario = Scenarios[0];

        InitPayrollPickers();
        InitChequeBankPicker();

        Show(kind);
    }

    /// <summary>Populates the payroll wage-month picker (the 12 months of the report's financial year) and the
    /// employee picker (for the Payslip), defaulting the month to the latest posted payroll voucher's month (else the
    /// financial-year start) and the employee to the first by name. Assigns the backing fields directly so the ctor
    /// does not trigger a premature rebuild before <see cref="Show"/> runs.</summary>
    private void InitPayrollPickers()
    {
        var first = new DateOnly(_company.FinancialYearStart.Year, _company.FinancialYearStart.Month, 1);
        for (int i = 0; i < 12; i++)
            PayrollMonths.Add(new PayrollMonthOption { FirstDay = first.AddMonths(i) });

        // Default to the month of the latest posted payroll voucher, else the financial-year start month.
        DateOnly? latest = null;
        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (!v.Lines.Any(l => l.Payroll is not null)) continue;
            if (latest is null || v.Date > latest.Value) latest = v.Date;
        }
        var target = latest ?? first;
        // Kind is still the ctor default here (not a payroll kind), so setting the properties does not trigger a
        // premature rebuild — OnSelectedPayroll*Changed guards on IsPayrollReport, which is false until Show runs.
        SelectedPayrollMonth = PayrollMonths.FirstOrDefault(m => m.FirstDay.Year == target.Year && m.FirstDay.Month == target.Month)
            ?? PayrollMonths.FirstOrDefault();

        foreach (var e in _company.Employees
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.EmployeeNumber, StringComparer.Ordinal))
        {
            var label = string.IsNullOrWhiteSpace(e.EmployeeNumber) ? e.Name : $"{e.Name} ({e.EmployeeNumber})";
            PayrollEmployees.Add(new PayrollEmployeeOption { EmployeeId = e.Id, Display = label });
        }
        SelectedPayrollEmployee = PayrollEmployees.FirstOrDefault();

        // W-J1 (census 7.24) — the vendor's "List of Pay Heads". Ordered by the SAME display label the reports
        // print, so the picker entry and the report title cannot read differently for the same head.
        foreach (var ph in _company.PayHeads
            .OrderBy(p => string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName!.Trim(), StringComparer.Ordinal)
            .ThenBy(p => p.Id))
        {
            var label = string.IsNullOrWhiteSpace(ph.DisplayName) ? ph.Name : ph.DisplayName!.Trim();
            PayrollPayHeads.Add(new PayrollPayHeadOption { PayHeadId = ph.Id, Display = label });
        }
        SelectedPayrollPayHead = PayrollPayHeads.FirstOrDefault();
    }

    /// <summary>
    /// Populates the Cheque Printing report's <b>bank</b> picker — "All Banks" followed by every ledger whose
    /// <c>Enable Cheque Printing</c> is on, by name.
    ///
    /// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/print-cheques/</c>, section "Cheque Printing
    /// Report", scopes that report by a <b>List of Banks</b>. This is that scope.</para>
    ///
    /// <para><b>🔴 Why this exists at all.</b> <c>ChequePrinting.Build</c> has carried a <c>bankLedgerId</c>
    /// parameter, and a test proving it narrows the list, since the engine was written — with <b>no caller in
    /// <c>src/Apex.Desktop</c> that could ever pass a value</b>. A filter no operator can reach is the
    /// "capability that exists as a service method no user can reach" this project has already filed twice
    /// (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>). The picker is the route in.</para>
    ///
    /// <para>The default assignment cannot trigger a premature rebuild: <see cref="Kind"/> still holds the enum's
    /// zero value here (<see cref="ReportKind.TrialBalance"/>) because <see cref="Show"/> has not run, and
    /// <see cref="OnSelectedChequeBankChanged"/> guards on <see cref="ReportKind.ChequePrinting"/> — the same
    /// argument <see cref="InitPayrollPickers"/> makes for its two pickers.</para>
    /// </summary>
    private void InitChequeBankPicker()
    {
        ChequeBanks.Add(ChequeBankOption.AllBanks);
        // 🔴 EVERY BANK LEDGER, not only the cheque-printing ones. The picker started life scoped to the Cheque
        // Printing report, where "Enable Cheque Printing" is the right filter. It now also scopes the Cheque
        // Register (census 8.5) and the Deposit Slip (8.6), and a bank account you have never asked this product
        // to INK cheques for still receives cash and still holds a cheque book — narrowing to the flag would have
        // made those two reports permanently blank for that account with no way for the operator to tell why.
        foreach (var l in _company.Ledgers
            .Where(l => l.EnableChequePrinting || ClassificationRules.IsBankGroup(l.GroupId, _company))
            .OrderBy(l => l.Name, StringComparer.Ordinal))
            ChequeBanks.Add(new ChequeBankOption { LedgerId = l.Id, Display = l.Name });
        SelectedChequeBank = ChequeBanks[0];
    }

    // =============================================================== RQ-1 / RQ-2 / RQ-6 report parameters

    /// <summary>The as-of / period-end date currently driving the report (RQ-1). Read-only; set via F2 / Alt+F2.</summary>
    public DateOnly AsOf => _asOf;

    /// <summary>The explicit period window when one is chosen (Alt+F2), else <c>null</c> (RQ-1).</summary>
    public PeriodRange? Period => _options.Period;

    /// <summary>True when a report shows ledger/item-level detail; false for the group-level summary (RQ-2).</summary>
    public bool Detailed => _options.Detailed;

    /// <summary>Hide exact-zero-balance rows (RQ-6 F12).</summary>
    public bool HideZeroBalances => _options.HideZeroBalances;

    /// <summary>Show each row's percentage of its section/column total (RQ-6 F12).</summary>
    public bool ShowPercentages => _options.ShowPercentages;

    /// <summary>Closing-stock valuation basis for P&amp;L / Balance Sheet (RQ-6 F12).</summary>
    public ClosingStockMode ClosingStock => _options.ClosingStock;

    /// <summary>The active sort/filter VIEW (RQ-3 Alt+F12). <see cref="ReportSortFilter.None"/> = the default view.</summary>
    public ReportSortFilter SortFilter => _sortFilter;

    // ---- W2-13a: Ctrl+B "Basis of Values" → Scale Factor (census row 14.5) ----

    /// <summary>The Scale Factor the figures are DISPLAYED in (Ctrl+B). <see cref="ReportScale.Default"/> = rupees.</summary>
    public ReportScale Scale => _scale;

    /// <summary>
    /// True for the reports this slice's Scale Factor acts on — the three accounting statements
    /// (Trial Balance / Balance Sheet / Profit &amp; Loss).
    ///
    /// <para>🔴 <b>The scope is NAMED rather than implied, and it is narrower than the reference product's.</b>
    /// The vendor offers Ctrl+B on the inventory and cash/funds-flow reports as well (and on Stock Summary it
    /// also carries a Stock Valuation Method and a Godown/Stock-Position basis, none of which this slice
    /// builds). Every report kind outside this set answers <c>false</c> and
    /// <see cref="ApplyScale"/> refuses on it, so nothing on screen can silently pretend to be scaled. Row 14.5
    /// is therefore NOT closed by this slice — see the slice artefact for the named residual.</para>
    /// </summary>
    public bool SupportsScaleFactor => Kind is ReportKind.TrialBalance or ReportKind.BalanceSheet
        or ReportKind.ProfitAndLoss;

    /// <summary>
    /// Ctrl+B — sets the Scale Factor and re-projects. A no-op on a report that does not support it (the state
    /// stays <see cref="ReportScale.Default"/>), so a refused scale can never leave a half-scaled grid.
    /// </summary>
    public void ApplyScale(ReportScale scale)
    {
        if (!SupportsScaleFactor) return;
        _scale = scale;
        OnPropertyChanged(nameof(Scale));
        Show(Kind);
    }

    /// <summary>
    /// The scale value of a figure, for display only (see <see cref="ReportScales.Apply"/>). Every accounting
    /// row cell goes through this; nothing else in the class does.
    /// </summary>
    private Money Scaled(Money money) => ReportScales.Apply(_scale, money);

    /// <summary>
    /// 🔴 The header clause that says which unit the figures are in, e.g. <c>"  —  ₹ in Thousands"</c>. Empty at
    /// <see cref="ReportScale.Default"/>.
    ///
    /// <para>This is not decoration. A Trial Balance that silently reads "105.00" where the books say
    /// ₹1,05,000 is a misstated financial statement, so a scaled report is required to declare its unit in its
    /// own subtitle — the same line that already declares the as-of, the scenario and the detail level.</para>
    /// </summary>
    private string ScaleSuffix => _scale == ReportScale.Default
        ? string.Empty
        : $"  —  ₹ in {ReportScales.Label(_scale)}";

    /// <summary>True for the reports RQ-2 detailed↔summary applies to (TB / BS / P&amp;L / Stock Summary).</summary>
    public bool SupportsDetailToggle => Kind is ReportKind.TrialBalance or ReportKind.BalanceSheet
        or ReportKind.ProfitAndLoss or ReportKind.StockSummary;

    /// <summary>True for the reports the RQ-3 sort/filter VIEW acts on (the row-bearing accounting + Stock
    /// Summary reports). On any other report kind the Alt+F12 view is inert (rows pass through unchanged).</summary>
    public bool SupportsSortFilter => Kind is ReportKind.TrialBalance or ReportKind.BalanceSheet
        or ReportKind.ProfitAndLoss or ReportKind.StockSummary or ReportKind.DayBook;

    /// <summary>F2 — sets the as-of date and clears any period window, then re-projects (RQ-1).</summary>
    public void SetAsOf(DateOnly asOf)
    {
        _options = _options with { AsOfDate = asOf, Period = null };
        NotifyParameterChanged();
        Show(Kind);
    }

    /// <summary>Alt+F2 — sets the explicit period window (ignored if inverted) and re-projects (RQ-1).</summary>
    public void SetPeriod(DateOnly from, DateOnly to)
    {
        var range = new PeriodRange(from, to);
        if (!range.IsValid) return; // reject an inverted window rather than corrupt the projection
        _options = _options.WithPeriod(range);
        NotifyParameterChanged();
        Show(Kind);
    }

    /// <summary>Clears the period window, restoring the default as-of (RQ-1).</summary>
    public void ClearPeriod()
    {
        _options = _options with { Period = null, AsOfDate = _defaultAsOf };
        NotifyParameterChanged();
        Show(Kind);
    }

    /// <summary>Alt+F1 — flips detailed↔summary and re-projects (RQ-2). A no-op on reports that do not roll up.</summary>
    public void ToggleDetailed()
    {
        if (!SupportsDetailToggle) return;
        _options = _options.WithDetailed(!_options.Detailed);
        NotifyParameterChanged();
        Show(Kind);
    }

    /// <summary>F12 — apply the hide-zero / percentages / closing-stock configuration and re-project (RQ-6).</summary>
    public void ApplyConfiguration(bool hideZero, bool showPercentages, ClosingStockMode closingStock)
    {
        _options = _options with
        {
            HideZeroBalances = hideZero,
            ShowPercentages = showPercentages,
            ClosingStock = closingStock,
        };
        NotifyParameterChanged();
        Show(Kind);
    }

    /// <summary>
    /// Alt+F12 — apply the RQ-3 sort/filter VIEW and re-project. The view is applied to the row-bearing
    /// sections after they are built; the figures and the Grand Total stay engine-computed over the full set.
    /// </summary>
    public void ApplySortFilter(ReportSortFilter view)
    {
        _sortFilter = view ?? ReportSortFilter.None;
        OnPropertyChanged(nameof(SortFilter));
        Show(Kind);
    }

    /// <summary>Clears the sort/filter VIEW back to the identity (Alt+F12 Clear) and re-projects.</summary>
    public void ClearSortFilter() => ApplySortFilter(ReportSortFilter.None);

    /// <summary>Notifies the parameter read-props so a bound status/header line refreshes after a change.</summary>
    private void NotifyParameterChanged()
    {
        OnPropertyChanged(nameof(AsOf));
        OnPropertyChanged(nameof(Period));
        OnPropertyChanged(nameof(Detailed));
        OnPropertyChanged(nameof(HideZeroBalances));
        OnPropertyChanged(nameof(ShowPercentages));
        OnPropertyChanged(nameof(ClosingStock));
    }

    /// <summary>Rebuilds the current report whenever the scenario picker changes.</summary>
    partial void OnSelectedScenarioChanged(ScenarioOption? value)
    {
        _options = _options.WithScenario(CurrentScenario);
        Show(Kind);
    }

    /// <summary>Switches the displayed report and rebuilds its rows (under the selected scenario, if any).</summary>
    public void Show(ReportKind kind)
    {
        Kind = kind;
        OnPropertyChanged(nameof(SupportsScenario));
        OnPropertyChanged(nameof(SupportsDetailToggle));
        // W2-13a: a kind that cannot scale must not inherit a scale from the kind before it — otherwise the
        // grid would silently show divided figures on a report whose header carries no unit clause.
        OnPropertyChanged(nameof(SupportsScaleFactor));
        if (!SupportsScaleFactor && _scale != ReportScale.Default)
        {
            _scale = ReportScale.Default;
            OnPropertyChanged(nameof(Scale));
        }
        // The layout flags are computed from Kind; notify the view so the right DataTemplate shows.
        OnPropertyChanged(nameof(IsInventoryReport));
        OnPropertyChanged(nameof(IsGstReport));
        OnPropertyChanged(nameof(IsAccountingReport));
        OnPropertyChanged(nameof(IsStockSummary));
        OnPropertyChanged(nameof(IsGodownSummary));
        OnPropertyChanged(nameof(IsStockMovement));
        OnPropertyChanged(nameof(IsReorderStatus));
        OnPropertyChanged(nameof(IsSupplierPaymentAdvice));
        // Without this the picker stays hidden when the operator arrives on the Cheque Printing report from
        // another report in the same viewer — the bug the whole block above exists to prevent.
        OnPropertyChanged(nameof(ShowChequeBankPicker));
        // Wave J2: the same argument for the two new banking guards — the F8 status filter and the F5 slip
        // switch are bound through these, so a viewer that arrives on the Cheque Register from another report
        // would otherwise leave both chords dead until the screen was re-entered.
        OnPropertyChanged(nameof(IsChequeRegister));
        // W-K1 (census 9.8 / 9.7 / 9.6): the same argument again — an operator who arrives on one of these six
        // reports FROM another report in the same viewer would otherwise see the previous report's grid, because
        // Kind changed but nothing told the view its layout flags had.
        OnPropertyChanged(nameof(IsBillsPending));
        OnPropertyChanged(nameof(IsItemCostAnalysis));
        OnPropertyChanged(nameof(IsJobWorkAnalysis));
        OnPropertyChanged(nameof(IsDepositSlip));
        OnPropertyChanged(nameof(IsPhysicalStockRegister));
        OnPropertyChanged(nameof(IsOrderRegister));
        OnPropertyChanged(nameof(IsAllocationRegister));
        OnPropertyChanged(nameof(IsJobWorkOrderBook));
        OnPropertyChanged(nameof(IsBatchwise));
        OnPropertyChanged(nameof(IsBatchAgeAnalysis));
        OnPropertyChanged(nameof(IsTaxAnalysis));
        OnPropertyChanged(nameof(IsGstr1));
        OnPropertyChanged(nameof(IsGstr3b));
        OnPropertyChanged(nameof(SupportsComparative));
        OnPropertyChanged(nameof(ShowGstGrid));
        // Phase 7 slice 8 statutory-report flags + the pair-shared header labels + interest footnote.
        OnPropertyChanged(nameof(IsStatutoryReport));
        OnPropertyChanged(nameof(IsStatutoryOutstanding));
        OnPropertyChanged(nameof(IsStatutoryNotWithheld));
        OnPropertyChanged(nameof(IsStatutoryInterest));
        OnPropertyChanged(nameof(IsStatutoryNatureSummary));
        OnPropertyChanged(nameof(IsLedgersWithoutPan));
        OnPropertyChanged(nameof(StatCodeHeader));
        OnPropertyChanged(nameof(StatWithheldHeader));
        OnPropertyChanged(nameof(StatEventDateHeader));
        OnPropertyChanged(nameof(StatTaxHeader));
        OnPropertyChanged(nameof(StatInterestHeader));
        OnPropertyChanged(nameof(StatutoryFootnote));
        OnPropertyChanged(nameof(HasStatutoryFootnote));
        // Phase 8 slice 8 payroll-report flags + the wage-month / employee picker visibilities.
        OnPropertyChanged(nameof(IsPayrollReport));
        OnPropertyChanged(nameof(IsPayslipReport));
        OnPropertyChanged(nameof(IsPayrollMatrix));
        OnPropertyChanged(nameof(ShowPayrollMonthPicker));
        OnPropertyChanged(nameof(ShowPayrollEmployeePicker));
        // W7-D2 payroll statutory forms (census 7.20 / 7.21) + their statutory-period picker.
        OnPropertyChanged(nameof(IsPayrollStatutoryForm));
        OnPropertyChanged(nameof(IsStatutoryPeriodForm));
        OnPropertyChanged(nameof(ShowStatutoryPeriodPicker));
        // W-J1 (census 7.22–7.26) + the pay-head picker census 7.24 is scoped by. Without these the picker keeps
        // whatever visibility the PREVIOUS report left it with, which is how a scope control ends up on a report
        // it does not scope.
        OnPropertyChanged(nameof(IsPayrollBreakupReport));
        OnPropertyChanged(nameof(ShowPayrollPayHeadPicker));
        PayrollFootnotes.Clear();
        HasPayrollFootnotes = false;
        PayrollColumns2.Clear();
        PayrollRows2.Clear();
        PayrollSection2Title = string.Empty;
        HasPayrollSection2 = false;
        RebuildStatutoryPeriods(kind);
        // A kind change can invalidate the extra columns (e.g. switching to a non-comparative kind); the base
        // report always rebuilds below and, if comparative, the multi-column grid rebuilds after it.
        Rows.Clear();
        PayrollColumns.Clear();
        PayrollRows.Clear();
        PayslipEarnings.Clear();
        PayslipDeductions.Clear();
        PayslipEmployerContributions.Clear();
        // 🔴 RULING 18: default the title back to GUARDED before the new kind's builder runs. Only a builder that
        // concatenates a master name into the heading sets it true, and it must do so on every rebuild.
        TitleCarriesMasterName = false;

        switch (kind)
        {
            case ReportKind.TrialBalance: BuildTrialBalance(); break;
            case ReportKind.BalanceSheet: BuildBalanceSheet(); break;
            case ReportKind.ProfitAndLoss: BuildProfitAndLoss(); break;
            case ReportKind.DayBook: BuildDayBook(); break;

            case ReportKind.StockSummary: BuildStockSummary(); break;
            case ReportKind.GodownSummary: BuildGodownSummary(); break;
            case ReportKind.StockItemMovement: BuildStockItemMovement(); break;
            case ReportKind.ReceiptNoteRegister: BuildAllocationRegister("Receipt Note Register", Report.BuildReceiptNoteRegister); break;
            case ReportKind.DeliveryNoteRegister: BuildAllocationRegister("Delivery Note Register", Report.BuildDeliveryNoteRegister); break;
            case ReportKind.RejectionRegister: BuildAllocationRegister("Rejection Register", Report.BuildRejectionRegister); break;
            case ReportKind.PhysicalStockRegister: BuildPhysicalStockRegister(); break;
            case ReportKind.OrderRegister: BuildOrderRegister(); break;
            case ReportKind.ReorderStatus: BuildReorderStatus(); break;
            case ReportKind.Batchwise: BuildBatchwise(); break;
            case ReportKind.BatchAgeAnalysis: BuildBatchAgeAnalysis(); break;
            case ReportKind.PriceList: BuildPriceList(); break;

            // W-K1 (census 9.8 / 9.7 / 9.6): inventory costing & tracking.
            case ReportKind.PurchaseBillsPending: BuildBillsPending(purchase: true); break;
            case ReportKind.SalesBillsPending: BuildBillsPending(purchase: false); break;
            case ReportKind.StockItemCostAnalysis: BuildItemCostAnalysis(ReportKind.StockItemCostAnalysis); break;
            case ReportKind.StockGroupCostAnalysis: BuildItemCostAnalysis(ReportKind.StockGroupCostAnalysis); break;
            case ReportKind.CostTrackBreakup: BuildItemCostAnalysis(ReportKind.CostTrackBreakup); break;
            case ReportKind.JobWorkAnalysis: BuildJobWorkAnalysis(); break;

            case ReportKind.TaxAnalysis: BuildTaxAnalysis(); break;
            case ReportKind.Gstr1: BuildGstr1(); break;
            case ReportKind.Gstr3b: BuildGstr3b(); break;

            case ReportKind.CashFlow: BuildCashFlow(); break;
            case ReportKind.FundsFlow: BuildFundsFlow(); break;
            case ReportKind.RatioAnalysis: BuildRatioAnalysis(); break;

            case ReportKind.NegativeStock: BuildNegativeStock(); break;
            case ReportKind.NegativeCashBank: BuildNegativeCashBank(); break;
            case ReportKind.MemorandumRegister: BuildMemorandumRegister(); break;
            case ReportKind.ReversingJournalRegister: BuildReversingJournalRegister(); break;
            case ReportKind.PosRegister: BuildPosRegister(); break;

            case ReportKind.JobWorkInOrderBook: BuildJobWorkOrderBook(JobWorkDirection.In); break;
            case ReportKind.JobWorkOutOrderBook: BuildJobWorkOrderBook(JobWorkDirection.Out); break;
            case ReportKind.MaterialInRegister:
                BuildMaterialRegister("Material In Register", VoucherBaseType.MaterialIn, StockDirection.Inward); break;
            case ReportKind.MaterialOutRegister:
                BuildMaterialRegister("Material Out Register", VoucherBaseType.MaterialOut, StockDirection.Outward); break;

            // ---- Phase 7 slice 8 statutory TDS/TCS exception & outstanding reports (R1–R9) ----
            case ReportKind.TdsOutstanding: BuildTdsOutstanding(); break;
            case ReportKind.TdsNotDeducted: BuildTdsNotDeducted(); break;
            case ReportKind.TdsInterest: BuildTdsInterest(); break;
            case ReportKind.TdsNatureSummary: BuildTdsNatureSummary(); break;
            case ReportKind.TcsOutstanding: BuildTcsOutstanding(); break;
            case ReportKind.TcsNotCollected: BuildTcsNotCollected(); break;
            case ReportKind.TcsInterest: BuildTcsInterest(); break;
            case ReportKind.TcsNatureSummary: BuildTcsNatureSummary(); break;
            case ReportKind.LedgersWithoutPan: BuildLedgersWithoutPan(); break;

            // ---- W2-12: the report families (census 11.6 / 11.7 / 11.8) ----
            case ReportKind.SalesRegister: BuildVoucherRegister(VoucherRegisterKind.Sales); break;
            case ReportKind.PurchaseRegister: BuildVoucherRegister(VoucherRegisterKind.Purchase); break;
            case ReportKind.JournalRegister: BuildVoucherRegister(VoucherRegisterKind.Journal); break;
            case ReportKind.CreditNoteRegister: BuildVoucherRegister(VoucherRegisterKind.CreditNote); break;
            case ReportKind.DebitNoteRegister: BuildVoucherRegister(VoucherRegisterKind.DebitNote); break;
            case ReportKind.GroupSummary: BuildGroupSummary(); break;
            case ReportKind.GroupVouchers: BuildGroupVouchers(); break;
            case ReportKind.LedgerMonthlySummary: BuildLedgerMonthlySummary(); break;
            case ReportKind.Statistics: BuildStatistics(); break;

            case ReportKind.Payslip: BuildPayslip(); break;
            case ReportKind.PaySheet: BuildPaySheet(); break;
            case ReportKind.PayrollRegister: BuildPayrollRegister(); break;
            case ReportKind.AttendanceRegister: BuildAttendanceRegister(); break;
            case ReportKind.PaymentAdvice: BuildPaymentAdvice(); break;

            // ---- Wave 7 D1: Banking documents (census 8.4 / 8.7) ----
            case ReportKind.ChequePrinting: BuildChequePrinting(); break;
            case ReportKind.SupplierPaymentAdvice: BuildSupplierPaymentAdvice(); break;

            // ---- Wave J2: the rest of the banking documents (census 8.5 / 8.6) ----
            case ReportKind.ChequeRegister: BuildChequeRegister(); break;
            case ReportKind.ChequeRegisterDetail: BuildChequeRegisterDetail(); break;
            case ReportKind.DepositSlip: BuildDepositSlip(); break;
            case ReportKind.EPayments: BuildEPayments(); break;

            // W7-D2 — the PF statutory forms beyond the ECR (census 7.20). Every one of these reaches an engine
            // that THROWS on an incompletely set-up payroll (see RunStatutoryForm), and Show() has no handler.
            case ReportKind.PfForm3A: RunStatutoryForm(BuildPfForm3A); break;
            case ReportKind.PfForm5: RunStatutoryForm(BuildPfForm5); break;
            case ReportKind.PfForm6A: RunStatutoryForm(BuildPfForm6A); break;
            case ReportKind.PfForm10: RunStatutoryForm(BuildPfForm10); break;
            case ReportKind.PfForm12A: RunStatutoryForm(BuildPfForm12A); break;

            // W7-D2 — the ESI statutory forms beyond the monthly contribution file (census 7.21).
            case ReportKind.EsiForm3: RunStatutoryForm(BuildEsiForm3); break;
            case ReportKind.EsiForm5: RunStatutoryForm(BuildEsiForm5); break;
            case ReportKind.EsiForm6: RunStatutoryForm(BuildEsiForm6); break;

            // W-J1 — census rows 7.22–7.26. Wrapped in RunStatutoryForm for the same reason the forms above are:
            // these reach engines that THROW on a half-set-up payroll (a missing employee/pay head master), and
            // Show() still has no handler of its own, so an unguarded throw takes the shell down on menu activation.
            case ReportKind.AttendanceSheet: RunStatutoryForm(BuildAttendanceSheet); break;
            case ReportKind.PayHeadEmployeeBreakup: RunStatutoryForm(BuildPayHeadEmployeeBreakup); break;
            case ReportKind.EmployeePayHeadBreakup: RunStatutoryForm(BuildEmployeePayHeadBreakup); break;
            case ReportKind.PayrollStatutorySummary: RunStatutoryForm(BuildPayrollStatutorySummary); break;
            case ReportKind.IncomeTaxComputation: RunStatutoryForm(BuildIncomeTaxComputation); break;
        }

        // RQ-4: after the single-column report is built, (re)build the comparative multi-column grid when any
        // extra columns are present. This composes the engine ComparativeReport over [base spec, …extras]; the
        // plain Rows above stay intact so a switch back to single column is instant and byte-for-byte the same.
        RebuildComparative();

        // The row set is final — refresh the reusable empty-state's visibility (UI-defect C7).
        OnPropertyChanged(nameof(IsEmpty));
    }

    // =============================================================== RQ-4 comparative build / mutate

    /// <summary>
    /// The base column spec for the comparative report — the report's OWN current period/scenario, so the
    /// first comparative column always equals the plain single-column report the user is looking at.
    /// </summary>
    private ComparativeReport.ColumnSpec BaseColumnSpec()
    {
        var label = _options.Period is { } p
            ? $"{FormatDate(p.From)}–{FormatDate(p.To)}"
            : $"as at {FormatDate(_asOf)}";
        if (CurrentScenario is { } s) label += $" · {s.Name}";
        // Carry the report's OWN full options (as-of, Detailed/HideZero/%/ClosingStock, period, scenario) so the base
        // column reproduces the exact single-column report. Period/Scenario ride inside Options; leaving the spec's
        // own Period/Scenario null means OptionsFor starts from _options verbatim rather than the FY-end fallback.
        return new ComparativeReport.ColumnSpec(label, Period: null, Scenario: null, Options: _options);
    }

    /// <summary>
    /// Rebuilds <see cref="ComparativeColumns"/> + <see cref="ComparativeRows"/> from the base column spec plus
    /// the added extra columns. A no-op (clears both collections) when there are no extras or the current kind is
    /// not comparative, so the report stays single-column. Signed engine values are formatted per kind here.
    /// </summary>
    private void RebuildComparative()
    {
        ComparativeColumns.Clear();
        ComparativeRows.Clear();
        NotifyComparativeVisibility();

        if (_extraColumns.Count == 0 || ComparativeKind is not { } kind) return;

        var specs = new List<ComparativeReport.ColumnSpec> { BaseColumnSpec() };
        specs.AddRange(_extraColumns);

        var comparative = ComparativeReport.Build(_company, kind, specs);

        foreach (var c in comparative.Columns)
            ComparativeColumns.Add(new ComparativeColumnVM(c.Label, ColumnTotalText(kind, c)));

        foreach (var row in comparative.Rows)
        {
            var cells = new List<string>(row.Values.Count);
            foreach (var v in row.Values)
                cells.Add(v is { } m ? FormatSignedByKind(kind, m) : string.Empty);
            ComparativeRows.Add(new ComparativeRowVM(row.Label, row.GroupName, cells));
        }

        NotifyComparativeVisibility();
    }

    /// <summary>Notifies the comparative-mode flags so the view swaps between the single-column and multi-column grids.</summary>
    private void NotifyComparativeVisibility()
    {
        OnPropertyChanged(nameof(IsComparative));
        OnPropertyChanged(nameof(ShowSingleAccountingGrid));
        OnPropertyChanged(nameof(ShowSingleInventoryGrid));
    }

    /// <summary>The per-column total text appropriate to the kind (TB: Dr/Cr; BS: Liab/Assets; P&amp;L: net;
    /// Stock: closing value). Shown under each column header as a bold total line.</summary>
    private static string ColumnTotalText(ComparativeReportKind kind, ComparativeReport.Column c) => kind switch
    {
        ComparativeReportKind.TrialBalance =>
            $"Dr {IndianFormat.AmountAlways(c.TotalDebit)} · Cr {IndianFormat.AmountAlways(c.TotalCredit)}",
        ComparativeReportKind.BalanceSheet =>
            $"Liab {IndianFormat.AmountAlways(c.TotalLiabilities)} · Assets {IndianFormat.AmountAlways(c.TotalAssets)}",
        ComparativeReportKind.ProfitAndLoss =>
            (c.NetProfit.Amount >= 0m ? "Net Profit " : "Net Loss ")
                + IndianFormat.AmountAlways(new Money(Math.Abs(c.NetProfit.Amount))),
        ComparativeReportKind.StockSummary =>
            IndianFormat.AmountAlways(c.StockClosingValue),
        _ => string.Empty,
    };

    /// <summary>
    /// Formats a SIGNED engine value for a comparative cell. The sign carries the side (TB +Dr/−Cr, P&amp;L
    /// +income/−expense, BS +liability/−asset, Stock = closing value ≥ 0). The magnitude is shown Indian-style;
    /// a Dr/Cr suffix is appended for the balance kinds so the side reads without a separate column.
    /// </summary>
    private static string FormatSignedByKind(ComparativeReportKind kind, Money value)
    {
        var magnitude = IndianFormat.Amount(new Money(Math.Abs(value.Amount)));
        return kind switch
        {
            ComparativeReportKind.TrialBalance => value.Amount >= 0m ? $"{magnitude} Dr" : $"{magnitude} Cr",
            ComparativeReportKind.BalanceSheet => magnitude, // side implied by the row's group; keep it clean
            ComparativeReportKind.ProfitAndLoss => magnitude,
            _ => magnitude,
        };
    }

    /// <summary>
    /// Alt+C — appends one comparison column (a period window and/or a scenario with a display label) and
    /// re-renders the report as a multi-column comparative grid. A no-op on a non-comparative report kind or on
    /// an inverted period window (rejected, matching the single-column period-set behaviour). Returns whether the
    /// column was actually added, so the panel can surface a validation failure instead of a false success.
    /// </summary>
    public bool AddComparisonColumn(string label, PeriodRange? period, Scenario? scenario)
    {
        if (!SupportsComparative) return false;
        if (period is { } p && !p.IsValid) return false; // reject an inverted window

        var text = string.IsNullOrWhiteSpace(label) ? DefaultColumnLabel(period, scenario) : label.Trim();
        // Carry the report's OWN display options (Detailed/HideZero/%/ClosingStock) so every column renders
        // consistently with the base; the spec's own period/scenario are overlaid on top of them by the engine.
        _extraColumns.Add(new ComparativeReport.ColumnSpec(text, period, scenario, Options: _options));
        RebuildComparative();
        return true;
    }

    /// <summary>A sensible default label for an added column when the user leaves the label blank.</summary>
    private string DefaultColumnLabel(PeriodRange? period, Scenario? scenario)
    {
        var label = period is { } p ? $"{FormatDate(p.From)}–{FormatDate(p.To)}" : $"as at {FormatDate(_asOf)}";
        if (scenario is { } s) label += $" · {s.Name}";
        return label;
    }

    /// <summary>
    /// Alt+N (by month) — replaces the extra columns with one column per calendar month across the current
    /// period (or books-begin → as-of when no explicit period is set). The base column stays first, so the
    /// months read alongside the whole-period base. A no-op on a non-comparative kind.
    /// </summary>
    public bool AutoColumnsByMonth()
    {
        if (!SupportsComparative) return false;
        var period = _options.Period ?? new PeriodRange(_company.BooksBeginFrom, _asOf);
        _extraColumns.Clear();
        // Inherit the report's display flags on every monthly column (each month overlays its own period).
        foreach (var spec in ComparativeReport.MonthlyColumns(period))
            _extraColumns.Add(spec with { Options = _options });
        RebuildComparative();
        return true;
    }

    /// <summary>
    /// Alt+N (by scenario) — replaces the extra columns with one column per scenario defined on the company,
    /// over the current period/as-of window. Returns false (and adds nothing) when the company has no scenarios,
    /// so the panel can report that there is nothing to compare. A no-op on a non-comparative kind.
    /// </summary>
    public bool AutoColumnsByScenario()
    {
        if (!SupportsComparative) return false;
        if (_company.Scenarios.Count == 0) return false;

        var period = _options.Period ?? new PeriodRange(_company.BooksBeginFrom, _asOf);
        _extraColumns.Clear();
        // The base column already carries the actual books, so generate the scenario columns WITHOUT the engine's
        // leading "Actual" column (it would duplicate the base). One extra column per scenario. Inherit the
        // report's display flags on each (each column overlays its own period + scenario).
        foreach (var spec in ComparativeReport.ScenarioColumns(_company, period, includeActualColumn: false))
            _extraColumns.Add(spec with { Options = _options });
        RebuildComparative();
        return true;
    }

    /// <summary>Clears every extra column, returning the report to its plain single-column view (Alt+C/Alt+N reset).</summary>
    public void ClearComparative()
    {
        _extraColumns.Clear();
        RebuildComparative();
    }

    /// <summary>The number of extra (non-base) comparison columns currently added — for the shell/tests.</summary>
    public int ExtraColumnCount => _extraColumns.Count;

    // =============================================================== RQ-8 Save View (config tuple only)

    /// <summary>
    /// The stable, opaque report-kind tokens that the persisted <see cref="SavedReportView.ReportKind"/> string
    /// carries — one per <see cref="ReportKind"/>. These are frozen forever: renaming the Desktop enum must NOT
    /// change a token (an already-saved view still resolves), so the map is authored by hand, not <c>ToString()</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<ReportKind, string> KindTokens = new Dictionary<ReportKind, string>
    {
        [ReportKind.TrialBalance] = "TrialBalance",
        [ReportKind.BalanceSheet] = "BalanceSheet",
        [ReportKind.ProfitAndLoss] = "ProfitAndLoss",
        [ReportKind.DayBook] = "DayBook",
        [ReportKind.StockSummary] = "StockSummary",
        [ReportKind.GodownSummary] = "GodownSummary",
        [ReportKind.StockItemMovement] = "StockItemMovement",
        [ReportKind.ReceiptNoteRegister] = "ReceiptNoteRegister",
        [ReportKind.DeliveryNoteRegister] = "DeliveryNoteRegister",
        [ReportKind.RejectionRegister] = "RejectionRegister",
        [ReportKind.PhysicalStockRegister] = "PhysicalStockRegister",
        [ReportKind.OrderRegister] = "OrderRegister",
        [ReportKind.ReorderStatus] = "ReorderStatus",
        [ReportKind.Batchwise] = "Batchwise",
        [ReportKind.BatchAgeAnalysis] = "BatchAgeAnalysis",
        [ReportKind.PriceList] = "PriceList",
        [ReportKind.TaxAnalysis] = "TaxAnalysis",
        [ReportKind.Gstr1] = "Gstr1",
        [ReportKind.Gstr3b] = "Gstr3b",
        [ReportKind.CashFlow] = "CashFlow",
        [ReportKind.FundsFlow] = "FundsFlow",
        [ReportKind.RatioAnalysis] = "RatioAnalysis",
        [ReportKind.NegativeStock] = "NegativeStock",
        [ReportKind.NegativeCashBank] = "NegativeCashBank",
        [ReportKind.MemorandumRegister] = "MemorandumRegister",
        [ReportKind.ReversingJournalRegister] = "ReversingJournalRegister",
        [ReportKind.PosRegister] = "PosRegister",
        [ReportKind.JobWorkInOrderBook] = "JobWorkInOrderBook",
        [ReportKind.JobWorkOutOrderBook] = "JobWorkOutOrderBook",
        [ReportKind.MaterialInRegister] = "MaterialInRegister",
        [ReportKind.MaterialOutRegister] = "MaterialOutRegister",
        [ReportKind.TdsOutstanding] = "TdsOutstanding",
        [ReportKind.TdsNotDeducted] = "TdsNotDeducted",
        [ReportKind.TdsInterest] = "TdsInterest",
        [ReportKind.TdsNatureSummary] = "TdsNatureSummary",
        [ReportKind.TcsOutstanding] = "TcsOutstanding",
        [ReportKind.TcsNotCollected] = "TcsNotCollected",
        [ReportKind.TcsInterest] = "TcsInterest",
        [ReportKind.TcsNatureSummary] = "TcsNatureSummary",
        [ReportKind.LedgersWithoutPan] = "LedgersWithoutPan",
        [ReportKind.Payslip] = "Payslip",
        [ReportKind.PaySheet] = "PaySheet",
        [ReportKind.PayrollRegister] = "PayrollRegister",
        [ReportKind.AttendanceRegister] = "AttendanceRegister",
        [ReportKind.PaymentAdvice] = "PaymentAdvice",
        [ReportKind.SalesRegister] = "SalesRegister",
        [ReportKind.PurchaseRegister] = "PurchaseRegister",
        [ReportKind.JournalRegister] = "JournalRegister",
        [ReportKind.CreditNoteRegister] = "CreditNoteRegister",
        [ReportKind.DebitNoteRegister] = "DebitNoteRegister",
        [ReportKind.GroupSummary] = "GroupSummary",
        [ReportKind.GroupVouchers] = "GroupVouchers",
        [ReportKind.LedgerMonthlySummary] = "LedgerMonthlySummary",
        [ReportKind.Statistics] = "Statistics",
        [ReportKind.ChequePrinting] = "ChequePrinting",
        [ReportKind.SupplierPaymentAdvice] = "SupplierPaymentAdvice",
        [ReportKind.ChequeRegister] = "ChequeRegister",
        [ReportKind.ChequeRegisterDetail] = "ChequeRegisterDetail",
        [ReportKind.DepositSlip] = "DepositSlip",
        [ReportKind.EPayments] = "EPayments",
        // W7-D2 payroll statutory forms (census 7.20 / 7.21). Every ReportKind MUST appear here: TokenFor indexes
        // this dictionary directly, so a kind with no token throws KeyNotFoundException the moment an operator
        // presses Alt+K to save the view — which is what these eight did before this line existed.
        // The tokens are PERSISTED in saved views, so they are frozen: rename a kind and this string stays.
        [ReportKind.PfForm3A] = "PfForm3A",
        [ReportKind.PfForm5] = "PfForm5",
        [ReportKind.PfForm6A] = "PfForm6A",
        [ReportKind.PfForm10] = "PfForm10",
        [ReportKind.PfForm12A] = "PfForm12A",
        [ReportKind.EsiForm3] = "EsiForm3",
        [ReportKind.EsiForm5] = "EsiForm5",
        [ReportKind.EsiForm6] = "EsiForm6",
        // W-J1 — census rows 7.22–7.26. Same rule as the block above: a kind with no token here throws
        // KeyNotFoundException the moment an operator presses Alt+K on it. The tokens are PERSISTED, so frozen.
        [ReportKind.AttendanceSheet] = "AttendanceSheet",
        [ReportKind.PayHeadEmployeeBreakup] = "PayHeadEmployeeBreakup",
        [ReportKind.EmployeePayHeadBreakup] = "EmployeePayHeadBreakup",
        [ReportKind.PayrollStatutorySummary] = "PayrollStatutorySummary",
        [ReportKind.IncomeTaxComputation] = "IncomeTaxComputation",
        // W-K1 (census 9.8 / 9.7 / 9.6). Frozen tokens — see this map's doc comment: a saved view stores the
        // STRING, so renaming the enum member must never change what is written here.
        [ReportKind.PurchaseBillsPending] = "PurchaseBillsPending",
        [ReportKind.SalesBillsPending] = "SalesBillsPending",
        [ReportKind.StockItemCostAnalysis] = "StockItemCostAnalysis",
        [ReportKind.StockGroupCostAnalysis] = "StockGroupCostAnalysis",
        [ReportKind.CostTrackBreakup] = "CostTrackBreakup",
        [ReportKind.JobWorkAnalysis] = "JobWorkAnalysis",
    };

    private static readonly IReadOnlyDictionary<string, ReportKind> TokenKinds =
        KindTokens.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    /// <summary>Maps a Desktop <see cref="ReportKind"/> to its stable persisted token.</summary>
    public static string TokenFor(ReportKind kind) => KindTokens[kind];

    /// <summary>Resolves a persisted token back to a Desktop <see cref="ReportKind"/>, or null when unknown
    /// (a view saved by a newer build the engine token no longer maps — the caller skips it rather than crash).</summary>
    public static ReportKind? KindFor(string token) =>
        TokenKinds.TryGetValue(token, out var kind) ? kind : null;

    /// <summary>
    /// Captures the report's CURRENT configuration as a config-only <see cref="SavedReportView"/> (RQ-8) — its
    /// kind token, period/as-of, detailed flag, F12 options, scenario NAME, sort/filter thresholds and the
    /// comparative columns. No computed figure is captured: applying the view later recomputes the report from
    /// the live company (ER-9), so a saved view can never go stale.
    /// </summary>
    public SavedReportView ToSavedView() => new()
    {
        ReportKind = TokenFor(Kind),
        AsOfDate = _options.AsOfDate,
        PeriodFrom = _options.Period?.From,
        PeriodTo = _options.Period?.To,
        Detailed = _options.Detailed,
        HideZeroBalances = _options.HideZeroBalances,
        ShowPercentages = _options.ShowPercentages,
        ClosingStock = _options.ClosingStock,
        ScenarioName = _options.Scenario?.Name,
        SortKey = _sortFilter.SortKey,
        SortAscending = _sortFilter.Ascending,
        FilterMinRupees = _sortFilter.Min?.Amount,
        FilterMaxRupees = _sortFilter.Max?.Amount,
        FilterNameContains = _sortFilter.NameContains,
        ComparativeColumns = _extraColumns.Count == 0
            ? null
            : _extraColumns.Select(c => new SavedComparativeColumn
            {
                Label = c.Label,
                PeriodFrom = c.Period?.From,
                PeriodTo = c.Period?.To,
                ScenarioName = c.Scenario?.Name,
            }).ToList(),
    };

    /// <summary>
    /// Re-applies a saved <paramref name="view"/> (RQ-8) to this report and RECOMPUTES it: rebuilds the
    /// <see cref="ReportOptions"/> (period/as-of/detail/F12/scenario), the <see cref="ReportSortFilter"/> view
    /// (sort + rupee thresholds + name filter) and the comparative columns, re-binding every scenario NAME to a
    /// live scenario on this company (an unknown name → the actual books, ER-9). The report kind is assumed to
    /// already match this view model's <see cref="Kind"/> (the shell opens a fresh report of the saved kind).
    /// Never loads figures — the projection re-runs through the engine so the on-screen numbers are identical to
    /// configuring the same options by hand.
    /// </summary>
    public void ApplySavedView(SavedReportView view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));

        // ---- ReportOptions (RQ-1/2/6) ----
        var options = ReportOptions.AsOf(view.AsOfDate)
            .WithDetailed(view.Detailed)
            .WithHideZeroBalances(view.HideZeroBalances)
            .WithShowPercentages(view.ShowPercentages)
            .WithClosingStock(view.ClosingStock)
            .WithScenario(ScenarioByName(view.ScenarioName));
        if (view.PeriodFrom is { } from && view.PeriodTo is { } to)
        {
            var range = new PeriodRange(from, to);
            if (range.IsValid) options = options.WithPeriod(range);
        }
        _options = options;

        // Keep the scenario picker in step so the header/subtitle and a later re-save reflect the applied scenario.
        // Assign the BACKING FIELD (not the SelectedScenario property): the property setter fires
        // OnSelectedScenarioChanged, which would overwrite the _options we just built from the view and recompute
        // twice. We already rebuild _options above and Show(Kind) below, so bypass the side-effect and just notify.
#pragma warning disable MVVMTK0034
        _selectedScenario = Scenarios.FirstOrDefault(o => o.Scenario == _options.Scenario) ?? Scenarios[0];
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedScenario));

        // ---- ReportSortFilter (RQ-3) ----
        _sortFilter = ReportSortFilter.None
            .WithSort(view.SortKey, view.SortAscending)
            .WithRange(
                view.FilterMinRupees is { } min ? Money.FromRupees(min) : null,
                view.FilterMaxRupees is { } max ? Money.FromRupees(max) : null)
            .WithNameContains(view.FilterNameContains);

        // ---- comparative columns (RQ-4) ----
        _extraColumns.Clear();
        if (view.ComparativeColumns is { Count: > 0 } cols)
            foreach (var c in cols)
            {
                PeriodRange? period = c.PeriodFrom is { } cf && c.PeriodTo is { } ct
                    ? new PeriodRange(cf, ct) : null;
                _extraColumns.Add(new ComparativeReport.ColumnSpec(
                    c.Label, period, ScenarioByName(c.ScenarioName), Options: _options));
            }

        NotifyParameterChanged();
        OnPropertyChanged(nameof(SortFilter));
        Show(Kind); // RECOMPUTE — figures are never loaded from the view (ER-9).
    }

    /// <summary>Resolves a scenario NAME to a live scenario on this company, or null (actual books) when the
    /// name is null/empty or matches no scenario — the ER-9 re-bind-on-apply rule.</summary>
    private Scenario? ScenarioByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _company.Scenarios.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The RQ-7 universal drill: Enter (or double-click) on the highlighted report row drills into the
    /// appropriate target, dispatched by report kind. It is deliberately a SAFE NO-OP on any non-drillable row
    /// (section headers, totals, folded Net Profit, derived Stock-in-Hand, ratio/statement computed lines) —
    /// those carry no drill key, so the engine is never called with <see cref="Guid.Empty"/>:
    /// <list type="bullet">
    /// <item>Stock Summary → the existing <see cref="DrillToMovementRequested"/> (item's movement report).</item>
    /// <item>Trial Balance / Balance Sheet / Profit &amp; Loss → <see cref="DrillToLedgerRequested"/> (that
    /// ledger's vouchers, a <c>LedgerBook</c>, for the report's current period).</item>
    /// <item>Day Book → <see cref="DrillToVoucherRequested"/> (that voucher's read-only detail).</item>
    /// </list>
    /// </summary>
    public void Drill(ReportRow? row)
    {
        if (row is null) return;

        switch (Kind)
        {
            case ReportKind.StockSummary:
            case ReportKind.ReorderStatus:
                // Both carry the row's stock item so Enter / double-click opens that item's Stock Item Movement.
                if (row.DrillStockItemId is { } itemId)
                    DrillToMovementRequested?.Invoke(itemId);
                break;

            case ReportKind.TrialBalance:
            case ReportKind.BalanceSheet:
            case ReportKind.ProfitAndLoss:
                if (row.DrillLedgerId != Guid.Empty)
                    // A P&L line is a flow figure (period movement) — drill it as movement-in-window so the
                    // opened ledger-book reconciles to the clicked figure; TB/BS lines are point-in-time.
                    DrillToLedgerRequested?.Invoke(row.DrillLedgerId, DrillFrom, DrillTo,
                        Kind == ReportKind.ProfitAndLoss);
                break;

            case ReportKind.DayBook:
                if (row.DrillVoucherId != Guid.Empty)
                    DrillToVoucherRequested?.Invoke(row.DrillVoucherId);
                break;

            // ---- W2-12 (census 11.6): a register drills month → voucher-wise → the voucher itself. ----
            case ReportKind.SalesRegister:
            case ReportKind.PurchaseRegister:
            case ReportKind.JournalRegister:
            case ReportKind.CreditNoteRegister:
            case ReportKind.DebitNoteRegister:
                if (row.DrillVoucherId != Guid.Empty)
                    DrillToVoucherRequested?.Invoke(row.DrillVoucherId);
                else if (row.DrillPeriod is { } month)
                    DrillToRegisterMonthRequested?.Invoke(Kind, month.From, month.To);
                break;

            // ---- W2-12 (census 11.7): a sub-group drills to its own summary, a ledger to its MONTHLY
            // summary. Never straight to the voucher list — that is 11.5's shape defect. ----
            case ReportKind.GroupSummary:
                if (row.DrillGroupId != Guid.Empty)
                    DrillToGroupSummaryRequested?.Invoke(row.DrillGroupId);
                else if (row.DrillLedgerId != Guid.Empty)
                    DrillToLedgerMonthlyRequested?.Invoke(row.DrillLedgerId, DrillFrom, DrillTo);
                break;

            case ReportKind.GroupVouchers:
                if (row.DrillVoucherId != Guid.Empty)
                    DrillToVoucherRequested?.Invoke(row.DrillVoucherId);
                break;

            // ---- Wave 7 D1 (census 8.4 / 8.7): both banking lists drill to the voucher that made the payment.
            // For 8.4 that drill IS the print route — Ctrl+P on the opened voucher inks the cheque leaf. ----
            case ReportKind.ChequePrinting:
            case ReportKind.SupplierPaymentAdvice:
            case ReportKind.DepositSlip:
            // Census 8.10 — an e-Payments row drills to the payment that raised it, which is how an operator
            // reaches the entry an exception row is complaining about.
            case ReportKind.EPayments:
                if (row.DrillVoucherId != Guid.Empty)
                    DrillToVoucherRequested?.Invoke(row.DrillVoucherId);
                break;

            // Census 8.5 — "View More Details in Cheque Register": a book row opens its leaf list, IN PLACE, so
            // the drill needs no shell event and works wherever the report is hosted. Escape re-shows the
            // summary; a register whose summary has no drill is half a report.
            case ReportKind.ChequeRegister:
                if (row.DrillChequeBookId != Guid.Empty)
                {
                    _chequeRegisterBookId = row.DrillChequeBookId;
                    Show(ReportKind.ChequeRegisterDetail);
                }
                break;

            // On the leaf list the drill is the paying voucher — an unissued leaf has none, so Enter is a no-op
            // on exactly the rows where there is nothing to open.
            case ReportKind.ChequeRegisterDetail:
                if (row.DrillVoucherId != Guid.Empty)
                    DrillToVoucherRequested?.Invoke(row.DrillVoucherId);
                break;

            // A monthly-summary month row opens that MONTH's ledger vouchers — the window is the month's,
            // not the report's, so the opened book foots to the clicked row.
            case ReportKind.LedgerMonthlySummary:
                if (row.DrillPeriod is { } ledgerMonth && _scopeMasterId != Guid.Empty)
                    DrillToLedgerRequested?.Invoke(_scopeMasterId, ledgerMonth.From, ledgerMonth.To, false);
                break;
        }
    }

    /// <summary>" under scenario <name>" suffix for the subtitle, or empty when showing the actual books.</summary>
    private string ScenarioSuffix =>
        CurrentScenario is { } s ? $"  —  under scenario “{s.Name}”" : string.Empty;

    /// <summary>The RQ-2 detailed/summary suffix for the subtitle ("(Summary)" when rolled up).</summary>
    private string DetailSuffix => _options.Detailed ? string.Empty : "  —  (Summary)";

    /// <summary>
    /// The "as at DATE" clause for the closing-balance statements (Trial Balance). The Trial Balance is a
    /// CLOSING-balance statement as-at the report date (opening carried forward): with a period window it
    /// closes as-at Period.To, so Period.From has no effect on the figures. The clause therefore always reads
    /// "as at {To}" (like the Balance Sheet) — never "for the period …", which would misleadingly imply an
    /// in-window movement. See TrialBalance.Build's closing-as-at-To dispatch.
    /// </summary>
    private string AsOfOrPeriodClause => $"as at {FormatDate(_asOf)}";

    /// <summary>Formats a percentage share (RQ-6) rounded to 2 dp — e.g. " (42.86%)".</summary>
    private static string PercentSuffix(decimal share) =>
        $"  ({share.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}%)";

    // --------------------------------------------------------------- Trial Balance

    private void BuildTrialBalance()
    {
        // RQ-1: the Trial Balance is a CLOSING-balance statement as-at the report date (opening carried
        // forward), like the Balance Sheet. With a period window (Alt+F2) it closes as-at Period.To; with
        // no window it closes as-at the as-of date. The engine ReportOptions dispatcher picks the date; the
        // scenario rides along.
        var tb = TrialBalance.Build(_company, _options);
        Title = "Trial Balance";
        Subtitle = $"{CompanyName}  —  {AsOfOrPeriodClause}{ScenarioSuffix}{DetailSuffix}{ScaleSuffix}";
        IsTwoColumn = true;

        var detailRows = tb.Rows.OrderBy(r => r.GroupName).ThenBy(r => r.LedgerName).ToList();

        if (_options.Detailed)
        {
            // RQ-6: hide exact-zero rows (net Dr − Cr == 0). RQ-6: percentage of the Dr column total.
            var kept = _options.HideZeroBalances
                ? ReportConfig.HideZeroBalances(detailRows, r => Net(r.Debit, r.Credit))
                : (IReadOnlyList<TrialBalanceRow>)detailRows;

            // RQ-3: the sort/filter VIEW re-orders/hides these ledger rows (magnitude = |Dr − Cr|). Percentages
            // are computed over what the view keeps, so a filtered view's shares still sum to 100%.
            var shown = _sortFilter.Apply(kept, r => r.LedgerName, r => Magnitude(r.Debit, r.Credit));

            var pct = _options.ShowPercentages
                ? ReportConfig.Percentages(shown, r => Magnitude(r.Debit, r.Credit))
                : null;

            for (var i = 0; i < shown.Count; i++)
            {
                var r = shown[i];
                var particulars = pct is null ? r.LedgerName : r.LedgerName + PercentSuffix(pct[i]);
                // RQ-7: carry the owning ledger id so Enter drills into that ledger's vouchers (LedgerBook).
                // W2-13a: Scaled() is the Ctrl+B display divide — the ROW CELL is scaled, never the projection.
                Rows.Add(DrCrLedgerLine(particulars, Scaled(r.Debit), Scaled(r.Credit), r.GroupName, r.LedgerId));
            }
        }
        else
        {
            // RQ-2: group-level roll-up. Sum the signed (Dr − Cr) net per group, then place in the Dr/Cr column.
            // Defect C: a group whose ledgers net to exactly zero must NOT render a blank Dr/Cr row — the
            // legacy detailed TB suppresses zero rows, so the summary roll-up suppresses net-zero groups too,
            // regardless of the RQ-6 hide-zero flag (Grand Total is unaffected; it stays balanced).
            var groups = ReportGrouping.RollUp(detailRows, r => r.GroupName, r => Net(r.Debit, r.Credit));
            var kept = ReportConfig.HideZeroBalances(groups, g => g.Amount);
            // RQ-3: the view re-orders/hides the group rows too (magnitude = |net|).
            var shown = _sortFilter.Apply(kept, g => g.Key, g => new Money(Math.Abs(g.Amount.Amount)));
            foreach (var g in shown)
            {
                var (dr, cr) = SplitSigned(g.Amount);
                Rows.Add(ReportRow.DrCrLine(g.Key, Scaled(dr), Scaled(cr)));
            }
        }

        // The Grand Total is scaled by the SAME divide as the rows above it, or the column would not foot.
        Rows.Add(ReportRow.DrCrTotal("Grand Total", Scaled(tb.TotalDebit), Scaled(tb.TotalCredit)));
    }

    // --------------------------------------------------------------- Balance Sheet

    private void BuildBalanceSheet()
    {
        // RQ-1 as-of + RQ-6 closing-stock basis pass through ReportOptions; scenario rides along.
        var bs = BalanceSheet.Build(_company, _asOf, _options);
        Title = "Balance Sheet";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}{DetailSuffix}{ScaleSuffix}";
        IsTwoColumn = false;

        AddBalanceSheetSide("Liabilities", "Total Liabilities",
            bs.Liabilities.Select(l => (l.Name, l.GroupName, l.Amount, l.LedgerId)), bs.TotalLiabilities);
        AddBalanceSheetSide("Assets", "Total Assets",
            bs.Assets.Select(a => (a.Name, a.GroupName, a.Amount, a.LedgerId)), bs.TotalAssets);
    }

    /// <summary>Adds one Balance-Sheet side (Liabilities/Assets) honouring RQ-2 summary + RQ-6 hide-zero/percent.</summary>
    private void AddBalanceSheetSide(
        string header, string totalLabel,
        IEnumerable<(string Name, string GroupName, Money Amount, Guid LedgerId)> lines, Money total)
    {
        Rows.Add(new ReportRow { Particulars = header, IsHeader = true });

        var all = lines.ToList();
        if (_options.Detailed)
        {
            // Legacy view hides the exact-zero lines; RQ-6 hide-zero is therefore already the default here.
            var kept = all.Where(l => l.Amount != Money.Zero).ToList();
            // RQ-3: the sort/filter VIEW acts within this side, so the group structure (Liabilities vs Assets)
            // is preserved. Percentages are computed over the kept rows.
            var shown = _sortFilter.Apply(kept, l => l.Name, l => l.Amount);
            var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, l => l.Amount) : null;
            for (var i = 0; i < shown.Count; i++)
            {
                var particulars = pct is null ? shown[i].Name : shown[i].Name + PercentSuffix(pct[i]);
                // RQ-7: carry the owning ledger id (Guid.Empty for the synthetic heads → not drillable).
                // W2-13a: the Ctrl+B display divide (percentages above were computed over the UNSCALED set).
                Rows.Add(LedgerLine(particulars, Scaled(shown[i].Amount), shown[i].GroupName, shown[i].LedgerId));
            }
        }
        else
        {
            // RQ-2: roll each side up to one row per group.
            var groups = ReportGrouping.RollUp(all, l => l.GroupName, l => l.Amount);
            var kept = _options.HideZeroBalances
                ? ReportConfig.HideZeroBalances(groups, g => g.Amount)
                : groups.Where(g => g.Amount != Money.Zero).ToList();
            // RQ-3: view within the side (magnitude = |group amount|).
            var shown = _sortFilter.Apply(kept, g => g.Key, g => new Money(Math.Abs(g.Amount.Amount)));
            var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, g => g.Amount) : null;
            for (var i = 0; i < shown.Count; i++)
            {
                var particulars = pct is null ? shown[i].Key : shown[i].Key + PercentSuffix(pct[i]);
                Rows.Add(ReportRow.Line(particulars, Scaled(shown[i].Amount)));
            }
        }

        Rows.Add(ReportRow.Total(totalLabel, Scaled(total)));
    }

    // --------------------------------------------------------------- Profit & Loss

    private void BuildProfitAndLoss()
    {
        // RQ-1 as-of/period-end + RQ-6 closing-stock basis pass through ReportOptions; scenario rides along.
        var pl = ProfitAndLoss.Build(_company, _asOf, _options);
        Title = "Profit & Loss A/c";
        var plClause = _options.Period is { } p
            ? $"for the period {FormatDate(p.From)} to {FormatDate(p.To)}"
            : $"for the period ending {FormatDate(_asOf)}";
        Subtitle = $"{CompanyName}  —  {plClause}{ScenarioSuffix}{DetailSuffix}{ScaleSuffix}";
        IsTwoColumn = false;

        AddProfitAndLossSide("Income", "Total Income",
            pl.Income.Select(i => (i.LedgerName, i.Amount, i.LedgerId)), pl.TotalIncome);
        AddProfitAndLossSide("Expenses", "Total Expenses",
            pl.Expenses.Select(e => (e.LedgerName, e.Amount, e.LedgerId)), pl.TotalExpenses);

        var isProfit = pl.NetProfit.Amount >= 0m;
        var label = isProfit ? "Net Profit" : "Net Loss";
        var magnitude = new Money(Math.Abs(pl.NetProfit.Amount));
        Rows.Add(ReportRow.Total(label, Scaled(magnitude)));
    }

    /// <summary>Adds one P&amp;L side (Income/Expenses) honouring RQ-2 summary + RQ-6 hide-zero/percent.</summary>
    private void AddProfitAndLossSide(
        string header, string totalLabel,
        IEnumerable<(string LedgerName, Money Amount, Guid LedgerId)> lines, Money total)
    {
        Rows.Add(new ReportRow { Particulars = header, IsHeader = true });

        // P&L lines carry no group key on the projection; the summary roll-up keys on the section header so a
        // rolled-up side collapses to a single "Income"/"Expenses" total row (still Σ==detailed). A rolled-up
        // row is NOT a single ledger, so it carries Guid.Empty (not drillable); a detailed row keeps its
        // ledger id so Enter (RQ-7) drills into that ledger's vouchers.
        var all = lines.ToList();
        IReadOnlyList<(string Name, Money Amount, Guid LedgerId)> shown = _options.Detailed
            ? all.Select(l => (l.LedgerName, l.Amount, l.LedgerId)).ToList()
            : ReportGrouping.RollUp(all, _ => header, l => l.Amount)
                .Select(g => (g.Key, g.Amount, Guid.Empty)).ToList();

        if (_options.HideZeroBalances)
            shown = ReportConfig.HideZeroBalances(shown, l => l.Amount);

        // RQ-3: the sort/filter VIEW acts within this side (Income vs Expenses), preserving the two-section
        // structure. Magnitude = |amount|.
        shown = _sortFilter.Apply(shown, l => l.Name, l => new Money(Math.Abs(l.Amount.Amount)));

        var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, l => l.Amount) : null;
        for (var i = 0; i < shown.Count; i++)
        {
            var particulars = pct is null ? shown[i].Name : shown[i].Name + PercentSuffix(pct[i]);
            Rows.Add(LedgerLine(particulars, Scaled(shown[i].Amount), string.Empty, shown[i].LedgerId));
        }

        Rows.Add(ReportRow.Total(totalLabel, Scaled(total)));
    }

    // ---- Dr/Cr signed helpers (Money is unsigned magnitude + side; TB rows split across two columns) ----

    /// <summary>The signed net of a Dr/Cr pair (Dr positive, Cr negative) as integer paisa.</summary>
    private static Money Net(Money debit, Money credit) => new(debit.Amount - credit.Amount);

    /// <summary>The magnitude (|Dr − Cr|) of a Dr/Cr pair — used for a percentage-of-total share.</summary>
    private static Money Magnitude(Money debit, Money credit) => new(Math.Abs(debit.Amount - credit.Amount));

    /// <summary>Splits a signed net back into (Dr, Cr) columns: positive → Dr, negative → Cr.</summary>
    private static (Money Debit, Money Credit) SplitSigned(Money net) =>
        net.Amount >= 0m ? (new Money(net.Amount), Money.Zero) : (Money.Zero, new Money(-net.Amount));

    // ---- RQ-7 drill-carrying row factories (mirror ReportRow.DrCrLine/Line but stamp the drill ledger id) ----

    /// <summary>A Trial-Balance Dr/Cr row that carries its owning ledger id so Enter drills into its vouchers.</summary>
    private static ReportRow DrCrLedgerLine(string particulars, Money debit, Money credit, string secondary, Guid ledgerId)
        => new()
        {
            Particulars = particulars,
            Secondary = secondary,
            Debit = IndianFormat.Amount(debit),
            Credit = IndianFormat.Amount(credit),
            IsTwoColumn = true,
            DrillLedgerId = ledgerId,
        };

    /// <summary>A single-amount (BS/P&amp;L) row that carries its owning ledger id so Enter drills into its vouchers.</summary>
    private static ReportRow LedgerLine(string particulars, Money amount, string secondary, Guid ledgerId)
        => new()
        {
            Particulars = particulars,
            Secondary = secondary,
            Amount = IndianFormat.Amount(amount),
            DrillLedgerId = ledgerId,
        };

    // --------------------------------------------------------------- Day Book

    private void BuildDayBook()
    {
        // RQ-1: the Day Book already filters [from,to]; feed the chosen period (else books-begin → as-of).
        var from = _options.Period?.From ?? _company.BooksBeginFrom;
        var built = DayBook.Build(_company, from, _asOf);
        Title = "Day Book";
        Subtitle = $"{CompanyName}  —  {FormatDate(from)} to {FormatDate(_asOf)}";
        IsTwoColumn = false;

        // RQ-3: the sort/filter VIEW acts on the Day Book entries. The Name filter/sort must match the SAME text
        // the row RENDERS as its particulars ("{VoucherTypeName} No. {Number}") — matching a hidden internal
        // string would hide rows whose visible text matches (e.g. "No.") and match text that is never shown. We
        // also fold in the party/particulars so a party-name filter still works. Magnitude = the voucher amount;
        // the default view leaves the date-ordered list untouched.
        //
        // 🔴 A CANCELLED ROW KEEPS ITS FULL MAGNITUDE HERE, DELIBERATELY. This comment used to claim "cancelled
        // rows carry Money.Zero so a positive range filter naturally excludes them", and that was simply false:
        // `DayBook.Build` passes `v.TotalDebit` regardless of `v.Cancelled`, and `Voucher.TotalDebit` sums the
        // debit-line magnitudes with no cancelled test — so a cancelled ₹20,000 receipt survives an amount range of
        // 15,000–100,000, measured. Nothing zeroes it and nothing should: the Day Book LISTS cancelled vouchers by
        // design (that is the whole evidence value of Cancel over Delete), and a row that vanishes from a filtered
        // view but not from the unfiltered one would be the worse behaviour. The claim was untestable before
        // Phase 10.11 S3, because until then no voucher in the product could be cancelled at all; it is testable
        // now, so it is stated as it is rather than as it was wished to be.
        var rows = _sortFilter.Apply(
            built,
            r => $"{DayBookParticulars(r)} {r.PartyOrParticulars}",
            r => new Money(Math.Abs(r.Amount.Amount)));

        foreach (var r in rows)
        {
            var secondary = r.PartyOrParticulars ?? string.Empty;
            var amt = IndianFormat.Amount(r.Amount);
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  {DayBookParticulars(r)}",
                Secondary = r.IsCancelled ? "(Cancelled) " + secondary : secondary,
                Amount = amt,
                // Phase 10.11 S3: carry the engine row's cancelled flag into presentation so the row renders in
                // the muted ink (CancelledRowToBrushConverter). The "(Cancelled)" text above stays — colour alone
                // is never the only carrier of a fact this material.
                IsCancelled = r.IsCancelled,
                DrillVoucherId = r.VoucherId,   // RQ-7: Enter opens this voucher's read-only detail
            });
        }

        // Distinguish a genuinely empty period from a period whose vouchers were all removed by the current
        // filter view: pre-filter count 0 → the period is empty; pre-filter > 0 with post-filter 0 → the filter
        // emptied it (a different, accurate message).
        if (rows.Count == 0)
            Rows.Add(new ReportRow
            {
                Particulars = built.Count == 0
                    ? "No vouchers in this period."
                    : "No rows match the current filter.",
                IsHeader = true,
            });
    }

    /// <summary>The particulars text a Day Book row renders (voucher type + number) — the SAME string used for
    /// the RQ-3 name filter/sort so a filter on visible text matches what the user actually sees.</summary>
    private static string DayBookParticulars(DayBookRow r) => $"{r.VoucherTypeName} No. {r.FormattedNumber}";

    // =============================================================== inventory reports (slice 3.4b)

    private DateOnly BooksFrom => _company.BooksBeginFrom;

    // --------------------------------------------------------------- Stock Summary  (Item | Closing Qty | Rate | Value)

    private void BuildStockSummary()
    {
        // RQ-1: the engine Stock Summary already takes [from,to]; feed the chosen period (else books-begin → as-of).
        var from = _options.Period?.From ?? BooksFrom;
        var ss = Report.BuildStockSummary(_company, _asOf, from);
        Title = "Stock Summary";
        Subtitle = _options.Period is { } p
            ? $"{CompanyName}  —  for the period {FormatDate(p.From)} to {FormatDate(p.To)}{DetailSuffix}"
            : $"{CompanyName}  —  as at {FormatDate(_asOf)}{DetailSuffix}";

        if (_options.Detailed)
        {
            // RQ-6: hide exact-zero closing-value rows; percentages of the total closing value.
            var kept = _options.HideZeroBalances
                ? ReportConfig.HideZeroBalances(ss.Rows, r => r.ClosingValue)
                : (IReadOnlyList<StockSummaryRow>)ss.Rows;
            // RQ-3: the sort/filter VIEW re-orders/hides the item rows (magnitude = closing value).
            var shown = _sortFilter.Apply(kept, r => r.ItemName, r => r.ClosingValue);
            var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, r => r.ClosingValue) : null;

            for (var i = 0; i < shown.Count; i++)
            {
                var r = shown[i];
                var unitRate = r.ClosingQuantity != 0m
                    ? IndianFormat.Amount(r.ClosingValue.Amount / r.ClosingQuantity)
                    : string.Empty;
                var itemName = pct is null ? r.ItemName : r.ItemName + PercentSuffix(pct[i]);
                Rows.Add(new ReportRow
                {
                    // Col1 Item, Col2 Inward, Col3 Outward, Col4 Closing Qty, Col5 Rate, Col6 Closing Value.
                    Col1 = itemName,
                    Col2 = IndianFormat.Quantity(r.InwardQuantity),
                    Col3 = IndianFormat.Quantity(r.OutwardQuantity),
                    Col4 = IndianFormat.Quantity(r.ClosingQuantity),
                    Col5 = unitRate,
                    Col6 = IndianFormat.Amount(r.ClosingValue),
                    DrillStockItemId = r.StockItemId,   // Enter / double-click → Stock Item Movement
                });
            }
        }
        else
        {
            // RQ-2: roll up to one row per stock group by closing value (Σ == the detailed total).
            var groups = ReportGrouping.RollUp(ss.Rows, r => r.GroupName, r => r.ClosingValue);
            var kept = _options.HideZeroBalances
                ? ReportConfig.HideZeroBalances(groups, g => g.Amount)
                : groups;
            // RQ-3: view the group rows (magnitude = |group closing value|).
            var shown = _sortFilter.Apply(kept, g => g.Key, g => new Money(Math.Abs(g.Amount.Amount)));
            var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, g => g.Amount) : null;
            for (var i = 0; i < shown.Count; i++)
            {
                var label = pct is null ? shown[i].Key : shown[i].Key + PercentSuffix(pct[i]);
                Rows.Add(new ReportRow { Col1 = label, Col6 = IndianFormat.Amount(shown[i].Amount) });
            }
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col6 = IndianFormat.AmountAlways(ss.TotalClosingValue),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Godown Summary  (Godown | Item | Qty | Value)

    private void BuildGodownSummary()
    {
        var gs = Report.BuildGodownSummary(_company, _asOf);
        Title = "Godown Summary";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        foreach (var r in gs.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.GodownName,
                Col2 = r.ItemName,
                Col3 = IndianFormat.Quantity(r.ClosingQuantity),
                Col4 = IndianFormat.Amount(r.ClosingValue),
            });

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col4 = IndianFormat.AmountAlways(gs.TotalClosingValue),
            IsTotal = true,
        });
    }

    // ------------------------------------------ W-K1 · census 9.8 — Bills Pending
    //   Date | Tracking No. | Stock Item | Party | Received | Billed | Pending

    /// <summary>
    /// Builds <b>Purchase Bills Pending</b> (<paramref name="purchase"/> true) or <b>Sales Bills Pending</b>
    /// (false) — census 9.8. The two of the vendor's sections become two headed blocks in one list.
    /// <para>🔴 <b>A section with no rows still prints its heading, followed by an explicit "(none)".</b>
    /// Silently dropping an empty section makes "nothing is pending here" indistinguishable from "this report
    /// forgot to look", and the empty case is the one an operator most needs to be able to trust — it is the
    /// answer to "have I billed everything I received?".</para>
    /// </summary>
    private void BuildBillsPending(bool purchase)
    {
        var rows = purchase
            ? Report.BuildPurchaseBillsPending(_company, _asOf)
            : Report.BuildSalesBillsPending(_company, _asOf);

        Title = purchase ? "Purchase Bills Pending" : "Sales Bills Pending";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        if (!_company.UseTrackingNumbers)
        {
            // The honest empty state. Naming the F11 caption verbatim tells the operator exactly which switch
            // to turn on rather than leaving them with a blank grid.
            Rows.Add(new ReportRow
            {
                Particulars = "Tracking numbers are not enabled. Turn on F11 → "
                    + "\"Use tracking numbers (enables delivery and receipt notes)\" to use this report.",
                IsHeader = true,
            });
            return;
        }

        // 🔴 R7 — THE TWO SIDES ARE NOT EQUALLY ATTESTED, AND THE DIFFERENCE IS RECORDED RATHER THAN HIDDEN.
        // The PURCHASE captions are the vendor's, VERBATIM: help.tallysolutions.com/purchase-order-tally/ prints
        // "Goods Recd. but Bills not Recd.:" and "Bills Recd. but Goods not Recd.:" as the two section headings.
        // The SALES page (help.tallysolutions.com/sales-order-tally/) attests the report NAME "Sales Bills
        // Pending" verbatim but describes its two sections in PROSE ("goods may have been delivered but not
        // invoiced"; "invoices raised but against which goods have not been delivered") and never gives a caption
        // pair. So the two sales captions below are OURS, written to mirror the attested purchase pair over the
        // outward document names. That is a documented divergence, not a quotation — if the vendor caption is
        // later found, these two strings are what to change.
        AddBillsPendingSection(rows, BillsPendingSection.GoodsNotBilled,
            purchase ? "Goods Recd. but Bills not Recd." : "Goods Delivered but Bills not Raised");
        AddBillsPendingSection(rows, BillsPendingSection.BilledNotReceived,
            purchase ? "Bills Recd. but Goods not Recd." : "Bills Raised but Goods not Delivered");
    }

    private void AddBillsPendingSection(
        IReadOnlyList<BillsPendingRow> all, BillsPendingSection section, string heading)
    {
        Rows.Add(new ReportRow { Col1 = heading, IsHeader = true });

        var any = false;
        var pending = 0m;
        foreach (var r in all)
        {
            if (r.Section != section) continue;
            any = true;
            pending += r.PendingQuantity;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.TrackingNumber,
                Col3 = r.ItemName,
                Col4 = r.PartyName ?? string.Empty,
                Col5 = IndianFormat.Quantity(r.ReceivedQuantity),
                Col6 = IndianFormat.Quantity(r.BilledQuantity),
                Col7 = IndianFormat.Quantity(r.PendingQuantity),
            });
        }

        if (!any)
        {
            Rows.Add(new ReportRow { Col2 = "(none)" });
            return;
        }
        Rows.Add(new ReportRow
        {
            Col1 = "Total",
            Col7 = IndianFormat.Quantity(pending),
            IsTotal = true,
        });
    }

    // ------------------------------------------ W-K1 · census 9.7 — Item Cost Analysis
    //   Name | Cost (Expense) | Revenue (Income) | Balance at Cost | Profit/Loss

    /// <summary>
    /// Builds one of the three <b>Item Cost Analysis</b> reports (census 9.7). The four money column captions
    /// are the vendor's, verbatim; see <see cref="ItemCostAnalysis"/> for what each holds.
    /// </summary>
    private void BuildItemCostAnalysis(ReportKind kind)
    {
        Title = kind switch
        {
            ReportKind.StockItemCostAnalysis => "Stock Item Cost Analysis",
            ReportKind.StockGroupCostAnalysis => "Stock Group Cost Analysis",
            _ => "Cost Track Break-up",
        };
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        if (!_company.EnableCostTracking)
        {
            Rows.Add(new ReportRow
            {
                Particulars = "Cost tracking is not enabled. Turn on F11 → \"Enable Cost Tracking\" to use "
                    + "this report.",
                IsHeader = true,
            });
            return;
        }

        var rows = kind switch
        {
            ReportKind.StockItemCostAnalysis => Report.BuildStockItemCostAnalysis(_company, _asOf),
            ReportKind.StockGroupCostAnalysis => Report.BuildStockGroupCostAnalysis(_company, _asOf),
            _ => Report.BuildCostTrackBreakup(_company, _asOf),
        };

        var cost = Money.Zero;
        var revenue = Money.Zero;
        var balance = Money.Zero;
        var profit = Money.Zero;
        foreach (var r in rows)
        {
            cost += r.Cost;
            revenue += r.Revenue;
            balance += r.BalanceAtCost;
            profit += r.ProfitOrLoss;
            Rows.Add(new ReportRow
            {
                Col1 = r.Name,
                Col2 = IndianFormat.Amount(r.Cost),
                Col3 = IndianFormat.Amount(r.Revenue),
                Col4 = IndianFormat.Amount(r.BalanceAtCost),
                Col5 = IndianFormat.AmountAlways(r.ProfitOrLoss),
            });
        }

        if (rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "(no cost-tracked movements)" });
            return;
        }
        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col2 = IndianFormat.AmountAlways(cost),
            Col3 = IndianFormat.AmountAlways(revenue),
            Col4 = IndianFormat.AmountAlways(balance),
            Col5 = IndianFormat.AmountAlways(profit),
            IsTotal = true,
        });
    }

    // ------------------------------------------ W-K1 · census 9.6 — Job Work Analysis
    //   Particulars | Amount, in the vendor's Revenue / Cost / Nett Profit sections per job

    /// <summary>
    /// Builds <b>Job Work Analysis</b> (census 9.6) — per job/project, the vendor's Revenue (Income) and Cost
    /// (Expenses) sections and the Nett Profit/Loss.
    /// </summary>
    private void BuildJobWorkAnalysis()
    {
        Title = "Job Work Analysis";
        var period = _options.Period;
        Subtitle = period is { } p
            ? $"{CompanyName}  —  {FormatDate(p.From)} to {FormatDate(p.To)}"
            : $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        if (!_company.EnableJobCosting)
        {
            Rows.Add(new ReportRow
            {
                Particulars = "Job costing is not enabled. Turn on F11 → \"Enable Job Costing\", then set a "
                    + "job/project on a godown, to use this report.",
                IsHeader = true,
            });
            return;
        }

        // The engine takes a window; with no period chosen the window runs from the books-begin date so a job's
        // whole life is counted. Starting at _asOf instead would report every job as empty, which reads as a
        // broken feature rather than as an unset period.
        var from = period?.From ?? _company.BooksBeginFrom;
        var jobs = Report.BuildJobWorkAnalysis(_company, from, _asOf);

        if (jobs.Count == 0)
        {
            Rows.Add(new ReportRow
            {
                Particulars = "No godown has been set as a job/project. Set one on the Godown master under "
                    + "\"Set job/project for job costing\".",
                IsHeader = true,
            });
            return;
        }

        foreach (var job in jobs)
        {
            var sites = job.GodownNames.Count > 0 ? $"  ({string.Join(", ", job.GodownNames)})" : string.Empty;
            Rows.Add(new ReportRow { Col1 = job.JobName + sites, IsHeader = true });

            Rows.Add(new ReportRow { Col1 = "  Revenue (Income)" });
            foreach (var l in job.Revenue)
                Rows.Add(new ReportRow { Col1 = "    " + l.LedgerName, Col2 = IndianFormat.Amount(l.Amount) });
            if (job.Revenue.Count == 0) Rows.Add(new ReportRow { Col1 = "    (none)" });
            Rows.Add(new ReportRow
            {
                Col1 = "  Total Revenue", Col2 = IndianFormat.AmountAlways(job.TotalRevenue), IsTotal = true,
            });

            Rows.Add(new ReportRow { Col1 = "  Cost (Expenses)" });
            foreach (var l in job.Cost)
                Rows.Add(new ReportRow { Col1 = "    " + l.LedgerName, Col2 = IndianFormat.Amount(l.Amount) });
            if (job.Cost.Count == 0) Rows.Add(new ReportRow { Col1 = "    (none)" });
            Rows.Add(new ReportRow
            {
                Col1 = "  Total Cost", Col2 = IndianFormat.AmountAlways(job.TotalCost), IsTotal = true,
            });

            Rows.Add(new ReportRow
            {
                Col1 = "  Nett Profit/Loss",
                Col2 = IndianFormat.AmountAlways(job.NettProfit),
                IsTotal = true,
            });
        }
    }

    // --------------------------------------------------------------- Stock Item Movement  (Date | Voucher | In | Out | Balance | Value)

    private void BuildStockItemMovement()
    {
        var itemId = _movementItemId ?? _company.StockItems.FirstOrDefault()?.Id;
        if (itemId is not { } id)
        {
            Title = "Stock Item Movement";
            Subtitle = $"{CompanyName}  —  no stock items";
            Rows.Add(new ReportRow { Particulars = "No stock items.", IsHeader = true });
            return;
        }

        var mv = Report.BuildStockItemMovement(_company, id, _asOf);
        Title = "Stock Item Movement";
        Subtitle = $"{CompanyName}  —  {mv.ItemName}  —  {FormatDate(mv.From)} to {FormatDate(mv.To)}";

        // Opening line so the running balance reads from the carried-forward on-hand.
        Rows.Add(new ReportRow
        {
            Col1 = FormatDate(mv.From),
            Col2 = "Opening Balance",
            Col5 = IndianFormat.Quantity(mv.OpeningQuantity),
            IsHeader = true,
        });

        foreach (var r in mv.Rows)
        {
            var voucher = r.Number > 0 ? $"{r.VoucherTypeName} No. {r.FormattedNumber}" : r.VoucherTypeName;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = voucher,
                Col3 = r.InwardQuantity != 0m ? IndianFormat.Quantity(r.InwardQuantity) : string.Empty,
                Col4 = r.OutwardQuantity != 0m ? IndianFormat.Quantity(r.OutwardQuantity) : string.Empty,
                Col5 = IndianFormat.Quantity(r.RunningQuantity),
                Col6 = IndianFormat.Amount(r.RunningValue),
            });
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Closing Balance",
            Col5 = IndianFormat.Quantity(mv.ClosingQuantity),
            Col6 = IndianFormat.AmountAlways(mv.ClosingValue),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Allocation registers (Receipt/Delivery/Rejection)
    //   Date | No. | Party | Item | Godown | Qty | Rate | Value | Batch

    private void BuildAllocationRegister(
        string title, Func<Company, DateOnly, DateOnly, IReadOnlyList<InventoryRegisterRow>> build)
    {
        var rows = build(_company, BooksFrom, _asOf);
        Title = title;
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        var total = Money.Zero;
        foreach (var r in rows)
        {
            var qtySigned = r.Direction == StockDirection.Inward ? r.Quantity : -r.Quantity;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.FormattedNumber,
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(qtySigned),
                Col7 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
                Col8 = IndianFormat.Amount(r.Value),
                Secondary = r.BatchLabel ?? string.Empty,
            });
            total += r.Value;
        }

        // An empty register leaves Rows empty so the shared EmptyState component renders the "no entries"
        // message across the whole body (UI-defect C6/C7) — not parked into the starved "*" Item column.
        if (rows.Count > 0)
            Rows.Add(new ReportRow { Col4 = "Grand Total", Col8 = IndianFormat.AmountAlways(total), IsTotal = true });
    }

    // --------------------------------------------------------------- Physical Stock register (Date | Item | Godown | Book | Counted | Variance)

    private void BuildPhysicalStockRegister()
    {
        var rows = Report.BuildPhysicalStockRegister(_company, BooksFrom, _asOf);
        Title = "Physical Stock Register";
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.ItemName,
                Col3 = r.GodownName,
                Col4 = IndianFormat.Quantity(r.BookQuantity),
                Col5 = IndianFormat.Quantity(r.CountedQuantity),
                Col6 = IndianFormat.Quantity(r.Variance),
                Secondary = r.BatchLabel ?? string.Empty,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col2 = "No physical-stock counts in this period.", IsHeader = true });
    }

    // --------------------------------------------------------------- Order register (Date | No. | Party | Item | Godown | Ordered | Pending | Rate)

    private void BuildOrderRegister()
    {
        var rows = Report.BuildOrderRegister(_company, BooksFrom, _asOf);
        Title = "Order Register";
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = $"{r.VoucherTypeName} No. {r.FormattedNumber}",
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(r.OrderedQuantity),
                Col7 = IndianFormat.Quantity(r.OutstandingQuantity),
                Col8 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
            });

        // Empty ⇒ leave Rows empty so the shared EmptyState renders the message (UI-defect C6/C7).
    }

    // --------------------------------------------------------------- Job Work Order Book (Phase 6 slice 8; RQ-51)
    //   Order header row (Date | Order No. | Party | FG × qty) + tracked component rows
    //   (Item | Track | Ordered | Fulfilled | Pending), pending = ordered − Σ fulfilling material movements.

    private void BuildJobWorkOrderBook(JobWorkDirection direction)
    {
        var rows = direction == JobWorkDirection.In
            ? JobWorkReports.BuildInOrderBook(_company, BooksFrom, _asOf)
            : JobWorkReports.BuildOutOrderBook(_company, BooksFrom, _asOf);
        Title = direction == JobWorkDirection.In ? "Job Work In Order Book" : "Job Work Out Order Book";
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        foreach (var o in rows)
        {
            // Order header row (bold): Date | Order No. | Party | Finished good × quantity.
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(o.Date),
                Col2 = o.OrderNo,
                Col3 = o.PartyName ?? string.Empty,
                Col4 = $"{o.VoucherTypeName} No. {o.FormattedNumber} — {o.FinishedGoodName} × {IndianFormat.Quantity(o.FinishedGoodQuantity)}",
                IsHeader = true,
            });
            foreach (var c in o.Components)
                Rows.Add(new ReportRow
                {
                    Col4 = c.ComponentName,
                    Col5 = TrackLabel(c.Track),
                    Col6 = IndianFormat.Quantity(c.OrderedQuantity),
                    Col7 = IndianFormat.Quantity(c.FulfilledQuantity),
                    Col8 = IndianFormat.Quantity(c.PendingQuantity),
                });
        }

        // Empty ⇒ leave Rows empty so the shared EmptyState renders the message (UI-defect C6/C7).
    }

    private static string TrackLabel(JobWorkComponentTrack track) =>
        track == JobWorkComponentTrack.PendingToReceive ? "To Receive" : "To Issue";

    // --------------------------------------------------------------- Material In/Out Register (Phase 6 slice 8; RQ-51)
    //   Date | No. | Party | Item | Godown | Qty (signed) | Rate | Value — one row per source/destination line.

    private void BuildMaterialRegister(string title, VoucherBaseType baseType, StockDirection primary)
    {
        var rows = baseType == VoucherBaseType.MaterialIn
            ? JobWorkReports.BuildMaterialInRegister(_company, BooksFrom, _asOf)
            : JobWorkReports.BuildMaterialOutRegister(_company, BooksFrom, _asOf);
        Title = title;
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        var total = Money.Zero;
        foreach (var r in rows)
        {
            var qtySigned = r.Direction == StockDirection.Inward ? r.Quantity : -r.Quantity;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.FormattedNumber,
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(qtySigned),
                Col7 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
                Col8 = IndianFormat.Amount(r.Value),
                Secondary = r.BatchLabel ?? string.Empty,
            });
            // Head-line value = the register's primary direction (received for In, dispatched for Out), so a
            // balanced transfer is not double-counted across its source + destination legs.
            if (r.Direction == primary) total += r.Value;
        }

        // An empty register leaves Rows empty so the shared EmptyState renders the message across the whole
        // body (UI-defect C6/C7) — not parked into the starved "*" Item column.
        if (rows.Count > 0)
            Rows.Add(new ReportRow { Col4 = "Grand Total", Col8 = IndianFormat.AmountAlways(total), IsTotal = true });
    }

    // ------------------ Reorder Status (Item | Closing | Reorder Level | Pending POs | SOs Due | Shortfall | Order to be Placed)

    /// <summary>
    /// F8 "Reorder only" filter (RQ-53): when on, the Reorder-Status report shows only rows that genuinely need
    /// ordering (<c>OrderToBePlaced &gt; 0</c>); when off, every item resolved to a reorder level shows. False for
    /// every other report.
    /// </summary>
    [ObservableProperty] private bool _reorderOnlyFilter;

    /// <summary>F8 on the Reorder-Status report: toggles the "reorder only" filter and re-projects the report.</summary>
    public void ToggleReorderOnly()
    {
        if (Kind != ReportKind.ReorderStatus) return;
        ReorderOnlyFilter = !ReorderOnlyFilter;
        Rows.Clear();
        BuildReorderStatus();
    }

    private void BuildReorderStatus()
    {
        var rs = Report.BuildReorderStatus(_company, _asOf);
        Title = "Reorder Status";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}"
                   + (ReorderOnlyFilter ? "  —  reorder only (F8)" : string.Empty);

        var shown = 0;
        foreach (var r in rs.Rows)
        {
            if (ReorderOnlyFilter && r.OrderToBePlaced <= 0m) continue;   // F8: hide rows with nothing to order
            shown++;
            Rows.Add(new ReportRow
            {
                Col1 = r.ItemName,
                Col2 = IndianFormat.Quantity(r.ClosingQuantity),
                Col3 = IndianFormat.Quantity(r.ReorderLevel),
                Col4 = IndianFormat.Quantity(r.PendingPurchaseOrders),
                Col5 = IndianFormat.Quantity(r.SalesOrdersDue),
                Col6 = IndianFormat.Quantity(r.Shortfall),
                Col7 = IndianFormat.Quantity(r.OrderToBePlaced),
                DrillStockItemId = r.StockItemId,        // Enter/double-click → Stock Item Movement; Ctrl+F9 → PO
                ReorderOrderQuantity = r.OrderToBePlaced, // raw qty for the Ctrl+F9 Purchase-Order prefill
            });
        }

        if (shown == 0)
            Rows.Add(new ReportRow
            {
                Col1 = ReorderOnlyFilter
                    ? "No items need ordering."
                    : "All items are above their reorder levels.",
                IsHeader = true,
            });
    }

    // =============================================================== batch reports (Phase 6 Cluster 1; RQ-8)

    // --------------------------------------------------------------- Batch-wise report
    //   Item | Batch | Mfg | Expiry | Godown | Opening | Inward | Outward | Closing ... (via Col1..Col8 + Secondary)

    /// <summary>
    /// Builds the Batch-wise report (Reports → Inventory Books → Batch → Batch-wise; RQ-8) over the report's
    /// [from, to] window — per item, per batch, its opening/inward/outward/closing quantities with mfg &amp;
    /// expiry and its closing value at the batch's authoritative cost (DP-8). A pure projection over the engine
    /// <see cref="Report.BuildBatchwiseReport"/>; the on-screen numbers derive from the same
    /// <see cref="Apex.Ledger.Services.BatchStockService"/> the batch engine uses (ER-4).
    /// </summary>
    private void BuildBatchwise()
    {
        var from = _options.Period?.From ?? BooksFrom;
        var report = Report.BuildBatchwiseReport(_company, _asOf, from);
        Title = "Batch-wise";
        Subtitle = $"{CompanyName}  —  {FormatDate(from)} to {FormatDate(_asOf)}";

        var total = Money.Zero;
        foreach (var r in report.Rows)
        {
            total += r.ClosingValue;
            Rows.Add(new ReportRow
            {
                // Col1 Item | Col2 Batch | Col3 Mfg | Col4 Expiry | Col5 Godown | Col6 Inward |
                // Col7 Outward | Col8 Closing-Qty ; Secondary carries the closing value.
                Col1 = r.ItemName,
                Col2 = r.Batch,
                Col3 = r.ManufacturingDate is { } m ? FormatDate(m) : "—",
                Col4 = r.ExpiryDate is { } e ? FormatDate(e) : "—",
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(r.InwardQuantity),
                Col7 = IndianFormat.Quantity(r.OutwardQuantity),
                Col8 = IndianFormat.Quantity(r.ClosingQuantity),
                Secondary = IndianFormat.Amount(r.ClosingValue),
            });
        }

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No batch-tracked stock in this period.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Secondary = IndianFormat.AmountAlways(total),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Batch Age Analysis
    //   Item | Batch | Mfg | Expiry | Days | Godown | Qty | Value ; past-expiry rows flagged distinctly (IsExpired).

    /// <summary>
    /// Builds the Age Analysis of expiring batches (Reports → Inventory Books → Batch → Age Analysis; RQ-8) as of
    /// the report date: every batch with on-hand stock whose resolved expiry is past OR within the near-expiry
    /// window (30 days), soonest-expiry first, with the whole-day gap to expiry (negative once past). Past-expiry
    /// rows are flagged <b>distinctly</b> (<see cref="ReportRow.IsExpired"/> → a red foreground). A pure
    /// projection over the engine <see cref="Report.BuildBatchAgeAnalysis"/>.
    /// </summary>
    private void BuildBatchAgeAnalysis()
    {
        const int withinDays = 30;
        var report = Report.BuildBatchAgeAnalysis(_company, _asOf, withinDays);
        Title = "Age Analysis of Expiring Batches";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}  —  next {withinDays} days (past-expiry flagged)";

        var total = Money.Zero;
        foreach (var r in report.Rows)
        {
            total += r.Value;
            var days = r.DaysToExpiry;
            var daysText = r.IsExpired
                ? $"expired {(-days)}d ago"
                : days == 0 ? "expires today" : $"in {days}d";
            Rows.Add(new ReportRow
            {
                // Col1 Item | Col2 Batch | Col3 Mfg | Col4 Expiry | Col5 Days | Col6 Godown | Col7 Qty ;
                // Secondary carries the value.
                Col1 = r.ItemName,
                Col2 = r.Batch,
                Col3 = r.ManufacturingDate is { } m ? FormatDate(m) : "—",
                Col4 = FormatDate(r.ExpiryDate),
                Col5 = daysText,
                Col6 = r.GodownName,
                Col7 = IndianFormat.Quantity(r.Quantity),
                Secondary = IndianFormat.Amount(r.Value),
                IsExpired = r.IsExpired,   // past-expiry rows render in the distinct alert colour (RQ-8)
            });
        }

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow
            {
                Col1 = "No batches are expired or expiring within the next 30 days.",
                IsHeader = true,
            });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Total (expiring / expired)",
            Secondary = IndianFormat.AmountAlways(total),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Price List report (slice 5; RQ-31)
    //   Level | Item | Applicable From | From Qty | To Qty | Rate | Discount % | Net Rate (via Col1..Col7 + Secondary)

    /// <summary>
    /// Builds the Price List report (Reports → Inventory Books → Price List; Phase 6 slice 5; RQ-31; Tally-Book
    /// p.35): per price <b>Level</b>, per <b>inventory item</b>, per <see cref="Apex.Ledger.Domain.PriceList.ApplicableFrom"/>
    /// version, the quantity slabs (From / To / Rate / Discount % / net rate). <b>Inventory items only</b> — a
    /// ledger never appears. A pure projection over the engine <see cref="PriceListReport.Build"/>; the Level and
    /// item repeat only at the top of each group so the nesting reads. De-branded (never any "Tally" text).
    /// </summary>
    private void BuildPriceList()
    {
        var report = PriceListReport.Build(_company);
        Title = "Price List";
        Subtitle = $"{CompanyName}  —  price levels & dated slabs";

        if (report.Items.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No price lists defined.", IsHeader = true });
            return;
        }

        foreach (var item in report.Items)
        {
            var firstVersion = true;
            foreach (var version in item.Versions)
            {
                var applicable = ApexDate.Format(version.ApplicableFrom);
                var firstSlab = true;
                foreach (var slab in version.Slabs)
                {
                    Rows.Add(new ReportRow
                    {
                        // Level + item only on the group's first row; Applicable-From only on the version's first slab.
                        Col1 = firstVersion && firstSlab ? item.PriceLevelName : string.Empty,
                        Col2 = firstVersion && firstSlab ? item.StockItemName : string.Empty,
                        Col3 = firstSlab ? applicable : string.Empty,
                        Col4 = IndianFormat.Quantity(slab.FromQty),
                        Col5 = slab.ToQty is { } to ? IndianFormat.Quantity(to) : "onwards",
                        Col6 = IndianFormat.Amount(slab.Rate),
                        Col7 = slab.DiscountPercent > 0m
                            ? slab.DiscountPercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%"
                            : "—",
                        Secondary = IndianFormat.Amount(slab.EffectiveUnitRate),
                        IsHeader = firstVersion && firstSlab,
                    });
                    firstSlab = false;
                    firstVersion = false;
                }
            }
        }
    }

    // =============================================================== GST reports (slice 4d)

    /// <summary>Formats GST basis points as a percent label (1800 → "18%", 900 → "9%", 0 → "0%").</summary>
    private static string RatePercent(int basisPoints)
    {
        var pct = basisPoints / 100m;
        return pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>The head name for a Tax-Analysis rate row (CGST / SGST / IGST / Cess).</summary>
    private static string HeadName(GstTaxHead head) => head switch
    {
        GstTaxHead.Central => "CGST",
        GstTaxHead.State => "SGST",
        GstTaxHead.Integrated => "IGST",
        GstTaxHead.Cess => "Cess",
        _ => head.ToString(),
    };

    /// <summary>The empty-state row shown when GST is off (no crash — the returns are simply empty).</summary>
    private bool GstOffGuard(string title)
    {
        Title = title;
        Subtitle = $"{CompanyName}  —  GST is not enabled for this company";
        if (!_company.GstEnabled)
        {
            Rows.Add(new ReportRow { Col1 = "GST is not enabled. Enable it under F11 Features → GST.", IsHeader = true });
            return true;
        }
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";
        return false;
    }

    // --------------------------------------------------------------- Tax Analysis
    //   Outward then Inward section; each rows by (rate, head): Rate | CGST | SGST | IGST | Taxable | Tax.

    private void BuildTaxAnalysis()
    {
        if (GstOffGuard("Tax Analysis")) return;

        var ta = Report.BuildTaxAnalysis(_company, BooksFrom, _asOf);
        Title = "Tax Analysis";

        AddTaxAnalysisSide("Outward Supplies (Output Tax)", ta.Outward);
        AddTaxAnalysisSide("Inward Supplies (Input Tax / ITC)", ta.Inward);

        // Grand total across both sides.
        var grandTax = new Money(ta.Outward.TotalTax.Amount + ta.Inward.TotalTax.Amount);
        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total (Output + Input)",
            Col2 = IndianFormat.AmountAlways(ta.Outward.TotalCgst.Amount + ta.Inward.TotalCgst.Amount),
            Col3 = IndianFormat.AmountAlways(ta.Outward.TotalSgst.Amount + ta.Inward.TotalSgst.Amount),
            Col4 = IndianFormat.AmountAlways(ta.Outward.TotalIgst.Amount + ta.Inward.TotalIgst.Amount),
            Col6 = IndianFormat.AmountAlways(grandTax),
            IsTotal = true,
        });
    }

    /// <summary>Adds one Tax-Analysis side (Outward/Inward): a section header, its rate/head rows and a subtotal.</summary>
    private void AddTaxAnalysisSide(string sectionTitle, TaxAnalysisSide side)
    {
        // Col1 Rate/Head | Col2 CGST | Col3 SGST | Col4 IGST | Col5 Taxable | Col6 Tax.
        Rows.Add(new ReportRow { Col1 = sectionTitle, IsHeader = true });

        foreach (var r in side.RateRows)
        {
            Rows.Add(new ReportRow
            {
                Col1 = $"{RatePercent(r.RateBasisPoints)}  {HeadName(r.Head)}",
                // Place the head's tax under its own column so the grid reads as a rate×head matrix.
                Col2 = r.Head == GstTaxHead.Central ? IndianFormat.Amount(r.Tax) : string.Empty,
                Col3 = r.Head == GstTaxHead.State ? IndianFormat.Amount(r.Tax) : string.Empty,
                Col4 = r.Head == GstTaxHead.Integrated ? IndianFormat.Amount(r.Tax) : string.Empty,
                Col5 = IndianFormat.Amount(r.TaxableValue),
                Col6 = IndianFormat.Amount(r.Tax),
            });
        }

        if (side.RateRows.Count == 0)
            Rows.Add(new ReportRow { Col1 = "No supplies in this period.", Col6 = string.Empty });

        Rows.Add(new ReportRow
        {
            Col1 = "Sub-total",
            Col2 = IndianFormat.AmountAlways(side.TotalCgst),
            Col3 = IndianFormat.AmountAlways(side.TotalSgst),
            Col4 = IndianFormat.AmountAlways(side.TotalIgst),
            Col6 = IndianFormat.AmountAlways(side.TotalTax),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- GSTR-1 (outward supplies)
    //   Sections: B2B, B2C, Rate-wise summary, HSN/SAC summary, plus Exempt line + grand totals.

    private void BuildGstr1()
    {
        if (GstOffGuard("GSTR-1")) return;

        var r = Report.BuildGstr1(_company, BooksFrom, _asOf);
        Title = "GSTR-1";

        // --- B2B: Party/GSTIN | Invoice No/Date | POS | Rate | Taxable | CGST | SGST | IGST ---
        Rows.Add(new ReportRow { Col1 = "B2B — Registered (party-wise invoices)", IsHeader = true });
        foreach (var b in r.B2B)
        {
            Rows.Add(new ReportRow
            {
                Col1 = b.PartyName,
                Col2 = b.PartyGstin ?? string.Empty,
                Col3 = $"No. {b.InvoiceNumber}  {FormatDate(b.InvoiceDate)}",
                Col4 = b.PlaceOfSupplyStateCode ?? string.Empty,
                Col5 = IndianFormat.Amount(b.TaxableValue),
                Col6 = IndianFormat.Amount(b.Cgst),
                Col7 = IndianFormat.Amount(b.Sgst),
                Col8 = IndianFormat.Amount(b.Igst),
            });
        }
        if (r.B2B.Count == 0)
            Rows.Add(new ReportRow { Col1 = "No B2B invoices.", Col2 = string.Empty });

        // --- B2C: rate-wise consolidation | Taxable | CGST | SGST | IGST ---
        Rows.Add(new ReportRow { Col1 = "B2C — Consumer (rate-wise)", IsHeader = true });
        foreach (var b in r.B2C)
        {
            Rows.Add(new ReportRow
            {
                Col1 = $"At {RatePercent(b.RateBasisPoints)}",
                Col5 = IndianFormat.Amount(b.TaxableValue),
                Col6 = IndianFormat.Amount(b.Cgst),
                Col7 = IndianFormat.Amount(b.Sgst),
                Col8 = IndianFormat.Amount(b.Igst),
            });
        }
        if (r.B2C.Count == 0)
            Rows.Add(new ReportRow { Col1 = "No B2C supplies.", Col2 = string.Empty });

        // --- Rate-wise summary | Taxable | Tax ---
        Rows.Add(new ReportRow { Col1 = "Rate-wise summary", IsHeader = true });
        foreach (var rr in r.RateSummary)
        {
            Rows.Add(new ReportRow
            {
                Col1 = $"At {RatePercent(rr.RateBasisPoints)}",
                Col5 = IndianFormat.Amount(rr.TaxableValue),
                Col8 = IndianFormat.Amount(rr.TotalTax),   // total tax shown in the last amount column
            });
        }

        // --- HSN/SAC summary: HSN | Description | UQC | Qty | Taxable | CGST | SGST | IGST ---
        Rows.Add(new ReportRow { Col1 = "HSN / SAC summary", IsHeader = true });
        foreach (var h in r.HsnSummary)
        {
            Rows.Add(new ReportRow
            {
                Col1 = h.HsnSac,
                Col2 = h.Description,
                Col3 = h.Uqc ?? string.Empty,
                Col4 = IndianFormat.Quantity(h.Quantity),
                Col5 = IndianFormat.Amount(h.TaxableValue),
                Col6 = IndianFormat.Amount(h.Cgst),
                Col7 = IndianFormat.Amount(h.Sgst),
                Col8 = IndianFormat.Amount(h.Igst),
            });
        }

        // --- Exempt/Nil/Non-GST outward value line ---
        Rows.Add(new ReportRow
        {
            Col1 = "Exempt / Nil-rated / Non-GST outward",
            Col5 = IndianFormat.AmountAlways(r.ExemptNilNonGstValue),
        });

        // --- Grand totals (output tax by head) ---
        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total (Output Tax)",
            Col6 = IndianFormat.AmountAlways(r.TotalCgst),
            Col7 = IndianFormat.AmountAlways(r.TotalSgst),
            Col8 = IndianFormat.AmountAlways(r.TotalIgst),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- GSTR-3B (summary return)
    //   3.1 Outward supplies; 4 Eligible ITC; Net tax payable per head (display-only, no set-off).

    private void BuildGstr3b()
    {
        if (GstOffGuard("GSTR-3B")) return;

        var r = Report.BuildGstr3b(_company, BooksFrom, _asOf);
        Title = "GSTR-3B";

        // Col1 label | Col2 Taxable value | Col3 CGST | Col4 SGST | Col5 IGST.
        Rows.Add(new ReportRow { Col1 = "3.1  Details of outward supplies", IsHeader = true });
        Rows.Add(new ReportRow
        {
            Col1 = "(a) Taxable outward supplies",
            Col2 = IndianFormat.Amount(r.TaxableOutwardValue),
            Col3 = IndianFormat.Amount(r.OutwardCgst),
            Col4 = IndianFormat.Amount(r.OutwardSgst),
            Col5 = IndianFormat.Amount(r.OutwardIgst),
        });
        Rows.Add(new ReportRow
        {
            Col1 = "(c) Exempt / Nil-rated / Non-GST outward",
            Col2 = IndianFormat.Amount(r.ExemptNilNonGstOutward),
        });
        Rows.Add(new ReportRow
        {
            Col1 = "Total output tax",
            Col3 = IndianFormat.AmountAlways(r.OutwardCgst),
            Col4 = IndianFormat.AmountAlways(r.OutwardSgst),
            Col5 = IndianFormat.AmountAlways(r.OutwardIgst),
            IsTotal = true,
        });

        Rows.Add(new ReportRow { Col1 = "4  Eligible ITC", IsHeader = true });
        Rows.Add(new ReportRow
        {
            Col1 = "(A) ITC available (inward supplies)",
            Col3 = IndianFormat.Amount(r.ItcCgst),
            Col4 = IndianFormat.Amount(r.ItcSgst),
            Col5 = IndianFormat.Amount(r.ItcIgst),
        });
        Rows.Add(new ReportRow
        {
            Col1 = "Total eligible ITC",
            Col3 = IndianFormat.AmountAlways(r.ItcCgst),
            Col4 = IndianFormat.AmountAlways(r.ItcSgst),
            Col5 = IndianFormat.AmountAlways(r.ItcIgst),
            IsTotal = true,
        });

        Rows.Add(new ReportRow { Col1 = "Net tax payable  (output − ITC; indicative, no set-off)", IsHeader = true });
        Rows.Add(new ReportRow
        {
            Col1 = "Net payable / (credit carried forward)",
            Col3 = IndianFormat.AmountAlways(r.NetCgst),
            Col4 = IndianFormat.AmountAlways(r.NetSgst),
            Col5 = IndianFormat.AmountAlways(r.NetIgst),
            IsTotal = true,
        });
    }

    // =============================================================== statutory TDS/TCS reports (Phase 7 slice 8)
    //   Pure projections over the S8 Report facades; every amount renders in WHOLE rupees (the returns are filed so).
    //   The TDS and TCS members of each pair share a DataTemplate, so the Build* methods populate the SAME Col slots
    //   (only the pair-shared Stat*Header labels differ). Deterministic engine ordering is preserved verbatim.

    /// <summary>Formats an overdue-days count for the outstandings grid: the number when overdue, else "—".</summary>
    private static string OverdueText(int days) => days > 0 ? days.ToString() : "—";

    // --------------------------------------------------------------- R1 TDS Outstandings
    //   Section | Nature | Deducted | Deposited | Outstanding | Overdue days
    private void BuildTdsOutstanding()
    {
        var report = Report.BuildTdsOutstanding(_company, _asOf);
        Title = "TDS Outstandings";
        Subtitle = $"{CompanyName}  —  deducted, not yet deposited as at {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Section,
                Col2 = r.Nature,
                Col3 = IndianFormat.Rupees(r.Deducted),
                Col4 = IndianFormat.Rupees(r.Deposited),
                Col5 = IndianFormat.Rupees(r.Outstanding),
                Col6 = OverdueText(r.OverdueDays),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No TDS deducted in this period.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col3 = FootedRupees(report.Rows, r => r.Deducted),
            Col4 = FootedRupees(report.Rows, r => r.Deposited),
            Col5 = FootedRupees(report.Rows, r => r.Outstanding),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R5 TCS Outstandings (mirror of R1)
    private void BuildTcsOutstanding()
    {
        var report = Report.BuildTcsOutstanding(_company, _asOf);
        Title = "TCS Outstandings";
        Subtitle = $"{CompanyName}  —  collected, not yet deposited as at {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Code,
                Col2 = r.Nature,
                Col3 = IndianFormat.Rupees(r.Collected),
                Col4 = IndianFormat.Rupees(r.Deposited),
                Col5 = IndianFormat.Rupees(r.Outstanding),
                Col6 = OverdueText(r.OverdueDays),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No TCS collected in this period.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col3 = FootedRupees(report.Rows, r => r.Collected),
            Col4 = FootedRupees(report.Rows, r => r.Deposited),
            Col5 = FootedRupees(report.Rows, r => r.Outstanding),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R2 TDS Not Deducted
    //   Date | Party | Section / Nature | Assessable | Aggregate in window | Threshold | Shortfall
    //   The aggregate column is FY-to-date on every section but §194-I, whose threshold window is the CALENDAR
    //   MONTH (first proviso: rent "for a month or part of a month" against ₹50,000, and no annual limb at all),
    //   which is why the shared header reads "Cumulative" rather than "Cumul. (FY)".
    private void BuildTdsNotDeducted()
    {
        var report = Report.BuildTdsNotDeducted(_company, _asOf);
        Title = "TDS Not Deducted";
        Subtitle = $"{CompanyName}  —  applicable but below threshold, up to {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.Party,
                Col3 = StatSectionNature(r.Section, r.Nature),
                Col4 = IndianFormat.Rupees(r.Assessable),
                Col5 = IndianFormat.Rupees(r.AggregateInWindow),
                Col6 = IndianFormat.Rupees(r.Threshold),
                Col7 = IndianFormat.Rupees(r.Shortfall),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No below-threshold TDS assessments.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col2 = "Grand Total",
            Col4 = FootedRupees(report.Rows, r => r.Assessable),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R6 TCS Not Collected (mirror of R2)
    private void BuildTcsNotCollected()
    {
        var report = Report.BuildTcsNotCollected(_company, _asOf);
        Title = "TCS Not Collected";
        Subtitle = $"{CompanyName}  —  applicable but below threshold, up to {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.Party,
                Col3 = StatSectionNature(r.Code, r.Nature),
                Col4 = IndianFormat.Rupees(r.Assessable),
                Col5 = IndianFormat.Rupees(r.CumulativeInFy),
                Col6 = IndianFormat.Rupees(r.Threshold),
                Col7 = IndianFormat.Rupees(r.Shortfall),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No below-threshold TCS collections.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col2 = "Grand Total",
            Col4 = FootedRupees(report.Rows, r => r.Assessable),
            IsTotal = true,
        });
    }

    /// <summary>Combines a section/collection code with its nature name for the middle column of the
    /// not-withheld grid, folding a duplicate (code == nature) down to just the code.</summary>
    private static string StatSectionNature(string code, string nature)
        => string.Equals(code, nature, StringComparison.OrdinalIgnoreCase) ? code : $"{code} — {nature}";

    /// <summary>The whole-rupee grand total that FOOTS to the displayed (per-row rounded) amounts (F5): the round-
    /// half-up rupee value of each row is summed, so the total never differs by ₹1 from the visible column (rows can
    /// carry paisa, e.g. a GST-inclusive TCS base), unlike <c>RupeesAlways</c> of the paisa-exact Σ.</summary>
    private static string FootedRupees<T>(IEnumerable<T> rows, Func<T, Money> amount)
        => IndianFormat.RupeesAlways(rows.Sum(r => IndianFormat.WholeRupees(amount(r))));

    // --------------------------------------------------------------- R3 TDS Interest u/s 201(1A)
    //   Party | Section | TDS | Deduction Date | Deposit Date | Due Date | Months | Interest @1.5%
    private void BuildTdsInterest()
    {
        var report = Report.BuildTdsInterest201(_company, _asOf);
        Title = "TDS Interest u/s 201(1A)";
        Subtitle = $"{CompanyName}  —  late-deposit interest up to {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Party,
                Col2 = r.Section,
                Col3 = IndianFormat.Rupees(r.Tds),
                Col4 = FormatDate(r.DeductionDate),
                Col5 = r.DepositDate is { } d ? FormatDate(d) : "Undeposited",
                Col6 = FormatDate(r.DueDate),
                Col7 = r.Months.ToString(),
                Col8 = IndianFormat.Rupees(r.Interest),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No late-deposited TDS — no interest accrues.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col3 = FootedRupees(report.Rows, r => r.Tds),
            Col8 = FootedRupees(report.Rows, r => r.Interest),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R7 TCS Interest u/s 206C(7) (mirror of R3)
    private void BuildTcsInterest()
    {
        var report = Report.BuildTcsInterest206C7(_company, _asOf);
        Title = "TCS Interest u/s 206C(7)";
        Subtitle = $"{CompanyName}  —  late-deposit interest up to {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Party,
                Col2 = r.Code,
                Col3 = IndianFormat.Rupees(r.Tcs),
                Col4 = FormatDate(r.CollectionDate),
                Col5 = r.DepositDate is { } d ? FormatDate(d) : "Undeposited",
                Col6 = FormatDate(r.DueDate),
                Col7 = r.Months.ToString(),
                Col8 = IndianFormat.Rupees(r.Interest),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No late-deposited TCS — no interest accrues.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col3 = FootedRupees(report.Rows, r => r.Tcs),
            Col8 = FootedRupees(report.Rows, r => r.Interest),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R4 TDS Nature-of-Payment summary
    //   Section | Nature | Assessable | Deducted | Deposited | Outstanding | Below-threshold count
    private void BuildTdsNatureSummary()
    {
        var report = Report.BuildTdsNatureSummary(_company, _asOf);
        Title = "TDS Nature of Payment Summary";
        Subtitle = $"{CompanyName}  —  section-wise activity as at {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Section,
                Col2 = r.Nature,
                Col3 = IndianFormat.Rupees(r.Assessable),
                Col4 = IndianFormat.Rupees(r.Deducted),
                Col5 = IndianFormat.Rupees(r.Deposited),
                Col6 = IndianFormat.Rupees(r.Outstanding),
                Col7 = r.BelowThresholdCount.ToString(),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No TDS activity in this period.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            // G8: foot Assessable (Col3) and Deposited (Col5) too — the detail rows carry both, so leaving the
            // total-row cells blank under populated columns read as an incomplete footing.
            Col3 = FootedRupees(report.Rows, r => r.Assessable),
            Col4 = FootedRupees(report.Rows, r => r.Deducted),
            Col5 = FootedRupees(report.Rows, r => r.Deposited),
            Col6 = FootedRupees(report.Rows, r => r.Outstanding),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R8 TCS Nature-of-Goods summary (mirror of R4)
    private void BuildTcsNatureSummary()
    {
        var report = Report.BuildTcsNatureSummary(_company, _asOf);
        Title = "TCS Nature of Goods Summary";
        Subtitle = $"{CompanyName}  —  code-wise activity as at {FormatDate(_asOf)}";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Code,
                Col2 = r.Nature,
                Col3 = IndianFormat.Rupees(r.Assessable),
                Col4 = IndianFormat.Rupees(r.Collected),
                Col5 = IndianFormat.Rupees(r.Deposited),
                Col6 = IndianFormat.Rupees(r.Outstanding),
                Col7 = r.BelowThresholdCount.ToString(),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "No TCS activity in this period.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            // G8: foot Assessable (Col3) and Deposited (Col5) too (mirror of the TDS summary above).
            Col3 = FootedRupees(report.Rows, r => r.Assessable),
            Col4 = FootedRupees(report.Rows, r => r.Collected),
            Col5 = FootedRupees(report.Rows, r => r.Deposited),
            Col6 = FootedRupees(report.Rows, r => r.Outstanding),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- R9 Ledgers / Parties without PAN
    //   Party | Deductee/Collectee type | PAN present? | Sections / Codes | Tax at no-PAN rate (this FY)
    private void BuildLedgersWithoutPan()
    {
        var report = Report.BuildLedgersWithoutPan(_company, _asOf);
        Title = "Ledgers without PAN";
        Subtitle = $"{CompanyName}  —  deductee / collectee parties missing a PAN (FY of {FormatDate(_asOf)})";

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.Party,
                Col2 = r.PartyType,
                Col3 = r.PanPresent ? "Yes" : "No",
                Col4 = r.Codes,
                Col5 = IndianFormat.Rupees(r.TaxAtNoPanRate),
            });

        if (report.Rows.Count == 0)
        {
            Rows.Add(new ReportRow { Col1 = "Every deductee / collectee party has a PAN.", IsHeader = true });
            return;
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col5 = FootedRupees(report.Rows, r => r.TaxAtNoPanRate),
            IsTotal = true,
        });
    }

    // =============================================================== statements reports (RQ-5 part 1)

    /// <summary>The period window driving a statement report: the chosen [From,To] (Alt+F2), else
    /// books-begin → the effective as-of. The cash-flow / funds-flow statements are period movements,
    /// so an unset window spans the whole books-to-date (matching the Day Book / Stock Summary default).</summary>
    private PeriodRange StatementPeriod =>
        _options.Period ?? new PeriodRange(BooksFrom, _asOf);

    // --------------------------------------------------------------- Cash Flow
    //   Opening Balance header · Inflows section (+ Total Inflows) · Outflows section (+ Total Outflows) ·
    //   Net Cash Flow · Closing Balance.  Particulars = ledger, Secondary = group, single Amount column.

    private void BuildCashFlow()
    {
        var period = StatementPeriod;
        var cf = CashFlow.Build(_company, period);
        Title = "Cash Flow";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        Rows.Add(ReportRow.Total("Opening Balance", cf.OpeningBalance));

        Rows.Add(new ReportRow { Particulars = "Inflows", IsHeader = true });
        foreach (var line in cf.Inflows)
            Rows.Add(ReportRow.Line(line.Name, line.Amount, line.GroupName));
        if (cf.Inflows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No inflows in this period.", IsHeader = true });
        Rows.Add(ReportRow.Total("Total Inflows", cf.TotalInflows));

        Rows.Add(new ReportRow { Particulars = "Outflows", IsHeader = true });
        foreach (var line in cf.Outflows)
            Rows.Add(ReportRow.Line(line.Name, line.Amount, line.GroupName));
        if (cf.Outflows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No outflows in this period.", IsHeader = true });
        Rows.Add(ReportRow.Total("Total Outflows", cf.TotalOutflows));

        Rows.Add(ReportRow.Total("Net Cash Flow", cf.NetCashFlow));
        Rows.Add(ReportRow.Total("Closing Balance", cf.ClosingBalance));
    }

    // --------------------------------------------------------------- Funds Flow
    //   Sources of Funds section (+ Total Sources) · Applications of Funds section (+ Total Applications).
    //   Funds From Operations / Funds Lost In Operations lead their side. Two-column statement that balances.

    private void BuildFundsFlow()
    {
        var period = StatementPeriod;
        var ff = FundsFlow.Build(_company, period);
        Title = "Funds Flow";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Sources of Funds", IsHeader = true });
        foreach (var line in ff.Sources)
            Rows.Add(ReportRow.Line(line.Name, line.Amount, line.GroupName));
        if (ff.Sources.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No sources in this period.", IsHeader = true });
        Rows.Add(ReportRow.Total("Total Sources", ff.TotalSources));

        Rows.Add(new ReportRow { Particulars = "Applications of Funds", IsHeader = true });
        foreach (var line in ff.Applications)
            Rows.Add(ReportRow.Line(line.Name, line.Amount, line.GroupName));
        if (ff.Applications.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No applications in this period.", IsHeader = true });
        Rows.Add(ReportRow.Total("Total Applications", ff.TotalApplications));
    }

    // --------------------------------------------------------------- Ratio Analysis
    //   A flat label/value dashboard grouped into Working-Capital / Capital-Structure / Profitability /
    //   Efficiency blocks. Money via IndianFormat; decimal? ratios show their value or "N/A" (null).

    private void BuildRatioAnalysis()
    {
        // Balances are as-at the effective as-of; the P&L / sales window follows the RQ-1 period (rides in _options).
        var ra = RatioAnalysis.Build(_company, _asOf, _options);
        Title = "Ratio Analysis";
        Subtitle = _options.Period is { } p
            ? $"{CompanyName}  —  as at {FormatDate(_asOf)}  (period {FormatDate(p.From)} to {FormatDate(p.To)})"
            : $"{CompanyName}  —  as at {FormatDate(_asOf)}";
        IsTwoColumn = false;

        // The reference product's Ratio Analysis is a two-column report: Principal Groups (key figures) on the
        // left, Principal Ratios (the ratios relating those figures) on the right. We render them as two
        // labelled sections one under the other (the report grid is single-column here).
        Rows.Add(new ReportRow { Particulars = "Principal Groups", IsHeader = true });
        foreach (var g in ra.PrincipalGroups)
            AddRatioMoney(g.Label, g.Value);

        Rows.Add(new ReportRow { Particulars = "Principal Ratios", IsHeader = true });
        foreach (var r in ra.PrincipalRatios)
            AddRatioLine(r);
    }

    /// <summary>Adds a Principal-Ratio row, formatting per its unit (ratio / percent / days; null → "N/A").</summary>
    private void AddRatioLine(PrincipalRatioLine ratio)
    {
        switch (ratio.Unit)
        {
            case RatioUnit.Percent: AddRatioPercent(ratio.Label, ratio.Value); break;
            case RatioUnit.Days: AddRatioDays(ratio.Label, ratio.Value); break;
            default: AddRatioValue(ratio.Label, ratio.Value); break;
        }
    }

    /// <summary>Adds a label → days row; null renders "N/A", else 0 dp with a " days" suffix.</summary>
    private void AddRatioDays(string label, decimal? days)
        => Rows.Add(new ReportRow
        {
            Particulars = label,
            Amount = days is { } v
                ? Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " days"
                : "N/A",
        });

    /// <summary>Adds a label → money row to the Ratio-Analysis dashboard (always rendered, even zero).</summary>
    private void AddRatioMoney(string label, Money value)
        => Rows.Add(new ReportRow { Particulars = label, Amount = IndianFormat.AmountAlways(value) });

    /// <summary>Adds a label → ratio row; a null (zero-denominator) ratio renders "N/A", else 2 dp.</summary>
    private void AddRatioValue(string label, decimal? ratio)
        => Rows.Add(new ReportRow { Particulars = label, Amount = FormatRatio(ratio) });

    /// <summary>Adds a label → percentage row; null renders "N/A", else 2 dp with a "%" suffix (value is ×100 already).</summary>
    private void AddRatioPercent(string label, decimal? percent)
        => Rows.Add(new ReportRow
        {
            Particulars = label,
            Amount = percent is { } v
                ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%"
                : "N/A",
        });

    /// <summary>Formats a nullable ratio to 2 dp, or "N/A" when null (a guarded divide-by-zero).</summary>
    private static string FormatRatio(decimal? ratio)
        => ratio is { } v ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "N/A";

    // =============================================================== exception reports (RQ-5 part 2)
    //   All four render through the single-amount accounting grid (Particulars | Secondary | Amount): the
    //   extra columns (godown / dates / voucher no.) ride in Particulars + Secondary, the money/quantity in
    //   the Amount cell. Grounded in the catalog §17 Exception Reports. Keyboard-first (the grid is the same
    //   focusable report grid the other reports use). No print/export/drill — per slice scope.

    // --------------------------------------------------------------- Negative Stock  (Item | Godown | Qty | Value, as-at)

    private void BuildNegativeStock()
    {
        var report = NegativeStock.Build(_company, _asOf);
        Title = "Negative Stock";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";
        IsTwoColumn = false;

        foreach (var r in report.Rows)
        {
            // Particulars = item; Secondary = godown + negative qty; Amount = (negative) value.
            Rows.Add(new ReportRow
            {
                Particulars = r.ItemName,
                Secondary = $"{r.GodownName}  ·  Qty {IndianFormat.Quantity(r.Quantity)}",
                Amount = IndianFormat.Amount(r.Value),
            });
        }

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No negative stock as at this date.", IsHeader = true });
    }

    // --------------------------------------------------------------- Negative Cash / Bank  (Ledger | As-At | Balance)

    private void BuildNegativeCashBank()
    {
        var report = NegativeCashBank.Build(_company, _asOf);
        Title = "Negative Cash / Bank";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";
        IsTwoColumn = false;

        foreach (var r in report.Rows)
        {
            // The balance is on the credit side (negative cash / bank OD); show the magnitude with a "Cr" side.
            var side = r.Balance.Side == DrCr.Debit ? "Dr" : "Cr";
            Rows.Add(new ReportRow
            {
                Particulars = r.LedgerName,
                Secondary = $"as at {FormatDate(r.AsOf)}",
                Amount = $"{IndianFormat.Amount(r.Balance.Amount)} {side}",
            });
        }

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No negative cash / bank balances as at this date.", IsHeader = true });
    }

    // --------------------------------------------------------------- Memorandum Register  (Date | Vch No | Party | Amount)

    private void BuildMemorandumRegister()
    {
        var period = StatementPeriod;
        var report = MemorandumRegister.Build(_company, period.From, period.To);
        Title = "Memorandum Register";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  Memo No. {r.FormattedNumber}",
                Secondary = r.PartyOrParticulars ?? string.Empty,
                Amount = IndianFormat.Amount(r.Amount),
            });

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No memorandum vouchers in this period.", IsHeader = true });
        else
            Rows.Add(ReportRow.Total("Total", report.Total));
    }

    // --------------------------------------------------------------- Reversing Journal Register
    //   Date | Applicable Upto | Vch No | Particulars | Amount

    private void BuildReversingJournalRegister()
    {
        var period = StatementPeriod;
        var report = ReversingJournalRegister.Build(_company, period.From, period.To);
        Title = "Reversing Journal Register";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        foreach (var r in report.Rows)
        {
            var applicable = r.ApplicableUpto is { } d ? $"Applicable upto {FormatDate(d)}" : "Applicable upto —";
            var particulars = string.IsNullOrEmpty(r.Particulars) ? applicable : $"{r.Particulars}  ·  {applicable}";
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  Rev. Jrnl No. {r.FormattedNumber}",
                Secondary = particulars,
                Amount = IndianFormat.Amount(r.Amount),
            });
        }

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No reversing journals in this period.", IsHeader = true });
        else
            Rows.Add(ReportRow.Total("Total", report.Total));
    }

    // --------------------------------------------------------------- POS Register (RQ-44; DP-6)
    //   Date | Vch No | Party | Gift · Card · Cheque · Cash | Bill Total

    private void BuildPosRegister()
    {
        var period = StatementPeriod;
        var report = PosRegister.Build(_company, period.From, period.To);
        Title = "POS Register";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        static string Tenders(PosRegisterRow r)
        {
            var parts = new List<string>();
            if (r.Gift.Amount != 0m) parts.Add($"Gift {IndianFormat.Amount(r.Gift)}");
            if (r.Card.Amount != 0m) parts.Add($"Card {IndianFormat.Amount(r.Card)}");
            if (r.Cheque.Amount != 0m) parts.Add($"Cheque {IndianFormat.Amount(r.Cheque)}");
            if (r.Cash.Amount != 0m) parts.Add($"Cash {IndianFormat.Amount(r.Cash)}");
            return parts.Count == 0 ? "—" : string.Join("  ·  ", parts);
        }

        foreach (var r in report.Rows)
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  Bill No. {r.FormattedNumber}  ·  {r.Party}",
                Secondary = Tenders(r),
                Amount = IndianFormat.Amount(r.BillTotal),
            });

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No POS bills in this period.", IsHeader = true });
        else
        {
            Rows.Add(new ReportRow
            {
                Particulars = "Tenders",
                Secondary = $"Gift {IndianFormat.Amount(report.TotalGift)}  ·  Card {IndianFormat.Amount(report.TotalCard)}" +
                            $"  ·  Cheque {IndianFormat.Amount(report.TotalCheque)}  ·  Cash {IndianFormat.Amount(report.TotalCash)}",
                IsHeader = true,
            });
            Rows.Add(ReportRow.Total("Total", report.TotalBill));
        }
    }

    // =============================================================== Wave 7 D1 — Banking documents (8.4 / 8.7)

    /// <summary>Show the bank picker — the vendor's "List of Banks" scope
    /// (<c>help.tallysolutions.com/print-cheques/</c>, "Cheque Printing Report"; the same scope is
    /// "View Cheques with Status for Specific Banks" on the Cheque Register and <b>F4</b> on the Deposit Slip).
    /// False on every other report.</summary>
    public bool ShowChequeBankPicker =>
        Kind is ReportKind.ChequePrinting
             or ReportKind.ChequeRegister
             or ReportKind.ChequeRegisterDetail
             or ReportKind.DepositSlip
             // census 8.10 - F4 scopes the e-Payments report to one remitting bank, or leaves it on All Banks.
             or ReportKind.EPayments;

    /// <summary>"All Banks", then every ledger with cheque printing enabled. Built once in the ctor.</summary>
    public ObservableCollection<ChequeBankOption> ChequeBanks { get; } = new();

    /// <summary>The bank the Cheque Printing report is scoped to; changing it re-projects the report.</summary>
    [ObservableProperty] private ChequeBankOption? _selectedChequeBank;

    partial void OnSelectedChequeBankChanged(ChequeBankOption? value)
    {
        if (ShowChequeBankPicker) Show(Kind);
    }

    /// <summary>
    /// <b>Cheque Printing</b> (census row 8.4) — <c>help.tallysolutions.com/print-cheques/</c>, section "Cheque
    /// Printing Report": the cheques pending for printing, showing the favouring name with the instrument number
    /// and date. Enter on a row drills to the paying voucher, where Ctrl+P inks the leaf.
    ///
    /// <para>🔴 <b>THE CHEQUE NUMBER RIDES IN <c>Particulars</c>, AND THAT IS THE WHOLE POINT.</b> It used to sit only in <c>ReportRow.Secondary</c>, which NEITHER egress carries — <c>ReportPrintProjector</c> emits Particulars + Amount, <c>ReportTabularProjector</c> emits label + money — so the printed, PDF-exported, CSV/XLSX-exported and emailed Cheque Printing report was a list of cheques <b>with no cheque numbers on it</b>, the one field that identifies the leaf being printed. This is the Day Book's "(Cancelled)" fix applied again: put the load-bearing fact in the cell the projections can see, rather than growing a Secondary column on every accounting report. <c>Secondary</c> keeps the bank and the instrument date, which are context, not identity.</para>
    /// <para>The report lists cheques on banks whose <c>Enable Cheque Printing</c> is on — the two v5 columns that
    /// had seventeen references in the engine and not one in the UI until this slice.</para>
    /// </summary>
    private void BuildChequePrinting()
    {
        var period = StatementPeriod;
        // The picker's "All Banks" entry carries Guid.Empty, which the engine reads as "no bank filter" — the
        // same shape its default argument has, so an unpicked report projects exactly what it always did.
        var bankId = SelectedChequeBank?.LedgerId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var rows = ChequePrinting.Build(_company, period, bankId);
        Title = "Cheque Printing";
        Subtitle = $"{CompanyName}  —  cheques drawn {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + (bankId is null ? string.Empty : $"  —  {SelectedChequeBank!.Display}");
        IsTwoColumn = false;

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  Cheque No. {r.InstrumentNumber}  ·  {r.FavouringName}  ·  Vch No. {r.FormattedNumber}",
                Secondary = $"{r.BankName}" +
                            (r.InstrumentDate is { } d ? $"  ·  dated {FormatDate(d)}" : string.Empty),
                Amount = IndianFormat.Amount(r.Amount),
                // The drill is what makes the row PRINTABLE: Enter opens the voucher, and Ctrl+P there yields the
                // cheque. A list with no drill would be a list of cheques nobody can print.
                DrillVoucherId = r.VoucherId,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow
            {
                // When a FILTER is what emptied the list, the empty state has to say so, or the operator cannot
                // tell "nothing drawn anywhere" from "nothing drawn on the bank I picked" — the same rule the
                // supplier advice's empty state keeps below.
                Particulars = bankId is not null
                    ? $"No cheques pending for printing on {SelectedChequeBank!.Display}. Choose \"All Banks\" "
                      + "above to see the cheques drawn on the other banks."
                    : "No cheques pending for printing. A cheque appears here once a Payment voucher pays "
                      + "a bank ledger with Enable Cheque Printing on, by Cheque/DD, carrying a cheque number.",
                IsHeader = true,
            });
        else
            Rows.Add(ReportRow.Total("Total", rows.Aggregate(Money.Zero, (acc, r) => acc + r.Amount)));
    }

    // =============================================================== Wave J2 — Cheque Register (census 8.5)

    /// <summary>The cheque book the detail view is scoped to, or <see cref="Guid.Empty"/> for every book.
    /// Set by the summary drill and cleared when the summary is re-shown.</summary>
    private Guid _chequeRegisterBookId = Guid.Empty;

    /// <summary>
    /// The vendor's <b>Cheque Status Filter</b> (<b>F8</b>) — <c>help.tallysolutions.com/cheque-register/</c>,
    /// "View Cheques with Specific Statuses". Empty ⇒ every bucket, which is how the report opens.
    ///
    /// <para>It cycles rather than presenting a multi-select, because the button bar carries one key: <b>all →
    /// Available → Blank → Cancelled → Unreconciled → Reconciled → Out of Period → all</b>. The caption always
    /// says which one is on, so the operator can never be looking at a filtered register that looks complete.</para>
    /// </summary>
    private ChequeRegisterStatus? _chequeStatusFilter;

    /// <summary>True on either half of the Cheque Register — drives the F8 status filter's availability.</summary>
    public bool IsChequeRegister => Kind is ReportKind.ChequeRegister or ReportKind.ChequeRegisterDetail;

    /// <summary>
    /// Which cheque leaf each detail row stands for. Kept as a side map rather than as two more fields on
    /// <c>ReportRow</c>, because <c>ReportRow</c> is shared by ~60 reports and a leaf number means nothing on any
    /// of the others; a row identity that only one report populates belongs beside that report.
    /// </summary>
    private readonly Dictionary<ReportRow, (Guid Book, string Leaf)> _chequeLeafByRow = new();

    /// <summary>
    /// <b>Alt+A — "Alter Status"</b> on the Cheque Register's leaf list
    /// (<c>help.tallysolutions.com/cheque-register/</c>). Cycles the highlighted leaf
    /// <b>Available → Blank → Cancelled → Available</b> and re-projects.
    ///
    /// <para>🔴 <b>THIS IS THE ONLY WRITER OF AN OPERATOR CHEQUE STATUS IN THE PRODUCT.</b> Without it the
    /// <c>cheque_status_overrides</c> table has storage, the register has three buckets that read it, and nothing
    /// an operator can press ever writes one — which is the dead-capability shape this whole track exists to
    /// close, reproduced one slice later.</para>
    ///
    /// <para><b>A SPENT LEAF IS REFUSED, and the refusal is the safety.</b> Marking a cheque you have already
    /// paid out as "Blank" would put a leaf that is gone back into the issuable pile. The projection already
    /// ignores such a status; refusing to record it means the stored data cannot disagree with the report either.</para>
    /// </summary>
    public bool AlterHighlightedChequeStatus(out string message)
    {
        message = string.Empty;
        if (Kind != ReportKind.ChequeRegisterDetail) return false;
        if (SelectedRow is not { } row || !_chequeLeafByRow.TryGetValue(row, out var leaf))
        {
            message = "Move to a cheque leaf first — Alt+A alters the status of the highlighted cheque.";
            return false;
        }
        if (leaf.Book == Guid.Empty)
        {
            message = "This cheque belongs to no cheque book, so it has no status to alter. Record a cheque book "
                    + "covering its number on the bank ledger master first.";
            return false;
        }
        if (row.DrillVoucherId != Guid.Empty)
        {
            message = $"Cheque No. {leaf.Leaf} has already been paid out, so its status follows the books and "
                    + "cannot be set by hand. Cancel or delete the voucher if the cheque was never issued.";
            return false;
        }

        var current = _company.FindChequeStatus(leaf.Book, leaf.Leaf)?.Status ?? ChequeStatus.Available;
        var next = current switch
        {
            ChequeStatus.Available => ChequeStatus.Blank,
            ChequeStatus.Blank => ChequeStatus.Cancelled,
            _ => ChequeStatus.Available,
        };
        _company.SetChequeStatus(leaf.Book, leaf.Leaf, next);
        message = $"Cheque No. {leaf.Leaf} is now {next}.";
        Show(Kind);
        return true;
    }

    /// <summary>F8 on the Cheque Register: advances the status filter and re-projects.</summary>
    public void CycleChequeStatusFilter()
    {
        if (!IsChequeRegister) return;
        _chequeStatusFilter = _chequeStatusFilter switch
        {
            null => ChequeRegisterStatus.Available,
            ChequeRegisterStatus.Available => ChequeRegisterStatus.Blank,
            ChequeRegisterStatus.Blank => ChequeRegisterStatus.Cancelled,
            ChequeRegisterStatus.Cancelled => ChequeRegisterStatus.Unreconciled,
            ChequeRegisterStatus.Unreconciled => ChequeRegisterStatus.Reconciled,
            ChequeRegisterStatus.Reconciled => ChequeRegisterStatus.OutOfPeriod,
            _ => null,
        };
        Show(Kind);
    }

    /// <summary>The caption for the current F8 filter, or blank when every bucket is showing.</summary>
    private string ChequeStatusFilterCaption =>
        _chequeStatusFilter is { } s ? $"  —  {StatusCaption(s)} only (F8)" : string.Empty;

    /// <summary>One summary row's count in a given bucket — the single place the six columns are addressed by
    /// enum, so the F8 narrowing and the row's own caption cannot name different numbers.</summary>
    private static int BucketCount(ChequeRegisterSummaryRow r, ChequeRegisterStatus s) => s switch
    {
        ChequeRegisterStatus.Available => r.Available,
        ChequeRegisterStatus.Unreconciled => r.Unreconciled,
        ChequeRegisterStatus.Reconciled => r.Reconciled,
        ChequeRegisterStatus.Blank => r.Blank,
        ChequeRegisterStatus.Cancelled => r.Cancelled,
        _ => r.OutOfPeriod,
    };

    /// <summary>The vendor's own wording for a bucket (<c>help.tallysolutions.com/cheque-register/</c>).</summary>
    private static string StatusCaption(ChequeRegisterStatus s) => s switch
    {
        ChequeRegisterStatus.Available => "Available",
        ChequeRegisterStatus.Unreconciled => "Unreconciled",
        ChequeRegisterStatus.Reconciled => "Reconciled",
        ChequeRegisterStatus.Blank => "Blank",
        ChequeRegisterStatus.Cancelled => "Cancelled",
        _ => "Out of Period",
    };

    /// <summary>
    /// <b>Cheque Register</b> summary (census row 8.5) —
    /// <c>help.tallysolutions.com/docs/te9rel65/Banking/Cheque_Register.htm</c>: one row per cheque book with its
    /// six status counts. Enter drills to the leaf list ("View More Details in Cheque Register").
    ///
    /// <para>🔴 <b>THE COUNTS RIDE IN <c>Particulars</c> AND <c>Secondary</c> BECAUSE ONE EGRESS CANNOT SEE THE
    /// OTHER.</b> <c>ReportPrintProjector</c> emits Particulars + Amount only, so a bucket count that lived
    /// anywhere else would print as a blank register — the same defect the Cheque Printing report had to fix for
    /// its cheque numbers. The book name and the range go in Particulars, which every projection carries.</para>
    /// </summary>
    private void BuildChequeRegister()
    {
        _chequeRegisterBookId = Guid.Empty;
        var period = StatementPeriod;
        var bankId = SelectedChequeBank?.LedgerId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var all = ChequeRegister.Summary(_company, period, bankId);
        // F8 narrows the SUMMARY to the books that actually hold a leaf in the chosen bucket, and says so in the
        // caption. 🔴 It has to do something visible here: the operator presses F8 on the report they are
        // looking at, and a chord that silently only affects the drill-down view reads as a broken key. The
        // per-book counts themselves are never filtered — a count of Available leaves is a count of Available
        // leaves whatever the filter says, and filtering it would make the summary contradict the leaf list.
        // (Narrowing the SET of books is ours; the vendor documents the filter over the cheque list itself.)
        var rows = _chequeStatusFilter is { } bucket
            ? all.Where(r => BucketCount(r, bucket) > 0).ToList()
            : all;

        Title = "Cheque Register";
        Subtitle = $"{CompanyName}  —  {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + (bankId is null ? string.Empty : $"  —  {SelectedChequeBank!.Display}")
                   + ChequeStatusFilterCaption;
        IsTwoColumn = false;

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Particulars = $"{r.BankName}  ·  {r.ChequeBookName}  ({r.FromNumber}–{r.ToNumber}, {r.Total} leaves)"
                              + $"  ·  Available {r.Available}  ·  Blank {r.Blank}  ·  Cancelled {r.Cancelled}",
                Secondary = $"Unreconciled {r.Unreconciled}  ·  Reconciled {r.Reconciled}"
                            + $"  ·  Out of period {r.OutOfPeriod}",
                DrillChequeBookId = r.ChequeBookId,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow
            {
                // The empty state has to name the route that fills it, or the operator is looking at a report
                // they cannot tell apart from a broken one.
                Particulars = bankId is not null
                    ? $"No cheque books recorded for {SelectedChequeBank!.Display}. Record one on the bank ledger "
                      + "master — Masters > Ledgers > alter the bank > Cheque Books."
                    : "No cheque books recorded. A cheque book is the range of leaf numbers your bank issued you; "
                      + "record one on the bank ledger master (Masters > Ledgers > Cheque Books) and the register "
                      + "can then tell you which leaves are still available.",
                IsHeader = true,
            });
    }

    /// <summary>
    /// <b>Cheque Register</b> detail (census row 8.5) — the leaf-by-leaf view. One row per cheque number, in the
    /// bucket it falls in, drilling to the paying voucher where one exists.
    ///
    /// <para>The vendor's "cheques which do not belong to any cheque range" case appears here as its own
    /// <see cref="ChequeRegister.NotInRangeCaption"/> rows rather than being dropped — a cheque you actually
    /// wrote that the register cannot see is worse than an untidy register.</para>
    /// </summary>
    private void BuildChequeRegisterDetail()
    {
        var period = StatementPeriod;
        var bankId = SelectedChequeBank?.LedgerId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var filter = _chequeStatusFilter is { } s ? new[] { s } : null;
        var all = ChequeRegister.Build(_company, period, bankId, filter);
        var rows = _chequeRegisterBookId == Guid.Empty
            ? all
            : all.Where(r => r.ChequeBookId == _chequeRegisterBookId).ToList();

        var book = _chequeRegisterBookId == Guid.Empty ? null : _company.FindChequeBook(_chequeRegisterBookId);
        Title = book is null ? "Cheque Register — all leaves" : $"Cheque Register — {book.Name}";
        Subtitle = $"{CompanyName}  —  {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + (bankId is null ? string.Empty : $"  —  {SelectedChequeBank!.Display}")
                   + ChequeStatusFilterCaption;
        IsTwoColumn = false;

        _chequeLeafByRow.Clear();
        foreach (var r in rows)
        {
            var reportRow = new ReportRow
            {
                // The leaf number and its bucket are the two facts this report exists to state, so both ride in
                // Particulars where every egress (print, PDF, CSV/XLSX, e-mail) carries them.
                Particulars = $"Cheque No. {r.ChequeNumber}  ·  {StatusCaption(r.Status)}"
                              + (r.VoucherDate is { } d ? $"  ·  {FormatDate(d)}" : string.Empty)
                              + (string.IsNullOrEmpty(r.FavouringName) ? string.Empty : $"  ·  {r.FavouringName}"),
                Secondary = $"{r.BankName}  ·  {r.ChequeBookName}"
                            + (string.IsNullOrEmpty(r.FormattedNumber) ? string.Empty : $"  ·  Vch No. {r.FormattedNumber}"),
                Amount = r.Amount == Money.Zero ? string.Empty : IndianFormat.Amount(r.Amount),
                DrillVoucherId = r.VoucherId ?? Guid.Empty,
            };
            _chequeLeafByRow[reportRow] = (r.ChequeBookId, r.ChequeNumber);
            Rows.Add(reportRow);
        }

        if (rows.Count == 0)
            Rows.Add(new ReportRow
            {
                Particulars = _chequeStatusFilter is { } only
                    ? $"No cheques in the {StatusCaption(only)} bucket. Press F8 to move the status filter on."
                    : "No cheque leaves to show. Record a cheque book on the bank ledger master "
                      + "(Masters > Ledgers > Cheque Books) first.",
                IsHeader = true,
            });
    }

    // =============================================================== Wave J2 — Deposit Slip (census 8.6)

    /// <summary>True on the Deposit Slip — drives the F5 Cash/Cheque switch's availability.</summary>
    public bool IsDepositSlip => Kind == ReportKind.DepositSlip;

    /// <summary>The vendor's F5 mode switch (<c>help.tallysolutions.com/deposit-slips/</c>). Cheque is the
    /// default because the cheque slip is the one with per-instrument lines to check.</summary>
    private DepositSlipKind _depositSlipKind = DepositSlipKind.Cheque;

    /// <summary>F5 on the Deposit Slip: switches between the Cash and the Cheque slip and re-projects.</summary>
    public void ToggleDepositSlipKind()
    {
        if (!IsDepositSlip) return;
        _depositSlipKind = _depositSlipKind == DepositSlipKind.Cheque ? DepositSlipKind.Cash : DepositSlipKind.Cheque;
        Show(ReportKind.DepositSlip);
    }

    /// <summary>
    /// <b>Deposit Slip</b> (census row 8.6) — <c>help.tallysolutions.com/deposit-slips/</c>: the pay-in slip for
    /// the cash or the cheques being banked, with the bank's own header block.
    ///
    /// <para>🔴 <b>A bank must be PICKED.</b> Unlike the Cheque Printing report there is no "All Banks" slip: a
    /// deposit slip is a piece of paper handed to one teller at one branch, and totalling three banks onto one
    /// would produce a document nobody can present. "All Banks" therefore asks the operator to choose rather than
    /// silently picking one for them.</para>
    ///
    /// <para><b>Two fields the vendor's slip has and ours does not print, stated rather than faked:</b> the
    /// company's telephone number (no column exists on <c>companies</c>) and the cash denomination breakdown
    /// (not in the books and not derivable). Neither is captioned, because a caption over permanent blank is the
    /// dead-field defect this project has filed three times.</para>
    /// </summary>
    private void BuildDepositSlip()
    {
        var period = StatementPeriod;
        IsTwoColumn = false;
        var kindLabel = _depositSlipKind == DepositSlipKind.Cash ? "Cash Deposit Slip" : "Cheque Deposit Slip";
        Title = kindLabel;

        var bankId = SelectedChequeBank?.LedgerId is { } id && id != Guid.Empty ? id : (Guid?)null;
        if (bankId is null || _company.FindLedger(bankId.Value) is not { } bank)
        {
            Subtitle = $"{CompanyName}  —  {FormatDate(period.From)} to {FormatDate(period.To)}";
            Rows.Add(new ReportRow
            {
                Particulars = "Choose the bank account you are paying into (F4). A deposit slip is presented at "
                              + "one branch, so it is drawn for one bank account at a time.",
                IsHeader = true,
            });
            return;
        }

        var slip = DepositSlip.Build(_company, bank, period, _depositSlipKind);
        Subtitle = $"{CompanyName}  —  {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + $"  —  {kindLabel} (F5 switches)";

        // The header block the vendor prints. Each field is shown only when it HAS a value: an "Account number:"
        // caption over a blank is exactly the shape that got three features filed as dead here.
        var header = new List<string> { $"Bank: {slip.BankName}" };
        if (!string.IsNullOrEmpty(slip.AccountNumber)) header.Add($"A/c No. {slip.AccountNumber}");
        if (!string.IsNullOrEmpty(slip.BranchName)) header.Add($"Branch: {slip.BranchName}");
        header.Add($"Account holder: {slip.AccountHolderName}");
        Rows.Add(new ReportRow { Particulars = string.Join("  ·  ", header), IsHeader = true });

        if (string.IsNullOrEmpty(slip.AccountNumber) || string.IsNullOrEmpty(slip.BranchName))
            Rows.Add(new ReportRow
            {
                Particulars = "Tip: the account number and branch print on this slip once they are recorded on "
                              + "the bank ledger master (Masters > Ledgers > Bank Identity).",
                IsHeader = true,
            });

        foreach (var l in slip.Lines)
            Rows.Add(new ReportRow
            {
                Particulars = FormatDate(l.Date)
                              + (string.IsNullOrEmpty(l.InstrumentNumber)
                                  ? string.Empty
                                  : $"  Cheque No. {l.InstrumentNumber}")
                              + (l.InstrumentDate is { } id2 ? $"  dated {FormatDate(id2)}" : string.Empty)
                              + $"  ·  {l.ReceivedFrom}",
                Secondary = $"Vch No. {l.FormattedNumber}",
                Amount = IndianFormat.Amount(l.Amount),
                DrillVoucherId = l.VoucherId,
            });

        if (slip.Lines.Count == 0)
            Rows.Add(new ReportRow
            {
                Particulars = _depositSlipKind == DepositSlipKind.Cash
                    ? $"No cash was banked into {slip.BankName} in this period. Press F5 for the cheque slip."
                    : $"No cheques were banked into {slip.BankName} in this period. Press F5 for the cash slip.",
                IsHeader = true,
            });
        else
            Rows.Add(ReportRow.Total("Total deposited", slip.Total));
    }

    // =============================================================== census 8.10 — e-Payments

    /// <summary>True on the e-Payments report — drives its Ctrl+A export and the status line under the grid.</summary>
    public bool IsEPayments => Kind == ReportKind.EPayments;

    /// <summary>The last e-Payments projection, kept so Ctrl+A exports exactly the rows on screen rather than
    /// re-deriving them from parameters that may have moved.</summary>
    private EPaymentsReport? _ePayments;

    /// <summary>Where the last payment-instruction export went, or why it did not. Shown on the report.</summary>
    [ObservableProperty] private string _ePaymentsExportStatus = string.Empty;

    /// <summary>
    /// <b>e-Payments</b> (census row 8.10) — <c>help.tallysolutions.com/e-payments-report/</c>. The electronic
    /// payments in the period, under the vendor's own section headings: <b>Ready for Sending to Bank</b> first,
    /// then <b>Incomplete/Incorrect Bank Ledger Master Details</b> and <b>Incomplete/Incorrect Transaction
    /// Details</b>. Each exception row carries the sentence that says what to fix and on which master.
    ///
    /// <para><b>F4</b> scopes it to one remitting bank; the default is All Banks, which is what the vendor's
    /// report shows. <b>Ctrl+A</b> exports the payment-instruction file for the ready rows.</para>
    ///
    /// <para>Two of the vendor's sections are deliberately ABSENT and the report says so on its own face rather
    /// than leaving an operator to wonder: "Sent to Bank (Unreconciled)" with its In Progress / Successful /
    /// Unsuccessful split needs a stored export-and-response state that this schema has no column for, and
    /// "Mismatch in Bank Details (With Masters)" needs a per-transaction copy of the payee's account to compare
    /// against the master. See <see cref="EPayments"/> for the full statement.</para>
    /// </summary>
    private void BuildEPayments()
    {
        var period = StatementPeriod;
        IsTwoColumn = false;
        Title = "e-Payments";
        EPaymentsExportStatus = string.Empty;

        var bankId = SelectedChequeBank?.LedgerId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var bank = bankId is null ? null : _company.FindLedger(bankId.Value);
        var scope = bank?.Name ?? "All Banks";

        var report = EPayments.Build(_company, bank, period);
        _ePayments = report;

        Subtitle = $"{CompanyName}  —  {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + $"  —  {scope} (F4 switches)  —  NEFT and RTGS payments";

        Section("Ready for Sending to Bank", report.ReadyForSendingToBank, showReason: false);
        Section("Incomplete/Incorrect Bank Ledger Master Details", report.IncompleteBankLedgerMaster,
            showReason: true);
        Section("Incomplete/Incorrect Transaction Details", report.IncompleteTransactionDetails,
            showReason: true);

        if (report.Rows.Count == 0)
            Rows.Add(new ReportRow
            {
                Particulars = $"No NEFT or RTGS payment left {scope} in this period. A payment is an e-payment "
                              + "when its bank allocation records the transfer as NEFT or RTGS.",
                IsHeader = true,
            });

        Rows.Add(new ReportRow
        {
            Particulars = "Ctrl+A exports a payment instruction file for the rows that are ready. The layout is "
                          + "Apex's own documented CSV — no bank publishes one this product could clone — so "
                          + "check it against your bank's upload template before you use it.",
            IsHeader = true,
        });
        Rows.Add(new ReportRow
        {
            Particulars = "Not shown: whether a payment has already been sent to the bank, and whether the bank "
                          + "accepted it. Neither is recorded anywhere in this product, so no section claims it.",
            IsHeader = true,
        });

        void Section(string heading, IReadOnlyList<EPaymentRow> section, bool showReason)
        {
            if (section.Count == 0) return;
            Rows.Add(new ReportRow { Particulars = heading, IsHeader = true });
            foreach (var r in section)
                Rows.Add(new ReportRow
                {
                    Particulars = FormatDate(r.Date)
                                  + $"  ·  {(r.PayeeName.Length == 0 ? "(no single beneficiary)" : r.PayeeName)}"
                                  + $"  ·  {r.TransactionType} from {r.BankLedgerName}"
                                  + (showReason ? $"  —  {r.Reason}" : string.Empty),
                    Secondary = $"Vch No. {r.FormattedNumber}",
                    Amount = IndianFormat.Amount(r.Amount),
                    DrillVoucherId = r.VoucherId,
                });
            if (!showReason)
                Rows.Add(ReportRow.Total("Total ready for sending to bank", report.ReadyTotal));
        }
    }

    /// <summary>
    /// <b>Ctrl+A on the e-Payments report</b> — writes the payment-instruction file for the rows that are ready
    /// (the vendor's <b>Export &gt; Payment Instructions</b>). Refuses, with a reason, when there is nothing
    /// ready: a zero-row instruction file uploaded to a bank portal is a support call, not an export.
    ///
    /// <para>Ctrl+A rather than the vendor's Alt+E because Alt+E on this product is the generic report Export
    /// panel (CSV / XLSX / PDF / HTML / XML / JSON / ASCII), which this report needs as much as any other;
    /// taking it would have removed a working route to add one. Ctrl+A is this codebase's own established key for
    /// "write this return's file" — the Kerala Flood Cess return, Form 24Q's FVU and Form 16's PDF all sit on
    /// it — so the chord is a divergence from the vendor and a match for the product it is in.</para>
    /// </summary>
    public string? ExportPaymentInstructions()
    {
        if (!IsEPayments || _ePayments is not { } report)
        {
            EPaymentsExportStatus = "Payment instructions are exported from the e-Payments report.";
            return null;
        }

        if (report.ReadyForSendingToBank.Count == 0)
        {
            EPaymentsExportStatus =
                "Nothing is ready to send. Every e-payment in this period is missing a bank account number or an "
                + "IFS code — fix the masters named above and the rows will move into 'Ready for Sending to Bank'.";
            return null;
        }

        var text = EPayments.BuildPaymentInstructionFile(_company, report);
        var folder = Apex.Desktop.Services.ExportFolderDefault.Resolve();
        var file = $"PaymentInstructions-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.csv";

        try
        {
            System.IO.Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, file);
            System.IO.File.WriteAllText(path, text);
            EPaymentsExportStatus =
                $"Saved {path} — {report.ReadyForSendingToBank.Count} payment(s), "
                + $"{IndianFormat.Amount(report.ReadyTotal)} in total.";
            return path;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            EPaymentsExportStatus = $"Could not write the payment instruction file: {ex.Message}";
            return null;
        }
    }

    /// <summary>The supplier advices the current report holds, so Ctrl+P can render the LETTER rather than the
    /// grid. Empty off this report.</summary>
    public IReadOnlyList<SupplierPaymentAdviceRow> CurrentSupplierAdvices { get; private set; }
        = Array.Empty<SupplierPaymentAdviceRow>();

    /// <summary>True for the supplier Payment Advice (census 8.7) — drives the Ctrl+P letter branch. Deliberately
    /// distinct from <see cref="ReportKind.PaymentAdvice"/>, the PAYROLL bank advice.</summary>
    public bool IsSupplierPaymentAdvice => Kind == ReportKind.SupplierPaymentAdvice;

    /// <summary>
    /// The vendor's <b>reconciled-only</b> filter on the Payment Advice
    /// (<c>help.tallysolutions.com/payment-advice/</c> — the report shows each payment as "matched (reconciled) or
    /// not" and can be narrowed to the reconciled ones). Off by default, so the report opens on everything.
    ///
    /// <para>It is on <b>F8</b>, the key this product already scopes to a report through the same door the
    /// Reorder-Status "reorder only" filter uses. Note the engine has carried this parameter since it was
    /// written; without this toggle it had no caller that could ever set it, which is the "capability no user can
    /// reach" shape this project has already filed twice.</para>
    /// </summary>
    [ObservableProperty] private bool _adviceReconciledOnly;

    /// <summary>F8 on the supplier Payment Advice: toggles the reconciled-only filter and re-projects.</summary>
    public void ToggleAdviceReconciledOnly()
    {
        if (Kind != ReportKind.SupplierPaymentAdvice) return;
        AdviceReconciledOnly = !AdviceReconciledOnly;
        Rows.Clear();
        BuildSupplierPaymentAdvice();
    }

    /// <summary>
    /// <b>Payment Advice</b> for suppliers (census row 8.7) — <c>help.tallysolutions.com/payment-advice/</c>: the
    /// payments made to suppliers, each showing whether the bank statement has matched (reconciled) it. Ctrl+P
    /// renders the letters through <c>PaymentAdvicePdf</c>.
    /// </summary>
    private void BuildSupplierPaymentAdvice()
    {
        var period = StatementPeriod;
        var advices = SupplierPaymentAdvice.Build(_company, period, reconciledOnly: AdviceReconciledOnly);
        CurrentSupplierAdvices = advices;
        Title = "Payment Advice";
        Subtitle = $"{CompanyName}  —  payments to suppliers {FormatDate(period.From)} to {FormatDate(period.To)}"
                   + (AdviceReconciledOnly ? "  —  reconciled only (F8)" : string.Empty);
        IsTwoColumn = false;

        foreach (var a in advices)
        {
            var mode = SupplierPaymentAdvice.PaymentModeText(a.PaymentMode);
            var parts = new List<string>();
            if (mode.Length > 0) parts.Add(mode);
            if (!string.IsNullOrWhiteSpace(a.InstrumentNumber)) parts.Add("No. " + a.InstrumentNumber);
            if (!string.IsNullOrWhiteSpace(a.BankName)) parts.Add(a.BankName);
            // The vendor's headline fact about each row is whether it is matched; say it in words, not a symbol.
            parts.Add(a.IsReconciled ? "reconciled" : "not reconciled");

            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(a.Date)}  Vch No. {a.FormattedNumber}  ·  {a.PartyName}",
                Secondary = string.Join("  ·  ", parts),
                Amount = IndianFormat.Amount(a.NetPaid),
                DrillVoucherId = a.VoucherId,
            });
        }

        if (advices.Count == 0)
            Rows.Add(new ReportRow
            {
                // An empty report must read as an answer, not as a breakage — and when a FILTER is what emptied
                // it, the empty state has to say so or the operator cannot tell "nothing paid" from "nothing
                // cleared yet".
                Particulars = AdviceReconciledOnly
                    ? "No reconciled payments to suppliers in this period. Press F8 to include the payments the "
                      + "bank statement has not cleared yet."
                    : "No payments to suppliers in this period.",
                IsHeader = true,
            });
        else
            Rows.Add(ReportRow.Total("Total Paid", advices.Aggregate(Money.Zero, (acc, a) => acc + a.NetPaid)));
    }

    // =============================================================== Payroll presentation reports (Phase 8 slice 8)

    // The four wide payroll reports share the dynamic PayrollColumns/PayrollRows matrix; the Payslip is a bespoke
    // single-employee detail. Every figure is a deterministic projection of the POSTED Payroll voucher for the wage
    // month (F1/F2) — so the reports reflect what was actually paid (they carry As-User-Defined-Value amounts and
    // omit a cancelled / never-posted run) and reconcile to the books to the paisa. Money is 2-decimal rupees (blank
    // cells in the body, always-rendered figures in the Grand-Total row); attendance columns are day counts.

    private const double PayrollLabelWidth = 200;
    private const double PayrollNumWidth = 112;
    private const double PayrollDayWidth = 96;

    private static PayrollMatrixColumnVm PayCol(string header, double width, bool numeric)
        => new() { Header = header, Width = width, IsNumeric = numeric };

    private static PayrollMatrixCellVm PayCell(string text, double width, bool numeric)
        => new() { Text = text, Width = width, IsNumeric = numeric };

    /// <summary>The [from,to] wage-month window the payroll reports project (the selected month, else the FY start).</summary>
    private (DateOnly From, DateOnly To) PayrollPeriod()
    {
        var m = SelectedPayrollMonth ?? PayrollMonths.FirstOrDefault();
        var from = m?.FirstDay ?? new DateOnly(_company.FinancialYearStart.Year, _company.FinancialYearStart.Month, 1);
        var to = m?.LastDay ?? from.AddMonths(1).AddDays(-1);
        return (from, to);
    }

    /// <summary>The full employee roster — the payroll reports project the <b>posted Payroll voucher</b> for the wage
    /// month and internally keep only employees who actually have posted payroll lines (a cancelled / never-posted
    /// member yields no row), so the roster is passed straight through with no recompute and no error-swallowing.</summary>
    private List<Guid> EligiblePayrollEmployees()
        => _company.Employees.Select(e => e.Id).ToList();

    /// <summary>Sets the payroll-report subtitle (company + wage month) and returns the period.</summary>
    private (DateOnly From, DateOnly To) BeginPayrollReport(string title)
    {
        var (from, to) = PayrollPeriod();
        Title = title;
        Subtitle = $"{CompanyName}  —  Wage month {from.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        IsTwoColumn = false;
        IsPayrollEmpty = false;
        PayrollEmptyNote = string.Empty;
        return (from, to);
    }

    // ======================================================= W7-D2 payroll statutory forms (census 7.20 / 7.21)
    //
    // The five PF forms (3A, 5, 6A, 10, 12A) and the three ESI forms (3, 5, 6). EVERY figure below is read off
    // Apex.Ledger's PfStatutoryForms / EsiStatutoryForms projections, which in turn read off the SAME PfEcr /
    // EsiMonthlyContribution / PayrollComputationService the payroll voucher posts. Nothing here computes PF or
    // ESI. Columns are captioned as the forms caption themselves; the columns this book does not maintain print
    // BLANK with a footnote rather than being dropped or invented.

    private const double StatSerialWidth = 56;
    private const double StatNameWidth = 190;
    private const double StatCodeWidth = 130;
    private const double StatDateWidth = 110;
    private const double StatNumWidth = 118;

    /// <summary>
    /// The Form 12A particulars column. 🔴 Widened from 300 to fit its OWN longest label in full:
    /// <c>"Contribution Payable by the Employer — A/c No. 10 (Pension Fund)"</c> is 64 characters, which at the
    /// shipped monospace advance (6.8726px at the cell's 12.5pt) needs 440px of text plus the template's 16px
    /// padding = 456. At 300 the three "Contribution Payable by the Employer" rows all cut at or before their
    /// account number, so three different money rows read identically. Sized so nothing on the form truncates at
    /// all, which is a stronger fix than re-ordering the caption.
    /// </summary>
    private const double StatWideLabelWidth = 460;

    /// <summary>
    /// Form 6A page 2's five challan account-head columns. 🔴 These five captions are the ONLY thing telling
    /// A/c 1, 2, 10, 21 and 22 apart, and at <see cref="StatNumWidth"/> (118) an operator read "EPF Contributio"
    /// and nothing more — five different money columns, visually identical, on the page where 6A reconciles to
    /// the challans. The captions now LEAD with the account number (see BuildPfForm6A) and the column is wide
    /// enough that the differentiator is never the part that gets cut.
    /// </summary>
    private const double StatChallanHeadWidth = 190;

    // The PF Form 3A member identity band — one column per statutory identity field, each sized to the longest
    // value that field actually carries (a PF account number is the widest, at 26 characters). These are the
    // widths that replaced the single concatenated identity cell; see BuildPfForm3A.
    private const double IdentityNameWidth = 190;
    private const double IdentityAccountWidth = 230;
    private const double IdentityUanWidth = 120;
    private const double IdentityDobWidth = 116;
    private const double IdentitySexWidth = 60;
    private const double IdentityJoinedWidth = 130;
    private const double IdentityFatherWidth = 170;

    /// <summary>Whole-rupee display of a statutory-form integer figure (always rendered, even zero — a statutory
    /// column that is genuinely nil must read "0", not blank; blank on these forms means "not maintained").</summary>
    private static string RupeeCell(long value) => IndianFormat.RupeesAlways(value);

    /// <summary>A date as the statutory forms print it, or an em dash when the master does not carry one.</summary>
    private static string DateCell(DateOnly? value)
        => value is { } d ? d.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "—";

    /// <summary>
    /// Runs a statutory-form projection that reaches the <see cref="PfEcr"/> / <see cref="EsiMonthlyContribution"/>
    /// engines, and turns the two failures those engines <b>throw</b> on into an empty form with an explanation.
    ///
    /// <para>🔴 This is not defensive decoration. <see cref="PfEcr.Build"/> throws
    /// <see cref="InvalidOperationException"/> outright when a PF-applicable member has no valid 12-digit UAN (it
    /// keys the ECR line on it) or has no effective salary structure for the month — both of which are ordinary
    /// states of a half-set-up payroll. <see cref="ReportsViewModel.Show"/> has no exception handler around its
    /// build switch, so without this the throw escapes the menu activation and takes the shell down. The shipped
    /// PF ECR page already catches exactly this pair (<c>PfEcrReportViewModel.cs:192</c>) and shows the message
    /// instead of dying; the statutory forms, which call the same engine, must behave the same way.</para>
    /// </summary>
    private void RunStatutoryForm(Action build)
    {
        try
        {
            build();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Leave whatever the projection managed to add — a half-filled grid under a message that names the
            // member is more useful than a blank one — but make the failure impossible to mistake for "no data".
            IsPayrollEmpty = true;
            PayrollEmptyNote = ex.Message;
        }
    }

    /// <summary>Adds a footnote line and lights the footnote panel.</summary>
    private void Footnote(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || PayrollFootnotes.Contains(text)) return;
        PayrollFootnotes.Add(text);
        HasPayrollFootnotes = true;
    }

    /// <summary>Builds a matrix row from (text, width, numeric) triples.</summary>
    private static PayrollMatrixRowVm StatRow(bool isTotal, params (string Text, double Width, bool Numeric)[] cells)
        => new()
        {
            IsTotal = isTotal,
            Cells = cells.Select(c => PayCell(c.Text, c.Width, c.Numeric)).ToList(),
        };

    /// <summary>
    /// Repopulates <see cref="StatutoryPeriods"/> for <paramref name="kind"/>: PF <b>currency periods</b>
    /// (1 Mar … 28/29 Feb) for Forms 3A / 6A, ESI <b>contribution periods</b> (Apr–Sep / Oct–Mar) for ESI Forms
    /// 5 / 6, and nothing at all for every other report. Both lists are built from
    /// <see cref="PayrollStatutoryPeriods"/> so the picker and the engine can never disagree about where a period
    /// starts; neither list offers an arbitrary date, because neither form is defined over one.
    /// </summary>
    private void RebuildStatutoryPeriods(ReportKind kind)
    {
        var wanted = kind is ReportKind.PfForm3A or ReportKind.PfForm6A
            ? StatutoryPeriodFamily.PfCurrency
            : kind is ReportKind.EsiForm5 or ReportKind.EsiForm6
                ? StatutoryPeriodFamily.EsiContribution
                : StatutoryPeriodFamily.None;

        if (wanted == _statutoryPeriodFamily) return;   // same family ⇒ keep the user's selection across forms
        _statutoryPeriodFamily = wanted;

        _rebuildingStatutoryPeriods = true;
        try
        {
            StatutoryPeriods.Clear();
            SelectedStatutoryPeriod = null;
            if (wanted == StatutoryPeriodFamily.None) return;

            var fyStart = new DateOnly(_company.FinancialYearStart.Year, _company.FinancialYearStart.Month, 1);
            if (wanted == StatutoryPeriodFamily.PfCurrency)
            {
                // The currency period containing the financial-year start, and the two before it.
                var start = PayrollStatutoryPeriods.CurrencyPeriodStart(fyStart);
                for (var i = 0; i < 3; i++)
                {
                    var s = start.AddYears(-i);
                    StatutoryPeriods.Add(new StatutoryPeriodOption
                    {
                        From = s,
                        To = PayrollStatutoryPeriods.CurrencyPeriodEnd(s),
                    });
                }
            }
            else
            {
                // The two contribution periods of the financial year, then the two before them — newest first.
                // Only real contribution periods are offered; the form is not defined over an arbitrary window.
                for (var i = 0; i < 4; i++)
                {
                    var probe = fyStart.AddMonths(6 * (1 - i));   // Oct of the FY, Apr of the FY, Oct prior, Apr prior
                    StatutoryPeriods.Add(new StatutoryPeriodOption
                    {
                        From = PayrollStatutoryPeriods.EsiContributionPeriodStart(probe),
                        To = PayrollStatutoryPeriods.EsiContributionPeriodEnd(probe),
                    });
                }
            }

            SelectedStatutoryPeriod = StatutoryPeriods.FirstOrDefault();
        }
        finally { _rebuildingStatutoryPeriods = false; }
    }

    private enum StatutoryPeriodFamily { None, PfCurrency, EsiContribution }

    private StatutoryPeriodFamily _statutoryPeriodFamily = StatutoryPeriodFamily.None;

    /// <summary>The statutory period the current form projects (the selection, else the first offered).</summary>
    private (DateOnly From, DateOnly To) StatutoryPeriod()
    {
        var p = SelectedStatutoryPeriod ?? StatutoryPeriods.FirstOrDefault();
        if (p is not null) return (p.From, p.To);
        var fyStart = new DateOnly(_company.FinancialYearStart.Year, _company.FinancialYearStart.Month, 1);
        var s = PayrollStatutoryPeriods.CurrencyPeriodStart(fyStart);
        return (s, PayrollStatutoryPeriods.CurrencyPeriodEnd(s));
    }

    /// <summary>Sets a statutory form's title + subtitle and returns its period; mirrors
    /// <see cref="BeginPayrollReport"/> for the multi-month forms.</summary>
    private (DateOnly From, DateOnly To) BeginStatutoryPeriodForm(string title, string periodCaption, string? code)
    {
        var (from, to) = StatutoryPeriod();
        Title = title;
        Subtitle = $"{CompanyName}  —  {periodCaption} {from.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}"
                 + $" to {to.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}"
                 + (string.IsNullOrWhiteSpace(code) ? string.Empty : $"  ·  Code {code}");
        IsTwoColumn = false;
        IsPayrollEmpty = false;
        PayrollEmptyNote = string.Empty;
        return (from, to);
    }

    /// <summary>Sets a monthly statutory form's title + subtitle and returns its wage month.</summary>
    private (DateOnly From, DateOnly To) BeginStatutoryMonthForm(string title, string? code)
    {
        var (from, to) = PayrollPeriod();
        Title = title;
        Subtitle = $"{CompanyName}  —  Month {from.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}"
                 + (string.IsNullOrWhiteSpace(code) ? string.Empty : $"  ·  Code {code}");
        IsTwoColumn = false;
        IsPayrollEmpty = false;
        PayrollEmptyNote = string.Empty;
        return (from, to);
    }

    // ------------------------------------------------------------------------------------------- PF Form 3A
    /// <summary>
    /// PF <b>Form 3A</b> — the per-member annual contribution card over the currency period. Each member gets an
    /// identity band, twelve month rows and a footing total, in sequence; the whole roster is on one scrolling
    /// grid rather than one card per screen.
    /// </summary>
    private void BuildPfForm3A()
    {
        var (from, _) = BeginStatutoryPeriodForm("PF Form 3A — Contribution Card",
            "Currency period", _company.PfConfig?.EstablishmentCode);
        var form = PfStatutoryForms.BuildForm3A(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Month", StatNameWidth, false),
            ("Amount of Wages", StatNumWidth, true),
            ($"Worker's EPF @ {form.Members.FirstOrDefault()?.StatutoryRateOfContribution ?? PfStatutoryForms.StatutoryRate(_company)}", StatNumWidth, true),
            ("Higher Rate of Vol. Contribution", StatNumWidth, true),
            ("Employer's EPF Difference", StatNumWidth, true),
            ("Pension Fund Contribution", StatNumWidth, true),
            ("Refund of Advance", StatNumWidth, true),
            ("Non-Contributing Days", StatNumWidth, true),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var card in form.Members)
        {
            var m = card.Member;

            // 🔴 The identity block gets ONE CELL PER STATUTORY FIELD, captioned, not one concatenated string.
            // It used to be a single ~190-character run packed into the 190px Month column while the row's other
            // seven cells sat empty; the cell trims with CharacterEllipsis, so ~86% of the block — the A/c
            // number, the UAN, the date of birth, the sex and the fund-joining date — was silently cut, and every
            // figure on the twelve rows below was attributed to a member the reader could not identify. Widths
            // are sized to the longest real value each field carries (see IdentityBand*Width).
            PayrollRows.Add(StatRow(false,
                ("Name of Member", IdentityNameWidth, false),
                ("PF Account Number", IdentityAccountWidth, false),
                ("UAN", IdentityUanWidth, false),
                ("Date of Birth", IdentityDobWidth, false),
                ("Sex", IdentitySexWidth, false),
                ("Joined the Fund", IdentityJoinedWidth, false),
                ("Father's/Husband's", IdentityFatherWidth, false)));
            PayrollRows.Add(StatRow(true,
                (m.Name, IdentityNameWidth, false),
                (string.IsNullOrEmpty(m.AccountNumber) ? "—" : m.AccountNumber, IdentityAccountWidth, false),
                (string.IsNullOrEmpty(m.Uan) ? "—" : m.Uan, IdentityUanWidth, false),
                (DateCell(m.DateOfBirth), IdentityDobWidth, false),
                (string.IsNullOrEmpty(m.Sex) ? "—" : m.Sex, IdentitySexWidth, false),
                (DateCell(m.DateOfJoiningTheFund), IdentityJoinedWidth, false),
                ("____________", IdentityFatherWidth, false)));   // not maintained — ruled blank, see the footnote

            foreach (var row in card.Months)
                PayrollRows.Add(StatRow(false,
                    (row.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture), StatNameWidth, false),
                    (RupeeCell(row.AmountOfWages), StatNumWidth, true),
                    (RupeeCell(row.WorkersEpf), StatNumWidth, true),
                    (string.Empty, StatNumWidth, true),                       // higher voluntary RATE — not maintained
                    (RupeeCell(row.EmployerEpfDifference), StatNumWidth, true),
                    (RupeeCell(row.PensionFundContribution), StatNumWidth, true),
                    // 🔴 Refund of Advance and Non-Contributing Days print BLANK, not "0". By this form's own
                    // convention a rendered "0" asserts the figure is genuinely nil, and neither of these can be:
                    // PfEcr hardcodes RefundOfAdvances to 0 with no parameter to supply it, and nothing in the
                    // product ever passes its optional ncpDaysByEmployee. A literal 0 here would be an unfounded
                    // statutory assertion, so the columns are ruled and blank with the footnote below.
                    (string.Empty, StatNumWidth, true),
                    (string.Empty, StatNumWidth, true)));

            PayrollRows.Add(StatRow(true,
                ("Total", StatNameWidth, false),
                (RupeeCell(card.TotalAmountOfWages), StatNumWidth, true),
                (RupeeCell(card.TotalWorkersEpf), StatNumWidth, true),
                (string.Empty, StatNumWidth, true),
                (RupeeCell(card.TotalEmployerEpfDifference), StatNumWidth, true),
                (RupeeCell(card.TotalPensionFundContribution), StatNumWidth, true),
                (string.Empty, StatNumWidth, true),      // Refund of Advance — not maintained, as above
                (string.Empty, StatNumWidth, true)));    // Non-Contributing Days — not maintained, as above
        }

        Footnote("Father's / Husband's Name and the Higher Rate of Voluntary Contribution: "
               + PfStatutoryForms.NotMaintainedNote);
        Footnote("Refund of Advance and Non-Contributing Service Days: " + PfStatutoryForms.NotMaintainedNote);
        MarkStatutoryFormEmpty(form.Members.Count == 0,
            "No member is enrolled in the Provident Fund — Form 3A has no contribution card to draw for this "
            + "currency period.");
    }

    // ------------------------------------------------------------------------------------------- PF Form 6A
    /// <summary>
    /// PF <b>Form 6A</b> — the consolidated annual statement. Page 1 (per member) is the main grid; page 2 (the
    /// twelve monthly challan remittances) is the SECOND section, with its own columns, because that is where 6A
    /// reconciles to the challans and its account heads are nothing like page 1's.
    /// </summary>
    private void BuildPfForm6A()
    {
        var (from, _) = BeginStatutoryPeriodForm("PF Form 6A — Annual Statement of Contribution",
            "Currency period", _company.PfConfig?.EstablishmentCode);
        var form = PfStatutoryForms.BuildForm6A(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Account Number", StatCodeWidth, false),
            ("Name of Member", StatNameWidth, false),
            ("Wages, Retaining Allowance & DA", StatNumWidth, true),
            ($"Worker's Contribution @ {form.StatutoryRateOfContribution}", StatNumWidth, true),
            ("Employer's EPF Difference", StatNumWidth, true),
            ("Pension Fund Contribution", StatNumWidth, true),
            ("Refund of Advance", StatNumWidth, true),
            ("Rate of Higher Vol. Contribution", StatNumWidth, true),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var r in form.Members)
            PayrollRows.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (string.IsNullOrEmpty(r.Member.AccountNumber) ? "—" : r.Member.AccountNumber, StatCodeWidth, false),
                (r.Member.Name, StatNameWidth, false),
                (RupeeCell(r.Wages), StatNumWidth, true),
                (RupeeCell(r.WorkersContribution), StatNumWidth, true),
                (RupeeCell(r.EmployerEpfDifference), StatNumWidth, true),
                (RupeeCell(r.PensionFundContribution), StatNumWidth, true),
                (string.Empty, StatNumWidth, true),                            // Refund of Advance — not maintained
                (string.Empty, StatNumWidth, true)));                          // higher voluntary RATE — not maintained

        PayrollRows.Add(StatRow(true,
            (string.Empty, StatSerialWidth, true), (string.Empty, StatCodeWidth, false),
            ("Grand Total", StatNameWidth, false),
            (RupeeCell(form.TotalWages), StatNumWidth, true),
            (RupeeCell(form.TotalWorkersContribution), StatNumWidth, true),
            (RupeeCell(form.TotalEmployerEpfDifference), StatNumWidth, true),
            (RupeeCell(form.TotalPensionFundContribution), StatNumWidth, true),
            (string.Empty, StatNumWidth, true),      // Refund of Advance — not maintained, see the footnote
            (string.Empty, StatNumWidth, true)));

        // ---- Page 2: the twelve monthly remittances (its OWN column band). ----
        PayrollSection2Title = "Page 2 — Monthly remittances (challan account heads)";
        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Month / Year", StatCodeWidth, false),
            // 🔴 The account number LEADS every caption. It is the only differentiator between these five heads,
            // and trailing it meant all five truncated to the same unreadable stem in their column.
            ("A/c No. 1 — EPF Contributions incl. Refund of Advances", StatChallanHeadWidth, true),
            ("A/c No. 10 — Pension Fund Contributions", StatChallanHeadWidth, true),
            ("A/c No. 21 — EDLI Contribution", StatChallanHeadWidth, true),
            ("A/c No. 2 — Adm. Charges", StatChallanHeadWidth, true),
            ("A/c No. 22 — EDLI Adm. Charges", StatChallanHeadWidth, true),
            ("Date of Remittance", StatDateWidth, false),
        })
            PayrollColumns2.Add(PayCol(header, width, numeric));

        foreach (var r in form.Remittances)
            PayrollRows2.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (r.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture), StatCodeWidth, false),
                // Each figure carries its HEADER's width, not StatNumWidth — a value cell narrower than its
                // header slides every later column left and the figures stop sitting under their own captions.
                (RupeeCell(r.EpfContributionsAccount1), StatChallanHeadWidth, true),
                (RupeeCell(r.PensionFundContributionsAccount10), StatChallanHeadWidth, true),
                (RupeeCell(r.EdliContributionAccount21), StatChallanHeadWidth, true),
                (RupeeCell(r.AdminChargesAccount2), StatChallanHeadWidth, true),
                (RupeeCell(r.EdliAdminChargesAccount22), StatChallanHeadWidth, true),
                (string.Empty, StatDateWidth, false)));                        // challan fact — see the footnote
        HasPayrollSection2 = PayrollRows2.Count > 0;

        Footnote("Rate of Higher Voluntary Contribution and Refund of Advance: "
               + PfStatutoryForms.NotMaintainedNote);
        Footnote(PfStatutoryForms.ChallanFactNote);
        MarkStatutoryFormEmpty(form.Members.Count == 0,
            "No member is enrolled in the Provident Fund — Form 6A has nothing to consolidate for this currency "
            + "period.");
    }

    // -------------------------------------------------------------------------------------------- PF Form 5
    /// <summary>PF <b>Form 5</b> — the monthly return of members newly joining the Fund.</summary>
    private void BuildPfForm5()
    {
        var (from, _) = BeginStatutoryMonthForm("PF Form 5 — Return of Employees Qualifying for Membership",
            _company.PfConfig?.EstablishmentCode);
        var form = PfStatutoryForms.BuildForm5(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Account No.", StatCodeWidth, false),
            ("Name of Employee", StatNameWidth, false),
            ("Father's / Husband's Name", StatNameWidth, false),
            ("Date of Birth", StatDateWidth, false),
            ("Sex", StatSerialWidth, false),
            ("Date of Joining the Fund", StatDateWidth, false),
            ("Total Period of Previous Service", StatWideLabelWidth, false),
            ("Remarks", StatNameWidth, false),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var r in form.Rows)
            PayrollRows.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (string.IsNullOrEmpty(r.Member.AccountNumber) ? "—" : r.Member.AccountNumber, StatCodeWidth, false),
                (r.Member.Name, StatNameWidth, false),
                (string.Empty, StatNameWidth, false),                           // not maintained
                (DateCell(r.Member.DateOfBirth), StatDateWidth, false),
                (string.IsNullOrEmpty(r.Member.Sex) ? "—" : r.Member.Sex, StatSerialWidth, false),
                (DateCell(r.Member.DateOfJoiningTheFund), StatDateWidth, false),
                (string.Empty, StatWideLabelWidth, false),                      // previous service — not maintained
                (string.Empty, StatNameWidth, false)));

        Footnote("Father's / Husband's Name and Total Period of Previous Service: " + PfStatutoryForms.NotMaintainedNote);
        MarkPayrollEmpty(form.Rows.Count == 0);
        if (form.Rows.Count == 0)
            PayrollEmptyNote = "No member joined the Fund in this month — a nil return, not a missing one.";
    }

    // ------------------------------------------------------------------------------------------- PF Form 10
    /// <summary>PF <b>Form 10</b> — the monthly return of members leaving service.</summary>
    private void BuildPfForm10()
    {
        // The form's own title names its month — "Return of the members leaving service during the month of …" —
        // so the month is resolved before the title is built rather than after.
        var month = PayrollPeriod().From;
        var (from, _) = BeginStatutoryMonthForm(
            "PF Form 10 — Return of Members Leaving Service during "
            + month.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            _company.PfConfig?.EstablishmentCode);
        var form = PfStatutoryForms.BuildForm10(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Account No.", StatCodeWidth, false),
            ("Name of Member", StatNameWidth, false),
            ("Father's / Husband's Name", StatNameWidth, false),
            ("Date of Leaving Service", StatDateWidth, false),
            ("Reason for Leaving", StatWideLabelWidth, false),
            ("Remarks", StatNameWidth, false),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var r in form.Rows)
            PayrollRows.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (string.IsNullOrEmpty(r.Member.AccountNumber) ? "—" : r.Member.AccountNumber, StatCodeWidth, false),
                (r.Member.Name, StatNameWidth, false),
                (string.Empty, StatNameWidth, false),                           // not maintained
                (DateCell(r.Member.DateOfLeavingService), StatDateWidth, false),
                (string.Empty, StatWideLabelWidth, false),                      // not maintained
                (string.Empty, StatNameWidth, false)));

        Footnote("Father's / Husband's Name and Reason for Leaving: " + PfStatutoryForms.NotMaintainedNote);
        MarkPayrollEmpty(form.Rows.Count == 0);
        if (form.Rows.Count == 0)
            PayrollEmptyNote = "No member left service in this month — a nil return, not a missing one.";
    }

    // ------------------------------------------------------------------------------------------ PF Form 12A
    /// <summary>
    /// PF <b>Form 12A</b> — the monthly statement of contribution. Due and Remitted are reported <b>side by
    /// side</b>: every Due figure comes from our posted books through the ECR projection, every Remitted figure is
    /// a fact about a bank challan and is therefore left blank. Defaulting Remitted to Due would print an
    /// assertion that the money was remitted.
    /// </summary>
    private void BuildPfForm12A()
    {
        var (from, _) = BeginStatutoryMonthForm("PF Form 12A — Statement of Contribution",
            _company.PfConfig?.EstablishmentCode);
        var form = PfStatutoryForms.BuildForm12A(_company, from);

        PayrollColumns.Add(PayCol("Particulars", StatWideLabelWidth, false));
        PayrollColumns.Add(PayCol("Amount Due (₹)", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Amount Remitted (₹)", StatNumWidth, true));

        void Line(string label, string due, string remitted, bool total = false)
            => PayrollRows.Add(StatRow(total,
                (label, StatWideLabelWidth, false), (due, StatNumWidth, true), (remitted, StatNumWidth, true)));

        Line("Wages on which the Contributions are Payable",
            RupeeCell(form.WagesOnWhichContributionsArePayable), string.Empty);
        Line("Contribution Recovered from the Employees — A/c No. 1",
            RupeeCell(form.ContributionRecoveredFromEmployeesAccount1), string.Empty);
        Line("Contribution Payable by the Employer — A/c No. 1",
            RupeeCell(form.ContributionPayableByEmployerAccount1), string.Empty);
        Line("Contribution Payable by the Employer — A/c No. 10 (Pension Fund)",
            RupeeCell(form.ContributionPayableByEmployerAccount10), string.Empty);
        Line("Contribution Payable by the Employer — A/c No. 21 (EDLI)",
            RupeeCell(form.ContributionPayableByEmployerAccount21), string.Empty);
        Line("Administrative Charges — A/c No. 2",
            RupeeCell(form.AdministrativeChargesDueAccount2), string.Empty);
        Line("Administrative Charges — A/c No. 22 (EDLI)",
            RupeeCell(form.AdministrativeChargesDueAccount22), string.Empty);
        Line("Total",
            RupeeCell(form.ContributionRecoveredFromEmployeesAccount1
                    + form.ContributionPayableByEmployerAccount1
                    + form.ContributionPayableByEmployerAccount10
                    + form.ContributionPayableByEmployerAccount21
                    + form.AdministrativeChargesDueAccount2
                    + form.AdministrativeChargesDueAccount22),
            string.Empty, total: true);
        Line("Date of Remittance", "—", string.Empty);
        Line("Details of Subscribers (number contributing this month)",
            form.DetailsOfSubscribers.ToString(CultureInfo.InvariantCulture), string.Empty);
        Line("Currency Period from",
            form.CurrencyPeriodFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture), string.Empty);
        Line("Group Code", string.Empty, string.Empty);

        Footnote(PfStatutoryForms.ChallanFactNote);
        Footnote("Group Code: " + PfStatutoryForms.NotMaintainedNote);
        MarkPayrollEmpty(false);
    }

    // ------------------------------------------------------------------------------------------- ESI Form 3
    /// <summary>ESI <b>Form 3</b> — the monthly return of declaration forms (Reg. 14).</summary>
    private void BuildEsiForm3()
    {
        var (from, _) = BeginStatutoryMonthForm("ESI Form 3 — Return of Declaration Forms",
            _company.EsiConfig?.EmployerCode);
        var form = EsiStatutoryForms.BuildForm3(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Name of Employee", StatNameWidth, false),
            ("Distinguishing Number with Employer", StatCodeWidth, false),
            ("Father's or Husband's Name", StatNameWidth, false),
            ("Insurance No. allotted by the Corporation", StatWideLabelWidth, false),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var r in form.Rows)
            PayrollRows.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (r.Member.Name, StatNameWidth, false),
                (string.IsNullOrEmpty(r.Member.DistinguishingNumber) ? "—" : r.Member.DistinguishingNumber, StatCodeWidth, false),
                (string.Empty, StatNameWidth, false),                           // not maintained
                (string.Empty, StatWideLabelWidth, false)));                    // entered at the branch office

        Footnote("Father's or Husband's Name: " + EsiStatutoryForms.NotMaintainedNote);
        Footnote(EsiStatutoryForms.BranchOfficeColumnNote);
        MarkPayrollEmpty(form.Rows.Count == 0);
        if (form.Rows.Count == 0)
            PayrollEmptyNote = "No insured person entered coverage in this month — a nil return, not a missing one.";
    }

    // ------------------------------------------------------------------------------------------- ESI Form 5
    /// <summary>
    /// ESI <b>Form 5</b> — the half-yearly return of contributions (Reg. 26). Column 7 is column 5 ÷ column 4, the
    /// formula the column's own caption states; it is NOT Form 6's divisor.
    /// </summary>
    private void BuildEsiForm5()
    {
        var (from, _) = BeginStatutoryPeriodForm("ESI Form 5 — Return of Contributions",
            "Contribution period", _company.EsiConfig?.EmployerCode);
        var form = EsiStatutoryForms.BuildForm5(_company, from);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Sl. No.", StatSerialWidth, true),
            ("Insurance No.", StatCodeWidth, false),
            ("Name of the Insured Person", StatNameWidth, false),
            ("No. of Days for which Wages Paid / Payable", StatNumWidth, true),
            ("Total Amount of Wages Paid / Payable", StatNumWidth, true),
            ("Employees' Contribution Deducted", StatNumWidth, true),
            ("Average Daily Wages (5 ÷ 4)", StatNumWidth, true),
            ("Still Working within the Insurable Wages Ceiling", StatCodeWidth, false),
            ("Name of the Dispensary of IP", StatNameWidth, false),
            ("Remarks", StatNameWidth, false),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var r in form.Rows)
            PayrollRows.Add(StatRow(false,
                (r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                (string.IsNullOrEmpty(r.Member.InsuranceNumber) ? "—" : r.Member.InsuranceNumber, StatCodeWidth, false),
                (r.Member.Name, StatNameWidth, false),
                (r.NoOfDaysWagesPaid.ToString(CultureInfo.InvariantCulture), StatNumWidth, true),
                (RupeeCell(r.TotalWages), StatNumWidth, true),
                (RupeeCell(r.EmployeesContributionDeducted), StatNumWidth, true),
                (IndianFormat.AmountAlways(r.AverageDailyWages), StatNumWidth, true),
                (r.StillWorkingWithinCeiling ? "Yes" : "No", StatCodeWidth, false),
                (string.Empty, StatNameWidth, false),                           // dispensary — not maintained
                (string.Empty, StatNameWidth, false)));

        PayrollRows.Add(StatRow(true,
            (string.Empty, StatSerialWidth, true), (string.Empty, StatCodeWidth, false),
            ("Grand Total", StatNameWidth, false),
            (form.TotalDays.ToString(CultureInfo.InvariantCulture), StatNumWidth, true),
            (RupeeCell(form.TotalWages), StatNumWidth, true),
            (RupeeCell(form.TotalEmployeesContribution), StatNumWidth, true),
            (string.Empty, StatNumWidth, true), (string.Empty, StatCodeWidth, false),
            (string.Empty, StatNameWidth, false), (string.Empty, StatNameWidth, false)));

        Footnote("Name of the Dispensary of IP: " + EsiStatutoryForms.NotMaintainedNote);
        Footnote("\"Still working\" is read from the member's date of leaving service; a member with no recorded "
               + "date of leaving is reported as still working.");
        MarkStatutoryFormEmpty(form.Rows.Count == 0,
            "No insured person is covered by ESI — Form 5 has no contributions to return for this contribution "
            + "period.");
    }

    // ------------------------------------------------------------------------------------------- ESI Form 6
    /// <summary>
    /// ESI <b>Form 6</b> — the register of employees (Reg. 32(1)), month-columned over the contribution period.
    /// Read <see cref="EsiStatutoryForms.AverageDailyWagesNote"/> before touching the daily-wage column.
    /// </summary>
    private void BuildEsiForm6()
    {
        var (from, _) = BeginStatutoryPeriodForm("ESI Form 6 — Register of Employees",
            "Contribution period", _company.EsiConfig?.EmployerCode);
        var form = EsiStatutoryForms.BuildForm6(_company, from);

        PayrollColumns.Add(PayCol("Sl. No.", StatSerialWidth, true));
        PayrollColumns.Add(PayCol("Insurance No.", StatCodeWidth, false));
        PayrollColumns.Add(PayCol("Name of the Insured Person", StatNameWidth, false));
        PayrollColumns.Add(PayCol("Occupation", StatCodeWidth, false));
        PayrollColumns.Add(PayCol("Rate of Wages in the First Wage Period", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Deptt. and Shift, if any", StatCodeWidth, false));
        PayrollColumns.Add(PayCol("Date of Appointment", StatDateWidth, false));
        PayrollColumns.Add(PayCol("Date of Leaving Service", StatDateWidth, false));
        PayrollColumns.Add(PayCol("Father's or Husband's Name", StatNameWidth, false));
        foreach (var month in form.Months)
        {
            var label = month.From.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            PayrollColumns.Add(PayCol($"{label} — Days", StatNumWidth, true));
            PayrollColumns.Add(PayCol($"{label} — Wages", StatNumWidth, true));
            PayrollColumns.Add(PayCol($"{label} — Employees' Share", StatNumWidth, true));
        }
        PayrollColumns.Add(PayCol("Total Days in Contribution Period", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Total Wages in Contribution Period", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Total Employees' Share in Contribution Period", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Average Daily Wages", StatNumWidth, true));
        PayrollColumns.Add(PayCol("Remarks", StatNameWidth, false));

        foreach (var r in form.Rows)
        {
            var cells = new List<PayrollMatrixCellVm>
            {
                PayCell(r.SerialNumber.ToString(CultureInfo.InvariantCulture), StatSerialWidth, true),
                PayCell(string.IsNullOrEmpty(r.Member.InsuranceNumber) ? "—" : r.Member.InsuranceNumber, StatCodeWidth, false),
                PayCell(r.Member.Name, StatNameWidth, false),
                PayCell(string.IsNullOrEmpty(r.Member.Occupation) ? "—" : r.Member.Occupation, StatCodeWidth, false),
                PayCell(RupeeCell(r.RateOfWagesInFirstWagePeriod), StatNumWidth, true),
                PayCell(string.IsNullOrEmpty(r.Member.DepartmentOrShift) ? "—" : r.Member.DepartmentOrShift, StatCodeWidth, false),
                PayCell(DateCell(r.Member.DateOfAppointment), StatDateWidth, false),
                PayCell(DateCell(r.Member.DateOfLeavingService), StatDateWidth, false),
                PayCell(string.Empty, StatNameWidth, false),                    // not maintained
            };
            foreach (var m in r.Months)
            {
                cells.Add(PayCell(m.NoOfDaysWagesPaid.ToString(CultureInfo.InvariantCulture), StatNumWidth, true));
                cells.Add(PayCell(RupeeCell(m.TotalWages), StatNumWidth, true));
                cells.Add(PayCell(RupeeCell(m.EmployeesShareOfContribution), StatNumWidth, true));
            }
            cells.Add(PayCell(r.TotalDaysInContributionPeriod.ToString(CultureInfo.InvariantCulture), StatNumWidth, true));
            cells.Add(PayCell(RupeeCell(r.TotalWagesInContributionPeriod), StatNumWidth, true));
            cells.Add(PayCell(RupeeCell(r.TotalEmployeesShareInContributionPeriod), StatNumWidth, true));
            cells.Add(PayCell(IndianFormat.AmountAlways(r.AverageDailyWages), StatNumWidth, true));
            cells.Add(PayCell(string.Empty, StatNameWidth, false));
            PayrollRows.Add(new PayrollMatrixRowVm { Cells = cells });
        }

        Footnote("Father's or Husband's Name: " + EsiStatutoryForms.NotMaintainedNote);
        Footnote(EsiStatutoryForms.AverageDailyWagesNote);
        Footnote("\"Rate of Wages in the First Wage Period\" is the member's ESI wages in the first month of the "
               + "contribution period in which they were paid; this book carries no separate wage-rate field.");
        MarkStatutoryFormEmpty(form.Rows.Count == 0,
            "No insured person is covered by ESI — the Form 6 register has no employee to record for this "
            + "contribution period.");
    }

    // --------------------------------------------------------------- Pay Sheet (employees × pay heads matrix)
    private void BuildPaySheet()
    {
        var (from, to) = BeginPayrollReport("Pay Sheet");
        var ids = EligiblePayrollEmployees();
        var sheet = Report.BuildPaySheet(_company, ids, from, to);

        PayrollColumns.Add(PayCol("Employee", PayrollLabelWidth, false));
        foreach (var col in sheet.Columns) PayrollColumns.Add(PayCol(col.Name, PayrollNumWidth, true));
        PayrollColumns.Add(PayCol("Gross", PayrollNumWidth, true));
        PayrollColumns.Add(PayCol("Deductions", PayrollNumWidth, true));
        PayrollColumns.Add(PayCol("Net Pay", PayrollNumWidth, true));

        foreach (var r in sheet.Rows)
        {
            var cells = new List<PayrollMatrixCellVm> { PayCell(r.EmployeeName, PayrollLabelWidth, false) };
            foreach (var v in r.Values) cells.Add(PayCell(IndianFormat.Amount(v), PayrollNumWidth, true));
            cells.Add(PayCell(IndianFormat.Amount(r.GrossEarnings), PayrollNumWidth, true));
            cells.Add(PayCell(IndianFormat.Amount(r.TotalDeductions), PayrollNumWidth, true));
            cells.Add(PayCell(IndianFormat.Amount(r.NetPayable), PayrollNumWidth, true));
            PayrollRows.Add(new PayrollMatrixRowVm { Cells = cells });
        }

        var totals = new List<PayrollMatrixCellVm> { PayCell("Grand Total", PayrollLabelWidth, false) };
        foreach (var t in sheet.ColumnTotals) totals.Add(PayCell(IndianFormat.AmountAlways(t), PayrollNumWidth, true));
        totals.Add(PayCell(IndianFormat.AmountAlways(sheet.TotalGrossEarnings), PayrollNumWidth, true));
        totals.Add(PayCell(IndianFormat.AmountAlways(sheet.TotalDeductions), PayrollNumWidth, true));
        totals.Add(PayCell(IndianFormat.AmountAlways(sheet.TotalNetPayable), PayrollNumWidth, true));
        PayrollRows.Add(new PayrollMatrixRowVm { Cells = totals, IsTotal = true });

        MarkPayrollEmpty(sheet.Rows.Count == 0);
    }

    // --------------------------------------------------------------- Payroll Register / Statement (columnar)
    private void BuildPayrollRegister()
    {
        var (from, to) = BeginPayrollReport("Payroll Register");
        var ids = EligiblePayrollEmployees();
        var reg = Report.BuildPayrollRegister(_company, ids, from, to);

        foreach (var (header, numeric) in new[]
        {
            ("Employee", false), ("Gross", true), ("Prof. Tax", true), ("Employee PF", true),
            ("Employee ESI", true), ("Income Tax", true), ("Other Ded.", true),
            ("Total Ded.", true), ("Net Pay", true), ("Employer Contrib.", true),
        })
            PayrollColumns.Add(PayCol(header, header == "Employee" ? PayrollLabelWidth : PayrollNumWidth, numeric));

        foreach (var r in reg.Rows)
            PayrollRows.Add(new PayrollMatrixRowVm
            {
                Cells = new List<PayrollMatrixCellVm>
                {
                    PayCell(r.EmployeeName, PayrollLabelWidth, false),
                    PayCell(IndianFormat.Amount(r.GrossEarnings), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.ProfessionalTax), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.EmployeePf), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.EmployeeEsi), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.IncomeTax), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.OtherDeductions), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.TotalDeductions), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.NetPayable), PayrollNumWidth, true),
                    PayCell(IndianFormat.Amount(r.EmployerContributions), PayrollNumWidth, true),
                },
            });

        PayrollRows.Add(new PayrollMatrixRowVm
        {
            IsTotal = true,
            Cells = new List<PayrollMatrixCellVm>
            {
                PayCell("Grand Total", PayrollLabelWidth, false),
                PayCell(IndianFormat.AmountAlways(reg.TotalGrossEarnings), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalProfessionalTax), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalEmployeePf), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalEmployeeEsi), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalIncomeTax), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalOtherDeductions), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalDeductions), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalNetPayable), PayrollNumWidth, true),
                PayCell(IndianFormat.AmountAlways(reg.TotalEmployerContributions), PayrollNumWidth, true),
            },
        });

        MarkPayrollEmpty(reg.Rows.Count == 0);
    }

    // --------------------------------------------------------------- Attendance / Production Register (matrix)
    private void BuildAttendanceRegister()
    {
        var (from, to) = BeginPayrollReport("Attendance Register");
        var ids = _company.Employees.Select(e => e.Id).ToList();
        var reg = Report.BuildAttendanceRegister(_company, ids, from, to);

        PayrollColumns.Add(PayCol("Employee", PayrollLabelWidth, false));
        foreach (var t in reg.Types) PayrollColumns.Add(PayCol(t.Name, PayrollDayWidth, true));
        PayrollColumns.Add(PayCol("Days Paid", PayrollDayWidth, true));
        PayrollColumns.Add(PayCol("LOP", PayrollDayWidth, true));

        foreach (var r in reg.Rows)
        {
            var cells = new List<PayrollMatrixCellVm> { PayCell(r.EmployeeName, PayrollLabelWidth, false) };
            foreach (var v in r.Values) cells.Add(PayCell(Days(v), PayrollDayWidth, true));
            cells.Add(PayCell(Days(r.DaysPaid), PayrollDayWidth, true));
            cells.Add(PayCell(Days(r.DaysLop), PayrollDayWidth, true));
            PayrollRows.Add(new PayrollMatrixRowVm { Cells = cells });
        }

        var totals = new List<PayrollMatrixCellVm> { PayCell("Total", PayrollLabelWidth, false) };
        foreach (var t in reg.TypeTotals) totals.Add(PayCell(DaysAlways(t), PayrollDayWidth, true));
        totals.Add(PayCell(DaysAlways(reg.Rows.Sum(r => r.DaysPaid)), PayrollDayWidth, true));
        totals.Add(PayCell(DaysAlways(reg.Rows.Sum(r => r.DaysLop)), PayrollDayWidth, true));
        PayrollRows.Add(new PayrollMatrixRowVm { Cells = totals, IsTotal = true });

        MarkPayrollEmpty(reg.Rows.Count == 0);
    }

    // --------------------------------------------------------------- Payment / Bank Advice
    private void BuildPaymentAdvice()
    {
        var (from, to) = BeginPayrollReport("Payment Advice");
        var ids = EligiblePayrollEmployees();
        var advice = Report.BuildPaymentAdvice(_company, ids, from, to);

        PayrollColumns.Add(PayCol("Employee", PayrollLabelWidth, false));
        PayrollColumns.Add(PayCol("Bank", 160, false));
        PayrollColumns.Add(PayCol("A/c Number", 140, false));
        PayrollColumns.Add(PayCol("IFSC", 120, false));
        PayrollColumns.Add(PayCol("Net Pay", PayrollNumWidth, true));

        foreach (var r in advice.Rows)
            PayrollRows.Add(new PayrollMatrixRowVm
            {
                Cells = new List<PayrollMatrixCellVm>
                {
                    PayCell(r.EmployeeName, PayrollLabelWidth, false),
                    PayCell(r.BankName ?? "—", 160, false),
                    PayCell(r.BankAccountNumber ?? "—", 140, false),
                    PayCell(r.BankIfsc ?? "—", 120, false),
                    PayCell(IndianFormat.Amount(r.NetPayable), PayrollNumWidth, true),
                },
            });

        PayrollRows.Add(new PayrollMatrixRowVm
        {
            IsTotal = true,
            Cells = new List<PayrollMatrixCellVm>
            {
                PayCell("Total", PayrollLabelWidth, false),
                PayCell(string.Empty, 160, false),
                PayCell(string.Empty, 140, false),
                PayCell(string.Empty, 120, false),
                PayCell(IndianFormat.AmountAlways(advice.TotalNetPayable), PayrollNumWidth, true),
            },
        });

        MarkPayrollEmpty(advice.Rows.Count == 0);
    }

    // ================================================== W-J1: census rows 7.22 – 7.26 (user ruling 19)
    //
    // Five REPORTS over payroll data this product already computes. Not one of them computes a new figure, and
    // not one of them touches storage. Each is grounded in the vendor page named on its ReportKind.

    private const double BreakupNameWidth = 240;
    private const double BreakupNumWidth = 130;

    /// <summary>The Income Tax Computation's value column. 🔴 Sized for its longest VALUE, not its longest
    /// figure: the snapshot rows carry the regime label <c>"New regime (u/s 115BAC)"</c> (23 characters), which
    /// at <see cref="BreakupNumWidth"/> was cut to "New regime (u/s" — leaving a report whose two regimes, the
    /// one thing that changes which deductions apply, read almost identically.</summary>
    private const double IncomeTaxValueWidth = 240;

    /// <summary>The Income Tax Computation's particulars column, sized for
    /// <c>"Less: Standard Deduction u/s 16(ia)"</c> with room to spare.</summary>
    private const double IncomeTaxLabelWidth = 380;

    // The shipped grid is monospace, so a column fits its heading exactly when
    // (characters × advance) ≤ (width − padding). These two numbers are the measured metrics of the shipped face
    // and are the same pair PayrollStatutoryFormLegibilityTests asserts against; they live here so a column whose
    // heading is DATA-DRIVEN can size itself rather than hoping a hand-picked constant is still big enough.
    private const double ColumnHeaderAdvance = 6.5977;   // TextBlock.colHdr, FontSize 12
    private const double ColumnHeaderPadding = 16;       // colHdr Padding="8,4"

    /// <summary>A column width guaranteed to show <paramref name="header"/> in full, never narrower than
    /// <paramref name="minimum"/>. Used where the heading embeds a master's own text (a payroll unit symbol), so
    /// no hand-picked constant can be known to be wide enough for every company's data.</summary>
    private static double PayColWidthFor(string header, double minimum)
        => Math.Max(minimum, header.Length * ColumnHeaderAdvance + ColumnHeaderPadding);

    /// <summary>A signed, debit-positive balance as the breakup reports print it: the magnitude followed by
    /// <c>Dr</c> or <c>Cr</c>, blank at exactly zero. A bare negative number on a balance column is ambiguous —
    /// the reader cannot tell a credit balance from a data-entry sign — so the side is spelled out.
    /// <para>Delegates to <see cref="IndianFormat.Signed(decimal, DrCr)"/>, which is where this codebase's
    /// blank-at-zero + side-suffix convention already lives (and which is careful not to leave a dangling
    /// "Dr" behind a blank cell). Re-implementing it here would be a second place for that convention to
    /// drift.</para></summary>
    private static string Signed(decimal v)
        => IndianFormat.Signed(Math.Abs(v), v >= 0m ? DrCr.Debit : DrCr.Credit);

    // --------------------------------------------------------------- 7.22 Attendance Sheet
    // 🔴 NOT the Attendance Register (7.15). That one is the wide per-type MATRIX; this is the fixed four-figure
    // SUMMARY the vendor's page names. Both ship, and neither answers for the other.
    private void BuildAttendanceSheet()
    {
        var (from, to) = BeginPayrollReport("Attendance Sheet");
        var ids = _company.Employees.Select(e => e.Id).ToList();
        // The vendor's F12 "Remove zero-valued transactions" (…/payroll-reports/attendance-sheet-payroll/) is this
        // book's existing F12 "hide zero balances" — the same operator intent through the same key, rather than a
        // second configuration switch nobody would find. An engine flag with no keystroke behind it would be a
        // parameter, not a feature.
        var sheet = Report.BuildAttendanceSheet(_company, ids, from, to, _options.HideZeroBalances);

        var producedHeader = sheet.ProductionUnits.Count == 1
            ? $"Units Produced ({sheet.ProductionUnits[0]})"
            : "Units Produced";
        var overtimeHeader = sheet.OvertimeUnits.Count == 1
            ? $"Overtime ({sheet.OvertimeUnits[0]})"
            : "Overtime";

        // 🔴 The two production headings embed a payroll-unit SYMBOL the operator chose, so their width cannot be
        // a hand-picked constant: "Units Produced (Nos)" already overflows PayrollNumWidth, and a company using
        // "Pieces" would overflow anything guessed here. They size themselves to their own text.
        var producedWidth = PayColWidthFor(producedHeader, PayrollNumWidth);
        var overtimeWidth = PayColWidthFor(overtimeHeader, PayrollNumWidth);

        PayrollColumns.Add(PayCol("Employee", PayrollLabelWidth, false));
        PayrollColumns.Add(PayCol("Emp. No.", PayrollDayWidth, false));
        PayrollColumns.Add(PayCol("Days Present", PayrollDayWidth, true));
        PayrollColumns.Add(PayCol("Days Absent", PayrollDayWidth, true));
        PayrollColumns.Add(PayCol(producedHeader, producedWidth, true));
        PayrollColumns.Add(PayCol(overtimeHeader, overtimeWidth, true));

        foreach (var r in sheet.Rows)
            PayrollRows.Add(new PayrollMatrixRowVm
            {
                Cells = new List<PayrollMatrixCellVm>
                {
                    PayCell(r.EmployeeName, PayrollLabelWidth, false),
                    PayCell(Or(r.EmployeeNumber), PayrollDayWidth, false),
                    PayCell(Days(r.DaysPresent), PayrollDayWidth, true),
                    PayCell(Days(r.DaysAbsent), PayrollDayWidth, true),
                    PayCell(Days(r.UnitsProduced), producedWidth, true),
                    PayCell(Days(r.OvertimeWorked), overtimeWidth, true),
                },
            });

        PayrollRows.Add(new PayrollMatrixRowVm
        {
            IsTotal = true,
            Cells = new List<PayrollMatrixCellVm>
            {
                PayCell("Total", PayrollLabelWidth, false),
                PayCell(string.Empty, PayrollDayWidth, false),
                PayCell(DaysAlways(sheet.TotalDaysPresent), PayrollDayWidth, true),
                PayCell(DaysAlways(sheet.TotalDaysAbsent), PayrollDayWidth, true),
                PayCell(DaysAlways(sheet.TotalUnitsProduced), producedWidth, true),
                PayCell(DaysAlways(sheet.TotalOvertimeWorked), overtimeWidth, true),
            },
        });

        Footnote("Overtime is the production recorded against an attendance/production type that an overtime pay "
               + "head is calculated on. This book carries no overtime flag on the type itself, so nothing is "
               + "inferred from a type's name.");
        if (sheet.ProductionUnits.Count > 1)
            Footnote("Units Produced spans more than one payroll unit (" + string.Join(", ", sheet.ProductionUnits)
                   + "); the figures are summed as recorded and nothing is converted between units.");
        if (sheet.OvertimeUnits.Count > 1)
            Footnote("Overtime spans more than one payroll unit (" + string.Join(", ", sheet.OvertimeUnits)
                   + "); the figures are summed as recorded and nothing is converted between units.");
        if (sheet.UncountedTypeNames.Count > 0)
            Footnote("These user-defined attendance types were recorded in the period and are NOT part of any "
                   + "column above: " + string.Join(", ", sheet.UncountedTypeNames)
                   + ". The Attendance Register (Reports > Payroll Reports > Attendance Register) shows them.");

        MarkStatutoryFormEmpty(sheet.Rows.Count == 0,
            "No employee is on the roster, so there is no attendance to summarise.");
    }

    // --------------------------------------------------------------- 7.23 Pay Head Employee Breakup
    private void BuildPayHeadEmployeeBreakup()
    {
        var (from, to) = BeginPayrollReport("Pay Head Employee Breakup");
        if (SelectedPayrollEmployee is not { } chosen)
        {
            MarkStatutoryFormEmpty(true, "No employee master exists, so there is no breakup to show.");
            return;
        }

        var breakup = Report.BuildPayHeadEmployeeBreakup(_company, chosen.EmployeeId, from, to);
        Subtitle = $"{CompanyName}  —  {breakup.ScopeName}  —  "
                 + $"Wage month {from.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        RenderBreakup(breakup, "Pay Head");

        MarkStatutoryFormEmpty(breakup.IsEmpty,
            $"Nothing has been posted for {breakup.ScopeName} on or before the end of this wage month.");
    }

    // --------------------------------------------------------------- 7.24 Employee Pay Head Breakup
    // 🔴 The TRANSPOSE of 7.23, not a second name for it — one pay head across every employee. Shared engine so
    // the two can never disagree; separate report + separate picker so neither silently answers for the other.
    private void BuildEmployeePayHeadBreakup()
    {
        var (from, to) = BeginPayrollReport("Employee Pay Head Breakup");
        if (SelectedPayrollPayHead is not { } chosen)
        {
            MarkStatutoryFormEmpty(true, "No pay head master exists, so there is no breakup to show.");
            return;
        }

        var breakup = Report.BuildEmployeePayHeadBreakup(_company, chosen.PayHeadId, from, to);
        Subtitle = $"{CompanyName}  —  {breakup.ScopeName}  —  "
                 + $"Wage month {from.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        RenderBreakup(breakup, "Employee");

        MarkStatutoryFormEmpty(breakup.IsEmpty,
            $"Nothing has been posted to {breakup.ScopeName} on or before the end of this wage month.");
    }

    /// <summary>Renders a <see cref="PayHeadBreakup"/> into the shared payroll matrix: a group band, its lines,
    /// a group subtotal, then the grand total. Both 7.23 and 7.24 go through here, which is what keeps the two
    /// transpositions visually and arithmetically identical.</summary>
    private void RenderBreakup(PayHeadBreakup breakup, string nameHeader)
    {
        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            (nameHeader, BreakupNameWidth, false),
            ("Opening Balance", BreakupNumWidth, true),
            ("Debit", BreakupNumWidth, true),
            ("Credit", BreakupNumWidth, true),
            ("Closing Balance", BreakupNumWidth, true),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var group in breakup.Groups)
        {
            // The group heading row. The professional-hierarchy rule: lines nest UNDER a named parent band, never
            // a flat dump of every pay head in the company.
            PayrollRows.Add(StatRow(false,
                (group.GroupName, BreakupNameWidth, false),
                (string.Empty, BreakupNumWidth, true),
                (string.Empty, BreakupNumWidth, true),
                (string.Empty, BreakupNumWidth, true),
                (string.Empty, BreakupNumWidth, true)));

            foreach (var line in group.Lines)
                PayrollRows.Add(StatRow(false,
                    ("    " + line.Name, BreakupNameWidth, false),
                    (Signed(line.Opening), BreakupNumWidth, true),
                    (IndianFormat.Amount(line.Debit), BreakupNumWidth, true),
                    (IndianFormat.Amount(line.Credit), BreakupNumWidth, true),
                    (Signed(line.Closing), BreakupNumWidth, true)));

            PayrollRows.Add(StatRow(true,
                ($"Total — {group.GroupName}", BreakupNameWidth, false),
                (Signed(group.Opening), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(group.Debit), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(group.Credit), BreakupNumWidth, true),
                (Signed(group.Closing), BreakupNumWidth, true)));
        }

        if (breakup.Groups.Count > 0)
            PayrollRows.Add(StatRow(true,
                ("Grand Total", BreakupNameWidth, false),
                (Signed(breakup.TotalOpening), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(breakup.TotalDebit), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(breakup.TotalCredit), BreakupNumWidth, true),
                (Signed(breakup.TotalClosing), BreakupNumWidth, true)));

        Footnote(PayHeadBreakup.ExcludedLegNote);
        Footnote("Opening Balance is the cumulative posting before this wage month; this book keeps no separate "
               + "opening-balance master for an employee-and-pay-head pair, so none is read from one.");
    }

    // --------------------------------------------------------------- 7.25 Payroll Statutory Summary
    private void BuildPayrollStatutorySummary()
    {
        var (from, to) = BeginPayrollReport("Payroll Statutory Summary");
        var summary = Report.BuildPayrollStatutorySummary(_company, from, to);

        foreach (var (header, width, numeric) in new (string, double, bool)[]
        {
            ("Pay Head Type", BreakupNameWidth, false),
            ("Payable", BreakupNumWidth, true),
            ("Paid", BreakupNumWidth, true),
            ("Balance", BreakupNumWidth, true),
        })
            PayrollColumns.Add(PayCol(header, width, numeric));

        foreach (var row in summary.Rows)
            PayrollRows.Add(StatRow(false,
                (row.Caption, BreakupNameWidth, false),
                (IndianFormat.Amount(row.Payable.Amount), BreakupNumWidth, true),
                (IndianFormat.Amount(row.Paid.Amount), BreakupNumWidth, true),
                (IndianFormat.Amount(row.Balance.Amount), BreakupNumWidth, true)));

        if (summary.Rows.Count > 0)
            PayrollRows.Add(StatRow(true,
                ("Total", BreakupNameWidth, false),
                (IndianFormat.AmountAlways(summary.TotalPayable.Amount), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(summary.TotalPaid.Amount), BreakupNumWidth, true),
                (IndianFormat.AmountAlways(summary.TotalBalance.Amount), BreakupNumWidth, true)));

        // The vendor opens the "Statutory Pay Head Details" on Enter. This book carries no drill stack on the
        // payroll matrix, so the detail level is rendered as its own band UNDER the summary rather than being
        // dropped — a documented divergence in shape, not in content: every figure the drill would show is here.
        if (summary.Rows.Count > 0)
        {
            PayrollSection2Title = "Statutory Pay Head Details";
            foreach (var (header, width, numeric) in new (string, double, bool)[]
            {
                ("Pay Head Type", BreakupNameWidth, false),
                ("Pay Head", BreakupNameWidth, false),
                ("Ledger", BreakupNameWidth, false),
                ("Payable", BreakupNumWidth, true),
                ("Paid", BreakupNumWidth, true),
                ("Balance", BreakupNumWidth, true),
            })
                PayrollColumns2.Add(PayCol(header, width, numeric));

            foreach (var row in summary.Rows)
                foreach (var detail in row.Details)
                    PayrollRows2.Add(StatRow(false,
                        (row.Caption, BreakupNameWidth, false),
                        (detail.PayHeadName, BreakupNameWidth, false),
                        (Or(detail.LedgerName), BreakupNameWidth, false),
                        (IndianFormat.Amount(detail.Payable.Amount), BreakupNumWidth, true),
                        (IndianFormat.Amount(detail.Paid.Amount), BreakupNumWidth, true),
                        (IndianFormat.Amount(detail.Balance.Amount), BreakupNumWidth, true)));

            HasPayrollSection2 = PayrollRows2.Count > 0;
        }

        Footnote(PayrollStatutorySummary.PaidDerivationNote);
        // Empty since census row 7.18 closed the NPS divergence; Footnote() ignores an empty string, so nothing is
        // printed. It stays wired because the next unsupported statutory type belongs in that constant.
        Footnote(PayrollStatutorySummary.UnsupportedTypeNote);
        Footnote("This is the roll-up OVER the PF, ESI and Professional-Tax computations, not a replacement for "
               + "them; those reports live under Reports > Statutory Reports > Payroll.");

        MarkStatutoryFormEmpty(summary.IsEmpty,
            "No pay head is configured as a PF, ESI, Professional-Tax, Income-Tax or National-Pension-Scheme "
            + "statutory head, so there is nothing to summarise.");
    }

    // --------------------------------------------------------------- 7.26 Income Tax Computation
    // 🔴 REUSES the annual computation that backs Form 16 Part B (Form24Q.BuildAnnexureII → ComputeAnnual). A
    // second computation here would drift from the certificate the employee is handed.
    private void BuildIncomeTaxComputation()
    {
        var (from, to) = BeginPayrollReport("Income Tax Computation");
        if (SelectedPayrollEmployee is not { } chosen)
        {
            MarkStatutoryFormEmpty(true, "No employee master exists, so there is no computation to show.");
            return;
        }

        var fyStartYear = IncomeTaxComputationReport.FinancialYearStartYearFor(from);
        var report = Report.BuildIncomeTaxComputation(_company, chosen.EmployeeId, fyStartYear);

        PayrollColumns.Add(PayCol("Particulars", IncomeTaxLabelWidth, false));
        PayrollColumns.Add(PayCol("Amount", IncomeTaxValueWidth, true));

        if (report is null)
        {
            Subtitle = $"{CompanyName}  —  {chosen.Display}  —  Financial year "
                     + $"{fyStartYear}-{(fyStartYear + 1) % 100:00}";
            MarkStatutoryFormEmpty(true,
                $"{chosen.Display} has no taxable salary and no salary-TDS posted in financial year "
                + $"{fyStartYear}-{(fyStartYear + 1) % 100:00}, so there is no computation for this year.");
            return;
        }

        Subtitle = $"{CompanyName}  —  {report.EmployeeName}  —  Financial year {report.FinancialYearLabel}";

        // The vendor's "overall tax deduction snapshot": who this is for, under which regime, and where the
        // year's withholding stands. Carried as leading rows because this matrix has no header panel of its own.
        PayrollRows.Add(StatRow(false, ("PAN", IncomeTaxLabelWidth, false), (Or(report.Pan), IncomeTaxValueWidth, false)));
        PayrollRows.Add(StatRow(false, ("Tax Regime", IncomeTaxLabelWidth, false), (report.RegimeLabel, IncomeTaxValueWidth, false)));
        PayrollRows.Add(StatRow(false,
            ("Tax deducted so far", IncomeTaxLabelWidth, false),
            (IndianFormat.AmountAlways(report.TaxDeductedSoFar.Amount), IncomeTaxValueWidth, true)));
        PayrollRows.Add(StatRow(false,
            ("Balance tax payable", IncomeTaxLabelWidth, false),
            (IndianFormat.AmountAlways(report.BalanceTaxPayable.Amount), IncomeTaxValueWidth, true)));

        foreach (var line in report.Lines)
            PayrollRows.Add(StatRow(line.IsSubtotal,
                (line.Caption, IncomeTaxLabelWidth, false),
                (line.Amount is { } m ? IndianFormat.AmountAlways(m.Amount) : string.Empty, IncomeTaxValueWidth, true)));

        // The vendor drills into an italic component on Enter. This matrix carries no drill stack, so each
        // component's breakdown is rendered as its own band rather than being dropped.
        PayrollSection2Title = "Component details";
        PayrollColumns2.Add(PayCol("Component", 260, false));
        PayrollColumns2.Add(PayCol("Particulars", IncomeTaxLabelWidth, false));
        PayrollColumns2.Add(PayCol("Amount", IncomeTaxValueWidth, true));
        foreach (var detail in report.Details)
            foreach (var line in detail.Lines)
                PayrollRows2.Add(StatRow(line.IsSubtotal,
                    (detail.Caption, 260, false),
                    (line.Caption, IncomeTaxLabelWidth, false),
                    (line.Amount is { } m ? IndianFormat.AmountAlways(m.Amount) : string.Empty, IncomeTaxValueWidth, true)));
        HasPayrollSection2 = PayrollRows2.Count > 0;

        Footnote("Every figure is the same annual computation that backs Form 16 Part B and Form 24Q Annexure II; "
               + "this report computes no tax of its own.");
        Footnote(IncomeTaxComputationReport.RateVintageNote);
    }

    // --------------------------------------------------------------- Payslip (single-employee detail + PDF)
    private void BuildPayslip()
    {
        var (from, to) = PayrollPeriod();
        Title = "Payslip";
        IsTwoColumn = false;
        _currentPayslip = null;
        IsPayslipEmpty = false;
        PayslipEmployee = PayslipMeta = PayslipMeta2 = string.Empty;
        PayslipGross = PayslipTotalDeductions = PayslipNet = PayslipNetWords = string.Empty;
        PayslipAttendance = PayslipYtd = string.Empty;
        PayslipPeriodText = $"For {FormatDate(from)} to {FormatDate(to)}";

        var emp = SelectedPayrollEmployee;
        if (emp is null)
        {
            IsPayslipEmpty = true;
            Subtitle = $"{CompanyName}  —  no employees to show a payslip for.";
            OnPropertyChanged(nameof(HasPayslipEmployerContributions));
            return;
        }

        // The payslip projects the POSTED Payroll voucher for the month; empty earnings/deductions ⇒ nothing was
        // posted for this member this wage month (a cancelled or never-run pay), so show a "not posted" note.
        var slip = Report.BuildPayslip(_company, emp.EmployeeId, from, to);
        if (slip.Earnings.Count == 0 && slip.Deductions.Count == 0 && slip.EmployerContributions.Count == 0)
        {
            IsPayslipEmpty = true;
            Subtitle = $"{CompanyName}  —  {emp.Display}  —  no payroll posted for this wage month.";
            OnPropertyChanged(nameof(HasPayslipEmployerContributions));
            return;
        }

        _currentPayslip = slip;
        Subtitle = $"{CompanyName}  —  {slip.EmployeeName}  —  {from.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        PayslipEmployee = slip.EmployeeName;
        PayslipMeta = $"Emp No: {Or(slip.EmployeeNumber)}    Designation: {Or(slip.Designation)}    Department: {Or(slip.Department)}    DOJ: {(slip.DateOfJoining is { } d ? FormatDate(d) : "—")}";
        PayslipMeta2 = $"PAN: {Or(slip.Pan)}    UAN: {Or(slip.Uan)}    ESI No: {Or(slip.EsiNumber)}    Bank: {Or(slip.BankName)}    A/c: {Or(slip.BankAccountNumber)}    IFSC: {Or(slip.BankIfsc)}";

        foreach (var l in slip.Earnings)
            PayslipEarnings.Add(new PayslipLineVm { Name = l.Name, Amount = IndianFormat.AmountAlways(l.Amount) });
        foreach (var l in slip.Deductions)
            PayslipDeductions.Add(new PayslipLineVm { Name = l.Name, Amount = IndianFormat.AmountAlways(l.Amount) });
        foreach (var l in slip.EmployerContributions)
            PayslipEmployerContributions.Add(new PayslipLineVm { Name = l.Name, Amount = IndianFormat.AmountAlways(l.Amount) });

        PayslipGross = IndianFormat.AmountAlways(slip.GrossEarnings);
        PayslipTotalDeductions = IndianFormat.AmountAlways(slip.TotalDeductions);
        PayslipNet = IndianFormat.AmountAlways(slip.NetPayable);
        PayslipNetWords = IndianAmountInWords.Convert(slip.NetPayable.Amount);
        PayslipAttendance = $"Days Paid: {Days(slip.DaysPaid)}     Loss of Pay: {Days(slip.DaysLop)}";
        PayslipYtd = $"Year to date  —  Gross {IndianFormat.AmountAlways(slip.YtdGrossEarnings)}   ·   Deductions {IndianFormat.AmountAlways(slip.YtdTotalDeductions)}   ·   Net {IndianFormat.AmountAlways(slip.YtdNetPayable)}";
        OnPropertyChanged(nameof(HasPayslipEmployerContributions));
    }

    /// <summary>Sets the payroll-matrix empty state (drives the "nothing to show" note and hides the grid).</summary>
    private void MarkPayrollEmpty(bool empty)
    {
        IsPayrollEmpty = empty;
        PayrollEmptyNote = empty ? "No employees with salary for this wage month." : string.Empty;
    }

    /// <summary>
    /// Sets the empty state for a statutory form with its OWN reason, instead of the wage-month default.
    ///
    /// <para>🔴 The default note reads <i>"No employees with salary for this wage month."</i> On the four
    /// MULTI-MONTH forms — PF 3A and 6A (a twelve-month currency period) and ESI 5 and 6 (a six-month
    /// contribution period) — that is <b>the wrong window AND the wrong reason</b>: it names a wage month these
    /// forms are not drawn over, and it blames missing salary when the real cause is that the establishment has
    /// no member enrolled in the scheme at all. Forms 5, 10 and 3 already say what they mean; these four now do
    /// too.</para>
    /// </summary>
    private void MarkStatutoryFormEmpty(bool empty, string note)
    {
        MarkPayrollEmpty(empty);
        if (empty) PayrollEmptyNote = note;
    }

    private static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s!;

    /// <summary>A day/hour attendance count, trailing zeros trimmed; blank at exactly zero (report convention).</summary>
    private static string Days(decimal v) => v == 0m ? string.Empty : v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A day/hour attendance count always rendered (even zero) — for the attendance totals row.</summary>
    private static string DaysAlways(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // --------------------------------------------------------------- helpers

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;

        // Default to the financial-year end when there are no vouchers.
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private static string FormatDate(DateOnly d) => ApexDate.Format(d);

    // =============================================================== W2-12 — the report families (11.6/11.7/11.8)

    /// <summary>
    /// An accounting register (census 11.6). Two levels, and which one is showing is decided by the shell,
    /// not by an in-place toggle: the TOP level is one row per calendar month of the window, and the DRILLED
    /// level (<see cref="_registerVoucherLevel"/>, opened as its own cascade column from a month row) is that
    /// month's voucher-wise listing. Building the top level as a flat voucher list — the "filtered Day Book"
    /// shortcut — is the exact mistake the verification pass caught this row on.
    /// </summary>
    private void BuildVoucherRegister(VoucherRegisterKind registerKind)
    {
        var period = StatementPeriod;
        IsTwoColumn = false;

        if (_registerVoucherLevel)
        {
            var months = MonthAxis.Months(period.From, period.To);
            var label = months.Count == 1 ? months[0].Label : $"{FormatDate(period.From)} to {FormatDate(period.To)}";
            Title = $"{VoucherRegister.TitleOf(registerKind)} — {label}";
            Subtitle = $"{CompanyName}  —  voucher-wise for {FormatDate(period.From)} to {FormatDate(period.To)}";

            var vouchers = VoucherRegister.Vouchers(_company, registerKind, period.From, period.To);
            var footed = 0m;
            foreach (var v in vouchers)
            {
                Rows.Add(new ReportRow
                {
                    Particulars = $"{FormatDate(v.Date)}  No. {v.FormattedNumber}",
                    Secondary = v.Particulars ?? string.Empty,
                    Amount = IndianFormat.Amount(v.Value),
                    // RQ-7: every voucher-level row drills into the voucher itself.
                    DrillVoucherId = v.VoucherId,
                });
                footed += v.Value.Amount;
            }

            if (vouchers.Count == 0)
                Rows.Add(new ReportRow { Particulars = "No vouchers of this kind in this period.", IsHeader = true });
            else
                Rows.Add(ReportRow.Total("Total", new Money(footed)));
            return;
        }

        var report = VoucherRegister.Build(_company, registerKind, period.From, period.To);
        Title = report.Title;
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";

        foreach (var m in report.Months)
            Rows.Add(new ReportRow
            {
                Particulars = m.Month.Label,
                Secondary = m.VoucherCount == 1 ? "1 voucher" : $"{m.VoucherCount} vouchers",
                Amount = IndianFormat.Amount(m.Value),
                // The month IS the drill key here — an empty month drills too, and correctly shows nothing,
                // which is more honest than an inert row that looks broken.
                DrillPeriod = new PeriodRange(m.Month.From, m.Month.To),
            });

        if (report.Months.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No months in this period.", IsHeader = true });
        else
            Rows.Add(ReportRow.Total("Total", report.Total));
    }

    /// <summary>
    /// Group Summary (census 11.7): the sub-groups and directly-attached ledgers of the scoped group with
    /// their closing balances, on the Dr/Cr grid. Sub-group rows drill into their own summary; ledger rows
    /// drill into the <see cref="ReportKind.LedgerMonthlySummary"/>.
    /// </summary>
    private void BuildGroupSummary()
    {
        var period = StatementPeriod;
        IsTwoColumn = true;

        if (_company.FindGroup(_scopeMasterId) is null)
        {
            Title = "Group Summary";
            Subtitle = CompanyName;
            Rows.Add(new ReportRow { Particulars = "Choose a group to summarise.", IsHeader = true });
            return;
        }

        var gs = GroupSummary.Build(_company, _scopeMasterId, period.From, period.To);
        // 🔴 RULING 18: the heading carries a GROUP MASTER NAME the user typed — flag it so the PDF and all four
        // tabular formats print it verbatim instead of scrubbing the vendor token out of a real group's name.
        Title = $"Group Summary — {gs.GroupName}";
        TitleCarriesMasterName = true;
        Subtitle = $"{CompanyName}  —  closing as at {FormatDate(period.To)} "
            + $"(movement {FormatDate(period.From)} to {FormatDate(period.To)})";

        foreach (var r in gs.Rows)
        {
            var closing = r.ClosingSide == DrCr.Debit
                ? (Debit: r.ClosingAmount, Credit: Money.Zero)
                : (Debit: Money.Zero, Credit: r.ClosingAmount);

            Rows.Add(new ReportRow
            {
                Particulars = r.Name,
                Secondary = r.IsGroup ? "Group" : "Ledger",
                Debit = IndianFormat.Amount(closing.Debit),
                Credit = IndianFormat.Amount(closing.Credit),
                IsTwoColumn = true,
                DrillGroupId = r.GroupId,
                DrillLedgerId = r.LedgerId,
            });
        }

        if (gs.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "This group holds no sub-groups and no ledgers.", IsHeader = true });
        else
            Rows.Add(ReportRow.DrCrTotal(
                "Total",
                gs.ClosingSide == DrCr.Debit ? gs.ClosingAmount : Money.Zero,
                gs.ClosingSide == DrCr.Credit ? gs.ClosingAmount : Money.Zero));
    }

    /// <summary>
    /// Group Vouchers (census 11.7): every voucher carrying at least one ledger under the scoped group, with
    /// that voucher's movement <b>on the group's own ledgers</b> in the Dr/Cr columns, so the footer
    /// reconciles to the Group Summary's movement. Each row drills into its voucher.
    /// </summary>
    private void BuildGroupVouchers()
    {
        var period = StatementPeriod;
        IsTwoColumn = true;

        if (_company.FindGroup(_scopeMasterId) is null)
        {
            Title = "Group Vouchers";
            Subtitle = CompanyName;
            Rows.Add(new ReportRow { Particulars = "Choose a group to list vouchers for.", IsHeader = true });
            return;
        }

        var gv = GroupVouchers.Build(_company, _scopeMasterId, period.From, period.To);
        // 🔴 RULING 18: heading carries a GROUP MASTER NAME — see TitleCarriesMasterName.
        Title = $"Group Vouchers — {gv.GroupName}";
        TitleCarriesMasterName = true;
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";

        foreach (var r in gv.Rows)
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  {r.VoucherTypeName} No. {r.FormattedNumber}",
                Secondary = r.Particulars ?? string.Empty,
                Debit = IndianFormat.Amount(r.Debit),
                Credit = IndianFormat.Amount(r.Credit),
                IsTwoColumn = true,
                DrillVoucherId = r.VoucherId,
            });

        if (gv.Rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No vouchers touch this group in this period.", IsHeader = true });
        else
            Rows.Add(ReportRow.DrCrTotal("Total", gv.TotalDebit, gv.TotalCredit));
    }

    /// <summary>
    /// The Ledger Monthly Summary (census T1-32) — the level the Account Books family never had. One row per
    /// month with that month's Dr/Cr movement in the two amount columns and the running closing in the
    /// secondary cell; each month row drills into that month's ledger vouchers.
    /// </summary>
    private void BuildLedgerMonthlySummary()
    {
        var period = StatementPeriod;
        IsTwoColumn = true;

        if (_company.FindLedger(_scopeMasterId) is null)
        {
            Title = "Ledger Monthly Summary";
            Subtitle = CompanyName;
            Rows.Add(new ReportRow { Particulars = "Choose a ledger.", IsHeader = true });
            return;
        }

        var ms = LedgerMonthlySummary.Build(_company, _scopeMasterId, period.From, period.To);
        // 🔴 RULING 18: heading carries a LEDGER MASTER NAME — the customer/supplier/bank this statement is ABOUT.
        // This is the exact case the seam exists for; see TitleCarriesMasterName.
        Title = $"Ledger Monthly Summary — {ms.LedgerName}";
        TitleCarriesMasterName = true;
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";

        Rows.Add(new ReportRow
        {
            Particulars = "Opening Balance",
            Secondary = $"{IndianFormat.Amount(ms.OpeningAmount)} {SideLabel(ms.OpeningSide)}",
            IsHeader = true,
        });

        foreach (var r in ms.Rows)
            Rows.Add(new ReportRow
            {
                Particulars = r.Month.Label,
                Secondary = $"closing {IndianFormat.Amount(r.ClosingAmount)} {SideLabel(r.ClosingSide)}",
                Debit = IndianFormat.Amount(r.Debit),
                Credit = IndianFormat.Amount(r.Credit),
                IsTwoColumn = true,
                DrillPeriod = new PeriodRange(r.Month.From, r.Month.To),
            });

        Rows.Add(new ReportRow
        {
            Particulars = "Closing Balance",
            Secondary = $"{IndianFormat.AmountAlways(ms.ClosingAmount)} {SideLabel(ms.ClosingSide)}",
            IsTotal = true,
        });
    }

    /// <summary>
    /// Statistics (census 11.8): the two documented sections — <b>Types of Vouchers</b> (every voucher type
    /// with the number entered in the window, and how many of those were cancelled) and <b>Types of
    /// Accounts</b> (the master counts). The counts render in the single Amount cell as plain integers, not
    /// as money — a count is not a rupee figure and must not be formatted like one.
    /// </summary>
    private void BuildStatistics()
    {
        var period = StatementPeriod;
        var stats = Statistics.Build(_company, period.From, period.To);
        Title = "Statistics";
        Subtitle = $"{CompanyName}  —  for the period {FormatDate(period.From)} to {FormatDate(period.To)}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Types of Vouchers", IsHeader = true });
        foreach (var t in stats.VoucherTypes)
            Rows.Add(new ReportRow
            {
                Particulars = t.Name,
                Secondary = t.CancelledCount == 0
                    ? string.Empty
                    : $"{t.CancelledCount} cancelled",
                Amount = t.Count.ToString(CultureInfo.InvariantCulture),
            });
        Rows.Add(new ReportRow
        {
            Particulars = "Total Vouchers",
            Secondary = stats.TotalCancelledVouchers == 0
                ? string.Empty
                : $"{stats.TotalCancelledVouchers} cancelled",
            Amount = stats.TotalVouchers.ToString(CultureInfo.InvariantCulture),
            IsTotal = true,
        });

        Rows.Add(new ReportRow { Particulars = "Types of Accounts", IsHeader = true });
        foreach (var m in stats.Masters)
            Rows.Add(new ReportRow
            {
                Particulars = m.Name,
                Amount = m.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    /// <summary>"Dr" / "Cr" for a balance side.</summary>
    private static string SideLabel(DrCr side) => side == DrCr.Debit ? "Dr" : "Cr";
}

/// <summary>
/// One entry in a report's scenario picker: either the actual books (<see cref="Scenario"/> is null,
/// shown as "Actual (no scenario)") or a defined <see cref="Domain.Scenario"/>. The <see cref="Display"/>
/// is what the combo shows.
/// </summary>
public sealed class ScenarioOption
{
    /// <summary>The wrapped scenario, or null for the real (actual-books) option.</summary>
    public Scenario? Scenario { get; }

    /// <summary>The combo label ("Actual (no scenario)" or the scenario name).</summary>
    public string Display { get; }

    public ScenarioOption(Scenario? scenario)
    {
        Scenario = scenario;
        Display = scenario is null ? "Actual (no scenario)" : scenario.Name;
    }

    /// <summary>The shared "actual books" option (a null scenario).</summary>
    public static ScenarioOption Actual { get; } = new((Scenario?)null);
}

/// <summary>
/// One header of a comparative (RQ-4) report grid: the column's display <see cref="Label"/> (its period and/or
/// scenario) plus a pre-formatted <see cref="TotalText"/> total line for that column. Presentation-only.
/// </summary>
public sealed class ComparativeColumnVM
{
    public string Label { get; }
    public string TotalText { get; }

    public ComparativeColumnVM(string label, string totalText)
    {
        Label = label;
        TotalText = totalText;
    }
}

/// <summary>
/// One row of a comparative (RQ-4) report grid: the line's display <see cref="Label"/> (ledger / group / item),
/// its optional <see cref="GroupName"/>, and one pre-formatted value <see cref="Cells"/> string per column
/// (aligned to the grid's columns; a blank string marks a column where the key is absent). Presentation-only.
/// </summary>
public sealed class ComparativeRowVM
{
    public string Label { get; }
    public string? GroupName { get; }

    /// <summary>The formatted value cells, left→right, aligned to <see cref="ReportsViewModel.ComparativeColumns"/>.</summary>
    public System.Collections.Generic.IReadOnlyList<string> Cells { get; }

    public ComparativeRowVM(string label, string? groupName, System.Collections.Generic.IReadOnlyList<string> cells)
    {
        Label = label;
        GroupName = groupName;
        Cells = cells;
    }
}

/// <summary>A selectable wage month on a payroll report (its first-of-month date + "MMM yyyy" label). Every payroll
/// report (Payslip / Pay Sheet / Payroll Register / Attendance / Payment Advice) is built one wage month at a time.</summary>
public sealed class PayrollMonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>
/// A selectable <b>statutory period</b> on the four multi-month payroll statutory forms (W7-D2): a PF currency
/// period (1 Mar … 28/29 Feb, for Forms 3A / 6A) or an ESI contribution period (Apr–Sep / Oct–Mar, for ESI Forms
/// 5 / 6). Carries its own window, so the report never has to re-derive one from a loose date.
/// </summary>
public sealed class StatutoryPeriodOption
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }

    /// <summary>The period as the forms head it — "01-Mar-2025 to 28-Feb-2026".</summary>
    public string Label =>
        $"{From.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} to {To.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}";

    public override string ToString() => Label;
}

/// <summary>A selectable employee on the Payslip screen (its id + display label "Name (Number)").</summary>
public sealed class PayrollEmployeeOption
{
    public Guid EmployeeId { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}

/// <summary>
/// One entry of the Employee Pay Head Breakup's pay-head scope (census 7.24) — the vendor's "List of Pay Heads"
/// (<c>help.tallysolutions.com/tally-prime/payroll-reports/payroll-employee-pay-head-breakup-tally/</c>).
/// </summary>
public sealed class PayrollPayHeadOption
{
    public Guid PayHeadId { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}

/// <summary>
/// One entry of the Cheque Printing report's bank scope — the vendor's "List of Banks"
/// (<c>help.tallysolutions.com/print-cheques/</c>, "Cheque Printing Report").
/// <see cref="LedgerId"/> is <see cref="Guid.Empty"/> on the <see cref="AllBanks"/> entry, which means no filter.
/// </summary>
public sealed class ChequeBankOption
{
    public Guid LedgerId { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;

    /// <summary>The unfiltered head of the list. A NEW instance per call would break <c>SelectedItem</c>
    /// identity in the ComboBox, so it is a single shared instance.</summary>
    public static readonly ChequeBankOption AllBanks = new() { Display = "All Banks" };
}

/// <summary>One column of the shared payroll matrix (Pay Sheet / Payroll Register / Attendance / Payment Advice):
/// its <see cref="Header"/>, fixed pixel <see cref="Width"/> and whether it is a right-aligned numeric column.
/// Header and body cells carry the same widths so they line up as the grid scrolls horizontally.</summary>
public sealed class PayrollMatrixColumnVm
{
    public string Header { get; init; } = string.Empty;
    public double Width { get; init; }
    public bool IsNumeric { get; init; }
    public bool IsText => !IsNumeric;
}

/// <summary>One cell of a payroll matrix row — its formatted <see cref="Text"/>, its column <see cref="Width"/> and
/// numeric/text alignment (mirrors the owning <see cref="PayrollMatrixColumnVm"/>).</summary>
public sealed class PayrollMatrixCellVm
{
    public string Text { get; init; } = string.Empty;
    public double Width { get; init; }
    public bool IsNumeric { get; init; }
    public bool IsText => !IsNumeric;
}

/// <summary>One row of a payroll matrix — the per-column <see cref="Cells"/> and whether it is the bold footing
/// Grand-Total row.</summary>
public sealed class PayrollMatrixRowVm
{
    public IReadOnlyList<PayrollMatrixCellVm> Cells { get; init; } = Array.Empty<PayrollMatrixCellVm>();
    public bool IsTotal { get; init; }
}

/// <summary>One printed line of the Payslip detail — a head <see cref="Name"/> and its always-rendered
/// <see cref="Amount"/> (an earning, a deduction, or an employer contribution).</summary>
public sealed class PayslipLineVm
{
    public string Name { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
}
