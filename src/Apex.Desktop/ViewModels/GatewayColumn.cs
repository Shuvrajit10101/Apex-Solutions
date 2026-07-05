using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One column in the cascading (Miller-columns) Gateway. A column is either:
/// <list type="bullet">
/// <item>a <b>menu column</b> — a titled, keyboard-navigable list of <see cref="MenuItemViewModel"/>
/// rows (sections + items), with its own highlighted item; drilling into a highlighted <c>Group</c>
/// item spawns another menu column to the right, and drilling into a <c>Page</c> item spawns a page
/// column to the right; OR</item>
/// <item>a <b>page column</b> — a titled host for exactly one page sub-view-model (a report, a
/// voucher-entry screen, the ledger master, or the chart of accounts), rendered by the view inside
/// its own scrollable column.</item>
/// </list>
/// Each column owns its header and (in the view) its own independent vertical scroll, so text is never
/// clipped or overwritten across columns. Kept UI-toolkit-free so the whole cascade is unit-testable.
/// </summary>
public sealed partial class GatewayColumn : ViewModelBase
{
    /// <summary>The column header shown at the top of the column (e.g. "Gateway", "Vouchers", "Balance Sheet").</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>
    /// True while this column is the focused (active) column — arrow keys move within it and it paints
    /// the bright amber highlight. Earlier columns keep their selection but render it "inactive".
    /// </summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>True for a menu column (a list of rows); false for a page column.</summary>
    public bool IsMenu => Page is null;

    /// <summary>True for a page column (hosts a single page sub-view-model).</summary>
    public bool IsPage => Page is not null;

    /// <summary>The rows in a menu column (headers + selectable items). Empty for a page column.</summary>
    public ObservableCollection<MenuItemViewModel> Items { get; } = new();

    /// <summary>The hosted page sub-view-model for a page column; null for a menu column.</summary>
    public object? Page { get; }

    /// <summary>The hosted report view model (non-null only for a Balance Sheet / TB / P&amp;L / Day Book column).</summary>
    public ReportsViewModel? Report => Page as ReportsViewModel;

    /// <summary>The hosted voucher-entry view model (non-null only for a voucher-entry column).</summary>
    public VoucherEntryViewModel? Voucher => Page as VoucherEntryViewModel;

    /// <summary>The hosted inventory/order voucher-entry view model (non-null only for that column).</summary>
    public InventoryVoucherEntryViewModel? InventoryVoucher => Page as InventoryVoucherEntryViewModel;

    /// <summary>The hosted ledger-master view model (non-null only for the Ledger-creation column).</summary>
    public LedgerMasterViewModel? Ledger => Page as LedgerMasterViewModel;

    /// <summary>The hosted chart-of-accounts view model (non-null only for the Chart-of-Accounts column).</summary>
    public ChartOfAccountsViewModel? Chart => Page as ChartOfAccountsViewModel;

    /// <summary>The hosted Outstandings view model (non-null only for a Receivables/Payables column).</summary>
    public OutstandingsViewModel? Outstanding => Page as OutstandingsViewModel;

    /// <summary>The hosted Cost-Category master (non-null only for the Cost-Category creation column).</summary>
    public CostCategoryMasterViewModel? CostCategory => Page as CostCategoryMasterViewModel;

    /// <summary>The hosted Cost-Centre master (non-null only for the Cost-Centre creation column).</summary>
    public CostCentreMasterViewModel? CostCentre => Page as CostCentreMasterViewModel;

    /// <summary>The hosted cost-report view model (non-null only for a Category Summary / Break-up column).</summary>
    public CostReportsViewModel? CostReport => Page as CostReportsViewModel;

    /// <summary>The hosted Budget master (non-null only for the Budget-creation column).</summary>
    public BudgetMasterViewModel? BudgetMaster => Page as BudgetMasterViewModel;

    /// <summary>The hosted Budget Variance report (non-null only for the Budget Variance column).</summary>
    public BudgetVarianceViewModel? BudgetVariance => Page as BudgetVarianceViewModel;

    /// <summary>The hosted Bank Reconciliation page (non-null only for the BRS column).</summary>
    public BankReconciliationViewModel? BankReconciliation => Page as BankReconciliationViewModel;

    /// <summary>The hosted Import Bank Statement page (non-null only for the statement-import column).</summary>
    public BankStatementImportViewModel? BankStatementImport => Page as BankStatementImportViewModel;

