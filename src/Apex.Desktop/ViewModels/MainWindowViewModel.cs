using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>Which screen the single window is currently showing.</summary>
public enum Screen
{
    CompanySelect,
    CreateCompany,
    Gateway,
    Report,
    ReportConfig,
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
    EmailCompose,
    SmtpSettings,
    VoucherEntry,
    InventoryVoucherEntry,
    LedgerMaster,
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

    // Payroll statutory report (Phase 8 slice 4; RQ-9) — PF ECR / Challan, gated on Payroll Statutory.
    PfEcrReport,

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

    // Reports → Statutory Reports (Phase 7 slice 8): the TDS/TCS exception & outstanding reports, nested under
    // TDS Reports / TCS Reports sub-groups (+ a common Ledgers-without-PAN report spanning both taxes).
    StatutoryReports,
    TdsReports,
    TcsReports,

    // Reports → Statutory Reports → Payroll (PF) (Phase 8 slice 4): the PF ECR / Challan report, nested under a
    // Payroll sub-group only when Payroll Statutory is enabled.
    PayrollStatutoryReports,
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

    /// <summary>The company GST-configuration (F11 Features → GST) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private GstConfigViewModel? _gstConfig;

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

    /// <summary>The F12 report-Configuration panel view model, non-null only while that config column is open (RQ-6).</summary>
    [ObservableProperty] private ReportConfigViewModel? _reportConfig;

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
        && GstConfig is null && NatureOfPaymentMaster is null && NatureOfGoodsMaster is null
        && TdsStatPayment is null && ChallanReconciliation is null && Form26Q is null
        && TcsStatPayment is null && TcsChallanReconciliation is null && Form27EQ is null
        && Form16A is null && Form27D is null && Form27A is null
        && ReportConfig is null
        && ReportSortFilter is null && AddComparisonColumn is null && AutoColumns is null
        && SaveView is null && SavedViews is null && PrintPreview is null && PrintConfigPanel is null
        && ExportPanel is null && ExportDataPanel is null && ImportDataPanel is null
        && EmailCompose is null && SmtpSettings is null
        && LedgerVouchers is null && VoucherDetail is null;

