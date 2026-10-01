using System.Collections.Generic;
using System.Linq;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The ER-13 company-feature gate a report KIND sits behind — the F11 switch whose MENU ROW surfaces that
/// report. One member per distinct gate the report menus actually use; <see cref="None"/> is a report every
/// company may open.
///
/// <para>🔴 <b>THIS ENUM EXISTS SO THE SAVED-VIEW DOOR AND THE MENU DOOR CANNOT DRIFT.</b> The product's report
/// gates live on the menu ROW, not on the opener — "Payroll Reports" is a conditional
/// <c>col.Add(...)</c>, the Item Cost Analysis rows are another, the VAT Reports group another. A saved view
/// (Alt+K) stores a kind TOKEN and re-opens that kind directly, so it walks past every one of those rows. A
/// previous pass closed the kinds whose OPENERS happened to carry their own guard and stated in a red-flagged
/// comment that the class was closed; a review then MEASURED a saved view opening the Pay Sheet — every
/// employee's gross, deductions and net pay — on a company with F11 Payroll switched OFF. The rule "a kind
/// belongs on the list only when an opener already refuses it" guarantees the list stays incomplete.
/// <b>That list held FIFTEEN kinds of ninety-three</b> — the two payroll registers, the ten payroll-statutory
/// forms and the three cost kinds. (The review that found the hole said eighteen; the arms are countable in
/// this file's own history and there were fifteen. The number is corrected here rather than repeated, because
/// an uncounted figure is the shape of defect this branch has been withheld for.)
/// So the gate is no longer a list of exceptions: all ninety-three kinds carry an explicit decision, the
/// decision is EVALUATED through the same properties the menu builders branch on, and a kind with no decision
/// is DENIED rather than waved through.</para>
/// </summary>
public enum ReportFeatureGate
{
    /// <summary>No company feature gates this report — its menu row is unconditional.</summary>
    None,

    /// <summary>F11 → Maintain Payroll. Gates the "Payroll Reports" group (the pay statements, the attendance
    /// pair and the two breakups).</summary>
    Payroll,

    /// <summary>F11 → Enable Payroll Statutory. Gates the "Statutory Reports → Payroll" group (the PF and ESI
    /// forms and the statutory summary). <b>Not</b> the Income Tax Computation — that row carries a second
    /// condition of its own; see <see cref="SalaryTds"/>.</summary>
    PayrollStatutory,

    /// <summary>
    /// Payroll Statutory <b>and</b> F11 → Enable Salary TDS — the two-part gate the §192 rows ship behind.
    ///
    /// <para>🔴 <b>THIS MEMBER EXISTS BECAUSE THE TABLE ONCE MAPPED THE INCOME TAX COMPUTATION TO
    /// <see cref="PayrollStatutory"/> ALONE, WHICH IS ONE CONDITION SHORT OF ITS MENU ROW.</b> The row lives
    /// inside the Payroll-Statutory column but is added under a further <c>Enable Salary TDS</c> test, so on a
    /// company with the statute on and §192 off the menu row and the Go To row were both gone while a saved view
    /// (Alt+K) still rendered one employee's annual income-tax computation. That is exactly the drift this file
    /// was written to make impossible, surviving in the one place nobody re-read: a gate that models the GROUP
    /// and not the ROW. A two-part row needs a two-part gate.</para>
    /// </summary>
    SalaryTds,

    /// <summary>Payroll Statutory <b>and</b> the establishment's own gratuity enrolment — the two-part gate
    /// census 7.13 ships. A statute switched on with no enrolment has no provision to show.</summary>
    GratuityEnrolment,

    /// <summary>Payroll Statutory <b>and</b> the establishment's own statutory-bonus enrolment (census 7.14).</summary>
    BonusEnrolment,

    /// <summary>F11 → Accounting → Enable Cost Centres. Gates the "Cost Centres" group under Statements of
    /// Accounts.</summary>
    CostCentres,

    /// <summary>F11 → Enable TDS. Gates the "TDS Reports" group.</summary>
    Tds,

    /// <summary>F11 → Enable TCS. Gates the "TCS Reports" group.</summary>
    Tcs,

    /// <summary>TDS <b>or</b> TCS — the gate on "Ledgers without PAN", which spans both taxes.</summary>
    TdsOrTcs,

