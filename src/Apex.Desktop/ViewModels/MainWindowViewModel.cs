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
    LedgerMaster,
    ChartOfAccounts,
    Outstandings,
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

    /// <summary>The ledger-master view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private LedgerMasterViewModel? _ledgerMaster;

    /// <summary>The chart-of-accounts tree view model, non-null only while that page column is open.</summary>
    [ObservableProperty] private ChartOfAccountsViewModel? _chartOfAccounts;

    /// <summary>The Outstandings (Receivables/Payables) view model, non-null only while that page is open.</summary>
    [ObservableProperty] private OutstandingsViewModel? _outstandings;

    /// <summary>
    /// True on the pre-company centred-menu screens (Company Select / Create Company). On the Gateway
    /// the cascade view (<see cref="IsGatewayCascade"/>) is shown instead of this centred menu.
    /// </summary>
    public bool IsMenuScreen => !IsGatewayCascade
        && Reports is null && VoucherEntry is null && LedgerMaster is null && ChartOfAccounts is null
        && Outstandings is null;

    partial void OnReportsChanged(ReportsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnVoucherEntryChanged(VoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnLedgerMasterChanged(LedgerMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnChartOfAccountsChanged(ChartOfAccountsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnOutstandingsChanged(OutstandingsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
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
        col.Add(new MenuItemViewModel("Day Book", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));

        // ---- REPORTS ----
        col.Add(MenuItemViewModel.Header("Reports"));
        col.Add(new MenuItemViewModel("Balance Sheet", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Profit & Loss A/c", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Trial Balance", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Statements of Accounts", () => { }, "▸", isSubItem: true, kind: MenuItemKind.Group));

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
        return col;
    }

    /// <summary>Builds the "Create" submenu column (Masters → Create): the master-creation entries.</summary>
    private GatewayColumn BuildCreateColumn()
    {
        var col = new GatewayColumn("Create");
        col.Add(MenuItemViewModel.Header("Create"));
        col.Add(new MenuItemViewModel("Ledger", () => { }, "Alt+C", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Group", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
    }

    /// <summary>
    /// Builds the "Statements of Accounts → Outstandings" submenu column (Reports → Statements of
    /// Accounts): the Receivables and Payables pages, each a page item under this Outstandings group.
    /// </summary>
    private GatewayColumn BuildOutstandingsColumn()
    {
        var col = new GatewayColumn("Statements of Accounts");
        col.Add(MenuItemViewModel.Header("Outstandings"));
        col.Add(new MenuItemViewModel("Receivables", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        col.Add(new MenuItemViewModel("Payables", () => { }, "", isSubItem: true, kind: MenuItemKind.Page));
        return col;
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
    /// or, when called cold (e.g. from a test/F-key before the cascade exists), as the sole page.
    /// </summary>
    public void OpenReport(ReportKind kind)
    {
        if (Company is null) return;

        var reports = new ReportsViewModel(Company, kind);
        OpenPageColumn(new GatewayColumn(reports.Title, reports), Screen.Report, reports.Title,
            () => Reports = reports);
    }

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

    /// <summary>Nulls the report/voucher/ledger/chart/outstandings page view models (mutually exclusive pages).</summary>
    private void ClearSubScreens()
    {
        Reports = null;
        VoucherEntry = null;
        LedgerMaster = null;
        ChartOfAccounts = null;
        Outstandings = null;
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
        else if (CurrentScreen == Screen.LedgerMaster)
            BackFromPage();
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

    /// <summary>Adds a bill-wise allocation row to a voucher line (the sub-panel "+ Add bill" button).</summary>
    public void AddBillAllocation(VoucherLineViewModel line)
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.AddBillAllocation(line);
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
            case Screen.LedgerMaster:
                LedgerMaster?.Create();
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
            "Create" => (BuildCreateColumn(), GatewayMenu.Create,
                "Gateway of Apex Solutions — Create"),
            "Statements of Accounts" => (BuildOutstandingsColumn(), GatewayMenu.Outstandings,
                "Gateway of Apex Solutions — Outstandings"),
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
            case "Receivables": OpenOutstandings(OutstandingsKind.Receivables); break;
            case "Payables": OpenOutstandings(OutstandingsKind.Payables); break;
            case "Contra": OpenVoucher(VoucherBaseType.Contra); break;
            case "Payment": OpenVoucher(VoucherBaseType.Payment); break;
            case "Receipt": OpenVoucher(VoucherBaseType.Receipt); break;
            case "Journal": OpenVoucher(VoucherBaseType.Journal); break;
            case "Sales": OpenVoucher(VoucherBaseType.Sales); break;
            case "Purchase": OpenVoucher(VoucherBaseType.Purchase); break;
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
                    "Create" => GatewayMenu.Create,
                    "Statements of Accounts" => GatewayMenu.Outstandings,
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

        // Create master + report quick-jumps (enabled once a company is open).
        ButtonBar.Add(new ButtonBarItem("Alt+C", "Create Ledger", ShowLedgerMaster, hasCompany));
        // Ctrl+B — Bill Settlement (only on the Outstandings page); elsewhere it is a disabled hint.
        ButtonBar.Add(new ButtonBarItem("Ctrl+B", "Settle Bills", SettleBills, IsOutstandingsScreen));
        ButtonBar.Add(new ButtonBarItem("O", "Outstandings", () => OpenOutstandings(OutstandingsKind.Receivables), hasCompany));
        ButtonBar.Add(new ButtonBarItem("B", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), hasCompany));
        ButtonBar.Add(new ButtonBarItem("P", "Profit & Loss", () => OpenReport(ReportKind.ProfitAndLoss), hasCompany));
        ButtonBar.Add(new ButtonBarItem("T", "Trial Balance", () => OpenReport(ReportKind.TrialBalance), hasCompany));
        ButtonBar.Add(new ButtonBarItem("D", "Day Book", () => OpenReport(ReportKind.DayBook), hasCompany));

        ButtonBar.Add(new ButtonBarItem("F11", "Features", () => Message = "F11 Features — configured per company (Phase 1 defaults)."));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", () => Message = "F12 Configure — display options (Phase 1 defaults)."));
    }
}
