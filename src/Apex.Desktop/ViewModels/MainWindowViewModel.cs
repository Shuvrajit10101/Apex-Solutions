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
}

/// <summary>
/// The single-window shell view model — the Gateway-of-Tally state machine. Owns the current
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
        Reports = null;
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

    /// <summary>Shows the Gateway of Tally menu for the open company.</summary>
    public void ShowGateway()
    {
        if (Company is null) { ShowCompanySelect(); return; }

        CurrentScreen = Screen.Gateway;
        ScreenTitle = "Gateway of Tally";
        Message = null;
        Reports = null;
        Menu.Clear();

        Menu.Add(new MenuItemViewModel("Balance Sheet", () => OpenReport(ReportKind.BalanceSheet)));
        Menu.Add(new MenuItemViewModel("Profit & Loss A/c", () => OpenReport(ReportKind.ProfitAndLoss)));
        Menu.Add(new MenuItemViewModel("Trial Balance", () => OpenReport(ReportKind.TrialBalance)));
        Menu.Add(new MenuItemViewModel("Day Book", () => OpenReport(ReportKind.DayBook)));
        Menu.Add(new MenuItemViewModel("Display More Reports", () => OpenReport(ReportKind.TrialBalance)));
        Menu.Add(new MenuItemViewModel("Quit — Change Company", ShowCompanySelect));

        SetSelected(0);
        BuildButtonBar();
    }

    // =============================================================== screen: report

    /// <summary>Opens a report for the current company.</summary>
    public void OpenReport(ReportKind kind)
    {
        if (Company is null) return;

        CurrentScreen = Screen.Report;
        Reports = new ReportsViewModel(Company, kind);
        ScreenTitle = Reports.Title;
        Menu.Clear();
        Message = null;
        BuildButtonBar();
    }

    // =============================================================== keyboard navigation

    /// <summary>Moves the highlight up (arrow Up); wraps to the bottom.</summary>
    public void MoveUp()
    {
        if (Menu.Count == 0) return;
        SetSelected((_selectedIndex - 1 + Menu.Count) % Menu.Count);
    }

    /// <summary>Moves the highlight down (arrow Down); wraps to the top.</summary>
    public void MoveDown()
    {
        if (Menu.Count == 0) return;
        SetSelected((_selectedIndex + 1) % Menu.Count);
    }

    /// <summary>Enter: activates the highlighted menu item (or creates the company on that screen).</summary>
    public void ActivateSelected()
    {
        if (CurrentScreen == Screen.CreateCompany)
        {
            CreateCompany();
            return;
        }

        if (Menu.Count == 0) return;
        if (_selectedIndex < 0 || _selectedIndex >= Menu.Count) return;
        Menu[_selectedIndex].Activate();
    }

    /// <summary>Esc: steps back one screen (Report → Gateway → Company Select).</summary>
    public void Back()
    {
        switch (CurrentScreen)
        {
            case Screen.Report:
                ShowGateway();
                break;
            case Screen.CreateCompany:
                ShowCompanySelect();
                break;
            case Screen.Gateway:
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
            Menu[i].IsSelected = i == index;
        _selectedIndex = index;
    }

    /// <summary>Index of the currently highlighted menu item (for tests/keyboard).</summary>
    public int SelectedIndex => _selectedIndex;

    // =============================================================== right button bar

    private void BuildButtonBar()
    {
        ButtonBar.Clear();

        // The core accounting F-keys. Report/voucher shortcuts are wired where implemented.
        ButtonBar.Add(new ButtonBarItem("F1", "Help", () => Message = "Apex Solutions — Tally Prime clone (Phase 1)."));
        ButtonBar.Add(new ButtonBarItem("F2", "Date", () => Message = StatusDate));
        ButtonBar.Add(new ButtonBarItem("F3", "Company", ShowCompanySelect));

        var hasCompany = Company is not null;
        ButtonBar.Add(new ButtonBarItem("F4", "Contra", () => OpenReport(ReportKind.DayBook), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F5", "Payment", () => OpenReport(ReportKind.DayBook), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F6", "Receipt", () => OpenReport(ReportKind.DayBook), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F7", "Journal", () => OpenReport(ReportKind.DayBook), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F8", "Sales", () => OpenReport(ReportKind.DayBook), hasCompany));
        ButtonBar.Add(new ButtonBarItem("F9", "Purchase", () => OpenReport(ReportKind.DayBook), hasCompany));

        // Report quick-jumps (enabled once a company is open).
        ButtonBar.Add(new ButtonBarItem("B", "Balance Sheet", () => OpenReport(ReportKind.BalanceSheet), hasCompany));
        ButtonBar.Add(new ButtonBarItem("P", "Profit & Loss", () => OpenReport(ReportKind.ProfitAndLoss), hasCompany));
        ButtonBar.Add(new ButtonBarItem("T", "Trial Balance", () => OpenReport(ReportKind.TrialBalance), hasCompany));
        ButtonBar.Add(new ButtonBarItem("D", "Day Book", () => OpenReport(ReportKind.DayBook), hasCompany));

        ButtonBar.Add(new ButtonBarItem("F11", "Features", () => Message = "F11 Features — configured per company (Phase 1 defaults)."));
        ButtonBar.Add(new ButtonBarItem("F12", "Configure", () => Message = "F12 Configure — display options (Phase 1 defaults)."));
    }
}