    /// <summary>F11 → Enable VAT. Gates the "VAT Reports" group (census 15.5 / 15.6).</summary>
    Vat,

    /// <summary>F11 → Maintain Batch-wise details. Gates the "Batch" group under Inventory Reports.</summary>
    Batchwise,

    /// <summary>F11 → Enable multiple Price Levels. Gates the Price List report row.</summary>
    PriceLevels,

    /// <summary>A POS-flagged Sales voucher type exists. Gates the POS Register row.</summary>
    PosSales,

    /// <summary>F11 → Use tracking numbers. Gates the two Bills Pending rows (census 9.8).</summary>
    TrackingNumbers,

    /// <summary>F11 → Enable Cost Tracking. Gates the three Item Cost Analysis rows (census 9.7).</summary>
    CostTracking,

    /// <summary>F11 → Enable Job Costing. Gates the Job Work Analysis row (census 9.6).</summary>
    JobCosting,

    /// <summary>F11 → Enable Job Order Processing. Gates the four Job Work register rows.</summary>
    JobOrderProcessing,
}

public sealed partial class MainWindowViewModel
{
    // =================================================================== the named menu gates (one definition)
    //
    // 🔴 EVERY REPORT-MENU FEATURE CONDITION IS DEFINED ONCE, HERE, AND THE REPORT MENU BUILDERS BRANCH ON THESE
    // PROPERTIES RATHER THAN ON A REPEATED INLINE PATTERN. That is the whole mechanism behind
    // ReportKindIsPermitted: the saved-view door and the menu row evaluate the SAME expression, so a change to
    // one is a change to both and the two cannot answer differently about the same company. Re-inlining any of
    // these conditions at a report-menu site re-opens the drift this file closes.
    //
    // ⚠️ SCOPE, STATED SO THE SENTENCE ABOVE IS TRUE RATHER THAN NEARLY TRUE. These are the gates on REPORT rows
    // and report GROUPS. The MASTER rows that happen to read the same company flags — BuildCreateColumn's Cost
    // Masters, Payroll Masters, Statutory Masters, Batch, BOM and Price Level rows, and the voucher-type rows —
    // are deliberately left inline, because they gate master PAGES rather than ReportKinds and so take no part in
    // the saved-view class this file closes. Folding them in would imply a relationship that does not exist.

    /// <summary>F11 → Maintain Payroll: the "Payroll Reports" group. (The Payroll MASTERS section reads the same
    /// flag inline — see this region's scope note; it gates master pages, not report kinds.)</summary>
    internal bool PayrollFeatureOn => Company is { PayrollEnabled: true };

    /// <summary>F11 → Enable Payroll Statutory: the "Statutory Reports → Payroll" group.</summary>
    internal bool PayrollStatutoryFeatureOn => Company is { PayrollStatutoryEnabled: true };

    /// <summary>The §192 rows' own two-part gate: the Payroll-Statutory group switch AND F11 → Enable Salary TDS.
    /// <c>BuildPayrollStatutoryReportsColumn</c> branches on THIS property for the 24Q / Form 16 / Income Tax
    /// Computation / Form 12BA rows, so the row and <see cref="ReportFeatureGate.SalaryTds"/> are the same
    /// expression and cannot answer differently about the same company.</summary>
    internal bool SalaryTdsFeatureOn =>
        Company is { PayrollStatutoryEnabled: true, SalaryTdsEnabled: true };

    /// <summary>Census 7.13's two-part gate: the statute master switch AND this establishment's gratuity
    /// enrolment. The write guard in <see cref="PostGratuityProvisionFromReport"/> asks the same question.</summary>
    internal bool GratuityRegisterFeatureOn =>
        Company is { PayrollStatutoryEnabled: true, GratuityConfig: not null };

    /// <summary>Census 7.14's two-part gate: the statute master switch AND the statutory-bonus enrolment.</summary>
    internal bool BonusRegisterFeatureOn =>
        Company is { PayrollStatutoryEnabled: true, BonusConfig: not null };

    /// <summary>F11 → Accounting → Enable Cost Centres (census row 1.7). <c>!= false</c> rather than
    /// <c>== true</c> because the flag's own shipped convention is that an unset company is ON.</summary>
    internal bool CostCentresFeatureOn => Company?.EnableCostCentres != false;

