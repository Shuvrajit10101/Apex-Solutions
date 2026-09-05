using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
// Aliased rather than importing Apex.Ledger.Services wholesale: this file deliberately fully-qualifies engine
// services (ManufacturingJournalService, …) so that namespace cannot start shadowing Apex.Desktop.Services.
using VoucherTypeResolver = Apex.Ledger.Services.VoucherTypeResolver;
// Phase 10.11 S4 — the Delete guards. Aliased for the reason above, not imported.
using MasterDeletionRules = Apex.Ledger.Services.MasterDeletionRules;

namespace Apex.Desktop.ViewModels;

/// <summary>Which screen the single window is currently showing.</summary>
public enum Screen
{
    CompanySelect,
    CreateCompany,

    // Company Alteration — the profile fields of the OPEN company (mailing name, postal block, book dates,
    // base currency), reached from the Gateway's Company section. Its own screen id rather than a mode flag on
    // CreateCompany because the two are reached from different places: creation must work with no company open,
    // alteration needs one.
    AlterCompany,

    Gateway,
    Report,

    // WI-12 — the Day-Book "Add Voucher" (Alt+A) voucher-type picker: a menu column of every ACTIVE voucher
    // type appended to the RIGHT of the live Day Book (the report stays bound beneath it, mirroring the F12
    // report-config column), so picking a type opens that entry over the Day Book and Esc pops back to it.
    AddVoucherPicker,

    // W2-14 (census 14.1) — Go To (Alt+G): the jump-anywhere index over the shell's own openers, pushed as a
    // cascade column over whatever surface was open. Ctrl+G "Switch To" (row 14.2) is a DIFFERENT verb in the
    // vendor documentation and is deliberately NOT built here.
    GoTo,

    ReportConfig,

    // W2-13a (census 14.5) — the Ctrl+B "Basis of Values" panel: the report Scale Factor, pushed as a cascade
    // column over the live report exactly like the F12 config panel beside it.
    BasisOfValues,

    ReportSortFilter,
    AddComparisonColumn,
    AutoColumns,
    SaveView,
    SavedViews,
    PrintPreview,
    PrintConfig,
    Export,
    ExportData,
    ImportData,

    // Data -> Backup / Restore: the backup carve-out from the otherwise-excluded Phase 10, because plan.md names
    // backup as the mitigation for its OWN top-ranked data-loss risk (R-7).
    BackupCompany,
    RestoreCompany,
    EmailCompose,
    SmtpSettings,
    VoucherEntry,
    InventoryVoucherEntry,
    LedgerMaster,
    AccountGroupMaster,
    ChartOfAccounts,
    Outstandings,
    CostCategoryMaster,
    CostCentreMaster,
    CostReport,
    BudgetMaster,
    BudgetVariance,
    BankReconciliation,
    BankStatementImport,
    ScenarioMaster,
    InterestReport,
    CurrencyMaster,
    ForexReport,
    StockGroupMaster,
    StockCategoryMaster,
    UnitMaster,
    GodownMaster,
    StockItemMaster,
    BatchMaster,
    BatchAllocation,
    BomMaster,
    ManufacturingJournalEntry,
    JobWorkOrderEntry,
    MaterialMovementEntry,
    PosBilling,
    GstConfig,
    GstRateSetup,

    // F12 voucher-numbering configuration (numbering-design-v2 §5; §9 S4) — pushed as a cascade column over a
    // voucher-entry context; edits the per-type Prefix/Suffix/Width/Prefill/Prevent-duplicate S3-persisted fields.
    VoucherNumberingConfig,

    // Composition returns (Phase 9 slice 3; RQ-16) — CMP-08 (quarterly) + GSTR-4 (annual), surfaced only for a
    // Composition dealer under Reports → Statutory Reports → Composition Returns.
    Cmp08Report,
    Gstr4Report,

    // Advanced-GST read-only report/return screens (Phase 9 UI-1; RQ-17) — surfaced for a Regular GST company under
    // Reports → Statutory Reports → Annual Returns / GST Returns (Advanced).
    Gstr9Report,
    Gstr9cReport,
    ElectronicLedgersReport,
    ItcSetOffReport,
    ItcReversalReport,
    Gstr2bReconReport,
    ItcGateReport,
    QrmpReport,
    GstAmendmentsReport,
    EInvoiceEWayStatusReport,

    // Advanced-GST INTERACTIVE action screens (Phase 9 UI-2; RQ-17) — the screens that DRIVE the engine's actions,
    // surfaced for a Regular GST company under Reports → Statutory Reports → GST Actions. Opening one posts nothing;
    // only an explicit action mutates.
    ImsActions,
    RunSetOff,
    PostItcReversal,
    ImportGstr2b,
    GenerateEInvoice,
    GenerateEWayBill,

    NatureOfPaymentMaster,
    NatureOfGoodsMaster,
    TdsStatPayment,
    ChallanReconciliation,
    Form26Q,
    TcsStatPayment,
    TcsChallanReconciliation,
    Form27EQ,
    Form16A,
    Form27D,
    Form27A,
    PriceLevelsMaster,
    PriceListsMaster,
    ReorderLevelsMaster,

    // Payroll masters (Phase 8 slice 1; RQ-1/RQ-2/RQ-3) — surfaced only when F11 "Maintain Payroll" is on.
    EmployeeCategoryMaster,
    EmployeeGroupMaster,
    EmployeeMaster,
    PayrollUnitMaster,
    AttendanceTypeMaster,

    // Payroll masters (Phase 8 slice 2; RQ-4/RQ-5) — Pay Head + Salary Details, same F11 gate.
    PayHeadMaster,
    SalaryStructureMaster,

    // Payroll vouchers (Phase 8 slice 3; RQ-6/RQ-7) — Attendance / Production + Payroll, same F11 gate.
    AttendanceVoucherEntry,
    PayrollVoucherEntry,

    // Payroll statutory reports — PF ECR / Challan (Phase 8 slice 4; RQ-9), ESI Monthly Contribution
    // (Phase 8 slice 5; RQ-10) and PT Deduction Register (Phase 8 slice 6; RQ-11), all gated on Payroll Statutory.
    PfEcrReport,
    EsiContributionReport,
    ProfessionalTaxRegister,

    // Gratuity provision + statutory Bonus registers (Phase 8 slice 9; RQ-14/RQ-15) — under Reports → Statutory
    // Reports → Payroll, each gated on its own enrolment (GratuityConfig / BonusConfig).
    GratuityProvisionRegister,
    BonusRegister,

    // §192 salary-TDS (Phase 8 slice 7; RQ-12/RQ-13) — the per-employee Form-12BB declaration master + the Form 24Q
    // return and Form 16 certificate reports, all gated on the F11 "Enable Salary TDS" switch.
    TaxDeclarationMaster,
    Form24Q,
    Form16,

    LedgerVouchers,
    VoucherDetail,
}

/// <summary>
/// Which Gateway submenu the RIGHTMOST menu column of the cascade is currently showing. The Gateway
/// root is always column 1; the <c>Vouchers</c> and <c>Create</c> submenus appear as an extra menu
/// column to its right. Kept for the step-back semantics the tests assert.
/// </summary>
public enum GatewayMenu
{
    Root,
    Vouchers,
    Create,
    Outstandings,
    StatementsOfAccounts,
    CostCentres,
    Budgets,
    Banking,
    OtherVouchers,
    OrderVouchers,
    InventoryVouchers,
    InventoryReports,

    // Reports → Inventory Reports → Batch (Phase 6 Cluster 1; RQ-8/RQ-54): Batch-wise + Age Analysis.
    InventoryBatchReports,

    GstReports,
    Statements,
    ExceptionReports,

    // Account Books family (catalog §16 / RQ-30): Cash Book / Bank Book / Ledger, each drilling to a
    // ledger picker that opens that ledger's LedgerBook (a pure reuse of the existing RQ-7 drill).
    AccountBooks,
    CashBook,
    BankBook,
    LedgerBooks,

    // W2-12 (census 11.7): Reports → Account Books → Groups → Group Summary / Group Vouchers each open a
    // DATA-DRIVEN picker column of the company's own accounting groups, mirroring the ledger-book pickers
    // above (so a bare letter FILTERS the list rather than firing a computed hotkey — the WI-2/WI-9 rule).
    GroupSummaryPicker,
    GroupVouchersPicker,

    // Reports → Statutory Reports (Phase 7 slice 8): the TDS/TCS exception & outstanding reports, nested under
    // TDS Reports / TCS Reports sub-groups (+ a common Ledgers-without-PAN report spanning both taxes).
    StatutoryReports,
    TdsReports,
    TcsReports,

    // Reports → Statutory Reports → Composition Returns (Phase 9 slice 3): CMP-08 + GSTR-4, surfaced only for a
    // Composition dealer.
    CompositionReturns,

    // Reports → Statutory Reports → Annual Returns / GST Returns (Advanced) (Phase 9 UI-1): the advanced-GST
    // read-only report screens, surfaced only for a Regular GST company.
    AnnualReturns,
    GstAdvancedReturns,

    // Reports → Statutory Reports → GST Actions (Phase 9 UI-2): the advanced-GST INTERACTIVE screens (IMS, run
    // set-off, post reversal, import 2B, generate e-invoice / e-Way Bill), surfaced only for a Regular GST company.
    GstActions,

    // Reports → Statutory Reports → Payroll (PF) (Phase 8 slice 4): the PF ECR / Challan report, nested under a
    // Payroll sub-group only when Payroll Statutory is enabled.
    PayrollStatutoryReports,

    // Reports → Payroll Reports (Phase 8 slice 8): the payslip + pay sheet + payroll register + attendance register +
    // payment advice, a group under the Reports root shown only when Payroll is enabled.
    PayrollReports,

    // Data -> Backup / Restore: the two data-safety screens, nested under a "Data" section on the Gateway root so
    // backup is reachable through the ordinary cascade, not a hidden hotkey.
    Data,
}

/// <summary>
/// The single-window shell view model — the Gateway-of-Apex-Solutions state machine, now driving a
/// CASCADING MULTI-COLUMN ("Miller columns") Gateway. Column 1 is the root Gateway menu; drilling into
/// a group item (Vouchers / Create) adds a submenu column to its right, and drilling into a page item
/// (a report, a voucher-entry screen, a ledger master, or the chart of accounts) adds a page column to
/// the right — earlier columns stay visible, showing their selected item in a dim "inactive" style.
/// Changing the selection in an earlier column discards every column to its right.
///
/// <para>The pre-company screens (Company Select / Create Company) keep the classic single centred
/// <see cref="Menu"/>. On the Gateway, <see cref="Menu"/> and <see cref="SelectedIndex"/> transparently
/// project the ACTIVE column so the keyboard driver and the existing tests keep working. Kept
/// UI-toolkit-free so it is unit-testable headlessly.</para>
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;

    [ObservableProperty] private Screen _currentScreen = Screen.CompanySelect;
    [ObservableProperty] private string _screenTitle = "Select Company";
    [ObservableProperty] private string _statusCompany = "No company loaded";
    [ObservableProperty] private string _statusDate = string.Empty;
    [ObservableProperty] private string _newCompanyName = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// The classic single centred menu — used only on the pre-company screens (Company Select /
    /// Create Company). On the Gateway the cascade in <see cref="Columns"/> is shown instead; there,
    /// this collection mirrors the ACTIVE column so keyboard/tests see the focused list.
    /// </summary>
    public ObservableCollection<MenuItemViewModel> Menu { get; } = new();

    /// <summary>
    /// The cascading Gateway columns (left → right). Non-empty only while a company is open and the
    /// Gateway is showing; the pre-company screens use <see cref="Menu"/> instead.
    /// </summary>
    public ObservableCollection<GatewayColumn> Columns { get; } = new();

    /// <summary>The right-hand vertical button bar for the current screen.</summary>
    public ObservableCollection<ButtonBarItem> ButtonBar { get; } = new();

    /// <summary>True whenever the cascading Gateway (rather than the centred menu) is showing.</summary>
    [ObservableProperty] private bool _isGatewayCascade;

    /// <summary>The reports view model, non-null only while a report page column is open (rightmost).</summary>
    [ObservableProperty] private ReportsViewModel? _reports;

    /// <summary>The voucher-entry view model, non-null only while a voucher page column is open.</summary>
    [ObservableProperty] private VoucherEntryViewModel? _voucherEntry;

    /// <summary>The inventory/order voucher-entry view model, non-null only while such a page column is open.</summary>
    [ObservableProperty] private InventoryVoucherEntryViewModel? _inventoryVoucherEntry;

    /// <summary>The ledger-master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private LedgerMasterViewModel? _ledgerMaster;

    /// <summary>The accounting-Group master view model, non-null only while that page column is open (WI-7).</summary>
    [ObservableProperty] private AccountGroupMasterViewModel? _accountGroupMaster;

    /// <summary>The chart-of-accounts tree view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private ChartOfAccountsViewModel? _chartOfAccounts;

    /// <summary>The Outstandings (Receivables/Payables) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private OutstandingsViewModel? _outstandings;

    /// <summary>The Cost-Category master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private CostCategoryMasterViewModel? _costCategoryMaster;

    /// <summary>The Cost-Centre master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private CostCentreMasterViewModel? _costCentreMaster;

    /// <summary>The cost-report (Category Summary / Break-up) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private CostReportsViewModel? _costReports;

    /// <summary>The Budget-creation master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private BudgetMasterViewModel? _budgetMaster;

    /// <summary>The Budget Variance report view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private BudgetVarianceViewModel? _budgetVariance;

    /// <summary>The Bank Reconciliation (BRS) view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private BankReconciliationViewModel? _bankReconciliation;

    /// <summary>The Import Bank Statement view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private BankStatementImportViewModel? _bankStatementImport;

    /// <summary>The Scenario-creation master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private ScenarioMasterViewModel? _scenarioMaster;

    /// <summary>The Interest Calculation report view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private InterestReportViewModel? _interestReport;

    /// <summary>The Currency-creation master (+ Rates of Exchange) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private CurrencyMasterViewModel? _currencyMaster;

    /// <summary>The Forex Gain/Loss (unrealized revaluation) report view model, non-null only while that page is open.</summary>
    [ObservableProperty] private ForexReportViewModel? _forexReport;

    /// <summary>The Stock-Group master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private StockGroupMasterViewModel? _stockGroupMaster;

    /// <summary>The Stock-Category master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private StockCategoryMasterViewModel? _stockCategoryMaster;

    /// <summary>The Unit-of-Measure master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private UnitMasterViewModel? _unitMaster;

    /// <summary>The Godown master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private GodownMasterViewModel? _godownMaster;

    /// <summary>The Stock-Item master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private StockItemMasterViewModel? _stockItemMaster;

    /// <summary>The Batch/Lot master view model (Phase 6 Cluster 1), non-null only while that page column is open.</summary>
    [ObservableProperty] private BatchMasterViewModel? _batchMaster;

    /// <summary>The batch-allocation sub-screen view model (Phase 6 Cluster 1; RQ-3), non-null only while it is open.</summary>
    [ObservableProperty] private BatchAllocationViewModel? _batchAllocation;

    /// <summary>The Bill-of-Materials master view model (Phase 6 Cluster 2; RQ-9), non-null only while that page is open.</summary>
    [ObservableProperty] private BomMasterViewModel? _bomMaster;

    /// <summary>The Manufacturing-Journal voucher-entry view model (Phase 6 Cluster 2; RQ-11), non-null only while it is open.</summary>
    [ObservableProperty] private ManufacturingJournalEntryViewModel? _manufacturingJournalEntry;

    /// <summary>The Job Work In/Out Order voucher-entry view model (Phase 6 slice 8; RQ-47), non-null only while it is open.</summary>
    [ObservableProperty] private JobWorkOrderEntryViewModel? _jobWorkOrderEntry;

    /// <summary>The Material In/Out movement voucher-entry view model (Phase 6 slice 8; RQ-48), non-null only while it is open.</summary>
    [ObservableProperty] private MaterialMovementEntryViewModel? _materialMovementEntry;

    [ObservableProperty] private PosBillingViewModel? _posBilling;

    /// <summary>The Company Alteration profile page, non-null only while that page is open.</summary>
    [ObservableProperty] private CompanyProfileViewModel? _alterCompany;

    /// <summary>The company GST-configuration (F11 Features → GST) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private GstConfigViewModel? _gstConfig;

    /// <summary>The F12 voucher-numbering configuration (numbering S4) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private VoucherNumberingConfigViewModel? _voucherNumberingConfig;

    /// <summary>The GST Rate Setup (dated GST 2.0 rate + cess bulk maintenance) view model, non-null only while open.</summary>
    [ObservableProperty] private GstRateSetupViewModel? _gstRateSetup;

    /// <summary>The CMP-08 composition quarterly-statement report (Phase 9 slice 3), non-null only while that page is open.</summary>
    [ObservableProperty] private Cmp08ReportViewModel? _cmp08Report;

    /// <summary>The GSTR-4 composition annual-return report (Phase 9 slice 3), non-null only while that page is open.</summary>
    [ObservableProperty] private Gstr4ReportViewModel? _gstr4Report;

    /// <summary>The GSTR-9 annual-return report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private Gstr9ReportViewModel? _gstr9Report;

    /// <summary>The GSTR-9C reconciliation-statement report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private Gstr9cReportViewModel? _gstr9cReport;

    /// <summary>The GST electronic-ledgers report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private ElectronicLedgersReportViewModel? _electronicLedgersReport;

    /// <summary>The Rule-88A ITC set-off (display-only) report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private ItcSetOffReportViewModel? _itcSetOffReport;

    /// <summary>The ITC-reversal (display-only) report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private ItcReversalReportViewModel? _itcReversalReport;

    /// <summary>The GSTR-2B reconciliation report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private Gstr2bReconReportViewModel? _gstr2bReconReport;

    /// <summary>The ITC-gate (§16(2)(aa) / §17(5)) advisory report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private ItcGateReportViewModel? _itcGateReport;

    /// <summary>The QRMP / IFF cadence report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private QrmpReportViewModel? _qrmpReport;

    /// <summary>The GSTR-1/3B amendments report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private GstAmendmentsReportViewModel? _gstAmendmentsReport;

    /// <summary>The e-Invoice / e-Way status report (Phase 9 UI-1), non-null only while that page is open.</summary>
    [ObservableProperty] private EInvoiceEWayStatusReportViewModel? _eInvoiceEWayStatusReport;

    /// <summary>The IMS (Accept / Reject / Pending) action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private ImsActionsViewModel? _imsActions;

    /// <summary>The Run Set-Off (Rule 88A) &amp; Pay action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private RunSetOffViewModel? _runSetOff;

    /// <summary>The Post-ITC-Reversal action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private PostItcReversalViewModel? _postItcReversal;

    /// <summary>The Import-GSTR-2B action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private ImportGstr2bViewModel? _importGstr2b;

    /// <summary>The Generate-e-Invoice action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private GenerateEInvoiceViewModel? _generateEInvoice;

    /// <summary>The Generate-e-Way-Bill action screen (Phase 9 UI-2), non-null only while that page is open.</summary>
    [ObservableProperty] private GenerateEWayBillViewModel? _generateEWayBill;

    /// <summary>The Nature-of-Payment (TDS section) master (Phase 7 slice 1), non-null only while that page is open.</summary>
    [ObservableProperty] private NatureOfPaymentMasterViewModel? _natureOfPaymentMaster;

    /// <summary>The Nature-of-Goods (§206C TCS) master (Phase 7 slice 1), non-null only while that page is open.</summary>
    [ObservableProperty] private NatureOfGoodsMasterViewModel? _natureOfGoodsMaster;

    /// <summary>The TDS Stat-Payment (deposit) page (Phase 7 slice 3), non-null only while that page is open.</summary>
    [ObservableProperty] private TdsStatPaymentViewModel? _tdsStatPayment;

    /// <summary>The Challan Reconciliation (Alt+R) report (Phase 7 slice 3), non-null only while that page is open.</summary>
    [ObservableProperty] private ChallanReconciliationViewModel? _challanReconciliation;

    /// <summary>The Form 26Q quarterly-TDS-return report (Phase 7 slice 4), non-null only while that page is open.</summary>
    [ObservableProperty] private Form26QViewModel? _form26Q;

    /// <summary>The TCS Stat-Payment (deposit) page (Phase 7 slice 6), non-null only while that page is open.</summary>
    [ObservableProperty] private TcsStatPaymentViewModel? _tcsStatPayment;

    /// <summary>The TCS Challan Reconciliation report (Phase 7 slice 6), non-null only while that page is open.</summary>
    [ObservableProperty] private TcsChallanReconciliationViewModel? _tcsChallanReconciliation;

    /// <summary>The Form 27EQ quarterly-TCS-return report (Phase 7 slice 6), non-null only while that page is open.</summary>
    [ObservableProperty] private Form27EQViewModel? _form27EQ;

    /// <summary>The Form 16A TDS-certificate report (Phase 7 slice 7), non-null only while that page is open.</summary>
    [ObservableProperty] private Form16AViewModel? _form16A;

    /// <summary>The Form 27D TCS-certificate report (Phase 7 slice 7), non-null only while that page is open.</summary>
    [ObservableProperty] private Form27DViewModel? _form27D;

    /// <summary>The Form 27A return-control-chart report (Phase 7 slice 7), non-null only while that page is open.</summary>
    [ObservableProperty] private Form27AViewModel? _form27A;

    /// <summary>The Price Level creation master (slice 5; RQ-26), non-null only while that page is open.</summary>
    [ObservableProperty] private PriceLevelsViewModel? _priceLevels;

    /// <summary>The Price List creation master (slice 5; RQ-27), non-null only while that page is open.</summary>
    [ObservableProperty] private PriceListsViewModel? _priceLists;

    /// <summary>The Reorder Levels master (slice 6; RQ-32), non-null only while that page is open.</summary>
    [ObservableProperty] private ReorderLevelsViewModel? _reorderLevels;

    // Payroll master page VMs (Phase 8 slice 1). Bound from the cascade page column via an EXPLICIT
    // {Binding #Root.((vm:MainWindowViewModel)DataContext).XMaster} path in MainWindow.axaml, not the implicit
    // x:DataType fallback the other master ContentControls use: the Avalonia 12 XamlIl compiled-binding
    // transformer intermittently fails to resolve these (session-new) members through the GatewayColumn→Window
    // fallback on a clean build (AVLN2000), which breaks the CI build and leaves the ContentControls mis-visible
    // → a layout storm that hangs the headless window tests. The explicit #Root path resolves deterministically.

    /// <summary>The Employee-Category master (Phase 8 slice 1; RQ-2), non-null only while that page column is open.</summary>
    [ObservableProperty] private EmployeeCategoryMasterViewModel? _employeeCategoryMaster;

    /// <summary>The Employee-Group master (Phase 8 slice 1; RQ-2), non-null only while that page column is open.</summary>
    [ObservableProperty] private EmployeeGroupMasterViewModel? _employeeGroupMaster;

    /// <summary>The Employee master (Phase 8 slice 1; RQ-2), non-null only while that page column is open.</summary>
    [ObservableProperty] private EmployeeMasterViewModel? _employeeMaster;

    /// <summary>The Payroll-Unit master (Phase 8 slice 1; RQ-3), non-null only while that page column is open.</summary>
    [ObservableProperty] private PayrollUnitMasterViewModel? _payrollUnitMaster;

    /// <summary>The Attendance/Production-Type master (Phase 8 slice 1; RQ-3), non-null only while that page is open.</summary>
    [ObservableProperty] private AttendanceTypeMasterViewModel? _attendanceTypeMaster;

    /// <summary>The Pay Head master (Phase 8 slice 2; RQ-4), non-null only while that page column is open.</summary>
    [ObservableProperty] private PayHeadMasterViewModel? _payHeadMaster;

    /// <summary>The Salary Details / structure master (Phase 8 slice 2; RQ-5), non-null only while that page is open.</summary>
    [ObservableProperty] private SalaryStructureMasterViewModel? _salaryDetails;

    /// <summary>The Attendance / Production voucher entry (Phase 8 slice 3; RQ-6), non-null only while that page is open.</summary>
    [ObservableProperty] private AttendanceVoucherEntryViewModel? _attendanceVoucher;

    /// <summary>The Payroll voucher entry (Phase 8 slice 3; RQ-7), non-null only while that page column is open.</summary>
    [ObservableProperty] private PayrollVoucherEntryViewModel? _payrollVoucher;

    /// <summary>The PF ECR / Challan report (Phase 8 slice 4; RQ-9), non-null only while that page column is open.</summary>
    [ObservableProperty] private PfEcrReportViewModel? _pfEcrReport;

    /// <summary>The ESI Monthly Contribution report (Phase 8 slice 5; RQ-10), non-null only while that page column is
    /// open.</summary>
    [ObservableProperty] private EsiContributionReportViewModel? _esiContributionReport;

    /// <summary>The PT Deduction Register report (Phase 8 slice 6; RQ-11), non-null only while that page column is
    /// open.</summary>
    [ObservableProperty] private ProfessionalTaxRegisterViewModel? _professionalTaxRegister;

    /// <summary>The Gratuity Provision register (Phase 8 slice 9; RQ-14), non-null only while that page column is open.</summary>
    [ObservableProperty] private GratuityProvisionRegisterViewModel? _gratuityProvisionRegister;

    /// <summary>The statutory-Bonus register (Phase 8 slice 9; RQ-15), non-null only while that page column is open.</summary>
    [ObservableProperty] private BonusRegisterViewModel? _bonusRegister;

    /// <summary>The per-employee Income-Tax Declaration (Form 12BB) master (Phase 8 slice 7; RQ-12), non-null only
    /// while that page column is open.</summary>
    [ObservableProperty] private TaxDeclarationViewModel? _taxDeclarationMaster;

    /// <summary>The Form 24Q quarterly salary-TDS-return report (Phase 8 slice 7; RQ-13), non-null only while that
    /// page column is open.</summary>
    [ObservableProperty] private Form24QViewModel? _form24Q;

    /// <summary>The Form 16 salary-TDS-certificate report (Phase 8 slice 7; RQ-13), non-null only while that page
    /// column is open.</summary>
    [ObservableProperty] private Form16ViewModel? _form16;

    /// <summary>The F12 report-Configuration panel view model, non-null only while that config column is open (RQ-6).</summary>
    [ObservableProperty] private ReportConfigViewModel? _reportConfig;

    /// <summary>The Ctrl+B "Basis of Values" (Scale Factor) panel view model, non-null only while that
    /// column is open (W2-13a, census row 14.5).</summary>
    [ObservableProperty] private BasisOfValuesViewModel? _basisOfValues;

    /// <summary>The Alt+F12 report Sort/Filter panel view model, non-null only while that view column is open (RQ-3).</summary>
    [ObservableProperty] private ReportSortFilterViewModel? _reportSortFilter;

    /// <summary>The Alt+C "Add Comparison Column" panel view model, non-null only while that panel column is open (RQ-4).</summary>
    [ObservableProperty] private AddComparisonColumnViewModel? _addComparisonColumn;

    /// <summary>The Alt+N "Auto Columns" chooser view model, non-null only while that panel column is open (RQ-4).</summary>
    [ObservableProperty] private AutoColumnsViewModel? _autoColumns;

    /// <summary>The Ctrl+S "Save View" panel view model, non-null only while that panel column is open (RQ-8).</summary>
    [ObservableProperty] private SaveViewViewModel? _saveView;

    /// <summary>The Alt+K "Saved Views" list panel view model, non-null only while that panel column is open (RQ-8).</summary>
    [ObservableProperty] private SavedViewsViewModel? _savedViews;

    /// <summary>The Alt+G "Go To" jump index view model, non-null only while that panel column is open (W2-14,
    /// census row 14.1).</summary>
    [ObservableProperty] private GoToViewModel? _goTo;

    /// <summary>The P / Ctrl+P "Print Preview" panel view model, non-null only while that preview column is open (RQ-9).</summary>
    [ObservableProperty] private PrintPreviewViewModel? _printPreview;

    /// <summary>The F12 print-config panel (RQ-12) over a voucher/invoice preview, non-null only while that column is open.</summary>
    [ObservableProperty] private PrintConfigViewModel? _printConfigPanel;

    /// <summary>The E / Alt+E "Export" panel view model (RQ-14/16), non-null only while that panel column is open.</summary>
    [ObservableProperty] private ExportViewModel? _exportPanel;

    /// <summary>The Y "Export Data" (canonical company backup, RQ-19/DP-4) panel, non-null only while that column is open.</summary>
    [ObservableProperty] private ExportDataViewModel? _exportDataPanel;

    /// <summary>The O / Alt+O "Import" (canonical/CSV company import, RQ-20..24) panel, non-null only while that column is open.</summary>
    [ObservableProperty] private ImportDataViewModel? _importDataPanel;

    /// <summary>The Data -> "Backup Company" panel (the R-7 carve-out), non-null only while that column is open.</summary>
    [ObservableProperty] private BackupCompanyViewModel? _backupCompanyPanel;

    /// <summary>The Data -> "Restore Company" panel (the R-7 carve-out), non-null only while that column is open.</summary>
    [ObservableProperty] private RestoreCompanyViewModel? _restoreCompanyPanel;

    /// <summary>The M / Ctrl+M "E-Mail" compose panel (RQ-25/26), non-null only while that column is open.</summary>
    [ObservableProperty] private EmailComposeViewModel? _emailCompose;

    /// <summary>The "SMTP Settings" capture panel (RQ-27), non-null only while that column is open.</summary>
    [ObservableProperty] private SmtpSettingsViewModel? _smtpSettings;

    /// <summary>The RQ-7 ledger-vouchers drill column (a drilled TB/BS/P&amp;L ledger's LedgerBook), non-null only while open.</summary>
    [ObservableProperty] private LedgerVouchersViewModel? _ledgerVouchers;

    /// <summary>The RQ-7 read-only voucher-detail drill column, non-null only while that column is open (rightmost).</summary>
    [ObservableProperty] private VoucherDetailViewModel? _voucherDetail;

    /// <summary>
    /// True on the pre-company centred-menu screens (Company Select / Create Company). On the Gateway
    /// the cascade view (<see cref="IsGatewayCascade"/>) is shown instead of this centred menu.
    /// </summary>
    public bool IsMenuScreen => !IsGatewayCascade
        && Reports is null && VoucherEntry is null && InventoryVoucherEntry is null && LedgerMaster is null
        && AccountGroupMaster is null
        && ChartOfAccounts is null
        && Outstandings is null && CostCategoryMaster is null && CostCentreMaster is null
        && CostReports is null && BudgetMaster is null && BudgetVariance is null
        && BankReconciliation is null && BankStatementImport is null && ScenarioMaster is null
        && InterestReport is null && CurrencyMaster is null && ForexReport is null
        && StockGroupMaster is null && StockCategoryMaster is null && UnitMaster is null
        && GodownMaster is null && StockItemMaster is null && BatchMaster is null && BatchAllocation is null
        && BomMaster is null && ManufacturingJournalEntry is null && PosBilling is null
        && JobWorkOrderEntry is null && MaterialMovementEntry is null
        && PriceLevels is null && PriceLists is null && ReorderLevels is null
        && EmployeeCategoryMaster is null && EmployeeGroupMaster is null && EmployeeMaster is null
        && PayrollUnitMaster is null && AttendanceTypeMaster is null
        && PayHeadMaster is null && SalaryDetails is null
        && AttendanceVoucher is null && PayrollVoucher is null && PfEcrReport is null
        && EsiContributionReport is null && ProfessionalTaxRegister is null
        && GratuityProvisionRegister is null && BonusRegister is null
        && TaxDeclarationMaster is null && Form24Q is null && Form16 is null
        && GstConfig is null && GstRateSetup is null && Cmp08Report is null && Gstr4Report is null
        && Gstr9Report is null && Gstr9cReport is null && ElectronicLedgersReport is null
        && ItcSetOffReport is null && ItcReversalReport is null && Gstr2bReconReport is null
        && ItcGateReport is null && QrmpReport is null && GstAmendmentsReport is null
        && EInvoiceEWayStatusReport is null
        && ImsActions is null && RunSetOff is null && PostItcReversal is null && ImportGstr2b is null
        && GenerateEInvoice is null && GenerateEWayBill is null
        && NatureOfPaymentMaster is null && NatureOfGoodsMaster is null
        && TdsStatPayment is null && ChallanReconciliation is null && Form26Q is null
        && TcsStatPayment is null && TcsChallanReconciliation is null && Form27EQ is null
        && Form16A is null && Form27D is null && Form27A is null
        && ReportConfig is null
        && ReportSortFilter is null && AddComparisonColumn is null && AutoColumns is null
        && SaveView is null && SavedViews is null && PrintPreview is null && PrintConfigPanel is null
        && ExportPanel is null && ExportDataPanel is null && ImportDataPanel is null
        && BackupCompanyPanel is null && RestoreCompanyPanel is null
        && EmailCompose is null && SmtpSettings is null
        && LedgerVouchers is null && VoucherDetail is null;

    partial void OnReportsChanged(ReportsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnVoucherEntryChanged(VoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnInventoryVoucherEntryChanged(InventoryVoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnLedgerMasterChanged(LedgerMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnAccountGroupMasterChanged(AccountGroupMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnChartOfAccountsChanged(ChartOfAccountsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnOutstandingsChanged(OutstandingsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnCostCategoryMasterChanged(CostCategoryMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnCostCentreMasterChanged(CostCentreMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnCostReportsChanged(CostReportsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBudgetMasterChanged(BudgetMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBudgetVarianceChanged(BudgetVarianceViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBankReconciliationChanged(BankReconciliationViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBankStatementImportChanged(BankStatementImportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnScenarioMasterChanged(ScenarioMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnInterestReportChanged(InterestReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnCurrencyMasterChanged(CurrencyMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForexReportChanged(ForexReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnStockGroupMasterChanged(StockGroupMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnStockCategoryMasterChanged(StockCategoryMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnUnitMasterChanged(UnitMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGodownMasterChanged(GodownMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnStockItemMasterChanged(StockItemMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBatchMasterChanged(BatchMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBatchAllocationChanged(BatchAllocationViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBomMasterChanged(BomMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnManufacturingJournalEntryChanged(ManufacturingJournalEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnJobWorkOrderEntryChanged(JobWorkOrderEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnMaterialMovementEntryChanged(MaterialMovementEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPosBillingChanged(PosBillingViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstConfigChanged(GstConfigViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstRateSetupChanged(GstRateSetupViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnCmp08ReportChanged(Cmp08ReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstr4ReportChanged(Gstr4ReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstr9ReportChanged(Gstr9ReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstr9cReportChanged(Gstr9cReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnElectronicLedgersReportChanged(ElectronicLedgersReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnItcSetOffReportChanged(ItcSetOffReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnItcReversalReportChanged(ItcReversalReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstr2bReconReportChanged(Gstr2bReconReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnItcGateReportChanged(ItcGateReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnQrmpReportChanged(QrmpReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGstAmendmentsReportChanged(GstAmendmentsReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEInvoiceEWayStatusReportChanged(EInvoiceEWayStatusReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnImsActionsChanged(ImsActionsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnRunSetOffChanged(RunSetOffViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPostItcReversalChanged(PostItcReversalViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnImportGstr2bChanged(ImportGstr2bViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGenerateEInvoiceChanged(GenerateEInvoiceViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGenerateEWayBillChanged(GenerateEWayBillViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnNatureOfPaymentMasterChanged(NatureOfPaymentMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnNatureOfGoodsMasterChanged(NatureOfGoodsMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnTdsStatPaymentChanged(TdsStatPaymentViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnChallanReconciliationChanged(ChallanReconciliationViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm26QChanged(Form26QViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnTcsStatPaymentChanged(TcsStatPaymentViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnTcsChallanReconciliationChanged(TcsChallanReconciliationViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm27EQChanged(Form27EQViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm16AChanged(Form16AViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm27DChanged(Form27DViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm27AChanged(Form27AViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPriceLevelsChanged(PriceLevelsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPriceListsChanged(PriceListsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnReorderLevelsChanged(ReorderLevelsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEmployeeCategoryMasterChanged(EmployeeCategoryMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEmployeeGroupMasterChanged(EmployeeGroupMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEmployeeMasterChanged(EmployeeMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPayrollUnitMasterChanged(PayrollUnitMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnAttendanceTypeMasterChanged(AttendanceTypeMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPayHeadMasterChanged(PayHeadMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnSalaryDetailsChanged(SalaryStructureMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnAttendanceVoucherChanged(AttendanceVoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPayrollVoucherChanged(PayrollVoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPfEcrReportChanged(PfEcrReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEsiContributionReportChanged(EsiContributionReportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnProfessionalTaxRegisterChanged(ProfessionalTaxRegisterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnGratuityProvisionRegisterChanged(GratuityProvisionRegisterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBonusRegisterChanged(BonusRegisterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnTaxDeclarationMasterChanged(TaxDeclarationViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm24QChanged(Form24QViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnForm16Changed(Form16ViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnReportConfigChanged(ReportConfigViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnReportSortFilterChanged(ReportSortFilterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnAddComparisonColumnChanged(AddComparisonColumnViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnAutoColumnsChanged(AutoColumnsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnSaveViewChanged(SaveViewViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnSavedViewsChanged(SavedViewsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPrintPreviewChanged(PrintPreviewViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnPrintConfigPanelChanged(PrintConfigViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnExportPanelChanged(ExportViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnExportDataPanelChanged(ExportDataViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnImportDataPanelChanged(ImportDataViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnBackupCompanyPanelChanged(BackupCompanyViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnRestoreCompanyPanelChanged(RestoreCompanyViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnEmailComposeChanged(EmailComposeViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnSmtpSettingsChanged(SmtpSettingsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnLedgerVouchersChanged(LedgerVouchersViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnVoucherDetailChanged(VoucherDetailViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnIsGatewayCascadeChanged(bool value) => OnPropertyChanged(nameof(IsMenuScreen));

    /// <summary>
    /// Which Gateway submenu the rightmost MENU column is showing (Root / Vouchers / Create) — for the
    /// step-back semantics. A page column on top of the root reads as <see cref="GatewayMenu.Root"/>.
    /// </summary>
    public GatewayMenu CurrentGatewayMenu { get; private set; } = GatewayMenu.Root;

    /// <summary>The currently open company (null before one is selected/created).</summary>
    public Company? Company { get; private set; }

    /// <summary>Index of the highlighted item in the centred pre-company menu.</summary>
    private int _menuSelectedIndex;

    private int _activeColumnIndex;

    /// <summary>
    /// Index of the focused (active) column in the cascade.
    /// <para>
    /// WI-2 — this setter is also the single reset point for picker type-ahead. Focus moving to a different
    /// column is exactly "Esc / a completed selection / entering or leaving a column", so clearing the prefix
    /// on BOTH the column being left and the one being entered covers every reset the feature needs, with no
    /// per-call-site resets to forget at the ~125 places a column is pushed. Type-ahead itself never touches
    /// this index (it only moves the highlight), so a prefix being typed is never cleared underneath it.
    /// </para>
    /// </summary>
    public int ActiveColumnIndex
    {
        get => _activeColumnIndex;
        private set
        {
            if (_activeColumnIndex == value) return;

            ColumnAtOrNull(_activeColumnIndex)?.ResetTypeAhead();
            _activeColumnIndex = value;
            ColumnAtOrNull(value)?.ResetTypeAhead();
        }
    }

    /// <summary>The cascade column at <paramref name="index"/>, or null when out of range.</summary>
    private GatewayColumn? ColumnAtOrNull(int index) =>
        index >= 0 && index < Columns.Count ? Columns[index] : null;

    public MainWindowViewModel() : this(new CompanyStorage()) { }

    public MainWindowViewModel(CompanyStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

        // Built HERE and not in ShowCreateCompany, so it is never null. The company-creation screen is driven
        // directly by ~150 test fixtures that set NewCompanyName and call CreateCompany() without ever opening
        // the screen; a lazily-built form would make CreateCompany read a null and need a second code path.
        CreateCompanyProfile = new CompanyProfileViewModel(_storage, () => { });

        // WI-9: the SHARED choke point for bare-letter hotkeys. Columns are pushed from ~125 call sites, so
        // assigning here — as a column enters the cascade — is what makes the accelerators reach EVERY menu
        // column (root, submenu, picker) instead of only the ones a builder remembered to call. Page columns
        // hold no rows, so it is a no-op for them.
        Columns.CollectionChanged += (_, e) =>
        {
            // WI-2: a column POPPED off the cascade (Esc / Back / a page replacing it) is left with whatever
            // type-ahead prefix was mid-flight. It is removed BEFORE ActiveColumnIndex moves, so the focus-change
            // reset cannot see it — clear it here, where every removal passes.
            if (e.OldItems is not null)
                foreach (GatewayColumn column in e.OldItems)
                    column.ResetTypeAhead();

            if (e.NewItems is null) return;
            foreach (GatewayColumn column in e.NewItems)
                column.AssignHotKeys();
        };

        ShowCompanySelect();
    }

    // =============================================================== screen: company select

    /// <summary>Shows the company-selection menu: existing companies + Create + Load Demo.</summary>
    public void ShowCompanySelect()
    {
        CurrentScreen = Screen.CompanySelect;
        ScreenTitle = "Company Info — Select Company";
        Message = null;
        ClearSubScreens();
        LeaveCascade();
        Menu.Clear();

        foreach (var entry in _storage.ListCompanies())
        {
            var captured = entry;
            Menu.Add(new MenuItemViewModel(captured.Name, () => OpenExisting(captured), "Open"));
        }

        Menu.Add(new MenuItemViewModel("Create Company", ShowCreateCompany, "F3"));
        Menu.Add(new MenuItemViewModel("Load Robert Demo", LoadRobertDemo, "Demo"));

        SetMenuSelected(0);
        BuildButtonBar();
    }

    /// <summary>
    /// The company-creation form's profile fields (mailing name, postal block, book dates, base currency).
    ///
    /// <para><b>The NAME is deliberately NOT read from here.</b> It stays on <see cref="NewCompanyName"/>,
    /// which ~150 test fixtures set directly before calling <see cref="CreateCompany"/>, and which the
    /// creation form has always bound. Routing it through this form as well would give the same value two
    /// homes and one of them would eventually go stale.</para>
    /// </summary>
    public CompanyProfileViewModel CreateCompanyProfile { get; }

    private void ShowCreateCompany()
    {
        CurrentScreen = Screen.CreateCompany;
        ScreenTitle = "Company Creation";
        NewCompanyName = string.Empty;
        ResetCreateCompanyProfile();
        Message = "Enter the company details, then press Ctrl+A (or Enter) to create.";
        LeaveCascade();
        Menu.Clear();
        BuildButtonBar();
    }

    /// <summary>Clears the creation form back to its seeded defaults, so a second create starts blank.</summary>
    private void ResetCreateCompanyProfile()
    {
        CreateCompanyProfile.MailingName = string.Empty;
        CreateCompanyProfile.Address = string.Empty;
        CreateCompanyProfile.SelectedState = null;
        CreateCompanyProfile.Country = "India";
        CreateCompanyProfile.Pin = string.Empty;
        CreateCompanyProfile.FinancialYearStartText = string.Empty;
        CreateCompanyProfile.BooksBeginFromText = string.Empty;
        CreateCompanyProfile.BaseCurrencySymbol = "₹";
        CreateCompanyProfile.BaseCurrencyName = "INR";
        CreateCompanyProfile.DecimalPlacesText = "2";
        CreateCompanyProfile.DecimalUnitName = "Paisa";
        CreateCompanyProfile.ClearMessage();
    }

    /// <summary>
    /// Creates a fresh seeded company from the creation form, saves it, and opens it. No-op on a blank name.
    ///
    /// <para><b>A creation where nothing but the name was typed must stay byte-identical to what this method
    /// produced before the form existed.</b> Every profile field is applied only when it was actually typed —
    /// blank leaves the seeded default in place — and the two dates are passed through as <c>null</c> so
    /// <c>CompanyFactory.CreateSeeded</c>'s own defaulting still governs. That is what keeps ~150 existing
    /// fixtures, and every book already on disk, exactly where they were.</para>
    ///
    /// <para><b>🔴 THE NAME COLLISION IS REFUSED HERE, and it is a book-eater, not a nicety.</b> The company's
    /// <c>.db</c> path is derived from its name with the invalid filename characters replaced
    /// (<c>CompanyStorage.PathForName</c>), and <c>CompanyStorage.Load</c> takes the FIRST company row in the
    /// file. So creating "Acme/Traders" on a machine that already has "Acme_Traders" used to write a SECOND
    /// company row into the FIRST company's file, with no exception and no message — and everything typed into
    /// the second one then became unreachable forever, because the loader never returns it. <b>Alteration refuses
    /// the same collision</b> — <c>CompanyStorage.Rename</c> tests the sanitised destination path before it moves
    /// anything (this comment used to say alteration "refuses to rename" outright, which was true only while the
    /// rename was carved out; census row 1.4 shipped it 2026-09-05). Create and alter must agree on which names
    /// are refusable, so the check is here too. <c>Exists</c> tests the SANITISED path, which is what makes it catch the colliding pair rather than
    /// only the identical name. <b>WHICH pairs collide is platform-dependent</b> — <c>/</c> collapses
    /// everywhere, <c>:</c> only on Windows; see <c>CompanyStorage.PathForName</c> for the full note.</para>
    ///
    /// <para><b>And the domain's own refusals are reported, not thrown.</b> <c>CreateSeeded</c> runs
    /// <c>new Company(...)</c>, whose constructor throws on an impossible pair of book dates; nothing between
    /// here and the Avalonia dispatcher catches, so an escaped exception is a crash with no message on the
    /// form. The screen pre-validates, and this is the backstop behind it.</para>
    /// </summary>
    public void CreateCompany()
    {
        var name = (NewCompanyName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A company name is required.";
            return;
        }

        if (_storage.Exists(name))
        {
            Message = $"A company file already exists for '{name}'. "
                    + "Company names must differ by more than the characters a filename cannot hold.";
            return;
        }

        // Pre-validate the typed profile BEFORE anything is created, so a bad PIN or an impossible pair of
        // book dates is a message on the form rather than a half-created company.
        if (!CreateCompanyProfile.TryReadForCreate(out var profile))
        {
            Message = CreateCompanyProfile.Message;
            return;
        }

        Company company;
        try
        {
            company = Apex.Ledger.Services.CompanyFactory.CreateSeeded(
                name, profile.FinancialYearStart, profile.BooksBeginFrom);

            if (profile.MailingName is { } mailing) company.MailingName = mailing;
            if (profile.Address is { } address) company.Address = address;
            if (profile.State is { } state) company.State = state;
            if (profile.Country is { } country) company.Country = country;
            if (profile.Pin is { } pin) company.Pin = pin;
            if (profile.BaseCurrencySymbol is { } symbol) company.BaseCurrencySymbol = symbol;
            if (profile.BaseCurrencyName is { } currency) company.BaseCurrencyName = currency;
            if (profile.DecimalPlaces is { } places) company.DecimalPlaces = places;
            if (profile.DecimalUnitName is { } unit) company.DecimalUnitName = unit;

            _storage.Save(company);
        }
        catch (Exception ex) when (SaveFailure.IsReportable(ex))
        {
            // Nothing has been opened, so there is nothing to roll back — the half-built aggregate is local and
            // is dropped with the frame. The form keeps everything the operator typed.
            CreateCompanyProfile.Refuse(ex.Message);
            Message = ex.Message;
            return;
        }

        OpenCompany(company);
    }

    /// <summary>
    /// Opens <b>Company Alteration</b> for the OPEN company as a cascade page column: the same profile fields
    /// the creation screen captures, pre-filled.
    ///
    /// <para><b>This screen carries BOTH company verbs census row 1.4 owes</b>, and it is the reference product's
    /// own screen for both (RULING 14 / R7 — <i>help.tallysolutions.com/…/set-up-company-tally/</i>): editing the
    /// <b>Name</b> and accepting RENAMES the book (see <see cref="CompanyProfileViewModel.IsNameEditable"/> — the
    /// name shipped read-only until 2026-09-05 and this comment said so), and <b>Alt+D</b> raises the confirmation
    /// that DELETES it (see <see cref="RequestDeleteOpenCompany"/>).</para>
    ///
    /// <para><b>No accelerator — and the honest reason is scope, not a chord collision.</b> The reference
    /// product reaches company alteration through a COMPANY MENU on <c>Alt+K</c> (Book PDF p.15, Study Guide
    /// pp.61/267 — both [V]). This row shipped saying that chord "is already bound in this application", which
    /// overstates it: measured at <c>Views/MainWindow.axaml.cs</c> line 757 (re-pointed 2026-08-18 — the
    /// comment cited line 653, which now holds the Ctrl+T post-dated toggle; re-located by CONTENT, since the
    /// file's only <c>Key.K</c> test is the one quoted next), the saved-views binding is
    /// <c>Key.K &amp;&amp; Alt &amp;&amp; vm.IsReportContext</c> — it is bound in REPORT context only, and on
    /// the Gateway root column, where this row lives, <c>Alt+K</c> is unbound. The dispatcher already scopes
    /// that chord by context, so a Gateway-scoped one would follow the existing pattern rather than create an
    /// arbitration hazard.
    /// <b>What is actually missing is the menu the chord opens.</b> The attested route is Alt+K → a company
    /// menu → Alter, and this application has no company menu; binding Alt+K straight to this one page would
    /// be an invented shortcut wearing an attested chord, which is worse than none. The row is therefore
    /// reached by arrow and Enter, like Chart of Accounts, and the company menu is logged as owed —
    /// <c>docs/w0-2-company-screen-grounding.md</c> §9 item 17.</para>
    /// </summary>
    public void ShowAlterCompany()
    {
        if (Company is null) return;

        // 🔴 `onChanged` re-syncs the STATUS LINE as well as the button bar, and that became load-bearing when the
        // Name field went editable for census row 1.4: accepting this screen can now RENAME the open book, and the
        // status line — which names the open company — would otherwise keep showing the old name until the operator
        // shut and re-opened the company. `Company` is a plain property with no change notification, so nothing
        // else would ever notice.
        var page = new CompanyProfileViewModel(Company, _storage, onChanged: () =>
        {
            if (Company is { } open) StatusCompany = open.Name;
            BuildButtonBar();
        });
        OpenPageColumn(new GatewayColumn("Company Alteration", page), Screen.AlterCompany,
            "Company Alteration", () => AlterCompany = page);
    }

    /// <summary>
    /// RELEASES the open book — <see cref="Company"/> goes null, the status line stops naming it — and returns to
    /// Company Select.
    ///
    /// <para>🔴 <b>Its ONE caller is the company DELETE (census row 1.4), and that is why it is private.</b> Once
    /// the <c>.db</c> is gone the shell cannot be left holding an aggregate whose file no longer exists: every
    /// later save would silently re-create the deleted book at the same path. W2-18 first wrote this as a public
    /// "Shut Company" verb for a Gateway menu column that <see cref="BuildRootColumn"/> now explains was removed
    /// as unfaithful — leaving it public would have left a method no operator could reach, which is the exact
    /// defect class (<c>CompanyStorage.Rename</c>, <c>CostReports.BuildLedgerBreakup</c>) this pass exists to stop
    /// repeating. <b>The Shut verb itself is NOT claimed as built</b>: it belongs with the Alt+K top menu, inside
    /// open user ruling U-6.</para>
    ///
    /// <para><b>The three status fields are reset by hand because nothing else does it.</b> <see cref="Company"/>
    /// is a plain property, not an <c>[ObservableProperty]</c>, so there is no change handler to hang this on;
    /// <see cref="OpenCompany"/> sets these three together on the way in and this is their only way out.
    /// <see cref="ShowCompanySelect"/> then clears the sub screens and the cascade.</para>
    /// </summary>
    private void ReleaseOpenCompany()
    {
        Company = null;
        StatusCompany = "No company loaded";
        StatusDate = string.Empty;
        ShowCompanySelect();
    }

    /// <summary>Builds, saves and opens the embedded Robert demo (creating a populated company).</summary>
    public void LoadRobertDemo()
    {
        var name = UniqueDemoName();
        var company = DemoData.BuildRobert(name);
        _storage.Save(company);
        OpenCompany(company);
    }

    private string UniqueDemoName()
    {
        var baseName = DemoData.DefaultName;
        if (!_storage.Exists(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!_storage.Exists(candidate)) return candidate;
        }
        return $"{baseName} {Guid.NewGuid():N}";
    }

    private void OpenExisting(CompanyEntry entry)
    {
        try
        {
            var company = _storage.Load(entry);
            OpenCompany(company);
        }
        catch (Exception ex)
        {
            Message = $"Could not open '{entry.Name}': {ex.Message}";
        }
    }

    // =============================================================== screen: gateway (cascade)

    private void OpenCompany(Company company)
    {
        Company = company;
        StatusCompany = company.Name;
        StatusDate = ApexDate.Format(company.FinancialYearStart);
        ShowGateway();
    }

    /// <summary>
    /// Shows the cascading Gateway of Apex Solutions for the open company: column 1 is the root menu
    /// (MASTERS / TRANSACTIONS / REPORTS sections with their items), reset to a single column with the
    /// first item highlighted. Drilling in adds columns to the right.
    /// </summary>
    public void ShowGateway()
    {
        if (Company is null) { ShowCompanySelect(); return; }

        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = GatewayMenu.Root;
        ScreenTitle = "Gateway of Apex Solutions";
        Message = null;
        ClearSubScreens();
        EnterCascade();

        Columns.Clear();
        // WI-1 (DEFECT 2) — the cascade was rebuilt from scratch, so any armed Alt+C request lost its column.
        AbandonCreateOnTheFlyIfColumnGone();
        Columns.Add(BuildRootColumn());
        ActiveColumnIndex = 0;
        Columns[0].SelectFirstSelectable();
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Builds the root Gateway menu column (its section headers and their items).</summary>
    private GatewayColumn BuildRootColumn()
    {
        var col = new GatewayColumn("Gateway of Apex Solutions");

        // ---- MASTERS ----
        // "Alter Company" sits HERE, between Create and Chart of Accounts, and the placement is a correction,
        // not a preference. `docs/invented-vs-cloned.md` IV-29 records this exact menu as invented — "The
        // Gateway's sections and vocabulary are ours, not Tally's — and 'Alter' is not on it" — diagnoses the
        // cause as "the menu GREW A SECTION PER PHASE rather than being laid out once from the reference
        // product", and prescribes: add "Alter" to MASTERS. This row first shipped as a NEW "Company" section
        // placed AHEAD of Masters, i.e. all three moves IV-29 names as wrong, and it moved the Gateway's
        // default keyboard highlight off Masters → Create for every entry into the screen — a product-wide
        // navigation change riding in on an address-capture slice. Under Masters the highlight is back where
        // it was and the section list is the one the register already catalogues.
        // The DIVERGENCE that remains is recorded rather than hidden: the reference product's Masters → Alter
        // is a master-alteration submenu, whereas this row alters the COMPANY. See IV-29 and
        // docs/w0-2-company-screen-grounding.md §9 item 17.
        col.Add(MenuItemViewModel.Header("Masters"));
        col.Add(new MenuItemViewModel("Create", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Alter Company", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Chart of Accounts", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // ---- STATUTORY (F11 Company Features → Statutory Configuration) ----
        // This one F11 page hosts GST *and* TDS/TCS, Payroll Statutory (PF/ESI/PT) and §192 salary-TDS, gratuity and
        // bonus (its own title is "Statutory Configuration (F11)"). Labelling the entry "GST" hid the salary-TDS
        // enable toggle from anyone not looking under GST (WI-8); "GST & Taxation" signals the tax config lives here.
        col.Add(MenuItemViewModel.Header("Statutory"));
        col.Add(new MenuItemViewModel("GST & Taxation", () => { }, "F11", isSubItem: true, kind: MenuItemKind.Page));
        // GST Rate Setup (dated GST 2.0 rate + cess bulk maintenance; Phase 9 slice 1) — only once GST is enabled.
        if (Company is { GstEnabled: true })
            col.Add(new MenuItemViewModel("GST Rate Setup", () => { }, "Ctrl+R", isSubItem: true, kind: MenuItemKind.Page));

        // ---- TRANSACTIONS ----
        col.Add(MenuItemViewModel.Header("Transactions"));
        col.Add(new MenuItemViewModel("Vouchers", () => { }, "F4–F9  ▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Banking", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Day Book", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // ---- REPORTS ----
        col.Add(MenuItemViewModel.Header("Reports"));
        col.Add(new MenuItemViewModel("Balance Sheet", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Profit & Loss A/c", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Trial Balance", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Account Books", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Statements", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Statements of Accounts", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Inventory Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("GST Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Exception Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // Payroll Reports (Phase 8 slice 8; RQ-16; catalog §14) — the payslip + pay sheet + payroll register +
        // attendance register + payment advice. Surfaced only when the F11 feature "Maintain Payroll" is on (ER-13),
        // so a company that never enables Payroll is byte-identical to the pre-slice Reports menu.
        if (Company is { PayrollEnabled: true })
            col.Add(new MenuItemViewModel("Payroll Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // Statutory Reports (Phase 7 slice 8; catalog §13) — the TDS/TCS exception & outstanding reports and, from
        // Phase 8 slice 4, the Payroll (PF) statutory reports; from Phase 9 slice 3 the Composition Returns (CMP-08 /
        // GSTR-4); from Phase 9 UI-1 the Annual Returns / GST Returns (Advanced) groups. Surfaced only when the F11
        // feature enables TDS, TCS or Payroll Statutory, or the company is a Composition dealer or a Regular GST
        // dealer (ER-13), so a company using none is byte-identical to the pre-slice Reports menu. This group is the
        // ONLY door to the advanced-GST screens, so a plain Regular GST company (GST on, no TDS/TCS/Payroll) must see
        // it — omitting IsRegularGstDealer here made all ten UI-1 screens unreachable through the real cascade.
        if (Company is { TdsEnabled: true } or { TcsEnabled: true } or { PayrollStatutoryEnabled: true }
            || IsCompositionDealer || IsRegularGstDealer)
            col.Add(new MenuItemViewModel("Statutory Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // ---- DATA (the backup/restore carve-out from the otherwise-excluded Phase 10) ----
        // plan.md names backup/restore as the mitigation for its OWN top-ranked data-loss risk (R-7) and then puts
        // it in a phase that is excluded. It is surfaced here as a first-class Gateway section, not a hidden
        // hotkey, because a safety net nobody can find is not a safety net. Only Backup/Restore is carved out —
        // the rest of Phase 10 (security, roles, audit trail, vault) stays excluded.
        col.Add(MenuItemViewModel.Header("Data"));
        col.Add(new MenuItemViewModel("Backup / Restore", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // 🔴 NO "Company" SECTION IS ADDED HERE, AND THAT IS THE FIDELITY ANSWER, NOT AN OMISSION.
        // W2-18 built one (Header "Company" + a Group row drilling to Create / Alter / Select / Shut) and it was
        // REMOVED on 2026-09-05 rather than re-pointed, because the shipped inventory test
        // `GatewayHierarchyTests.Gateway_exposes_the_sections_with_their_items_nested` caught it and the test was
        // RIGHT on two independent authorities:
        //   • RULING 14 / R7 — the vendor's own help puts every company verb on the TOP MENU, not on this screen:
        //     help.tallysolutions.com/…/set-up-company-tally/ reads "press Alt+K (Company) > Create" and
        //     "press Alt+K (Company) > Alter", and …/company-faq-tally/ gives Alt+F3 (Select Company). A Gateway
        //     SECTION is not where the reference product keeps them.
        //   • `docs/invented-vs-cloned.md` IV-29 states the reference Gateway verbatim — Masters · Transactions ·
        //     Utilities · Reports — and its †† 2026-08-17 block records that W0-2b added exactly this "Company"
        //     section once already and it was corrected out, with the diagnosis that this menu's standing fault
        //     is having GROWN A SECTION PER PHASE. Re-adding it lower down repeats the move it names.
        // Census row 14.9 therefore stays OPEN. What it actually needs is the Alt+K top-menu shell, and the chord
        // is inside open user ruling U-6 — a build agent must not assign it. See the finish-b4 artefact.

        // ---- top-level action: change company ----
        col.Add(new MenuItemViewModel("Quit — Change Company", ShowCompanySelect, "F3", kind: MenuItemKind.Action));

        return col;
    }

    /// <summary>
    /// Builds the "Vouchers" submenu column (Transactions → Vouchers): the eight accounting voucher types —
    /// Contra/Payment/Receipt/Journal/Sales/Purchase under F4–F9, then Credit Note (Alt+F6) and Debit Note
    /// (Alt+F5) — each a page item under its key.
    /// </summary>
    private GatewayColumn BuildVouchersColumn()
    {
        var col = new GatewayColumn("Vouchers");
        col.Add(MenuItemViewModel.Header("Vouchers"));
        col.Add(new MenuItemViewModel("Contra", () => { }, "F4", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Payment", () => { }, "F5", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Receipt", () => { }, "F6", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Journal", () => { }, "F7", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Sales", () => { }, "F8", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Purchase", () => { }, "F9", isSubItem: true, kind: MenuItemKind.Page));
        // Credit Note / Debit Note (Alt+F6 / Alt+F5). Two of the predefined types had NO menu row anywhere in the
        // app — reachable only by their accelerator or the Day-Book Alt+A picker, so an operator who did not
        // already know the key could not find them at all. They belong here beside the sales they reverse (Book
        // p.24 lists Credit Note at #11 and Debit Note at #12), nested under this same VOUCHERS header rather
        // than buried under "Other Vouchers" with the provisional kinds — they are ordinary weekly accounting
        // vouchers (decision D9 option A). The hints are TallyPrime's keys, and the keys this app already binds,
        // so neither row can advertise a dead key.
        col.Add(new MenuItemViewModel("Credit Note", () => { }, "Alt+F6", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Debit Note", () => { }, "Alt+F5", isSubItem: true, kind: MenuItemKind.Page));

        // Inventory (stock/order) voucher kinds under their own groups (professional hierarchy):
        // Order Vouchers [PO, SO]; Inventory Vouchers [GRN, Delivery, Rejection In/Out, Stock Journal, Physical Stock].
        col.Add(MenuItemViewModel.Header("Inventory"));
        col.Add(new MenuItemViewModel("Order Vouchers", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Inventory Vouchers", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // Provisional (off-books) voucher kinds under their own group (Reversing Journal / Memorandum).
        col.Add(MenuItemViewModel.Header("Other Vouchers"));
        col.Add(new MenuItemViewModel("Other Vouchers", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // TDS / TCS Stat Payment (Phase 7 slices 3 & 6; catalog §13) — the Payment "Ctrl+F" deposit of the accrued
        // TDS/TCS Payable liability. Each entry is surfaced only when its F11 feature is on, so a company that enables
        // neither is byte-identical (ER-13). The "Statutory" header appears once when either tax is enabled.
        if (Company is { TdsEnabled: true } or { TcsEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("Statutory"));
            if (Company is { TdsEnabled: true })
                col.Add(new MenuItemViewModel("TDS Stat Payment", () => { }, "Ctrl+F", isSubItem: true, kind: MenuItemKind.Page));
            // The TCS deposit deposits collected TCS; its in-screen deposit action is Ctrl+A (no global open accelerator
            // is advertised, so Ctrl+F stays unambiguously the TDS deposit even when both taxes are on — no dead key).
            if (Company is { TcsEnabled: true })
                col.Add(new MenuItemViewModel("TCS Stat Payment", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }

        // Payroll vouchers (Phase 8 slice 3; RQ-6/RQ-7) — the Attendance / Production voucher (records attendance
        // values, non-accounting) and the Payroll voucher (Ctrl+F4, posts the balanced integrated salary entry),
        // surfaced under their own nested section only when the F11 feature "Maintain Payroll" is on. A company that
        // never enables Payroll shows neither and is byte-identical (ER-13), so the whole header hides when off.
        if (Company is { PayrollEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("Payroll"));
            col.Add(new MenuItemViewModel("Attendance / Production", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Payroll", () => { }, "Ctrl+F4", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>
    /// Builds the "Order Vouchers" submenu column (Transactions → Vouchers → Order Vouchers): the two order
    /// kinds — <b>Purchase Order</b> (Ctrl+F9) and <b>Sales Order</b> (Ctrl+F8) — each a page item. Orders
    /// carry ordered-item lines only and post no stock/accounting effect (an outstanding commitment).
    /// </summary>
    private GatewayColumn BuildOrderVouchersColumn()
    {
        var col = new GatewayColumn("Order Vouchers");
        col.Add(MenuItemViewModel.Header("Order Vouchers"));
        col.Add(new MenuItemViewModel("Purchase Order", () => { }, "Ctrl+F9", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Sales Order", () => { }, "Ctrl+F8", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Inventory Vouchers" submenu column (Transactions → Vouchers → Inventory Vouchers): the six
    /// stock-moving kinds — <b>Receipt Note (GRN)</b> (Alt+F9), <b>Delivery Note</b> (Alt+F8),
    /// <b>Rejection In</b> (Ctrl+F6), <b>Rejection Out</b> (Ctrl+F5), <b>Stock Journal</b> (Alt+F7) and
    /// <b>Physical Stock</b> (Ctrl+F7) — each a page item. They move stock only (no accounting entry, DP-5).
    /// </summary>
    private GatewayColumn BuildInventoryVouchersColumn()
    {
        var col = new GatewayColumn("Inventory Vouchers");
        col.Add(MenuItemViewModel.Header("Inventory Vouchers"));
        col.Add(new MenuItemViewModel("Receipt Note", () => { }, "Alt+F9", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Delivery Note", () => { }, "Alt+F8", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Rejection In", () => { }, "Ctrl+F6", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Rejection Out", () => { }, "Ctrl+F5", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Stock Journal", () => { }, "Alt+F7", isSubItem: true, kind: MenuItemKind.Page));
        // Physical Stock is Ctrl+F7 (TallyPrime's official key). This row printed "F10", which in this app opens
        // the Other Vouchers menu — an advertised key that did something else, while Ctrl+F7 was bound to nothing.
        col.Add(new MenuItemViewModel("Physical Stock", () => { }, "Ctrl+F7", isSubItem: true, kind: MenuItemKind.Page));
        // Manufacturing Journal (Phase 6 Cluster 2; RQ-11/RQ-53) — a Stock-Journal-derived type reached under
        // Inventory Vouchers via Alt+F7 (the manufacturing shortcut), surfaced only when the F12 config
        // "Set Components (BOM)" is on (RQ-10/RQ-52), so a non-BOM company is unaffected.
        if (Company is { SetComponentsBom: true })
            col.Add(new MenuItemViewModel("Manufacturing Journal", () => { }, "Alt+F7", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Opens the "Order Vouchers" submenu column directly (Transactions → Vouchers → Order Vouchers) — the
    /// public entry the Ctrl+F8/F9 hotkeys / tests use. Rebuilds the cascade to [root → Vouchers → Order
    /// Vouchers] and focuses it.
    /// </summary>
    public void ShowOrderVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowVouchersMenu();
        SelectVouchersChild("Order Vouchers");
        OpenSubmenuColumn(BuildOrderVouchersColumn(), GatewayMenu.OrderVouchers,
            "Gateway of Apex Solutions — Order Vouchers");
    }

    /// <summary>
    /// Opens the "Inventory Vouchers" submenu column directly (Transactions → Vouchers → Inventory Vouchers) —
    /// the public entry the Alt+F7/8/9 + Ctrl+F5/6 hotkeys / tests use. Rebuilds the cascade to
    /// [root → Vouchers → Inventory Vouchers] and focuses it.
    /// </summary>
    public void ShowInventoryVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowVouchersMenu();
        SelectVouchersChild("Inventory Vouchers");
        OpenSubmenuColumn(BuildInventoryVouchersColumn(), GatewayMenu.InventoryVouchers,
            "Gateway of Apex Solutions — Inventory Vouchers");
    }

    /// <summary>Highlights a named child of the (rightmost) Vouchers submenu column before drilling into it.</summary>
    private void SelectVouchersChild(string label)
    {
        var vouchers = Columns[^1];
        for (var i = 0; i < vouchers.Items.Count; i++)
            if (vouchers.Items[i].IsSelectable && vouchers.Items[i].Label == label)
            {
                vouchers.SetSelected(i);
                break;
            }
    }

    /// <summary>
    /// Builds the "Other Vouchers" submenu column (Transactions → Vouchers → Other Vouchers): the two
    /// provisional voucher kinds — <b>Reversing Journal</b> (carries an Applicable-Upto date) and
    /// <b>Memorandum</b> (a non-affecting suspense entry) — each a page item under this group.
    /// </summary>
    private GatewayColumn BuildOtherVouchersColumn()
    {
        var col = new GatewayColumn("Other Vouchers");
        col.Add(MenuItemViewModel.Header("Other Vouchers"));
        col.Add(new MenuItemViewModel("Reversing Journal", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Memorandum", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // POS Billing (Phase 6 slice 7; RQ-38..RQ-44): a Sales item-invoice with a tender split, posted through a
        // user-created POS-flagged Sales type (auto-created on first use, mirroring the Manufacturing Journal).
        col.Add(new MenuItemViewModel("POS Billing", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Job Work vouchers (Phase 6 slice 8; RQ-45/RQ-47/RQ-48/RQ-54) — the four seeded types reached under F10
        // Other Vouchers, surfaced only when the F11 feature "Enable Job Order Processing" is on (RQ-52), so a
        // company that never enables it is byte-identical (ER-13).
        if (Company is { EnableJobOrderProcessing: true })
        {
            col.Add(MenuItemViewModel.Header("Job Work"));
            col.Add(new MenuItemViewModel("Job Work In Order", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Job Work Out Order", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Material In", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Material Out", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>
    /// Builds the "Banking" submenu column (Transactions → Banking): the Bank Reconciliation and Import
    /// Bank Statement pages, each a page item under this Banking group (professional hierarchy).
    /// </summary>
    private GatewayColumn BuildBankingColumn()
    {
        var col = new GatewayColumn("Banking");
        col.Add(MenuItemViewModel.Header("Banking"));
        col.Add(new MenuItemViewModel("Bank Reconciliation", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Import Bank Statement", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Backup / Restore" submenu column (Data → Backup / Restore): the two data-safety pages. A backup
    /// is a version-stamped snapshot of the company DATABASE taken through the SQLite Online Backup API — a
    /// different thing from Export Data (which serialises the aggregate to JSON/XML for interchange), and the two
    /// are deliberately not conflated in the menu.
    /// </summary>
    private GatewayColumn BuildDataColumn()
    {
        var col = new GatewayColumn("Backup / Restore");
        col.Add(MenuItemViewModel.Header("Backup / Restore"));
        col.Add(new MenuItemViewModel("Backup Company", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Restore Company", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Builds the "Create" submenu column (Masters → Create): the master-creation entries,
    /// nested under an Accounting section and a Cost section (professional hierarchy).</summary>
    private GatewayColumn BuildCreateColumn()
    {
        var col = new GatewayColumn("Create");
        col.Add(MenuItemViewModel.Header("Accounting Masters"));
        col.Add(new MenuItemViewModel("Ledger", () => { }, "Alt+C", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Group", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Cost Masters"));
        col.Add(new MenuItemViewModel("Cost Category", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Cost Centre", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Inventory Masters"));
        col.Add(new MenuItemViewModel("Stock Group", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Stock Category", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Unit", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Godown", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Stock Item", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Reorder Levels master (Phase 6 slice 6; RQ-32/RQ-54) — a core inventory master (per item / group /
        // category), always available; a company with no definitions falls back to the legacy per-item fields so
        // the Reorder-Status report is unchanged (ER-13).
        col.Add(new MenuItemViewModel("Reorder Levels", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Batch / Lot master (Phase 6 Cluster 1; RQ-1/RQ-54) — surfaced only when the company flag
        // "Maintain Batch-wise details" is on (RQ-52), so a non-batch company is unaffected.
        if (Company is { MaintainBatchwiseDetails: true })
            col.Add(new MenuItemViewModel("Batch", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Bill of Materials master (Phase 6 Cluster 2; RQ-9/RQ-54) — surfaced only when the F12 config
        // "Set Components (BOM)" is on (RQ-10/RQ-52), so a non-BOM company is unaffected.
        if (Company is { SetComponentsBom: true })
            col.Add(new MenuItemViewModel("Bill of Materials", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Price Level / Price List masters (Phase 6 slice 5; RQ-26/RQ-27/RQ-54) — surfaced only when the F11
        // flag "Enable multiple Price Levels" is on (RQ-52), so a non-price-level company is unaffected.
        if (Company is { EnableMultiplePriceLevels: true })
        {
            col.Add(new MenuItemViewModel("Price Level", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Price List", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }

        col.Add(MenuItemViewModel.Header("Budgets & Controls"));
        col.Add(new MenuItemViewModel("Budget", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Scenario", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Multi-Currency"));
        col.Add(new MenuItemViewModel("Currency", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // Statutory Masters (Phase 7 slice 1; TDS/TCS) — the Nature-of-Payment (TDS section) master surfaces only
        // when the F11 feature "Enable TDS" is on; Nature-of-Goods (§206C) only when "Enable TCS" is on. A company
        // with neither is byte-identical (ER-13), so the whole header hides when both are off.
        if (Company is { TdsEnabled: true } or { TcsEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("Statutory Masters"));
            if (Company is { TdsEnabled: true })
                col.Add(new MenuItemViewModel("Nature of Payment", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            if (Company is { TcsEnabled: true })
                col.Add(new MenuItemViewModel("Nature of Goods", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }

        // Payroll Masters (Phase 8 slice 1; RQ-2/RQ-3) — the employee / payroll-unit / attendance-type masters,
        // surfaced under their own nested section only when the F11 feature "Maintain Payroll" is on. A company
        // that never enables Payroll carries no payroll masters and is byte-identical (ER-13), so the whole header
        // hides when the flag is off.
        if (Company is { PayrollEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("Payroll Masters"));
            col.Add(new MenuItemViewModel("Employee Category", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Employee Group", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Employee", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Payroll Unit", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Attendance / Production Type", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            // Phase 8 slice 2 (RQ-4/RQ-5): Pay Head + Salary Details, the heart of the salary structure.
            col.Add(new MenuItemViewModel("Pay Head", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Salary Details", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            // Phase 8 slice 7 (RQ-12): the per-employee income-tax declaration (Form 12BB), surfaced only when the
            // F11 feature "Enable Salary TDS" is on (ER-13) — its figures drive the §192 salary-TDS estimate.
            if (Company is { SalaryTdsEnabled: true })
                col.Add(new MenuItemViewModel("Income Tax Declaration", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>
    /// Builds the "Statements of Accounts" hub submenu column (Reports → Statements of Accounts): the two
    /// statement groups — <b>Outstandings</b> (Receivables/Payables) and <b>Cost Centres</b> (Category
    /// Summary / Cost Centre Break-up) — each a Group item drilling into its own submenu column.
    /// </summary>
    private GatewayColumn BuildStatementsOfAccountsColumn()
    {
        var col = new GatewayColumn("Statements of Accounts");
        col.Add(MenuItemViewModel.Header("Statements of Accounts"));
        col.Add(new MenuItemViewModel("Outstandings", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Cost Centres", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Budgets", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Interest Calculation", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Forex Gain/Loss", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // W2-12 (census 11.8): Statistics — the counts of vouchers entered and masters created. The vendor
        // places it under Statement of Accounts, which is this hub.
        col.Add(new MenuItemViewModel("Statistics", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Outstandings" submenu column (Reports → Statements of Accounts → Outstandings): the
    /// Receivables and Payables pages, each a page item under this Outstandings group.
    /// </summary>
    private GatewayColumn BuildOutstandingsColumn()
    {
        var col = new GatewayColumn("Outstandings");
        col.Add(MenuItemViewModel.Header("Outstandings"));
        col.Add(new MenuItemViewModel("Receivables", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Payables", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Cost Centres" submenu column (Reports → Statements of Accounts → Cost Centres): the
    /// Category Summary and Cost Centre Break-up report pages, each a page item under this group.
    /// </summary>
    private GatewayColumn BuildCostCentresColumn()
    {
        var col = new GatewayColumn("Cost Centres");
        col.Add(MenuItemViewModel.Header("Cost Centres"));
        col.Add(new MenuItemViewModel("Category Summary", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Cost Centre Break-up", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Budgets" submenu column (Reports → Statements of Accounts → Budgets): the Budget
    /// Variance report page (Budget vs Actual vs Variance), a page item under this Budgets group.
    /// </summary>
    private GatewayColumn BuildBudgetsColumn()
    {
        var col = new GatewayColumn("Budgets");
        col.Add(MenuItemViewModel.Header("Budgets"));
        col.Add(new MenuItemViewModel("Budget Variance", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Inventory Reports" submenu column (Reports → Inventory Reports): the nine Phase-3 stock
    /// reports nested under three sub-sections (professional hierarchy, never flat) — <b>Stock</b> (Stock
    /// Summary, Godown Summary, Stock Movement), <b>Analysis</b> (Reorder Status) and <b>Registers</b> (Receipt
    /// Note, Delivery Note, Rejection, Physical Stock, Order). Each is a page item reusing
    /// <see cref="Screen.Report"/> + <see cref="OpenReport(ReportKind)"/>.
    /// </summary>
    private GatewayColumn BuildInventoryReportsColumn()
    {
        var col = new GatewayColumn("Inventory Reports");
        col.Add(MenuItemViewModel.Header("Stock"));
        col.Add(new MenuItemViewModel("Stock Summary", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Godown Summary", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Stock Movement", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Analysis"));
        col.Add(new MenuItemViewModel("Reorder Status", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Batch reports (Phase 6 Cluster 1; RQ-8/RQ-54) nest under a Batch sub-group — surfaced only when the
        // company flag "Maintain Batch-wise details" is on (RQ-52).
        if (Company is { MaintainBatchwiseDetails: true })
            col.Add(new MenuItemViewModel("Batch", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // Price List report (Phase 6 slice 5; RQ-31/RQ-54) nests beside the analysis reports — surfaced only when
        // the F11 flag "Enable multiple Price Levels" is on (RQ-52), so a non-price-level company is unaffected.
        if (Company is { EnableMultiplePriceLevels: true })
            col.Add(new MenuItemViewModel("Price List", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Registers"));
        col.Add(new MenuItemViewModel("Receipt Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Delivery Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Rejection Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Physical Stock Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Order Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // POS Register (Phase 6 slice 7; RQ-44): the day-close tender view of POS bills — surfaced only when a
        // POS-flagged Sales type exists (mirrors the batch/price-list conditional surfacing).
        if (Company is { } c && c.VoucherTypes.Any(t => t.IsPosSales))
            col.Add(new MenuItemViewModel("POS Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // Job Work reports (Phase 6 slice 8; RQ-51/RQ-54) nest under their own sub-section — surfaced only when the
        // F11 feature "Enable Job Order Processing" is on (RQ-52), so a non-job-work company is byte-identical (ER-13).
        if (Company is { EnableJobOrderProcessing: true })
        {
            col.Add(MenuItemViewModel.Header("Job Work Reports"));
            col.Add(new MenuItemViewModel("Job Work In Order Book", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Job Work Out Order Book", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Material In Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Material Out Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>
    /// Opens the "Reports → Inventory Reports" submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → Inventory Reports] and focuses the submenu.
    /// </summary>
    public void ShowInventoryReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Inventory Reports");
        OpenSubmenuColumn(BuildInventoryReportsColumn(), GatewayMenu.InventoryReports,
            "Gateway of Apex Solutions — Inventory Reports");
    }

    /// <summary>
    /// Builds the "Batch" submenu column (Reports → Inventory Reports → Batch; Phase 6 Cluster 1; RQ-8/RQ-54):
    /// the two batch reports nested under a single <b>Batch</b> section — <b>Batch-wise</b> (per item/batch
    /// inwards/outwards/closing with mfg &amp; expiry) and <b>Age Analysis</b> (batches expiring within N days,
    /// past-expiry flagged distinctly). Each is a page item reusing <see cref="Screen.Report"/> +
    /// <see cref="OpenReport(ReportKind, Guid?)"/>.
    /// </summary>
    private GatewayColumn BuildInventoryBatchReportsColumn()
    {
        var col = new GatewayColumn("Batch");
        col.Add(MenuItemViewModel.Header("Batch"));
        col.Add(new MenuItemViewModel("Batch-wise", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Age Analysis", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Opens the "Reports → Inventory Reports → Batch" submenu column directly (the public entry a hotkey/test
    /// uses). Rebuilds the cascade to [root → Inventory Reports → Batch] and focuses the submenu.
    /// </summary>
    public void ShowInventoryBatchReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowInventoryReportsMenu();
        SelectSubmenuItem("Batch");
        OpenSubmenuColumn(BuildInventoryBatchReportsColumn(), GatewayMenu.InventoryBatchReports,
            "Gateway of Apex Solutions — Batch Reports");
    }

    /// <summary>
    /// Builds the "GST Reports" submenu column (Reports → GST Reports; slice 4d): the three Phase-4 GST returns
    /// nested under a single <b>GST</b> section — <b>Tax Analysis</b> (period tax by rate/head), <b>GSTR-1</b>
    /// (outward supplies: B2B/B2C, rate-wise, HSN) and <b>GSTR-3B</b> (summary: outward, ITC, net payable). Each
    /// is a page item reusing <see cref="Screen.Report"/> + <see cref="OpenReport(ReportKind)"/>. Shown whether
    /// or not GST is enabled; a GST-off company opens the report to a friendly empty state (never crashes).
    /// </summary>
    /// <summary>
    /// The FY-gated <b>"Form NNN"</b> menu label (CA S9) — the 1961-Act number for FY 2025-26 and earlier, the
    /// confirmed 2025-Act number from FY 2026-27 onward. A form with <b>no confirmed renumbering</b> (e.g. 27A) falls
    /// through unchanged, which is what keeps unverified artifacts from being silently re-cited.
    /// <para>When <b>no company — and therefore no financial year — is in scope</b> the <b>dual</b> form
    /// ("Form 24Q / 138") is shown rather than guessing a vocabulary. Every current caller is company-gated, so the
    /// dual branch is a safety net rather than the normal path.</para>
    /// <para><b>Keep <see cref="ActivateMenuItem"/> in step:</b> menu activation dispatches on this label string, so
    /// each renumbered label needs its own case there or the item becomes unreachable.</para>
    /// </summary>
    private string FormMenuLabel(string legacyForm) => Company is { } company
        ? $"Form {StatuteVocabulary.FormLabel(legacyForm, company.FinancialYearStart.Year)}"
        : $"Form {StatuteVocabulary.FormLabelDual(legacyForm)}";

    private GatewayColumn BuildGstReportsColumn()
    {
        var col = new GatewayColumn("GST Reports");
        col.Add(MenuItemViewModel.Header("GST"));
        col.Add(new MenuItemViewModel("Tax Analysis", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GSTR-1", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GSTR-3B", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // Challan Reconciliation (Phase 7 slice 3; catalog §13) — deposits vs deductions per section. Surfaced
        // under its own TDS header only when the F11 feature "Enable TDS" is on (ER-13), reached by Alt+R too.
        if (Company is { TdsEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("TDS"));
            col.Add(new MenuItemViewModel("Challan Reconciliation", () => { }, "Alt+R", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel(FormMenuLabel("26Q"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel(FormMenuLabel("16A"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            // Form 27A carries NO confirmed 2025-Act renumbering, so FormMenuLabel deliberately leaves it alone.
            col.Add(new MenuItemViewModel("Form 27A (TDS)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }

        // TCS Challan Reconciliation + Form 27EQ (Phase 7 slice 6; catalog §13) — the collector's mirror of the TDS
        // pair. Surfaced under their own TCS header only when the F11 feature "Enable TCS" is on (ER-13). No global
        // open accelerator (Alt+R stays the TDS recon even when both taxes are on — no colliding/dead key).
        if (Company is { TcsEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("TCS"));
            col.Add(new MenuItemViewModel("TCS Challan Reconciliation", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel(FormMenuLabel("27EQ"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel(FormMenuLabel("27D"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Form 27A (TCS)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>
    /// Builds the "Statements" submenu column (Reports → Statements; RQ-5 part 1): the three financial-analysis
    /// statements nested under a single <b>Financial Statements</b> section — <b>Cash Flow</b> (cash &amp; bank
    /// inflows/outflows reconciling opening to closing), <b>Funds Flow</b> (sources &amp; applications of funds)
    /// and <b>Ratio Analysis</b> (the standard accounting ratios). Each is a page item reusing
    /// <see cref="Screen.Report"/> + <see cref="OpenReport(ReportKind)"/>; all three honour the F2/Alt+F2 period.
    /// </summary>
    private GatewayColumn BuildStatementsColumn()
    {
        var col = new GatewayColumn("Statements");
        col.Add(MenuItemViewModel.Header("Financial Statements"));
        col.Add(new MenuItemViewModel("Cash Flow", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Funds Flow", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Ratio Analysis", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Opens the "Reports → Statements" submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → Statements] and focuses the submenu.
    /// </summary>
    public void ShowStatementsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statements");
        OpenSubmenuColumn(BuildStatementsColumn(), GatewayMenu.Statements,
            "Gateway of Apex Solutions — Statements");
    }

    // =============================================================== Account Books (catalog §16 / RQ-30)

    /// <summary>
    /// Builds the "Account Books" hub submenu column (Reports → Account Books; catalog §16 / RQ-30): the three
    /// core books — <b>Cash Book</b>, <b>Bank Book</b> and <b>Ledger</b> — each a Group drilling into a picker
    /// of the relevant ledgers. Each picked ledger opens that ledger's
    /// <see cref="Apex.Ledger.Reports.LedgerBook"/> via the existing RQ-7 drill (<see cref="OpenLedgerVouchers"/>) —
    /// a pure reuse of an existing projection, no new engine report. Cash Book / Bank Book are the Ledger book
    /// filtered to a Cash-in-Hand / Bank ledger (<see cref="Apex.Ledger.Reports.ClassificationRules"/>). The
    /// per-voucher registers (Sales / Purchase / …) are the <b>Registers</b> section below.
    ///
    /// <para><b>W2-12 (census 11.6 / 11.7)</b> added two further sections, so the column nests under three
    /// named headers and is never a flat dump. 🔴 The <b>Registers</b> rows are NOT the Day Book filtered by
    /// voucher type — that note used to stand here and was wrong. A register opens <b>month-wise</b> and the
    /// voucher-wise listing is what a month row drills into; see
    /// <see cref="Apex.Ledger.Reports.VoucherRegister"/>.</para>
    /// </summary>
    private GatewayColumn BuildAccountBooksColumn()
    {
        var col = new GatewayColumn("Account Books");
        col.Add(MenuItemViewModel.Header("Account Books"));
        col.Add(new MenuItemViewModel("Cash Book", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Bank Book", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Ledger", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // ---- REGISTERS (census 11.6) ----
        col.Add(MenuItemViewModel.Header("Registers"));
        col.Add(new MenuItemViewModel("Sales Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Purchase Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Journal Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Credit Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Debit Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // ---- GROUPS (census 11.7) — each drills into a picker of the company's own groups. ----
        col.Add(MenuItemViewModel.Header("Groups"));
        col.Add(new MenuItemViewModel("Group Summary", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Group Vouchers", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        return col;
    }

    /// <summary>
    /// Builds a group-picker submenu column for a W2-12 group report (Group Summary / Group Vouchers): one
    /// page item per accounting group, name-sorted. Activating a group opens that report scoped to it.
    /// Data-driven like the ledger-book pickers, so a bare letter filters rather than activating.
    /// </summary>
    private GatewayColumn BuildGroupPickerColumn(string title)
    {
        var col = new GatewayColumn(title) { Kind = GatewayColumnKind.DataDriven };
        col.Add(MenuItemViewModel.Header(title));

        var groups = Company is null
            ? System.Array.Empty<Apex.Ledger.Domain.Group>()
            : Company.Groups.OrderBy(g => g.Name, System.StringComparer.OrdinalIgnoreCase).ToArray();

        if (groups.Length == 0)
            col.Add(MenuItemViewModel.Header("(no groups)"));
        else
            foreach (var group in groups)
                col.Add(new MenuItemViewModel(group.Name, () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        return col;
    }

    /// <summary>Opens "Reports → Account Books → Group Summary" (the group picker) directly.</summary>
    public void ShowGroupSummaryMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Account Books");
        OpenSubmenuColumn(BuildGroupPickerColumn("Group Summary"), GatewayMenu.GroupSummaryPicker,
            "Gateway of Apex Solutions — Group Summary");
    }

    /// <summary>Opens "Reports → Account Books → Group Vouchers" (the group picker) directly.</summary>
    public void ShowGroupVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Account Books");
        OpenSubmenuColumn(BuildGroupPickerColumn("Group Vouchers"), GatewayMenu.GroupVouchersPicker,
            "Gateway of Apex Solutions — Group Vouchers");
    }

    /// <summary>
    /// Opens a group-scoped W2-12 report for the group with <paramref name="groupName"/> (the label of a row
    /// in the group picker). A safe no-op when the name does not resolve.
    /// </summary>
    public void OpenGroupReportByName(ReportKind kind, string groupName)
    {
        var group = Company?.FindGroupByName(groupName);
        if (group is null) return;
        OpenGroupReport(kind, group.Id);
    }

    /// <summary>
    /// Opens the "Reports → Account Books" hub submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → Account Books] and focuses the hub.
    /// </summary>
    public void ShowAccountBooksMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Account Books");
        OpenSubmenuColumn(BuildAccountBooksColumn(), GatewayMenu.AccountBooks,
            "Gateway of Apex Solutions — Account Books");
    }

    /// <summary>
    /// Builds a ledger-picker submenu column for an Account Book: one page item per ledger matching
    /// <paramref name="include"/> (all ledgers for Ledger, cash-only for Cash Book, bank-only for Bank Book),
    /// name-sorted. Activating a ledger opens its <see cref="Apex.Ledger.Reports.LedgerBook"/> over the books
    /// period via <see cref="OpenLedgerVouchers"/>. An empty match shows a single non-selectable note.
    /// </summary>
    private GatewayColumn BuildLedgerBookPickerColumn(string title, Func<Apex.Ledger.Domain.Ledger, bool> include)
    {
        // WI-2/WI-9 conflict rule: this column's rows are the COMPANY'S ledgers, not authored menu options, so a
        // bare letter must FILTER it (type-ahead) rather than activate a computed hotkey. Marking the kind is
        // what routes the keystroke; see GatewayColumnKind.
        var col = new GatewayColumn(title) { Kind = GatewayColumnKind.DataDriven };
        col.Add(MenuItemViewModel.Header(title));

        var ledgers = Company is null
            ? System.Array.Empty<Apex.Ledger.Domain.Ledger>()
            : Company.Ledgers.Where(include)
                .OrderBy(l => l.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (ledgers.Length == 0)
            col.Add(MenuItemViewModel.Header("(no matching ledgers)"));
        else
            foreach (var ledger in ledgers)
                col.Add(new MenuItemViewModel(ledger.Name, () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // WI-1 — THE CORPUS'S SECOND ENTRY POINT: "Alt+C … in place of the Ledger field OR select Create option
        // under List of Ledger Accounts" (Study Guide ~2046–47). Only the key half shipped; this is the list
        // half. The row is PINNED at the end of the real ledgers, arrow-reachable and Enter-activated, and runs
        // the SAME CreateLedgerShortcut dispatch as the key — one mechanism, two entry points, rather than a
        // parallel path that could drift. It is flagged IsCreateRow so type-ahead SKIPS it: a bare "c" must
        // filter to the ledger named "Cash", never land the highlight on "Create Ledger".
        col.Add(new MenuItemViewModel("Create Ledger",
            () => CreateLedgerShortcut(MasterCreateFields.Ledger, caller: null),
            "Alt+C", isSubItem: true, kind: MenuItemKind.Action)
        { IsCreateRow = true });

        return col;
    }

    /// <summary>Opens the "Account Books → Cash Book" ledger picker (Cash-in-Hand ledgers only).</summary>
    public void ShowCashBookMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowAccountBooksMenu();
        SelectSubmenuItem("Cash Book");
        OpenSubmenuColumn(
            BuildLedgerBookPickerColumn("Cash Book",
                l => Apex.Ledger.Reports.ClassificationRules.IsCashLedger(l, Company)),
            GatewayMenu.CashBook, "Gateway of Apex Solutions — Cash Book");
    }

    /// <summary>Opens the "Account Books → Bank Book" ledger picker (Bank-Accounts / Bank-OD ledgers only).</summary>
    public void ShowBankBookMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowAccountBooksMenu();
        SelectSubmenuItem("Bank Book");
        OpenSubmenuColumn(
            BuildLedgerBookPickerColumn("Bank Book",
                l => Apex.Ledger.Reports.ClassificationRules.IsBankLedger(l, Company)),
            GatewayMenu.BankBook, "Gateway of Apex Solutions — Bank Book");
    }

    /// <summary>Opens the "Account Books → Ledger" picker (every ledger — the classic Ledger book).</summary>
    public void ShowLedgerBooksMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowAccountBooksMenu();
        SelectSubmenuItem("Ledger");
        OpenSubmenuColumn(
            BuildLedgerBookPickerColumn("Ledger", _ => true),
            GatewayMenu.LedgerBooks, "Gateway of Apex Solutions — Ledger");
    }

    /// <summary>
    /// Opens a ledger's Account-Book (its <see cref="Apex.Ledger.Reports.LedgerBook"/>) by ledger NAME — the
    /// action an Account-Books picker row triggers. Resolves the name to its ledger and drills to the book over
    /// the books period (books-begin → default as-of), reusing <see cref="OpenLedgerVouchers"/>. A safe no-op on
    /// an unknown name.
    /// </summary>
    public void OpenAccountBook(string ledgerName)
    {
        if (Company is null || string.IsNullOrWhiteSpace(ledgerName)) return;
        var ledger = Company.Ledgers.FirstOrDefault(
            l => string.Equals(l.Name, ledgerName, System.StringComparison.OrdinalIgnoreCase));
        if (ledger is null) return;

        var from = Company.BooksBeginFrom;
        var to = AccountBooksAsOf();
        OpenLedgerVouchers(ledger.Id, from, to);
    }

    /// <summary>The as-of upper bound an Account Book covers: the last voucher date, or the financial-year end
    /// when the company has no vouchers (matching the report default; no clock).</summary>
    private DateOnly AccountBooksAsOf()
    {
        DateOnly? last = null;
        foreach (var v in Company!.Vouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        return last ?? Company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    /// <summary>Highlights the named item in the rightmost (just-opened) submenu column, if present, so the
    /// drilled child column reads as its child (mirrors the Other-Vouchers drill helper).</summary>
    private void SelectSubmenuItem(string label)
    {
        if (Columns.Count == 0) return;
        var col = Columns[^1];
        for (var i = 0; i < col.Items.Count; i++)
            if (col.Items[i].IsSelectable && col.Items[i].Label == label)
            {
                col.SetSelected(i);
                return;
            }
    }

    /// <summary>
    /// Builds the "Exception Reports" submenu column (Reports → Exception Reports; RQ-5 part 2): the four
    /// exception surfacers nested under a single <b>Exception Reports</b> section — <b>Negative Stock</b>
    /// (items with a negative on-hand quantity), <b>Negative Cash / Bank</b> (cash/bank ledgers that have
    /// gone credit / overdrawn), the <b>Memorandum Register</b> (non-accounting memo vouchers) and the
    /// <b>Reversing Journal Register</b> (reversing journals with their applicable-upto date). Each is a page
    /// item reusing <see cref="Screen.Report"/> + <see cref="OpenReport(ReportKind, Guid?)"/>; Negative Stock
    /// and Negative Cash / Bank honour the F2 as-of, the two registers honour the F2/Alt+F2 period.
    /// </summary>
    private GatewayColumn BuildExceptionReportsColumn()
    {
        var col = new GatewayColumn("Exception Reports");
        col.Add(MenuItemViewModel.Header("Exception Reports"));
        col.Add(new MenuItemViewModel("Negative Stock", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Negative Cash / Bank", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Memorandum Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Reversing Journal Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Opens the "Reports → Exception Reports" submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → Exception Reports] and focuses the submenu.
    /// </summary>
    public void ShowExceptionReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Exception Reports");
        OpenSubmenuColumn(BuildExceptionReportsColumn(), GatewayMenu.ExceptionReports,
            "Gateway of Apex Solutions — Exception Reports");
    }

    /// <summary>
    /// Opens the "Reports → GST Reports" submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → GST Reports] and focuses the submenu.
    /// </summary>
    public void ShowGstReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("GST Reports");
        OpenSubmenuColumn(BuildGstReportsColumn(), GatewayMenu.GstReports,
            "Gateway of Apex Solutions — GST Reports");
    }

    // =============================================================== Statutory Reports (Phase 7 slice 8)

    /// <summary>
    /// Builds the "Statutory Reports" hub submenu column (Reports → Statutory Reports; Phase 7 slice 8): the
    /// TDS/TCS exception &amp; outstanding reports nested under two <b>Group</b> sub-columns — <b>TDS Reports</b>
    /// (present only when TDS is enabled) and <b>TCS Reports</b> (present only when TCS is enabled) — plus a common
    /// <b>Ledgers without PAN</b> page (R9 spans both taxes). Never a flat dump: the nine reports live two levels
    /// deep under their tax family, matching how Form 26Q / 27EQ / certificates are grouped under GST Reports.
    /// </summary>
    private GatewayColumn BuildStatutoryReportsColumn()
    {
        var col = new GatewayColumn("Statutory Reports");
        col.Add(MenuItemViewModel.Header("Statutory Reports"));
        if (Company is { TdsEnabled: true })
            col.Add(new MenuItemViewModel("TDS Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        if (Company is { TcsEnabled: true })
            col.Add(new MenuItemViewModel("TCS Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        // Payroll statutory reports (Phase 8 slice 4/5; RQ-9/RQ-10) nest under their own Payroll sub-group, surfaced
        // only when the F11 feature "Enable Payroll Statutory" is on (ER-13).
        if (Company is { PayrollStatutoryEnabled: true })
            col.Add(new MenuItemViewModel("Payroll", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        // Composition Returns (Phase 9 slice 3; RQ-16) nest under their own sub-group, surfaced only for a Composition
        // dealer (ER-13). A Regular company never sees CMP-08 / GSTR-4.
        if (IsCompositionDealer)
            col.Add(new MenuItemViewModel("Composition Returns", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        // Advanced-GST report screens (Phase 9 UI-1; RQ-17) nest under two sub-groups, surfaced only for a Regular GST
        // company (ER-13). A Composition / GST-off company never sees them.
        if (IsRegularGstDealer)
        {
            col.Add(new MenuItemViewModel("Annual Returns", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
            col.Add(new MenuItemViewModel("GST Returns (Advanced)", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
            // Phase 9 UI-2: the INTERACTIVE advanced-GST screens (the ones that drive the engine's actions) sit in
            // their own sibling group, so the read-only projections above stay visibly separate from the mutators.
            col.Add(new MenuItemViewModel("GST Actions", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        }
        // R9 Ledgers/Parties without PAN spans both taxes, so it sits at the Statutory-Reports level — but only
        // when a tax is on (a payroll-only company that never enabled TDS/TCS has no PAN report to show).
        if (Company is { TdsEnabled: true } or { TcsEnabled: true })
            col.Add(new MenuItemViewModel("Ledgers without PAN", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Builds the "Payroll" submenu column (Reports → Statutory Reports → Payroll; Phase 8 slice 4/5/6;
    /// RQ-9/RQ-10/RQ-11): the PF ECR / Challan report page (member-wise ECR 2.0 + the A/c 1/2/10/21/22 challan totals),
    /// the ESI Monthly Contribution report page (per-IP EE 0.75% / ER 3.25% + the offline monthly file) and the PT
    /// Deduction Register (per-employee monthly PT + FY cumulative + totals).</summary>
    private GatewayColumn BuildPayrollStatutoryReportsColumn()
    {
        var col = new GatewayColumn("Payroll");
        col.Add(MenuItemViewModel.Header("Payroll Statutory"));
        col.Add(new MenuItemViewModel("PF ECR / Challan", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("ESI Monthly Contribution", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("PT Deduction Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // Gratuity provision + statutory Bonus registers (Phase 8 slice 9; RQ-14/RQ-15) — each surfaced only when the
        // establishment is enrolled for that statute (GratuityConfig / BonusConfig), so a company that uses neither is
        // byte-identical to the pre-slice Payroll submenu (ER-13).
        if (Company is { GratuityConfig: not null })
            col.Add(new MenuItemViewModel("Gratuity Provision", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        if (Company is { BonusConfig: not null })
            col.Add(new MenuItemViewModel("Bonus Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        // §192 salary-TDS return + certificate (Phase 8 slice 7; RQ-13) — surfaced only when the F11 feature
        // "Enable Salary TDS" is on (ER-13), mirroring how the TDS/TCS returns gate on Enable TDS/TCS.
        if (Company is { SalaryTdsEnabled: true })
        {
            col.Add(new MenuItemViewModel(FormMenuLabel("24Q"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel(FormMenuLabel("16"), () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }
        return col;
    }

    /// <summary>Builds the "TDS Reports" submenu column: the four §194x TDS projections (R1–R4).</summary>
    private GatewayColumn BuildTdsReportsColumn()
    {
        var col = new GatewayColumn("TDS Reports");
        col.Add(MenuItemViewModel.Header("TDS Reports"));
        col.Add(new MenuItemViewModel("TDS Outstandings", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TDS Not Deducted", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TDS Interest u/s 201(1A)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TDS Nature of Payment Summary", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Builds the "TCS Reports" submenu column: the four §206C TCS projections (R5–R8).</summary>
    private GatewayColumn BuildTcsReportsColumn()
    {
        var col = new GatewayColumn("TCS Reports");
        col.Add(MenuItemViewModel.Header("TCS Reports"));
        col.Add(new MenuItemViewModel("TCS Outstandings", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TCS Not Collected", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TCS Interest u/s 206C(7)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("TCS Nature of Goods Summary", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>True iff the open company is a GST Composition dealer (drives the Composition Returns surfacing).</summary>
    private bool IsCompositionDealer =>
        Company?.Gst is { Enabled: true, RegistrationType: GstRegistrationType.Composition };

    /// <summary>True iff the open company is a Regular GST dealer (drives the advanced-GST report surfacing; Phase 9
    /// UI-1). A Composition / GST-off company never sees the Annual Returns / GST Returns (Advanced) groups (ER-13).</summary>
    private bool IsRegularGstDealer =>
        Company?.Gst is { Enabled: true, RegistrationType: GstRegistrationType.Regular };

    /// <summary>Builds the "Annual Returns" submenu column (Reports → Statutory Reports → Annual Returns; Phase 9 UI-1;
    /// RQ-17): the two annual GST returns — <b>GSTR-9</b> (annual return) and <b>GSTR-9C</b> (reconciliation statement) —
    /// each a page item projecting the pure Gstr9 / Gstr9c engines.</summary>
    private GatewayColumn BuildAnnualReturnsColumn()
    {
        var col = new GatewayColumn("Annual Returns");
        col.Add(MenuItemViewModel.Header("Annual Returns"));
        col.Add(new MenuItemViewModel("GSTR-9", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GSTR-9C", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Builds the "GST Returns (Advanced)" submenu column (Reports → Statutory Reports → GST Returns
    /// (Advanced); Phase 9 UI-1; RQ-17): the advanced-GST read-only report screens — electronic ledgers, ITC set-off,
    /// ITC reversal, GSTR-2B reconciliation, ITC gate, QRMP / IFF, GSTR-1/3B amendments and e-Invoice / e-Way status —
    /// each a page item projecting the pure engines.</summary>
    private GatewayColumn BuildGstAdvancedReturnsColumn()
    {
        var col = new GatewayColumn("GST Returns (Advanced)");
        col.Add(MenuItemViewModel.Header("GST Returns (Advanced)"));
        col.Add(new MenuItemViewModel("Electronic Ledgers", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("ITC Set-Off", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("ITC Reversal", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GSTR-2B Reconciliation", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("ITC Gate", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("QRMP / IFF", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GST Amendments", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("e-Invoice / e-Way Status", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Opens the "Reports → Statutory Reports → Annual Returns" submenu column directly (the public entry a
    /// hotkey/test uses). A no-op unless the company is a Regular GST dealer.</summary>
    public void ShowAnnualReturnsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        if (!IsRegularGstDealer) return;   // group hidden for a Composition / GST-off company (ER-13)
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("Annual Returns");
        OpenSubmenuColumn(BuildAnnualReturnsColumn(), GatewayMenu.AnnualReturns,
            "Gateway of Apex Solutions — Annual Returns");
    }

    /// <summary>Builds the "GST Actions" submenu column (Reports → Statutory Reports → GST Actions; Phase 9 UI-2;
    /// RQ-17): the advanced-GST <b>interactive</b> screens — the ones that DRIVE the engine's actions rather than
    /// merely project them: the IMS accept/reject/pending dashboard, the Rule-88A set-off + cash discharge, the ITC
    /// reversal poster, the GSTR-2B import, and the offline e-Invoice / e-Way Bill generators. Opening any of them
    /// posts nothing — only an explicit action on the page mutates.</summary>
    private GatewayColumn BuildGstActionsColumn()
    {
        var col = new GatewayColumn("GST Actions");
        col.Add(MenuItemViewModel.Header("GST Actions"));
        col.Add(new MenuItemViewModel("IMS (Accept / Reject / Pending)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Run Set-Off & Pay", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Post ITC Reversal", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Import GSTR-2B", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Generate e-Invoice", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Generate e-Way Bill", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Opens the "Reports → Statutory Reports → GST Actions" submenu column directly (the public entry a
    /// hotkey/test uses). A no-op unless the company is a Regular GST dealer.</summary>
    public void ShowGstActionsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        if (!IsRegularGstDealer) return;   // group hidden for a Composition / GST-off company (ER-13)
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("GST Actions");
        OpenSubmenuColumn(BuildGstActionsColumn(), GatewayMenu.GstActions,
            "Gateway of Apex Solutions — GST Actions");
    }

    /// <summary>Opens the "Reports → Statutory Reports → GST Returns (Advanced)" submenu column directly (the public
    /// entry a hotkey/test uses). A no-op unless the company is a Regular GST dealer.</summary>
    public void ShowGstAdvancedReturnsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        if (!IsRegularGstDealer) return;   // group hidden for a Composition / GST-off company (ER-13)
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("GST Returns (Advanced)");
        OpenSubmenuColumn(BuildGstAdvancedReturnsColumn(), GatewayMenu.GstAdvancedReturns,
            "Gateway of Apex Solutions — GST Returns (Advanced)");
    }

    /// <summary>Builds the "Composition Returns" submenu column (Reports → Statutory Reports → Composition Returns;
    /// Phase 9 slice 3; RQ-16): the two composition GST returns — <b>CMP-08</b> (quarterly self-assessed statement) and
    /// <b>GSTR-4</b> (annual return) — each a page item reusing the pure Cmp08 / Gstr4 engine projections.</summary>
    private GatewayColumn BuildCompositionReturnsColumn()
    {
        var col = new GatewayColumn("Composition Returns");
        col.Add(MenuItemViewModel.Header("Composition Returns"));
        col.Add(new MenuItemViewModel("CMP-08", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("GSTR-4", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Opens the "Reports → Statutory Reports → Composition Returns" submenu column directly (the public entry
    /// a hotkey/test uses). A no-op unless the company is a Composition dealer.</summary>
    public void ShowCompositionReturnsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        if (!IsCompositionDealer) return;   // group hidden for a Regular company (ER-13)
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("Composition Returns");
        OpenSubmenuColumn(BuildCompositionReturnsColumn(), GatewayMenu.CompositionReturns,
            "Gateway of Apex Solutions — Composition Returns");
    }

    /// <summary>Opens the "Reports → Statutory Reports" hub submenu column directly (the public entry a hotkey/test
    /// uses). Rebuilds the cascade to [root → Statutory Reports] and focuses the hub.</summary>
    public void ShowStatutoryReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statutory Reports");
        OpenSubmenuColumn(BuildStatutoryReportsColumn(), GatewayMenu.StatutoryReports,
            "Gateway of Apex Solutions — Statutory Reports");
    }

    /// <summary>Opens the "Reports → Statutory Reports → TDS Reports" submenu column directly.</summary>
    public void ShowTdsReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("TDS Reports");
        OpenSubmenuColumn(BuildTdsReportsColumn(), GatewayMenu.TdsReports,
            "Gateway of Apex Solutions — TDS Reports");
    }

    /// <summary>Opens the "Reports → Statutory Reports → TCS Reports" submenu column directly.</summary>
    public void ShowTcsReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("TCS Reports");
        OpenSubmenuColumn(BuildTcsReportsColumn(), GatewayMenu.TcsReports,
            "Gateway of Apex Solutions — TCS Reports");
    }

    /// <summary>Opens the "Reports → Statutory Reports → Payroll" submenu column directly (Phase 8 slice 4/5).</summary>
    public void ShowPayrollStatutoryReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("Payroll");
        OpenSubmenuColumn(BuildPayrollStatutoryReportsColumn(), GatewayMenu.PayrollStatutoryReports,
            "Gateway of Apex Solutions — Payroll");
    }

    /// <summary>Builds the "Payroll Reports" submenu column (Reports → Payroll Reports; Phase 8 slice 8; RQ-16;
    /// catalog §14): the Payslip (single-employee detail + PDF), the Pay Sheet (employees × pay heads), the Payroll
    /// Register/Statement (columnar salary summary), the Attendance Register (employees × attendance types) and the
    /// Payment/Bank Advice (net-pay-per-employee bank list) — all pure projections over the posted payroll data.</summary>
    private GatewayColumn BuildPayrollReportsColumn()
    {
        var col = new GatewayColumn("Payroll Reports");
        col.Add(MenuItemViewModel.Header("Payroll Reports"));
        col.Add(new MenuItemViewModel("Payslip", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Pay Sheet", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Payroll Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Attendance Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Payment Advice", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Opens the "Reports → Payroll Reports" submenu column directly (Phase 8 slice 8; RQ-16) — the public
    /// entry a hotkey/test uses. A no-op when Payroll is off (the group is not surfaced).</summary>
    public void ShowPayrollReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        if (Company is not { PayrollEnabled: true }) return;   // group hidden when Payroll is off (ER-13)
        SelectRootItem("Payroll Reports");
        OpenSubmenuColumn(BuildPayrollReportsColumn(), GatewayMenu.PayrollReports,
            "Gateway of Apex Solutions — Payroll Reports");
    }

    /// <summary>
    /// Opens the "Vouchers" submenu column directly (Transactions → Vouchers). Rebuilds the cascade to
    /// [root → Vouchers] and focuses the Vouchers column — the public entry the F-keys/tests use.
    /// </summary>
    public void ShowVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Vouchers");
        OpenSubmenuColumn(BuildVouchersColumn(), GatewayMenu.Vouchers,
            "Gateway of Apex Solutions — Vouchers");
    }

    /// <summary>
    /// Opens the "Other Vouchers" submenu column directly (Transactions → Vouchers → Other Vouchers).
    /// Rebuilds the cascade to [root → Vouchers → Other Vouchers] and focuses it — the public entry a
    /// hotkey/test uses.
    /// </summary>
    public void ShowOtherVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        // Rebuild the cascade down to the Vouchers submenu, then push Other Vouchers onto it.
        ShowVouchersMenu();
        var vouchers = Columns[^1];
        for (var i = 0; i < vouchers.Items.Count; i++)
            if (vouchers.Items[i].IsSelectable && vouchers.Items[i].Label == "Other Vouchers")
            {
                vouchers.SetSelected(i);
                break;
            }
        OpenSubmenuColumn(BuildOtherVouchersColumn(), GatewayMenu.OtherVouchers,
            "Gateway of Apex Solutions — Other Vouchers");
    }

    /// <summary>
    /// Opens the "Create" submenu column directly (Masters → Create). Rebuilds the cascade to
    /// [root → Create] and focuses the Create column.
    /// </summary>
    public void ShowCreateMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Create");
        OpenSubmenuColumn(BuildCreateColumn(), GatewayMenu.Create,
            "Gateway of Apex Solutions — Create");
    }

    /// <summary>Highlights the named root item and trims the cascade back to the root column.</summary>
    private void SelectRootItem(string label)
    {
        if (Columns.Count == 0 || !Columns[0].IsMenu) ShowGateway();
        TrimColumnsAfter(0);
        var root = Columns[0];
        for (var i = 0; i < root.Items.Count; i++)
            if (root.Items[i].IsSelectable && root.Items[i].Label == label)
            {
                root.SetSelected(i);
                break;
            }
    }

    /// <summary>
    /// Pushes a submenu menu column onto the cascade and focuses it. Used by the direct
    /// <see cref="ShowVouchersMenu"/> / <see cref="ShowCreateMenu"/> entries.
    /// </summary>
    private void OpenSubmenuColumn(GatewayColumn column, GatewayMenu menu, string title)
    {
        ClearSubScreens();
        Columns.Add(column);
        column.SelectFirstSelectable();
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = menu;
        ScreenTitle = title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    // =============================================================== screen: report

    /// <summary>
    /// Opens a report as a page column on the right of the cascade (when a company/Gateway is open) —
    /// or, when called cold (e.g. from a test/F-key before the cascade exists), as the sole page. For a
    /// <see cref="ReportKind.StockItemMovement"/> report, <paramref name="stockItemId"/> scopes it to one
    /// item (the Stock-Summary drill target); it is ignored by the other kinds. A Stock-Summary report is
    /// wired so drilling a row (Enter / double-click a stock item) opens that item's movement report.
    /// </summary>
    public void OpenReport(ReportKind kind, Guid? stockItemId = null)
    {
        if (Company is null) return;

        var reports = new ReportsViewModel(Company, kind, stockItemId);
        WireReportDrills(reports);
        OpenPageColumn(new GatewayColumn(reports.Title, reports), Screen.Report, reports.Title,
            () => Reports = reports);
    }

    /// <summary>
    /// Wires every RQ-7 drill event a report can raise to the shell handler that opens its target. Factored
    /// out of <see cref="OpenReport"/> so the W2-12 openers below — which construct a scoped
    /// <see cref="ReportsViewModel"/> rather than calling <see cref="OpenReport"/> — cannot silently ship a
    /// report whose rows look drillable and do nothing.
    /// </summary>
    private void WireReportDrills(ReportsViewModel reports)
    {
        if (reports.Kind == ReportKind.StockSummary)
            reports.DrillToMovementRequested += id => OpenReport(ReportKind.StockItemMovement, id);
        // RQ-7 universal drill-down: a TB/BS/P&L ledger row opens that ledger's vouchers as a NEW cascading
        // column (the report pane persists); a Day Book row opens the voucher's read-only detail.
        reports.DrillToLedgerRequested += (ledgerId, from, to, movement) => OpenLedgerVouchers(ledgerId, from, to, movement);
        reports.DrillToVoucherRequested += OpenVoucherDetail;

        // ---- W2-12 (census 11.6 / 11.7) ----
        reports.DrillToRegisterMonthRequested += (kind, from, to) => OpenRegisterMonth(kind, from, to);
        reports.DrillToGroupSummaryRequested += groupId => OpenGroupReport(ReportKind.GroupSummary, groupId);
        reports.DrillToLedgerMonthlyRequested += (ledgerId, from, to) => OpenLedgerMonthlySummary(ledgerId, from, to);
    }

    /// <summary>
    /// W2-12 (census 11.6). Opens a register's <b>voucher-wise</b> level for one month, as its OWN cascade
    /// column to the right of the month-wise register it drilled from — so the month list persists beneath
    /// and Esc/Back restores it, exactly like every other drill in the product.
    /// </summary>
    public void OpenRegisterMonth(ReportKind kind, DateOnly from, DateOnly to)
    {
        if (Company is null) return;

        var vm = new ReportsViewModel(Company, kind,
            period: new Apex.Ledger.Reports.PeriodRange(from, to), registerVoucherLevel: true);
        WireReportDrills(vm);
        OpenDrillColumn(new GatewayColumn(vm.Title, vm), Screen.Report, vm.Title, () => Reports = vm);
    }

    /// <summary>
    /// W2-12 (census 11.7). Opens a group-scoped report (Group Summary / Group Vouchers) as a page column.
    /// A safe no-op on an unknown group id.
    /// </summary>
    public void OpenGroupReport(ReportKind kind, Guid groupId)
    {
        if (Company is null || groupId == Guid.Empty) return;

        var vm = new ReportsViewModel(Company, kind, scopeMasterId: groupId);
        WireReportDrills(vm);
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.Report, vm.Title, () => Reports = vm);
    }

    /// <summary>
    /// W2-12 (census T1-32). Opens a ledger's Monthly Summary as a drill column — the level between a group
    /// or account book and the voucher list. Its month rows drill on into
    /// <see cref="OpenLedgerVouchers"/> for that month.
    /// </summary>
    public void OpenLedgerMonthlySummary(Guid ledgerId, DateOnly from, DateOnly to)
    {
        if (Company is null || ledgerId == Guid.Empty) return;

        var vm = new ReportsViewModel(Company, ReportKind.LedgerMonthlySummary,
            scopeMasterId: ledgerId, period: new Apex.Ledger.Reports.PeriodRange(from, to));
        WireReportDrills(vm);
        OpenDrillColumn(new GatewayColumn(vm.Title, vm), Screen.Report, vm.Title, () => Reports = vm);
    }

    /// <summary>
    /// The keyboard-first report drill (Enter / double-click on the highlighted report row). Dispatched by the
    /// report's own kind: Stock Summary → the item's movement report; TB/BS/P&amp;L → that ledger's vouchers;
    /// Day Book → the voucher's detail. A safe no-op on any non-drillable row. Also serves a drilled
    /// ledger-vouchers column (its posting rows drill one level deeper into the voucher).
    /// </summary>
    public void DrillReport(ReportRow? row)
    {
        if (LedgerVouchers is not null) LedgerVouchers.Drill(row);
        else Reports?.Drill(row);
    }

    /// <summary>
    /// RQ-7 keyboard-Enter drill (defect-1): drills the ACTIVE pane's highlighted row using the row the pane's
    /// grid two-way-bound as its <c>SelectedRow</c> — so the drill does not depend on which control holds focus.
    /// Returns true iff a drill was performed (a drillable row on a report / ledger-vouchers pane), letting the
    /// shell's Enter handler mark the key handled ahead of the generic cascade Enter. A safe no-op (false) on a
    /// non-drillable row, on a voucher-detail pane, or on any non-report screen.
    /// </summary>
    public bool DrillSelectedRow()
    {
        // A ledger-vouchers drill column takes priority: its posting rows drill one level deeper.
        if (CurrentScreen == Screen.LedgerVouchers && LedgerVouchers is { SelectedRow: { CanDrill: true } lvRow })
        {
            LedgerVouchers.Drill(lvRow);
            return true;
        }

        // An accounting report (TB/BS/P&L/Day Book) or Stock Summary: drill the highlighted row.
        if (CurrentScreen == Screen.Report && Reports is { SelectedRow: { CanDrill: true } reportRow })
        {
            Reports.Drill(reportRow);
            return true;
        }

        return false;
    }

    // =============================================================== screen: RQ-7 ledger-vouchers drill

    /// <summary>
    /// Opens the RQ-7 ledger-vouchers drill target — the drilled ledger's <see cref="Apex.Ledger.Reports.LedgerBook"/>
    /// over [<paramref name="from"/>,<paramref name="to"/>] — as its OWN cascading column to the RIGHT of the
    /// report it drilled from (mirroring <see cref="OpenReportConfig"/>): the report stays live beneath so Esc/Back
    /// pops this column and restores it. The posting rows are themselves drillable into the voucher detail. A
    /// safe no-op on a non-drillable id (the engine returns an empty book anyway).
    /// </summary>
    public void OpenLedgerVouchers(Guid ledgerId, DateOnly from, DateOnly to, bool movement = false)
    {
        if (Company is null || ledgerId == Guid.Empty) return;

        var vm = new LedgerVouchersViewModel(Company, ledgerId, from, to, movement);
        vm.DrillToVoucherRequested += OpenVoucherDetail;
        OpenDrillColumn(new GatewayColumn(vm.Title, vm), Screen.LedgerVouchers, vm.Title, () => LedgerVouchers = vm);
    }

    /// <summary>
    /// Opens the RQ-7 voucher-detail drill target — a read-only view of the voucher — as its OWN cascading column
    /// to the RIGHT of the report/ledger-vouchers column it drilled from (the prior pane persists; Esc/Back pops).
    /// A safe no-op when the id does not resolve to a voucher.
    /// </summary>
    public void OpenVoucherDetail(Guid voucherId)
    {
        if (Company is null) return;
        var voucher = Company.FindVoucher(voucherId);
        if (voucher is null) return;

        var vm = new VoucherDetailViewModel(Company, voucher);
        OpenDrillColumn(new GatewayColumn(vm.Title, vm), Screen.VoucherDetail, vm.Title, () => VoucherDetail = vm);
    }

    /// <summary>
    /// Appends a drill column to the RIGHT of the cascade WITHOUT trimming the pane it drilled from — the RQ-7
    /// Miller-column drill (prior panes persist), unlike <see cref="OpenPageColumn"/> which replaces the page.
    /// Esc/Back pops it and <see cref="RehydratePageFromRightmostColumn"/> re-binds the surviving pane.
    /// </summary>
    private void OpenDrillColumn(GatewayColumn column, Screen screen, string title, Action setPage)
    {
        if (Columns.Count == 0) return; // nothing to drill from
        setPage();
        Columns.Add(column);
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = screen;
        ScreenTitle = title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    // =============================================================== screen: report configuration (F12)

    /// <summary>
    /// F12 — opens the report Configuration panel (RQ-1/2/6) as its own cascading column to the RIGHT of the
    /// open report, never a stacked overlay. Unlike the other page-openers it does NOT trim the report page
    /// column: the report stays live (its <see cref="Reports"/> binding intact) so applying the panel
    /// re-projects the same report in place. A no-op unless a report is currently open. Re-pressing F12 while
    /// the panel is open is a no-op (there is already a config column).
    /// </summary>
    public void OpenReportConfig()
    {
        if (Reports is null) return;                 // only meaningful over an open report
        if (ReportConfig is not null) return;        // panel already open — don't stack a second one

        var config = new ReportConfigViewModel(Reports);
        ReportConfig = config;
        Columns.Add(new GatewayColumn(config.Title, config));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.ReportConfig;
        ScreenTitle = config.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Apply button on the F12 config panel: apply the settings and re-run the report.</summary>
    public void ApplyReportConfig() => ReportConfig?.Apply();

    /// <summary>
    /// Alt+F12 — opens the report Sort/Filter panel (RQ-3) as its own cascading column to the RIGHT of the open
    /// report, never a stacked overlay, mirroring <see cref="OpenReportConfig"/>. The report stays live beneath
    /// the panel so applying re-projects it in place. A no-op unless a report is open; re-pressing Alt+F12 while
    /// the panel is open is a no-op (there is already a sort/filter column).
    /// </summary>
    public void OpenReportSortFilter()
    {
        if (Reports is null) return;                 // only meaningful over an open report
        if (ReportSortFilter is not null) return;    // panel already open — don't stack a second one

        var panel = new ReportSortFilterViewModel(Reports);
        ReportSortFilter = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.ReportSortFilter;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Apply button on the Alt+F12 sort/filter panel: apply the view and re-run the report.</summary>
    public void ApplyReportSortFilter() => ReportSortFilter?.Apply();

    /// <summary>The Clear button on the Alt+F12 sort/filter panel: reset the view to the identity and re-run.</summary>
    public void ClearReportSortFilter() => ReportSortFilter?.Clear();

    // =============================================================== W2-13a: Ctrl+B Basis of Values (census 14.5)

    /// <summary>
    /// <b>Ctrl+B — Basis of Values.</b> Opens the Scale-Factor panel as its own cascading column to the RIGHT of
    /// the open report, never a stacked overlay, mirroring <see cref="OpenReportConfig"/>. The report stays live
    /// beneath it so applying re-projects in place.
    ///
    /// <para>Refused — quietly, with no column pushed — unless the open report actually supports the scale
    /// (<see cref="ReportsViewModel.SupportsScaleFactor"/>). A panel that opens on a report it cannot change is
    /// the dead-control defect this project has caught before; the button-bar row dims in the same condition so
    /// the key and the badge agree.</para>
    /// </summary>
    public void OpenBasisOfValues()
    {
        if (Reports is not { SupportsScaleFactor: true }) return;
        if (BasisOfValues is not null) return;        // panel already open — don't stack a second one

        var panel = new BasisOfValuesViewModel(Reports);
        BasisOfValues = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.BasisOfValues;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// Ctrl+A / the Apply button on the Ctrl+B panel: apply the Scale Factor, then pop the panel so the operator
    /// lands back on the re-scaled report rather than on a spent column.
    /// </summary>
    public void ApplyBasisOfValues()
    {
        if (BasisOfValues is null) return;
        BasisOfValues.Apply();
        if (CurrentScreen == Screen.BasisOfValues) Back();
    }

    /// <summary>
    /// True while a report is the ACTIVE page (or its F12 config panel is open) — the report-parameter
    /// shortcuts (F2, F12, Alt+F1, Alt+F2, Alt+F12, Alt+C, Alt+N) act on it. False when a drill column
    /// (LedgerVouchers / VoucherDetail) is the active/rightmost pane even though the report still exists
    /// beneath it: those shortcuts must be inert there so they never re-parameterise or re-open config on the
    /// underlying report the user has drilled away from (RQ-7). Enter (drill) and Esc/Back still work in the
    /// drill columns via their own handling.
    /// </summary>
    public bool IsReportContext => Reports is not null
        && CurrentScreen is not (Screen.LedgerVouchers or Screen.VoucherDetail);

    /// <summary>
    /// True only while the report page is the <b>ACTIVE COLUMN</b> — the operator is standing ON the report, not
    /// in something stacked over it.
    ///
    /// <para>🔴 <b>Why this exists next to <see cref="IsReportContext"/> rather than replacing it.</b> The two
    /// answer different questions and a destructive verb needs this one. <see cref="IsReportContext"/> is
    /// deliberately TRUE while an F12 config panel, an Alt+F12 sort/filter panel, an Alt+A add-voucher picker, an
    /// Alt+K saved-views panel or a Print Preview column is open, because each of those leaves
    /// <see cref="Reports"/> bound beneath it and the report-PARAMETER shortcuts must keep acting on the report
    /// underneath. Phase 10.11 S3's Alt+X arm was written on it and inherited exactly that width: with the Day
    /// Book row still highlighted behind the column, Alt+X raised the cancellation for the voucher BEHIND
    /// whichever panel the operator was in, and a single Y voided it. <c>IsPickerOpen</c> does not close that hole
    /// — it sees an open ComboBox popup, not a Miller column.</para>
    ///
    /// <para>Deliberately expressed as <see cref="Screen.Report"/> and nothing else, which is the same test
    /// <see cref="DrillSelectedRow"/> already uses to decide that the report's own Enter belongs to it:
    /// <see cref="Reports"/> is bound under exactly one screen (<see cref="OpenReport"/> and the column
    /// re-bind), so this is "the live report page and no other surface".</para>
    /// </summary>
    public bool IsLiveReportPage => Reports is not null && CurrentScreen == Screen.Report;

    /// <summary>
    /// True while the LIVE report is the Day Book (WI-12) — the single context the Alt+A "Add Voucher" picker is
    /// offered in. Stays true while its own picker column is open (<see cref="Reports"/> is left bound beneath the
    /// picker), so Esc/Back returns to the same live Day Book.
    /// </summary>
    public bool IsDayBookReport => Reports is { Kind: ReportKind.DayBook }
        && CurrentScreen is not (Screen.LedgerVouchers or Screen.VoucherDetail);

    /// <summary>True on a page that Print (P/Ctrl+P) can render (RQ-9/10/11): an open report, or a drilled
    /// voucher-detail (which prints the voucher / tax invoice). Used to gate the Print shortcut.</summary>
    public bool IsPrintablePage =>
        IsReportContext || (CurrentScreen == Screen.VoucherDetail && VoucherDetail is not null);

    /// <summary>
    /// F2 on a report — opens the Configuration panel focused on the single as-of date (RQ-1). The panel is
    /// the keyboard-first date-entry surface (there is no modal date dialog); it opens seeded from the report's
    /// current as-of with the period window off, so accepting sets the as-of.
    /// </summary>
    public void ReportSetAsOf()
    {
        if (Reports is null) return;
        OpenReportConfig();
        if (ReportConfig is { } cfg) cfg.UsePeriod = false;
    }

    /// <summary>
    /// Alt+F2 on a report — opens the Configuration panel focused on the [from,to] period window (RQ-1), with
    /// the window enabled so accepting sets an explicit period. Seeded from the report's current window (or the
    /// as-of when none is set yet).
    /// </summary>
    public void ReportSetPeriod()
    {
        if (Reports is null) return;
        OpenReportConfig();
        if (ReportConfig is { } cfg) cfg.UsePeriod = true;
    }

    /// <summary>Alt+F1 on a report — toggles detailed↔summary in place (RQ-2). A no-op on reports that do not roll up.</summary>
    public void ReportToggleDetailed() => Reports?.ToggleDetailed();

    // =============================================================== F2 — set the date, in whatever window (WI-5 4c)

    /// <summary>
    /// The open ENTRY page's working-date field, or <c>null</c> when the current screen has none. This is what
    /// makes <b>F2 — Date</b> work beyond reports: previously F2 was a stub on every non-report screen that
    /// merely printed the financial-year start to the status line, so on a voucher-entry screen — precisely the
    /// case the corpus documents ("Date — Type date of Purchase/Sale transactions by pressing F2") — it did
    /// nothing useful.
    /// </summary>
    public ISetsWorkingDate? ActiveWorkingDateTarget => CurrentScreen switch
    {
        Screen.VoucherEntry => VoucherEntry,
        Screen.InventoryVoucherEntry => InventoryVoucherEntry,
        Screen.ManufacturingJournalEntry => ManufacturingJournalEntry,
        Screen.JobWorkOrderEntry => JobWorkOrderEntry,
        Screen.MaterialMovementEntry => MaterialMovementEntry,
        Screen.PosBilling => PosBilling,
        Screen.AttendanceVoucherEntry => AttendanceVoucher,
        Screen.PayrollVoucherEntry => PayrollVoucher,
        _ => null,
    };

    /// <summary>True while the open screen owns a working date that F2 can set.</summary>
    public bool IsWorkingDateContext => ActiveWorkingDateTarget is not null;

    /// <summary>
    /// Raised when F2 asks to set the working date on an entry screen. The shell (view) responds by moving the
    /// caret into that screen's working-date box — the keyboard-first equivalent of Tally's F2 date prompt.
    /// <b>It deliberately does NOT open a calendar/DatePicker</b>: the app has zero DatePicker controls by
    /// design, and F2 must stay a keyboard action.
    /// </summary>
    public event EventHandler? WorkingDateEditRequested;

    /// <summary>
    /// <b>F2 — Date.</b> On an entry screen this puts the caret in the working-date field so the operator types
    /// the date (read by the one shared day-first parser, echoed canonically). Everywhere else it reports the
    /// current working date. Reports never reach here — their bare F2 is intercepted earlier and sets the
    /// report as-of instead (the Tally F2 / Alt+F2 split, left untouched).
    /// </summary>
    public void SetWorkingDate()
    {
        if (ActiveWorkingDateTarget is { } target)
        {
            StatusDate = target.WorkingDateText;
            WorkingDateEditRequested?.Invoke(this, EventArgs.Empty);
            Message = $"F2 — set the date (type {ApexDate.Canonical}, e.g. 01-Apr-2020).";
            return;
        }

        Message = StatusDate;
    }

    /// <summary>
    /// Keeps the status bar's "Current Date" showing the WORKING date of the open entry screen. Previously
    /// <see cref="StatusDate"/> was written once, at company open, with the financial-year start and never
    /// updated again — so it disagreed with the voucher being entered.
    /// </summary>
    public void RefreshStatusDate()
    {
        if (ActiveWorkingDateTarget is { } target) StatusDate = target.WorkingDateText;
    }

    /// <summary>True while the open report is the Reorder Status report (drives its F8 / Ctrl+F9 shortcuts).</summary>
    public bool IsReorderStatusReport => IsReportContext && Reports is { IsReorderStatus: true };

    /// <summary>F8 on the Reorder Status report — toggles the "reorder only" filter (RQ-53). A no-op otherwise.</summary>
    public void ReportToggleReorderOnly()
    {
        if (Reports is { IsReorderStatus: true } r) r.ToggleReorderOnly();
    }

    /// <summary>
    /// Ctrl+F9 on the Reorder Status report — raises a <b>Purchase Order</b> pre-filled from the selected row (the
    /// item, the company's main location, and the "Order to be Placed" quantity; RQ-53/Book p.161). Falls back to a
    /// blank Purchase Order when no drillable row is selected or the row's order quantity is zero.
    /// </summary>
    public void RaisePurchaseOrderFromReorder()
    {
        if (Company is null) return;
        if (Reports is not { IsReorderStatus: true } r) return;

        var row = r.SelectedRow;
        if (row?.DrillStockItemId is not { } itemId || row.ReorderOrderQuantity <= 0m)
        {
            OpenInventoryVoucher(VoucherBaseType.PurchaseOrder);   // no actionable row → a blank order
            return;
        }

        OpenInventoryVoucher(VoucherBaseType.PurchaseOrder);
        if (InventoryVoucherEntry is not { } entry) return;

        var item = Company.FindStockItem(itemId);
        if (item is null) return;
        var line = entry.Lines.FirstOrDefault() ?? entry.AddLine();
        line.SelectedItem = item;
        line.SelectedGodown = Company.MainLocation ?? Company.Godowns.FirstOrDefault();
        line.QuantityText = row.ReorderOrderQuantity.ToString("0.######",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    // =============================================================== screen: comparative columns (Alt+C / Alt+N)

    /// <summary>
    /// Alt+C — opens the "Add Comparison Column" panel (RQ-4) as its own cascading column to the RIGHT of the open
    /// report, never a stacked overlay, mirroring <see cref="OpenReportConfig"/>. The report stays live beneath the
    /// panel so applying appends a comparison column and re-renders the report in place. A no-op unless a
    /// comparative-capable report is open; re-pressing Alt+C while the panel is open is a no-op.
    /// </summary>
    public void OpenAddComparisonColumn()
    {
        if (Reports is null || !Reports.SupportsComparative) return; // only over a comparative-capable report
        if (AddComparisonColumn is not null) return;                 // panel already open — don't stack a second
        CloseComparativePanelsExcept(null);                          // the two panels are mutually exclusive

        var panel = new AddComparisonColumnViewModel(Reports);
        AddComparisonColumn = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.AddComparisonColumn;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Add button on the Alt+C panel: append the comparison column and re-render.</summary>
    public void ApplyAddComparisonColumn() => AddComparisonColumn?.Apply();

    /// <summary>
    /// Alt+N — opens the "Auto Columns" chooser (RQ-4) as its own cascading column to the RIGHT of the open
    /// report, never a stacked overlay, mirroring <see cref="OpenAddComparisonColumn"/>. Applying generates the
    /// chosen axis (by month / by scenario) on the live report. A no-op unless a comparative-capable report is
    /// open; re-pressing Alt+N while the panel is open is a no-op.
    /// </summary>
    public void OpenAutoColumns()
    {
        if (Reports is null || !Reports.SupportsComparative) return;
        if (AutoColumns is not null) return;
        CloseComparativePanelsExcept(null);

        var panel = new AutoColumnsViewModel(Reports);
        AutoColumns = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.AutoColumns;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Generate button on the Alt+N panel: generate the chosen axis and re-render.</summary>
    public void ApplyAutoColumns() => AutoColumns?.Apply();

    /// <summary>Resets the active report back to a single column (the Clear action on either comparative panel).</summary>
    public void ClearComparative() => Reports?.ClearComparative();

    /// <summary>
    /// Pops any open Alt+C / Alt+N comparative panel column so only one panel is ever stacked beside the report.
    /// The <paramref name="keep"/> argument is reserved for future use; currently both panels are closed.
    /// </summary>
    private void CloseComparativePanelsExcept(object? keep)
    {
        // Only one comparative panel is ever open at a time (opening the other pops this one). Pop the rightmost
        // column if it hosts a comparative panel, so switching Alt+C ↔ Alt+N replaces rather than stacks.
        if (Columns.Count > 0 && Columns[^1].Page is AddComparisonColumnViewModel or AutoColumnsViewModel)
        {
            Columns.RemoveAt(Columns.Count - 1);
            AddComparisonColumn = null;
            AutoColumns = null;
        }
    }

    // =============================================================== screen: Save View / Saved Views (RQ-8)

    /// <summary>
    /// Ctrl+S — opens the "Save View" panel (RQ-8) as its own cascading column to the RIGHT of the open report,
    /// never a stacked overlay, mirroring <see cref="OpenReportConfig"/>. The report stays live beneath the panel;
    /// applying captures the report's current CONFIGURATION TUPLE and upserts it (by name) into the company's
    /// store — no figures are stored (ER-9). A no-op unless a report is open; re-pressing Ctrl+S while the panel
    /// is open is a no-op (there is already a Save-View column).
    /// </summary>
    public void OpenSaveView()
    {
        if (Reports is null || Company is null) return; // only over an open report of an open company
        if (SaveView is not null) return;               // panel already open — don't stack a second

        var panel = new SaveViewViewModel(Reports, Company, _storage);
        SaveView = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.SaveView;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Save button on the Save-View panel: save the view, then pop the panel on success so
    /// the report is the active pane again (a rejected blank name leaves the panel open with its error).</summary>
    public void ApplySaveView()
    {
        if (SaveView is null) return;
        if (SaveView.Apply()) BackFromPage();
    }

    /// <summary>
    /// Alt+K — opens the "Saved Views" list (RQ-8), nested under Reports as its own cascading column to the RIGHT
    /// of the open report (keyboard-first, never a flat dump). Lists this company's saved views; the user opens
    /// (applies) or deletes one. A no-op unless a company is open; re-pressing Alt+K while the panel is open is a
    /// no-op. Unlike the other report panels it does not require a report to be open — it is reachable over any
    /// report page and lists the company's views regardless.
    /// </summary>
    public void OpenSavedViews()
    {
        if (Company is null) return;      // needs a company to scope the views to
        if (SavedViews is not null) return; // panel already open — don't stack a second

        var panel = new SavedViewsViewModel(Company, _storage);
        panel.OpenRequested += ApplySavedView;
        SavedViews = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.SavedViews;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>The Open action on the Saved-Views panel: apply the highlighted saved view (delegates to the
    /// panel, which raises the open request the shell services via <see cref="ApplySavedView"/>).</summary>
    public void OpenSelectedSavedView() => SavedViews?.Open();

    /// <summary>The Delete action on the Saved-Views panel: delete the highlighted saved view and refresh the list.</summary>
    public void DeleteSelectedSavedView() => SavedViews?.Delete();

    // =============================================================== W2-14: Go To (Alt+G), census row 14.1

    /// <summary>
    /// <b>Alt+G — Go To.</b> Opens the jump-anywhere index as its own cascading column over whatever surface is
    /// showing, so the surface underneath survives and Esc pops straight back to it.
    ///
    /// <para><b>Fidelity (RULING 14 — help.tallysolutions.com).</b> The vendor's keyboard-shortcut page defines
    /// Alt+G as <i>"To primarily open a report, and create masters and vouchers in the flow of work"</i> — all
    /// three verbs, and "in the flow of work", i.e. from wherever the user is. It is therefore NOT gated to the
    /// Gateway. <b>Ctrl+G ("Switch To") is a different verb on that same page</b> and is deliberately not built:
    /// census row 14.2 stays ABSENT.</para>
    ///
    /// <para>Needs a company (every destination is company-scoped). A second press is a no-op rather than a
    /// second stacked column.</para>
    /// </summary>
    public void OpenGoTo()
    {
        if (Company is null) return;
        if (GoTo is not null) return;    // already open — never stack a second index column

        var panel = new GoToViewModel(BuildGoToIndex());
        panel.GoRequested += TravelTo;
        GoTo = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.GoTo;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>The Go / Enter action on the Go To panel: travel to the highlighted destination.</summary>
    public void RunSelectedGoTo() => GoTo?.Go();

    /// <summary>
    /// Services a chosen Go To destination: closes the index column FIRST, then runs the destination's own
    /// opener. Closing first is what makes the jump land on the destination rather than on top of the index —
    /// every opener below pushes its own column, and leaving the index in place would bury it.
    /// </summary>
    private void TravelTo(GoToDestination destination)
    {
        // Pop the index column and unbind it; the surface it was pushed over is rehydrated by the opener that
        // follows (each opener rebuilds the cascade from the Gateway root or pushes over the live surface).
        if (Columns.Count > 0 && ReferenceEquals(Columns[^1].Page, GoTo))
            Columns.RemoveAt(Columns.Count - 1);
        GoTo = null;
        ActiveColumnIndex = Math.Max(0, Columns.Count - 1);

        destination.Open();
    }

    /// <summary>
    /// The Go To index: every destination the shell can open, nested under the Gateway root's OWN section
    /// headers so the two surfaces cannot disagree about where a thing lives.
    ///
    /// <para><b>This is an index over the existing dispatch, not a second one.</b> Every action below is the
    /// same public opener the corresponding Gateway menu row runs, so a destination can never travel somewhere
    /// the menu does not. Feature-gated rows are gated on the SAME F11/F12 flags their menu rows use (ER-13) —
    /// a Go To that offers a door the company has switched off is a worse lie than an omission, because the
    /// user typed the name and expected to arrive.</para>
    /// </summary>
    private List<GoToDestination> BuildGoToIndex()
    {
        var index = new List<GoToDestination>();
        var company = Company;
        if (company is null) return index;

        void Add(string section, string label, Action open, string hint = "")
            => index.Add(new GoToDestination(section, label, open, hint));

        // ---------------------------------------------------------------- Masters
        Add("Masters", "Ledger", ShowLedgerMaster, "Alt+C");
        Add("Masters", "Group", ShowAccountGroupMaster);
        Add("Masters", "Chart of Accounts", ShowChartOfAccounts);
        Add("Masters", "Alter Company", ShowAlterCompany);
        Add("Masters", "Cost Category", ShowCostCategoryMaster);
        Add("Masters", "Cost Centre", ShowCostCentreMaster);
        Add("Masters", "Stock Group", ShowStockGroupMaster);
        Add("Masters", "Stock Category", ShowStockCategoryMaster);
        Add("Masters", "Unit", ShowUnitMaster);
        Add("Masters", "Godown", ShowGodownMaster);
        Add("Masters", "Stock Item", ShowStockItemMaster);
        Add("Masters", "Reorder Levels", ShowReorderLevelsMaster);
        Add("Masters", "Budget", ShowBudgetMaster);
        Add("Masters", "Scenario", ShowScenarioMaster);
        Add("Masters", "Currency", ShowCurrencyMaster);
        if (company is { MaintainBatchwiseDetails: true })
            Add("Masters", "Batch", ShowBatchMaster);
        if (company is { SetComponentsBom: true })
            Add("Masters", "Bill of Materials", ShowBomMaster);
        if (company is { EnableMultiplePriceLevels: true })
        {
            Add("Masters", "Price Level", ShowPriceLevelsMaster);
            Add("Masters", "Price List", ShowPriceListsMaster);
        }
        if (company is { TdsEnabled: true })
            Add("Masters", "Nature of Payment", ShowNatureOfPaymentMaster);
        if (company is { TcsEnabled: true })
            Add("Masters", "Nature of Goods", ShowNatureOfGoodsMaster);
        if (company is { PayrollEnabled: true })
        {
            Add("Masters", "Employee Category", ShowEmployeeCategoryMaster);
            Add("Masters", "Employee Group", ShowEmployeeGroupMaster);
            Add("Masters", "Employee", ShowEmployeeMaster);
            Add("Masters", "Payroll Unit", ShowPayrollUnitMaster);
            Add("Masters", "Attendance / Production Type", ShowAttendanceTypeMaster);
            Add("Masters", "Pay Head", ShowPayHeadMaster);
            Add("Masters", "Salary Details", ShowSalaryStructureMaster);
            if (company is { SalaryTdsEnabled: true })
                Add("Masters", "Income Tax Declaration", ShowTaxDeclarationMaster);
        }

        // ---------------------------------------------------------------- Statutory
        Add("Statutory", "GST & Taxation", ShowGstConfig, "F11");
        if (company is { GstEnabled: true })
            Add("Statutory", "GST Rate Setup", ShowGstRateSetup, "Ctrl+R");

        // ---------------------------------------------------------------- Transactions
        // The accounting voucher kinds go through OpenVoucherFromTypeKey, NOT straight to OpenVoucher, so a jump
        // can never silently discard a voucher that is mid-keying — the same guard the F4–F9 keys use.
        Add("Transactions", "Contra", () => OpenVoucherFromTypeKey(VoucherBaseType.Contra), "F4");
        Add("Transactions", "Payment", () => OpenVoucherFromTypeKey(VoucherBaseType.Payment), "F5");
        Add("Transactions", "Receipt", () => OpenVoucherFromTypeKey(VoucherBaseType.Receipt), "F6");
        Add("Transactions", "Journal", () => OpenVoucherFromTypeKey(VoucherBaseType.Journal), "F7");
        Add("Transactions", "Sales", () => OpenVoucherFromTypeKey(VoucherBaseType.Sales), "F8");
        Add("Transactions", "Purchase", () => OpenVoucherFromTypeKey(VoucherBaseType.Purchase), "F9");
        Add("Transactions", "Credit Note", () => OpenVoucherFromTypeKey(VoucherBaseType.CreditNote), "Alt+F6");
        Add("Transactions", "Debit Note", () => OpenVoucherFromTypeKey(VoucherBaseType.DebitNote), "Alt+F5");
        Add("Transactions", "Purchase Order", () => OpenInventoryVoucher(VoucherBaseType.PurchaseOrder), "Ctrl+F9");
        Add("Transactions", "Sales Order", () => OpenInventoryVoucher(VoucherBaseType.SalesOrder), "Ctrl+F8");
        Add("Transactions", "Receipt Note", () => OpenInventoryVoucher(VoucherBaseType.ReceiptNote), "Alt+F9");
        Add("Transactions", "Delivery Note", () => OpenInventoryVoucher(VoucherBaseType.DeliveryNote), "Alt+F8");
        Add("Transactions", "Rejection In", () => OpenInventoryVoucher(VoucherBaseType.RejectionIn), "Ctrl+F6");
        Add("Transactions", "Rejection Out", () => OpenInventoryVoucher(VoucherBaseType.RejectionOut), "Ctrl+F5");
        Add("Transactions", "Stock Journal", () => OpenInventoryVoucher(VoucherBaseType.StockJournal), "Alt+F7");
        Add("Transactions", "Physical Stock", () => OpenInventoryVoucher(VoucherBaseType.PhysicalStock), "Ctrl+F7");
        Add("Transactions", "Memorandum", () => OpenVoucherFromTypeKey(VoucherBaseType.Memorandum));
        Add("Transactions", "Reversing Journal", () => OpenVoucherFromTypeKey(VoucherBaseType.ReversingJournal));
        if (company is { TdsEnabled: true })
            Add("Transactions", "TDS Stat Payment", ShowTdsStatPayment, "Ctrl+F");
        if (company is { TcsEnabled: true })
            Add("Transactions", "TCS Stat Payment", ShowTcsStatPayment);
        if (company is { PayrollEnabled: true })
        {
            Add("Transactions", "Attendance / Production", ShowAttendanceVoucher);
            Add("Transactions", "Payroll", ShowPayrollVoucher, "Ctrl+F4");
        }

        // ---------------------------------------------------------------- Reports
        Add("Reports", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet));
        Add("Reports", "Profit & Loss A/c", () => OpenReport(ReportKind.ProfitAndLoss));
        Add("Reports", "Trial Balance", () => OpenReport(ReportKind.TrialBalance));
        Add("Reports", "Day Book", () => OpenReport(ReportKind.DayBook));
        Add("Reports", "Cash Book", ShowCashBookMenu);
        Add("Reports", "Bank Book", ShowBankBookMenu);
        Add("Reports", "Ledger Book", ShowLedgerBooksMenu);
        Add("Reports", "Sales Register", () => OpenReport(ReportKind.SalesRegister));
        Add("Reports", "Purchase Register", () => OpenReport(ReportKind.PurchaseRegister));
        Add("Reports", "Journal Register", () => OpenReport(ReportKind.JournalRegister));
        Add("Reports", "Credit Note Register", () => OpenReport(ReportKind.CreditNoteRegister));
        Add("Reports", "Debit Note Register", () => OpenReport(ReportKind.DebitNoteRegister));
        Add("Reports", "Group Summary", ShowGroupSummaryMenu);
        Add("Reports", "Group Vouchers", ShowGroupVouchersMenu);
        Add("Reports", "Statistics", () => OpenReport(ReportKind.Statistics));
        Add("Reports", "Cash Flow", () => OpenReport(ReportKind.CashFlow));
        Add("Reports", "Funds Flow", () => OpenReport(ReportKind.FundsFlow));
        Add("Reports", "Ratio Analysis", () => OpenReport(ReportKind.RatioAnalysis));
        Add("Reports", "Receivables", () => OpenOutstandings(OutstandingsKind.Receivables));
        Add("Reports", "Payables", () => OpenOutstandings(OutstandingsKind.Payables));
        Add("Reports", "Cost Category Summary", () => OpenCostReport(CostReportKind.CategorySummary));
        Add("Reports", "Cost Centre Break-up", () => OpenCostReport(CostReportKind.CostCentreBreakup));
        Add("Reports", "Budget Variance", OpenBudgetVariance);
        Add("Reports", "Interest Calculation", OpenInterestReport);
        Add("Reports", "Forex Gain / Loss", OpenForexReport);
        Add("Reports", "Bank Reconciliation", OpenBankReconciliation, "BRS");
        Add("Reports", "Stock Summary", () => OpenReport(ReportKind.StockSummary));
        Add("Reports", "Godown Summary", () => OpenReport(ReportKind.GodownSummary));
        Add("Reports", "Reorder Status", () => OpenReport(ReportKind.ReorderStatus));
        Add("Reports", "Receipt Note Register", () => OpenReport(ReportKind.ReceiptNoteRegister));
        Add("Reports", "Delivery Note Register", () => OpenReport(ReportKind.DeliveryNoteRegister));
        Add("Reports", "Rejection Register", () => OpenReport(ReportKind.RejectionRegister));
        Add("Reports", "Physical Stock Register", () => OpenReport(ReportKind.PhysicalStockRegister));
        Add("Reports", "Order Register", () => OpenReport(ReportKind.OrderRegister));
        Add("Reports", "Negative Stock", () => OpenReport(ReportKind.NegativeStock));
        Add("Reports", "Negative Cash / Bank", () => OpenReport(ReportKind.NegativeCashBank));
        Add("Reports", "Memorandum Register", () => OpenReport(ReportKind.MemorandumRegister));
        Add("Reports", "Reversing Journal Register", () => OpenReport(ReportKind.ReversingJournalRegister));
        if (company is { MaintainBatchwiseDetails: true })
        {
            Add("Reports", "Batch-wise", () => OpenReport(ReportKind.Batchwise));
            Add("Reports", "Batch Age Analysis", () => OpenReport(ReportKind.BatchAgeAnalysis));
        }
        if (company is { EnableMultiplePriceLevels: true })
            Add("Reports", "Price List Report", () => OpenReport(ReportKind.PriceList));
        if (company is { EnableJobOrderProcessing: true })
        {
            Add("Reports", "Job Work In Order Book", () => OpenReport(ReportKind.JobWorkInOrderBook));
            Add("Reports", "Job Work Out Order Book", () => OpenReport(ReportKind.JobWorkOutOrderBook));
            Add("Reports", "Material In Register", () => OpenReport(ReportKind.MaterialInRegister));
            Add("Reports", "Material Out Register", () => OpenReport(ReportKind.MaterialOutRegister));
        }
        if (company is { GstEnabled: true })
        {
            Add("Reports", "Tax Analysis", () => OpenReport(ReportKind.TaxAnalysis));
            Add("Reports", "GSTR-1", () => OpenReport(ReportKind.Gstr1));
            Add("Reports", "GSTR-3B", () => OpenReport(ReportKind.Gstr3b));
        }
        if (company is { PayrollEnabled: true })
        {
            Add("Reports", "Payslip", () => OpenReport(ReportKind.Payslip));
            Add("Reports", "Pay Sheet", () => OpenReport(ReportKind.PaySheet));
            Add("Reports", "Payroll Register", () => OpenReport(ReportKind.PayrollRegister));
            Add("Reports", "Attendance Register", () => OpenReport(ReportKind.AttendanceRegister));
            Add("Reports", "Payment Advice", () => OpenReport(ReportKind.PaymentAdvice));
        }
        if (company is { TdsEnabled: true })
        {
            Add("Reports", "TDS Outstandings", () => OpenReport(ReportKind.TdsOutstanding));
            Add("Reports", "TDS Not Deducted", () => OpenReport(ReportKind.TdsNotDeducted));
            Add("Reports", "TDS Interest", () => OpenReport(ReportKind.TdsInterest));
            Add("Reports", "TDS Nature Summary", () => OpenReport(ReportKind.TdsNatureSummary));
        }
        if (company is { TcsEnabled: true })
        {
            Add("Reports", "TCS Outstandings", () => OpenReport(ReportKind.TcsOutstanding));
            Add("Reports", "TCS Not Collected", () => OpenReport(ReportKind.TcsNotCollected));
            Add("Reports", "TCS Interest", () => OpenReport(ReportKind.TcsInterest));
            Add("Reports", "TCS Nature Summary", () => OpenReport(ReportKind.TcsNatureSummary));
        }
        if (company is { TdsEnabled: true } or { TcsEnabled: true })
            Add("Reports", "Ledgers without PAN", () => OpenReport(ReportKind.LedgersWithoutPan));

        // ---------------------------------------------------------------- Data
        Add("Data", "Backup Company", OpenBackupCompany, "Alt+Y");
        Add("Data", "Restore Company", OpenRestoreCompany, "Alt+Y");
        Add("Data", "Import", OpenImport, "O");
        Add("Data", "Export Data", OpenExportData, "Y");
        Add("Data", "SMTP Settings", OpenSmtpSettings);

        return index;
    }

    /// <summary>
    /// Applies a saved view (RQ-8): resolves its stable kind token to a Desktop <see cref="ReportKind"/>, opens a
    /// FRESH report of that kind as a page column, then re-applies the config so the projection recomputes — the
    /// on-screen figures are identical to configuring the same options by hand (ER-9; figures are never loaded).
    /// An unknown token (a view saved by a newer build) is ignored. Opening the report replaces the Saved-Views
    /// panel column (it is a page-open), so the report becomes the active pane with the applied view.
    /// </summary>
    public void ApplySavedView(SavedReportView view)
    {
        if (view is null || Company is null) return;
        if (ReportsViewModel.KindFor(view.ReportKind) is not { } kind) return; // token this build cannot map

        OpenReport(kind);
        Reports?.ApplySavedView(view);
    }

    // =============================================================== screen: Print Preview (RQ-9 / DP-8)

    /// <summary>
    /// P / Ctrl+P — opens the "Print Preview" of the CURRENT report (RQ-9) as its own cascading column to the
    /// RIGHT of the open report, never a stacked overlay, mirroring <see cref="OpenReportConfig"/>. The report
    /// stays live beneath the preview; the report's on-screen rows/config are projected into a de-branded PDF
    /// (via <c>Apex.Ledger.Io</c>) and shown paginated. A no-op unless a report is open; re-pressing while the
    /// preview is open is a no-op (there is already a preview column). All IO stays in the Io project (ER-12).
    /// </summary>
    public void OpenPrintPreview()
    {
        if (PrintPreview is not null) return;     // preview already open — don't stack a second one

        // On a drilled voucher (RQ-7 detail) Print renders THAT voucher — a GST tax invoice for a Sales
        // item-invoice (RQ-11), else the plain Dr/Cr voucher (RQ-10). Otherwise it prints the open report (RQ-9).
        PrintPreviewViewModel preview;
        if (CurrentScreen == Screen.VoucherDetail && VoucherDetail is { } vd)
            preview = vd.BuildPrintPreview();
        // A Payslip prints the dedicated de-branded PayslipPdf (RQ-16) — the same PDF pipeline as the tax invoice /
        // TDS certificates — rather than the generic report grid; a payslip with no employee/structure is a no-op.
        else if (Reports is { IsPayslipReport: true, CurrentPayslip: { } slip })
            preview = new PrintPreviewViewModel(slip, Reports.Title);
        else if (Reports is { IsPayslipReport: true })
            return;                               // payslip with no employee/structure — nothing to print
        else if (Reports is not null)
            preview = new PrintPreviewViewModel(Reports);
        else
            return;                               // nothing to print

        PrintPreview = preview;
        Columns.Add(new GatewayColumn(preview.Title, preview));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.PrintPreview;
        ScreenTitle = preview.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// Ctrl+A / the Save button on the Print-Preview panel: writes the rendered PDF bytes to <paramref name="path"/>
    /// (chosen by the Avalonia layer, or a temp path). The renderer never touches disk — this is the only place
    /// the bytes are written. A no-op when no preview is open. Returns whether the file was written.
    /// </summary>
    public bool SavePrintPreview(string path) => PrintPreview?.SavePdf(path) ?? false;

    /// <summary>
    /// F12 on an open voucher/invoice print-preview (RQ-12) — opens the print Configuration panel (title override,
    /// narration on/off, copy marking) as its own cascading column to the RIGHT of the preview, never a stacked
    /// overlay, mirroring <see cref="OpenReportConfig"/>. The preview stays live beneath; applying re-renders it in
    /// place. A no-op unless a config-capable preview (voucher/invoice) is open; re-pressing while the panel is
    /// open is a no-op (there is already a config column).
    /// </summary>
    public void OpenPrintConfig()
    {
        // W2-31 (census 12.4): the panel now opens over ANY preview, not just a voucher/invoice. The F8 format,
        // F9 paper, F5 copies and F10 range/starting-number knobs apply to every document kind — and a report is
        // the surface most prints come from, so gating the whole panel on the RQ-12 document knobs left the F8/F9/
        // F5/F10 half unreachable from the screen that needs it most. The document knobs themselves are still
        // voucher/invoice-only; the panel hides them via PrintConfigViewModel.SupportsDocumentKnobs.
        if (PrintPreview is not { } preview) return;              // nothing being previewed
        if (PrintConfigPanel is not null) return;                 // panel already open — don't stack

        var panel = new PrintConfigViewModel(preview);
        PrintConfigPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.PrintConfig;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Apply button on the print-config panel: push the knobs and re-render the preview.</summary>
    public void ApplyPrintConfig() => PrintConfigPanel?.Apply();

    // =============================================================== screen: export

    /// <summary>
    /// True on a screen the E / Alt+E Export action can act on: a live report OR a master-list screen
    /// (Chart of Accounts, the ledger-creation list, the stock-item-creation list; RQ-14/16, slice 13). Master
    /// lists project through <see cref="MasterListTabularProjector"/>; reports through
    /// <see cref="ReportTabularProjector"/>.
    /// </summary>
    public bool IsExportablePage =>
        IsReportContext
        || TopMasterExportSource() is not null;

    /// <summary>
    /// The master-list export source on top of the cascade, if any: the currently-displayed master-list page
    /// column whose VM implements <see cref="IMasterListExportSource"/> (Chart of Accounts, Ledgers, Stock
    /// Items, Groups, Cost Centres / Categories, Godowns, Units, Currencies, Scenarios, Budgets, Stock Groups /
    /// Categories, …). Generalises slice-13 export from the original three bespoke screens to EVERY master list
    /// (audit Fix 1). Returns <c>null</c> when the top column is not a master list.
    /// </summary>
    private IMasterListExportSource? TopMasterExportSource()
        => Columns.Count > 0 ? Columns[^1].Page as IMasterListExportSource : null;

    /// <summary>
    /// E / Alt+E (RQ-14/16) — opens the "Export" panel for the CURRENT report OR master list as its own
    /// cascading column to the RIGHT of the open page, never a stacked overlay, mirroring
    /// <see cref="OpenReportConfig"/>. The page stays live beneath; applying projects it into a
    /// <see cref="Apex.Ledger.Io.TabularExport"/> (money as exact Number cells) and writes the chosen
    /// CSV/XLSX/PDF via <c>Apex.Ledger.Io</c>. A no-op unless an exportable page is open; re-pressing while the
    /// panel is open is a no-op (there is already an export column). All IO stays in the Io project (ER-12).
    /// </summary>
    public void OpenExport()
    {
        if (ExportPanel is not null) return;   // panel already open — don't stack a second one

        var panel = BuildExportPanel();
        if (panel is null) return;             // nothing exportable on screen

        ExportPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.Export;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// Builds the export panel for whatever exportable page is on top: a report (rich CSV/XLSX/PDF via the
    /// report projectors) or a master list (Chart of Accounts / ledgers / stock items via
    /// <see cref="MasterListTabularProjector"/>, with a generic tabular PDF). Returns <c>null</c> when nothing
    /// on screen is exportable. The master-list branch is checked before the report branch so a master column
    /// on top of a stale <see cref="Reports"/> still exports the master list.
    /// </summary>
    private ExportViewModel? BuildExportPanel()
    {
        // Chart of Accounts keeps its bespoke tree projector (indented names + a group's nature).
        if (CurrentScreen == Screen.ChartOfAccounts && ChartOfAccounts is { } coa)
            return new ExportViewModel(coa.Title,
                () => MasterListTabularProjector.ProjectChartOfAccounts(coa),
                projectPrint: null, ExportDefaultFolder(), System.DateTime.Now, writeBytes: null);

        // Ledgers keeps its bespoke projector (it also splits the Dr/Cr side into its own column).
        if (CurrentScreen == Screen.LedgerMaster && LedgerMaster is { } lm)
            return new ExportViewModel("Ledgers",
                () => MasterListTabularProjector.ProjectLedgers(lm),
                projectPrint: null, ExportDefaultFolder(), System.DateTime.Now, writeBytes: null);

        // Stock Items keeps its bespoke projector (exact Opening-Value column).
        if (CurrentScreen == Screen.StockItemMaster && StockItemMaster is { } sim)
            return new ExportViewModel("Stock Items",
                () => MasterListTabularProjector.ProjectStockItems(sim),
                projectPrint: null, ExportDefaultFolder(), System.DateTime.Now, writeBytes: null);

        // EVERY other master-list screen (Groups, Cost Centres/Categories, Godowns, Units, Currencies,
        // Scenarios, Budgets, Stock Groups/Categories, …) exports uniformly through the GENERIC source path
        // (audit Fix 1): its VM implements IMasterListExportSource, so a snapshot of the on-screen grid becomes
        // a TabularExport with numeric columns as summable Number cells.
        if (TopMasterExportSource() is { } source)
        {
            var snapshotTitle = source.ToMasterListSnapshot().Title;
            return new ExportViewModel(snapshotTitle,
                () => MasterListTabularProjector.ProjectSource(source),
                projectPrint: null, ExportDefaultFolder(), System.DateTime.Now, writeBytes: null);
        }

        if (IsReportContext && Reports is { } report)
            return new ExportViewModel(report);

        return null;
    }

    /// <summary>The default export folder (the user's Documents), matching the report export ctor.</summary>
    private static string ExportDefaultFolder()
    {
        try { return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments); }
        catch { return string.Empty; }
    }

    /// <summary>Ctrl+A / the Export button on the export panel: project + write the chosen file. Returns success.</summary>
    public bool ApplyExport() => ExportPanel?.Apply() ?? false;

    // =============================================================== screen: e-mail compose (RQ-25/26)

    /// <summary>
    /// M / Ctrl+M — opens the "E-Mail" compose panel for the CURRENT report (RQ-25) or a drilled voucher / tax
    /// invoice (RQ-11 attachment), as its own cascading column to the RIGHT of the page, never a stacked overlay,
    /// mirroring <see cref="OpenExport"/>. The report/invoice stays live beneath; the attachment defaults to its
    /// exported PDF (rendered via <c>Apex.Ledger.Io</c>). The hand-off is OFFLINE (RQ-26) — Save writes a
    /// byte-stable <c>.eml</c> (carrying the attachment), or a <c>mailto:</c> opens the OS mail client for a quick
    /// body — <b>nothing is sent</b>; no socket/SMTP path exists. A no-op unless a report or voucher-detail is on
    /// screen; re-pressing while the panel is open is a no-op (there is already a compose column).
    /// </summary>
    public void OpenEmailCompose()
    {
        if (EmailCompose is not null) return;   // panel already open — don't stack a second one

        EmailComposeViewModel panel;
        if (CurrentScreen == Screen.VoucherDetail && VoucherDetail is { } vd)
            panel = new EmailComposeViewModel(vd);       // e-mail the drilled voucher / tax invoice
        else if (IsReportContext && Reports is { } r)
            panel = new EmailComposeViewModel(r);        // e-mail the open report
        else
            return;                                      // nothing to e-mail

        EmailCompose = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.EmailCompose;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Save button on the compose panel: write the byte-stable <c>.eml</c> (with the
    /// attachment) to <paramref name="path"/>. The composer never touches disk — this is the only write. A no-op
    /// when no compose panel is open. Returns whether the file was written. Nothing is sent.</summary>
    public bool SaveEmail(string path) => EmailCompose?.SaveEml(path) ?? false;

    // =============================================================== screen: SMTP settings (RQ-27)

    /// <summary>
    /// Opens the "SMTP Settings" panel (RQ-27) for the open company as its own cascading column, mirroring
    /// <see cref="OpenExport"/>. It captures the outgoing-mail server profile (host / port / TLS / from-address /
    /// from-name) and round-trips it through the per-company store. <b>No password is captured (R13)</b> and
    /// nothing is sent — the profile is for a later phase to wire live transport. A no-op unless a company is
    /// open; re-pressing while the panel is open is a no-op.
    /// </summary>
    public void OpenSmtpSettings()
    {
        if (SmtpSettings is not null) return;   // panel already open — don't stack a second one
        if (Company is null) return;            // no company — nothing to configure

        var panel = new SmtpSettingsViewModel(_storage, Company);
        SmtpSettings = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.SmtpSettings;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Save button on the SMTP settings panel: upsert the captured profile. Returns success.</summary>
    public bool SaveSmtpSettings() => SmtpSettings?.Save() ?? false;

    // =============================================================== screen: export data (canonical backup)

    /// <summary>
    /// Y (Gateway → Export Data; RQ-19/DP-4) — opens the "Export Data" panel that serialises the WHOLE open company
    /// (masters + vouchers, money as integer paisa, deterministic order) to a canonical JSON/XML backup, as its own
    /// cascading column to the RIGHT of the Gateway. This complements the report/master-list export (E); it exports
    /// the entire company so it can be re-imported into a fresh company and reconcile to the paisa (PR-4). A no-op
    /// unless a company is open; re-pressing while the panel is open is a no-op (there is already one column).
    /// </summary>
    public void OpenExportData()
    {
        if (ExportDataPanel is not null) return;   // panel already open — don't stack a second one
        if (Company is null) return;               // nothing to export

        var panel = new ExportDataViewModel(Company);
        ExportDataPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.ExportData;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Export button on the Export-Data panel: serialise + write the canonical file.</summary>
    public bool ApplyExportData() => ExportDataPanel?.Apply() ?? false;

    // =============================================================== screen: import data

    /// <summary>
    /// O / Alt+O (Gateway → Import; RQ-20..24) — opens the "Import" panel that reads a canonical JSON/XML backup (or
    /// a flat CSV) and applies it INTO the open company through the engine-routed <see cref="ImportDataViewModel"/>
    /// (validate-before-apply, transactional, engine-routed). Opens as its own cascading column to the RIGHT of the
    /// Gateway. A no-op unless a company is open; re-pressing while the panel is open is a no-op.
    /// </summary>
    public void OpenImport()
    {
        if (ImportDataPanel is not null) return;   // panel already open — don't stack a second one
        if (Company is null) return;               // nothing to import into

        // The open Company aggregate is mutated in place by the import (and persisted by the panel), so any report
        // opened afterwards reads the fresh figures. We refresh the button bar but keep the panel open so its
        // success summary stays visible; the user steps back (Esc) to the Gateway when done.
        var panel = new ImportDataViewModel(Company, _storage, onImported: BuildButtonBar);
        ImportDataPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.ImportData;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Import button on the Import panel: read + parse + engine-routed apply. Returns success.</summary>
    public bool ApplyImport() => ImportDataPanel?.Apply() ?? false;

    // =============================================================== screen: backup / restore company (R-7)

    /// <summary>
    /// Gateway → Data → Backup / Restore → <b>Backup Company</b>: opens the panel that writes a consistent,
    /// version-stamped snapshot of the open company's DATABASE to a single <c>.apexbak</c> archive. A no-op
    /// unless a company is open; re-opening while the panel is up is a no-op (one column, not a stack).
    /// </summary>
    public void OpenBackupCompany()
    {
        if (BackupCompanyPanel is not null) return;
        if (Company is null) return;

        var panel = new BackupCompanyViewModel(Company, _storage);
        BackupCompanyPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.BackupCompany;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Ctrl+A / the Backup button on the Backup panel: take the snapshot. Returns success.</summary>
    public bool ApplyBackup() => BackupCompanyPanel?.Apply() ?? false;

    /// <summary>
    /// Opens the "Data → Backup / Restore" submenu column directly (Alt+Y, and the button-bar quick button).
    /// Rebuilds the cascade to [root → Backup / Restore] and focuses the submenu, exactly as drilling would.
    /// </summary>
    public void ShowDataMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Backup / Restore");
        OpenSubmenuColumn(BuildDataColumn(), GatewayMenu.Data,
            "Gateway of Apex Solutions — Backup / Restore");
    }

    /// <summary>
    /// Gateway → Data → Backup / Restore → <b>Restore Company</b>: opens the panel that puts an archive back over
    /// a company's database. The panel is two-step (Examine, then a confirmed Restore) — restore is the one
    /// genuinely destructive operation here (NFR-8). A no-op unless a company is open.
    /// </summary>
    public void OpenRestoreCompany()
    {
        if (RestoreCompanyPanel is not null) return;
        if (Company is null) return;

        var panel = new RestoreCompanyViewModel(Company, _storage, onRestored: ReopenRestoredCompany);
        RestoreCompanyPanel = panel;
        Columns.Add(new GatewayColumn(panel.Title, panel));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.RestoreCompany;
        ScreenTitle = panel.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    // =============================================================== the file / folder chooser (census 13.10, T1-20)

    /// <summary>
    /// What the operator should be asked to point at, for whichever path-carrying screen is open — or
    /// <c>null</c> when the current screen carries no data path, which makes the browse chord a safe no-op
    /// everywhere else.
    ///
    /// <para><b>Why the decision lives here and not in the view.</b> Which screen needs a folder and which needs
    /// a file is product behaviour, not shell plumbing: the backup destination and the export destination are
    /// FOLDERS (the file name is composed from the company/report name and a timestamp on the panel itself),
    /// while the restore source and the import source are existing FILES, and the <c>.eml</c> hand-off and the
    /// print-preview PDF are files being SAVED. Putting that in the view model is what lets a headless test
    /// prove we ask the operating system for the right shape of thing.</para>
    ///
    /// <para><b>Divergence, labelled as ours (R7 silence rule).</b> The vendor documents that these paths are
    /// configurable and that a backup path is set from the Data menu, but does not attest a browse control or a
    /// chord for one on these screens. The chord <b>Alt+B</b> and the wording below are OURS. Ctrl+B was not
    /// available and must not be taken — it is the vendor's "Basis of Values" and is reserved unbound.</para>
    /// </summary>
    public FilePathPickRequest? BrowseRequest() => CurrentScreen switch
    {
        Screen.BackupCompany when BackupCompanyPanel is { } backup =>
            FilePathPickRequest.Folder("Choose the folder to save the backup into", backup.Folder),

        Screen.RestoreCompany when RestoreCompanyPanel is { } restore =>
            FilePathPickRequest.OpenFile("Choose the backup archive to restore from",
                FolderOf(restore.FilePath),
                new FilePathFileType("Apex Solutions backup",
                    new[] { "*" + Apex.Persistence.Sqlite.CompanyBackup.ArchiveExtension }),
                AnyFile),

        Screen.ImportData when ImportDataPanel is { } import =>
            FilePathPickRequest.OpenFile("Choose the file to import",
                FolderOf(import.FilePath),
                ImportFileType(import.Format),
                AnyFile),

        Screen.ExportData when ExportDataPanel is { } exportData =>
            FilePathPickRequest.Folder("Choose the folder to export into", exportData.Folder),

        Screen.Export when ExportPanel is { } export =>
            FilePathPickRequest.Folder("Choose the folder to export into", export.Folder),

        Screen.EmailCompose when EmailCompose is { } email =>
            FilePathPickRequest.SaveFile("Save the e-mail message as", string.Empty,
                SafePathStem(email.DocumentTitle) + ".eml",
                new FilePathFileType("E-mail message", new[] { "*.eml" })),

        Screen.PrintPreview when PrintPreview is { } preview =>
            FilePathPickRequest.SaveFile("Save the PDF as", string.Empty,
                SafePathStem(preview.ReportTitle) + ".pdf",
                new FilePathFileType("PDF document", new[] { "*.pdf" })),

        _ => null,
    };

    private static readonly FilePathFileType AnyFile = new("All files", new[] { "*" });

    private static FilePathFileType ImportFileType(ImportDataFormat format) => format switch
    {
        ImportDataFormat.Xml => new FilePathFileType("Canonical XML", new[] { "*.xml" }),
        ImportDataFormat.Csv => new FilePathFileType("Comma-separated values", new[] { "*.csv" }),
        _ => new FilePathFileType("Canonical JSON", new[] { "*.json" }),
    };

    /// <summary>The folder a typed path already points into, so the dialog opens where the operator already is.</summary>
    private static string FolderOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return System.IO.Path.GetDirectoryName(path) ?? string.Empty; }
        catch (ArgumentException) { return string.Empty; }   // a malformed typed path just means "no start folder"
    }

    /// <summary>Turns a document title into a safe file-name stem (invalid path chars → '_'; blank → "Apex").</summary>
    private static string SafePathStem(string? title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Apex" : title.Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }

    /// <summary>
    /// Takes the operator's answer from the chooser and puts it where the open screen keeps its path.
    ///
    /// <para><b><c>null</c> means the dialog was cancelled and MUST change nothing</b> — the typed value stays
    /// exactly as it was. That is not a nicety: on the Restore panel the path in the box is the archive that is
    /// about to overwrite a whole company, and a cancelled dialog silently blanking it (or worse, half-setting
    /// it) would be a data-loss trap of exactly the kind this feature exists to remove.</para>
    ///
    /// <para>On the two SAVE screens the answer is a destination for bytes that are already rendered, so applying
    /// it writes the file there and then — that is what "Save as" means. Everywhere else it fills a field and the
    /// operator still presses the screen's own accept key.</para>
    ///
    /// <para>Returns true when a path was applied.</para>
    /// </summary>
    public bool ApplyBrowsedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        switch (CurrentScreen)
        {
            case Screen.BackupCompany when BackupCompanyPanel is { } backup:
                backup.Folder = path;
                return true;

            case Screen.RestoreCompany when RestoreCompanyPanel is { } restore:
                restore.FilePath = path;
                return true;

            case Screen.ImportData when ImportDataPanel is { } import:
                import.FilePath = path;
                return true;

            case Screen.ExportData when ExportDataPanel is { } exportData:
                exportData.Folder = path;
                return true;

            case Screen.Export when ExportPanel is { } export:
                export.Folder = path;
                return true;

            case Screen.EmailCompose:
                return SaveEmail(path);

            case Screen.PrintPreview:
                return SavePrintPreview(path);

            default:
                return false;
        }
    }

    /// <summary>Ctrl+E / the Examine button on the Restore panel: read the manifest, arm nothing. Returns success.</summary>
    public bool ExamineRestore() => RestoreCompanyPanel?.Examine() ?? false;

    /// <summary>Ctrl+A / the Restore button on the Restore panel: replace the company database. Returns success.</summary>
    public bool ApplyRestore() => RestoreCompanyPanel?.Apply() ?? false;

    /// <summary>
    /// After a successful restore the in-memory aggregate is the one that was just REPLACED, so every report and
    /// every button-bar gate would still be answering from the overwritten data. Swap in the reloaded company and
    /// refresh the derived shell state, keeping the panel visible so its success line stays readable.
    /// </summary>
    private void ReopenRestoredCompany(Company restored)
    {
        Company = restored;
        StatusCompany = restored.Name;
        StatusDate = ApexDate.Format(restored.FinancialYearStart);
        Message = $"Restored '{restored.Name}' from backup.";
        BuildButtonBar();
    }

    // =============================================================== screen: voucher entry

    /// <summary>
    /// Opens the reusable voucher-entry screen for the given base type as a page column on the right of
    /// the cascade, resolving the seeded voucher type on the current company.
    /// <para>WI-12: the optional <paramref name="date"/> seeds the new voucher's date (used by the Day-Book
    /// Alt+A "Add Voucher" flow so the entry lands on the highlighted row's date); when null the entry keeps its
    /// own default (last voucher date, else books-begin). The optional <paramref name="onSaved"/> overrides the
    /// post-save action — defaulting to <see cref="ShowGateway"/> so every existing single-argument call site is
    /// byte-identical — letting the Day-Book flow return to a REFRESHED Day Book instead of the Gateway.</para>
    /// </summary>
    public void OpenVoucher(VoucherBaseType baseType, DateOnly? date = null, Action? onSaved = null)
    {
        if (Company is null) return;

        // Resolve by RULE (active only, seeded series first, never a specialised variant) — not by "whatever came
        // first, and if nothing is active open a deactivated one anyway". See VoucherTypeResolver for why that
        // shape was wrong three ways.
        var type = VoucherTypeResolver.ResolveForEntry(Company, baseType);
        if (type is null)
        {
            Message = VoucherTypeResolver.NoActiveTypeMessage(Company, baseType);
            return;
        }

        OpenVoucher(type, date, onSaved);
    }

    /// <summary>
    /// Opens the voucher-entry screen for an EXACT voucher type — the identity-preserving overload. A caller that
    /// knows which series the operator chose (the Day-Book Alt+A picker row, a report drill-down) must come
    /// through here: resolving that choice back down to its base kind would silently substitute a different
    /// series, with a different name and a different number sequence.
    /// </summary>
    public void OpenVoucher(VoucherType type, DateOnly? date = null, Action? onSaved = null)
    {
        if (Company is null) return;
        ArgumentNullException.ThrowIfNull(type);

        var entry = new VoucherEntryViewModel(
            Company, type, _storage,
            onSaved: onSaved ?? ShowGateway,
            onCancelled: BackFromPage,
            date: date);
        // G-5 (BOOK pp.130–132): a batch-tracked line on a Purchase/Sales ITEM INVOICE opens the same real
        // batch-allocation sub-screen the stock screens use — as a cascade column to the right, so the invoice
        // stays beneath it and comes back intact on Esc. Wired here (not inside the entry VM) because the shell
        // owns the cascade, exactly as OpenInventoryVoucher does.
        entry.BatchAllocationRequested += (item, godown, qty, isOutward, onCommitted) =>
            ShowBatchAllocation(item, godown, qty, isOutward, onCommitted);
        var title = $"Accounting Voucher Creation — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.VoucherEntry, title,
            () => VoucherEntry = entry);
    }

    // =============================================================== screen: inventory voucher entry

    /// <summary>
    /// Opens the reusable stock/order voucher-entry screen for the given inventory base type (Purchase Order,
    /// Sales Order, Receipt Note/GRN, Delivery Note, Rejection In/Out, Stock Journal, Physical Stock) as a
    /// page column on the right of the cascade, resolving the seeded voucher type on the current company. The
    /// screen posts to the separate <see cref="InventoryVoucher"/> aggregate via
    /// <see cref="InventoryPostingService"/> — no Dr/Cr balancing.
    /// </summary>
    public void OpenInventoryVoucher(VoucherBaseType baseType, DateOnly? date = null, Action? onSaved = null)
    {
        if (Company is null) return;

        if (!VoucherEffects.IsInventoryBaseType(baseType))
        {
            Message = $"'{baseType}' is not a stock or order voucher.";
            return;
        }

        var type = VoucherTypeResolver.ResolveForEntry(Company, baseType);
        if (type is null)
        {
            Message = VoucherTypeResolver.NoActiveTypeMessage(Company, baseType);
            return;
        }

        OpenInventoryVoucher(type, date, onSaved);
    }

    /// <summary>
    /// Opens the stock/order voucher-entry screen for an EXACT voucher type — the identity-preserving overload
    /// (see <see cref="OpenVoucher(VoucherType, DateOnly?, Action?)"/> for why base-kind resolution is not good
    /// enough for a caller that already knows the series).
    /// </summary>
    public void OpenInventoryVoucher(VoucherType type, DateOnly? date = null, Action? onSaved = null)
    {
        if (Company is null) return;
        ArgumentNullException.ThrowIfNull(type);

        var entry = new InventoryVoucherEntryViewModel(
            Company, type, _storage,
            onSaved: onSaved ?? ShowGateway,
            onCancelled: BackFromPage,
            date: date);
        // RQ-3: a batch-tracked line opens the batch-allocation sub-screen as a cascade column to the right.
        entry.BatchAllocationRequested += (item, godown, qty, isOutward, onCommitted) =>
            ShowBatchAllocation(item, godown, qty, isOutward, onCommitted);
        var title = $"Inventory Voucher Creation — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.InventoryVoucherEntry, title,
            () => InventoryVoucherEntry = entry);
    }

    // =============================================================== screen: WI-12 Day-Book "Add Voucher" picker

    /// <summary>
    /// WI-12 — Alt+A on the Day Book: opens a voucher-type PICKER listing EVERY active voucher type ("Entering any
    /// type of voucher … from the day book"; Book p.431 "Alt+A → Add a voucher in a report"). The picker is a menu
    /// column appended to the RIGHT of the live Day Book, mirroring <see cref="OpenReportConfig"/>: it does NOT
    /// <see cref="ClearSubScreens"/>, so <see cref="Reports"/> stays bound beneath it (the report is NOT destroyed)
    /// and Esc/Back pops the picker straight back to the same live Day Book. Picking a type opens that voucher over
    /// the Day Book and, on save, returns to a REFRESHED Day Book so the new entry is visible. A no-op unless the
    /// live report is the Day Book. Reusing the cascade's own menu column (not a bespoke page/DataTemplate) keeps
    /// the arrow/Enter navigation and rendering identical to every other submenu — and adds no new layout surface.
    /// </summary>
    public void OpenAddVoucherFromReport()
    {
        if (Company is null || !IsDayBookReport) return;
        if (CurrentScreen == Screen.AddVoucherPicker) return; // already open — don't stack a second picker

        // Seed the new voucher's date from the highlighted Day-Book row (its own voucher's date); resolve it NOW
        // while the report is still bound, before the picker column takes focus.
        var seedDate = ResolveAddVoucherSeedDate();

        // WI-2/WI-9 conflict rule: these rows are the COMPANY'S voucher types (Company.VoucherTypes), including
        // any the user created — not an authored menu. A computed hotkey over user data would paint an arbitrary
        // mid-word red letter on a name nobody at build time has seen, so a bare letter FILTERS here instead.
        var picker = new GatewayColumn("Add Voucher") { Kind = GatewayColumnKind.DataDriven };
        picker.Add(MenuItemViewModel.Header("Select Voucher Type"));
        foreach (var type in Company.VoucherTypes.Where(t => t.IsActive && CanAddFromDayBook(t)))
        {
            // Capture the TYPE, not its base kind: these rows are the company's own voucher types, and two of
            // them can share a base (a second Sales series, a Manufacturing Journal over Stock Journal, a POS
            // till). Passing the base kind sent the choice through resolution again and opened a DIFFERENT type
            // than the row the operator was standing on.
            var chosen = type;
            picker.Add(new MenuItemViewModel(
                type.Name,
                () => PickAddVoucherType(chosen, seedDate),
                type.DefaultShortcut ?? string.Empty,
                isSubItem: true,
                kind: MenuItemKind.Action));
        }

        // Append WITHOUT ClearSubScreens/OpenPageColumn — the Day Book page column survives beneath (Reports stays
        // bound), exactly like the F12 config column sits beside its live report.
        Columns.Add(picker);
        picker.SelectFirstSelectable();
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.AddVoucherPicker;
        ScreenTitle = "Add Voucher";
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// WI-12 — whether a voucher type can be offered as something to ADD from the Day Book, i.e. whether choosing
    /// it actually produces a voucher on the screen the operator is looking at.
    /// <list type="bullet">
    /// <item><b>Attendance</b> never can. Nothing in the product posts a <c>Voucher</c> of that base kind — the
    /// Attendance / Production screen writes <c>AttendanceEntry</c> rows — so offering it would advertise an entry
    /// that cannot be made. The seed row is gone (see <c>SeedVoucherTypes</c>), but a company created before that
    /// still carries a stored Attendance row that a data migration has not removed, so the guard stays.</item>
    /// <item>A <b>Manufacturing Journal</b> type is offered only while the F12 "Set Components (BOM)" config is
    /// on, exactly like its menu row — its screen is gated on that config and would silently not open.</item>
    /// </list>
    /// </summary>
    private bool CanAddFromDayBook(VoucherType type) =>
        type.BaseType != VoucherBaseType.Attendance
        && (!type.IsManufacturingJournal || Company is { SetComponentsBom: true });

    /// <summary>
    /// WI-12 — a voucher type was chosen in the Day-Book Alt+A picker. Pops the picker column (so the entry opens in
    /// the Day Book's place — one page column, honouring the cascade invariant) and opens THAT TYPE's entry seeded
    /// with <paramref name="seedDate"/>, wiring the post-save action to re-run the Day Book so the new voucher
    /// appears. Routes inventory/order kinds to the inventory entry and the Job-Work / Material / Payroll /
    /// Manufacturing-Journal / POS kinds to their own dedicated screens (those carry no date/refresh override,
    /// matching their existing menu route).
    /// <para>The parameter is the chosen <see cref="VoucherType"/>, not its base kind: two types can share a base
    /// (a second Sales series; a Manufacturing Journal over Stock Journal; a POS Sales type), and re-resolving the
    /// base opened whichever one the resolver preferred rather than the row the operator picked.</para>
    /// </summary>
    private void PickAddVoucherType(VoucherType type, DateOnly? seedDate)
    {
        // Drop the picker menu column so OpenPageColumn's trim leaves exactly one page column (the new voucher,
        // in the Day Book's place). Without this the picker (a menu column) would survive the trim.
        if (CurrentScreen == Screen.AddVoucherPicker && Columns.Count > 0)
            Columns.RemoveAt(Columns.Count - 1);

        // On save, return to a freshly-built Day Book (its projection now includes the just-posted voucher).
        Action refreshDayBook = () => OpenReport(ReportKind.DayBook);

        // Types whose identity means a DIFFERENT SCREEN, not just a different series — checked before the base
        // switch, because each of these shares its base kind with an ordinary type.
        if (type.IsManufacturingJournal) { OpenManufacturingJournal(); return; }
        if (type.IsPosSales) { OpenPosBilling(); return; }

        switch (type.BaseType)
        {
            case VoucherBaseType.JobWorkInOrder: OpenJobWorkOrder(JobWorkDirection.In); break;
            case VoucherBaseType.JobWorkOutOrder: OpenJobWorkOrder(JobWorkDirection.Out); break;
            case VoucherBaseType.MaterialIn: OpenMaterialMovement(VoucherBaseType.MaterialIn); break;
            case VoucherBaseType.MaterialOut: OpenMaterialMovement(VoucherBaseType.MaterialOut); break;
            // A Payroll voucher is computed on its own screen (period + employees + Compute), never keyed as a
            // bare Dr/Cr grid — which is what routing it through the accounting entry would have given.
            case VoucherBaseType.Payroll: ShowPayrollVoucher(); break;
            default:
                if (VoucherEffects.IsInventoryBaseType(type.BaseType))
                    OpenInventoryVoucher(type, seedDate, refreshDayBook);
                else
                    OpenVoucher(type, seedDate, refreshDayBook);
                break;
        }
    }

    /// <summary>
    /// WI-12 — the date the Day-Book Alt+A entry defaults to: the highlighted Day-Book row's own voucher date (so an
    /// added voucher lands in the visible period, beside the row the user was on). Falls back to null when no
    /// drillable row is highlighted — the entry VM then keeps its default (last voucher date, else books-begin).
    /// </summary>
    private DateOnly? ResolveAddVoucherSeedDate()
    {
        var row = Reports?.SelectedRow;
        if (row is not null && row.DrillVoucherId != Guid.Empty)
            return Company?.FindVoucher(row.DrillVoucherId)?.Date;
        return null;
    }

    // =============================================================== screen: ledger master

    /// <summary>Opens the Ledger-creation master (Create → Ledger / Alt+C) as a page column.</summary>
    public void ShowLedgerMaster()
    {
        if (Company is null) return;

        var master = new LedgerMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Ledger Creation", master), Screen.LedgerMaster,
            "Ledger Creation", () => LedgerMaster = master);
    }

    /// <summary>
    /// Opens the accounting-Group creation master (Masters → Create → Group; WI-7) as a page column: create a
    /// custom group (e.g. "Salary Payable") under a chosen parent, with the nature derived read-only from that
    /// parent. This is what "Create → Group" opens — it previously mis-routed to Ledger Creation.
    /// </summary>
    public void ShowAccountGroupMaster()
    {
        if (Company is null) return;

        var master = new AccountGroupMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Group Creation", master), Screen.AccountGroupMaster,
            "Group Creation", () => AccountGroupMaster = master);
    }

    // =============================================================== screen: chart of accounts

    /// <summary>
    /// Opens the Ledger master in <b>Alter</b> mode over an existing ledger (WI-3) — the same form as Create,
    /// pre-filled, saving against the ledger's stable Guid so a rename applies retroactively to all history.
    /// Reached by Enter on a ledger row of the Chart of Accounts. A no-op if the id does not resolve.
    /// </summary>
    /// <summary>
    /// WI-3 — Enter on the Chart of Accounts: opens the highlighted row's master for <b>alteration</b>. A ledger
    /// row opens Ledger Alteration; a group row opens Group Alteration. A no-op when nothing is highlighted, so
    /// Enter on an untouched tree does nothing rather than opening an arbitrary account.
    /// </summary>
    public void AlterHighlightedChartRow()
    {
        if (ChartOfAccounts?.HighlightedRow is not { } row) return;

        if (row.LedgerId is { } ledgerId) ShowLedgerAlter(ledgerId);
        else if (row.GroupId is { } groupId) ShowAccountGroupAlter(groupId);
    }

    public void ShowLedgerAlter(Guid ledgerId)
    {
        if (Company is null) return;

        // Capture the tree INSTANCE, not the property: OpenPageColumn below runs ClearSubScreens, which nulls
        // ChartOfAccounts, so a `() => ChartOfAccounts?.Refresh()` closure would silently never fire and the tree
        // would keep showing the OLD name — which reads as a failed save.
        var tree = ChartOfAccounts;
        var master = LedgerMasterViewModel.ForAlter(
            Company, _storage, ledgerId, onChanged: () => tree?.Refresh());
        if (master is null) return;

        OpenPageColumn(new GatewayColumn("Ledger Alteration", master), Screen.LedgerMaster,
            "Ledger Alteration", () => LedgerMaster = master);
    }

    /// <summary>
    /// Opens the accounting-Group master in <b>Alter</b> mode over an existing group (WI-3): rename, re-alias or
    /// re-parent, with the nature re-derived and cascaded to every descendant. Reached by Enter on a group row of
    /// the Chart of Accounts. A no-op if the id does not resolve.
    /// </summary>
    public void ShowAccountGroupAlter(Guid groupId)
    {
        if (Company is null) return;

        var tree = ChartOfAccounts;   // captured for the same reason as ShowLedgerAlter — see the note there.
        var master = AccountGroupMasterViewModel.ForAlter(
            Company, _storage, groupId, onChanged: () => tree?.Refresh());
        if (master is null) return;

        OpenPageColumn(new GatewayColumn("Group Alteration", master), Screen.AccountGroupMaster,
            "Group Alteration", () => AccountGroupMaster = master);
    }

    /// <summary>
    /// Opens the Chart of Accounts (Masters → Chart of Accounts) as a page column: the group hierarchy with
    /// sub-groups nested/indented under their primary parent and ledgers under their group.
    /// <para>WI-3: the tree is no longer read-only. Up/Down move a row highlight and Enter drills into the
    /// highlighted master's <b>Alteration</b> screen — a ledger row opens Ledger Alteration, a group row opens
    /// Group Alteration. This is the entry point CA audit point 5 asked for ("Editing in Ledger should be allowed
    /// in the chart of accounts after the creation of Ledger … keyboard logic for the chart of accounts").</para>
    /// </summary>
    public void ShowChartOfAccounts()
    {
        if (Company is null) return;

        var chart = new ChartOfAccountsViewModel(Company);
        OpenPageColumn(new GatewayColumn("Chart of Accounts", chart), Screen.ChartOfAccounts,
            "Chart of Accounts", () => ChartOfAccounts = chart);
    }

    // =============================================================== screen: cost masters

    /// <summary>Opens the Cost-Category creation master (Masters → Create → Cost Category) as a page column.</summary>
    public void ShowCostCategoryMaster()
    {
        if (Company is null) return;

        var master = new CostCategoryMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Cost Category Creation", master), Screen.CostCategoryMaster,
            "Cost Category Creation", () => CostCategoryMaster = master);
    }

    /// <summary>Opens the Cost-Centre creation master (Masters → Create → Cost Centre) as a page column.</summary>
    public void ShowCostCentreMaster()
    {
        if (Company is null) return;

        var master = new CostCentreMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Cost Centre Creation", master), Screen.CostCentreMaster,
            "Cost Centre Creation", () => CostCentreMaster = master);
    }

    /// <summary>Opens the Budget creation master (Masters → Create → Budget) as a page column.</summary>
    public void ShowBudgetMaster()
    {
        if (Company is null) return;

        var master = new BudgetMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Budget Creation", master), Screen.BudgetMaster,
            "Budget Creation", () => BudgetMaster = master);
    }

    /// <summary>Opens the Scenario creation master (Masters → Create → Scenario) as a page column.</summary>
    public void ShowScenarioMaster()
    {
        if (Company is null) return;

        var master = new ScenarioMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Scenario Creation", master), Screen.ScenarioMaster,
            "Scenario Creation", () => ScenarioMaster = master);
    }

    /// <summary>
    /// Opens the Currency creation master (Masters → Create → Currency) as a page column: create a foreign
    /// <b>Currency</b> (symbol / formal name / decimals) and dated <b>Rates of Exchange</b> (standard /
    /// selling / buying) for it, both persisted (catalog §2/§20 Multi-currency).
    /// </summary>
    public void ShowCurrencyMaster()
    {
        if (Company is null) return;

        var master = new CurrencyMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Currency Creation", master), Screen.CurrencyMaster,
            "Currency Creation", () => CurrencyMaster = master);
    }

    // =============================================================== screen: inventory masters

    /// <summary>Opens the Stock-Group creation master (Masters → Create → Inventory Masters → Stock Group).</summary>
    public void ShowStockGroupMaster()
    {
        if (Company is null) return;

        var master = new StockGroupMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Stock Group Creation", master), Screen.StockGroupMaster,
            "Stock Group Creation", () => StockGroupMaster = master);
    }

    /// <summary>Opens the Stock-Category creation master (Masters → Create → Inventory Masters → Stock Category).</summary>
    public void ShowStockCategoryMaster()
    {
        if (Company is null) return;

        var master = new StockCategoryMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Stock Category Creation", master), Screen.StockCategoryMaster,
            "Stock Category Creation", () => StockCategoryMaster = master);
    }

    /// <summary>Opens the Unit-of-Measure creation master (Masters → Create → Inventory Masters → Unit).</summary>
    public void ShowUnitMaster()
    {
        if (Company is null) return;

        var master = new UnitMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Unit Creation", master), Screen.UnitMaster,
            "Unit Creation", () => UnitMaster = master);
    }

    /// <summary>Opens the Godown creation master (Masters → Create → Inventory Masters → Godown).</summary>
    public void ShowGodownMaster()
    {
        if (Company is null) return;

        var master = new GodownMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Godown Creation", master), Screen.GodownMaster,
            "Godown Creation", () => GodownMaster = master);
    }

    /// <summary>Opens the Stock-Item creation master (Masters → Create → Inventory Masters → Stock Item).</summary>
    /// <summary>
    /// WI-3 — Ctrl+Enter on the Stock Item master's <b>existing-items</b> list: opens the highlighted item for
    /// <b>alteration</b>. Returns false (and does nothing) on any other screen, or when no row is highlighted, so
    /// the key stays free everywhere else.
    ///
    /// <para>This is the ENTRY POINT <see cref="StockItemMasterViewModel.ForAlter"/> was missing. The Chart of
    /// Accounts is an accounts surface — its rows carry a LedgerId or a GroupId and nothing else — so the natural
    /// home for altering an inventory master is the inventory master's own list of what already exists, which is
    /// the surface the operator is already looking at after creating an item.</para>
    /// </summary>
    public bool AlterHighlightedStockItemRow()
    {
        if (!IsStockItemMasterScreen) return false;
        if (StockItemMaster!.HighlightedRow is not { } row) return false;

        ShowStockItemAlter(row.StockItemId);
        return true;
    }

    /// <summary>
    /// Opens the Stock Item master in <b>Alter</b> mode over an existing item (WI-3): the same form pre-filled,
    /// saving against the item's stable Guid so a rename follows every historical inventory entry. A no-op if the
    /// id does not resolve.
    /// </summary>
    public void ShowStockItemAlter(Guid itemId)
    {
        if (Company is null) return;

        var master = StockItemMasterViewModel.ForAlter(Company, _storage, itemId, onChanged: () => { });
        if (master is null) return;

        OpenPageColumn(new GatewayColumn("Stock Item Alteration", master), Screen.StockItemMaster,
            "Stock Item Alteration", () => StockItemMaster = master);
    }

    public void ShowStockItemMaster()
    {
        if (Company is null) return;

        var master = new StockItemMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Stock Item Creation", master), Screen.StockItemMaster,
            "Stock Item Creation", () => StockItemMaster = master);
    }

    /// <summary>
    /// Opens the Batch / Lot creation master (Masters → Create → Inventory Masters → Batch; Phase 6 Cluster 1)
    /// as a page column. A no-op unless the company flag "Maintain Batch-wise details" is on (RQ-52), so the
    /// screen can never be reached on a non-batch company.
    /// </summary>
    public void ShowBatchMaster()
    {
        if (Company is null) return;
        if (!Company.MaintainBatchwiseDetails) return;   // gated by the F11 company flag (RQ-52)

        var master = new BatchMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Batch Creation", master), Screen.BatchMaster,
            "Batch / Lot Creation", () => BatchMaster = master);
    }

    /// <summary>
    /// Opens the batch-allocation sub-screen (Phase 6 Cluster 1; RQ-3) for an inventory-voucher line as a page
    /// column to the right of the voucher screen. Called after item + godown + qty are known on a line whose
    /// item Maintains-in-Batches. The sub-screen defaults its selection via the engine's FEFO/FIFO
    /// <see cref="Apex.Ledger.Services.BatchStockService.DefaultIssueSelection"/> for an outward line and warns
    /// (never blocks) on an expired/near-expiry batch. A no-op unless the company flag is on.
    /// </summary>
    public void ShowBatchAllocation(
        StockItem item, Godown godown, decimal quantity, bool isOutward,
        Action<System.Collections.Generic.IReadOnlyList<BatchAllocation>>? onCommitted = null)
    {
        if (Company is null || item is null || godown is null) return;
        if (!Company.MaintainBatchwiseDetails || !item.MaintainInBatches) return;

        var asOf = AccountBooksAsOf();
        var sub = new BatchAllocationViewModel(Company, item, godown, quantity, asOf, isOutward,
            onCommitted: onCommitted);
        // The sub-screen sits to the RIGHT of the live voucher column (do NOT trim the voucher page): push it as
        // its own cascading column, mirroring the F12-panel-over-report pattern, so the voucher stays beneath.
        ClearSubScreens();
        BatchAllocation = sub;
        Columns.Add(new GatewayColumn(sub.Title, sub));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.BatchAllocation;
        ScreenTitle = sub.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// Opens the Bill-of-Materials creation master (Masters → Create → Inventory Masters → Bill of Materials;
    /// Phase 6 Cluster 2; RQ-9) as a page column. A no-op unless the F12 config "Set Components (BOM)" is on
    /// (RQ-10/RQ-52), so the screen can never be reached on a non-BOM company.
    /// </summary>
    public void ShowBomMaster()
    {
        if (Company is null) return;
        if (!Company.SetComponentsBom) return;   // gated by the F12 config (RQ-10/RQ-52)

        var master = new BomMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Bill of Materials Creation", master), Screen.BomMaster,
            "Bill of Materials Creation", () => BomMaster = master);
    }

    /// <summary>
    /// Opens the Price Level creation master (Masters → Create → Inventory Masters → Price Level; Phase 6 slice 5;
    /// RQ-26) as a page column. A no-op unless the F11 flag "Enable multiple Price Levels" is on (RQ-52), so the
    /// screen can never be reached on a non-price-level company.
    /// </summary>
    public void ShowPriceLevelsMaster()
    {
        if (Company is null) return;
        if (!Company.EnableMultiplePriceLevels) return;   // gated by the F11 company flag (RQ-52)

        var master = new PriceLevelsViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Price Level Creation", master), Screen.PriceLevelsMaster,
            "Price Level Creation", () => PriceLevels = master);
    }

    /// <summary>
    /// Opens the Price List creation master (Masters → Create → Inventory Masters → Price List; Phase 6 slice 5;
    /// RQ-27) as a page column. A no-op unless the F11 flag "Enable multiple Price Levels" is on (RQ-52).
    /// </summary>
    public void ShowPriceListsMaster()
    {
        if (Company is null) return;
        if (!Company.EnableMultiplePriceLevels) return;   // gated by the F11 company flag (RQ-52)

        var master = new PriceListsViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Price List Creation", master), Screen.PriceListsMaster,
            "Price List Creation", () => PriceLists = master);
    }

    /// <summary>
    /// Opens the Reorder Levels master (Masters → Create → Inventory Masters → Reorder Levels; Phase 6 slice 6;
    /// RQ-32..RQ-35) as a page column: define a reorder level + minimum order quantity per Stock Item / Group /
    /// Category, each figure Simple or Advanced (Alt+S / Alt+V). Always available (no F11 gate — a company with no
    /// definitions falls back to the legacy per-item fields, ER-13).
    /// </summary>
    public void ShowReorderLevelsMaster()
    {
        if (Company is null) return;

        var master = new ReorderLevelsViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Reorder Levels", master), Screen.ReorderLevelsMaster,
            "Reorder Levels", () => ReorderLevels = master);
    }

    // =============================================================== screen: manufacturing journal

    /// <summary>
    /// Opens the Manufacturing-Journal voucher-entry screen (Vouchers → Inventory Vouchers → Manufacturing
    /// Journal; Alt+F7; Phase 6 Cluster 2; RQ-11/RQ-12/RQ-13/RQ-15) as a page column. Resolves the company's
    /// Manufacturing-Journal voucher type — creating one via
    /// <see cref="Apex.Ledger.Services.ManufacturingJournalService.CreateManufacturingJournalType"/> if none
    /// exists yet (RQ-11) — then hosts the entry screen that posts through the engine. A no-op unless the F12
    /// config "Set Components (BOM)" is on (RQ-10/RQ-52).
    /// </summary>
    public void OpenManufacturingJournal()
    {
        if (Company is null) return;
        if (!Company.SetComponentsBom) return;   // gated by the F12 config (RQ-10/RQ-52)

        var service = new Apex.Ledger.Services.ManufacturingJournalService(Company);
        var type = Company.VoucherTypes.FirstOrDefault(t => t.IsManufacturingJournal);
        if (type is null)
        {
            // Create the Manufacturing-Journal voucher type on first use (RQ-11), avoiding a name clash.
            var name = "Manufacturing Journal";
            var n = 1;
            while (Company.VoucherTypes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"Manufacturing Journal {++n}";
            try
            {
                type = service.CreateManufacturingJournalType(name);
                _storage.Save(Company);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = ex.Message;
                return;
            }
        }

        var entry = new ManufacturingJournalEntryViewModel(
            Company, type, _storage,
            onSaved: ShowGateway,
            onCancelled: BackFromPage);
        var title = $"Manufacturing Journal — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.ManufacturingJournalEntry, title,
            () => ManufacturingJournalEntry = entry);
    }

    // =============================================================== screen: job work (slice 8)

    /// <summary>
    /// Opens the Job Work In/Out Order voucher-entry screen (Vouchers → Other Vouchers → Job Work In/Out Order;
    /// F10; Phase 6 slice 8; RQ-45/RQ-47/RQ-50) as a page column. Resolves the seeded Job Work In/Out Order
    /// voucher type on the current company (activated by the F11 feature). A no-op unless the F11 feature
    /// "Enable Job Order Processing" is on (RQ-45/RQ-52), so the screen can never be reached with the feature off.
    /// </summary>
    public void OpenJobWorkOrder(JobWorkDirection direction)
    {
        if (Company is null) return;
        if (!Company.EnableJobOrderProcessing) return;   // gated by the F11 feature (RQ-45/RQ-52)

        var baseType = direction == JobWorkDirection.In
            ? VoucherBaseType.JobWorkInOrder
            : VoucherBaseType.JobWorkOutOrder;
        var type = VoucherTypeResolver.ResolveForEntry(Company, baseType);
        if (type is null)
        {
            Message = VoucherTypeResolver.NoActiveTypeMessage(Company, baseType);
            return;
        }

        var entry = new JobWorkOrderEntryViewModel(
            Company, type, direction, _storage,
            onSaved: ShowGateway,
            onCancelled: BackFromPage);
        var title = $"Job Work Order Creation — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.JobWorkOrderEntry, title,
            () => JobWorkOrderEntry = entry);
    }

    /// <summary>
    /// Opens the Material In/Out movement voucher-entry screen (Vouchers → Other Vouchers → Material In/Out; F10;
    /// Phase 6 slice 8; RQ-46/RQ-48/RQ-49/RQ-50) as a page column. Resolves the seeded Material In/Out voucher
    /// type (activated by the F11 feature, carrying "Use for Job Work" and — for Material In — "Allow
    /// Consumption"). A no-op unless the F11 feature "Enable Job Order Processing" is on (RQ-45/RQ-52).
    /// </summary>
    public void OpenMaterialMovement(VoucherBaseType baseType)
    {
        if (Company is null) return;
        if (!Company.EnableJobOrderProcessing) return;   // gated by the F11 feature (RQ-45/RQ-52)
        if (baseType is not (VoucherBaseType.MaterialIn or VoucherBaseType.MaterialOut)) return;

        var type = VoucherTypeResolver.ResolveForEntry(Company, baseType);
        if (type is null)
        {
            Message = VoucherTypeResolver.NoActiveTypeMessage(Company, baseType);
            return;
        }

        var entry = new MaterialMovementEntryViewModel(
            Company, type, _storage,
            onSaved: ShowGateway,
            onCancelled: BackFromPage);
        var title = $"Material Movement Creation — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.MaterialMovementEntry, title,
            () => MaterialMovementEntry = entry);
    }

    // =============================================================== screen: POS billing (slice 7)

    /// <summary>
    /// Opens the POS Billing voucher-entry screen (Vouchers → Other Vouchers → POS Billing; catalog §11; Phase 6
    /// slice 7 RQ-38..RQ-44) as a page column. A POS bill is a Sales item-invoice with a tender split — it posts
    /// through a <b>POS-flagged Sales</b> voucher type (RQ-38). Resolves that type, creating a user-defined
    /// "Sales (POS)" type on first use (POS types are user-created, not seeded — mirroring the Manufacturing
    /// Journal), then hosts the entry that posts through the engine. When the POS config's print-after-save is on
    /// the retail receipt opens in a Print-Preview column after Accept (RQ-44).
    /// </summary>
    public void OpenPosBilling()
    {
        if (Company is null) return;

        var type = Company.VoucherTypes.FirstOrDefault(t => t.IsPosSales && t.IsActive)
                   ?? Company.VoucherTypes.FirstOrDefault(t => t.IsPosSales);
        if (type is null)
        {
            var name = "Sales (POS)";
            var n = 1;
            while (Company.VoucherTypes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"Sales (POS) {++n}";
            type = new VoucherType(Guid.NewGuid(), name, VoucherBaseType.Sales, useForPos: true,
                posConfig: new PosConfig
                {
                    DefaultTitle = "Retail Invoice",
                    Message1 = "Thank you for shopping with us!",
                    Declaration = "Goods once sold are subject to the store's return policy.",
                });
            Company.AddVoucherType(type);
            _storage.Save(Company);
        }

        PosReceiptData? pending = null;
        var entry = new PosBillingViewModel(
            Company, type, _storage,
            onSaved: () =>
            {
                if (pending is { } r) { var rr = r; pending = null; OpenPosReceiptPreview(rr); }
                else ShowGateway();
            },
            onCancelled: BackFromPage);
        entry.PrintReceiptRequested += r => pending = r;

        var title = $"POS Billing — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " — POS", entry), Screen.PosBilling, title,
            () => PosBilling = entry);
    }

    /// <summary>Replaces the POS entry column with a Print-Preview column showing the just-posted retail receipt (RQ-44).</summary>
    private void OpenPosReceiptPreview(PosReceiptData receipt)
    {
        ClearSubScreens();
        if (Columns.Count > 0 && !Columns[^1].IsMenu) Columns.RemoveAt(Columns.Count - 1);
        var preview = new PrintPreviewViewModel(receipt);
        PrintPreview = preview;
        Columns.Add(new GatewayColumn(preview.Title, preview));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.PrintPreview;
        ScreenTitle = preview.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>True while the POS Billing entry page is active (drives the Alt+I / Alt+A button-bar actions).</summary>
    public bool IsPosBillingEntry => CurrentScreen == Screen.PosBilling;

    /// <summary>Alt+I — toggles the in-progress POS bill between Single and Multi tender mode (both ways, RQ-42).</summary>
    public void TogglePosPaymentMode()
    {
        if (CurrentScreen == Screen.PosBilling) PosBilling?.TogglePaymentMode();
    }

    /// <summary>Alt+A — surfaces the per-rate tax analysis for the in-progress POS bill (RQ-53).</summary>
    public void ShowPosTaxAnalysis()
    {
        if (CurrentScreen == Screen.PosBilling) PosBilling?.ShowTaxAnalysis();
    }

    // =============================================================== screen: statutory (GST config)

    /// <summary>
    /// Opens the company GST-configuration page (F11 Features → GST; Masters → Statutory → GST) as a page
    /// column: an Enable-GST toggle, the GSTIN (validated, auto-filling the Home State), Home State/UT,
    /// registration type and return periodicity. Enabling calls the engine (seeds slabs + creates the six
    /// tax ledgers) and persists (catalog §12; phase4 slice 4c).
    /// </summary>
    public void ShowGstConfig()
    {
        if (Company is null) return;

        var page = new GstConfigViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("GST — Statutory", page), Screen.GstConfig,
            "GST — Statutory Configuration", () => GstConfig = page);
    }

    /// <summary>
    /// Opens the <b>GST Rate Setup</b> bulk-maintenance page (Statutory → GST Rate Setup; Phase 9 slice 1;
    /// plan.md C-6 / DP-24 / RQ-24) as a page column: the dated GST 2.0 rate-history + Compensation-Cess windows,
    /// a one-click "seed the GST 2.0 defaults" action, and add-a-window forms for mass HSN/rate maintenance. A no-op
    /// unless GST is enabled (the menu item is itself gated on <see cref="Company.GstEnabled"/>).
    /// </summary>
    public void ShowGstRateSetup()
    {
        if (Company is not { GstEnabled: true }) return;

        var page = new GstRateSetupViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("GST Rate Setup", page), Screen.GstRateSetup,
            "GST Rate Setup — Dated Rates & Cess", () => GstRateSetup = page);
    }

    /// <summary>
    /// Opens the <b>CMP-08</b> composition quarterly-statement report (Reports → Statutory Reports → Composition
    /// Returns → CMP-08; Phase 9 slice 3; RQ-16) as a page column: a read-only projection over the pure
    /// <see cref="Cmp08"/> engine for a chosen FY + quarter. A no-op unless the company is a Composition dealer (the
    /// menu item + open path are gated on <see cref="IsCompositionDealer"/>), so a Regular company never reaches it
    /// (ER-13).
    /// </summary>
    public void OpenCmp08Report()
    {
        if (Company is null || !IsCompositionDealer) return;

        var page = new Cmp08ReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("CMP-08", page), Screen.Cmp08Report,
            "Form CMP-08 — Composition Quarterly Statement", () => Cmp08Report = page);
    }

    /// <summary>
    /// Opens the <b>GSTR-4</b> composition annual-return report (Reports → Statutory Reports → Composition Returns →
    /// GSTR-4; Phase 9 slice 3; RQ-16) as a page column: a read-only projection over the pure <see cref="Gstr4"/>
    /// engine for a chosen financial year. A no-op unless the company is a Composition dealer (ER-13).
    /// </summary>
    public void OpenGstr4Report()
    {
        if (Company is null || !IsCompositionDealer) return;

        var page = new Gstr4ReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("GSTR-4", page), Screen.Gstr4Report,
            "Form GSTR-4 — Composition Annual Return", () => Gstr4Report = page);
    }

    // ---- Advanced-GST report screens (Phase 9 UI-1; RQ-17). Each opens a read-only page column projecting its pure
    // engine; all are gated on a Regular GST dealer (a Composition / GST-off company never reaches them, ER-13). ----

    /// <summary>Opens the <b>GSTR-9</b> annual-return report (Reports → Statutory Reports → Annual Returns → GSTR-9;
    /// Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenGstr9Report()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new Gstr9ReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("GSTR-9", page), Screen.Gstr9Report,
            "Form GSTR-9 — Annual Return", () => Gstr9Report = page);
    }

    /// <summary>Opens the <b>GSTR-9C</b> reconciliation-statement report (Reports → Statutory Reports → Annual Returns
    /// → GSTR-9C; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenGstr9cReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new Gstr9cReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("GSTR-9C", page), Screen.Gstr9cReport,
            "Form GSTR-9C — Reconciliation Statement", () => Gstr9cReport = page);
    }

    /// <summary>Opens the <b>Electronic Ledgers</b> report (Reports → Statutory Reports → GST Returns (Advanced) →
    /// Electronic Ledgers; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenElectronicLedgersReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ElectronicLedgersReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("Electronic Ledgers", page), Screen.ElectronicLedgersReport,
            "Electronic Ledgers", () => ElectronicLedgersReport = page);
    }

    /// <summary>Opens the <b>ITC Set-Off</b> (Rule-88A) display-only projection (Reports → Statutory Reports → GST
    /// Returns (Advanced) → ITC Set-Off; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenItcSetOffReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ItcSetOffReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("ITC Set-Off", page), Screen.ItcSetOffReport,
            "ITC Set-Off (Rule 88A) — projection", () => ItcSetOffReport = page);
    }

    /// <summary>Opens the <b>ITC Reversal</b> display-only view (Reports → Statutory Reports → GST Returns (Advanced) →
    /// ITC Reversal; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenItcReversalReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ItcReversalReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("ITC Reversal", page), Screen.ItcReversalReport,
            "ITC Reversal — outstanding balance & candidates", () => ItcReversalReport = page);
    }

    /// <summary>Opens the <b>GSTR-2B Reconciliation</b> report (Reports → Statutory Reports → GST Returns (Advanced) →
    /// GSTR-2B Reconciliation; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenGstr2bReconReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new Gstr2bReconReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("GSTR-2B Reconciliation", page), Screen.Gstr2bReconReport,
            "GSTR-2B Reconciliation", () => Gstr2bReconReport = page);
    }

    /// <summary>Opens the <b>ITC Gate</b> advisory report (Reports → Statutory Reports → GST Returns (Advanced) → ITC
    /// Gate; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenItcGateReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ItcGateReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("ITC Gate", page), Screen.ItcGateReport,
            "ITC Gate — §16(2)(aa) / §17(5)", () => ItcGateReport = page);
    }

    /// <summary>Opens the <b>QRMP / IFF</b> cadence report (Reports → Statutory Reports → GST Returns (Advanced) →
    /// QRMP / IFF; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenQrmpReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new QrmpReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("QRMP / IFF", page), Screen.QrmpReport,
            "QRMP / IFF Cadence", () => QrmpReport = page);
    }

    /// <summary>Opens the <b>GST Amendments</b> (GSTR-1/3B) report (Reports → Statutory Reports → GST Returns
    /// (Advanced) → GST Amendments; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenGstAmendmentsReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new GstAmendmentsReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("GST Amendments", page), Screen.GstAmendmentsReport,
            "GSTR-1 / 3B Amendments", () => GstAmendmentsReport = page);
    }

    /// <summary>Opens the <b>e-Invoice / e-Way Status</b> listing (Reports → Statutory Reports → GST Returns
    /// (Advanced) → e-Invoice / e-Way Status; Phase 9 UI-1). A no-op unless the company is a Regular GST dealer.</summary>
    public void OpenEInvoiceEWayStatusReport()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new EInvoiceEWayStatusReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("e-Invoice / e-Way Status", page), Screen.EInvoiceEWayStatusReport,
            "e-Invoice / e-Way Status", () => EInvoiceEWayStatusReport = page);
    }

    /// <summary>Opens the <b>IMS — Accept / Reject / Pending</b> action screen (Reports → Statutory Reports → GST
    /// Actions → IMS; Phase 9 UI-2). Opening it posts NOTHING — only an explicit Accept/Reject/Pending/Clear on the
    /// page records an IMS action. A no-op unless the company is a Regular GST dealer (ER-13).</summary>
    public void OpenImsActions()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ImsActionsViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("IMS", page), Screen.ImsActions,
            "IMS — Accept / Reject / Pending", () => ImsActions = page);
    }

    /// <summary>True while the IMS action screen is the active screen (drives its arrow-key row nav).</summary>
    public bool IsImsActionsScreen =>
        CurrentScreen == Screen.ImsActions && ImsActions is not null;

    /// <summary>Opens the <b>Run Set-Off (Rule 88A) &amp; Pay</b> action screen (Reports → Statutory Reports → GST
    /// Actions → Run Set-Off &amp; Pay; Phase 9 UI-2). Opening it only PREVIEWS the allocation — nothing is posted
    /// until the explicit Run / Deposit / Pay action. A no-op unless the company is a Regular GST dealer (ER-13).</summary>
    public void OpenRunSetOff()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new RunSetOffViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Run Set-Off", page), Screen.RunSetOff,
            "Run Set-Off (Rule 88A) & Pay", () => RunSetOff = page);
    }

    /// <summary>Opens the <b>Post ITC Reversal</b> action screen (Reports → Statutory Reports → GST Actions → Post ITC
    /// Reversal; Phase 9 UI-2). Opening it only projects the ECRS balance / candidates / history — nothing is posted
    /// until the explicit Post / Reclaim action. A no-op unless the company is a Regular GST dealer (ER-13).</summary>
    public void OpenPostItcReversal()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new PostItcReversalViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Post ITC Reversal", page), Screen.PostItcReversal,
            "Post ITC Reversal", () => PostItcReversal = page);
    }

    /// <summary>True while the Post-ITC-Reversal screen is the active screen (drives its arrow-key row nav).</summary>
    public bool IsPostItcReversalScreen =>
        CurrentScreen == Screen.PostItcReversal && PostItcReversal is not null;

    /// <summary>Opens the <b>Import GSTR-2B</b> action screen (Reports → Statutory Reports → GST Actions → Import
    /// GSTR-2B; Phase 9 UI-2). Opening it imports nothing — only the explicit Import reads + materialises the file.
    /// A no-op unless the company is a Regular GST dealer (ER-13).</summary>
    public void OpenImportGstr2b()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new ImportGstr2bViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Import GSTR-2B", page), Screen.ImportGstr2b,
            "Import GSTR-2B", () => ImportGstr2b = page);
    }

    /// <summary>Opens the <b>Generate e-Invoice</b> action screen (Reports → Statutory Reports → GST Actions →
    /// Generate e-Invoice; Phase 9 UI-2). Opening it prepares nothing — only the explicit Prepare / Record / Cancel
    /// acts. Offline INV-01 mode: no portal credentials. A no-op unless the company is a Regular GST dealer (ER-13).</summary>
    public void OpenGenerateEInvoice()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new GenerateEInvoiceViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Generate e-Invoice", page), Screen.GenerateEInvoice,
            "Generate e-Invoice", () => GenerateEInvoice = page);
    }

    /// <summary>True while the Generate-e-Invoice screen is the active screen (drives its arrow-key row nav).</summary>
    public bool IsGenerateEInvoiceScreen =>
        CurrentScreen == Screen.GenerateEInvoice && GenerateEInvoice is not null;

    /// <summary>Opens the <b>Generate e-Way Bill</b> action screen (Reports → Statutory Reports → GST Actions →
    /// Generate e-Way Bill; Phase 9 UI-2). Opening it prepares nothing — only the explicit Prepare / Part-B / Record /
    /// Cancel / Extend / Close acts. Offline EWB-01 mode. A no-op unless a Regular GST dealer (ER-13).</summary>
    public void OpenGenerateEWayBill()
    {
        if (Company is null || !IsRegularGstDealer) return;
        var page = new GenerateEWayBillViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Generate e-Way Bill", page), Screen.GenerateEWayBill,
            "Generate e-Way Bill", () => GenerateEWayBill = page);
    }

    /// <summary>True while the Generate-e-Way-Bill screen is the active screen (drives its arrow-key row nav).</summary>
    public bool IsGenerateEWayBillScreen =>
        CurrentScreen == Screen.GenerateEWayBill && GenerateEWayBill is not null;

    /// <summary>
    /// Opens the Nature-of-Payment (TDS section) master (Masters → Create → Statutory Masters → Nature of
    /// Payment; Phase 7 slice 1) as a page column: lists the seeded predefined TDS sections and creates customs.
    /// A no-op unless TDS is enabled (the menu item is itself gated on <see cref="Company.TdsEnabled"/>).
    /// </summary>
    public void ShowNatureOfPaymentMaster()
    {
        if (Company is not { TdsEnabled: true }) return;

        var master = new NatureOfPaymentMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Nature of Payment", master), Screen.NatureOfPaymentMaster,
            "Nature of Payment (TDS)", () => NatureOfPaymentMaster = master);
    }

    /// <summary>
    /// Opens the Nature-of-Goods (§206C TCS) master (Masters → Create → Statutory Masters → Nature of Goods;
    /// Phase 7 slice 1) as a page column: lists the seeded predefined §206C set and creates customs. A no-op
    /// unless TCS is enabled (the menu item is itself gated on <see cref="Company.TcsEnabled"/>).
    /// </summary>
    public void ShowNatureOfGoodsMaster()
    {
        if (Company is not { TcsEnabled: true }) return;

        var master = new NatureOfGoodsMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Nature of Goods", master), Screen.NatureOfGoodsMaster,
            "Nature of Goods (§206C TCS)", () => NatureOfGoodsMaster = master);
    }

    // =============================================================== screen: payroll masters (Phase 8 slice 1)

    /// <summary>
    /// Opens the Employee-Category master (Masters → Create → Payroll Masters → Employee Category; Phase 8 slice 1)
    /// as a page column. A no-op unless Payroll is enabled (the menu item is itself gated on
    /// <see cref="Company.PayrollEnabled"/>), so a non-payroll company never reaches it (ER-13).
    /// </summary>
    public void ShowEmployeeCategoryMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new EmployeeCategoryMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Employee Category Creation", master), Screen.EmployeeCategoryMaster,
            "Employee Category Creation", () => EmployeeCategoryMaster = master);
    }

    /// <summary>
    /// Opens the Employee-Group master (Masters → Create → Payroll Masters → Employee Group; Phase 8 slice 1) as a
    /// page column. A no-op unless Payroll is enabled.
    /// </summary>
    public void ShowEmployeeGroupMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new EmployeeGroupMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Employee Group Creation", master), Screen.EmployeeGroupMaster,
            "Employee Group Creation", () => EmployeeGroupMaster = master);
    }

    /// <summary>
    /// Opens the Employee master (Masters → Create → Payroll Masters → Employee; Phase 8 slice 1) as a page column.
    /// A no-op unless Payroll is enabled.
    /// </summary>
    public void ShowEmployeeMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new EmployeeMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Employee Creation", master), Screen.EmployeeMaster,
            "Employee Creation", () => EmployeeMaster = master);
    }

    /// <summary>
    /// Opens the Payroll-Unit master (Masters → Create → Payroll Masters → Payroll Unit; Phase 8 slice 1) as a page
    /// column. A no-op unless Payroll is enabled.
    /// </summary>
    public void ShowPayrollUnitMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new PayrollUnitMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Payroll Unit Creation", master), Screen.PayrollUnitMaster,
            "Payroll Unit Creation", () => PayrollUnitMaster = master);
    }

    /// <summary>
    /// Opens the Attendance/Production-Type master (Masters → Create → Payroll Masters → Attendance / Production
    /// Type; Phase 8 slice 1) as a page column. A no-op unless Payroll is enabled.
    /// </summary>
    public void ShowAttendanceTypeMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new AttendanceTypeMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Attendance Type Creation", master), Screen.AttendanceTypeMaster,
            "Attendance / Production Type Creation", () => AttendanceTypeMaster = master);
    }

    /// <summary>
    /// Opens the Pay Head master (Masters → Create → Payroll Masters → Pay Head; Phase 8 slice 2; RQ-4) as a page
    /// column. A no-op unless Payroll is enabled (ER-13).
    /// </summary>
    public void ShowPayHeadMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new PayHeadMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Pay Head Creation", master), Screen.PayHeadMaster,
            "Pay Head Creation", () => PayHeadMaster = master);
    }

    /// <summary>
    /// Opens the Salary Details / structure master (Masters → Create → Payroll Masters → Salary Details; Phase 8
    /// slice 2; RQ-5) as a page column. A no-op unless Payroll is enabled (ER-13).
    /// </summary>
    public void ShowSalaryStructureMaster()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var master = new SalaryStructureMasterViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Salary Details", master), Screen.SalaryStructureMaster,
            "Salary Details", () => SalaryDetails = master);
    }

    /// <summary>
    /// Opens the <b>Attendance / Production voucher</b> entry page (Transactions → Vouchers → Payroll → Attendance /
    /// Production; Phase 8 slice 3; RQ-6) as a page column: records per-employee attendance / leave / production
    /// values for a period (a non-accounting voucher). A no-op unless Payroll is enabled (ER-13).
    /// </summary>
    public void ShowAttendanceVoucher()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var page = new AttendanceVoucherEntryViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Attendance / Production", page), Screen.AttendanceVoucherEntry,
            "Attendance / Production Voucher", () => AttendanceVoucher = page);
    }

    /// <summary>
    /// Opens the <b>Payroll voucher</b> entry page (Transactions → Vouchers → Payroll → Payroll · Ctrl+F4; Phase 8
    /// slice 3; RQ-7) as a page column: pick a period + employees, Compute the salary breakdown, and post the
    /// balanced integrated accounting voucher. A no-op unless Payroll is enabled (ER-13).
    /// </summary>
    public void ShowPayrollVoucher()
    {
        if (Company is not { PayrollEnabled: true }) return;

        var page = new PayrollVoucherEntryViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Payroll", page), Screen.PayrollVoucherEntry,
            "Payroll Voucher", () => PayrollVoucher = page);
    }

    /// <summary>
    /// Opens the <b>TDS Stat Payment</b> deposit page (Transactions → Vouchers → TDS Stat Payment, the Payment
    /// "Ctrl+F"; Phase 7 slice 3) as a page column: deposits the accrued TDS Payable into the bank and records the
    /// ITNS-281 challan. A no-op unless TDS is enabled (the menu item is itself gated on
    /// <see cref="Company.TdsEnabled"/>), so a non-TDS company never reaches it (ER-13).
    /// </summary>
    public void ShowTdsStatPayment()
    {
        if (Company is not { TdsEnabled: true }) return;

        var page = new TdsStatPaymentViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("TDS Stat Payment", page), Screen.TdsStatPayment,
            "TDS Stat Payment (Deposit)", () => TdsStatPayment = page);
    }

    /// <summary>
    /// Opens the <b>Challan Reconciliation (Alt+R)</b> report page (Reports → GST Reports → TDS → Challan
    /// Reconciliation; Phase 7 slice 3) as a page column: the per-section deposited-vs-deducted match and remaining
    /// payable over the financial year. A no-op unless TDS is enabled (the menu item + Alt+R are gated on
    /// <see cref="Company.TdsEnabled"/>), so a non-TDS company never reaches it (ER-13).
    /// </summary>
    public void OpenChallanReconciliation()
    {
        if (Company is not { TdsEnabled: true }) return;

        var page = new ChallanReconciliationViewModel(Company);
        OpenPageColumn(new GatewayColumn(page.Title, page), Screen.ChallanReconciliation,
            "Challan Reconciliation", () => ChallanReconciliation = page);
    }

    /// <summary>True while the GSTR-2B Reconciliation report page is the active screen (drives its arrow-key row nav).</summary>
    public bool IsGstr2bReconScreen =>
        CurrentScreen == Screen.Gstr2bReconReport && Gstr2bReconReport is not null;

    /// <summary>True while the Challan Reconciliation report page is the active screen (drives its arrow-key nav).</summary>
    public bool IsChallanReconciliationScreen =>
        CurrentScreen == Screen.ChallanReconciliation && ChallanReconciliation is not null;

    /// <summary>
    /// Opens the <b>Form 26Q</b> quarterly-TDS-return report page (Reports → GST Reports → TDS → Form 26Q; Phase 7
    /// slice 4) as a page column: the deductor / challan / deductee blocks + control totals for a chosen FY + quarter,
    /// with a Ctrl+A FVU export and an Alt+B save-return. A no-op unless TDS is enabled (the menu item + the open path
    /// are gated on <see cref="Company.TdsEnabled"/>), so a non-TDS company never reaches it (ER-13).
    /// </summary>
    public void OpenForm26Q()
    {
        if (Company is not { TdsEnabled: true }) return;

        var page = new Form26QViewModel(Company);
        var form26Q = FormMenuLabel("26Q");
        OpenPageColumn(new GatewayColumn(form26Q, page), Screen.Form26Q,
            $"{form26Q} (Quarterly TDS Return)", () => Form26Q = page);
    }

    /// <summary>True while the Form 26Q return report page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm26QScreen => CurrentScreen == Screen.Form26Q && Form26Q is not null;

    /// <summary>
    /// Alt+B on the Form 26Q screen — <b>save &amp; return</b>: writes the FVU-compatible flat file for the current
    /// return to the export folder (the "save") then pops back to the menu (the "return"). A no-op off that screen.
    /// </summary>
    public void SaveReturnForm26Q()
    {
        if (!IsForm26QScreen || Form26Q is null) return;
        Form26Q.ExportFvu();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>PF ECR / Challan</b> report page (Reports → Statutory Reports → Payroll (PF) → PF ECR / Challan;
    /// Phase 8 slice 4; RQ-9) as a page column: the member-wise ECR 2.0 rows + the A/c 1/2/10/21/22 challan totals
    /// for a chosen FY + wage month, with a Ctrl+A ECR export and an Alt+B save-return. A no-op unless Payroll
    /// Statutory is enabled (the menu item + the open path are gated on <see cref="Company.PayrollStatutoryEnabled"/>),
    /// so a non-payroll company never reaches it (ER-13).
    /// </summary>
    public void OpenPfEcrReport()
    {
        if (Company is not { PayrollStatutoryEnabled: true }) return;

        var page = new PfEcrReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("PF ECR / Challan", page), Screen.PfEcrReport,
            "PF ECR / Challan (EPFO)", () => PfEcrReport = page);
    }

    /// <summary>True while the PF ECR / Challan report page is the active screen (drives its keyboard actions).</summary>
    public bool IsPfEcrReportScreen => CurrentScreen == Screen.PfEcrReport && PfEcrReport is not null;

    /// <summary>
    /// Alt+B on the PF ECR / Challan screen — <b>save &amp; return</b>: writes the ECR 2.0 flat file for the current
    /// return to the export folder (the "save") then pops back to the menu (the "return"). A no-op off that screen.
    /// </summary>
    public void SaveReturnPfEcr()
    {
        if (!IsPfEcrReportScreen || PfEcrReport is null) return;
        PfEcrReport.ExportEcr();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>ESI Monthly Contribution</b> report page (Reports → Statutory Reports → Payroll → ESI Monthly
    /// Contribution; Phase 8 slice 5; RQ-10) as a page column: the per-IP rows (IP number, name, days, ESI wages,
    /// EE 0.75% / ER 3.25% contributions) + the EE / ER / total footings for a chosen FY + wage month, with a Ctrl+A
    /// contribution-file export and an Alt+B save-return. A no-op unless Payroll Statutory is enabled (the menu item
    /// + the open path are gated on <see cref="Company.PayrollStatutoryEnabled"/>), so a non-payroll company never
    /// reaches it (ER-13).
    /// </summary>
    public void OpenEsiContributionReport()
    {
        if (Company is not { PayrollStatutoryEnabled: true }) return;

        var page = new EsiContributionReportViewModel(Company);
        OpenPageColumn(new GatewayColumn("ESI Monthly Contribution", page), Screen.EsiContributionReport,
            "ESI Monthly Contribution (ESIC)", () => EsiContributionReport = page);
    }

    /// <summary>True while the ESI Monthly Contribution report page is the active screen (drives its keyboard
    /// actions).</summary>
    public bool IsEsiContributionReportScreen => CurrentScreen == Screen.EsiContributionReport && EsiContributionReport is not null;

    /// <summary>
    /// Alt+B on the ESI Monthly Contribution screen — <b>save &amp; return</b>: writes the ESIC monthly-contribution
    /// offline file for the current return to the export folder (the "save") then pops back to the menu (the
    /// "return"). A no-op off that screen.
    /// </summary>
    public void SaveReturnEsiContribution()
    {
        if (!IsEsiContributionReportScreen || EsiContributionReport is null) return;
        EsiContributionReport.ExportReturn();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>PT Deduction Register</b> report page (Reports → Statutory Reports → Payroll → PT Deduction
    /// Register; Phase 8 slice 6; RQ-11) as a page column: the per-employee rows (name, number, PT wages, the month's
    /// PT and the FY-to-date cumulative bounded by the ₹2,500 cap) + the PT-wages / PT footings for a chosen FY + wage
    /// month, with a Ctrl+A CSV export and an Alt+B save-return. A no-op unless Payroll Statutory is enabled (the menu
    /// item + the open path are gated on <see cref="Company.PayrollStatutoryEnabled"/>), so a non-payroll company never
    /// reaches it (ER-13).
    /// </summary>
    public void OpenProfessionalTaxRegister()
    {
        if (Company is not { PayrollStatutoryEnabled: true }) return;

        var page = new ProfessionalTaxRegisterViewModel(Company);
        OpenPageColumn(new GatewayColumn("PT Deduction Register", page), Screen.ProfessionalTaxRegister,
            "PT Deduction Register", () => ProfessionalTaxRegister = page);
    }

    /// <summary>True while the PT Deduction Register report page is the active screen (drives its keyboard actions).</summary>
    public bool IsProfessionalTaxRegisterScreen => CurrentScreen == Screen.ProfessionalTaxRegister && ProfessionalTaxRegister is not null;

    /// <summary>
    /// Alt+B on the PT Deduction Register screen — <b>save &amp; return</b>: writes the register CSV for the current
    /// wage month to the export folder (the "save") then pops back to the menu (the "return"). A no-op off that screen.
    /// </summary>
    public void SaveReturnProfessionalTax()
    {
        if (!IsProfessionalTaxRegisterScreen || ProfessionalTaxRegister is null) return;
        ProfessionalTaxRegister.ExportRegister();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>Gratuity Provision</b> register page (Reports → Statutory Reports → Payroll → Gratuity Provision;
    /// Phase 8 slice 9; RQ-14) as a page column: the per-employee accrual as-on a chosen provision date (completed
    /// years, vested flag, Basic + DA, accrued gratuity) + the total liability / prior balance / delta, with a
    /// <b>Post Provision</b> action (Ctrl+A) that posts the delta voucher. A no-op unless the establishment is enrolled
    /// for gratuity (the menu item + the open path are gated on <see cref="Company.GratuityConfig"/>), so a non-gratuity
    /// company never reaches it (ER-13).
    /// </summary>
    public void OpenGratuityProvisionRegister()
    {
        if (Company is not { PayrollStatutoryEnabled: true, GratuityConfig: not null }) return;

        var page = new GratuityProvisionRegisterViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("Gratuity Provision", page), Screen.GratuityProvisionRegister,
            "Gratuity Provision Register", () => GratuityProvisionRegister = page);
    }

    /// <summary>True while the Gratuity Provision register page is the active screen (drives its keyboard actions).</summary>
    public bool IsGratuityProvisionRegisterScreen => CurrentScreen == Screen.GratuityProvisionRegister && GratuityProvisionRegister is not null;

    /// <summary>Ctrl+A on the Gratuity Provision screen — posts the period-end provision voucher for the delta over the
    /// prior balance (the screen's primary command). A no-op off that screen.</summary>
    public void PostGratuityProvision()
    {
        if (!IsGratuityProvisionRegisterScreen || GratuityProvisionRegister is null) return;
        GratuityProvisionRegister.PostProvision();
    }

    /// <summary>
    /// Opens the <b>Bonus</b> register page (Reports → Statutory Reports → Payroll → Bonus Register; Phase 8 slice 9;
    /// RQ-15) as a page column: the per-employee statutory-bonus figures for a chosen accounting year (eligibility,
    /// actual Basic + DA, capped base, rate, annual bonus) + the total bonus. A no-op unless the establishment is
    /// enrolled for statutory bonus (the menu item + the open path are gated on <see cref="Company.BonusConfig"/>), so a
    /// non-bonus company never reaches it (ER-13).
    /// </summary>
    public void OpenBonusRegister()
    {
        if (Company is not { PayrollStatutoryEnabled: true, BonusConfig: not null }) return;

        var page = new BonusRegisterViewModel(Company);
        OpenPageColumn(new GatewayColumn("Bonus Register", page), Screen.BonusRegister,
            "Statutory Bonus Register", () => BonusRegister = page);
    }

    /// <summary>True while the Bonus register page is the active screen.</summary>
    public bool IsBonusRegisterScreen => CurrentScreen == Screen.BonusRegister && BonusRegister is not null;

    // =============================================================== §192 salary TDS (Phase 8 slice 7)

    /// <summary>
    /// Opens the per-employee <b>Income Tax Declaration (Form 12BB)</b> master (Masters → Create → Payroll Masters →
    /// Income Tax Declaration; Phase 8 slice 7; RQ-12) as a page column: pick an employee, capture the declared
    /// investments / exemptions / prior-income the §192 engine estimates the salary TDS from, and Save (Ctrl+A). A
    /// no-op unless §192 salary TDS is enabled (the menu item + the open path are gated on
    /// <see cref="Company.SalaryTdsEnabled"/>), so a non-salary-TDS company never reaches it (ER-13).
    /// </summary>
    public void ShowTaxDeclarationMaster()
    {
        if (Company is not { SalaryTdsEnabled: true }) return;

        var master = new TaxDeclarationViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn("Income Tax Declaration", master), Screen.TaxDeclarationMaster,
            $"Income Tax Declaration ({FormMenuLabel("12BB")})", () => TaxDeclarationMaster = master);
    }

    /// <summary>True while the Income-Tax-Declaration master is the active screen (drives its arrow-key nav).</summary>
    public bool IsTaxDeclarationMasterScreen => CurrentScreen == Screen.TaxDeclarationMaster && TaxDeclarationMaster is not null;

    /// <summary>
    /// Opens the <b>Form 24Q</b> quarterly salary-TDS-return report page (Reports → Statutory Reports → Payroll →
    /// Form 24Q; Phase 8 slice 7; RQ-13) as a page column: the Annexure I deductee rows + the Q4 Annexure II annual
    /// computation + control totals for a chosen FY / quarter / section code, with a Ctrl+A flat-file export and an
    /// Alt+B save-return. A no-op unless §192 salary TDS is enabled (ER-13).
    /// </summary>
    public void OpenForm24Q()
    {
        if (Company is not { SalaryTdsEnabled: true }) return;

        var page = new Form24QViewModel(Company);
        var form24Q = FormMenuLabel("24Q");
        OpenPageColumn(new GatewayColumn(form24Q, page), Screen.Form24Q,
            $"{form24Q} (Quarterly Salary-TDS Return)", () => Form24Q = page);
    }

    /// <summary>True while the Form 24Q return report page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm24QScreen => CurrentScreen == Screen.Form24Q && Form24Q is not null;

    /// <summary>Alt+B on the Form 24Q screen — <b>save &amp; return</b>: writes the return flat file then pops back to the menu.</summary>
    public void SaveReturnForm24Q()
    {
        if (!IsForm24QScreen || Form24Q is null) return;
        Form24Q.ExportFvu();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>Form 16</b> salary-TDS-certificate report page (Reports → Statutory Reports → Payroll → Form 16;
    /// Phase 8 slice 7; RQ-13) as a page column: per-employee Part A (quarter-wise TDS) + Part B (salary/tax
    /// computation), with a Ctrl+A PDF export and an Alt+B save-return. A no-op unless §192 salary TDS is enabled (ER-13).
    /// </summary>
    public void OpenForm16()
    {
        if (Company is not { SalaryTdsEnabled: true }) return;

        var page = new Form16ViewModel(Company);
        var form16 = FormMenuLabel("16");
        OpenPageColumn(new GatewayColumn(form16, page), Screen.Form16,
            $"{form16} (Salary-TDS Certificate)", () => Form16 = page);
    }

    /// <summary>True while the Form 16 certificate page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm16Screen => CurrentScreen == Screen.Form16 && Form16 is not null;

    /// <summary>Alt+B on the Form 16 screen — <b>save &amp; return</b>: writes the certificate PDF then pops back to the menu.</summary>
    public void SaveReturnForm16()
    {
        if (!IsForm16Screen || Form16 is null) return;
        Form16.ExportPdf();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>TCS Stat Payment</b> deposit page (Transactions → Vouchers → Statutory → TCS Stat Payment, the
    /// Payment "Ctrl+F" family; Phase 7 slice 6) as a page column: deposits the collected TCS Payable into the bank and
    /// records the ITNS-281 challan. A no-op unless TCS is enabled (the menu item is itself gated on
    /// <see cref="Company.TcsEnabled"/>), so a non-TCS company never reaches it (ER-13).
    /// </summary>
    public void ShowTcsStatPayment()
    {
        if (Company is not { TcsEnabled: true }) return;

        var page = new TcsStatPaymentViewModel(Company, _storage, onChanged: BuildButtonBar);
        OpenPageColumn(new GatewayColumn("TCS Stat Payment", page), Screen.TcsStatPayment,
            "TCS Stat Payment (Deposit)", () => TcsStatPayment = page);
    }

    /// <summary>
    /// Opens the <b>TCS Challan Reconciliation</b> report page (Reports → GST Reports → TCS → TCS Challan
    /// Reconciliation; Phase 7 slice 6) as a page column: the per-code deposited-vs-collected match and remaining
    /// payable over the financial year. A no-op unless TCS is enabled (the menu item is gated on
    /// <see cref="Company.TcsEnabled"/>), so a non-TCS company never reaches it (ER-13).
    /// </summary>
    public void OpenTcsChallanReconciliation()
    {
        if (Company is not { TcsEnabled: true }) return;

        var page = new TcsChallanReconciliationViewModel(Company);
        OpenPageColumn(new GatewayColumn(page.Title, page), Screen.TcsChallanReconciliation,
            "TCS Challan Reconciliation", () => TcsChallanReconciliation = page);
    }

    /// <summary>True while the TCS Challan Reconciliation report page is the active screen (drives its arrow-key nav).</summary>
    public bool IsTcsChallanReconciliationScreen =>
        CurrentScreen == Screen.TcsChallanReconciliation && TcsChallanReconciliation is not null;

    /// <summary>
    /// Opens the <b>Form 27EQ</b> quarterly-TCS-return report page (Reports → GST Reports → TCS → Form 27EQ; Phase 7
    /// slice 6) as a page column: the collector / challan / collectee blocks + control totals for a chosen FY + quarter,
    /// with a Ctrl+A FVU export and an Alt+B save-return. A no-op unless TCS is enabled (the menu item + the open path
    /// are gated on <see cref="Company.TcsEnabled"/>), so a non-TCS company never reaches it (ER-13).
    /// </summary>
    public void OpenForm27EQ()
    {
        if (Company is not { TcsEnabled: true }) return;

        var page = new Form27EQViewModel(Company);
        var form27EQ = FormMenuLabel("27EQ");
        OpenPageColumn(new GatewayColumn(form27EQ, page), Screen.Form27EQ,
            $"{form27EQ} (Quarterly TCS Return)", () => Form27EQ = page);
    }

    /// <summary>True while the Form 27EQ return report page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm27EQScreen => CurrentScreen == Screen.Form27EQ && Form27EQ is not null;

    /// <summary>
    /// Alt+B on the Form 27EQ screen — <b>save &amp; return</b>: writes the FVU-compatible flat file for the current
    /// return to the export folder (the "save") then pops back to the menu (the "return"). A no-op off that screen.
    /// </summary>
    public void SaveReturnForm27EQ()
    {
        if (!IsForm27EQScreen || Form27EQ is null) return;
        Form27EQ.ExportFvu();
        BackFromPage();
    }

    // =============================================================== screen: TDS/TCS certificates & control chart (slice 7)

    /// <summary>
    /// Opens the <b>Form 16A</b> TDS-certificate report page (Reports → GST Reports → TDS → Form 16A; Phase 7 slice 7;
    /// catalog §13). Pick a deductee + FY/quarter and export the deterministic, de-branded certificate PDF (Ctrl+A) or
    /// save-and-return (Alt+B). A no-op unless TDS is enabled (the menu item + open path are gated on
    /// <see cref="Company.TdsEnabled"/>), so a non-TDS company never reaches it (ER-13).
    /// </summary>
    public void OpenForm16A()
    {
        if (Company is not { TdsEnabled: true }) return;

        var page = new Form16AViewModel(Company);
        var form16A = FormMenuLabel("16A");
        OpenPageColumn(new GatewayColumn(form16A, page), Screen.Form16A,
            $"{form16A} (TDS Certificate)", () => Form16A = page);
    }

    /// <summary>True while the Form 16A certificate page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm16AScreen => CurrentScreen == Screen.Form16A && Form16A is not null;

    /// <summary>Alt+B on the Form 16A screen — <b>save &amp; return</b>: writes the certificate PDF then pops back to the menu.</summary>
    public void SaveReturnForm16A()
    {
        if (!IsForm16AScreen || Form16A is null) return;
        Form16A.ExportPdf();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>Form 27D</b> TCS-certificate report page (Reports → GST Reports → TCS → Form 27D; Phase 7 slice 7;
    /// catalog §13) — the collector's mirror of Form 16A. Pick a collectee + FY/quarter and export the certificate PDF
    /// (Ctrl+A) or save-and-return (Alt+B). A no-op unless TCS is enabled (gated on <see cref="Company.TcsEnabled"/>, ER-13).
    /// </summary>
    public void OpenForm27D()
    {
        if (Company is not { TcsEnabled: true }) return;

        var page = new Form27DViewModel(Company);
        var form27D = FormMenuLabel("27D");
        OpenPageColumn(new GatewayColumn(form27D, page), Screen.Form27D,
            $"{form27D} (TCS Certificate)", () => Form27D = page);
    }

    /// <summary>True while the Form 27D certificate page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm27DScreen => CurrentScreen == Screen.Form27D && Form27D is not null;

    /// <summary>Alt+B on the Form 27D screen — <b>save &amp; return</b>: writes the certificate PDF then pops back to the menu.</summary>
    public void SaveReturnForm27D()
    {
        if (!IsForm27DScreen || Form27D is null) return;
        Form27D.ExportPdf();
        BackFromPage();
    }

    /// <summary>
    /// Opens the <b>Form 27A</b> return-control-chart report page (Reports → GST Reports → TDS/TCS → Form 27A; Phase 7
    /// slice 7; catalog §13). Pick a return (26Q/27EQ) + FY/quarter and export the deterministic, de-branded control
    /// chart PDF (Ctrl+A) or save-and-return (Alt+B). <paramref name="initialForm"/> ("26Q"/"27EQ") pre-selects the
    /// return the menu entry represents. A no-op unless the corresponding tax is enabled (ER-13).
    /// </summary>
    public void OpenForm27A(string initialForm)
    {
        bool available = initialForm switch
        {
            "27EQ" => Company is { TcsEnabled: true },
            _ => Company is { TdsEnabled: true },
        };
        if (!available) return;

        var page = new Form27AViewModel(Company!, initialForm);
        OpenPageColumn(new GatewayColumn("Form 27A", page), Screen.Form27A,
            "Form 27A (Return Control Chart)", () => Form27A = page);
    }

    /// <summary>True while the Form 27A control-chart page is the active screen (drives its arrow-key nav).</summary>
    public bool IsForm27AScreen => CurrentScreen == Screen.Form27A && Form27A is not null;

    /// <summary>Alt+B on the Form 27A screen — <b>save &amp; return</b>: writes the control-chart PDF then pops back to the menu.</summary>
    public void SaveReturnForm27A()
    {
        if (!IsForm27AScreen || Form27A is null) return;
        Form27A.ExportPdf();
        BackFromPage();
    }

    // =============================================================== screen: cost reports

    /// <summary>
    /// Opens a cost-centre report (Reports → Statements of Accounts → Cost Centres → Category Summary /
    /// Cost Centre Break-up) as a page column on the right of the cascade.
    /// </summary>
    public void OpenCostReport(CostReportKind kind)
    {
        if (Company is null) return;

        var vm = new CostReportsViewModel(Company, kind);
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.CostReport, vm.Title, () => CostReports = vm);
    }

    // =============================================================== screen: interest calculation

    /// <summary>
    /// Opens the Interest Calculation report (Reports → Statements of Accounts → Interest Calculation) as a
    /// page column: each interest-enabled ledger's accrued interest (principal / rate / days / interest,
    /// right-aligned) over the company period, plus the total. A projection over the posted vouchers.
    /// </summary>
    public void OpenInterestReport()
    {
        if (Company is null) return;

        var vm = new InterestReportViewModel(Company);
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.InterestReport, vm.Title,
            () => InterestReport = vm);
    }

    // =============================================================== screen: forex gain/loss

    /// <summary>
    /// Opens the Forex Gain/Loss report (Reports → Statements of Accounts → Forex Gain/Loss) as a page
    /// column: every open foreign-currency ledger balance revalued at an editable as-of rate, with the
    /// per-ledger and net unrealized gain/loss; "Book adjustment" posts the balanced revaluation Journal.
    /// </summary>
    public void OpenForexReport()
    {
        if (Company is null) return;

        var vm = new ForexReportViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.ForexReport, vm.Title,
            () => ForexReport = vm);
    }

    // =============================================================== screen: budget variance

    /// <summary>
    /// Opens the Budget Variance report (Reports → Statements of Accounts → Budgets → Budget Variance) as a
    /// page column: for the chosen budget, each target's Budget / Actual / Variance over the budget period.
    /// </summary>
    public void OpenBudgetVariance()
    {
        if (Company is null) return;

        var vm = new BudgetVarianceViewModel(Company);
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.BudgetVariance, vm.Title,
            () => BudgetVariance = vm);
    }

    /// <summary>
    /// Opens the "Statements of Accounts → Budgets" submenu column directly (the public entry a hotkey/test
    /// uses). Rebuilds the cascade to [root → Budgets] and focuses the submenu.
    /// </summary>
    public void ShowBudgetsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statements of Accounts");
        OpenSubmenuColumn(BuildBudgetsColumn(), GatewayMenu.Budgets,
            "Gateway of Apex Solutions — Budgets");
    }

    // =============================================================== screen: banking

    /// <summary>
    /// Opens the Bank Reconciliation page (Transactions → Banking → Bank Reconciliation) as a page column:
    /// pick a bank ledger, edit each transaction's Bank Date, and see Balance-as-per-Books vs -Bank.
    /// </summary>
    public void OpenBankReconciliation()
    {
        if (Company is null) return;

        var vm = new BankReconciliationViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.BankReconciliation, vm.Title,
            () => BankReconciliation = vm);
    }

    /// <summary>
    /// Opens the Import Bank Statement page (Transactions → Banking → Import Bank Statement) as a page
    /// column: point to a CSV, run the engine auto-match, and review matched/unmatched rows.
    /// </summary>
    public void OpenBankStatementImport()
    {
        if (Company is null) return;

        var vm = new BankStatementImportViewModel(Company, _storage, onChanged: () => { });
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.BankStatementImport, vm.Title,
            () => BankStatementImport = vm);
    }

    /// <summary>
    /// Opens the "Transactions → Banking" submenu column directly (the public entry a hotkey/test uses).
    /// Rebuilds the cascade to [root → Banking] and focuses the submenu.
    /// </summary>
    public void ShowBankingMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Banking");
        OpenSubmenuColumn(BuildBankingColumn(), GatewayMenu.Banking,
            "Gateway of Apex Solutions — Banking");
    }

    /// <summary>Reconciles the current BRS page (page "Reconcile" button).</summary>
    public void ReconcileBank()
    {
        if (CurrentScreen == Screen.BankReconciliation)
            BankReconciliation?.Reconcile();
    }

    /// <summary>Runs the statement import on the current page (page "Import" button).</summary>
    public void ImportBankStatement()
    {
        if (CurrentScreen == Screen.BankStatementImport)
            BankStatementImport?.Import();
    }

    // =============================================================== screen: outstandings

    /// <summary>
    /// Opens the Outstandings page (Reports → Statements of Accounts → Outstandings → Receivables/Payables)
    /// as a page column: the open bill-wise bills for the chosen side with due date, pending amount and
    /// ageing. Spacebar multi-selects bills and Alt+A opens a settlement voucher pre-loaded with them; the page
    /// itself posts nothing.
    /// </summary>
    public void OpenOutstandings(OutstandingsKind kind)
    {
        if (Company is null) return;

        var vm = new OutstandingsViewModel(Company, kind);
        var title = kind == OutstandingsKind.Receivables ? "Outstandings — Receivables" : "Outstandings — Payables";
        OpenPageColumn(new GatewayColumn(vm.Title, vm), Screen.Outstandings, title,
            () => Outstandings = vm);
    }

    /// <summary>
    /// Opens the "Statements of Accounts → Outstandings" submenu column directly (the public entry a
    /// hotkey/test uses). Rebuilds the cascade to [root → Outstandings] and focuses the submenu.
    /// </summary>
    public void ShowOutstandingsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statements of Accounts");
        OpenSubmenuColumn(BuildOutstandingsColumn(), GatewayMenu.Outstandings,
            "Gateway of Apex Solutions — Outstandings");
    }

    /// <summary>
    /// Opens the "Statements of Accounts" hub submenu column directly (Reports → Statements of Accounts).
    /// Rebuilds the cascade to [root → Statements of Accounts] and focuses the hub.
    /// </summary>
    public void ShowStatementsOfAccountsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statements of Accounts");
        OpenSubmenuColumn(BuildStatementsOfAccountsColumn(), GatewayMenu.StatementsOfAccounts,
            "Gateway of Apex Solutions — Statements of Accounts");
    }

    /// <summary>
    /// Opens the "Statements of Accounts → Cost Centres" submenu column directly (the public entry a
    /// hotkey/test uses). Rebuilds the cascade to [root → Cost Centres] and focuses the submenu.
    /// </summary>
    public void ShowCostCentresMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        SelectRootItem("Statements of Accounts");
        OpenSubmenuColumn(BuildCostCentresColumn(), GatewayMenu.CostCentres,
            "Gateway of Apex Solutions — Cost Centres");
    }

    /// <summary>
    /// Adds a page column to the right of the cascade (replacing any existing rightmost page/submenu of
    /// the active column), sets the matching sub-screen property + <see cref="CurrentScreen"/>, and
    /// leaves the menu columns to its left visible. Falls back to a lone cascade if none exists yet.
    /// </summary>
    private void OpenPageColumn(GatewayColumn pageColumn, Screen screen, string title, Action setPage)
    {
        EnterCascade();

        // Ensure there is at least a root column to sit the page beside.
        if (Columns.Count == 0 || Columns.All(c => c.IsPage))
        {
            Columns.Clear();
            var root = BuildRootColumn();
            Columns.Add(root);
            root.SelectFirstSelectable();
            ActiveColumnIndex = 0;
        }

        // Trim after the LAST MENU column — this removes any page column that is already open (whether
        // it is the active column or sits to the right of it), so a page is REPLACED, never stacked.
        // There is therefore AT MOST ONE page column, always the rightmost.
        TrimColumnsAfter(LastMenuColumnIndex());
        ClearSubScreens();
        setPage();
        Columns.Add(pageColumn);
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = screen;
        ScreenTitle = title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// Index of the rightmost MENU column (the deepest submenu). Used to trim away any existing page
    /// column before appending a new one so opening a page always REPLACES the current page.
    /// </summary>
    private int LastMenuColumnIndex()
    {
        for (var i = Columns.Count - 1; i >= 0; i--)
            if (Columns[i].IsMenu) return i;
        return -1;
    }

    /// <summary>
    /// Removes every column after <paramref name="index"/> (keeps [0..index]).
    /// <para>WI-1 (DEFECT 2) — this is the single choke point every page-REPLACING route funnels through, so an
    /// armed Alt+C create-on-the-fly is disarmed HERE the moment its column is trimmed away. Clearing it only in
    /// <see cref="BackFromPage"/> left the request armed after any navigation and soft-locked Alt+C for the rest
    /// of the session.</para>
    /// </summary>
    private void TrimColumnsAfter(int index)
    {
        for (var i = Columns.Count - 1; i > index; i--)
            Columns.RemoveAt(i);

        AbandonCreateOnTheFlyIfColumnGone();
    }

    /// <summary>Nulls every page view model (they are mutually exclusive — at most one page column open).</summary>
    private void ClearSubScreens()
    {
        // WI-11: the open master is going away with its page view model — the Accept confirmation goes with it.
        ResetMasterAcceptPrompt();

        Reports = null;
        VoucherEntry = null;
        InventoryVoucherEntry = null;
        LedgerMaster = null;
        AccountGroupMaster = null;
        ChartOfAccounts = null;
        Outstandings = null;
        CostCategoryMaster = null;
        CostCentreMaster = null;
        CostReports = null;
        BudgetMaster = null;
        BudgetVariance = null;
        BankReconciliation = null;
        BankStatementImport = null;
        ScenarioMaster = null;
        InterestReport = null;
        CurrencyMaster = null;
        ForexReport = null;
        StockGroupMaster = null;
        StockCategoryMaster = null;
        UnitMaster = null;
        GodownMaster = null;
        StockItemMaster = null;
        BatchMaster = null;
        BatchAllocation = null;
        BomMaster = null;
        ManufacturingJournalEntry = null;
        JobWorkOrderEntry = null;
        MaterialMovementEntry = null;
        PosBilling = null;
        AlterCompany = null;
        GstConfig = null;
        VoucherNumberingConfig = null;
        GstRateSetup = null;
        Cmp08Report = null;
        Gstr4Report = null;
        Gstr9Report = null;
        Gstr9cReport = null;
        ElectronicLedgersReport = null;
        ItcSetOffReport = null;
        ItcReversalReport = null;
        Gstr2bReconReport = null;
        ItcGateReport = null;
        QrmpReport = null;
        GstAmendmentsReport = null;
        EInvoiceEWayStatusReport = null;
        ImsActions = null;
        RunSetOff = null;
        PostItcReversal = null;
        ImportGstr2b = null;
        GenerateEInvoice = null;
        GenerateEWayBill = null;
        NatureOfPaymentMaster = null;
        NatureOfGoodsMaster = null;
        TdsStatPayment = null;
        ChallanReconciliation = null;
        Form26Q = null;
        TcsStatPayment = null;
        TcsChallanReconciliation = null;
        Form27EQ = null;
        Form16A = null;
        Form27D = null;
        Form27A = null;
        PriceLevels = null;
        PriceLists = null;
        ReorderLevels = null;
        EmployeeCategoryMaster = null;
        EmployeeGroupMaster = null;
        EmployeeMaster = null;
        PayrollUnitMaster = null;
        AttendanceTypeMaster = null;
        PayHeadMaster = null;
        SalaryDetails = null;
        AttendanceVoucher = null;
        PayrollVoucher = null;
        PfEcrReport = null;
        EsiContributionReport = null;
        ProfessionalTaxRegister = null;
        GratuityProvisionRegister = null;
        BonusRegister = null;
        TaxDeclarationMaster = null;
        Form24Q = null;
        Form16 = null;
        ReportConfig = null;
        BasisOfValues = null;
        ReportSortFilter = null;
        AddComparisonColumn = null;
        AutoColumns = null;
        SaveView = null;
        SavedViews = null;
        GoTo = null;
        PrintPreview = null;
        PrintConfigPanel = null;
        ExportPanel = null;
        ExportDataPanel = null;
        ImportDataPanel = null;
        BackupCompanyPanel = null;
        RestoreCompanyPanel = null;
        EmailCompose = null;
        SmtpSettings = null;
        LedgerVouchers = null;
        VoucherDetail = null;
    }

    /// <summary>Enters cascade mode (Gateway) — the centred pre-company menu is hidden.</summary>
    private void EnterCascade()
    {
        Menu.Clear();
        IsGatewayCascade = true;
    }

    /// <summary>Leaves cascade mode — the centred menu is shown again (pre-company screens).</summary>
    private void LeaveCascade()
    {
        Columns.Clear();
        // WI-1 (DEFECT 2) — leaving the cascade takes any create column with it; disarm so Alt+C is not soft-locked.
        AbandonCreateOnTheFlyIfColumnGone();
        IsGatewayCascade = false;
    }

    // =============================================================== form key helpers

    /// <summary>Ctrl+A on a form page: accept the current voucher / create the current ledger.</summary>
    public void AcceptCurrent() => ActivateSelected();

    /// <summary>
    /// ABANDONS the in-progress entry screen — discards what is being keyed (no save) and pops its page column.
    /// This is the <b>Escape</b> verb and the verb behind the six on-screen "Cancel" buttons.
    ///
    /// <para><b>🔴 It is NOT voucher cancellation, and it used to be called <c>CancelVoucher</c>.</b> Under that
    /// name it was bound to Alt+X app-wide, which spent the reference product's voucher-CANCEL accelerator on
    /// throwing away the screen. Phase 10.11 S3 took Alt+X back for
    /// <see cref="RequestCancelHighlightedVoucher"/> and renamed this method to what it actually does, so the two
    /// verbs can never again be confused by their names. Renamed rather than deleted <b>deliberately</b>: the
    /// plan's wording ("delete it so the compile breaks") would have destroyed a live feature — the rename breaks
    /// the compile at every stale caller just the same, and the behaviour survives.</para>
    /// </summary>
    public void AbandonEntry()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.Cancel();
        else if (CurrentScreen == Screen.InventoryVoucherEntry)
            InventoryVoucherEntry?.Cancel();
        else if (CurrentScreen == Screen.ManufacturingJournalEntry)
            ManufacturingJournalEntry?.Cancel();
        else if (CurrentScreen == Screen.JobWorkOrderEntry)
            JobWorkOrderEntry?.Cancel();
        else if (CurrentScreen == Screen.MaterialMovementEntry)
            MaterialMovementEntry?.Cancel();
        else if (CurrentScreen == Screen.PosBilling)
            PosBilling?.Cancel();
        else if (CurrentScreen is Screen.LedgerMaster or Screen.AccountGroupMaster or Screen.CostCategoryMaster
                 or Screen.CostCentreMaster or Screen.BudgetMaster or Screen.ScenarioMaster
                 or Screen.CurrencyMaster or Screen.StockGroupMaster or Screen.StockCategoryMaster
                 or Screen.UnitMaster or Screen.GodownMaster or Screen.StockItemMaster
                 or Screen.BatchMaster or Screen.BatchAllocation
                 or Screen.BomMaster or Screen.ReorderLevelsMaster
                 or Screen.GstConfig or Screen.GstRateSetup
                 or Screen.NatureOfPaymentMaster or Screen.NatureOfGoodsMaster
                 or Screen.TdsStatPayment or Screen.TcsStatPayment
                 or Screen.EmployeeCategoryMaster or Screen.EmployeeGroupMaster or Screen.EmployeeMaster
                 or Screen.PayrollUnitMaster or Screen.AttendanceTypeMaster
                 or Screen.PayHeadMaster or Screen.SalaryStructureMaster)
            BackFromPage();
    }

    // ====================================================== Phase 10.11 S3: Alt+X — cancel a POSTED voucher

    /// <summary>
    /// The voucher a raised cancellation confirmation will act on, or <see cref="Guid.Empty"/> when the
    /// confirmation currently up (if any) is the ordinary master Accept.
    ///
    /// <para>This is what makes the WI-11 confirmation ONE channel rather than two. A second flag + a second
    /// pair of Y/N key arms would have to be inserted somewhere in the window's first-match-wins chain, and the
    /// Alt+Y hole S1 closed (a stray accelerator answering a confirmation nobody read) is exactly the class of
    /// defect that duplication produces. Arming this id re-points the SAME prompt at a different action;
    /// <see cref="ConfirmMasterAccept"/> branches on it and <see cref="ResetMasterAcceptPrompt"/> disarms it.</para>
    /// </summary>
    private Guid _pendingCancelVoucherId;

    /// <summary>
    /// <b>Alt+X — raise the single Y/N confirmation for cancelling the voucher highlighted in the live report.</b>
    /// Returns <c>true</c> when the prompt was raised; <c>false</c> (a quiet no-op, or a named message) otherwise.
    ///
    /// <para><b>🔴 FIDELITY — UNVERIFIED-BY-DESIGN, our choice, corpus silent.</b> The source corpus says only
    /// that Alt+X cancels a voucher (Book PDF p.437); it nowhere describes what cancelling MEANS. Retaining the
    /// voucher's number, leaving the books unaffected, greying the row, over-printing "CANCELLED" and <b>this
    /// prompt's wording</b> are all OURS. The confirmation is deliberately a <b>SINGLE</b> prompt: the corpus's
    /// published double confirmation ("… Yes or No?" then "Are you sure Yes or No?") is attested for a master and
    /// for a group company, not for a voucher, and we decline to copy it across by analogy.</para>
    ///
    /// <para><b>The four gates, and why each is here rather than in the key handler.</b> The window's Alt+X arm
    /// decides only that the keystroke is ours (report context, not typing, no open picker, no Ctrl). Everything
    /// that depends on DATA is decided here so the on-screen route and any future button route cannot diverge
    /// from the accelerator:
    /// <list type="bullet">
    ///   <item>no company open, or no live report — inert;</item>
    ///   <item>a confirmation is already up — inert, so Alt+X cannot stack a second prompt over the first;</item>
    ///   <item>the highlighted row resolves to no voucher — a header, a total, an empty-state note, or no
    ///         selection at all. One lookup answers all of them, including the <see cref="Guid.Empty"/> a
    ///         non-drillable row carries, because no posted voucher can ever hold that id. A separate
    ///         <c>id == Guid.Empty</c> clause stood here and was REMOVED after a mutation run showed nothing
    ///         could distinguish it from the lookup: a guard no test can fail is dead code wearing the costume
    ///         of safety;</item>
    ///   <item>the voucher is ALREADY cancelled — refused with a named message rather than silently re-armed.
    ///         Re-cancelling is harmless to the books, but a prompt that asks a question whose answer changes
    ///         nothing trains an operator to answer prompts without reading them;</item>
    ///   <item>🔴 the voucher still holds a LIVE IRN or a LIVE e-Way Bill — refused, naming the portal document,
    ///         because cancelling it locally is a ONE-WAY DOOR that strands the statutory one. See
    ///         <see cref="LiveStatutoryDocumentBlocker"/>.</item>
    /// </list></para>
    /// </summary>
    public bool RequestCancelHighlightedVoucher()
    {
        if (Company is null || Reports is null) return false;
        if (IsAcceptPromptOpen) return false;

        // A previous outcome's notice goes before a new question is asked: the two bars share the status-bar row,
        // so leaving a stale notice up would paint it underneath the confirmation the operator is being asked.
        Notice = string.Empty;

        if (Reports.SelectedRow is not { DrillVoucherId: var id }) return false;
        if (Company.FindVoucher(id) is not { } voucher) return false;

        if (voucher.Cancelled)
        {
            RaiseLifecycleNotice($"{VoucherLabel(voucher)} is already cancelled.");
            return false;
        }

        if (LiveStatutoryDocumentBlocker(voucher) is { } blocker)
        {
            RaiseLifecycleNotice($"Cannot cancel {VoucherLabel(voucher)}: it carries {blocker}. "
                              + "Cancel that at the portal first, then cancel the voucher.");
            return false;
        }

        _pendingCancelVoucherId = id;
        // 🔴 UNVERIFIED-BY-DESIGN — ours, corpus silent (the corpus says only "To cancel a voucher", Book p.437).
        // WHAT THIS SENTENCE MUST NOT SAY, and did: "the books are unaffected". It is the exact opposite of what
        // cancelling does — every balance the voucher touched MOVES, which is the whole point of the verb and is
        // what the design's own T-5 requires ("every balance moves by exactly the invoice"). There is no un-cancel
        // (ORCHESTRATOR RULING 3) and no alteration route yet (S5), so an operator who read the old wording as "my
        // figures will not move" and pressed Y had no way back. `The_prompt_tells_the_truth_about_the_books` now
        // asserts the wording AND the balance movement in one case so the two cannot drift apart again.
        AcceptPromptText = $"Cancel {VoucherLabel(voucher)}? "
                           + "The number is kept, but the entry stops counting — every balance it touched will "
                           + "move. This cannot be undone. (Y/N)";
        IsAcceptPromptOpen = true;
        return true;
    }

    /// <summary>
    /// The live portal document that makes cancelling <paramref name="voucher"/> a one-way door, or <c>null</c>
    /// when there is none.
    ///
    /// <para>🔴 <b>Why Cancel refuses instead of proceeding.</b> <c>LedgerService.Cancel</c> sets a flag and
    /// nothing else, so a <c>Generated</c> <c>EInvoiceRecord</c> keeps its IRP-issued IRN, AckNo and AckDate. But
    /// the ONLY route in the app to cancel that IRN at the IRP is the Generate E-Invoice screen, and its
    /// <c>Rebuild()</c> lists <c>Vouchers.Where(v =&gt; !v.Cancelled)</c> — so the moment the voucher is cancelled
    /// locally it LEAVES that screen and the IRN can never be cancelled from here again. The app would be holding
    /// a live IRN for a document that is void in its own books, with no way back.
    /// <c>GenerateEWayBillViewModel.Rebuild()</c> carries the identical filter, so the e-Way Bill has the identical
    /// trap.</para>
    ///
    /// <para><b>Refusing is not a dead end — it is an ORDER.</b> The voucher is still live, so it is still listed
    /// on both portal screens: cancel the IRN / EWB there (24-hour window), which moves the record to
    /// <c>Cancelled</c>, and this gate lifts. The alternative — admitting locally-cancelled vouchers back into
    /// those screens — would leave the two states drifting apart in every report meanwhile. This mirrors the
    /// refusal §6.4 item 3 already specifies for Delete on a voucher carrying an e-invoice/e-Way record.</para>
    ///
    /// <para><b>Scope.</b> Only <c>Generated</c> blocks. <c>Pending</c> was never sent to the portal, and
    /// <c>Cancelled</c> / <c>Failed</c> / <c>NotApplicable</c> hold nothing that can be stranded.</para>
    /// </summary>
    private string? LiveStatutoryDocumentBlocker(Voucher voucher)
    {
        if (Company is null) return null;

        var irn = Company.EInvoiceRecords.FirstOrDefault(
            r => r.SourceVoucherId == voucher.Id && r.Status == EInvoiceStatus.Generated);
        var ewb = Company.EWayBillRecords.FirstOrDefault(
            r => r.SourceVoucherId == voucher.Id && r.Status == EWayStatus.Generated);

        return (irn, ewb) switch
        {
            (not null, not null) => "a live IRN and a live e-Way Bill",
            (not null, null)     => "a live IRN",
            (null, not null)     => "a live e-Way Bill",
            _                    => null,
        };
    }

    /// <summary>"Sales No. 3 dated 15-Apr-2024" — the operator-facing identity of a voucher, used in the
    /// cancellation prompt and its result message so both name the same document the report row shows.</summary>
    private string VoucherLabel(Voucher voucher)
    {
        var typeName = Company?.FindVoucherType(voucher.TypeId)?.Name ?? "Voucher";
        var number = Company?.FormatVoucherNumber(voucher) ?? string.Empty;
        var numberPart = string.IsNullOrWhiteSpace(number) ? string.Empty : $" No. {number}";
        return $"{typeName}{numberPart} dated {voucher.Date:dd-MMM-yyyy}";
    }

    /// <summary>
    /// "Y" on the cancellation confirmation: marks the armed voucher cancelled through the engine, persists, and
    /// rebuilds the live report so the row greys immediately.
    ///
    /// <para>The engine call is <c>LedgerService.Cancel</c> and NOTHING else — S3 adds no engine semantics. The
    /// voucher keeps its number (the engine sets a flag and never touches <c>Number</c>) and drops out of every
    /// balance because <c>LedgerBalances.CountsAsOf</c> and <c>ItemInvoiceStock.Counts</c> already exclude
    /// cancelled vouchers. Persisting through <c>_storage.Save</c> is the same route
    /// <see cref="ConvertMemorandum"/> takes; the store is a snapshot, so a save is how the flag survives.</para>
    ///
    /// <para>🔴 <b>A FAILED SAVE ROLLS THE FLAG BACK.</b> The engine mutates the in-memory aggregate and the save
    /// happens after it, so a save that throws used to leave the books cancelled in memory, nothing on disk, the
    /// report un-rebuilt and the row still black — the aggregate silently AHEAD of the store, which is the state
    /// every later save then carries. Two things were wrong and both are fixed here: the flag is restored in the
    /// catch, and the catch actually catches what <c>_storage.Save</c> throws. <c>CompanyStorage.Save</c> opens
    /// with <c>company.EnsureValid()</c>, and <c>Company.EnsureValid</c> throws <b>ArgumentException</b> (a bad
    /// PIN, or books-begin before the year start — its own doc says such a book "loads without complaint … and
    /// then the next save on any screen throws"). The old <c>catch (InvalidOperationException)</c> never saw it, so
    /// the one genuinely reachable failure on this path was an unhandled exception out of the window's key handler
    /// with the voucher already flagged. Restoring <c>Cancelled = false</c> is a ROLLBACK of a transaction that did
    /// not commit — it is NOT an un-cancel feature (ORCHESTRATOR RULING 3 ships none) and no UI route reaches
    /// it.</para>
    ///
    /// <para>🔴 <b>v52 — THE ROLLBACK NOW UNDOES BOTH HALVES.</b> <c>LedgerService.Cancel</c> also appends a
    /// <c>VoucherEditLogEntry</c>, and a rollback that put the flag back while leaving the log line standing would
    /// have left this company asserting a cancellation that never reached disk — which the NEXT successful save on
    /// any screen would then persist. So the failure arm calls <c>LedgerService.DiscardUncommittedCancel</c>,
    /// which clears the flag and drops that one entry together. It is also now the ONLY way this screen can clear
    /// the flag at all: <c>Voucher.Cancelled</c>'s setter is <c>internal</c>.</para>
    /// </summary>
    private void CancelPendingVoucher(Guid voucherId)
    {
        if (Company is null) return;

        var voucher = Company.FindVoucher(voucherId);
        var service = new Apex.Ledger.Services.LedgerService(Company);

        // v52 — the edit-log entry Cancel appends. Held so the failure arm can discard it: the rollback has to
        // undo BOTH halves of the verb, or the log keeps a line saying this voucher was cancelled when it was not.
        Apex.Ledger.Domain.VoucherEditLogEntry? logEntry = null;
        try
        {
            logEntry = service.Cancel(voucherId);
            _storage.Save(Company);
        }
        // 🔴 W0-13's shared predicate, NOT the narrow `is InvalidOperationException or ArgumentException` filter
        // this line used to carry. `SaveFailure.IsReportable` exists precisely to replace that shape: it also
        // admits DbException (SqliteException — BUSY from a second instance, READONLY, FULL), IOException,
        // UnauthorizedAccessException and OverflowException, none of which the old filter saw. With the narrow
        // filter a locked or read-only `.db` threw straight out of the window's key handler with the voucher
        // already flagged in memory and NOTHING on the notice bar. The rollback above runs either way; only the
        // report-vs-crash decision consults the predicate, which is the separation SaveFailure's own doc requires.
        catch (Exception ex) when (SaveFailure.IsReportable(ex))
        {
            // `logEntry` is null only when Cancel itself threw (an unknown voucher — also reportable), in which
            // case nothing was flagged and nothing was logged, so there is nothing to undo.
            if (logEntry is not null) service.DiscardUncommittedCancel(voucherId, logEntry);
            RaiseLifecycleNotice($"Cannot cancel: {ex.Message}");
            return;
        }

        RaiseLifecycleNotice(voucher is null
            ? "Voucher cancelled."
            : $"{VoucherLabel(voucher)} cancelled — the number is kept and every balance it touched has moved.");

        // Rebuild the live report in place so the cancelled row greys and its amount leaves the running figures
        // without the operator having to re-open the report.
        Reports?.Show(Reports.Kind);
    }

    /// <summary>
    /// 🔴 <b>The one surface the lifecycle verbs can actually be SEEN on.</b> Every outcome of Alt+X (cancel) and
    /// Alt+D (delete) — the refusals, a failed write and the success — reports through here.
    ///
    /// <para><b>Why not <see cref="Message"/>.</b> Alt+X works on exactly one screen, the live report page, and
    /// that page CANNOT RENDER <see cref="Message"/>: the report <c>DataTemplate</c> is typed
    /// <c>x:DataType="vm:ReportsViewModel"</c>, which has no <c>Message</c> property at all, and every
    /// <c>{Binding Message}</c> in the window sits in a master/entry/action panel. So the "already cancelled"
    /// refusal, the live-IRN refusal and — worst — a FAILED cancel were all indistinguishable from a dead key:
    /// no bar, no text, nothing. This bar is declared at window level beside the WI-11 confirmation, so it is
    /// visible on every screen including the report.</para>
    ///
    /// <para><b>Why not the WI-11 amber bar itself.</b> That bar is a QUESTION channel — while it is up the Y/N
    /// arms are live. Painting a notice there would leave a bare <c>Y</c> answering a statement, which is the Alt+Y
    /// hole S1 closed. <see cref="Message"/> is still set alongside, so the routes that DO render it keep
    /// working and nothing that reads it changes.</para>
    /// </summary>
    private void RaiseLifecycleNotice(string text)
    {
        Message = text;
        Notice = text;
    }

    /// <summary>
    /// A window-level notice line, rendered in the status-bar row beside the WI-11 confirmation. Set only by the
    /// Phase 10.11 lifecycle verbs — S3 cancel and S4 delete (see <see cref="RaiseLifecycleNotice"/>) — and cleared
    /// on any change of screen, because a notice belongs to the screen it was raised on.
    /// </summary>
    [ObservableProperty] private string _notice = string.Empty;

    // ====================================================== Phase 10.11 S4: Alt+D — DELETE a posted voucher/master

    /// <summary>What an armed Alt+D confirmation is about to delete. <see cref="None"/> means the confirmation
    /// currently up (if any) belongs to another verb.</summary>
    /// <summary>
    /// What an armed confirmation would delete. <b><see cref="Company"/> is the odd one out and deliberately so:</b>
    /// every other member — <see cref="Voucher"/>, <see cref="Ledger"/>, <see cref="Group"/>,
    /// <see cref="StockItem"/> and <see cref="PayrollMaster"/> — names a row INSIDE the open book, keyed by
    /// <see cref="_pendingDeleteId"/> and finished by a <c>_storage.Save(Company)</c>. A company deletion removes
    /// the <c>.db</c> FILE, carries no Guid (<c>Guid.Empty</c> is armed), and must never save — saving the
    /// aggregate it is deleting would re-create the book it just removed.
    /// <see cref="PerformPendingDeletion"/> branches it out before the shared machinery.
    ///
    /// <para>🔴 <b><see cref="PayrollMaster"/> and <see cref="Company"/> were added by two different branches that
    /// merged clean.</b> Git took one side of this enum and would have DROPPED the other silently; both were
    /// restored by hand at the merge. Neither is redundant: <see cref="PayrollMaster"/> is the payroll-master
    /// arm of census row 7.16, and <see cref="Company"/> is the delete half of row 1.4. If a later merge ever
    /// presents this line as a conflict again, the answer is to keep EVERY member, never to pick a side.</para>
    /// </summary>
    private enum DeletionTarget { None, Voucher, Ledger, Group, StockItem, PayrollMaster, Company }

    /// <summary>
    /// The armed deletion — ONE slot, on the ONE confirmation channel, exactly as S3's
    /// <see cref="_pendingCancelVoucherId"/> is. A second flag plus a second pair of Y/N key arms would have to be
    /// inserted into the window's first-match-wins chain, and the Alt+Y hole S1 closed (a stray accelerator
    /// answering a confirmation nobody read) is the class of defect that duplication produces — with a
    /// DESTRUCTIVE verb behind it this time. <see cref="ConfirmMasterAccept"/> branches on the kind and
    /// <see cref="ResetMasterAcceptPrompt"/> disarms it.
    /// </summary>
    private DeletionTarget _pendingDeleteKind;
    private Guid _pendingDeleteId;

    /// <summary>
    /// The five surfaces Alt+D is offered on (design §6.4 item 6): the live report page (the Day Book carries the
    /// voucher rows), the register drill and the voucher-detail column beneath it, the Chart of Accounts, and the
    /// Stock Item master's existing-items list.
    ///
    /// <para>🔴 <b>Why the report clause is <see cref="IsLiveReportPage"/> and not <see cref="IsReportContext"/>.</b>
    /// Inherited straight from the S3 review finding: <c>IsReportContext</c> is deliberately TRUE while an F12
    /// config, an Alt+F12 sort/filter, an Alt+A add-voucher picker, an Alt+K saved-views panel or a Print Preview
    /// column is stacked over the report, because the report-PARAMETER shortcuts must keep acting on the report
    /// underneath. A destructive verb written on it fires for the row BEHIND whichever column the operator is
    /// standing in. <c>IsPickerOpen</c> cannot see that — it looks for an open ComboBox popup, not a Miller
    /// column.</para>
    ///
    /// <para>The two drill columns are named EXPLICITLY rather than covered by a "report is bound" test, for the
    /// same reason: <see cref="Screen.LedgerVouchers"/> and <see cref="Screen.VoucherDetail"/> are the two screens
    /// that ARE the active column and DO own a voucher, and nothing stacks over them today.</para>
    ///
    /// <para>🔴 <b>THE STOCK ITEM CLAUSE EXCLUDES AN OPEN ALTERATION, and that is a fix.</b>
    /// <c>ShowStockItemAlter</c> opens the alteration column under the SAME <see cref="Screen.StockItemMaster"/>
    /// value, so <see cref="IsStockItemMasterScreen"/> cannot tell Creation from Alteration — and Alt+D therefore
    /// deleted the very master the open form was editing. Measured: the item went, the caption still read "Stock
    /// Item Alteration", the operator's keyed changes were still in the form, and the Ctrl+A that would have saved
    /// them was afterwards a completely silent no-op. §6.4 item 6 offers Alt+D on "the Stock Item master's
    /// existing-items LIST", not on an open alteration of a row in it, so the narrower reading is also the one the
    /// design asked for.</para>
    ///
    /// <para><b>The <c>Company is not null</c> clause is a precondition, honestly labelled.</b> It is not
    /// independently falsifiable — every screen below requires an open company to reach, and nothing in the
    /// application ever sets <c>Company</c> back to null — and <see cref="RequestDeleteHighlighted"/> re-tests it
    /// anyway. It is KEPT rather than deleted for a mutation score: this property gates the app's one destructive
    /// accelerator, and a null-safety precondition is not the same category as a guard whose comment claims a
    /// mechanism it cannot deliver.</para>
    /// </summary>
    public bool IsDeleteTargetPage =>
        Company is not null
        && (IsLiveReportPage
            || (CurrentScreen == Screen.LedgerVouchers && LedgerVouchers is not null)
            || (CurrentScreen == Screen.VoucherDetail && VoucherDetail is not null)
            || IsChartOfAccountsScreen
            || (IsStockItemMasterScreen && StockItemMaster is { IsAltering: false })
            // 7.16 — the payroll masters, on the SAME rule as the Stock Item master: the existing-list is a
            // delete surface, an OPEN ALTERATION of one of its rows is not.
            || PayrollMasterScreen is { IsAltering: false });

    /// <summary>
    /// <b>Alt+D — raise the single Y/N confirmation for deleting whatever the current surface has highlighted.</b>
    /// Returns <c>true</c> when the prompt was raised; <c>false</c> (a quiet no-op, or a named refusal on the
    /// notice bar) otherwise.
    ///
    /// <para><b>🔴 FIDELITY (R7) — THE PROMPT COUNT IS A CONFLICT, NOT A SILENCE. THIS RECORD WAS WRONG AND IS
    /// REWRITTEN.</b> The corpus settles that Alt+D is Delete and that a ledger carrying transactions cannot be
    /// deleted (STUDY-GUIDE PDF p.67). The referential guard, the numbering guard, the bill-wise guard, offering
    /// Cancel as the remedy, the five surfaces and every string below remain
    /// <b>UNVERIFIED-BY-DESIGN — ours, corpus silent</b>.
    /// <br/><b>The number of confirmations is NOT one of them.</b> This comment used to say the published DOUBLE
    /// confirmation "is attested for a MASTER and for a GROUP COMPANY and is <i>not attested for a voucher</i>",
    /// and filed the whole slice's SINGLE prompt — including the three MASTER routes below — under a
    /// decline-to-extend from that silence. Re-extracted first-hand with <c>pdftotext -raw</c>, both halves of that
    /// sentence are wrong:
    /// <list type="bullet">
    ///   <item><b>A MASTER is attested BOTH ways, in conflict.</b> BOOK PDF p.21 gives the ledger recipe as
    ///     <i>"… &gt; Alt+D &gt; Press Two times Enter"</i> (double); STUDY-GUIDE PDF p.67 gives the same object as
    ///     <i>"Press Alt+D supply Yes to confirm Deletion"</i> (single). So the three master routes are a DIVERGENCE
    ///     FROM AN ATTESTED SCOPE, not a decline-to-extend from silence.</item>
    ///   <item><b>A voucher is NOT silent either.</b> BOOK PDF pp.22-23 carries a heading that reads
    ///     <i>"How to Delete Voucher …?"</i> over the same <i>"Alt+D &gt; Press Two times Enter"</i> recipe — under
    ///     a path that then says <c>Alter &gt; Voucher type</c>, so the source contradicts itself within one entry.
    ///     Low-quality attestation is still attestation; "not attested" was not supportable.</item>
    ///   <item>The GROUP COMPANY double IS attested, unambiguously and with its wording (STUDY-GUIDE PDF p.277:
    ///     <i>"Delete Yes or No?"</i> then <i>"Are you sure Yes or No?"</i>). Company deletion is out of S4 by
    ///     ruling, so nothing here contradicts it.</item>
    /// </list>
    /// <b>🔴 WHAT SHIPS, AND ON WHAT BASIS — SETTLED BY THE USER 2026-08-18, IN TWO RULINGS. THE BEHAVIOUR IS
    /// UNCHANGED (one prompt on all five routes, exactly as S4 shipped it); ONLY THE RECORD CHANGES, AND IT
    /// CHANGES INTO TWO RECORDS.</b>
    /// <list type="bullet">
    ///   <item><b>(A) THE VOUCHER ROUTES — OUR DECISION AGAINST WEAK, SELF-CONTRADICTORY ATTESTATION.</b>
    ///     BOOK PDF pp.22-23 attest the double prompt for a voucher and contradict themselves doing it (see the
    ///     bullet above). We keep ONE prompt and record it as a decision taken <i>against</i> that attestation.
    ///     It is explicitly <b>not</b> "corpus silent" and <b>not</b> a
    ///     decline-to-extend-an-unattested-behaviour — <b>the whole earlier D-6 record rested on an absence that
    ///     turned out not to exist.</b></item>
    ///   <item><b>(B) THE THREE MASTER ROUTES (ledger, group, stock item) — A DELIBERATE DIVERGENCE FROM AN
    ///     ATTESTED SCOPE.</b> Here the double prompt IS cleanly attested (BOOK PDF p.21 for a ledger,
    ///     STUDY-GUIDE PDF p.277 with its wording for a Group Company). We ship one prompt anyway and record it
    ///     as a divergence from an attested scope — a different claim, on different evidence, from (A).
    ///     <i>STUDY-GUIDE p.67's single prompt for the same ledger object narrows the divergence and does not
    ///     change its category: we do not get to pick the friendly source and call the result fidelity.</i></item>
    /// </list>
    /// <b>Keep (A) and (B) apart.</b> They are defended by different pages, falsified by different findings and
    /// re-opened by different evidence; conflating them is the exact R7 defect a review lens caught on S3.
    /// Anything restating one must restate both, or say which it means.
    /// <br/><b>SUPERSEDED, quoted so the category history stays legible:</b> this paragraph read <i>"a SINGLE
    /// prompt on all five routes … a CONFLICT RESOLVED IN FAVOUR OF ONE ATTESTED SOURCE. That is a third R7
    /// category"</i>. One category is replaced by the two above.</para>
    ///
    /// <para><b>The gates, and why they live here rather than in the key handler.</b> The window's Alt+D arm
    /// decides only that the keystroke is ours (a delete-capable surface, not typing, no open picker, exactly
    /// Alt). Everything that depends on DATA is decided here, so an on-screen route added later cannot diverge
    /// from the accelerator:
    /// <list type="bullet">
    ///   <item>no company open — inert;</item>
    ///   <item>a confirmation is already up — inert, so Alt+D cannot stack a second prompt over the first, and
    ///         cannot re-point a live cancel confirmation at a delete;</item>
    ///   <item>nothing highlighted, or a row that resolves to no master/voucher — a quiet no-op;</item>
    ///   <item>🔴 the guards in <see cref="MasterDeletionRules"/> — refused with THEIR message, which names the
    ///         count of blocking documents, or (for a filed statutory document) offers Cancel instead. The
    ///         guard is asked BEFORE the question is put: a confirmation for something that cannot happen trains
    ///         an operator to answer prompts without reading them.</item>
    /// </list></para>
    /// </summary>
    public bool RequestDeleteHighlighted()
    {
        if (Company is null) return false;
        if (IsAcceptPromptOpen) return false;

        // 🔴 The previous outcome's notice is cleared in `Arm`, at the moment a NEW QUESTION actually goes up —
        // not here. Clearing it here wiped the refusal the operator was reading whenever the route then returned
        // false because nothing was highlighted: they pressed Alt+D on a header row and their diagnosis vanished
        // with nothing in its place. The invariant that matters is "a stale outcome never shares the status-bar row
        // with a live confirmation", and arming is exactly when that becomes possible.
        return CurrentScreen switch
        {
            // `Reports?` rather than `Reports!` + a `when IsLiveReportPage` clause: on Screen.Report the clause
            // reduced to a null test (IsLiveReportPage IS `Reports is not null && CurrentScreen == Screen.Report`,
            // and the switch has already established the second half), so it was an unfalsifiable guard spelled as
            // a screen predicate. The null-conditional says the same thing, cannot NRE, and leaves nothing dead.
            Screen.Report => RequestDeleteVoucher(Reports?.SelectedRow?.DrillVoucherId),
            Screen.LedgerVouchers => RequestDeleteVoucher(LedgerVouchers?.SelectedRow?.DrillVoucherId),
            Screen.VoucherDetail => RequestDeleteVoucher(VoucherDetail?.VoucherId),
            Screen.ChartOfAccounts => RequestDeleteChartRow(),
            Screen.StockItemMaster => RequestDeleteStockItemRow(),
            Screen.EmployeeCategoryMaster or Screen.EmployeeGroupMaster or Screen.EmployeeMaster
                or Screen.PayrollUnitMaster or Screen.AttendanceTypeMaster or Screen.PayHeadMaster
                => RequestDeletePayrollMasterRow(),
            _ => false,
        };
    }

    /// <summary>
    /// <b>Alt+D on the Company Alteration screen — arms the confirmation for deleting the OPEN COMPANY</b>
    /// (census row 1.4). Returns <c>true</c> when the question went up.
    ///
    /// <para><b>FIDELITY (R7; RULING 14 — the corpus is gone, so this is the vendor's own help).</b>
    /// <i>help.tallysolutions.com/…/set-up-company-tally/</i> deletes a company by
    /// <i>"Alt+K (Company) &gt; Alter. In the Company Alteration screen, press Alt+D."</i> The SCREEN and the
    /// CHORD are both attested for precisely this act. Only the route to the screen differs — ours is
    /// Gateway → Masters → Alter Company, because the Alt+K top menu is not built and its chord sits inside open
    /// user ruling U-6.</para>
    ///
    /// <para><b>The chord was FREE, so nothing is displaced.</b> <see cref="IsDeleteTargetPage"/>'s five surfaces
    /// exclude <see cref="Screen.AlterCompany"/>, and the bare-letter <c>D</c> quick-jump (Day Book) requires
    /// <c>KeyModifiers.None</c>. The master/voucher Alt+D keeps its meaning everywhere it already had one, which
    /// <c>AltD_elsewhere_still_deletes_the_master_not_the_company</c> pins.</para>
    ///
    /// <para>🔴 <b>THERE IS NO <c>MasterDeletionRules</c> GUARD HERE, AND THAT IS OURS — RULING 9.</b> Every other
    /// arm refuses a master that something else points at; a company is what everything else points AT, so there
    /// is no referential guard to run and no wider book to be inconsistent with. No admissible source says a
    /// company carrying vouchers cannot be deleted, and inventing that refusal would strand the operator with a
    /// book they cannot remove. <b>The confirmation is therefore the ONLY guard</b>, which is why it names the
    /// company and says in words that the whole book goes.</para>
    ///
    /// <para><b>Not gated on <c>IsTyping</c>, unlike the master arm, and the difference is real rather than an
    /// oversight.</b> The master arm guards it because the caret sits in a form over a LIST and the key would
    /// otherwise hit the row behind. This screen has no list behind it: its subject IS the company, so Alt+D can
    /// only mean one thing wherever the caret is. Guarding it would make the attested chord dead in ordinary use,
    /// since the operator reaches this screen precisely to type in its fields.</para>
    /// </summary>
    public bool RequestDeleteOpenCompany()
    {
        if (Company is null) return false;
        if (IsAcceptPromptOpen) return false;
        if (CurrentScreen != Screen.AlterCompany) return false;

        return Arm(DeletionTarget.Company, Guid.Empty,
            $"Delete company '{Company.Name}'? The whole book — every master, voucher and report in it — is "
            + "removed from disk permanently, and there is no undo. (Y/N)");
    }

    /// <summary>Arms the confirmation for a posted voucher, after the S4 guards accept it.</summary>
    private bool RequestDeleteVoucher(Guid? voucherId)
    {
        if (voucherId is not { } id) return false;
        if (Company!.FindVoucher(id) is not { } voucher) return false;

        if (!GuardsAllowDeletion(() => MasterDeletionRules.EnsureVoucherDeletable(Company, voucher),
                                 CancelRoutingFor(voucher))) return false;

        return Arm(DeletionTarget.Voucher, id,
            $"Delete {VoucherLabel(voucher)}? The entry and every line on it are removed from the books "
            + "permanently, and there is no undo. (Y/N)");
    }

    /// <summary>Arms the confirmation for the Chart of Accounts' highlighted row — a ledger row or a group row,
    /// resolved exactly the way <see cref="AlterHighlightedChartRow"/> resolves it, so Alt+D and Enter can never
    /// disagree about which master the highlight means.</summary>
    private bool RequestDeleteChartRow()
    {
        if (ChartOfAccounts?.HighlightedRow is not { } row) return false;

        if (row.LedgerId is { } ledgerId && Company!.FindLedger(ledgerId) is { } ledger)
        {
            if (!GuardsAllowDeletion(() => MasterDeletionRules.EnsureLedgerDeletable(Company, ledger))) return false;
            return Arm(DeletionTarget.Ledger, ledgerId,
                $"Delete ledger '{ledger.Name}'? This cannot be undone. (Y/N)");
        }

        if (row.GroupId is { } groupId && Company!.FindGroup(groupId) is { } group)
        {
            if (!GuardsAllowDeletion(() => MasterDeletionRules.EnsureGroupDeletable(Company, group))) return false;
            return Arm(DeletionTarget.Group, groupId,
                $"Delete group '{group.Name}'? This cannot be undone. (Y/N)");
        }

        return false;
    }

    /// <summary>Arms the confirmation for the Stock Item master's highlighted existing-item row — the same row
    /// Ctrl+Enter opens for alteration.</summary>
    private bool RequestDeleteStockItemRow()
    {
        if (StockItemMaster?.HighlightedRow is not { } row) return false;
        if (Company!.FindStockItem(row.StockItemId) is not { } item) return false;

        if (!GuardsAllowDeletion(() => MasterDeletionRules.EnsureStockItemDeletable(Company, item))) return false;

        return Arm(DeletionTarget.StockItem, row.StockItemId,
            $"Delete stock item '{item.Name}'? This cannot be undone. (Y/N)");
    }

    /// <summary>
    /// Runs one <see cref="MasterDeletionRules"/> guard and turns its refusal into a notice. Returns <c>true</c>
    /// when the guard passed.
    ///
    /// <para>The guards are pure and communicate by THROWING (the <see cref="MasterAlterationRules"/> shape they
    /// are built on), and their messages are written to be read by an operator — they already name the count of
    /// blocking documents and, for a filed statutory document, the remedy. So the refusal is surfaced verbatim
    /// rather than re-worded here: one wording, one place to correct it, and no chance of the screen saying
    /// something the rule does not.</para>
    /// </summary>
    private bool GuardsAllowDeletion(Action guard, string? routing = null)
    {
        try
        {
            guard();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            RaiseLifecycleNotice(routing is null ? ex.Message : $"{ex.Message} {routing}");
            return false;
        }
    }

    /// <summary>
    /// 🔴 <b>THE HALF OF THE REFUSAL ONLY THE VIEW MODEL CAN KNOW: whether the remedy it names is reachable from
    /// where the operator is standing.</b> Returns the extra sentence to append, or <c>null</c> when the guard's
    /// own wording is already actionable here.
    ///
    /// <para>Both voucher refusals end in <i>"Cancel it instead (Alt+X)"</i>, but the Alt+X arm is gated on
    /// <see cref="IsLiveReportPage"/> while Alt+D is offered on <see cref="Screen.LedgerVouchers"/> and
    /// <see cref="Screen.VoucherDetail"/> as well. Measured on both drill columns: the refusal appears, a real Alt+X
    /// on the same surface does nothing at all, and the destructive verb is then the ONLY lifecycle verb available
    /// there — the exact inverse of the design's argument that Cancel is the default gesture and Delete the
    /// exception. And on the surface where the key DOES work, the commonest filed case is refused by Cancel too
    /// (<see cref="LiveStatutoryDocumentBlocker"/>), so the one-line remedy was unreachable on two of five surfaces
    /// and refused on the third.</para>
    ///
    /// <para><b>Why the routing is added HERE rather than folded into the guard's message.</b> The message is the
    /// rule's, and it stays in one place — that is the whole reason <see cref="GuardsAllowDeletion"/> surfaces it
    /// verbatim. WHERE the operator is standing, and whether a portal document blocks Cancel, are facts the pure
    /// engine rule cannot see. <b>Why not simply extend the Alt+X arm to the two drill columns instead:</b> that
    /// arm belongs to S3 and <c>AltX_in_a_drill_column_beneath_a_live_report_raises_nothing</c> pins its current
    /// scope deliberately. Widening a shipped verb's surface is a scope decision for its own slice, not a
    /// side-effect of a delete fix.</para>
    /// </summary>
    private string? CancelRoutingFor(Voucher voucher)
    {
        if (LiveStatutoryDocumentBlocker(voucher) is { } blocker)
            return $"Alt+X is refused too while it carries {blocker} — cancel that at the portal first.";

        return IsLiveReportPage
            ? null
            : "Alt+X works on the Day Book — open it there to cancel this voucher.";
    }

    /// <summary>Arms the ONE confirmation channel for a deletion and puts the question up.</summary>
    private bool Arm(DeletionTarget kind, Guid id, string prompt)
    {
        // A previous outcome's notice goes before a new question is asked — the two share the status-bar row, and
        // this is the one moment at which a stale statement and a live question could appear side by side.
        Notice = string.Empty;
        _pendingDeleteKind = kind;
        _pendingDeleteId = id;
        AcceptPromptText = prompt;
        IsAcceptPromptOpen = true;
        return true;
    }

    /// <summary>
    /// "Y" on a deletion confirmation: performs the armed deletion, persists, and refreshes the surface it was
    /// raised on.
    ///
    /// <para>🔴 <b>WHY THE SAVE IS PRE-FLIGHTED INSTEAD OF ROLLED BACK, and this differs from S3 deliberately.</b>
    /// S3's cancel mutates a flag, so a failed save can be undone by restoring the flag. A DELETE cannot be undone
    /// that way: the voucher is out of <c>Company.Vouchers</c>, and nothing outside the engine assembly can put it
    /// back at its ORIGINAL LIST INDEX — and that index is persisted (<c>ORDER BY rowid</c>) and therefore
    /// user-visible in the Day Book's ordering of same-dated entries. Re-posting it would append it at the end and
    /// silently re-order the operator's book. So the one genuinely reachable save failure is checked BEFORE
    /// anything is removed: <c>CompanyStorage.Save</c> opens with <c>Company.EnsureValid()</c>, which throws
    /// <b>ArgumentException</b> on a bad PIN or a books-begin earlier than the year start — a state a book can be
    /// loaded in and only discovers on its next save. Calling it here means such a company reports a named refusal
    /// with the voucher still on the books, instead of losing the voucher to a save that was never going to
    /// commit.</para>
    ///
    /// <para><b>The residue, stated rather than implied.</b> If the write fails AFTER that check — a locked or
    /// unwritable <c>.db</c> — the store's transaction rolls back but the in-memory aggregate has already lost the
    /// row, so the book on screen is AHEAD of the file. That is reported as a named failure telling the operator to
    /// re-open the company; it is not silently swallowed, and it is not claimed to be handled.</para>
    /// </summary>
    private void PerformPendingDeletion(DeletionTarget kind, Guid id)
    {
        if (Company is null) return;

        // 🔴 THE COMPANY ARM LEAVES BEFORE ANY OF THE SHARED MACHINERY, and every line it skips is a line that
        // would be wrong for it. The `EnsureValid` pre-flight below exists because the other arms finish with
        // `_storage.Save(Company)`; this one must never save — writing the aggregate back after removing its file
        // would re-create the book the operator just deleted — so a company whose stored PIN is invalid must still
        // be deletable, and refusing it here would strand exactly the broken book most in need of removal.
        if (kind == DeletionTarget.Company)
        {
            PerformOpenCompanyDeletion();
            return;
        }

        // Pre-flight the ONE reachable save failure while nothing has been removed yet (see the summary).
        try
        {
            Company.EnsureValid();
        }
        catch (ArgumentException ex)
        {
            RaiseLifecycleNotice($"Cannot delete: {ex.Message}");
            return;
        }

        string what;
        try
        {
            switch (kind)
            {
                case DeletionTarget.Voucher:
                    if (Company.FindVoucher(id) is not { } voucher) return;
                    what = VoucherLabel(voucher);
                    // Re-ask the guards immediately before the irreversible act: the confirmation has been on
                    // screen for an unbounded time and nothing stops another surface changing the book meanwhile.
                    MasterDeletionRules.EnsureVoucherDeletable(Company, voucher);
                    new Apex.Ledger.Services.LedgerService(Company).Delete(id);
                    break;

                case DeletionTarget.Ledger:
                    if (Company.FindLedger(id) is not { } ledger) return;
                    what = $"Ledger '{ledger.Name}'";
                    MasterDeletionRules.EnsureLedgerDeletable(Company, ledger);
                    Company.RemoveLedger(ledger);
                    break;

                case DeletionTarget.Group:
                    if (Company.FindGroup(id) is not { } group) return;
                    what = $"Group '{group.Name}'";
                    MasterDeletionRules.EnsureGroupDeletable(Company, group);
                    Company.RemoveGroup(group);
                    break;

                case DeletionTarget.StockItem:
                    if (Company.FindStockItem(id) is not { } item) return;
                    what = $"Stock item '{item.Name}'";
                    MasterDeletionRules.EnsureStockItemDeletable(Company, item);
                    Company.RemoveStockItem(item);
                    break;

                // 7.16 — the payroll masters. Their referential guards live inside the payroll/pay-head services
                // (predefined categories, groups with children or employees under them, units that are a
                // component of a compound unit or an attendance type's unit, …) rather than in
                // MasterDeletionRules, and they throw with their own message. The screen re-resolves the id
                // rather than caching an entity, so the guard is re-asked immediately before the irreversible
                // act, exactly as the four cases above do.
                case DeletionTarget.PayrollMaster:
                {
                    if (PayrollMasterScreen is not { } list) return;
                    if (list.HighlightedMasterRow is not { } row || row.MasterId != id) return;
                    what = $"{Capitalise(list.MasterKindLabel)} '{row.MasterName}'";
                    list.DeleteMaster(id);
                    break;
                }

                default:
                    return;
            }

            _storage.Save(Company);
        }
        // 🔴 W0-13's shared predicate. This line used to be the narrow
        // `when (ex is InvalidOperationException or ArgumentException)` filter — the exact shape
        // `SaveFailure.IsReportable` was created to replace — on the ONE destructive verb in the application. The
        // summary above promised the residue "is reported as a named failure telling the operator to re-open the
        // company"; measured with the `.db` set read-only, it was neither reported NOR swallowed: SQLite Error 8
        // escaped the window key handler with the row already gone from memory and the notice bar EMPTY. Every
        // foreign-key failure this slice's guards now prevent escaped the same way. The filter admits DbException,
        // IOException, UnauthorizedAccessException and OverflowException as well, so the promise the doc comment
        // makes is now true. (The `ArgumentException` half of the old filter was additionally UNREACHABLE:
        // `Company.EnsureValid` is the only source, `CompanyStorage.Save` opens with it, and the pre-flight above
        // already calls and catches it — nothing between the two can change the PIN or the books-begin date.)
        catch (Exception ex) when (SaveFailure.IsReportable(ex))
        {
            // A guard that has become true since the question was asked, or a write that failed after the
            // pre-flight. The aggregate may now be AHEAD of the store — say so rather than imply a clean undo.
            RaiseLifecycleNotice($"Cannot delete: {ex.Message} Re-open the company before continuing.");
            RefreshDeletionSurface(kind);
            return;
        }

        RaiseLifecycleNotice($"{what} deleted.");
        RefreshDeletionSurface(kind);
    }

    /// <summary>
    /// "Y" on a COMPANY deletion (census row 1.4): removes the book's <c>.db</c>, releases the open aggregate and
    /// returns to Company Select.
    ///
    /// <para>🔴 <b>THE OUTCOME IS VERIFIED RATHER THAN ASSUMED, because <see cref="CompanyStorage.Delete"/> IS
    /// BEST-EFFORT AND SWALLOWS ITS REFUSALS.</b> Its own summary says so: a file held by a second instance
    /// (<c>IOException</c>) or sitting under an unwritable parent directory on POSIX
    /// (<c>UnauthorizedAccessException</c>) is caught and LEFT IN PLACE, and the method returns exactly as it does
    /// on success. Announcing "deleted" off that return, and releasing the company, would tell the operator their
    /// book was gone while it sat on disk and then show them a Company Select that still lists it — the app
    /// contradicting itself about a destructive act. So the file is re-tested and a survivor is reported as the
    /// failure it is, with the book still open and nothing else touched.</para>
    ///
    /// <para><b>The aggregate is released BEFORE the notice, not after.</b> <see cref="ReleaseOpenCompany"/>
    /// navigates to Company Select, and <see cref="Notice"/> is cleared on every change of screen — a notice
    /// raised first would be wiped by its own navigation and the operator would be returned to the picker with no
    /// statement of what just happened.</para>
    ///
    /// <para><b>Why the entry is rebuilt from the name rather than remembered.</b> It is the same derivation
    /// <see cref="CompanyStorage.ListCompanies"/> uses, so the path deleted is by construction the path the picker
    /// would have offered — including after a rename earlier in the same visit to this screen, which moved the
    /// file and would have staled anything captured when the screen opened.</para>
    /// </summary>
    private void PerformOpenCompanyDeletion()
    {
        var name = Company!.Name;
        var entry = new CompanyEntry(name, _storage.PathForName(name));

        _storage.Delete(entry);

        if (System.IO.File.Exists(entry.DatabasePath))
        {
            RaiseLifecycleNotice(
                $"Company '{name}' could NOT be deleted — its data file is still on disk. It is most likely open "
                + "in another window. Close it and try again; the company is unchanged.");
            return;
        }

        // Release first (this navigates), then say what happened — see the summary.
        ReleaseOpenCompany();
        RaiseLifecycleNotice($"Company '{name}' deleted.");
    }

    /// <summary>
    /// Re-renders the surface the deletion was raised on so the removed row leaves the screen immediately, the
    /// way S3's cancel re-runs the live report. A voucher deletion re-runs the report; a master deletion rebuilds
    /// the Chart of Accounts tree or the Stock Item master's existing-items list.
    ///
    /// <para>A voucher-detail column is deliberately NOT popped: the deletion leaves the operator looking at a
    /// detail pane for a voucher that is gone, and Esc/Left already returns to the register beneath it. Popping a
    /// column from inside a confirmation handler would move the cascade underneath the operator, which is the
    /// work-loss class the Alt+Y hole was closed for.</para>
    ///
    /// <para>🔴 <b>THAT EXCEPTION IS ABOUT DELETION AND DOES NOT CARRY TO ALTERATION — it was read across, and a
    /// document went out wrong.</b> The paragraph above rests on "the voucher is gone": there is nothing left to
    /// re-project, so leaving the pane standing is the least-surprising outcome and the pane cannot mislead
    /// anyone about a live document. After an ALTERATION the voucher still exists, still carries the same number,
    /// and is still PRINTABLE and E-MAILABLE from that very pane — so a pane left alone there is not inert, it
    /// re-issues the superseded document. The alteration doors therefore call
    /// <see cref="VoucherDetailViewModel.Refresh"/> (S5d/S5e review, C2), which is a re-projection and never a
    /// pop, so the cascade still does not move underneath the operator. Deletion and alteration disagree here on
    /// purpose; do not unify them.</para>
    ///
    /// <para>🔴 <b>THE REGISTER DRILL WAS MISSING AND THE COMMENT ABOVE CLAIMED IT WAS NOT.</b> A voucher deleted
    /// from <see cref="Screen.LedgerVouchers"/> re-ran the REPORT (which is not the active column) and left the
    /// deleted row on the drill with its amount still in the running balance, <c>SelectedRow</c> still pointing at
    /// it, and a second Alt+D on that stale row a silent dead key. The shipped register-drill test asserted only
    /// that the voucher had left the books, which is why it was green with the stale screen behind it. Unlike the
    /// voucher-detail exception this was an omission, not a decision — so the drill is rebuilt here, in place, the
    /// same way the report is.</para>
    /// </summary>
    private void RefreshDeletionSurface(DeletionTarget kind)
    {
        switch (kind)
        {
            case DeletionTarget.Voucher:
                Reports?.Show(Reports.Kind);
                LedgerVouchers?.Refresh();
                break;
            case DeletionTarget.Ledger:
            case DeletionTarget.Group:
                ChartOfAccounts?.Refresh();
                break;
            case DeletionTarget.StockItem:
                StockItemMaster?.ReloadExistingItems();
                break;
            case DeletionTarget.PayrollMaster:
                PayrollMasterScreen?.ReloadExisting();
                break;
        }
    }

    /// <summary>"employee category" → "Employee category", for the deleted-notice sentence. The kind labels are
    /// written lower case because the CONFIRMATION reads them mid-sentence; the notice starts with them.</summary>
    private static string Capitalise(string label) =>
        string.IsNullOrEmpty(label) ? label : char.ToUpperInvariant(label[0]) + label[1..];

    // ============================================ Phase 10.11 S5d: Ctrl+Enter — ALTER the highlighted voucher

    /// <summary>
    /// The three surfaces Ctrl+Enter opens a posted voucher for ALTERATION from — the live report page (the Day
    /// Book carries the voucher rows), the register drill, and the read-only voucher-detail column beneath it.
    ///
    /// <para><b>They are EXACTLY the three voucher arms of <see cref="IsDeleteTargetPage"/>, deliberately.</b>
    /// Alt+D and Ctrl+Enter resolve the highlighted voucher through the same three expressions, so the two verbs
    /// can never disagree about which document the highlight means — the same reason
    /// <see cref="RequestDeleteChartRow"/> resolves its row the way <see cref="AlterHighlightedChartRow"/> does.
    /// <see cref="IsDeleteTargetPage"/>'s two MASTER arms (Chart of Accounts, Stock Item master) are absent
    /// because master alteration already has its own route: Enter on the chart, and Ctrl+Enter on the Stock Item
    /// list via <see cref="AlterHighlightedStockItemRow"/>, which the window's arm consults FIRST.</para>
    ///
    /// <para>🔴 <b>Why the report clause is <see cref="IsLiveReportPage"/> and not <see cref="IsReportContext"/>.</b>
    /// Inherited from the S3 review finding and NOT re-derived: <c>IsReportContext</c> is deliberately TRUE while
    /// an F12 config, an Alt+F12 sort/filter, an Alt+A add-voucher picker, an Alt+K saved-views panel or a Print
    /// Preview column is stacked over the report, with the report's row still highlighted behind it. Opening an
    /// alteration from inside one of those columns would push an entry screen over the operator's open panel for
    /// the voucher BEHIND it. Alteration is not destructive the way Alt+X and Alt+D are — but it is the verb that
    /// puts a posted voucher into an editable form, and the scope hole is the same hole.</para>
    ///
    /// <para>🔴 <b>MEASURED, not assumed, and the measurement corrected a claim.</b> Swapping the window arm's
    /// clause for <see cref="IsReportContext"/> does NOT open the stacked-column hole, because
    /// <see cref="RequestAlterHighlightedVoucher"/>'s <c>CurrentScreen</c> switch has no <c>ReportConfig</c> /
    /// <c>ReportSortFilter</c> / <c>AddVoucherPicker</c> / <c>SavedViews</c> / <c>PrintPreview</c> arm and refuses
    /// those a second time. The two are redundant for that case and the switch is what decides it. What this
    /// property is measurably load-bearing for is the OTHER two surfaces: <see cref="IsReportContext"/> excludes
    /// <see cref="Screen.LedgerVouchers"/> and <see cref="Screen.VoucherDetail"/> by construction, so an arm
    /// written on it loses the register drill and the voucher-detail column entirely — which is exactly what the
    /// mutation reddened.</para>
    ///
    /// <para>The <c>Company is not null</c> clause is a precondition, honestly labelled: it is not independently
    /// falsifiable (every screen below needs an open company to reach, and nothing sets <c>Company</c> back to
    /// null) and <see cref="RequestAlterHighlightedVoucher"/> re-tests it anyway. Kept for the same reason
    /// <see cref="IsDeleteTargetPage"/> keeps its own.</para>
    /// </summary>
    public bool IsVoucherAlterTargetPage =>
        Company is not null
        && (IsLiveReportPage
            || (CurrentScreen == Screen.LedgerVouchers && LedgerVouchers is not null)
            || (CurrentScreen == Screen.VoucherDetail && VoucherDetail is not null));

    /// <summary>
    /// <b>Ctrl+Enter — open the highlighted posted voucher for ALTERATION.</b> Returns the THREE-VALUED
    /// <see cref="VoucherAlterationRequest"/>: <c>Opened</c>, <c>NoVoucherHere</c> (a quiet no-op — the caller
    /// MUST fall through so the row still drills), or <c>Refused</c> (terminal, with a NAMED refusal already on
    /// the notice bar — the caller MUST consume the key, because falling through would drill and
    /// <c>OnCurrentScreenChanged</c> wipes the notice on the way past).
    ///
    /// <para>🔴 It is deliberately NOT a <c>bool</c>. A bool conflates <c>NoVoucherHere</c> with <c>Refused</c>,
    /// and those two demand OPPOSITE caller behaviour — fall through versus consume. That conflation is the
    /// entire reason <see cref="VoucherAlterationRequest"/> exists, so do not "simplify" this signature.</para>
    ///
    /// <para><b>🔴 FIDELITY (R7) — TWO RECORDS, AND THEY MUST NOT BE MERGED.</b>
    /// <list type="bullet">
    ///   <item><b>(A) A DELIBERATE WIDENING OF AN ATTESTED BEHAVIOUR — the gesture.</b> The corpus attests
    ///     <c>Ctrl+Enter</c> as an ALTERATION key reached from a report drill-down, verbatim
    ///     <i>"To alter a master during voucher entry or from drilldown of a report"</i> (Book PDF p.436
    ///     [printed p.432], re-extracted with <c>pdftotext -raw</c> — <c>-layout</c> scrambles that three-column
    ///     table). What it attests is a <b>master</b>. Binding the same chord to a <b>voucher</b> from the same
    ///     place widens an attested behaviour to a second object. It is <b>not</b> corpus silence, and it is not
    ///     a narrowing.</item>
    ///   <item><b>(B) A DELIBERATE DIVERGENCE FROM AN ATTESTED BEHAVIOUR — the chord we did NOT use.</b> The
    ///     corpus's own route to voucher alteration is <b>plain Enter</b> on a register row:
    ///     <i>"… &gt; \&lt;X&gt; Register &gt; Select Month &amp; Show/Edit Entry"</i>, repeated verbatim for every
    ///     voucher family (Book PDF pp.32, 34, 37, 42, 47, 49, 64, 71 and the inventory families), and TallyPrime
    ///     has no separate read-only voucher screen — one action is named, not two. We keep plain Enter for the
    ///     read-only <see cref="Screen.VoucherDetail"/> column to preserve the Miller-column cascade. That is
    ///     USER DECISION 1 / VL-1, settled, with a follow-up to reconsider — recorded here as a divergence from
    ///     an ATTESTED behaviour, never as fidelity.</item>
    /// </list>
    /// <b>Attested and FOLLOWED, so it is neither of the above:</b> <c>Ctrl+A</c> saves the altered voucher —
    /// <i>"… &amp; Show/Edit Entry &gt; Press \"Ctrl+A\" for Save"</i> (Book PDF pp.51, 53, 56, 58). That is why
    /// <see cref="ActivateSelected"/> routes an altering entry screen to <c>AcceptAlteration</c> rather than
    /// inventing a second accept key.
    /// <br/><b>OURS — corpus silent:</b> the three surfaces above, the refusal sentences (they come from
    /// <see cref="VoucherAlterationEligibility"/>), and the notice bar they are shown on.</para>
    ///
    /// <para><b>The gates, and why they live here rather than in the key handler.</b> The window's Ctrl+Enter arm
    /// decides only that the keystroke is ours (a voucher-alteration surface, not typing, no open picker, exactly
    /// Ctrl). Everything that depends on DATA is decided here so a future button route cannot diverge from the
    /// accelerator:
    /// <list type="bullet">
    ///   <item>no company open — inert;</item>
    ///   <item>🔴 a confirmation is already up — inert. An armed Alt+X or Alt+D question names a voucher and is
    ///         answered by a bare Y; opening an entry screen over it would carry the arming into a screen that
    ///         cannot show the question, exactly the class <see cref="ActivateSelected"/>'s own lifecycle gate
    ///         was added for. Answer it first;</item>
    ///   <item>nothing highlighted, or a row that resolves to no voucher (a header, a total, an empty-state note,
    ///         or the <see cref="Guid.Empty"/> a non-drillable row carries) — a quiet no-op;</item>
    ///   <item>🔴 <see cref="VoucherEntryViewModel.ForAlter"/> REFUSED — the family-specific sentence is put on
    ///         the notice bar by name. 13 of the 33 enumerated shapes refuse and 8 more defer, so the refusal is
    ///         the COMMON outcome on a real book, not the edge case. It reaches the operator through
    ///         <see cref="RaiseLifecycleNotice"/> and not through <c>Message</c>, because the report page's
    ///         <c>DataTemplate</c> is typed <c>x:DataType="vm:ReportsViewModel"</c> and has no <c>Message</c>
    ///         property at all — the S3 review's finding, inherited rather than rediscovered.</item>
    /// </list></para>
    ///
    /// <para><b>Why the alteration opens as a DRILL column and not a page column.</b>
    /// <see cref="OpenPageColumn"/> trims every column after the last MENU column, which would delete the report
    /// or register the operator drilled from; the cascade would come back to the Gateway on Esc instead of to the
    /// row they were standing on. <see cref="OpenDrillColumn"/> appends to the right and leaves the pane beneath
    /// intact, and <see cref="BindPageColumn"/> already re-binds a surviving
    /// <see cref="VoucherEntryViewModel"/> when the column is popped.</para>
    /// </summary>
    public VoucherAlterationRequest RequestAlterHighlightedVoucher()
    {
        if (Company is null) return VoucherAlterationRequest.NoVoucherHere;

        // 🔴 An armed Alt+X / Alt+D question is answered by a BARE Y, and the entry screen this would open cannot
        // show it. Reported as Refused, not as NoVoucherHere: the operator is told what to do and the keystroke is
        // consumed, so it cannot fall through and drill out from under the question instead.
        if (IsAcceptPromptOpen)
        {
            RaiseLifecycleNotice(
                "Answer the question on screen first (Y or N) — Ctrl+Enter does nothing while it is up.");
            return VoucherAlterationRequest.Refused;
        }

        var voucherId = CurrentScreen switch
        {
            // `Reports?` rather than `Reports!` + an `IsLiveReportPage` clause, for the reason
            // RequestDeleteHighlighted records: on Screen.Report that clause reduces to a null test, so it would
            // be an unfalsifiable guard spelled as a screen predicate.
            Screen.Report => Reports?.SelectedRow?.DrillVoucherId,
            Screen.LedgerVouchers => LedgerVouchers?.SelectedRow?.DrillVoucherId,
            Screen.VoucherDetail => VoucherDetail?.VoucherId,
            _ => null,
        };

        if (voucherId is not { } id) return VoucherAlterationRequest.NoVoucherHere;
        if (Company.FindVoucher(id) is not { } voucher) return VoucherAlterationRequest.NoVoucherHere;

        return ShowVoucherAlteration(voucher);
    }

    /// <summary>
    /// Opens <paramref name="voucher"/>'s alteration screen, or puts its named refusal on the notice bar. Split
    /// out from <see cref="RequestAlterHighlightedVoucher"/> so the surface resolution and the open are separately
    /// legible — and so the refusal has exactly ONE exit.
    /// </summary>
    private VoucherAlterationRequest ShowVoucherAlteration(Voucher voucher)
    {
        // The surface the operator drilled from, captured as an INSTANCE: OpenDrillColumn does not clear the sub
        // screens, but a later pop rebinds them, and a `() => Reports?.Show(...)` closure read at save time could
        // see a different report. Same trap ShowLedgerAlter records for the Chart of Accounts tree.
        var report = Reports;
        var register = LedgerVouchers;
        // 🔴 THE THIRD SURFACE, AND THE ONE THE REVIEW CAUGHT MISSING. Ctrl+Enter is admitted FROM
        // Screen.VoucherDetail (IsVoucherAlterTargetPage's third arm), so the read-only column can be the pane
        // the alteration was raised from — and it is the pane that ISSUES DOCUMENTS: OpenPrintPreview's
        // Screen.VoucherDetail branch prints it, and EmailComposeViewModel attaches the same bytes. Left
        // unrefreshed it re-issued the SUPERSEDED document under the live voucher number. See
        // VoucherDetailViewModel.Refresh.
        var detail = VoucherDetail;

        // 🔴 S5e — A POS BILL GOES TO THE POS SCREEN, and the branch is here rather than inside ForAlter because
        // WHICH SCREEN opens is a shell decision. Every field of a POS bill's tender split is persisted, so it is
        // fully recoverable — but only on the screen that keys a tender split. VoucherAlterationEligibility still
        // refuses it for the accounting entry screen, correctly and for the unchanged reason; this route means an
        // operator never has to read that refusal.
        if (Company!.FindVoucherType(voucher.TypeId) is { IsPosSales: true })
            return ShowPosBillAlteration(voucher, report, register);

        var open = VoucherEntryViewModel.ForAlter(
            Company!, voucher.Id, _storage,
            onSaved: () =>
            {
                // Pop the alteration column, then re-render whatever survived beneath it so the amended figures
                // are on screen without the operator re-opening the report — the same courtesy S3's cancel and
                // S4's delete already pay through RefreshDeletionSurface.
                BackFromPage();
                report?.Show(report.Kind);
                register?.Refresh();
                detail?.Refresh();
            },
            onCancelled: BackFromPage);

        if (open.Refusal is { } refusal)
        {
            // 🔴 The refusal is SHOWN, never swallowed. `ForAlter` returns a refusal for most shapes on a real
            // book, and a caller that dropped it would make Ctrl+Enter indistinguishable from a dead key — the
            // precise failure VoucherAlterationOpen was made a two-sided type to prevent.
            RaiseLifecycleNotice(refusal);
            return VoucherAlterationRequest.Refused;
        }

        var entry = open.Entry!;
        // The same batch-allocation cascade wiring OpenVoucher does. Without it a batch-tracked line on an
        // altering screen would raise an event nobody handles — the shell owns the cascade, not the entry VM.
        entry.BatchAllocationRequested += (item, godown, qty, isOutward, onCommitted) =>
            ShowBatchAllocation(item, godown, qty, isOutward, onCommitted);

        var title = $"Accounting Voucher Alteration — {entry.Type.Name}";
        // No `Notice = string.Empty` here: OpenDrillColumn moves CurrentScreen to VoucherEntry from one of three
        // other screens, and OnCurrentScreenChanged clears the bar on every change. Writing it again would be a
        // guard no test could fail.
        // The CASCADE COLUMN label says Alteration too, not just the screen title above it. OpenVoucher labels
        // its column `type.Name + " Voucher"`, and reusing that here left an operator with the Day Book on
        // the left and a column on the right that read identically for a new entry and for an amendment of a
        // posted one. The master screens already distinguish the two ("Stock Item Alteration").
        OpenDrillColumn(new GatewayColumn(entry.Type.Name + " Voucher — Alteration", entry),
            Screen.VoucherEntry, title, () => VoucherEntry = entry);
        return VoucherAlterationRequest.Opened;
    }

    // ============================================ W2-15 (row 5.4): Alt+2 — DUPLICATE the highlighted voucher

    /// <summary>
    /// <b>Alt+2 — open a COPY of the highlighted posted voucher as a fresh entry</b> (census row 5.4). Returns
    /// the same three-valued <see cref="VoucherAlterationRequest"/> the alteration door returns, and for the same
    /// reason: <c>NoVoucherHere</c> must fall through (nothing was chosen — there is nothing to say), while
    /// <c>Refused</c> must be consumed, because a named sentence is already on the notice bar and
    /// <c>OnCurrentScreenChanged</c> wipes it on the way past.
    ///
    /// <para><b>Fidelity (R7; RULING 14).</b> <i>help.tallysolutions.com/day-book-tally/</i> gives the verb and
    /// the chord verbatim — <i>"Press <b>Alt</b>+<b>2</b> (Duplicate Vch)"</i> — on the Day Book. <c>Key.D2</c>
    /// had ZERO hits anywhere in <c>src/</c>, so this is a free addition of an attested chord, not a
    /// re-assignment of an occupied one; nothing in the open U-6 chord ruling is touched.</para>
    ///
    /// <para><b>The three surfaces are EXACTLY <see cref="IsVoucherAlterTargetPage"/>'s</b>, resolved by the very
    /// same <c>CurrentScreen</c> switch Ctrl+Enter uses. Duplicate and Alter must never disagree about which
    /// document the highlight means — the same rule that already binds Alt+D and Ctrl+Enter together.</para>
    ///
    /// <para><b>The armed-confirmation gate is the alteration door's, verbatim in effect.</b> An armed Alt+X /
    /// Alt+D question names a voucher and is answered by a bare Y; opening an entry screen over it would carry
    /// the arming into a screen that cannot show the question.</para>
    ///
    /// <para>🔴 <b>What this deliberately does NOT do — Insert Voucher (census row 5.5) is NOT built here.</b>
    /// The vendor's Insert (<i>"Select the entry above which you want to insert the transaction, press
    /// <b>Alt</b>+<b>I</b> (Insert Vch)"</i>) differs from the shipped Alt+A "Add voucher in a report" — which
    /// already seeds the new voucher with the highlighted row's date — in exactly one respect: inserting between
    /// two existing vouchers <i>"causes all subsequent vouchers of that type to be renumbered"</i>. That
    /// renumbering rewrites document numbers on vouchers that have already been issued, which collides head-on
    /// with the freezes <see cref="VoucherAlterationEligibility"/> already enforces (a live IRN, a challan
    /// record). It needs a user ruling, and Alt+I is in any case spent on the POS tender-mode toggle. Row 5.5
    /// therefore stays ABSENT rather than being closed by a second name for a verb that already exists.</para>
    /// </summary>
    public VoucherAlterationRequest RequestDuplicateHighlightedVoucher()
    {
        if (Company is null) return VoucherAlterationRequest.NoVoucherHere;

        if (IsAcceptPromptOpen)
        {
            RaiseLifecycleNotice(
                "Answer the question on screen first (Y or N) — Alt+2 does nothing while it is up.");
            return VoucherAlterationRequest.Refused;
        }

        var voucherId = CurrentScreen switch
        {
            Screen.Report => Reports?.SelectedRow?.DrillVoucherId,
            Screen.LedgerVouchers => LedgerVouchers?.SelectedRow?.DrillVoucherId,
            Screen.VoucherDetail => VoucherDetail?.VoucherId,
            _ => null,
        };

        if (voucherId is not { } id) return VoucherAlterationRequest.NoVoucherHere;
        if (Company.FindVoucher(id) is not { } voucher) return VoucherAlterationRequest.NoVoucherHere;

        return ShowVoucherDuplicate(voucher);
    }

    /// <summary>
    /// Opens a fresh entry screen pre-filled from <paramref name="voucher"/>, or puts its named refusal on the
    /// notice bar. The duplicate sibling of <see cref="ShowVoucherAlteration"/>.
    ///
    /// <para><b>It opens as a DRILL column for the identical reason the alteration does</b>:
    /// <see cref="OpenPageColumn"/> trims every column after the last MENU column, which would delete the report
    /// or register the operator duplicated FROM, and Esc would then return to the Gateway instead of to the row
    /// they were standing on.</para>
    ///
    /// <para>🔴 <b>There is no POS branch here, and its absence is deliberate.</b>
    /// <see cref="ShowVoucherAlteration"/> re-routes a POS bill to the POS screen because a posted bill's tender
    /// split is fully recoverable and must be amended somewhere. A POS bill's DUPLICATE is a different question —
    /// a till receipt is raised by taking money at a counter, not by copying yesterday's — and
    /// <see cref="VoucherAlterationEligibility"/> already refuses it by name before this method is reached. It is
    /// left refused rather than silently routed, and that is recorded as OURS.</para>
    /// </summary>
    private VoucherAlterationRequest ShowVoucherDuplicate(Voucher voucher)
    {
        // Captured as INSTANCES, exactly as the alteration door captures them: OpenDrillColumn does not clear the
        // sub screens, but a later pop rebinds them, so a closure reading `Reports` at save time could see a
        // different report.
        var report = Reports;
        var register = LedgerVouchers;

        var open = VoucherEntryViewModel.ForDuplicate(
            Company!, voucher.Id, _storage,
            onSaved: () =>
            {
                // Pop the duplicate column, then re-render whatever survived beneath it so the NEW voucher is on
                // screen without the operator re-opening the report. The voucher-detail pane is deliberately NOT
                // refreshed here (unlike the alteration door): it projects the SOURCE voucher, which a duplicate
                // does not touch, so there is nothing on it that could have gone stale.
                BackFromPage();
                report?.Show(report.Kind);
                register?.Refresh();
            },
            onCancelled: BackFromPage);

        if (open.Refusal is { } refusal)
        {
            RaiseLifecycleNotice(refusal);
            return VoucherAlterationRequest.Refused;
        }

        var entry = open.Entry!;
        entry.BatchAllocationRequested += (item, godown, qty, isOutward, onCommitted) =>
            ShowBatchAllocation(item, godown, qty, isOutward, onCommitted);

        var title = $"Accounting Voucher Creation — {entry.Type.Name} (Duplicate)";
        OpenDrillColumn(new GatewayColumn(entry.Type.Name + " Voucher — Duplicate", entry),
            Screen.VoucherEntry, title, () => VoucherEntry = entry);
        return VoucherAlterationRequest.Opened;
    }

    /// <summary>
    /// Opens <paramref name="voucher"/>'s alteration on the POS BILLING screen, or puts its named refusal on the
    /// notice bar. The POS sibling of <see cref="ShowVoucherAlteration"/>, and it opens a DRILL column for the
    /// identical reason: <see cref="OpenPageColumn"/> trims every column after the last MENU column, which would
    /// delete the report the operator drilled from and send Esc back to the Gateway instead of to the row they
    /// were standing on.
    /// </summary>
    private VoucherAlterationRequest ShowPosBillAlteration(
        Voucher voucher, ReportsViewModel? report, LedgerVouchersViewModel? register)
    {
        // The same third surface, for the same reason — this door is reached from Screen.VoucherDetail too.
        var detail = VoucherDetail;

        // 🔴 PRINT AFTER SAVE ON THE ALTERATION DOOR (review finding C8 — MAJOR / fidelity), and it is a
        // TWO-LAYERED fix of which this is the second layer. AcceptAlterationCore now RAISES
        // PrintReceiptRequested when the POS type's print-after-save is on; before this line nothing on this route
        // SUBSCRIBED — OpenPosBilling wires the event and its onSaved is the only caller of the receipt preview —
        // so raising it alone would still have produced no paper. The customer's only receipt kept understating
        // an amended bill.
        PosReceiptData? pendingReceipt = null;
        var open = PosBillingViewModel.ForAlter(
            Company!, voucher.Id, _storage,
            onSaved: () =>
            {
                BackFromPage();
                report?.Show(report.Kind);
                register?.Refresh();
                detail?.Refresh();
                // The receipt column is pushed AFTER the surfaces beneath are re-rendered, so Esc from the receipt
                // returns to a correct pane rather than to a stale one.
                if (pendingReceipt is { } r) { pendingReceipt = null; OpenPosReceiptDrill(r); }
            },
            onCancelled: BackFromPage);

        if (open.Refusal is { } refusal)
        {
            // 🔴 SHOWN, never swallowed — the same channel ShowVoucherAlteration uses, and for the same reason:
            // the report page's DataTemplate is typed to ReportsViewModel and has no Message property at all.
            RaiseLifecycleNotice(refusal);
            return VoucherAlterationRequest.Refused;
        }

        var entry = open.Entry!;
        entry.PrintReceiptRequested += r => pendingReceipt = r;
        var title = $"POS Bill Alteration — {entry.Type.Name}";
        OpenDrillColumn(new GatewayColumn(entry.Type.Name + " — POS Alteration", entry),
            Screen.PosBilling, title, () => PosBilling = entry);
        return VoucherAlterationRequest.Opened;
    }

    /// <summary>
    /// Shows an ALTERED POS bill's retail receipt as a Print-Preview <b>drill</b> column, appended over whatever
    /// the operator drilled from.
    ///
    /// <para><b>Why not <see cref="OpenPosReceiptPreview"/>, the fresh-entry sibling.</b> That one runs
    /// <c>ClearSubScreens</c> and REMOVES the trailing column, because a fresh bill's entry screen is a PAGE
    /// column that has served its purpose. An alteration is a drill column stacked over a live report, register or
    /// voucher-detail pane — the same reason <see cref="ShowPosBillAlteration"/> uses
    /// <see cref="OpenDrillColumn"/> rather than <see cref="OpenPageColumn"/> — so tearing the cascade down here
    /// would send Esc back to the Gateway instead of to the row the operator was standing on. The body is
    /// <see cref="OpenPrintPreview"/>'s tail verbatim, which is the shape that appends without trimming.</para>
    /// </summary>
    private void OpenPosReceiptDrill(PosReceiptData receipt)
    {
        var preview = new PrintPreviewViewModel(receipt);
        PrintPreview = preview;
        Columns.Add(new GatewayColumn(preview.Title, preview));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.PrintPreview;
        ScreenTitle = preview.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// 🔴 <b>The ONE place that decides which accept verb the POS billing screen runs</b> — <c>Accept</c> for a new
    /// bill, <see cref="PosBillingViewModel.AcceptAlteration"/> for a posted one being altered. The POS sibling of
    /// <see cref="AcceptVoucherEntryOrAlteration"/>, and it exists for the identical reason: the screen has TWO
    /// accept routes (Ctrl+A through <see cref="ActivateSelected"/>, and the on-screen Accept button through
    /// <c>MainWindow.OnAcceptPosClick</c>), and on an altering screen <c>Accept</c> would Post a SECOND bill
    /// beside the original. Both routes now come through here, so they cannot disagree.
    /// </summary>
    public bool AcceptPosBillingOrAlteration() =>
        PosBilling is { } pos && (pos.IsAltering ? pos.AcceptAlteration() : pos.Accept());

    /// <summary>
    /// 🔴 <b>The ONE place that decides which accept verb the voucher-entry screen runs</b> — <c>Accept</c> for a
    /// new voucher, <see cref="VoucherEntryViewModel.AcceptAlteration"/> for a posted one being altered. Returns
    /// what the verb returned; <c>false</c> when no entry screen is bound.
    ///
    /// <para><b>Why one method and not two call sites.</b> The screen has TWO accept routes — Ctrl+A through
    /// <see cref="ActivateSelected"/>, and the on-screen <i>Accept</i> button through
    /// <c>MainWindow.OnAcceptVoucherClick</c> — and until this slice the button called <c>VoucherEntry.Accept()</c>
    /// directly. On an altering screen that is a HARD REFUSAL ("use AcceptAlteration"), so the button and the key
    /// would have disagreed the moment alteration became reachable. Both now come through here, which is the same
    /// discipline <c>RequestDeleteChartRow</c> follows in resolving its row exactly as
    /// <see cref="AlterHighlightedChartRow"/> does.</para>
    ///
    /// <para><b>FIDELITY (R7) — ATTESTED AND FOLLOWED.</b> The corpus saves an altered voucher with the SAME key
    /// as creation: <i>"… &amp; Show/Edit Entry &gt; Press \"Ctrl+A\" for Save"</i>, Book PDF pp.51, 53, 56, 58.
    /// No second accept chord is invented. The branch on <c>IsAltering</c> (rather than on a separate screen id)
    /// is what lets the alteration form BE the entry form pre-filled, which is also how the reference product
    /// presents it — <i>"TallyPrime has no separate read-only voucher screen"</i>, design record §2.1.</para>
    ///
    /// <para><b>Why <c>Accept</c> could not simply be made to cope.</b> <c>Accept</c> is build + <c>Post</c> +
    /// REGISTRATION SIDE EFFECTS: it re-runs <c>DetectTdsContext</c>, <c>DetectRcmShape</c> and
    /// <c>BuildAdvanceLines</c> against TODAY's masters, mints a fresh <see cref="Guid"/> and posts a SECOND
    /// voucher beside the original (design §6.6a.6). Its refusal on an altering screen is a designed guard, not an
    /// oversight, and this branch is what stops an operator ever meeting it.</para>
    /// </summary>
    public bool AcceptVoucherEntryOrAlteration() =>
        VoucherEntry is { } entry && (entry.IsAltering ? entry.AcceptAlteration() : entry.Accept());

    // =============================================================== WI-11: the Accept? (Y/N) confirmation

    /// <summary>
    /// WI-11 — true while the terminal "Accept? (Y/N)" confirmation is up over a master screen. Every Y/N key
    /// arm in the window's tunnel handler is SCOPED to this flag, which is what stops the confirmation from
    /// hijacking Y (Gateway → Export Data) or Alt+N (Auto Columns) anywhere else in the app.
    /// <para>
    /// <b>Modifier scoping (Phase 10.11 S1).</b> While the flag is true the confirmation arm owns bare and
    /// Shift-held <c>Y</c>/<c>N</c> (answer it), <c>Escape</c> and <c>Alt+Escape</c> (dismiss it, master
    /// intact), and it CONSUMES <c>Alt+Y</c>/<c>Alt+N</c> as inert — those two neither answer the question nor
    /// reach their own accelerators, because the Alt+Y owner (Data → Backup / Restore) tears the open master's
    /// column down and would discard everything the operator keyed. <c>Ctrl</c>-held keys are excluded
    /// entirely, so Ctrl+A still saves outright. Answer the prompt first, then press the accelerator.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isAcceptPromptOpen;

    /// <summary>The prompt text shown while <see cref="IsAcceptPromptOpen"/> — e.g. "Accept Ledger? (Y/N)".</summary>
    [ObservableProperty] private string _acceptPromptText = string.Empty;

    /// <summary>
    /// True on the master screens that carry an Accept confirmation (the WI-11 scope).
    ///
    /// <para><b>The two company screens joined this list when the company profile form shipped</b>, so the
    /// company is accepted exactly the way every other master is: Ctrl+A saves outright, Enter asks first.
    /// This is a real behaviour change on creation — Enter used to create immediately — and it is made
    /// deliberately rather than left as an inconsistency, because the confirmation and the shortcut route
    /// through the same <see cref="ActivateSelected"/> and so cannot drift apart. Creation had NO test
    /// coverage of its navigation or keyboard behaviour at all before this (only its side effect, a company
    /// object, was exercised), which is why the change ships with that coverage rather than unobserved.</para>
    /// </summary>
    public bool IsMasterAcceptScreen =>
        CurrentScreen is Screen.CreateCompany or Screen.AlterCompany
            or Screen.LedgerMaster or Screen.AccountGroupMaster or Screen.CostCategoryMaster
            or Screen.CostCentreMaster or Screen.BudgetMaster or Screen.ScenarioMaster
            or Screen.CurrencyMaster or Screen.StockGroupMaster or Screen.StockCategoryMaster
            or Screen.UnitMaster or Screen.GodownMaster or Screen.StockItemMaster
            or Screen.BatchMaster or Screen.BomMaster or Screen.ReorderLevelsMaster
            or Screen.NatureOfPaymentMaster or Screen.NatureOfGoodsMaster
            or Screen.EmployeeCategoryMaster or Screen.EmployeeGroupMaster or Screen.EmployeeMaster
            or Screen.PayrollUnitMaster or Screen.AttendanceTypeMaster
            or Screen.PayHeadMaster or Screen.SalaryStructureMaster;

    /// <summary>
    /// WI-11 — raises the "Accept? (Y/N)" confirmation over the open master screen. This is the route the
    /// on-screen Accept affordance and the Enter key take.
    /// <para>
    /// <b>Ctrl+A does NOT come through here.</b> The accept-as-is shortcut keeps its existing direct path
    /// (<see cref="ActivateSelected"/>), so it saves without ever raising the prompt — matching the reference
    /// product, where the operator may answer Yes under Accept OR press Ctrl+A, and keeping the ~40 screens
    /// already regression-locked on Ctrl+A untouched.
    /// </para>
    /// <returns><c>true</c> when the prompt was raised; <c>false</c> off a master screen (a safe no-op).</returns>
    /// </summary>
    public bool RequestMasterAccept()
    {
        if (!IsMasterAcceptScreen || IsAcceptPromptOpen) return false;

        AcceptPromptText = $"Accept {MasterAcceptNoun()}? (Y/N)";
        IsAcceptPromptOpen = true;
        return true;
    }

    /// <summary>
    /// WI-11 — "Y": dismiss the prompt and perform the SAME save Ctrl+A performs.
    /// <para>Phase 10.11 S3: when <see cref="_pendingCancelVoucherId"/> is armed the prompt is a voucher
    /// CANCELLATION, not a master accept, and Y routes there instead. The id is read and disarmed BEFORE the
    /// action runs, so a cancellation can never leave the channel armed for the next unrelated prompt.</para>
    /// <para>Phase 10.11 S4: likewise for <see cref="_pendingDeleteKind"/> — a DELETION. Both armed slots are read
    /// and torn down together before either action runs, which is what makes "the channel is disarmed no matter
    /// what the action does" true of the destructive verb as well as the reversible one.</para>
    /// </summary>
    public bool ConfirmMasterAccept()
    {
        if (!IsAcceptPromptOpen) return false;

        // Read the armed action, then tear the prompt down through the ONE teardown before running it, so the
        // channel is disarmed no matter what the action does.
        var pendingCancel = _pendingCancelVoucherId;
        var pendingDeleteKind = _pendingDeleteKind;
        var pendingDeleteId = _pendingDeleteId;
        ResetMasterAcceptPrompt();
        if (pendingCancel != Guid.Empty)
        {
            CancelPendingVoucher(pendingCancel);
            return true;
        }

        if (pendingDeleteKind != DeletionTarget.None)
        {
            PerformPendingDeletion(pendingDeleteKind, pendingDeleteId);
            return true;
        }

        // Deliberately the identical code path as Ctrl+A, so the confirmation can never drift from the
        // accept-as-is shortcut into saving something different.
        ActivateSelected();
        return true;
    }

    /// <summary>
    /// WI-11 — "N" / Esc: dismiss the prompt and return to editing WITHOUT saving.
    /// <para>Phase 10.11 S3: this is also the "No" of a voucher cancellation, so it must disarm the pending
    /// cancellation — otherwise the next Accept prompt raised anywhere in the app inherits the armed id and a
    /// plain "Y" on a ledger master cancels a voucher. It clears the prompt through
    /// <see cref="ResetMasterAcceptPrompt"/> rather than by hand: an inline copy of the three assignments was
    /// written first, and a mutation run proved NOTHING could distinguish deleting the disarm from keeping it,
    /// because the teardown already covered every reachable path. Routing here leaves ONE place that clears this
    /// state, which is one place to get right and one place a test can pin.</para>
    /// </summary>
    public bool DismissMasterAccept()
    {
        if (!IsAcceptPromptOpen) return false;

        ResetMasterAcceptPrompt();
        return true;
    }

    /// <summary>
    /// WI-11 — THE TEARDOWN CHOKE POINT for the Accept confirmation. Answering Y/N is only ONE way to leave a
    /// master screen; Ctrl+A (accept-as-is, which bypasses the prompt by design), Esc / the Cancel button (abandon),
    /// navigation away all leave it too. Before this existed the flag stayed TRUE after those exits and the
    /// still-live Y/N arm — which sits EARLIER in the window's first-match-wins chain — then swallowed the next
    /// bare <c>Y</c> on the Gateway, drilling the highlighted row instead of opening Export Data (and leaving a
    /// stale confirmation bar until that stray keystroke). That is a shipped accelerator being SHADOWED, the
    /// exact invariant WI-11 promised not to break.
    /// <para>
    /// Rather than scatter a reset down every exit, this is called from the three places a master screen can be
    /// torn down or superseded: <see cref="OnCurrentScreenChanged"/> (navigating away — Esc, Back, any
    /// jump), <see cref="ClearSubScreens"/> (the page view models being nulled, the campaign convention for new
    /// screen state) and the top of <see cref="ActivateSelected"/> (Ctrl+A, which SAVES WITHOUT changing the
    /// screen and so is invisible to the other two).
    /// </para>
    /// </summary>
    private void ResetMasterAcceptPrompt()
    {
        // Phase 10.11 S3 — the early return does NOT test `_pendingCancelVoucherId`, and a clause that did was
        // removed after a mutation run proved it could never change the outcome. The invariant that makes it
        // redundant: the ONLY writer that arms the id (`RequestCancelHighlightedVoucher`) sets
        // `IsAcceptPromptOpen = true` in the same breath, and the only reader disarms it through here — so
        // "id armed AND prompt closed AND text empty" is unreachable, and on the Ctrl+A teardown path the id can
        // only be armed if the prompt is open, in which case this return does not fire. Keeping it would have been
        // a guard no test can fail — the same dead code wearing the costume of safety that the `id == Guid.Empty`
        // clause in `RequestCancelHighlightedVoucher` was deleted for, twenty lines away in the same slice.
        if (!IsAcceptPromptOpen && AcceptPromptText.Length == 0) return;

        IsAcceptPromptOpen = false;
        AcceptPromptText = string.Empty;
        // The armed cancellation is part of the prompt's state and dies with it.
        _pendingCancelVoucherId = Guid.Empty;
        // Phase 10.11 S4 — and so is the armed DELETION. Missing this line is the defect S3's own comment
        // describes one verb earlier: an armed action that outlives its prompt lets a plain "Y" on the next
        // unrelated Accept confirmation, anywhere in the app, execute it. With Delete behind the channel that is
        // a voucher or a master destroyed by a keystroke aimed at a ledger master.
        _pendingDeleteKind = DeletionTarget.None;
        // …and its id, which is PAIRED-STATE HYGIENE, honestly labelled. It is not independently falsifiable and a
        // mutation run confirms that: the id is only ever read when the KIND is armed, and the kind's disarm IS
        // pinned (`A_dismissed_deletion_cannot_be_executed_by_a_later_unrelated_Y`). It is kept rather than deleted
        // for a mutation score, because clearing one half of a two-field slot and not the other is exactly the
        // asymmetry the next reader would trip over. This is a different category from the dead clause the comment
        // above describes: that one CLAIMED a mechanism it could not deliver.
        _pendingDeleteId = Guid.Empty;
    }

    /// <summary>
    /// Any change of screen tears down whatever master was open, so the Accept confirmation can never survive
    /// into the next screen (see <see cref="ResetMasterAcceptPrompt"/>). Raising the prompt does not change the
    /// screen, so this never cancels a confirmation the operator is looking at.
    /// <para>Phase 10.11 S3 — the window-level <see cref="Notice"/> goes the same way and is cleared HERE rather
    /// than inside <see cref="ResetMasterAcceptPrompt"/>: that method early-returns when no prompt is up, which is
    /// precisely the state a notice is displayed in, so a notice routed through it would never be cleared.</para>
    /// </summary>
    partial void OnCurrentScreenChanged(Screen value)
    {
        ResetMasterAcceptPrompt();
        Notice = string.Empty;
    }

    /// <summary>The human noun for the open master screen, used in the prompt text.</summary>
    private string MasterAcceptNoun() => CurrentScreen switch
    {
        Screen.CreateCompany or Screen.AlterCompany => "Company",
        Screen.LedgerMaster => "Ledger",
        Screen.AccountGroupMaster => "Group",
        Screen.CostCategoryMaster => "Cost Category",
        Screen.CostCentreMaster => "Cost Centre",
        Screen.BudgetMaster => "Budget",
        Screen.ScenarioMaster => "Scenario",
        Screen.CurrencyMaster => "Currency",
        Screen.StockGroupMaster => "Stock Group",
        Screen.StockCategoryMaster => "Stock Category",
        Screen.UnitMaster => "Unit",
        Screen.GodownMaster => "Godown",
        Screen.StockItemMaster => "Stock Item",
        Screen.BatchMaster => "Batch",
        Screen.BomMaster => "Bill of Materials",
        Screen.ReorderLevelsMaster => "Reorder Level",
        Screen.NatureOfPaymentMaster => "Nature of Payment",
        Screen.NatureOfGoodsMaster => "Nature of Goods",
        Screen.EmployeeCategoryMaster => "Employee Category",
        Screen.EmployeeGroupMaster => "Employee Group",
        Screen.EmployeeMaster => "Employee",
        Screen.PayrollUnitMaster => "Payroll Unit",
        Screen.AttendanceTypeMaster => "Attendance Type",
        Screen.PayHeadMaster => "Pay Head",
        Screen.SalaryStructureMaster => "Salary Structure",
        _ => "",
    };

    /// <summary>Ctrl+A on the Currency master: create the currency form's entry (its main create action).</summary>
    public bool CreateCurrency() => CurrencyMaster?.CreateCurrency() ?? false;

    /// <summary>Create a rate-of-exchange quote on the Currency master (the "Add Rate" button).</summary>
    public bool CreateExchangeRate() => CurrencyMaster?.CreateRate() ?? false;

    /// <summary>Re-runs the Forex Gain/Loss revaluation at the current as-of date (the "Recompute" button).</summary>
    public void RecomputeForex() => ForexReport?.Recompute();

    /// <summary>Books the Forex Gain/Loss revaluation adjustment through the engine (the "Book" button).</summary>
    public void BookForexAdjustment() => ForexReport?.BookAdjustment();

    /// <summary>Ctrl+T: toggle the in-progress voucher as post-dated (post-dated cheque handling).</summary>
    public void TogglePostDated()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.TogglePostDated();
        else if (CurrentScreen == Screen.InventoryVoucherEntry)
            InventoryVoucherEntry?.TogglePostDated();
    }

    /// <summary>Ctrl+L: toggle the in-progress voucher as Optional (a provisional, scenario-only entry).</summary>
    public void ToggleOptional()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.ToggleOptional();
    }

    /// <summary>Ctrl+I: toggle the in-progress Purchase/Sales voucher between plain accounting and item-invoice mode.</summary>
    public void ToggleItemInvoice()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.ToggleItemInvoice();
    }

    /// <summary>Ctrl+H "Change Mode": cycle the in-progress Purchase/Sales voucher through As Voucher → Item Invoice →
    /// Accounting Invoice → As Voucher. A no-op on any other screen/type.</summary>
    public void ChangeMode()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.ChangeMode();
    }

    /// <summary>The Accounting-Invoice checkbox affordance: flip the in-progress Purchase/Sales voucher between plain
    /// accounting and accounting-(service)-invoice mode. A no-op on any other screen/type.</summary>
    public void ToggleAccountingInvoice()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.ToggleAccountingInvoice();
    }

    /// <summary>True while a Purchase/Sales voucher-entry page is active (drives the Ctrl+I item-invoice action).</summary>
    public bool IsInvoiceableEntry =>
        CurrentScreen == Screen.VoucherEntry && VoucherEntry?.CanBeItemInvoice == true;

    /// <summary>
    /// True while a voucher-entry page has ANY alternative entry mode for Ctrl+H to change to — Purchase/Sales (the
    /// three invoice modes) OR Contra/Payment/Receipt (Single ⟷ Double Entry, G-6).
    /// <para>This exists because Ctrl+H was gated on <see cref="IsInvoiceableEntry"/>, which is Purchase/Sales only —
    /// so Single Entry, though implemented, would have been unreachable from the keyboard on exactly the three
    /// vouchers it belongs to. TallyPrime has ONE "Change Mode" key whose mode list varies by voucher type; this is
    /// that key's gate.</para>
    /// </summary>
    public bool IsChangeModeEntry =>
        CurrentScreen == Screen.VoucherEntry
        && VoucherEntry is { } entry
        && (entry.CanBeItemInvoice || entry.CanBeSingleEntry);

    /// <summary>True while a Memorandum voucher-entry page is the active screen (drives the Convert action).</summary>
    public bool IsMemorandumEntry =>
        CurrentScreen == Screen.VoucherEntry && VoucherEntry?.Type.BaseType == VoucherBaseType.Memorandum;

    /// <summary>
    /// Converts a posted <b>Memorandum</b> voucher into a real voucher of <paramref name="targetBaseType"/>
    /// (default Journal) so it now affects the books, then persists the company. The memo is removed and the
    /// regularised voucher takes a fresh automatic number for its target type. Returns the new voucher, or
    /// null when the memo/target is invalid (surfaced as a message). This is the UI surface for the engine's
    /// <see cref="Apex.Ledger.Services.LedgerService.ConvertToRegular"/> (catalog §7).
    /// </summary>
    public Voucher? ConvertMemorandum(Guid memorandumVoucherId,
        VoucherBaseType targetBaseType = VoucherBaseType.Journal)
    {
        if (Company is null) return null;

        // Same rule as every other route: an INACTIVE type is never the target of a conversion either — a
        // provisional voucher must not silently become a real one under a series the operator switched off.
        var target = VoucherTypeResolver.ResolveForEntry(Company, targetBaseType);
        if (target is null)
        {
            Message = $"No active '{VoucherTypeResolver.DisplayName(Company, targetBaseType)}' "
                      + "voucher type is configured to convert into.";
            return null;
        }

        try
        {
            var service = new Apex.Ledger.Services.LedgerService(Company);
            var regular = service.ConvertToRegular(memorandumVoucherId, target.Id);
            _storage.Save(Company);
            Message = $"Memorandum converted to {target.Name} No. {Company.FormatVoucherNumber(regular)}.";
            return regular;
        }
        catch (InvalidOperationException ex)
        {
            Message = $"Cannot convert: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Alt+C: create the master appropriate to the active screen. On the stock-only Manufacturing Journal and
    /// BOM screens it inline-creates a COMPONENT stock item (RQ-53) — opening the accounting Ledger master there
    /// is nonsensical. Everywhere else (with a company open) it opens the Ledger-creation master.
    /// <para>WI-1: <paramref name="fieldId"/>/<paramref name="caller"/> carry the FOCUSED voucher field (the view
    /// resolves them from the key source; see <c>Views.CreateField</c>). When they name a master-backed field the
    /// shortcut becomes context-aware "create on the fly": the matching creation screen opens NON-DESTRUCTIVELY
    /// beside the live voucher and the new master is selected back into that very field. Both are null off a
    /// voucher (or on an untagged enum/voucher-reference field), where the historic behaviour above applies.</para>
    /// </summary>
    public void CreateLedgerShortcut(string? fieldId = null, object? caller = null)
    {
        if (Company is null) return;

        // WI-1 — the context-aware arm. Only a TAGGED field dispatches — from a voucher-entry screen, OR from a
        // create column already open over one. That second case is DEPTH 2 and it is the corpus's own example:
        // Alt+C on the Stock Item master's "Under" / "Base unit" picker. Without it the Stock Item creation
        // screen is a DEAD END on a company with no stock group or unit (CanCreate is false and the only offered
        // escape — create the missing master — is unreachable).
        var kind = MasterCreateFields.KindFor(fieldId);
        if (kind != MasterCreateKind.None && (IsCreateOnTheFlyCaller() || IsCreateOnTheFlyOpen))
        {
            CreateMasterOnTheFly(kind, fieldId!, caller);
            return;
        }

        // WI-1 / DEFECT 1 — THE SECOND DATA-LOSS GATE. A create column is open OVER a live entry screen, so
        // CurrentScreen is the MASTER screen and IsCreateOnTheFlyCaller() below is false. Every route past this
        // line replaces the page (OpenPageColumn → TrimColumnsAfter + ClearSubScreens), which would null the
        // entry view model still living underneath and destroy the in-progress voucher — the very loss WI-1
        // exists to remove, reintroduced one screen deeper. Alt+C on an UNTAGGED field of a create column is
        // therefore INERT. (Before this guard a nested Alt+C over a non-Ledger create column fell through to
        // ShowLedgerMaster; over a Ledger create column it was safe only by the != Screen.LedgerMaster accident.)
        if (IsCreateOnTheFlyOpen) return;

        // RQ-53 — the stock-only Manufacturing Journal / BOM screens inline-create a COMPONENT stock item;
        // opening the accounting Ledger master there is nonsensical. On the Manufacturing Journal (a live ENTRY
        // screen) this now goes through the non-destructive open too, so the shipped shortcut can no longer
        // discard a half-built manufacturing entry either. The BOM master is not an entry screen, so it keeps
        // the ordinary page-replacing route.
        if (CurrentScreen == Screen.ManufacturingJournalEntry)
        {
            CreateMasterOnTheFly(MasterCreateKind.StockItem, MasterCreateFields.StockItem, caller: null);
            return;
        }
        if (CurrentScreen == Screen.BomMaster)
        {
            ShowStockItemMaster();
            return;
        }

        // WI-1 — INERT on an entry screen whose focused field has no creatable master behind it (a Dr/Cr side,
        // a bill Ref-Type, a reference to an existing voucher). Falling through to the Ledger master here would
        // be BOTH a wrong-screen open AND — because that route replaces the page — the very data loss this work
        // item exists to remove.
        if (IsCreateOnTheFlyCaller()) return;

        if (CurrentScreen != Screen.LedgerMaster)
            ShowLedgerMaster();
    }

    /// <summary>
    /// WI-1 — the master kind the Alt+C BUTTON creates on the active screen. The button carries no focused
    /// field, so it cannot use the key's field dispatch; it uses the screen's own default instead. On the
    /// stock-only Manufacturing-Journal / BOM screens that is a Stock Item, everywhere else a Ledger.
    /// </summary>
    private MasterCreateKind CreateMasterButtonKind() => CurrentScreen
        is Screen.ManufacturingJournalEntry or Screen.BomMaster
        ? MasterCreateKind.StockItem
        : MasterCreateKind.Ledger;

    /// <summary>
    /// WI-1 — the Alt+C button label for the active screen. On the stock-only Manufacturing-Journal / BOM
    /// screens the shortcut creates a Stock Item, so the button must say so rather than promise a Ledger.
    /// </summary>
    private string CreateMasterButtonLabel()
        => "Create " + MasterCreateFields.NounFor(CreateMasterButtonKind());

    /// <summary>
    /// WI-1 / DEFECT 3 — what the Alt+C BUTTON-BAR item runs. Binding it straight to
    /// <see cref="CreateLedgerShortcut()"/> made it a DEAD control on every voucher-entry screen: with no field
    /// context it hit the inert guard and did nothing while still rendering enabled and captioned "Create
    /// Ledger" (before WI-1 the button bound <c>ShowLedgerMaster</c> and worked, so that was a regression of
    /// shipped behaviour). The button now takes the SAME non-destructive dispatch the key takes — the screen's
    /// default master opens beside the live voucher instead of replacing it — and simply has no field to return
    /// the new master into. Off an entry screen it keeps the historic page-replacing route.
    /// </summary>
    private void CreateMasterFromButton()
    {
        if (Company is null) return;

        if (IsCreateOnTheFlyCaller())
        {
            var kind = CreateMasterButtonKind();
            CreateMasterOnTheFly(kind, MasterCreateFields.FieldIdFor(kind), caller: null);
            return;
        }

        CreateLedgerShortcut();
    }

    // =============================================================== WI-1: Alt+C create-on-the-fly

    /// <summary>
    /// WI-1 — the in-flight "create on the fly" round-trip: which master screen was opened, from which field of
    /// which caller view model, the exact column it was appended as, and the master ids that existed BEFORE it
    /// opened (so the newly-created one can be identified by set difference — no reliance on "the last row",
    /// which a name-sorted master list does not guarantee).
    /// </summary>
    private sealed record CreateOnTheFlyRequest(
        MasterCreateKind Kind,
        string FieldId,
        object? Caller,
        GatewayColumn Column,
        System.Collections.Generic.HashSet<Guid> ExistingIds);

    /// <summary>
    /// WI-1 — the in-flight requests, innermost LAST (a stack). It is a stack rather than a single slot because
    /// the corpus's own case nests: Alt+C on a voucher's item field opens Stock Item Creation, and Alt+C on THAT
    /// screen's "Under" / "Base unit" picker must open Stock Group / Unit Creation over it, then unwind — each
    /// pop returning to the screen beneath with the new master selected. There is deliberately no depth cap.
    /// </summary>
    private readonly System.Collections.Generic.List<CreateOnTheFlyRequest> _createOnTheFly = new();

    /// <summary>True while an Alt+C create screen is open OVER a live entry screen (WI-1).</summary>
    public bool IsCreateOnTheFlyOpen => _createOnTheFly.Count > 0;

    /// <summary>How many create-on-the-fly columns are stacked (0 when none is open). 2 = the nested case.</summary>
    public int CreateOnTheFlyDepth => _createOnTheFly.Count;

    /// <summary>The screens a create-on-the-fly may be launched FROM — the voucher-entry family.</summary>
    private bool IsCreateOnTheFlyCaller() => CurrentScreen
        is Screen.VoucherEntry or Screen.InventoryVoucherEntry or Screen.ManufacturingJournalEntry
        or Screen.JobWorkOrderEntry or Screen.MaterialMovementEntry or Screen.PosBilling;

    /// <summary>
    /// WI-1 — opens <paramref name="kind"/>'s creation screen NON-DESTRUCTIVELY over the live voucher and arms
    /// the return-to-caller. Returns false when the kind has no master screen, or when the shell is not on a
    /// screen a create may be launched from — an entry screen, or a create column already open over one (the
    /// nested Stock Item → Stock Group / Unit case). Anywhere else the ordinary page-replacing route applies.
    /// </summary>
    public bool CreateMasterOnTheFly(MasterCreateKind kind, string fieldId, object? caller)
    {
        if (Company is null || kind == MasterCreateKind.None) return false;
        if (!IsCreateOnTheFlyCaller() && !IsCreateOnTheFlyOpen) return false;

        var existing = SnapshotMasterIds(kind);

        // Build the master with its onChanged wired to the round-trip completion — this is the ONLY difference
        // from the ordinary Show*Master route, which passes an empty callback.
        Action onCreated = CompleteCreateOnTheFly;
        GatewayColumn column;
        Screen screen;
        string title;
        Action setPage;

        switch (kind)
        {
            case MasterCreateKind.Ledger:
            {
                var m = new LedgerMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Ledger Creation", m), Screen.LedgerMaster, "Ledger Creation",
                     () => LedgerMaster = m);
                break;
            }
            case MasterCreateKind.AccountGroup:
            {
                // WI-7 (S2) shipped this master; Alt+C REUSES it rather than introducing a second Group screen.
                var m = new AccountGroupMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Group Creation", m), Screen.AccountGroupMaster, "Group Creation",
                     () => AccountGroupMaster = m);
                break;
            }
            case MasterCreateKind.CostCategory:
            {
                var m = new CostCategoryMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Cost Category Creation", m), Screen.CostCategoryMaster,
                     "Cost Category Creation", () => CostCategoryMaster = m);
                break;
            }
            case MasterCreateKind.CostCentre:
            {
                var m = new CostCentreMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Cost Centre Creation", m), Screen.CostCentreMaster,
                     "Cost Centre Creation", () => CostCentreMaster = m);
                break;
            }
            case MasterCreateKind.StockItem:
            {
                var m = new StockItemMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Stock Item Creation", m), Screen.StockItemMaster,
                     "Stock Item Creation", () => StockItemMaster = m);
                break;
            }
            case MasterCreateKind.StockGroup:
            {
                var m = new StockGroupMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Stock Group Creation", m), Screen.StockGroupMaster,
                     "Stock Group Creation", () => StockGroupMaster = m);
                break;
            }
            case MasterCreateKind.StockCategory:
            {
                var m = new StockCategoryMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Stock Category Creation", m), Screen.StockCategoryMaster,
                     "Stock Category Creation", () => StockCategoryMaster = m);
                break;
            }
            case MasterCreateKind.Unit:
            {
                var m = new UnitMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Unit Creation", m), Screen.UnitMaster,
                     "Unit Creation", () => UnitMaster = m);
                break;
            }
            case MasterCreateKind.Godown:
            {
                var m = new GodownMasterViewModel(Company, _storage, onCreated);
                (column, screen, title, setPage) =
                    (new GatewayColumn("Godown Creation", m), Screen.GodownMaster,
                     "Godown Creation", () => GodownMaster = m);
                break;
            }
            default:
                return false;
        }

        _createOnTheFly.Add(new CreateOnTheFlyRequest(kind, fieldId, caller, column, existing));
        OpenCreateMasterColumn(column, screen, title, setPage);
        return true;
    }

    /// <summary>
    /// WI-1 — THE DATA-LOSS FIX. Appends a create-master page column to the right of the LIVE entry screen
    /// WITHOUT <see cref="TrimColumnsAfter"/>/<see cref="ClearSubScreens"/> — the same non-destructive append the
    /// WI-12 Day-Book Alt+A picker uses (<see cref="OpenAddVoucherFromReport"/>) and the F12 config column uses
    /// over its live report.
    /// <para>Going through <see cref="OpenPageColumn"/> instead would trim back to the last MENU column and null
    /// <see cref="VoucherEntry"/>, SILENTLY DESTROYING the half-typed voucher: the operator who pressed Alt+C to
    /// add one missing ledger would lose every line already keyed. The entry view model instance therefore
    /// survives beneath this column, and popping the column (Esc / the Cancel button / a completed create) re-binds that SAME
    /// instance through <see cref="RehydratePageFromRightmostColumn"/> with its state intact.</para>
    /// </summary>
    private void OpenCreateMasterColumn(GatewayColumn pageColumn, Screen screen, string title, Action setPage)
    {
        setPage();
        Columns.Add(pageColumn);
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = screen;
        ScreenTitle = title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>The ids of every master of <paramref name="kind"/> currently on the company.</summary>
    private System.Collections.Generic.HashSet<Guid> SnapshotMasterIds(MasterCreateKind kind)
        => new(EnumerateMasterIds(kind));

    private System.Collections.Generic.IEnumerable<Guid> EnumerateMasterIds(MasterCreateKind kind)
    {
        if (Company is null) return Array.Empty<Guid>();
        return kind switch
        {
            MasterCreateKind.Ledger => Company.Ledgers.Select(x => x.Id),
            MasterCreateKind.AccountGroup => Company.Groups.Select(x => x.Id),
            MasterCreateKind.CostCategory => Company.CostCategories.Select(x => x.Id),
            MasterCreateKind.CostCentre => Company.CostCentres.Select(x => x.Id),
            MasterCreateKind.StockItem => Company.StockItems.Select(x => x.Id),
            MasterCreateKind.StockGroup => Company.StockGroups.Select(x => x.Id),
            MasterCreateKind.StockCategory => Company.StockCategories.Select(x => x.Id),
            MasterCreateKind.Unit => Company.Units.Select(x => x.Id),
            MasterCreateKind.Godown => Company.Godowns.Select(x => x.Id),
            _ => Array.Empty<Guid>(),
        };
    }

    /// <summary>
    /// WI-1 — the master was created: pop the create column (returning to the SAME live voucher) and SELECT the
    /// new master in the very field Alt+C was pressed in. Identifying the new master by set difference against
    /// the pre-open snapshot is deliberate — master lists are name-sorted, so "the last one" would frequently be
    /// the wrong record.
    /// </summary>
    private void CompleteCreateOnTheFly()
    {
        if (_createOnTheFly.Count == 0) return;

        // The INNERMOST request is the one whose screen just created something (the create columns unwind in
        // stack order), so pop from the top.
        var req = _createOnTheFly[^1];
        var created = EnumerateMasterIds(req.Kind).FirstOrDefault(id => !req.ExistingIds.Contains(id));
        _createOnTheFly.RemoveAt(_createOnTheFly.Count - 1);

        // Pop the create column — the entry view model beneath is re-bound by BackFromPage's rehydrate.
        if (Columns.Count > 0 && ReferenceEquals(Columns[^1], req.Column))
            BackFromPage();

        if (created != Guid.Empty)
            ApplyCreatedMaster(req.Kind, req.FieldId, req.Caller, created);
    }

    /// <summary>
    /// WI-1 — drops every armed create-on-the-fly whose column is no longer in the cascade, i.e. one popped
    /// WITHOUT a create (Esc / the Cancel button) or TRIMMED AWAY by a page-replacing navigation. Without this the request
    /// would stay armed and a later, unrelated master create on the same screen would jump back into a stale
    /// field.
    /// <para><b>DEFECT 2 — the session soft-lock.</b> This used to be called from <see cref="BackFromPage"/>
    /// ALONE, so any <see cref="OpenPageColumn"/> navigation trimmed the create column while leaving the request
    /// armed — and <see cref="IsCreateOnTheFlyOpen"/> then made the new Alt+C guard (and, before it, the
    /// "already open" check) reject EVERY subsequent Alt+C for the rest of the session, silently, because
    /// <see cref="CreateLedgerShortcut"/> discards the false. It is now driven from
    /// <see cref="TrimColumnsAfter"/> — the one place every page-replacing route funnels through — so the
    /// request cannot outlive its column by any path.</para>
    /// </summary>
    private void AbandonCreateOnTheFlyIfColumnGone()
    {
        for (var i = _createOnTheFly.Count - 1; i >= 0; i--)
            if (!Columns.Contains(_createOnTheFly[i].Column))
                _createOnTheFly.RemoveAt(i);
    }

    /// <summary>
    /// WI-1 — writes the newly-created master back into the field Alt+C was pressed in. The caller is the tagged
    /// control's own DataContext (the specific line/row view model), so the value lands in THAT row.
    /// </summary>
    private void ApplyCreatedMaster(MasterCreateKind kind, string fieldId, object? caller, Guid createdId)
    {
        if (Company is null) return;

        // The party/stock-leg pickers are wrapper lists owned by the entry view model; refresh them first so the
        // new ledger is an option before it is selected (otherwise the selection would silently not stick).
        (VoucherEntry as VoucherEntryViewModel)?.RefreshMasterPickers();

        switch (caller)
        {
            case VoucherLineViewModel line when fieldId == MasterCreateFields.Ledger:
                line.SelectedLedger = Company.Ledgers.FirstOrDefault(l => l.Id == createdId);
                return;

            case AdditionalCostRowViewModel row when fieldId == MasterCreateFields.Ledger:
                row.SelectedLedger = Company.Ledgers.FirstOrDefault(l => l.Id == createdId);
                return;

            // Accounting-invoice Particulars row. Resolved out of the ROW'S OWN option list (rebuilt a few lines above
            // by RefreshMasterPickers), not out of Company.Ledgers: that list is filtered to income/expense-nature
            // non-tax ledgers, so a ledger created under some other group is genuinely not selectable here and the
            // field must keep what it had rather than hold a value its ComboBox cannot display. Without this case the
            // create round-trip returned to a blank field on every Alt+C in that column.
            case AccountingInvoiceLineViewModel row when fieldId == MasterCreateFields.Ledger:
                row.SelectedLedger = row.Ledgers.FirstOrDefault(l => l.Id == createdId) ?? row.SelectedLedger;
                return;

            case CostAllocationRowViewModel row when fieldId == MasterCreateFields.CostCategory:
                row.SelectedCategory = Company.CostCategories.FirstOrDefault(c => c.Id == createdId);
                return;

            case CostAllocationRowViewModel row when fieldId == MasterCreateFields.CostCentre:
                row.SelectedCentre = Company.CostCentres.FirstOrDefault(c => c.Id == createdId);
                return;

            case InventoryVoucherLineViewModel line when fieldId == MasterCreateFields.StockItem:
                line.SelectedItem = Company.StockItems.FirstOrDefault(i => i.Id == createdId);
                return;

            case InventoryVoucherLineViewModel line when fieldId == MasterCreateFields.Godown:
                line.SelectedGodown = Company.Godowns.FirstOrDefault(g => g.Id == createdId);
                return;

            case JobWorkComponentLineViewModel line when fieldId == MasterCreateFields.StockItem:
                line.SelectedItem = Company.StockItems.FirstOrDefault(i => i.Id == createdId);
                return;

            case JobWorkComponentLineViewModel line when fieldId == MasterCreateFields.Godown:
                line.SelectedGodown = Company.Godowns.FirstOrDefault(g => g.Id == createdId);
                return;

            case VoucherEntryViewModel entry when fieldId == MasterCreateFields.Party:
                entry.SelectedParty = ResolvePartyOption(entry.Parties, createdId) ?? entry.SelectedParty;
                return;

            case VoucherEntryViewModel entry when fieldId == MasterCreateFields.StockLedger:
                entry.SelectedStockLedger = entry.StockLedgers.FirstOrDefault(l => l.Id == createdId)
                                            ?? entry.SelectedStockLedger;
                return;

            case InventoryVoucherEntryViewModel entry when fieldId == MasterCreateFields.Party:
                entry.SelectedParty = ResolvePartyOption(entry.Parties, createdId) ?? entry.SelectedParty;
                return;

            case JobWorkOrderEntryViewModel entry when fieldId == MasterCreateFields.Party:
                entry.SelectedParty = ResolvePartyOption(entry.Parties, createdId) ?? entry.SelectedParty;
                return;

            case MaterialMovementEntryViewModel entry when fieldId == MasterCreateFields.Party:
                entry.SelectedParty = ResolvePartyOption(entry.Parties, createdId) ?? entry.SelectedParty;
                return;

            case PosBillingViewModel entry when fieldId == MasterCreateFields.Party:
                entry.SelectedParty = ResolvePartyOption(entry.Parties, createdId) ?? entry.SelectedParty;
                return;

            // ---- DEPTH 2: the CREATE SCREEN's own pickers (the corpus's Stock Item → Unit / Stock Group /
            // Stock Category case, plus Ledger → Group). Refresh the master's picker list FIRST — the list was
            // built before the new record existed, so selecting into a stale list silently would not stick.
            case StockItemMasterViewModel m when fieldId == MasterCreateFields.StockGroup:
                m.RefreshPickers();
                m.SelectedGroup = m.Groups.FirstOrDefault(g => g.Id == createdId) ?? m.SelectedGroup;
                return;

            case StockItemMasterViewModel m when fieldId == MasterCreateFields.Unit:
                m.RefreshPickers();
                m.SelectedUnit = m.Units.FirstOrDefault(u => u.Id == createdId) ?? m.SelectedUnit;
                return;

            case StockItemMasterViewModel m when fieldId == MasterCreateFields.StockCategory:
                m.RefreshPickers();
                m.SelectedCategory = m.CategoryOptions.FirstOrDefault(o => o.Category?.Id == createdId)
                                     ?? m.SelectedCategory;
                return;

            case LedgerMasterViewModel m when fieldId == MasterCreateFields.AccountGroup:
                m.RefreshGroups();
                m.SelectedGroup = m.Groups.FirstOrDefault(g => g.Id == createdId) ?? m.SelectedGroup;
                return;
        }
    }

    /// <summary>
    /// WI-1 — the <see cref="PartyOption"/> for a just-created ledger, APPENDING one when the entry screen's
    /// party list was built before the ledger existed. Without the append the round-trip would silently fail on
    /// every entry screen whose party picker is a snapshot wrapper list: the ledger would be created, the
    /// operator returned to the voucher, and the field left blank as though nothing had happened.
    /// </summary>
    private PartyOption? ResolvePartyOption(
        System.Collections.ObjectModel.ObservableCollection<PartyOption> parties, Guid createdId)
    {
        if (parties.FirstOrDefault(p => p.Ledger?.Id == createdId) is { } existing) return existing;
        if (Company?.Ledgers.FirstOrDefault(l => l.Id == createdId) is not { } ledger) return null;

        var option = new PartyOption { Ledger = ledger, Display = ledger.Name };
        parties.Add(option);
        return option;
    }

    /// <summary>Adds a fresh blank particulars line to the current voucher (view "Add line" button).</summary>
    public void AddVoucherLine()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddLine();
    }

    /// <summary>Adds a fresh blank item line to the current item-invoice's inventory grid ("+ Add item").</summary>
    public void AddItemInvoiceLine()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddInventoryLine();
    }

    /// <summary>Adds a fresh blank Particulars line to the current accounting-invoice's grid ("+ Add line").</summary>
    public void AddAccountingInvoiceLine()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddAccountingInvoiceLine();
    }

    /// <summary>Adds a fresh blank line to the current inventory voucher's primary grid ("+ Add line").</summary>
    public void AddInventoryLine()
    {
        if (CurrentScreen == Screen.InventoryVoucherEntry)
            InventoryVoucherEntry?.AddLine();
    }

    /// <summary>Adds a fresh blank line to the current Stock Journal's destination grid ("+ Add destination line").</summary>
    public void AddInventoryDestinationLine()
    {
        if (CurrentScreen == Screen.InventoryVoucherEntry)
            InventoryVoucherEntry?.AddDestinationLine();
    }

    /// <summary>Adds a bill-wise allocation row to a voucher line (the sub-panel "+ Add bill" button).</summary>
    public void AddBillAllocation(VoucherLineViewModel line)
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddBillAllocation(line);
    }

    /// <summary>Adds a cost-allocation row to a voucher line (the sub-panel "+ Add centre" button).</summary>
    public void AddCostAllocation(VoucherLineViewModel line)
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddCostAllocation(line);
    }

    /// <summary>True while an Outstandings (Receivables/Payables) page column is the active screen.</summary>
    public bool IsOutstandingsScreen => CurrentScreen == Screen.Outstandings && Outstandings is not null;

    /// <summary>True while the Chart of Accounts page is the active screen — WI-3 gives it arrow-key highlighting
    /// and Enter-to-alter.</summary>
    public bool IsChartOfAccountsScreen =>
        CurrentScreen == Screen.ChartOfAccounts && ChartOfAccounts is not null;

    /// <summary>True iff the Stock Item master is the live screen — the arrows then move its existing-items
    /// highlight and Ctrl+Enter opens the highlighted item for alteration (WI-3).</summary>
    public bool IsStockItemMasterScreen =>
        CurrentScreen == Screen.StockItemMaster && StockItemMaster is not null;

    // ======================================================= census 7.16: Alter + Delete on the payroll masters

    /// <summary>
    /// The open payroll master screen, seen through the one interface the arrows, Ctrl+Enter and Alt+D all use —
    /// or <c>null</c> when the live screen is not a payroll master.
    ///
    /// <para><b>ONE resolver, deliberately.</b> Census row 7.16 records the defect as a capability missing across
    /// <i>all eight</i> payroll master kinds "rather than eight coincidences", and eight parallel shell arms is
    /// exactly how one kind silently ends up gated differently from the other seven. A screen either appears here
    /// and gets all three verbs, or it appears on none of them.</para>
    /// </summary>
    public IPayrollMasterList? PayrollMasterScreen => CurrentScreen switch
    {
        Screen.EmployeeCategoryMaster => EmployeeCategoryMaster,
        Screen.EmployeeGroupMaster => EmployeeGroupMaster,
        Screen.PayrollUnitMaster => PayrollUnitMaster,
        Screen.AttendanceTypeMaster => AttendanceTypeMaster,

        // 🔴 FOUR OF EIGHT, AND THAT IS THE HONEST STATE OF ROW 7.16 ON THIS BRANCH.
        // Screen.EmployeeMaster and Screen.PayHeadMaster are DELIBERATELY absent: EmployeeMasterViewModel and
        // PayHeadMasterViewModel implement neither IPayrollMasterList nor a ForAlter factory, so listing them
        // here would not compile — and listing them once they merely compile would be worse, because appearing
        // in this switch is what grants a screen the arrows, Ctrl+Enter AND Alt+D in a single step. A kind is
        // added here only when it can be driven end-to-end. The remainder, precisely:
        //   • Employee   — PayrollService.AlterEmployee and DeleteEmployee both exist; the view model needs the
        //                  six interface members, ForAlter, the Ctrl+A IsAltering branch, and the highlight bar
        //                  in its row template. Its list rows already carry a real MasterId.
        //   • Pay head   — blocked further back: PayHeadService has NO Alter method at all.
        //   • Salary structure master and tax declaration master — never considered by the slice.
        // PayrollMasterHalfWiredKindsTests locks all of the above, so this comment cannot quietly go stale.
        _ => null,
    };

    /// <summary>
    /// Ctrl+Enter on a payroll master's existing-list: opens the highlighted master for <b>alteration</b>. Returns
    /// false (and does nothing) on any other screen, or when nothing is highlighted, so the key stays free.
    ///
    /// <para>The <c>ForAlter</c> factories are static and per-type — each builds a whole screen with its own
    /// pickers — so this is the one place that resolves by screen. Everything the eight kinds genuinely share
    /// lives on <see cref="IPayrollMasterList"/> instead of being faked here.</para>
    /// </summary>
    public bool AlterHighlightedPayrollMasterRow()
    {
        if (Company is null) return false;
        if (PayrollMasterScreen is not { IsAltering: false } list) return false;
        if (list.HighlightedMasterRow is not { } row) return false;

        var id = row.MasterId;
        switch (CurrentScreen)
        {
            case Screen.EmployeeCategoryMaster:
            {
                if (EmployeeCategoryMasterViewModel.ForAlter(Company, _storage, id, onChanged: () => { })
                    is not { } m) return false;
                OpenPageColumn(new GatewayColumn(m.Caption, m), Screen.EmployeeCategoryMaster, m.Caption,
                    () => EmployeeCategoryMaster = m);
                return true;
            }
            case Screen.EmployeeGroupMaster:
            {
                if (EmployeeGroupMasterViewModel.ForAlter(Company, _storage, id, onChanged: () => { })
                    is not { } m) return false;
                OpenPageColumn(new GatewayColumn(m.Caption, m), Screen.EmployeeGroupMaster, m.Caption,
                    () => EmployeeGroupMaster = m);
                return true;
            }
            // No Screen.EmployeeMaster arm: EmployeeMasterViewModel has no ForAlter factory yet. See the
            // four-of-eight note on PayrollMasterScreen above for the exact remainder.
            case Screen.PayrollUnitMaster:
            {
                if (PayrollUnitMasterViewModel.ForAlter(Company, _storage, id, onChanged: () => { })
                    is not { } m) return false;
                OpenPageColumn(new GatewayColumn(m.Caption, m), Screen.PayrollUnitMaster, m.Caption,
                    () => PayrollUnitMaster = m);
                return true;
            }
            case Screen.AttendanceTypeMaster:
            {
                if (AttendanceTypeMasterViewModel.ForAlter(Company, _storage, id, onChanged: () => { })
                    is not { } m) return false;
                OpenPageColumn(new GatewayColumn(m.Caption, m), Screen.AttendanceTypeMaster, m.Caption,
                    () => AttendanceTypeMaster = m);
                return true;
            }
            // No Screen.PayHeadMaster arm: PayHeadMasterViewModel has no ForAlter factory, and it could not have
            // a working one — PayHeadService has no Alter method for it to call.
            default:
                return false;
        }
    }

    /// <summary>Arms the confirmation for the highlighted payroll master, exactly the way the Stock Item master's
    /// list does. The engine service owns the referential guards, and it is asked before the question is put.</summary>
    private bool RequestDeletePayrollMasterRow()
    {
        if (PayrollMasterScreen is not { IsAltering: false } list) return false;
        if (list.HighlightedMasterRow is not { } row) return false;

        return Arm(DeletionTarget.PayrollMaster, row.MasterId,
            $"Delete {list.MasterKindLabel} '{row.MasterName}'? This cannot be undone. (Y/N)");
    }

    /// <summary>Spacebar on the Outstandings page: toggle the highlighted bill's multi-select flag.</summary>
    public void ToggleOutstandingSelection()
    {
        if (IsOutstandingsScreen) Outstandings!.ToggleSelectHighlighted();
    }

    /// <summary>
    /// Alt+A on the Outstandings page — settle the spacebar-selected bills by OPENING a Single-Entry
    /// Receipt/Payment pre-loaded with them as Against-Reference allocations. Nothing is posted here: the
    /// operator confirms the date, picks the cash/bank ledger and every per-bill amount, and presses Accept.
    ///
    /// <para>This replaces the deleted Ctrl+B binding, which posted the whole batch unasked (register row IV-5).
    /// <b>Alt+A is TallyPrime's own "Add voucher in report"</b> [TallyHelp keyboard-shortcuts, Reports bottom bar;
    /// CORPUS-BOOK p.431], which is exactly the semantic needed — create a voucher FROM this report — and it is
    /// the meaning this app already gives Alt+A on the Day Book.</para>
    ///
    /// <para><b>The date is not seeded</b> (<c>date: null</c>), so a settlement is dated exactly like any
    /// hand-keyed Receipt/Payment. <b>That is a consistency choice, NOT a protection</b> — an earlier draft of
    /// this comment claimed it avoided <c>Outstandings.AsOf</c>, and it does not: <c>AsOf</c> is the maximum
    /// voucher date in the company and the entry screen's own default is the same expression
    /// (<c>Date = date ?? company.Vouchers.Max(v =&gt; v.Date) ?? BooksBeginFrom</c>), so on every reachable path
    /// the two are equal. A book whose newest voucher is a 31-Mar year-end journal DOES open the settlement dated
    /// 31-Mar. The difference from the deleted Ctrl+B path is that the date is now an editable field on screen
    /// that the operator confirms, rather than one stamped invisibly on an already-posted voucher.</para>
    ///
    /// <para>The <c>onSaved</c> hook is required, not decorative: <see cref="OpenVoucher(VoucherType, DateOnly?,
    /// Action?)"/> defaults to <see cref="ShowGateway"/>, which would dump the operator on the Gateway with no
    /// confirmation that the bill closed. Returning to a refreshed Outstandings of the SAME side is what shows
    /// them the settled bill is gone.</para>
    /// </summary>
    public void OpenSettlementVoucherFromOutstandings()
    {
        if (Company is null || !IsOutstandingsScreen) return;

        var outstandings = Outstandings!;
        var kind = outstandings.Kind;                       // captured now — the page is replaced by the entry screen
        var preload = outstandings.BuildSettlementPreload();
        if (preload is null) return;                        // the page carries the reason in its Message

        OpenVoucher(preload.Type, date: null, onSaved: () => OpenOutstandings(kind));
        if (CurrentScreen != Screen.VoucherEntry || VoucherEntry is null) return;
        VoucherEntry.PreloadSettlement(preload);
    }

    // =============================================================== keyboard navigation

    /// <summary>Moves the highlight up (arrow Up) within the active column, skipping headers; wraps.</summary>
    public void MoveUp() => StepActive(-1);

    /// <summary>Moves the highlight down (arrow Down) within the active column, skipping headers; wraps.</summary>
    public void MoveDown() => StepActive(+1);

    /// <summary>
    /// Steps the highlight in the active list (the cascade's focused menu column on the Gateway, else
    /// the centred pre-company menu). Changing the selection in an earlier column discards all columns
    /// to its right (the far-right page/submenu is replaced when the user next drills in).
    /// </summary>
    private void StepActive(int direction)
    {
        // On the Outstandings page the arrows move the bill-row highlight (for spacebar select + Alt+A settle).
        if (IsOutstandingsScreen)
        {
            Outstandings!.MoveHighlight(direction);
            return;
        }

        // On the GSTR-2B Reconciliation report the arrows move the bucket-row highlight (keeps a live selection).
        if (IsGstr2bReconScreen)
        {
            Gstr2bReconReport!.MoveHighlight(direction);
            return;
        }

        // On the IMS screen the arrows move the 2B-line highlight (the line the Accept/Reject/Pending acts on).
        if (IsImsActionsScreen)
        {
            ImsActions!.MoveHighlight(direction);
            return;
        }

        // On the Post-ITC-Reversal screen the arrows move the posted-reversal highlight (what a Reclaim acts on).
        if (IsPostItcReversalScreen)
        {
            PostItcReversal!.MoveHighlight(direction);
            return;
        }

        // On the e-Invoice / e-Way Bill screens the arrows move the voucher highlight (what the actions act on).
        if (IsGenerateEInvoiceScreen)
        {
            GenerateEInvoice!.MoveHighlight(direction);
            return;
        }

        if (IsGenerateEWayBillScreen)
        {
            GenerateEWayBill!.MoveHighlight(direction);
            return;
        }

        // On the Challan Reconciliation report the arrows move the section-row highlight (keeps a live selection).
        if (IsChallanReconciliationScreen)
        {
            ChallanReconciliation!.MoveHighlight(direction);
            return;
        }

        // On the Form 26Q return the arrows move the deductee-row highlight (keeps a live selection).
        if (IsForm26QScreen)
        {
            Form26Q!.MoveHighlight(direction);
            return;
        }

        // On the Form 24Q return the arrows move the Annexure-I deductee-row highlight (keeps a live selection).
        if (IsForm24QScreen)
        {
            Form24Q!.MoveHighlight(direction);
            return;
        }

        // On the Form 16 certificate the arrows move the employee highlight (rebuilds the certificate).
        if (IsForm16Screen)
        {
            Form16!.MoveHighlight(direction);
            return;
        }

        // On the Income-Tax-Declaration master the arrows move the employee highlight (loads that declaration).
        if (IsTaxDeclarationMasterScreen)
        {
            TaxDeclarationMaster!.MoveHighlight(direction);
            return;
        }

        // On the TCS Challan Reconciliation report the arrows move the code-row highlight (keeps a live selection).
        if (IsTcsChallanReconciliationScreen)
        {
            TcsChallanReconciliation!.MoveHighlight(direction);
            return;
        }

        // On the Form 27EQ return the arrows move the collectee-row highlight (keeps a live selection).
        if (IsForm27EQScreen)
        {
            Form27EQ!.MoveHighlight(direction);
            return;
        }

        // WI-3: on the Chart of Accounts the arrows move the account-row highlight (the master Enter opens for
        // alteration). This arm MUST sit before the IsGatewayCascade branch below — the CoA is a page column, so
        // that branch would otherwise swallow the keystroke and the tree would never respond.
        if (IsChartOfAccountsScreen)
        {
            ChartOfAccounts!.MoveHighlight(direction);
            return;
        }

        // WI-3: on the Stock Item master the arrows move the EXISTING-ITEMS highlight (Ctrl+Enter then opens that
        // item for alteration). Same placement rule as the Chart of Accounts arm above — the master is a page
        // column, so the IsGatewayCascade branch below would swallow the keystroke and the list would never
        // respond. Nothing regresses: a page column is not a menu, so that branch already returned without acting.
        if (IsStockItemMasterScreen)
        {
            StockItemMaster!.MoveHighlight(direction);
            return;
        }

        // 7.16: on any payroll master the arrows move the EXISTING-MASTERS highlight (Ctrl+Enter then opens that
        // master for alteration, Alt+D deletes it). Same placement rule as the two master arms above — these are
        // page columns, so the IsGatewayCascade branch below would swallow the keystroke and the list never move.
        if (PayrollMasterScreen is { } payrollMaster)
        {
            payrollMaster.MoveHighlight(direction);
            return;
        }

        // Numbering S4: on the F12 voucher-numbering config the arrows move the N1 voucher-type highlight (the type
        // whose numbering fields the N2/N3 editors show). Same placement rule as the master arms above — it is a page
        // column, so the IsGatewayCascade branch below would otherwise swallow the keystroke and the list never move.
        if (IsVoucherNumberingConfigScreen)
        {
            VoucherNumberingConfig!.MoveHighlight(direction);
            return;
        }

        if (IsGatewayCascade)
        {
            var col = ActiveColumn;
            if (col is null || !col.IsMenu) return;

            // Moving within an earlier column collapses the columns it had opened to the right.
            if (ActiveColumnIndex < Columns.Count - 1)
            {
                TrimColumnsAfter(ActiveColumnIndex);
                ClearSubScreens();
                CurrentScreen = Screen.Gateway;
            }

            col.Step(direction);
            SyncActiveColumn();
            return;
        }

        // Pre-company centred menu.
        if (Menu.Count == 0 || !Menu.Any(m => m.IsSelectable)) return;
        var index = _menuSelectedIndex;
        for (var i = 0; i < Menu.Count; i++)
        {
            index = (index + direction + Menu.Count) % Menu.Count;
            if (Menu[index].IsSelectable) { SetMenuSelected(index); return; }
        }
    }

    /// <summary>
    /// Enter / Right / Ctrl+A: on a form page runs its accept action; on a menu column drills into the
    /// highlighted item — a Group opens its submenu column, a Page opens its page column, an Action runs.
    /// </summary>
    public void ActivateSelected()
    {
        // 🔴 Phase 10.11 S4 — CTRL+A IS INERT OVER AN ARMED LIFECYCLE QUESTION, AND SAYS SO.
        // The Ctrl+A arm sits ABOVE the WI-11 confirmation arm in the window's tunnel chain, deliberately, so the
        // ~40 accept-as-is screens keep working and "Ctrl+A while the prompt happens to be up simply accepts" — and
        // that reasoning is sound while the prompt asks about the SAME object Ctrl+A would save. It stops being
        // sound the moment the shared channel carries a second verb about a DIFFERENT object. Measured on the Stock
        // Item master with "Widget" highlighted, "Gizmo" typed in the form and `Delete stock item 'Widget'?` up: one
        // Ctrl+A silently discarded the delete question (ResetMasterAcceptPrompt runs at the top of this method) and
        // CREATED Gizmo instead — a destructive question replaced by an unrelated write, with nothing on the notice
        // bar. That is the S1 Alt+Y hole shape on the destructive channel, and the arm's own comment claims the
        // prompt is "MODAL against Alt+letter chords" while leaving this one open.
        // So: answer the lifecycle question first. Two presses, exactly the doctrine already settled for Alt+Y and
        // for Escape. Nothing is saved, nothing is discarded, and the question stays on screen.
        if (IsAcceptPromptOpen
            && (_pendingDeleteKind != DeletionTarget.None || _pendingCancelVoucherId != Guid.Empty))
        {
            RaiseLifecycleNotice("Answer the question on screen first (Y or N) — Ctrl+A does nothing while it is up.");
            return;
        }

        // WI-11: the accept-as-is path. Ctrl+A saves WITHOUT changing the screen, so it is invisible to the
        // screen-change reset — clear the confirmation here or it leaks and shadows the Gateway's bare Y.
        ResetMasterAcceptPrompt();

        switch (CurrentScreen)
        {
            case Screen.CreateCompany:
                CreateCompany();
                return;
            case Screen.AlterCompany:
                AlterCompany?.Accept();
                return;
            case Screen.VoucherEntry:
                AcceptVoucherEntryOrAlteration();
                return;
            case Screen.InventoryVoucherEntry:
                InventoryVoucherEntry?.Accept();
                return;
            // WI-3: the SAME screen serves Create and Alter, so Ctrl+A runs whichever verb it was opened for.
            // Branching on IsAltering (not on a separate screen id) is what lets the alteration form be literally
            // the creation form pre-filled, exactly as Tally does it.
            case Screen.ChartOfAccounts:
                AlterHighlightedChartRow();
                return;
            case Screen.LedgerMaster:
                if (LedgerMaster is { IsAltering: true }) LedgerMaster.Alter();
                else LedgerMaster?.Create();
                return;
            case Screen.AccountGroupMaster:
                if (AccountGroupMaster is { IsAltering: true }) AccountGroupMaster.Alter();
                else AccountGroupMaster?.Create();
                return;
            case Screen.CostCategoryMaster:
                CostCategoryMaster?.Create();
                return;
            case Screen.CostCentreMaster:
                CostCentreMaster?.Create();
                return;
            case Screen.StockGroupMaster:
                StockGroupMaster?.Create();
                return;
            case Screen.StockCategoryMaster:
                StockCategoryMaster?.Create();
                return;
            case Screen.UnitMaster:
                UnitMaster?.Create();
                return;
            case Screen.GodownMaster:
                GodownMaster?.Create();
                return;
            // WI-3: the SAME screen serves Create and Alter here too, so Ctrl+A runs whichever verb it was opened
            // for. Without this branch a Stock Item Alteration screen's Ctrl+A ran Create() — which then failed
            // on the duplicate name, leaving the operator's edits unsaved with a confusing "already exists".
            case Screen.StockItemMaster:
                if (StockItemMaster is { IsAltering: true }) StockItemMaster.Alter();
                else StockItemMaster?.Create();
                return;
            case Screen.BatchMaster:
                BatchMaster?.Create();
                return;
            case Screen.BatchAllocation:
                // Ctrl+A commits the batch allocation; on success pop the sub-screen back to the voucher.
                if (BatchAllocation?.Apply() == true) BackFromPage();
                return;
            case Screen.BomMaster:
                BomMaster?.Create();
                return;
            case Screen.PriceLevelsMaster:
                PriceLevels?.Create();
                return;
            case Screen.PriceListsMaster:
                PriceLists?.Save();
                return;
            case Screen.ReorderLevelsMaster:
                ReorderLevels?.Create();
                return;
            case Screen.EmployeeCategoryMaster:
                EmployeeCategoryMaster?.Create();
                return;
            case Screen.EmployeeGroupMaster:
                EmployeeGroupMaster?.Create();
                return;
            case Screen.EmployeeMaster:
                EmployeeMaster?.Create();
                return;
            case Screen.PayrollUnitMaster:
                PayrollUnitMaster?.Create();
                return;
            case Screen.AttendanceTypeMaster:
                AttendanceTypeMaster?.Create();
                return;
            case Screen.PayHeadMaster:
                PayHeadMaster?.Create();
                return;
            case Screen.SalaryStructureMaster:
                SalaryDetails?.Save();
                return;
            case Screen.AttendanceVoucherEntry:
                AttendanceVoucher?.Accept();
                return;
            case Screen.PayrollVoucherEntry:
                PayrollVoucher?.Accept();
                return;
            case Screen.ManufacturingJournalEntry:
                ManufacturingJournalEntry?.Accept();
                return;
            case Screen.JobWorkOrderEntry:
                JobWorkOrderEntry?.Accept();
                return;
            case Screen.MaterialMovementEntry:
                MaterialMovementEntry?.Accept();
                return;
            case Screen.PosBilling:
                AcceptPosBillingOrAlteration();
                return;
            case Screen.BudgetMaster:
                BudgetMaster?.Create();
                return;
            case Screen.ScenarioMaster:
                ScenarioMaster?.Create();
                return;
            case Screen.CurrencyMaster:
                CurrencyMaster?.CreateCurrency();
                return;
            case Screen.GstConfig:
                GstConfig?.AcceptStatutoryConfig();
                return;
            // Numbering S4: Ctrl+A / Enter commits the working numbering config (validation runs inside Save — a
            // duplicate-date reject, digit-adjacent warn, or historical-stability block/confirm never tears the
            // screen down, so this stays a non-destructive accept action like every other master). Accept semantics
            // parity (Ctrl+A/Enter = accept): the FIRST accept on a change needing confirmation warns (Save →
            // NeedsConfirmation, IsConfirmPending); a SECOND accept confirms it (ConfirmSave persists). Without this
            // branch the accept key re-ran Save(), which resets IsConfirmPending and re-warns forever — leaving Confirm
            // reachable only by mouse / Tab+Space. Purely VM-side; the b8c617e key handler in MainWindow.axaml.cs is untouched.
            case Screen.VoucherNumberingConfig:
                if (VoucherNumberingConfig is { IsConfirmPending: true }) VoucherNumberingConfig.ConfirmSave();
                else VoucherNumberingConfig?.Save();
                return;
            case Screen.GstRateSetup:
                GstRateSetup?.AddRateHistory(); // Ctrl+A appends the add-form's dated rate window (primary action)
                return;
            case Screen.Cmp08Report:
                return; // read-only report — Ctrl+A/Enter is a safe no-op
            case Screen.Gstr4Report:
                return; // read-only report — Ctrl+A/Enter is a safe no-op
            // Advanced-GST report screens (Phase 9 UI-1) — all read-only projections; Ctrl+A/Enter is a safe no-op.
            case Screen.Gstr9Report:
            case Screen.Gstr9cReport:
            case Screen.ElectronicLedgersReport:
            case Screen.ItcSetOffReport:
            case Screen.ItcReversalReport:
            case Screen.Gstr2bReconReport:
            case Screen.ItcGateReport:
            case Screen.QrmpReport:
            case Screen.GstAmendmentsReport:
            case Screen.EInvoiceEWayStatusReport:
                return; // read-only report — Ctrl+A/Enter is a safe no-op
            // Advanced-GST INTERACTIVE action screens (Phase 9 UI-2) — Ctrl+A fires the page's PRIMARY action, which
            // is always an explicit, user-initiated mutation (opening the page never posts).
            case Screen.ImsActions:
                ImsActions?.Accept();   // Ctrl+A = accept the highlighted 2B line
                return;
            case Screen.RunSetOff:
                RunSetOff?.PostSetOff();   // Ctrl+A = run the previewed Rule-88A set-off
                return;
            case Screen.PostItcReversal:
                PostItcReversal?.Post();   // Ctrl+A = post the form's reversal
                return;
            case Screen.ImportGstr2b:
                ImportGstr2b?.Import();    // Ctrl+A = import the chosen portal JSON
                return;
            case Screen.GenerateEInvoice:
                GenerateEInvoice?.PrepareAndWriteJson();   // Ctrl+A = prepare + write the offline INV-01
                return;
            case Screen.GenerateEWayBill:
                GenerateEWayBill?.PrepareAndWriteJson();   // Ctrl+A = prepare + write the offline EWB-01
                return;
            case Screen.NatureOfPaymentMaster:
                NatureOfPaymentMaster?.Create();
                return;
            case Screen.NatureOfGoodsMaster:
                NatureOfGoodsMaster?.Create();
                return;
            case Screen.TdsStatPayment:
                TdsStatPayment?.Deposit();
                return;
            case Screen.ChallanReconciliation:
                return; // read-only report — Ctrl+A/Enter is a safe no-op
            case Screen.Form26Q:
                Form26Q?.ExportFvu(); // Ctrl+A exports the FVU flat file (the return's primary action)
                return;
            case Screen.PfEcrReport:
                PfEcrReport?.ExportEcr(); // Ctrl+A exports the ECR 2.0 flat file (the return's primary action)
                return;
            case Screen.EsiContributionReport:
                EsiContributionReport?.ExportReturn(); // Ctrl+A exports the ESIC monthly-contribution offline file
                return;
            case Screen.ProfessionalTaxRegister:
                ProfessionalTaxRegister?.ExportRegister(); // Ctrl+A exports the PT register CSV (the page's primary action)
                return;
            case Screen.GratuityProvisionRegister:
                GratuityProvisionRegister?.PostProvision(); // Ctrl+A posts the period-end gratuity provision (the page's primary action)
                return;
            case Screen.BonusRegister:
                return; // read-only register — Ctrl+A/Enter is a safe no-op

            case Screen.TaxDeclarationMaster:
                TaxDeclarationMaster?.Save(); // Ctrl+A saves the per-employee Form-12BB declaration
                return;
            case Screen.Form24Q:
                Form24Q?.ExportFvu(); // Ctrl+A exports the Form 24Q flat file (the return's primary action)
                return;
            case Screen.Form16:
                Form16?.ExportPdf(); // Ctrl+A exports the Form 16 certificate PDF (the page's primary action)
                return;
            case Screen.TcsStatPayment:
                TcsStatPayment?.Deposit();
                return;
            case Screen.TcsChallanReconciliation:
                return; // read-only report — Ctrl+A/Enter is a safe no-op
            case Screen.Form27EQ:
                Form27EQ?.ExportFvu(); // Ctrl+A exports the FVU flat file (the return's primary action)
                return;
            case Screen.BankReconciliation:
                BankReconciliation?.Reconcile();
                return;
            case Screen.BankStatementImport:
                BankStatementImport?.Import();
                return;
            // W2-14 (census 14.1) — Enter / Ctrl+A on the Go To index TRAVELS to the highlighted destination.
            // Routed through the same RunSelectedGoTo the panel's own button calls, so key and button can never
            // do two different things (the defect the Alt+C row above records).
            case Screen.GoTo:
                RunSelectedGoTo();
                return;
        }

        if (IsGatewayCascade)
        {
            DrillIn();
            return;
        }

        // Pre-company centred menu.
        if (Menu.Count == 0) return;
        if (_menuSelectedIndex < 0 || _menuSelectedIndex >= Menu.Count) return;
        var item = Menu[_menuSelectedIndex];
        if (item.IsSelectable) item.Activate();
    }

    /// <summary>
    /// Right/Enter on the Gateway cascade: drill into the active column's highlighted item. If it is
    /// already opened as the next column, just move focus there; otherwise open its submenu/page column.
    /// </summary>
    public void DrillIn()
    {
        var col = ActiveColumn;
        var item = col?.Selected;
        if (col is null || !col.IsMenu || item is null || !item.IsSelectable) return;

        switch (item.Kind)
        {
            case MenuItemKind.Group:
                OpenGroupOf(item);
                break;
            case MenuItemKind.Page:
                OpenPageOf(item);
                break;
            case MenuItemKind.Action:
                item.Activate();
                break;
        }
    }

    /// <summary>Opens (or refocuses) the submenu column for a highlighted Group item.</summary>
    private void OpenGroupOf(MenuItemViewModel item)
    {
        TrimColumnsAfter(ActiveColumnIndex);
        ClearSubScreens();
        CurrentScreen = Screen.Gateway;

        var (column, menu, title) = item.Label switch
        {
            "Vouchers" => (BuildVouchersColumn(), GatewayMenu.Vouchers,
                "Gateway of Apex Solutions — Vouchers"),
            "Other Vouchers" => (BuildOtherVouchersColumn(), GatewayMenu.OtherVouchers,
                "Gateway of Apex Solutions — Other Vouchers"),
            "Order Vouchers" => (BuildOrderVouchersColumn(), GatewayMenu.OrderVouchers,
                "Gateway of Apex Solutions — Order Vouchers"),
            "Inventory Vouchers" => (BuildInventoryVouchersColumn(), GatewayMenu.InventoryVouchers,
                "Gateway of Apex Solutions — Inventory Vouchers"),
            "Banking" => (BuildBankingColumn(), GatewayMenu.Banking,
                "Gateway of Apex Solutions — Banking"),
            "Create" => (BuildCreateColumn(), GatewayMenu.Create,
                "Gateway of Apex Solutions — Create"),
            "Statements of Accounts" => (BuildStatementsOfAccountsColumn(), GatewayMenu.StatementsOfAccounts,
                "Gateway of Apex Solutions — Statements of Accounts"),
            "Inventory Reports" => (BuildInventoryReportsColumn(), GatewayMenu.InventoryReports,
                "Gateway of Apex Solutions — Inventory Reports"),
            // "Batch" is a Group ONLY under Inventory Reports (under Create it is a Page → the batch master); the
            // Inventory-Reports hub is the active parent here, so drilling it opens the batch-reports submenu.
            "Batch" when CurrentGatewayMenu == GatewayMenu.InventoryReports => (
                BuildInventoryBatchReportsColumn(), GatewayMenu.InventoryBatchReports,
                "Gateway of Apex Solutions — Batch Reports"),
            "GST Reports" => (BuildGstReportsColumn(), GatewayMenu.GstReports,
                "Gateway of Apex Solutions — GST Reports"),
            "Statements" => (BuildStatementsColumn(), GatewayMenu.Statements,
                "Gateway of Apex Solutions — Statements"),
            "Account Books" => (BuildAccountBooksColumn(), GatewayMenu.AccountBooks,
                "Gateway of Apex Solutions — Account Books"),
            "Cash Book" => (BuildLedgerBookPickerColumn("Cash Book",
                    l => Apex.Ledger.Reports.ClassificationRules.IsCashLedger(l, Company!)),
                GatewayMenu.CashBook, "Gateway of Apex Solutions — Cash Book"),
            "Bank Book" => (BuildLedgerBookPickerColumn("Bank Book",
                    l => Apex.Ledger.Reports.ClassificationRules.IsBankLedger(l, Company!)),
                GatewayMenu.BankBook, "Gateway of Apex Solutions — Bank Book"),
            // "Ledger" is a Group ONLY under Account Books (elsewhere it is a Page → the ledger master); the
            // Account-Books hub is the active parent here, so drilling it opens the all-ledgers book picker.
            "Ledger" when CurrentGatewayMenu == GatewayMenu.AccountBooks => (
                BuildLedgerBookPickerColumn("Ledger", _ => true),
                GatewayMenu.LedgerBooks, "Gateway of Apex Solutions — Ledger"),
            // W2-12 (census 11.7): the two group reports each open a picker of the company's own groups.
            "Group Summary" => (BuildGroupPickerColumn("Group Summary"), GatewayMenu.GroupSummaryPicker,
                "Gateway of Apex Solutions — Group Summary"),
            "Group Vouchers" => (BuildGroupPickerColumn("Group Vouchers"), GatewayMenu.GroupVouchersPicker,
                "Gateway of Apex Solutions — Group Vouchers"),
            "Exception Reports" => (BuildExceptionReportsColumn(), GatewayMenu.ExceptionReports,
                "Gateway of Apex Solutions — Exception Reports"),
            "Statutory Reports" => (BuildStatutoryReportsColumn(), GatewayMenu.StatutoryReports,
                "Gateway of Apex Solutions — Statutory Reports"),
            "TDS Reports" => (BuildTdsReportsColumn(), GatewayMenu.TdsReports,
                "Gateway of Apex Solutions — TDS Reports"),
            "TCS Reports" => (BuildTcsReportsColumn(), GatewayMenu.TcsReports,
                "Gateway of Apex Solutions — TCS Reports"),
            "Payroll" => (BuildPayrollStatutoryReportsColumn(), GatewayMenu.PayrollStatutoryReports,
                "Gateway of Apex Solutions — Payroll"),
            "Composition Returns" => (BuildCompositionReturnsColumn(), GatewayMenu.CompositionReturns,
                "Gateway of Apex Solutions — Composition Returns"),
            "Annual Returns" => (BuildAnnualReturnsColumn(), GatewayMenu.AnnualReturns,
                "Gateway of Apex Solutions — Annual Returns"),
            "GST Returns (Advanced)" => (BuildGstAdvancedReturnsColumn(), GatewayMenu.GstAdvancedReturns,
                "Gateway of Apex Solutions — GST Returns (Advanced)"),
            "GST Actions" => (BuildGstActionsColumn(), GatewayMenu.GstActions,
                "Gateway of Apex Solutions — GST Actions"),
            "Payroll Reports" => (BuildPayrollReportsColumn(), GatewayMenu.PayrollReports,
                "Gateway of Apex Solutions — Payroll Reports"),
            "Outstandings" => (BuildOutstandingsColumn(), GatewayMenu.Outstandings,
                "Gateway of Apex Solutions — Outstandings"),
            "Cost Centres" => (BuildCostCentresColumn(), GatewayMenu.CostCentres,
                "Gateway of Apex Solutions — Cost Centres"),
            "Budgets" => (BuildBudgetsColumn(), GatewayMenu.Budgets,
                "Gateway of Apex Solutions — Budgets"),
            // Data → Backup / Restore (the R-7 carve-out).
            "Backup / Restore" => (BuildDataColumn(), GatewayMenu.Data,
                "Gateway of Apex Solutions — Backup / Restore"),
            _ => (BuildCreateColumn(), GatewayMenu.Create, "Gateway of Apex Solutions"),
        };

        Columns.Add(column);
        column.SelectFirstSelectable();
        ActiveColumnIndex = Columns.Count - 1;
        CurrentGatewayMenu = menu;
        ScreenTitle = title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Opens the page column for a highlighted Page item (report / voucher / ledger / chart).</summary>
    private void OpenPageOf(MenuItemViewModel item)
    {
        // Inside an Account-Books ledger picker (Cash Book / Bank Book / Ledger), every Page row is a LEDGER
        // NAME — open that ledger's Account Book (its LedgerBook) rather than falling through the fixed switch.
        if (CurrentGatewayMenu is GatewayMenu.CashBook or GatewayMenu.BankBook or GatewayMenu.LedgerBooks)
        {
            OpenAccountBook(item.Label);
            return;
        }

        // W2-12 (census 11.7): inside a group picker every Page row is a GROUP NAME — open that group's
        // report rather than falling through to the fixed label switch (where "Group" means the master).
        if (CurrentGatewayMenu is GatewayMenu.GroupSummaryPicker)
        {
            OpenGroupReportByName(ReportKind.GroupSummary, item.Label);
            return;
        }

        if (CurrentGatewayMenu is GatewayMenu.GroupVouchersPicker)
        {
            OpenGroupReportByName(ReportKind.GroupVouchers, item.Label);
            return;
        }

        switch (item.Label)
        {
            // Company → Alter Company (the open company's own profile).
            case "Alter Company": ShowAlterCompany(); break;
            // Data → Backup / Restore (the R-7 carve-out).
            case "Backup Company": OpenBackupCompany(); break;
            case "Restore Company": OpenRestoreCompany(); break;
            case "Chart of Accounts": ShowChartOfAccounts(); break;
            case "Day Book": OpenReport(ReportKind.DayBook); break;
            case "Balance Sheet": OpenReport(ReportKind.BalanceSheet); break;
            case "Profit & Loss A/c": OpenReport(ReportKind.ProfitAndLoss); break;
            case "Trial Balance": OpenReport(ReportKind.TrialBalance); break;
            case "Ledger": ShowLedgerMaster(); break;
            case "Group": ShowAccountGroupMaster(); break;
            case "Cost Category": ShowCostCategoryMaster(); break;
            case "Cost Centre": ShowCostCentreMaster(); break;
            case "Stock Group": ShowStockGroupMaster(); break;
            case "Stock Category": ShowStockCategoryMaster(); break;
            case "Unit": ShowUnitMaster(); break;
            case "Godown": ShowGodownMaster(); break;
            case "Stock Item": ShowStockItemMaster(); break;
            case "Reorder Levels": ShowReorderLevelsMaster(); break;
            case "Batch": ShowBatchMaster(); break;
            case "Bill of Materials": ShowBomMaster(); break;
            case "Price Level": ShowPriceLevelsMaster(); break;
            // "Price List" is the master under Create, but the report under Inventory Reports (mirrors "Batch").
            case "Price List" when CurrentGatewayMenu == GatewayMenu.InventoryReports:
                OpenReport(ReportKind.PriceList); break;
            case "Price List": ShowPriceListsMaster(); break;
            case "Batch-wise": OpenReport(ReportKind.Batchwise); break;
            case "Age Analysis": OpenReport(ReportKind.BatchAgeAnalysis); break;
            case "Budget": ShowBudgetMaster(); break;
            case "Scenario": ShowScenarioMaster(); break;
            case "Currency": ShowCurrencyMaster(); break;
            case "GST & Taxation": ShowGstConfig(); break;
            case "GST Rate Setup": ShowGstRateSetup(); break;
            // Composition returns (Phase 9 slice 3) — under Reports → Statutory Reports → Composition Returns.
            case "CMP-08": OpenCmp08Report(); break;
            case "GSTR-4": OpenGstr4Report(); break;
            // Advanced-GST report screens (Phase 9 UI-1) — under Reports → Statutory Reports → Annual Returns /
            // GST Returns (Advanced).
            case "GSTR-9": OpenGstr9Report(); break;
            case "GSTR-9C": OpenGstr9cReport(); break;
            case "Electronic Ledgers": OpenElectronicLedgersReport(); break;
            case "ITC Set-Off": OpenItcSetOffReport(); break;
            case "ITC Reversal": OpenItcReversalReport(); break;
            case "GSTR-2B Reconciliation": OpenGstr2bReconReport(); break;
            case "ITC Gate": OpenItcGateReport(); break;
            case "QRMP / IFF": OpenQrmpReport(); break;
            case "GST Amendments": OpenGstAmendmentsReport(); break;
            case "e-Invoice / e-Way Status": OpenEInvoiceEWayStatusReport(); break;
            // Advanced-GST INTERACTIVE action screens (Phase 9 UI-2) — under Reports → Statutory Reports → GST Actions.
            case "IMS (Accept / Reject / Pending)": OpenImsActions(); break;
            case "Run Set-Off & Pay": OpenRunSetOff(); break;
            case "Post ITC Reversal": OpenPostItcReversal(); break;
            case "Import GSTR-2B": OpenImportGstr2b(); break;
            case "Generate e-Invoice": OpenGenerateEInvoice(); break;
            case "Generate e-Way Bill": OpenGenerateEWayBill(); break;
            case "Nature of Payment": ShowNatureOfPaymentMaster(); break;
            case "Nature of Goods": ShowNatureOfGoodsMaster(); break;
            // Payroll masters (Phase 8 slice 1) — under Masters → Create → Payroll Masters, gated by F11 Maintain Payroll.
            case "Employee Category": ShowEmployeeCategoryMaster(); break;
            case "Employee Group": ShowEmployeeGroupMaster(); break;
            case "Employee": ShowEmployeeMaster(); break;
            case "Payroll Unit": ShowPayrollUnitMaster(); break;
            case "Attendance / Production Type": ShowAttendanceTypeMaster(); break;
            case "Pay Head": ShowPayHeadMaster(); break;
            case "Salary Details": ShowSalaryStructureMaster(); break;
            case "Income Tax Declaration": ShowTaxDeclarationMaster(); break;
            // Payroll vouchers (Phase 8 slice 3) — under Transactions → Vouchers → Payroll, gated by F11 Maintain Payroll.
            case "Attendance / Production": ShowAttendanceVoucher(); break;
            case "Payroll": ShowPayrollVoucher(); break;
            case "TDS Stat Payment": ShowTdsStatPayment(); break;
            case "Challan Reconciliation": OpenChallanReconciliation(); break;
            // CA S9 — each return/certificate answers to BOTH its 1961-Act and its confirmed 2025-Act number, because
            // FormMenuLabel picks the label by financial year and this switch dispatches on that label. Omitting a
            // renumbered case would leave the menu item present but DEAD from FY 2026-27 onward. The dual-form labels
            // ("Form 26Q / 140") are matched too, for the no-company-in-scope fallback.
            case "Form 26Q" or "Form 140" or "Form 26Q / 140": OpenForm26Q(); break;
            case "TCS Stat Payment": ShowTcsStatPayment(); break;
            case "TCS Challan Reconciliation": OpenTcsChallanReconciliation(); break;
            case "Form 27EQ" or "Form 143" or "Form 27EQ / 143": OpenForm27EQ(); break;
            case "Form 16A" or "Form 131" or "Form 16A / 131": OpenForm16A(); break;
            case "Form 27D" or "Form 133" or "Form 27D / 133": OpenForm27D(); break;
            case "Form 27A (TDS)": OpenForm27A("26Q"); break;
            case "Form 27A (TCS)": OpenForm27A("27EQ"); break;
            case "Receivables": OpenOutstandings(OutstandingsKind.Receivables); break;
            case "Payables": OpenOutstandings(OutstandingsKind.Payables); break;
            case "Category Summary": OpenCostReport(CostReportKind.CategorySummary); break;
            case "Cost Centre Break-up": OpenCostReport(CostReportKind.CostCentreBreakup); break;
            case "Budget Variance": OpenBudgetVariance(); break;
            case "Interest Calculation": OpenInterestReport(); break;
            case "Forex Gain/Loss": OpenForexReport(); break;
            case "Stock Summary": OpenReport(ReportKind.StockSummary); break;
            case "Godown Summary": OpenReport(ReportKind.GodownSummary); break;
            case "Stock Movement": OpenReport(ReportKind.StockItemMovement); break;
            case "Reorder Status": OpenReport(ReportKind.ReorderStatus); break;
            case "Receipt Note Register": OpenReport(ReportKind.ReceiptNoteRegister); break;
            case "Delivery Note Register": OpenReport(ReportKind.DeliveryNoteRegister); break;
            case "Rejection Register": OpenReport(ReportKind.RejectionRegister); break;
            case "Physical Stock Register": OpenReport(ReportKind.PhysicalStockRegister); break;
            case "Order Register": OpenReport(ReportKind.OrderRegister); break;
            case "POS Register": OpenReport(ReportKind.PosRegister); break;
            case "Tax Analysis": OpenReport(ReportKind.TaxAnalysis); break;
            case "GSTR-1": OpenReport(ReportKind.Gstr1); break;
            case "GSTR-3B": OpenReport(ReportKind.Gstr3b); break;
            case "Cash Flow": OpenReport(ReportKind.CashFlow); break;
            case "Funds Flow": OpenReport(ReportKind.FundsFlow); break;
            case "Ratio Analysis": OpenReport(ReportKind.RatioAnalysis); break;
            case "Negative Stock": OpenReport(ReportKind.NegativeStock); break;
            case "Negative Cash / Bank": OpenReport(ReportKind.NegativeCashBank); break;
            case "Memorandum Register": OpenReport(ReportKind.MemorandumRegister); break;
            case "Reversing Journal Register": OpenReport(ReportKind.ReversingJournalRegister); break;
            // W2-12 (census 11.6) — Reports → Account Books → Registers. Each opens MONTH-WISE.
            case "Sales Register": OpenReport(ReportKind.SalesRegister); break;
            case "Purchase Register": OpenReport(ReportKind.PurchaseRegister); break;
            case "Journal Register": OpenReport(ReportKind.JournalRegister); break;
            case "Credit Note Register": OpenReport(ReportKind.CreditNoteRegister); break;
            case "Debit Note Register": OpenReport(ReportKind.DebitNoteRegister); break;
            // W2-12 (census 11.8) — Reports → Statements of Accounts → Statistics.
            case "Statistics": OpenReport(ReportKind.Statistics); break;
            // Statutory TDS/TCS exception & outstanding reports (Phase 7 slice 8) — under Reports → Statutory Reports.
            case "TDS Outstandings": OpenReport(ReportKind.TdsOutstanding); break;
            case "TDS Not Deducted": OpenReport(ReportKind.TdsNotDeducted); break;
            case "TDS Interest u/s 201(1A)": OpenReport(ReportKind.TdsInterest); break;
            case "TDS Nature of Payment Summary": OpenReport(ReportKind.TdsNatureSummary); break;
            case "TCS Outstandings": OpenReport(ReportKind.TcsOutstanding); break;
            case "TCS Not Collected": OpenReport(ReportKind.TcsNotCollected); break;
            case "TCS Interest u/s 206C(7)": OpenReport(ReportKind.TcsInterest); break;
            case "TCS Nature of Goods Summary": OpenReport(ReportKind.TcsNatureSummary); break;
            case "Ledgers without PAN": OpenReport(ReportKind.LedgersWithoutPan); break;
            // Payroll statutory reports (Phase 8 slice 4/5) — under Reports → Statutory Reports → Payroll.
            case "PF ECR / Challan": OpenPfEcrReport(); break;
            case "ESI Monthly Contribution": OpenEsiContributionReport(); break;
            case "PT Deduction Register": OpenProfessionalTaxRegister(); break;
            // Gratuity provision + statutory Bonus registers (Phase 8 slice 9) — under Reports → Statutory Reports → Payroll.
            case "Gratuity Provision": OpenGratuityProvisionRegister(); break;
            case "Bonus Register": OpenBonusRegister(); break;
            // §192 salary-TDS return + certificate (Phase 8 slice 7) — under Reports → Statutory Reports → Payroll.
            case "Form 24Q" or "Form 138" or "Form 24Q / 138": OpenForm24Q(); break;
            case "Form 16" or "Form 130" or "Form 16 / 130": OpenForm16(); break;
            // Payroll presentation reports (Phase 8 slice 8) — under Reports → Payroll Reports.
            case "Payslip": OpenReport(ReportKind.Payslip); break;
            case "Pay Sheet": OpenReport(ReportKind.PaySheet); break;
            case "Payroll Register": OpenReport(ReportKind.PayrollRegister); break;
            case "Attendance Register": OpenReport(ReportKind.AttendanceRegister); break;
            case "Payment Advice": OpenReport(ReportKind.PaymentAdvice); break;
            case "Bank Reconciliation": OpenBankReconciliation(); break;
            case "Import Bank Statement": OpenBankStatementImport(); break;
            case "Contra": OpenVoucher(VoucherBaseType.Contra); break;
            case "Payment": OpenVoucher(VoucherBaseType.Payment); break;
            case "Receipt": OpenVoucher(VoucherBaseType.Receipt); break;
            case "Journal": OpenVoucher(VoucherBaseType.Journal); break;
            case "Sales": OpenVoucher(VoucherBaseType.Sales); break;
            case "Purchase": OpenVoucher(VoucherBaseType.Purchase); break;
            // The §34 Credit / Debit Note entries — their new menu rows (Transactions → Vouchers) route here, to
            // the same screens Alt+F6 / Alt+F5 already opened.
            case "Credit Note": OpenVoucher(VoucherBaseType.CreditNote); break;
            case "Debit Note": OpenVoucher(VoucherBaseType.DebitNote); break;
            case "Reversing Journal": OpenVoucher(VoucherBaseType.ReversingJournal); break;
            case "Memorandum": OpenVoucher(VoucherBaseType.Memorandum); break;
            case "Purchase Order": OpenInventoryVoucher(VoucherBaseType.PurchaseOrder); break;
            case "Sales Order": OpenInventoryVoucher(VoucherBaseType.SalesOrder); break;
            case "Receipt Note": OpenInventoryVoucher(VoucherBaseType.ReceiptNote); break;
            case "Delivery Note": OpenInventoryVoucher(VoucherBaseType.DeliveryNote); break;
            case "Rejection In": OpenInventoryVoucher(VoucherBaseType.RejectionIn); break;
            case "Rejection Out": OpenInventoryVoucher(VoucherBaseType.RejectionOut); break;
            case "Stock Journal": OpenInventoryVoucher(VoucherBaseType.StockJournal); break;
            case "Physical Stock": OpenInventoryVoucher(VoucherBaseType.PhysicalStock); break;
            case "Manufacturing Journal": OpenManufacturingJournal(); break;
            case "POS Billing": OpenPosBilling(); break;
            // Job Work vouchers (Phase 6 slice 8; RQ-47/RQ-48) — under F10 Other Vouchers, gated by the F11 feature.
            case "Job Work In Order": OpenJobWorkOrder(JobWorkDirection.In); break;
            case "Job Work Out Order": OpenJobWorkOrder(JobWorkDirection.Out); break;
            case "Material In": OpenMaterialMovement(VoucherBaseType.MaterialIn); break;
            case "Material Out": OpenMaterialMovement(VoucherBaseType.MaterialOut); break;
            // Job Work registers (Phase 6 slice 8; RQ-51) — under Reports → Inventory Reports → Job Work Reports.
            case "Job Work In Order Book": OpenReport(ReportKind.JobWorkInOrderBook); break;
            case "Job Work Out Order Book": OpenReport(ReportKind.JobWorkOutOrderBook); break;
            case "Material In Register": OpenReport(ReportKind.MaterialInRegister); break;
            case "Material Out Register": OpenReport(ReportKind.MaterialOutRegister); break;
        }
    }

    /// <summary>
    /// Esc / Left: steps back one level. On a pre-company screen the classic step-back applies. On the
    /// Gateway cascade it removes the rightmost column and returns focus to the previous column, with
    /// its selection intact — collapsing to Company Select once only the root column remains.
    /// </summary>
    public void Back()
    {
        switch (CurrentScreen)
        {
            case Screen.CreateCompany:
                ShowCompanySelect();
                return;
            case Screen.CompanySelect:
                return; // top level — nothing above
        }

        if (IsGatewayCascade)
        {
            BackFromPage();
            return;
        }

        ShowGateway();
    }

    /// <summary>
    /// Removes the rightmost cascade column and refocuses the previous one (its selection intact). When
    /// only the root column is left, leaves the Gateway to Company Select.
    /// </summary>
    private void BackFromPage()
    {
        if (!IsGatewayCascade || Columns.Count == 0)
        {
            ShowGateway();
            return;
        }

        if (Columns.Count <= 1)
        {
            ShowCompanySelect();
            return;
        }

        Columns.RemoveAt(Columns.Count - 1);
        ClearSubScreens();
        // WI-1: an Alt+C create column that is popped WITHOUT creating (Esc / the Cancel button) disarms the round-trip.
        AbandonCreateOnTheFlyIfColumnGone();
        ActiveColumnIndex = Columns.Count - 1;
        // If a page column survives (e.g. the report under a just-closed F12 config column), re-bind its
        // page view model and screen so the surviving page stays live — otherwise fall to the Gateway.
        RehydratePageFromRightmostColumn();
        CurrentGatewayMenu = RightmostMenuKind();
        ScreenTitle = Columns[ActiveColumnIndex].Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>
    /// After a column pop, re-binds the surviving rightmost column's page view model to its shell property
    /// and restores <see cref="CurrentScreen"/> so a page that sat to the LEFT of a just-closed column (e.g.
    /// a report under its F12 config panel) is not left orphaned. When the rightmost column is a menu, the
    /// shell returns to the Gateway. Only the page kinds that can sit beneath another page column need be
    /// handled here; the rest fall through to the Gateway (unchanged behaviour).
    /// </summary>
    private void RehydratePageFromRightmostColumn()
    {
        CurrentScreen = BindPageColumn(Columns[ActiveColumnIndex]);

        // WI-1 DEPTH 2 — while a create column is STILL open, the page columns BENEATH it must stay bound too.
        // BackFromPage's ClearSubScreens nulls every page property, and binding only the rightmost would leave
        // the in-progress voucher unreachable from the shell (VoucherEntry null) even though its column, and all
        // its data, are still there — so the write-back that follows a nested create would silently skip it.
        if (IsCreateOnTheFlyOpen)
            for (var i = 0; i < ActiveColumnIndex; i++)
                if (Columns[i].IsPage) BindPageColumn(Columns[i]);
    }

    /// <summary>
    /// Re-binds ONE surviving column's page view model to its shell property and reports the screen it
    /// represents (Gateway for a menu column or a page kind that never sits beneath another).
    /// </summary>
    private Screen BindPageColumn(GatewayColumn col)
    {
        switch (col.Page)
        {
            case ReportsViewModel r:
                Reports = r;
                return Screen.Report;
            // RQ-7 drill columns can sit beneath one another (report → ledger-vouchers → voucher-detail); when a
            // deeper drill column is popped the surviving one must be re-bound so it stays live. A report may also
            // survive beneath a just-popped ledger-vouchers/voucher-detail column.
            case LedgerVouchersViewModel lv:
                LedgerVouchers = lv;
                return Screen.LedgerVouchers;
            case VoucherDetailViewModel vd:
                VoucherDetail = vd;
                return Screen.VoucherDetail;
            // A print-preview column survives beneath a just-popped F12 print-config panel (RQ-12), so re-bind it.
            case PrintPreviewViewModel pv:
                PrintPreview = pv;
                return Screen.PrintPreview;
            // WI-1 — an ENTRY screen survives beneath a just-popped Alt+C create-master column. Re-binding the
            // SAME view-model instance (the column has held it all along) is what makes the in-progress voucher
            // come back with every line, party and amount intact instead of as a fresh blank entry.
            case VoucherEntryViewModel ve:
                VoucherEntry = ve;
                return Screen.VoucherEntry;
            case InventoryVoucherEntryViewModel ive:
                InventoryVoucherEntry = ive;
                return Screen.InventoryVoucherEntry;
            case ManufacturingJournalEntryViewModel mje:
                ManufacturingJournalEntry = mje;
                return Screen.ManufacturingJournalEntry;
            case JobWorkOrderEntryViewModel jwe:
                JobWorkOrderEntry = jwe;
                return Screen.JobWorkOrderEntry;
            case MaterialMovementEntryViewModel mme:
                MaterialMovementEntry = mme;
                return Screen.MaterialMovementEntry;
            case PosBillingViewModel pos:
                PosBilling = pos;
                return Screen.PosBilling;
            // WI-1 DEPTH 2 — a MASTER-creation column can itself sit beneath a nested Alt+C create column (Stock
            // Item Creation with Stock Group / Unit Creation over it). Re-binding the SAME instance is what
            // brings the half-filled master back with its name, alias and opening balance intact — and what lets
            // ApplyCreatedMaster then select the just-created record into the field it was launched from.
            case LedgerMasterViewModel lm:
                LedgerMaster = lm;
                return Screen.LedgerMaster;
            case AccountGroupMasterViewModel agm:
                AccountGroupMaster = agm;
                return Screen.AccountGroupMaster;
            case StockItemMasterViewModel sim:
                StockItemMaster = sim;
                return Screen.StockItemMaster;
            case StockGroupMasterViewModel sgm:
                StockGroupMaster = sgm;
                return Screen.StockGroupMaster;
            case StockCategoryMasterViewModel scm:
                StockCategoryMaster = scm;
                return Screen.StockCategoryMaster;
            case UnitMasterViewModel um:
                UnitMaster = um;
                return Screen.UnitMaster;
            case GodownMasterViewModel gm:
                GodownMaster = gm;
                return Screen.GodownMaster;
            case CostCategoryMasterViewModel ccatm:
                CostCategoryMaster = ccatm;
                return Screen.CostCategoryMaster;
            case CostCentreMasterViewModel ccm:
                CostCentreMaster = ccm;
                return Screen.CostCentreMaster;
            default:
                return Screen.Gateway;
        }
    }

    /// <summary>The submenu kind of the rightmost menu column (Root when it is the root Gateway).</summary>
    private GatewayMenu RightmostMenuKind()
    {
        for (var i = Columns.Count - 1; i >= 0; i--)
            if (Columns[i].IsMenu)
                return Columns[i].Title switch
                {
                    "Vouchers" => GatewayMenu.Vouchers,
                    "Other Vouchers" => GatewayMenu.OtherVouchers,
                    "Order Vouchers" => GatewayMenu.OrderVouchers,
                    "Inventory Vouchers" => GatewayMenu.InventoryVouchers,
                    "Banking" => GatewayMenu.Banking,
                    "Create" => GatewayMenu.Create,
                    "Statements of Accounts" => GatewayMenu.StatementsOfAccounts,
                    "Inventory Reports" => GatewayMenu.InventoryReports,
                    "Batch" => GatewayMenu.InventoryBatchReports,
                    "GST Reports" => GatewayMenu.GstReports,
                    "Statements" => GatewayMenu.Statements,
                    "Exception Reports" => GatewayMenu.ExceptionReports,
                    "Statutory Reports" => GatewayMenu.StatutoryReports,
                    "TDS Reports" => GatewayMenu.TdsReports,
                    "TCS Reports" => GatewayMenu.TcsReports,
                    "Payroll" => GatewayMenu.PayrollStatutoryReports,
                    "Composition Returns" => GatewayMenu.CompositionReturns,
                    "Annual Returns" => GatewayMenu.AnnualReturns,
                    "GST Returns (Advanced)" => GatewayMenu.GstAdvancedReturns,
                    "GST Actions" => GatewayMenu.GstActions,
                    "Payroll Reports" => GatewayMenu.PayrollReports,
                    "Account Books" => GatewayMenu.AccountBooks,
                    "Cash Book" => GatewayMenu.CashBook,
                    "Bank Book" => GatewayMenu.BankBook,
                    "Ledger" => GatewayMenu.LedgerBooks,
                    "Outstandings" => GatewayMenu.Outstandings,
                    "Cost Centres" => GatewayMenu.CostCentres,
                    "Budgets" => GatewayMenu.Budgets,
                    "Backup / Restore" => GatewayMenu.Data,
                    _ => GatewayMenu.Root,
                };
        return GatewayMenu.Root;
    }

    /// <summary>The currently focused column, or null.</summary>
    private GatewayColumn? ActiveColumn =>
        ActiveColumnIndex >= 0 && ActiveColumnIndex < Columns.Count ? Columns[ActiveColumnIndex] : null;

    /// <summary>
    /// The kind of the focused menu column, or null when the focused column is not a menu (a page column) or
    /// there is no cascade. Exposed so a test can assert the WI-2/WI-9 routing rule directly.
    /// </summary>
    public GatewayColumnKind? ActiveColumnKind =>
        IsGatewayCascade && ActiveColumn is { IsMenu: true } col ? col.Kind : null;

    /// <summary>The bare-letter hotkey rows of the focused menu column — diagnostics and tests.</summary>
    public System.Collections.Generic.IReadOnlyList<MenuItemViewModel> ActiveColumnHotKeyItems =>
        ActiveColumn is { IsMenu: true } col
            ? col.Items.Where(i => i.HasHotKey).ToList()
            : System.Array.Empty<MenuItemViewModel>();

    /// <summary>The type-ahead prefix accumulated in the focused data-driven picker column ("" when none).</summary>
    public string ActiveTypeAheadPrefix => ActiveColumn?.TypeAheadPrefix ?? string.Empty;

    /// <summary>
    /// WI-2 / WI-9 — THE CONFLICT RESOLUTION, in one place. A bare letter arriving on the Gateway cascade means
    /// one of two different things, decided by the KIND of the focused column (never case by case):
    /// <list type="bullet">
    /// <item><b>Authored</b> menu column (Gateway, Vouchers, Create, …) — the letter ACTIVATES the row that owns
    /// it as its computed hotkey, exactly as if the operator had arrowed to that row and pressed Enter.</item>
    /// <item><b>Data-driven</b> picker column (ledgers, parties, stock items) — the letter FILTERS: it extends
    /// the type-ahead prefix and moves the highlight to the first matching row. Enter then selects it.</item>
    /// </list>
    /// <para>
    /// Returns <c>true</c> only when the letter was actually consumed, so the caller (the window's tunnel
    /// handler) can fall through to the existing bare-letter accelerators — the report quick-jumps (B/P/T/D)
    /// and the Gateway-root E/O/Y panels — whenever no row claimed it. That "claimed or fall through" contract
    /// is what keeps this arm from shadowing the accelerators that already shipped.
    /// </para>
    /// </summary>
    public bool HandleMenuLetter(char letter)
    {
        if (!IsGatewayCascade) return false;
        if (ActiveColumn is not { IsMenu: true } col) return false;

        if (col.Kind == GatewayColumnKind.DataDriven)
        {
            if (!col.TypeAhead(letter)) return false;
            SyncActiveColumn();
            return true;
        }

        var target = col.FindByHotKey(letter);
        if (target is null) return false;

        var index = col.Items.IndexOf(target);
        if (index < 0) return false;

        col.SetSelected(index);
        SyncActiveColumn();
        DrillIn();
        return true;
    }

    /// <summary>
    /// Repaints the cascade after a focus/selection change: marks the active column active (bright
    /// highlight) and the rest inactive (dim), and mirrors the active menu column into <see cref="Menu"/>
    /// / <see cref="SelectedIndex"/> so the keyboard driver and headless tests see the focused list.
    /// </summary>
    private void SyncActiveColumn()
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            Columns[i].SetActive(i == ActiveColumnIndex);
            // C4 F1: only the rightmost column is "terminal" and fills the leftover viewport; a page column
            // with another column to its right keeps a bounded width so a report + its drill fit together.
            Columns[i].IsLast = i == Columns.Count - 1;
        }

        Menu.Clear();
        var col = ActiveColumn;
        if (col is not null && col.IsMenu)
            foreach (var item in col.Items)
                Menu.Add(item);
        _menuSelectedIndex = col?.SelectedIndex ?? -1;
    }

    private void SetMenuSelected(int index)
    {
        for (var i = 0; i < Menu.Count; i++)
            Menu[i].IsSelected = i == index && Menu[i].IsSelectable;
        _menuSelectedIndex = index;
    }

    /// <summary>
    /// Index of the currently highlighted item — the active cascade column's selection on the Gateway,
    /// else the centred pre-company menu's selection.
    /// </summary>
    public int SelectedIndex => IsGatewayCascade ? (ActiveColumn?.SelectedIndex ?? -1) : _menuSelectedIndex;

    // =============================================================== right button bar

    /// <summary>
    /// F12 Configure — context-sensitive ledger-screen configuration (Book pp.133–141): on the Ledger master it
    /// toggles the "Method of Appropriation" additional-cost field's visibility; elsewhere a Phase-1 hint.
    /// </summary>
    private void F12Configure()
    {
        // Re-pressing F12 while the numbering config is already open POPS it (the F12/Esc toggle, numbering S4).
        if (CurrentScreen == Screen.VoucherNumberingConfig)
        {
            Back();
            return;
        }

        if (CurrentScreen == Screen.LedgerMaster && LedgerMaster is { } lm)
        {
            lm.ToggleConfiguration();
            return;
        }

        // On a voucher-entry context F12 opens the per-type voucher-numbering configuration (numbering-design-v2 §5.1),
        // pushed as a cascade column to the right (prior panes persist). Every other F12 context is unchanged: the
        // report-F12 (OpenReportConfig) and print-preview-F12 (OpenPrintConfig) are handled earlier in the key tunnel.
        if (Company is not null && IsVoucherNumberingContext(out var preselectTypeId))
        {
            OpenVoucherNumberingConfig(preselectTypeId);
            return;
        }

        Message = "F12 Configure — display options (Phase 1 defaults).";
    }

    /// <summary>
    /// True when the live screen is a voucher-entry context whose type's numbering F12 configures; outputs that
    /// type's id so the config column opens pre-selected on it (numbering S4).
    /// </summary>
    private bool IsVoucherNumberingContext(out Guid preselectTypeId)
    {
        preselectTypeId = Guid.Empty;
        switch (CurrentScreen)
        {
            case Screen.VoucherEntry when VoucherEntry is { } v: preselectTypeId = v.Type.Id; return true;
            case Screen.InventoryVoucherEntry when InventoryVoucherEntry is { } iv: preselectTypeId = iv.Type.Id; return true;
            case Screen.ManufacturingJournalEntry when ManufacturingJournalEntry is { } mj: preselectTypeId = mj.Type.Id; return true;
            case Screen.JobWorkOrderEntry when JobWorkOrderEntry is { } jw: preselectTypeId = jw.Type.Id; return true;
            case Screen.MaterialMovementEntry when MaterialMovementEntry is { } mm: preselectTypeId = mm.Type.Id; return true;
            case Screen.PosBilling when PosBilling is { } pos: preselectTypeId = pos.Type.Id; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Opens the F12 voucher-numbering configuration as a cascade column pushed to the RIGHT of the current pane
    /// (numbering S4; §5.1). Like <see cref="OpenReportConfig"/> it does NOT trim the pane it opened over, so the
    /// voucher-entry column stays live beneath and Esc/F12 pops back to it. Re-pressing while open is a no-op (the F12
    /// toggle in <see cref="F12Configure"/> pops instead). Optionally pre-selects <paramref name="preselectTypeId"/>.
    /// </summary>
    public void OpenVoucherNumberingConfig(Guid? preselectTypeId = null)
    {
        if (Company is null) return;
        if (VoucherNumberingConfig is not null) return; // already open — don't stack a second column

        var page = new VoucherNumberingConfigViewModel(Company, _storage, onSaved: BuildButtonBar);
        if (preselectTypeId is { } id) page.SelectByTypeId(id);
        VoucherNumberingConfig = page;
        Columns.Add(new GatewayColumn(page.Title, page));
        ActiveColumnIndex = Columns.Count - 1;
        CurrentScreen = Screen.VoucherNumberingConfig;
        ScreenTitle = page.Title;
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>True while the F12 voucher-numbering configuration is the live screen — the arrows then move its N1
    /// voucher-type highlight (numbering S4).</summary>
    public bool IsVoucherNumberingConfigScreen =>
        CurrentScreen == Screen.VoucherNumberingConfig && VoucherNumberingConfig is not null;

    /// <summary>
    /// 🔴 <b>The type F-keys (F4–F9) as the OPERATOR presses them — the same six verbs, with the one thing
    /// <see cref="OpenVoucher"/> must never do added in front: silently destroying unsaved keying.</b>
    ///
    /// <para><b>The defect (S5d/S5e review, C9 — MAJOR).</b> The six rows were enabled on <c>hasCompany</c> alone
    /// and the window dispatches plain F4–F9 with no screen test at all, so ONE keystroke ran
    /// <see cref="OpenVoucher"/> → <see cref="OpenPageColumn"/> → <c>ClearSubScreens</c>, which nulls
    /// <see cref="VoucherEntry"/> and <see cref="Reports"/> unconditionally. Measured on a half-keyed alteration:
    /// no prompt (<c>IsAcceptPromptOpen == false</c>), no <see cref="Notice"/>, no <c>Message</c>, the amendment
    /// gone, and the Day Book column beneath it torn down (Columns 3 → 2). Measured on a half-keyed NEW entry:
    /// the identical silent loss without the column teardown.</para>
    ///
    /// <para>🔴 <b>SCOPED TO THE SCREEN, NOT TO <c>IsAltering</c>.</b> See
    /// <see cref="VoucherEntryViewModel.HasUnsavedWork"/> for why — an <c>IsAltering</c> guard closes the smaller
    /// half of the defect and leaves a new entry destroyed by the same key.</para>
    ///
    /// <para><b>Why a REFUSAL and not a discard PROMPT.</b> The Y/N confirmation channel is
    /// <see cref="IsAcceptPromptOpen"/>, and it is scoped to master screens (<see cref="IsMasterAcceptScreen"/>
    /// excludes <see cref="Screen.VoucherEntry"/>); raising a question on a screen whose arms cannot answer it is
    /// the Alt+Y hole S1 closed. The operator already has both exits and they are named in the sentence: Esc
    /// abandons, Ctrl+A accepts. A discard prompt is a design question, not a defect fix.</para>
    ///
    /// <para><b>Why the row stays ENABLED and the guard sits in the action.</b> <c>Fire</c> skips a disabled row
    /// entirely, so dimming these six would turn a silent discard into a silent DEAD KEY — the same failure in a
    /// different costume. The action runs and puts the reason on the notice bar.</para>
    ///
    /// <para><b>Narrowness, deliberately.</b> Only the six type-key ROWS are wrapped. Direct
    /// <see cref="OpenVoucher"/> callers — the Vouchers menu, <c>OpenAddVoucherFromReport</c>, the settlement
    /// route, every test fixture — are untouched, because none of them is a keystroke aimed at a screen the
    /// operator is standing on. <b>KNOWN AND NOT CLOSED HERE:</b> the same six keys on
    /// <see cref="Screen.PosBilling"/> discard an in-progress or altering POS bill the same way; that needs a
    /// <c>HasUnsavedWork</c> on <see cref="PosBillingViewModel"/> and is reported, not smuggled into this fix.</para>
    /// </summary>
    private void OpenVoucherFromTypeKey(VoucherBaseType baseType)
    {
        if (CurrentScreen == Screen.VoucherEntry && VoucherEntry is { HasUnsavedWork: true } entry)
        {
            RaiseLifecycleNotice(entry.IsAltering
                ? "Opening another voucher type would discard this alteration and close the report beneath it. "
                + "Press Esc to abandon the alteration, or Ctrl+A to save it, and then press the key again."
                : "Opening another voucher type would discard the voucher you are keying. Press Esc to abandon "
                + "it, or Ctrl+A to accept it, and then press the key again.");
            return;
        }

        OpenVoucher(baseType);
    }

    private void BuildButtonBar()
    {
        ButtonBar.Clear();

        // The core accounting F-keys. Report/voucher shortcuts are wired where implemented.
        ButtonBar.Add(new ButtonBarItem("F1", "Help", () => Message = "Apex Solutions — accounting (Phase 1)."));
        // F2 — Date. On an entry screen this now SETS the working date (caret into the date field, keyboard-only);
        // elsewhere it reports the current date. It used to unconditionally print the never-updated FY-start.
        ButtonBar.Add(new ButtonBarItem("F2", "Date", SetWorkingDate));
        ButtonBar.Add(new ButtonBarItem("F3", "Company", ShowCompanySelect));

        var hasCompany = Company is not null;
        // F4–F9 now open the real accounting voucher-entry screens. They go through OpenVoucherFromTypeKey, NOT
        // straight to OpenVoucher, so a type key can never silently discard keying — see that method.
        ButtonBar.Add(new ButtonBarItem("F4", "Contra", () => OpenVoucherFromTypeKey(VoucherBaseType.Contra), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F5", "Payment", () => OpenVoucherFromTypeKey(VoucherBaseType.Payment), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F6", "Receipt", () => OpenVoucherFromTypeKey(VoucherBaseType.Receipt), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F7", "Journal", () => OpenVoucherFromTypeKey(VoucherBaseType.Journal), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F8", "Sales", () => OpenVoucherFromTypeKey(VoucherBaseType.Sales), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F9", "Purchase", () => OpenVoucherFromTypeKey(VoucherBaseType.Purchase), hasCompany));

        // Ctrl+L — mark the in-progress voucher Optional (only while entering a real voucher).
        var onVoucher = CurrentScreen == Screen.VoucherEntry;
        ButtonBar.Add(new ButtonBarItem("Ctrl+L", "Optional", ToggleOptional, onVoucher));
        // Ctrl+I — enter a Purchase/Sales "as invoice" (item-invoice mode); enabled only on such an entry.
        ButtonBar.Add(new ButtonBarItem("Ctrl+I", "As Invoice", ToggleItemInvoice, IsInvoiceableEntry));
        // Ctrl+H — TallyPrime's one "Change Mode" picker: the invoice modes on Purchase/Sales, Single ⟷ Double
        // Entry on Contra/Payment/Receipt (G-6). Advertised only where there is another mode to change to.
        ButtonBar.Add(new ButtonBarItem("Ctrl+H", "Change Mode", ChangeMode, IsChangeModeEntry));
        // Alt+I / Alt+A — POS payment-mode toggle + tax analysis; enabled only on the POS Billing entry (slice 7).
        var onPos = CurrentScreen == Screen.PosBilling;
        ButtonBar.Add(new ButtonBarItem("Alt+I", "Payment Mode", TogglePosPaymentMode, onPos));
        // Alt+A is context-sensitive: on Outstandings it SETTLES the selected bills (Phase 10.11 S2 / VL-4), on
        // the Day Book it ADDS a voucher (WI-12), on POS it shows tax analysis.
        // Only ONE Alt+A row is emitted — the shell's Fire()/hint lookup takes the first key match, so a second
        // Alt+A would shadow this. BRANCH ORDER MIRRORS THE KEY DISPATCHER: Outstandings first, then the Day Book,
        // then POS. Get it wrong and the Outstandings page advertises "Tax Analysis" and fires the POS handler.
        if (IsOutstandingsScreen)
            ButtonBar.Add(new ButtonBarItem("Alt+A", "Settle Bills", OpenSettlementVoucherFromOutstandings, true));
        else if (IsDayBookReport)
            ButtonBar.Add(new ButtonBarItem("Alt+A", "Add Voucher", OpenAddVoucherFromReport, true));
        else
            ButtonBar.Add(new ButtonBarItem("Alt+A", "Tax Analysis", ShowPosTaxAnalysis, onPos));

        // W2-15 (row 5.4) — Alt+2 DUPLICATE. Advertised rather than key-only: a chord nobody can find is not a
        // feature, and this file's own Data-section comment states that rule. It is ENABLED on exactly the three
        // surfaces the chord bites on (`IsVoucherAlterTargetPage` — the live report page, the register drill, the
        // voucher-detail column) and DIMMED everywhere else, because an enabled badge that fires nothing is
        // register defect IV-31. The click runs the identical door the key runs, so the two cannot drift — the
        // same rule the Alt+C row above records after key and button once did different things.
        ButtonBar.Add(new ButtonBarItem("Alt+2", "Duplicate",
            () => RequestDuplicateHighlightedVoucher(), IsVoucherAlterTargetPage));

        // Create master + report quick-jumps (enabled once a company is open).
        // WI-1: the button runs the SAME dispatch as the Alt+C key (it previously bound ShowLedgerMaster
        // directly, so on the Manufacturing-Journal / BOM screens the key created a Stock Item while the button
        // created a Ledger — key and button advertised one shortcut and did two different things). The button
        // carries no focused field, so it opens the SCREEN's default master (CreateMasterFromButton) — but
        // still through the non-destructive route, so clicking it mid-voucher no longer discards the entry.
        // DEFECT 3: it is DISABLED while a create column is already open, where Alt+C is inert by design — an
        // enabled button captioned "Create Ledger" that does nothing is worse than an honestly dimmed one.
        ButtonBar.Add(new ButtonBarItem("Alt+C", CreateMasterButtonLabel(), CreateMasterFromButton,
            hasCompany && !IsCreateOnTheFlyOpen));
        ButtonBar.Add(new ButtonBarItem("Scn", "Scenarios", ShowScenarioMaster, hasCompany));

        // W2-13a (census 14.5) — Ctrl+B BASIS OF VALUES. This is the row the Alt+A note below says was
        // deliberately absent: Ctrl+B was the Bill-Settlement badge until Phase 10.11 S2 removed the binding,
        // and the chord was left free precisely because in the reference product it is Basis of Values
        // (OutstandingsViewModel records that in its own remarks). It is now that, and nothing else.
        // ENABLED only where the open report can actually be re-scaled, and DIMMED everywhere else, because an
        // enabled badge that fires nothing is register defect IV-31 — the key arm carries the identical guard.
        ButtonBar.Add(new ButtonBarItem("Ctrl+B", "Basis of Values", OpenBasisOfValues,
            Reports is { SupportsScaleFactor: true }));
        // NOTE ON Ctrl+B (updated by W2-13a): Ctrl+B was the Bill-Settlement badge until Phase 10.11 S2 (register
        // row IV-5) removed the binding, and this note used to say there was deliberately no Ctrl+B row at all.
        // The chord now carries the verb the reference product puts on it — Basis of Values — and its row is
        // emitted ABOVE, guarded so it is only enabled where it fires. Settlement remains on the Alt+A row above
        // and is NOT reachable from Ctrl+B: the old destructive path is gone, not re-pointed.
        // "Outs" (not "O") — the bare-O key is bound to Import on the Gateway (RQ-28: a hint's letter must map
        // to the action that key actually triggers), so the Outstandings quick-button uses a non-key mnemonic
        // badge and is reached by click, never by a colliding "O" keystroke.
        ButtonBar.Add(new ButtonBarItem("Outs", "Outstandings", () => OpenOutstandings(OutstandingsKind.Receivables), hasCompany));
        ButtonBar.Add(new ButtonBarItem("BRS", "Bank Recon", OpenBankReconciliation, hasCompany));
        ButtonBar.Add(new ButtonBarItem("Imp", "Import Stmt", OpenBankStatementImport, hasCompany));
        ButtonBar.Add(new ButtonBarItem("C", "Cost Centres", () => OpenCostReport(CostReportKind.CostCentreBreakup), hasCompany));
        ButtonBar.Add(new ButtonBarItem("Int", "Interest", OpenInterestReport, hasCompany));
        ButtonBar.Add(new ButtonBarItem("SS", "Stock Summary", () => OpenReport(ReportKind.StockSummary), hasCompany));
        ButtonBar.Add(new ButtonBarItem("B", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), hasCompany));
        ButtonBar.Add(new ButtonBarItem("P", "Profit & Loss", () => OpenReport(ReportKind.ProfitAndLoss), hasCompany));
        ButtonBar.Add(new ButtonBarItem("T", "Trial Balance", () => OpenReport(ReportKind.TrialBalance), hasCompany));
        ButtonBar.Add(new ButtonBarItem("D", "Day Book", () => OpenReport(ReportKind.DayBook), hasCompany));

        // M — E-Mail (RQ-25/26): compose an offline .eml / mailto for the current report or drilled invoice.
        // Enabled on a printable page (a report, or a drilled voucher-detail); nothing is sent.
        ButtonBar.Add(new ButtonBarItem("M", "E-Mail", OpenEmailCompose, IsPrintablePage));
        // SMTP — capture the outgoing-mail server profile (RQ-27; no password, nothing sent). Company-scoped.
        ButtonBar.Add(new ButtonBarItem("SMTP", "SMTP Settings", OpenSmtpSettings, hasCompany));

        // W2-14 (census 14.1) — Alt+G GO TO. Advertised rather than key-only, for the same reason the Alt+2
        // Duplicate row above is: a chord nobody can find is not a feature. Enabled once a company is open (every
        // destination on the index is company-scoped) and dimmed otherwise, because an enabled badge that fires
        // nothing is register defect IV-31. The click runs the identical door the key runs.
        ButtonBar.Add(new ButtonBarItem("Alt+G", "Go To", OpenGoTo, hasCompany));

        // Alt+Y — Data (Backup / Restore; the R-7 carve-out). A quick door to the data-safety menu from anywhere,
        // alongside the Gateway → Data cascade. Bare Y is already Export Data on the Gateway root, so this one is
        // Alt-modified and the badge says so.
        ButtonBar.Add(new ButtonBarItem("Alt+Y", "Data", ShowDataMenu, hasCompany));

        // F11 Features → the company GST (Statutory) configuration page (slice 4c).
        ButtonBar.Add(new ButtonBarItem("F11", "Features", ShowGstConfig, hasCompany));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", F12Configure));
    }
}