    /// <summary>The hosted Scenario master (non-null only for the Scenario-creation column).</summary>
    public ScenarioMasterViewModel? ScenarioMaster => Page as ScenarioMasterViewModel;

    /// <summary>The hosted Interest Calculation report (non-null only for the Interest-report column).</summary>
    public InterestReportViewModel? InterestReport => Page as InterestReportViewModel;

    /// <summary>The hosted Currency master (+ Rates of Exchange) — non-null only for the Currency-creation column.</summary>
    public CurrencyMasterViewModel? CurrencyMaster => Page as CurrencyMasterViewModel;

    /// <summary>The hosted Forex Gain/Loss report (non-null only for the Forex-revaluation column).</summary>
    public ForexReportViewModel? ForexReport => Page as ForexReportViewModel;

    /// <summary>The hosted Stock-Group master (non-null only for the Stock-Group creation column).</summary>
    public StockGroupMasterViewModel? StockGroupMaster => Page as StockGroupMasterViewModel;

    /// <summary>The hosted Stock-Category master (non-null only for the Stock-Category creation column).</summary>
    public StockCategoryMasterViewModel? StockCategoryMaster => Page as StockCategoryMasterViewModel;

    /// <summary>The hosted Unit-of-Measure master (non-null only for the Unit creation column).</summary>
    public UnitMasterViewModel? UnitMaster => Page as UnitMasterViewModel;

    /// <summary>The hosted Godown master (non-null only for the Godown creation column).</summary>
    public GodownMasterViewModel? GodownMaster => Page as GodownMasterViewModel;

    /// <summary>The hosted Stock-Item master (non-null only for the Stock-Item creation column).</summary>
    public StockItemMasterViewModel? StockItemMaster => Page as StockItemMasterViewModel;

    /// <summary>The hosted company GST-configuration page (non-null only for the GST/Statutory column).</summary>
    public GstConfigViewModel? GstConfig => Page as GstConfigViewModel;

    /// <summary>The hosted F12 report-Configuration panel (non-null only for the report-config column).</summary>
    public ReportConfigViewModel? ReportConfig => Page as ReportConfigViewModel;

    /// <summary>The index of the highlighted row within a menu column (−1 when none selectable).</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Builds a menu column with the given title (rows are added by the caller).</summary>
    public GatewayColumn(string title)
    {
        Title = title;
        Page = null;
    }

    /// <summary>Builds a page column hosting the given page sub-view-model.</summary>
    public GatewayColumn(string title, object page)
    {
        Title = title;
        Page = page ?? throw new ArgumentNullException(nameof(page));
    }

    /// <summary>The currently highlighted selectable row, or null (page column / no selectable rows).</summary>
    public MenuItemViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>Adds a row to a menu column.</summary>
    public void Add(MenuItemViewModel item) => Items.Add(item);

    /// <summary>Highlights the first selectable row (skipping any leading section header).</summary>
    public void SelectFirstSelectable()
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].IsSelectable) { SetSelected(i); return; }
        SetSelected(-1);
    }

    /// <summary>
    /// Moves the highlight by <paramref name="direction"/> (±1), skipping section headers and wrapping
    /// around. No-op on a page column or a column with no selectable rows.
    /// </summary>
    public void Step(int direction)
    {
        if (Items.Count == 0) return;
        if (!Items.Any(m => m.IsSelectable)) return;

        var index = SelectedIndex < 0 ? (direction > 0 ? -1 : 0) : SelectedIndex;
        for (var i = 0; i < Items.Count; i++)
        {
            index = (index + direction + Items.Count) % Items.Count;
            if (Items[index].IsSelectable) { SetSelected(index); return; }
        }
    }

    /// <summary>Sets the highlighted row index and repaints the per-row selected flags.</summary>
    public void SetSelected(int index)
    {
        SelectedIndex = index;
        for (var i = 0; i < Items.Count; i++)
            Items[i].IsSelected = i == index && Items[i].IsSelectable;
    }

    /// <summary>
    /// Marks this column active/inactive: only an active menu column paints the bright amber highlight,
    /// so every row is told whether its column is the focused one.
    /// </summary>
    public void SetActive(bool active)
    {
        IsActive = active;
        foreach (var item in Items)
            item.IsActiveColumn = active;
    }
}