    /// <summary>F11 → Enable TDS.</summary>
    internal bool TdsFeatureOn => Company is { TdsEnabled: true };

    /// <summary>F11 → Enable TCS.</summary>
    internal bool TcsFeatureOn => Company is { TcsEnabled: true };

    /// <summary>F11 → Enable VAT (census area 15).</summary>
    internal bool VatFeatureOn => Company is { VatEnabled: true };

    /// <summary>F11 → Maintain Batch-wise details.</summary>
    internal bool BatchwiseFeatureOn => Company is { MaintainBatchwiseDetails: true };

    /// <summary>F11 → Enable multiple Price Levels.</summary>
    internal bool PriceLevelsFeatureOn => Company is { EnableMultiplePriceLevels: true };

    /// <summary>A POS-flagged Sales voucher type exists, so a POS Register has something to show.</summary>
    internal bool PosSalesFeatureOn => Company is { } c && c.VoucherTypes.Any(t => t.IsPosSales);

    /// <summary>F11 → Use tracking numbers (census 9.8). With it off there is no way to key a tracking
    /// number, so the Bills Pending reports would always be empty.</summary>
    internal bool TrackingNumbersFeatureOn => Company is { UseTrackingNumbers: true };

    /// <summary>F11 → Enable Cost Tracking (census 9.7).</summary>
    internal bool CostTrackingFeatureOn => Company is { EnableCostTracking: true };

    /// <summary>F11 → Enable Job Costing (census 9.6).</summary>
    internal bool JobCostingFeatureOn => Company is { EnableJobCosting: true };

    /// <summary>F11 → Enable Job Order Processing.</summary>
    internal bool JobOrderProcessingFeatureOn => Company is { EnableJobOrderProcessing: true };

    // =================================================================== the total kind → gate decision table

