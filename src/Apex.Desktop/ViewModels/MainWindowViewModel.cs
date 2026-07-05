using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
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
        && GodownMaster is null && StockItemMaster is null;

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
        col.Add(new MenuItemViewModel("Statements of Accounts", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));
        col.Add(new MenuItemViewModel("Inventory Reports", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

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

        col.Add(MenuItemViewModel.Header("Budgets & Controls"));
        col.Add(new MenuItemViewModel("Budget", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Scenario", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        col.Add(MenuItemViewModel.Header("Multi-Currency"));
        col.Add(new MenuItemViewModel("Currency", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
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

        col.Add(MenuItemViewModel.Header("Registers"));
        col.Add(new MenuItemViewModel("Receipt Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Delivery Note Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Rejection Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Physical Stock Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Order Register", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
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
        OpenPageColumn(new GatewayColumn(reports.Title, reports), Screen.Report, reports.Title,
            () => Reports = reports);
    }

    /// <summary>
    /// The keyboard-first Stock-Summary drill (Enter on the report page): drills the highlighted/selected
    /// Stock-Summary item row into its Stock Item Movement report. A no-op unless a Stock Summary is open.
    /// </summary>
    public void DrillReport(ReportRow? row) => Reports?.Drill(row);

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
        else if (CurrentScreen is Screen.LedgerMaster or Screen.CostCategoryMaster
                 or Screen.CostCentreMaster or Screen.BudgetMaster or Screen.ScenarioMaster
                 or Screen.CurrencyMaster or Screen.StockGroupMaster or Screen.StockCategoryMaster
                 or Screen.UnitMaster or Screen.GodownMaster or Screen.StockItemMaster)
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

    /// <summary>Alt+C: open the Ledger-creation master whenever a company is open.</summary>
    public void CreateLedgerShortcut()
    {
        if (Company is not null && CurrentScreen != Screen.LedgerMaster)
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
            case Screen.BudgetMaster:
                BudgetMaster?.Create();
                return;
            case Screen.ScenarioMaster:
                ScenarioMaster?.Create();
                return;
            case Screen.CurrencyMaster:
                CurrencyMaster?.CreateCurrency();
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
            case "Budget": ShowBudgetMaster(); break;
            case "Scenario": ShowScenarioMaster(); break;
            case "Currency": ShowCurrencyMaster(); break;
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
        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = RightmostMenuKind();
        ScreenTitle = Columns[ActiveColumnIndex].Title;
        SyncActiveColumn();
        BuildButtonBar();
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

        // Create master + report quick-jumps (enabled once a company is open).
        ButtonBar.Add(new ButtonBarItem("Alt+C", "Create Ledger", ShowLedgerMaster, hasCompany));
        ButtonBar.Add(new ButtonBarItem("Scn", "Scenarios", ShowScenarioMaster, hasCompany));
        // Ctrl+B — Bill Settlement (only on the Outstandings page); elsewhere it is a disabled hint.
        ButtonBar.Add(new ButtonBarItem("Ctrl+B", "Settle Bills", SettleBills, IsOutstandingsScreen));
        ButtonBar.Add(new ButtonBarItem("O", "Outstandings", () => OpenOutstandings(OutstandingsKind.Receivables), hasCompany));
        ButtonBar.Add(new ButtonBarItem("BRS", "Bank Recon", OpenBankReconciliation, hasCompany));
        ButtonBar.Add(new ButtonBarItem("Imp", "Import Stmt", OpenBankStatementImport, hasCompany));
        ButtonBar.Add(new ButtonBarItem("C", "Cost Centres", () => OpenCostReport(CostReportKind.CostCentreBreakup), hasCompany));
        ButtonBar.Add(new ButtonBarItem("Int", "Interest", OpenInterestReport, hasCompany));
        ButtonBar.Add(new ButtonBarItem("SS", "Stock Summary", () => OpenReport(ReportKind.StockSummary), hasCompany));
        ButtonBar.Add(new ButtonBarItem("B", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), hasCompany));
        ButtonBar.Add(new ButtonBarItem("P", "Profit & Loss", () => OpenReport(ReportKind.ProfitAndLoss), hasCompany));
        ButtonBar.Add(new ButtonBarItem("T", "Trial Balance", () => OpenReport(ReportKind.TrialBalance), hasCompany));
        ButtonBar.Add(new ButtonBarItem("D", "Day Book", () => OpenReport(ReportKind.DayBook), hasCompany));

        ButtonBar.Add(new ButtonBarItem("F11", "Features", () => Message = "F11 Features — configured per company (Phase 1 defaults)."));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", () => Message = "F12 Configure — display options (Phase 1 defaults)."));
    }
}