    partial void OnReportsChanged(ReportsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnVoucherEntryChanged(VoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnInventoryVoucherEntryChanged(InventoryVoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnLedgerMasterChanged(LedgerMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
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

    /// <summary>Index of the focused (active) column in the cascade.</summary>
    public int ActiveColumnIndex { get; private set; }

    public MainWindowViewModel() : this(new CompanyStorage()) { }

    public MainWindowViewModel(CompanyStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
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

    private void ShowCreateCompany()
    {
        CurrentScreen = Screen.CreateCompany;
        ScreenTitle = "Company Creation";
        NewCompanyName = string.Empty;
        Message = "Enter the company name, then press Enter (Ctrl+A) to create.";
        LeaveCascade();
        Menu.Clear();
        BuildButtonBar();
    }

    /// <summary>Creates a fresh seeded company, saves it, and opens it. No-op on a blank name.</summary>
    public void CreateCompany()
    {
        var name = (NewCompanyName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A company name is required.";
            return;
        }

        var company = Apex.Ledger.Services.CompanyFactory.CreateSeeded(name);
        _storage.Save(company);
        OpenCompany(company);
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
        StatusDate = company.FinancialYearStart.ToString("dd-MMM-yyyy");
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
        Columns.Add(BuildRootColumn());
        ActiveColumnIndex = 0;
        Columns[0].SelectFirstSelectable();
        SyncActiveColumn();
        BuildButtonBar();
    }

    /// <summary>Builds the root Gateway menu column (the three sections and their items).</summary>
    private GatewayColumn BuildRootColumn()
    {
        var col = new GatewayColumn("Gateway of Apex Solutions");

        // ---- MASTERS ----
        col.Add(MenuItemViewModel.Header("Masters"));
        col.Add(new MenuItemViewModel("Create", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Chart of Accounts", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // ---- STATUTORY (GST) ----
        col.Add(MenuItemViewModel.Header("Statutory"));
        col.Add(new MenuItemViewModel("GST", () => { }, "F11", isSubItem: true, kind: MenuItemKind.Page));

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

        // Statutory Reports (Phase 7 slice 8; catalog §13) — the TDS/TCS exception & outstanding reports and, from
        // Phase 8 slice 4, the Payroll (PF) statutory reports. Surfaced only when the F11 feature enables TDS, TCS
        // or Payroll Statutory (ER-13), so a company using none is byte-identical to the pre-slice Reports menu.
        if (Company is { TdsEnabled: true } or { TcsEnabled: true } or { PayrollStatutoryEnabled: true })
            col.Add(new MenuItemViewModel("Statutory Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

        // ---- top-level action: change company ----
        col.Add(new MenuItemViewModel("Quit — Change Company", ShowCompanySelect, "F3", kind: MenuItemKind.Action));

        return col;
    }

    /// <summary>
    /// Builds the "Vouchers" submenu column (Transactions → Vouchers): the six accounting voucher
    /// types, each a page item under its F-key.
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
    /// <b>Physical Stock</b> (F10 menu) — each a page item. They move stock only (no accounting entry, DP-5).
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
        col.Add(new MenuItemViewModel("Physical Stock", () => { }, "F10", isSubItem: true, kind: MenuItemKind.Page));
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
            col.Add(new MenuItemViewModel("Form 26Q", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Form 16A", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Form 27A (TDS)", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        }

        // TCS Challan Reconciliation + Form 27EQ (Phase 7 slice 6; catalog §13) — the collector's mirror of the TDS
        // pair. Surfaced under their own TCS header only when the F11 feature "Enable TCS" is on (ER-13). No global
        // open accelerator (Alt+R stays the TDS recon even when both taxes are on — no colliding/dead key).
        if (Company is { TcsEnabled: true })
        {
            col.Add(MenuItemViewModel.Header("TCS"));
            col.Add(new MenuItemViewModel("TCS Challan Reconciliation", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Form 27EQ", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
            col.Add(new MenuItemViewModel("Form 27D", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
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
    /// per-voucher registers (Sales / Purchase / … registers) reuse the Day Book filtered by voucher type and
    /// are surfaced elsewhere; they are noted for a later slice.
    /// </summary>
    private GatewayColumn BuildAccountBooksColumn()
    {
        var col = new GatewayColumn("Account Books");
        col.Add(MenuItemViewModel.Header("Account Books"));
        col.Add(new MenuItemViewModel("Cash Book", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Bank Book", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Ledger", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        return col;
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
        var col = new GatewayColumn(title);
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
        // Payroll (PF) statutory reports (Phase 8 slice 4; RQ-9) nest under their own Payroll sub-group, surfaced
        // only when the F11 feature "Enable Payroll Statutory" is on (ER-13).
        if (Company is { PayrollStatutoryEnabled: true })
            col.Add(new MenuItemViewModel("Payroll (PF)", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        // R9 Ledgers/Parties without PAN spans both taxes, so it sits at the Statutory-Reports level — but only
        // when a tax is on (a payroll-only company that never enabled TDS/TCS has no PAN report to show).
        if (Company is { TdsEnabled: true } or { TcsEnabled: true })
            col.Add(new MenuItemViewModel("Ledgers without PAN", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>Builds the "Payroll (PF)" submenu column (Reports → Statutory Reports → Payroll (PF); Phase 8 slice
    /// 4; RQ-9): the PF ECR / Challan report page (member-wise ECR 2.0 + the A/c 1/2/10/21/22 challan totals).</summary>
    private GatewayColumn BuildPayrollStatutoryReportsColumn()
    {
        var col = new GatewayColumn("Payroll (PF)");
        col.Add(MenuItemViewModel.Header("Provident Fund"));
        col.Add(new MenuItemViewModel("PF ECR / Challan", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
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

    /// <summary>Opens the "Reports → Statutory Reports → Payroll (PF)" submenu column directly (Phase 8 slice 4).</summary>
    public void ShowPayrollStatutoryReportsMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }
        ShowStatutoryReportsMenu();
        SelectSubmenuItem("Payroll (PF)");
        OpenSubmenuColumn(BuildPayrollStatutoryReportsColumn(), GatewayMenu.PayrollStatutoryReports,
            "Gateway of Apex Solutions — Payroll (PF)");
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
        if (kind == ReportKind.StockSummary)
            reports.DrillToMovementRequested += id => OpenReport(ReportKind.StockItemMovement, id);
        // RQ-7 universal drill-down: a TB/BS/P&L ledger row opens that ledger's vouchers as a NEW cascading
        // column (the report pane persists); a Day Book row opens the voucher's read-only detail.
        reports.DrillToLedgerRequested += (ledgerId, from, to, movement) => OpenLedgerVouchers(ledgerId, from, to, movement);
        reports.DrillToVoucherRequested += OpenVoucherDetail;
        OpenPageColumn(new GatewayColumn(reports.Title, reports), Screen.Report, reports.Title,
            () => Reports = reports);
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
        if (PrintPreview is not { SupportsPrintConfig: true } preview) return; // only over a voucher/invoice preview
        if (PrintConfigPanel is not null) return;                              // panel already open — don't stack

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

    // =============================================================== screen: voucher entry

    /// <summary>
    /// Opens the reusable voucher-entry screen for the given base type as a page column on the right of
    /// the cascade, resolving the seeded voucher type on the current company.
    /// </summary>
    public void OpenVoucher(VoucherBaseType baseType)
    {
        if (Company is null) return;

        var type = Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType && t.IsActive)
                   ?? Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType);
        if (type is null)
        {
            Message = $"No '{baseType}' voucher type is configured for this company.";
            return;
        }

        var entry = new VoucherEntryViewModel(
            Company, type, _storage,
            onSaved: ShowGateway,
            onCancelled: BackFromPage);
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
    public void OpenInventoryVoucher(VoucherBaseType baseType)
    {
        if (Company is null) return;

        if (!VoucherEffects.IsInventoryBaseType(baseType))
        {
            Message = $"'{baseType}' is not a stock or order voucher.";
            return;
        }

        var type = Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType && t.IsActive)
                   ?? Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType);
        if (type is null)
        {
            Message = $"No '{baseType}' voucher type is configured for this company.";
            return;
        }

        var entry = new InventoryVoucherEntryViewModel(
            Company, type, _storage,
            onSaved: ShowGateway,
            onCancelled: BackFromPage);
        // RQ-3: a batch-tracked line opens the batch-allocation sub-screen as a cascade column to the right.
        entry.BatchAllocationRequested += (item, godown, qty, isOutward, onCommitted) =>
            ShowBatchAllocation(item, godown, qty, isOutward, onCommitted);
        var title = $"Inventory Voucher Creation — {type.Name}";
        OpenPageColumn(new GatewayColumn(type.Name + " Voucher", entry), Screen.InventoryVoucherEntry, title,
            () => InventoryVoucherEntry = entry);
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

    // =============================================================== screen: chart of accounts

    /// <summary>
    /// Opens the read-only Chart of Accounts (Masters → Chart of Accounts) as a page column: the group
    /// hierarchy with sub-groups nested/indented under their primary parent and ledgers under their group.
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
        var type = Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType && t.IsActive)
                   ?? Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType);
        if (type is null)
        {
            Message = $"No '{baseType}' voucher type is configured for this company.";
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

        var type = Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType && t.IsActive)
                   ?? Company.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType);
        if (type is null)
        {
            Message = $"No '{baseType}' voucher type is configured for this company.";
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
        OpenPageColumn(new GatewayColumn("Form 26Q", page), Screen.Form26Q,
            "Form 26Q (Quarterly TDS Return)", () => Form26Q = page);
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
        OpenPageColumn(new GatewayColumn("Form 27EQ", page), Screen.Form27EQ,
            "Form 27EQ (Quarterly TCS Return)", () => Form27EQ = page);
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
        OpenPageColumn(new GatewayColumn("Form 16A", page), Screen.Form16A,
            "Form 16A (TDS Certificate)", () => Form16A = page);
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
        OpenPageColumn(new GatewayColumn("Form 27D", page), Screen.Form27D,
            "Form 27D (TCS Certificate)", () => Form27D = page);
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
    /// ageing. Spacebar multi-select + Ctrl+B settle bills through the engine, then the report refreshes.
    /// </summary>
    public void OpenOutstandings(OutstandingsKind kind)
    {
        if (Company is null) return;

        var vm = new OutstandingsViewModel(Company, _storage, kind, onChanged: () => { });
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

    /// <summary>Removes every column after <paramref name="index"/> (keeps [0..index]).</summary>
    private void TrimColumnsAfter(int index)
    {
        for (var i = Columns.Count - 1; i > index; i--)
            Columns.RemoveAt(i);
    }

    /// <summary>Nulls every page view model (they are mutually exclusive — at most one page column open).</summary>
    private void ClearSubScreens()
    {
        Reports = null;
        VoucherEntry = null;
        InventoryVoucherEntry = null;
        LedgerMaster = null;
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
        GstConfig = null;
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
        ReportConfig = null;
        ReportSortFilter = null;
        AddComparisonColumn = null;
        AutoColumns = null;
        SaveView = null;
        SavedViews = null;
        PrintPreview = null;
        PrintConfigPanel = null;
        ExportPanel = null;
        ExportDataPanel = null;
        ImportDataPanel = null;
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
        IsGatewayCascade = false;
    }

    // =============================================================== form key helpers

    /// <summary>Ctrl+A on a form page: accept the current voucher / create the current ledger.</summary>
    public void AcceptCurrent() => ActivateSelected();

    /// <summary>Alt+X: cancel the in-progress voucher (no save) and pop its page column.</summary>
    public void CancelVoucher()
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
        else if (CurrentScreen is Screen.LedgerMaster or Screen.CostCategoryMaster
                 or Screen.CostCentreMaster or Screen.BudgetMaster or Screen.ScenarioMaster
                 or Screen.CurrencyMaster or Screen.StockGroupMaster or Screen.StockCategoryMaster
                 or Screen.UnitMaster or Screen.GodownMaster or Screen.StockItemMaster
                 or Screen.BatchMaster or Screen.BatchAllocation
                 or Screen.BomMaster or Screen.ReorderLevelsMaster
                 or Screen.GstConfig
                 or Screen.NatureOfPaymentMaster or Screen.NatureOfGoodsMaster
                 or Screen.TdsStatPayment or Screen.TcsStatPayment
                 or Screen.EmployeeCategoryMaster or Screen.EmployeeGroupMaster or Screen.EmployeeMaster
                 or Screen.PayrollUnitMaster or Screen.AttendanceTypeMaster
                 or Screen.PayHeadMaster or Screen.SalaryStructureMaster)
            BackFromPage();
    }

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

    /// <summary>True while a Purchase/Sales voucher-entry page is active (drives the Ctrl+I item-invoice action).</summary>
    public bool IsInvoiceableEntry =>
        CurrentScreen == Screen.VoucherEntry && VoucherEntry?.CanBeItemInvoice == true;

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

        var target = Company.VoucherTypes.FirstOrDefault(t => t.BaseType == targetBaseType && t.IsActive)
                     ?? Company.VoucherTypes.FirstOrDefault(t => t.BaseType == targetBaseType);
        if (target is null)
        {
            Message = $"No '{targetBaseType}' voucher type is configured to convert into.";
            return null;
        }

        try
        {
            var service = new Apex.Ledger.Services.LedgerService(Company);
            var regular = service.ConvertToRegular(memorandumVoucherId, target.Id);
            _storage.Save(Company);
            Message = $"Memorandum converted to {target.Name} No. {regular.Number}.";
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
    /// </summary>
    public void CreateLedgerShortcut()
    {
        if (Company is null) return;
        if (CurrentScreen is Screen.ManufacturingJournalEntry or Screen.BomMaster)
        {
            ShowStockItemMaster();
            return;
        }
        if (CurrentScreen != Screen.LedgerMaster)
            ShowLedgerMaster();
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

    /// <summary>Spacebar on the Outstandings page: toggle the highlighted bill's multi-select flag.</summary>
    public void ToggleOutstandingSelection()
    {
        if (IsOutstandingsScreen) Outstandings!.ToggleSelectHighlighted();
    }

    /// <summary>Ctrl+B on the Outstandings page: settle (knock off) the selected bills via the engine.</summary>
    public void SettleBills()
    {
        if (IsOutstandingsScreen) Outstandings!.SettleSelected();
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
        // On the Outstandings page the arrows move the bill-row highlight (for spacebar select + Ctrl+B).
        if (IsOutstandingsScreen)
        {
            Outstandings!.MoveHighlight(direction);
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
        switch (CurrentScreen)
        {
            case Screen.CreateCompany:
                CreateCompany();
                return;
            case Screen.VoucherEntry:
                VoucherEntry?.Accept();
                return;
            case Screen.InventoryVoucherEntry:
                InventoryVoucherEntry?.Accept();
                return;
            case Screen.LedgerMaster:
                LedgerMaster?.Create();
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
            case Screen.StockItemMaster:
                StockItemMaster?.Create();
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
                PosBilling?.Accept();
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
            "Exception Reports" => (BuildExceptionReportsColumn(), GatewayMenu.ExceptionReports,
                "Gateway of Apex Solutions — Exception Reports"),
            "Statutory Reports" => (BuildStatutoryReportsColumn(), GatewayMenu.StatutoryReports,
                "Gateway of Apex Solutions — Statutory Reports"),
            "TDS Reports" => (BuildTdsReportsColumn(), GatewayMenu.TdsReports,
                "Gateway of Apex Solutions — TDS Reports"),
            "TCS Reports" => (BuildTcsReportsColumn(), GatewayMenu.TcsReports,
                "Gateway of Apex Solutions — TCS Reports"),
            "Payroll (PF)" => (BuildPayrollStatutoryReportsColumn(), GatewayMenu.PayrollStatutoryReports,
                "Gateway of Apex Solutions — Payroll (PF)"),
            "Outstandings" => (BuildOutstandingsColumn(), GatewayMenu.Outstandings,
                "Gateway of Apex Solutions — Outstandings"),
            "Cost Centres" => (BuildCostCentresColumn(), GatewayMenu.CostCentres,
                "Gateway of Apex Solutions — Cost Centres"),
            "Budgets" => (BuildBudgetsColumn(), GatewayMenu.Budgets,
                "Gateway of Apex Solutions — Budgets"),
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

        switch (item.Label)
        {
            case "Chart of Accounts": ShowChartOfAccounts(); break;
            case "Day Book": OpenReport(ReportKind.DayBook); break;
            case "Balance Sheet": OpenReport(ReportKind.BalanceSheet); break;
            case "Profit & Loss A/c": OpenReport(ReportKind.ProfitAndLoss); break;
            case "Trial Balance": OpenReport(ReportKind.TrialBalance); break;
            case "Ledger": ShowLedgerMaster(); break;
            case "Group": ShowLedgerMaster(); break;
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
            case "GST": ShowGstConfig(); break;
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
            // Payroll vouchers (Phase 8 slice 3) — under Transactions → Vouchers → Payroll, gated by F11 Maintain Payroll.
            case "Attendance / Production": ShowAttendanceVoucher(); break;
            case "Payroll": ShowPayrollVoucher(); break;
            case "TDS Stat Payment": ShowTdsStatPayment(); break;
            case "Challan Reconciliation": OpenChallanReconciliation(); break;
            case "Form 26Q": OpenForm26Q(); break;
            case "TCS Stat Payment": ShowTcsStatPayment(); break;
            case "TCS Challan Reconciliation": OpenTcsChallanReconciliation(); break;
            case "Form 27EQ": OpenForm27EQ(); break;
            case "Form 16A": OpenForm16A(); break;
            case "Form 27D": OpenForm27D(); break;
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
            // PF statutory report (Phase 8 slice 4) — under Reports → Statutory Reports → Payroll (PF).
            case "PF ECR / Challan": OpenPfEcrReport(); break;
            case "Bank Reconciliation": OpenBankReconciliation(); break;
            case "Import Bank Statement": OpenBankStatementImport(); break;
            case "Contra": OpenVoucher(VoucherBaseType.Contra); break;
            case "Payment": OpenVoucher(VoucherBaseType.Payment); break;
            case "Receipt": OpenVoucher(VoucherBaseType.Receipt); break;
            case "Journal": OpenVoucher(VoucherBaseType.Journal); break;
            case "Sales": OpenVoucher(VoucherBaseType.Sales); break;
            case "Purchase": OpenVoucher(VoucherBaseType.Purchase); break;
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
        var col = Columns[ActiveColumnIndex];
        switch (col.Page)
        {
            case ReportsViewModel r:
                Reports = r;
                CurrentScreen = Screen.Report;
                break;
            // RQ-7 drill columns can sit beneath one another (report → ledger-vouchers → voucher-detail); when a
            // deeper drill column is popped the surviving one must be re-bound so it stays live. A report may also
            // survive beneath a just-popped ledger-vouchers/voucher-detail column.
            case LedgerVouchersViewModel lv:
                LedgerVouchers = lv;
                CurrentScreen = Screen.LedgerVouchers;
                break;
            case VoucherDetailViewModel vd:
                VoucherDetail = vd;
                CurrentScreen = Screen.VoucherDetail;
                break;
            // A print-preview column survives beneath a just-popped F12 print-config panel (RQ-12), so re-bind it.
            case PrintPreviewViewModel pv:
                PrintPreview = pv;
                CurrentScreen = Screen.PrintPreview;
                break;
            default:
                CurrentScreen = Screen.Gateway;
                break;
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
                    "Payroll (PF)" => GatewayMenu.PayrollStatutoryReports,
                    "Account Books" => GatewayMenu.AccountBooks,
                    "Cash Book" => GatewayMenu.CashBook,
                    "Bank Book" => GatewayMenu.BankBook,
                    "Ledger" => GatewayMenu.LedgerBooks,
                    "Outstandings" => GatewayMenu.Outstandings,
                    "Cost Centres" => GatewayMenu.CostCentres,
                    "Budgets" => GatewayMenu.Budgets,
                    _ => GatewayMenu.Root,
                };
        return GatewayMenu.Root;
    }

    /// <summary>The currently focused column, or null.</summary>
    private GatewayColumn? ActiveColumn =>
        ActiveColumnIndex >= 0 && ActiveColumnIndex < Columns.Count ? Columns[ActiveColumnIndex] : null;

    /// <summary>
    /// Repaints the cascade after a focus/selection change: marks the active column active (bright
    /// highlight) and the rest inactive (dim), and mirrors the active menu column into <see cref="Menu"/>
    /// / <see cref="SelectedIndex"/> so the keyboard driver and headless tests see the focused list.
    /// </summary>
    private void SyncActiveColumn()
    {
        for (var i = 0; i < Columns.Count; i++)
            Columns[i].SetActive(i == ActiveColumnIndex);

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
        if (CurrentScreen == Screen.LedgerMaster && LedgerMaster is { } lm)
        {
            lm.ToggleConfiguration();
            return;
        }
        Message = "F12 Configure — display options (Phase 1 defaults).";
    }

    private void BuildButtonBar()
    {
        ButtonBar.Clear();

        // The core accounting F-keys. Report/voucher shortcuts are wired where implemented.
        ButtonBar.Add(new ButtonBarItem("F1", "Help", () => Message = "Apex Solutions — accounting (Phase 1)."));
        ButtonBar.Add(new ButtonBarItem("F2", "Date", () => Message = StatusDate));
        ButtonBar.Add(new ButtonBarItem("F3", "Company", ShowCompanySelect));

        var hasCompany = Company is not null;
        // F4–F9 now open the real accounting voucher-entry screens.
        ButtonBar.Add(new ButtonBarItem("F4", "Contra", () => OpenVoucher(VoucherBaseType.Contra), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F5", "Payment", () => OpenVoucher(VoucherBaseType.Payment), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F6", "Receipt", () => OpenVoucher(VoucherBaseType.Receipt), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F7", "Journal", () => OpenVoucher(VoucherBaseType.Journal), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F8", "Sales", () => OpenVoucher(VoucherBaseType.Sales), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F9", "Purchase", () => OpenVoucher(VoucherBaseType.Purchase), hasCompany));

        // Ctrl+L — mark the in-progress voucher Optional (only while entering a real voucher).
        var onVoucher = CurrentScreen == Screen.VoucherEntry;
        ButtonBar.Add(new ButtonBarItem("Ctrl+L", "Optional", ToggleOptional, onVoucher));
        // Ctrl+I — enter a Purchase/Sales "as invoice" (item-invoice mode); enabled only on such an entry.
        ButtonBar.Add(new ButtonBarItem("Ctrl+I", "As Invoice", ToggleItemInvoice, IsInvoiceableEntry));
        // Alt+I / Alt+A — POS payment-mode toggle + tax analysis; enabled only on the POS Billing entry (slice 7).
        var onPos = CurrentScreen == Screen.PosBilling;
        ButtonBar.Add(new ButtonBarItem("Alt+I", "Payment Mode", TogglePosPaymentMode, onPos));
        ButtonBar.Add(new ButtonBarItem("Alt+A", "Tax Analysis", ShowPosTaxAnalysis, onPos));

        // Create master + report quick-jumps (enabled once a company is open).
        ButtonBar.Add(new ButtonBarItem("Alt+C", "Create Ledger", ShowLedgerMaster, hasCompany));
        ButtonBar.Add(new ButtonBarItem("Scn", "Scenarios", ShowScenarioMaster, hasCompany));
        // Ctrl+B — Bill Settlement (only on the Outstandings page); elsewhere it is a disabled hint.
        ButtonBar.Add(new ButtonBarItem("Ctrl+B", "Settle Bills", SettleBills, IsOutstandingsScreen));
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

        // F11 Features → the company GST (Statutory) configuration page (slice 4c).
        ButtonBar.Add(new ButtonBarItem("F11", "Features", ShowGstConfig, hasCompany));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", F12Configure));
    }
}