    /// <summary>
    /// 🔴 <b>EVERY <see cref="ReportKind"/> AND ITS ER-13 GATE. TOTAL BY CONTRACT, NOT BY CONVENTION.</b>
    ///
    /// <para><see cref="SavedViewGateFor"/> returns <c>null</c> for a kind that is absent here, and
    /// <see cref="ReportKindIsPermitted"/> then DENIES it. A report kind added without a line in this table is
    /// therefore unreachable through a saved view rather than silently ungated — the failure lands on the
    /// developer who added it instead of on an operator whose company switched the feature off. And it lands at
    /// build time in CI, because <c>Every_report_kind_carries_a_saved_view_gate_decision</c> enumerates
    /// <c>Enum.GetValues&lt;ReportKind&gt;()</c> and fails naming the missing member.</para>
    ///
    /// <para><see cref="ReportFeatureGate.None"/> is an explicit DECISION, not a default: it records that this
    /// kind's menu row is unconditional, which a reader can check against the builder named beside it. The GST
    /// trio is the interesting case — "GST Reports" is deliberately an ungated root row (a GST-off company opens
    /// Tax Analysis / GSTR-1 / GSTR-3B to a friendly empty state rather than meeting a missing menu item), so
    /// <c>None</c> here matches the menu rather than contradicting it.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<ReportKind, ReportFeatureGate> ReportKindGates =
        new Dictionary<ReportKind, ReportFeatureGate>
        {
            // ---- the core accounting reports: root Gateway rows, unconditional.
            [ReportKind.TrialBalance] = ReportFeatureGate.None,
            [ReportKind.BalanceSheet] = ReportFeatureGate.None,
            [ReportKind.ProfitAndLoss] = ReportFeatureGate.None,
            [ReportKind.DayBook] = ReportFeatureGate.None,

            // ---- the Day Book's three exception registers (Ctrl+J on the Day Book) — no F11 switch.
            [ReportKind.OptionalVouchersRegister] = ReportFeatureGate.None,
            [ReportKind.CancelledVouchersRegister] = ReportFeatureGate.None,
            [ReportKind.PostDatedVouchersRegister] = ReportFeatureGate.None,

            // ---- BuildInventoryReportsColumn, the unconditional rows.
            [ReportKind.StockSummary] = ReportFeatureGate.None,
            [ReportKind.GodownSummary] = ReportFeatureGate.None,
            [ReportKind.StockItemMovement] = ReportFeatureGate.None,
            [ReportKind.ReceiptNoteRegister] = ReportFeatureGate.None,
            [ReportKind.DeliveryNoteRegister] = ReportFeatureGate.None,
            [ReportKind.RejectionRegister] = ReportFeatureGate.None,
            [ReportKind.PhysicalStockRegister] = ReportFeatureGate.None,
            [ReportKind.OrderRegister] = ReportFeatureGate.None,
            [ReportKind.ReorderStatus] = ReportFeatureGate.None,

            // ---- BuildInventoryReportsColumn, the conditional rows.
            [ReportKind.Batchwise] = ReportFeatureGate.Batchwise,
            [ReportKind.BatchAgeAnalysis] = ReportFeatureGate.Batchwise,
            [ReportKind.PriceList] = ReportFeatureGate.PriceLevels,
            [ReportKind.PosRegister] = ReportFeatureGate.PosSales,
            [ReportKind.PurchaseBillsPending] = ReportFeatureGate.TrackingNumbers,
            [ReportKind.SalesBillsPending] = ReportFeatureGate.TrackingNumbers,
            [ReportKind.StockItemCostAnalysis] = ReportFeatureGate.CostTracking,
            [ReportKind.StockGroupCostAnalysis] = ReportFeatureGate.CostTracking,
            [ReportKind.CostTrackBreakup] = ReportFeatureGate.CostTracking,
            [ReportKind.JobWorkAnalysis] = ReportFeatureGate.JobCosting,
            [ReportKind.JobWorkInOrderBook] = ReportFeatureGate.JobOrderProcessing,
            [ReportKind.JobWorkOutOrderBook] = ReportFeatureGate.JobOrderProcessing,
            [ReportKind.MaterialInRegister] = ReportFeatureGate.JobOrderProcessing,
            [ReportKind.MaterialOutRegister] = ReportFeatureGate.JobOrderProcessing,

            // ---- BuildGstReportsColumn. The "GST Reports" root row is UNCONDITIONAL by design (see the table's
            // own doc): a GST-off company opens these three to an empty state rather than losing the menu row.
            [ReportKind.TaxAnalysis] = ReportFeatureGate.None,
            [ReportKind.Gstr1] = ReportFeatureGate.None,
            [ReportKind.Gstr3b] = ReportFeatureGate.None,

            // ---- BuildStatementsColumn / BuildExceptionReportsColumn: unconditional.
            [ReportKind.CashFlow] = ReportFeatureGate.None,
            [ReportKind.FundsFlow] = ReportFeatureGate.None,
            [ReportKind.RatioAnalysis] = ReportFeatureGate.None,
            [ReportKind.NegativeStock] = ReportFeatureGate.None,
            [ReportKind.NegativeCashBank] = ReportFeatureGate.None,
            [ReportKind.MemorandumRegister] = ReportFeatureGate.None,
            [ReportKind.ReversingJournalRegister] = ReportFeatureGate.None,

            // ---- BuildStatutoryReportsColumn → TDS / TCS groups.
            [ReportKind.TdsOutstanding] = ReportFeatureGate.Tds,
            [ReportKind.TdsNotDeducted] = ReportFeatureGate.Tds,
            [ReportKind.TdsInterest] = ReportFeatureGate.Tds,
            [ReportKind.TdsNatureSummary] = ReportFeatureGate.Tds,
            [ReportKind.TcsOutstanding] = ReportFeatureGate.Tcs,
            [ReportKind.TcsNotCollected] = ReportFeatureGate.Tcs,
            [ReportKind.TcsInterest] = ReportFeatureGate.Tcs,
            [ReportKind.TcsNatureSummary] = ReportFeatureGate.Tcs,
            [ReportKind.LedgersWithoutPan] = ReportFeatureGate.TdsOrTcs,

            // ---- BuildPayrollReportsColumn — the group itself is gated on F11 Maintain Payroll. THIS IS THE
            // FAMILY THE REVIEW MEASURED OPENING THROUGH A SAVED VIEW ON A PAYROLL-OFF COMPANY: the Pay Sheet is
            // every employee's gross, deductions and net pay, and it had no opener guard to be copied from.
            [ReportKind.Payslip] = ReportFeatureGate.Payroll,
            [ReportKind.PaySheet] = ReportFeatureGate.Payroll,
            [ReportKind.PayrollRegister] = ReportFeatureGate.Payroll,
            [ReportKind.AttendanceRegister] = ReportFeatureGate.Payroll,
            [ReportKind.PaymentAdvice] = ReportFeatureGate.Payroll,
            [ReportKind.AttendanceSheet] = ReportFeatureGate.Payroll,
            [ReportKind.PayHeadEmployeeBreakup] = ReportFeatureGate.Payroll,
            [ReportKind.EmployeePayHeadBreakup] = ReportFeatureGate.Payroll,

            // ---- BuildAccountBooksColumn (registers / groups / monthly summary) + Statistics: unconditional.
            [ReportKind.SalesRegister] = ReportFeatureGate.None,
            [ReportKind.PurchaseRegister] = ReportFeatureGate.None,
            [ReportKind.JournalRegister] = ReportFeatureGate.None,
            [ReportKind.CreditNoteRegister] = ReportFeatureGate.None,
            [ReportKind.DebitNoteRegister] = ReportFeatureGate.None,
            [ReportKind.GroupSummary] = ReportFeatureGate.None,
            [ReportKind.GroupVouchers] = ReportFeatureGate.None,
            [ReportKind.LedgerMonthlySummary] = ReportFeatureGate.None,
            [ReportKind.Statistics] = ReportFeatureGate.None,

            // ---- BuildBankingColumn: every row is unconditional (verified by reading the builder).
            [ReportKind.ChequePrinting] = ReportFeatureGate.None,
            [ReportKind.SupplierPaymentAdvice] = ReportFeatureGate.None,
            [ReportKind.ChequeRegister] = ReportFeatureGate.None,
            [ReportKind.ChequeRegisterDetail] = ReportFeatureGate.None,
            [ReportKind.DepositSlip] = ReportFeatureGate.None,
            [ReportKind.EPayments] = ReportFeatureGate.None,

            // ---- BuildPayrollStatutoryReportsColumn — the group is gated on F11 Enable Payroll Statutory.
            [ReportKind.PfForm3A] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.PfForm5] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.PfForm6A] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.PfForm10] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.PfForm12A] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.EsiForm3] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.EsiForm5] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.EsiForm6] = ReportFeatureGate.PayrollStatutory,
            [ReportKind.PayrollStatutorySummary] = ReportFeatureGate.PayrollStatutory,
            // Census 7.26 sits in the SAME column but under a further "Enable Salary TDS" test, so it takes the
            // two-part gate rather than the group's. Mapping it to PayrollStatutory was a measured hole: the row
            // left the menu and Go To while a saved view still rendered it.
            [ReportKind.IncomeTaxComputation] = ReportFeatureGate.SalaryTds,

            // ---- BuildVatReportsColumn — the group is gated on F11 Enable VAT (census 15.5 / 15.6).
            [ReportKind.VatComputation] = ReportFeatureGate.Vat,
            [ReportKind.CstFormsReceivable] = ReportFeatureGate.Vat,
            [ReportKind.CstFormsIssuable] = ReportFeatureGate.Vat,

            // ---- W-V2, the five re-homed rows (census 11.9 / 11.10 / 11.11 / 7.13 / 7.14).
            [ReportKind.ReceivablesOutstanding] = ReportFeatureGate.None,
            [ReportKind.PayablesOutstanding] = ReportFeatureGate.None,
            [ReportKind.CostCategorySummary] = ReportFeatureGate.CostCentres,
            [ReportKind.CostCentreBreakup] = ReportFeatureGate.CostCentres,
            [ReportKind.CostCentreLedgerBreakup] = ReportFeatureGate.CostCentres,
            [ReportKind.BudgetVariance] = ReportFeatureGate.None,
            [ReportKind.GratuityProvisionRegister] = ReportFeatureGate.GratuityEnrolment,
            [ReportKind.BonusRegister] = ReportFeatureGate.BonusEnrolment,
        };

    /// <summary>
    /// The ER-13 gate recorded for <paramref name="kind"/>, or <c>null</c> when the kind carries no decision at
    /// all. Public because the totality test asserts over it: a new <see cref="ReportKind"/> with no line in
    /// <see cref="ReportKindGates"/> must fail a test rather than reach an operator.
    /// </summary>
    public static ReportFeatureGate? SavedViewGateFor(ReportKind kind) =>
        ReportKindGates.TryGetValue(kind, out var gate) ? gate : null;

    /// <summary>
    /// Whether this company has the named feature on — evaluated through the SAME properties the menu builders
    /// branch on, which is what makes the saved-view door and the menu row incapable of disagreeing.
    /// </summary>
    private bool ReportFeatureGateIsOpen(ReportFeatureGate gate) => gate switch
    {
        ReportFeatureGate.None => true,
        ReportFeatureGate.Payroll => PayrollFeatureOn,
        ReportFeatureGate.PayrollStatutory => PayrollStatutoryFeatureOn,
        ReportFeatureGate.SalaryTds => SalaryTdsFeatureOn,
        ReportFeatureGate.GratuityEnrolment => GratuityRegisterFeatureOn,
        ReportFeatureGate.BonusEnrolment => BonusRegisterFeatureOn,
        ReportFeatureGate.CostCentres => CostCentresFeatureOn,
        ReportFeatureGate.Tds => TdsFeatureOn,
        ReportFeatureGate.Tcs => TcsFeatureOn,
        ReportFeatureGate.TdsOrTcs => TdsFeatureOn || TcsFeatureOn,
        ReportFeatureGate.Vat => VatFeatureOn,
        ReportFeatureGate.Batchwise => BatchwiseFeatureOn,
        ReportFeatureGate.PriceLevels => PriceLevelsFeatureOn,
        ReportFeatureGate.PosSales => PosSalesFeatureOn,
        ReportFeatureGate.TrackingNumbers => TrackingNumbersFeatureOn,
        ReportFeatureGate.CostTracking => CostTrackingFeatureOn,
        ReportFeatureGate.JobCosting => JobCostingFeatureOn,
        ReportFeatureGate.JobOrderProcessing => JobOrderProcessingFeatureOn,

        // 🔴 DENY, do not wave through. A gate member added without an arm here is a gate nobody evaluated, and
        // the safe reading of "nobody decided" on a confidentiality boundary is "not permitted".
        //
        // ⚠️ AND WHAT THIS LINE IS AND IS NOT COVERED BY, STATED EXACTLY, BECAUSE AN EARLIER DRAFT OF THIS
        // COMMENT CLAIMED MORE. `Every_report_feature_gate_member_is_reached_by_a_report_kind` proves no gate
        // member is an orphan, and the seventeen cases of
        // `A_saved_view_refuses_a_report_family_this_company_has_not_switched_on` plus
        // `A_saved_view_refuses_a_cost_centre_report_when_the_feature_is_switched_off` between them drive EVERY
        // non-`None` arm above through a company that has the feature off. What NO test proves is that a
        // FUTURE gate member gets an arm — this line is the only thing standing there, and it denies. That is
        // the deliberate choice, not an oversight: a new gate falls closed, and the failure is a report that
        // will not open rather than one that opens when it should not.
        _ => false,
    };

    /// <summary>
    /// The ER-13 company-feature gate for a report KIND, so every door into a report asks the same question.
    /// Returns true when this company may see this kind at all.
    ///
    /// <para>🔴 <b>DENY BY DEFAULT, AND THAT IS THE FIX.</b> Its predecessor enumerated the kinds it refused and
    /// permitted everything else, with the stated rule "a kind belongs on this list only when an opener already
    /// refuses it". That rule cannot close the class: most report gates live on the menu ROW, so the fifteen
    /// listed kinds were the ones that happened to have opener guards, and a review then measured a saved view
    /// opening the Pay Sheet — every employee's pay — on a company with F11 Payroll switched off. Now every kind
    /// carries an explicit decision in <see cref="ReportKindGates"/> and a kind with none is refused, so a report
    /// cannot be BORN ungated.</para>
    ///
    /// <para>Used by <see cref="ApplySavedView"/> (the Alt+K door) and available to any future door. The openers
    /// keep their own inline guards; both now evaluate the same properties, so they cannot drift.</para>
    /// </summary>
    private bool ReportKindIsPermitted(ReportKind kind) =>
        SavedViewGateFor(kind) is { } gate && ReportFeatureGateIsOpen(gate);
}
