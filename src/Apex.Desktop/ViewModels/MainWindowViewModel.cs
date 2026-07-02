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
}

/// <summary>
/// Which menu the Gateway state machine is currently showing. The Gateway is the root; the
/// <c>Vouchers</c> and <c>Create</c> submenus push on top of it (Esc pops back to the Gateway).
/// </summary>
public enum GatewayMenu
{
    Root,
    Vouchers,
    Create,
}

/// <summary>
/// The single-window shell view model — the Gateway-of-Apex-Solutions state machine. Owns the current
/// screen, the keyboard-navigable menu, the open company, the reports view model, and the
/// right-hand F-key button bar. Kept UI-toolkit-free so it is unit-testable headlessly: a test
/// can drive "Load Robert Demo → Balance Sheet" purely through this class (design §9 spirit).
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

    /// <summary>The current menu (company list, gateway, or report list) driven by arrow keys.</summary>
    public ObservableCollection<MenuItemViewModel> Menu { get; } = new();

    /// <summary>The right-hand vertical button bar for the current screen.</summary>
    public ObservableCollection<ButtonBarItem> ButtonBar { get; } = new();

    /// <summary>The reports view model, non-null only while a report is showing.</summary>
    [ObservableProperty] private ReportsViewModel? _reports;

    /// <summary>The voucher-entry view model, non-null only while a voucher is being entered.</summary>
    [ObservableProperty] private VoucherEntryViewModel? _voucherEntry;

    /// <summary>The ledger-master view model, non-null only while that screen is showing.</summary>
    [ObservableProperty] private LedgerMasterViewModel? _ledgerMaster;

    /// <summary>The chart-of-accounts tree view model, non-null only while that screen is showing.</summary>
    [ObservableProperty] private ChartOfAccountsViewModel? _chartOfAccounts;

    /// <summary>True on the menu-driven screens (company select, gateway, create company).</summary>
    public bool IsMenuScreen =>
        Reports is null && VoucherEntry is null && LedgerMaster is null && ChartOfAccounts is null;

    partial void OnReportsChanged(ReportsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnVoucherEntryChanged(VoucherEntryViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnLedgerMasterChanged(LedgerMasterViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));
    partial void OnChartOfAccountsChanged(ChartOfAccountsViewModel? value) => OnPropertyChanged(nameof(IsMenuScreen));

    /// <summary>Which Gateway menu is showing (Root / Vouchers / Create) — for Esc step-back.</summary>
    public GatewayMenu CurrentGatewayMenu { get; private set; } = GatewayMenu.Root;

    /// <summary>The currently open company (null before one is selected/created).</summary>
    public Company? Company { get; private set; }

    /// <summary>Index of the highlighted menu item.</summary>
    private int _selectedIndex;

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
        Menu.Clear();

        foreach (var entry in _storage.ListCompanies())
        {
            var captured = entry;
            Menu.Add(new MenuItemViewModel(captured.Name, () => OpenExisting(captured), "Open"));
        }

        Menu.Add(new MenuItemViewModel("Create Company", ShowCreateCompany, "F3"));
        Menu.Add(new MenuItemViewModel("Load Robert Demo", LoadRobertDemo, "Demo"));

        SetSelected(0);
        BuildButtonBar();
    }

    private void ShowCreateCompany()
    {
        CurrentScreen = Screen.CreateCompany;
        ScreenTitle = "Company Creation";
        NewCompanyName = string.Empty;
        Message = "Enter the company name, then press Enter (Ctrl+A) to create.";
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

    // =============================================================== screen: gateway

    private void OpenCompany(Company company)
    {
        Company = company;
        StatusCompany = company.Name;
        StatusDate = company.FinancialYearStart.ToString("dd-MMM-yyyy");
        ShowGateway();
    }

    /// <summary>
    /// Shows the root Gateway of Apex Solutions menu for the open company — organised into three
    /// non-selectable section headers (MASTERS / TRANSACTIONS / REPORTS) with the selectable items
    /// nested under each. "Vouchers" and "Create" open their own submenus; the rest jump straight
    /// to a screen. Arrow keys skip the headers; Enter only fires on the items.
    /// </summary>
    public void ShowGateway()
    {
        if (Company is null) { ShowCompanySelect(); return; }

        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = GatewayMenu.Root;
        ScreenTitle = "Gateway of Apex Solutions";
        Message = null;
        ClearSubScreens();
        Menu.Clear();

        // ---- MASTERS ----
        Menu.Add(MenuItemViewModel.Header("Masters"));
        Menu.Add(new MenuItemViewModel("Create", ShowCreateMenu, "▸", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Chart of Accounts", ShowChartOfAccounts, "", isSubItem: true));

        // ---- TRANSACTIONS ----
        Menu.Add(MenuItemViewModel.Header("Transactions"));
        Menu.Add(new MenuItemViewModel("Vouchers", ShowVouchersMenu, "F4–F9  ▸", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Day Book", () => OpenReport(ReportKind.DayBook), "", isSubItem: true));

        // ---- REPORTS ----
        Menu.Add(MenuItemViewModel.Header("Reports"));
        Menu.Add(new MenuItemViewModel("Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), "", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Profit & Loss A/c", () => OpenReport(ReportKind.ProfitAndLoss), "", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Trial Balance", () => OpenReport(ReportKind.TrialBalance), "", isSubItem: true));

        // ---- top-level action: change company ----
        Menu.Add(new MenuItemViewModel("Quit — Change Company", ShowCompanySelect, "F3"));

        SelectFirstSelectable();
        BuildButtonBar();
    }

    /// <summary>
    /// The "Vouchers" submenu (Transactions → Vouchers): the six accounting voucher types, each
    /// under its F-key. Selecting one opens the VoucherEntry screen for that base type. Esc pops
    /// back to the root Gateway.
    /// </summary>
    public void ShowVouchersMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }

        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = GatewayMenu.Vouchers;
        ScreenTitle = "Gateway of Apex Solutions — Vouchers";
        Message = null;
        ClearSubScreens();
        Menu.Clear();

        Menu.Add(MenuItemViewModel.Header("Vouchers"));
        Menu.Add(new MenuItemViewModel("Contra", () => OpenVoucher(VoucherBaseType.Contra), "F4", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Payment", () => OpenVoucher(VoucherBaseType.Payment), "F5", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Receipt", () => OpenVoucher(VoucherBaseType.Receipt), "F6", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Journal", () => OpenVoucher(VoucherBaseType.Journal), "F7", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Sales", () => OpenVoucher(VoucherBaseType.Sales), "F8", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Purchase", () => OpenVoucher(VoucherBaseType.Purchase), "F9", isSubItem: true));

        SelectFirstSelectable();
        BuildButtonBar();
    }

    /// <summary>
    /// The "Create" submenu (Masters → Create): the master-creation entries. Ledger opens the
    /// Ledger-creation master; Group is offered for parity but reuses the same master screen
    /// (a lightweight, read-only chart tree already shows the group hierarchy). Esc pops back.
    /// </summary>
    public void ShowCreateMenu()
    {
        if (Company is null) { ShowCompanySelect(); return; }

        CurrentScreen = Screen.Gateway;
        CurrentGatewayMenu = GatewayMenu.Create;
        ScreenTitle = "Gateway of Apex Solutions — Create";
        Message = null;
        ClearSubScreens();
        Menu.Clear();

        Menu.Add(MenuItemViewModel.Header("Create"));
        Menu.Add(new MenuItemViewModel("Ledger", ShowLedgerMaster, "Alt+C", isSubItem: true));
        Menu.Add(new MenuItemViewModel("Group", ShowLedgerMaster, "", isSubItem: true));

        SelectFirstSelectable();
        BuildButtonBar();
    }

    // =============================================================== screen: report

    /// <summary>Opens a report for the current company.</summary>
    public void OpenReport(ReportKind kind)
    {
        if (Company is null) return;

        CurrentScreen = Screen.Report;
        ClearSubScreens();
        Reports = new ReportsViewModel(Company, kind);
        ScreenTitle = Reports.Title;
        Menu.Clear();
        Message = null;
        BuildButtonBar();
    }

    // =============================================================== screen: voucher entry

    /// <summary>
    /// Opens the reusable voucher-entry screen for the given base type (Contra/Payment/Receipt/
    /// Journal/Sales/Purchase), resolving the seeded voucher type on the current company.
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

        CurrentScreen = Screen.VoucherEntry;
        ClearSubScreens();
        VoucherEntry = new VoucherEntryViewModel(
            Company, type, _storage,
            onSaved: ShowGateway,
            onCancelled: ShowGateway);
        ScreenTitle = $"Accounting Voucher Creation — {type.Name}";
        Menu.Clear();
        Message = null;
        BuildButtonBar();
    }

    // =============================================================== screen: ledger master

    /// <summary>Opens the Ledger-creation master (Create → Ledger / Alt+C).</summary>
    public void ShowLedgerMaster()
    {
        if (Company is null) return;

        CurrentScreen = Screen.LedgerMaster;
        ClearSubScreens();
        LedgerMaster = new LedgerMasterViewModel(Company, _storage, onChanged: () => { });
        ScreenTitle = "Ledger Creation";
        Menu.Clear();
        Message = null;
        BuildButtonBar();
    }

    // =============================================================== screen: chart of accounts

    /// <summary>
    /// Opens the read-only Chart of Accounts (Masters → Chart of Accounts): the group hierarchy with
    /// sub-groups nested/indented under their primary parent and ledgers under their group.
    /// </summary>
    public void ShowChartOfAccounts()
    {
        if (Company is null) return;

        CurrentScreen = Screen.ChartOfAccounts;
        ClearSubScreens();
        ChartOfAccounts = new ChartOfAccountsViewModel(Company);
        ScreenTitle = "Chart of Accounts";
        Menu.Clear();
        Message = null;
        BuildButtonBar();
    }

    /// <summary>Nulls the report/voucher/ledger/chart sub-screen view models (mutually exclusive screens).</summary>
    private void ClearSubScreens()
    {
        Reports = null;
        VoucherEntry = null;
        LedgerMaster = null;
        ChartOfAccounts = null;
    }

    // =============================================================== form key helpers

    /// <summary>Ctrl+A on a form screen: accept the current voucher / create the current ledger.</summary>
    public void AcceptCurrent() => ActivateSelected();

    /// <summary>Alt+X: cancel the in-progress voucher (no save) and return to the Gateway.</summary>
    public void CancelVoucher()
    {
        if (CurrentScreen == Screen.VoucherEntry)
            VoucherEntry?.Cancel();
        else if (CurrentScreen == Screen.LedgerMaster)
            ShowGateway();
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

    // =============================================================== keyboard navigation

    /// <summary>Moves the highlight up (arrow Up), skipping non-selectable section headers; wraps.</summary>
    public void MoveUp() => Step(-1);

    /// <summary>Moves the highlight down (arrow Down), skipping non-selectable section headers; wraps.</summary>
    public void MoveDown() => Step(+1);

    /// <summary>
    /// Advances the highlight in the given direction, skipping section headers and wrapping around.
    /// No-op when the menu has no selectable items.
    /// </summary>
    private void Step(int direction)
    {
        if (Menu.Count == 0) return;
        if (!Menu.Any(m => m.IsSelectable)) return;

        var index = _selectedIndex;
        for (var i = 0; i < Menu.Count; i++)
        {
            index = (index + direction + Menu.Count) % Menu.Count;
            if (Menu[index].IsSelectable)
            {
                SetSelected(index);
                return;
            }
        }
    }

    /// <summary>Highlights the first selectable item (skipping any leading section header).</summary>
    private void SelectFirstSelectable()
    {
        for (var i = 0; i < Menu.Count; i++)
            if (Menu[i].IsSelectable)
            {
                SetSelected(i);
                return;
            }
        SetSelected(0);
    }

    /// <summary>
    /// Enter / Ctrl+A: activates the highlighted menu item, or performs the screen's accept action
    /// (create company, accept voucher, create ledger) when on a form screen.
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

        if (Menu.Count == 0) return;
        if (_selectedIndex < 0 || _selectedIndex >= Menu.Count) return;
        var item = Menu[_selectedIndex];
        if (item.IsSelectable) item.Activate(); // never fire on a section header
    }

    /// <summary>
    /// Esc: steps back one level. On a sub-screen (Report/Voucher/Ledger/Chart) → Gateway; inside a
    /// Gateway submenu (Vouchers/Create) → the root Gateway; on the root Gateway → Company Select.
    /// </summary>
    public void Back()
    {
        switch (CurrentScreen)
        {
            case Screen.Report:
            case Screen.VoucherEntry:   // Esc / Alt+X cancels the voucher without saving
            case Screen.LedgerMaster:
            case Screen.ChartOfAccounts:
                ShowGateway();
                break;
            case Screen.CreateCompany:
                ShowCompanySelect();
                break;
            case Screen.Gateway:
                // Inside a submenu, Esc pops back to the root Gateway; on the root, to Company Select.
                if (CurrentGatewayMenu != GatewayMenu.Root)
                    ShowGateway();
                else
                    ShowCompanySelect();
                break;
            case Screen.CompanySelect:
            default:
                break; // top level — nothing above
        }
    }

    private void SetSelected(int index)
    {
        for (var i = 0; i < Menu.Count; i++)
            Menu[i].IsSelected = i == index && Menu[i].IsSelectable;
        _selectedIndex = index;
    }

    /// <summary>Index of the currently highlighted menu item (for tests/keyboard).</summary>
    public int SelectedIndex => _selectedIndex;

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
        ButtonBar.Add(new ButtonBarItem("B", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), hasCompany));
        ButtonBar.Add(new ButtonBarItem("P", "Profit & Loss", () => OpenReport(ReportKind.ProfitAndLoss), hasCompany));
        ButtonBar.Add(new ButtonBarItem("T", "Trial Balance", () => OpenReport(ReportKind.TrialBalance), hasCompany));
        ButtonBar.Add(new ButtonBarItem("D", "Day Book", () => OpenReport(ReportKind.DayBook), hasCompany));

        ButtonBar.Add(new ButtonBarItem("F11", "Features", () => Message = "F11 Features — configured per company (Phase 1 defaults)."));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", () => Message = "F12 Configure — display options (Phase 1 defaults)."));
    }
}
