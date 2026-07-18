using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// WI-2 / WI-9 — what a bare letter MEANS in a menu column. The two features contend for the same keystroke
/// on the same widget, so the shell resolves it by COLUMN KIND rather than case by case:
/// <list type="bullet">
/// <item><see cref="Authored"/> — a fixed, hand-built menu (Gateway, Vouchers, Create, …). Its rows are known
/// at build time, so each gets a computed bare-letter hotkey and a bare letter <b>ACTIVATES</b> that row.</item>
/// <item><see cref="DataDriven"/> — a picker over company data (ledgers, stock items, parties). Its rows are
/// whatever the company holds, so hotkeys are meaningless and a bare letter <b>FILTERS</b> (type-ahead);
/// Enter then selects the highlighted row.</item>
/// </list>
/// </summary>
public enum GatewayColumnKind
{
    /// <summary>A fixed, hand-built menu column — a bare letter activates its hotkey row.</summary>
    Authored,

    /// <summary>A picker over company data — a bare letter filters (type-ahead) instead of activating.</summary>
    DataDriven,
}

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

    /// <summary>
    /// True while this column is the RIGHTMOST (terminal) column in the cascade. Drives the C4
    /// viewport-aware width (<see cref="Apex.Desktop.Converters.CascadeColumnWidthConverter"/>): only the
    /// terminal page column fills the leftover viewport (killing the dead-cream band); a page column that
    /// has ANOTHER column to its right (e.g. a report with a ledger-vouchers drill column) keeps a bounded
    /// width so BOTH fit side-by-side when the viewport allows, with the h-ScrollViewer as the fallback.
    /// Kept in sync by <see cref="MainWindowViewModel"/>'s column repaint (SyncActiveColumn).
    /// </summary>
    [ObservableProperty] private bool _isLast = true;

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

    /// <summary>The hosted accounting-Group master (non-null only for the Group-creation column; WI-7).</summary>
    public AccountGroupMasterViewModel? AccountGroupMaster => Page as AccountGroupMasterViewModel;

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

    /// <summary>The hosted Batch/Lot master (non-null only for the Batch-creation column; Phase 6 Cluster 1).</summary>
    public BatchMasterViewModel? BatchMaster => Page as BatchMasterViewModel;

    /// <summary>The hosted batch-allocation sub-screen (non-null only for that column; Phase 6 Cluster 1; RQ-3).</summary>
    public BatchAllocationViewModel? BatchAllocation => Page as BatchAllocationViewModel;

    /// <summary>The hosted Bill-of-Materials master (non-null only for the BOM-creation column; Phase 6 Cluster 2; RQ-9).</summary>
    public BomMasterViewModel? BomMaster => Page as BomMasterViewModel;

    /// <summary>The hosted Manufacturing-Journal voucher-entry screen (non-null only for that column; Phase 6 Cluster 2; RQ-11).</summary>
    public ManufacturingJournalEntryViewModel? ManufacturingJournalEntry => Page as ManufacturingJournalEntryViewModel;

    /// <summary>The hosted Job Work In/Out Order voucher-entry screen (non-null only for that column; Phase 6 slice 8; RQ-47).</summary>
    public JobWorkOrderEntryViewModel? JobWorkOrderEntry => Page as JobWorkOrderEntryViewModel;

    /// <summary>The hosted Material In/Out movement voucher-entry screen (non-null only for that column; Phase 6 slice 8; RQ-48).</summary>
    public MaterialMovementEntryViewModel? MaterialMovementEntry => Page as MaterialMovementEntryViewModel;

    /// <summary>The hosted POS Billing voucher-entry screen (non-null only for that column; Phase 6 slice 7; RQ-38..RQ-44).</summary>
    public PosBillingViewModel? PosBilling => Page as PosBillingViewModel;

    /// <summary>The hosted company GST-configuration page (non-null only for the GST/Statutory column).</summary>
    public GstConfigViewModel? GstConfig => Page as GstConfigViewModel;

    /// <summary>The hosted Nature-of-Payment (TDS section) master (non-null only for that column; Phase 7 slice 1).</summary>
    public NatureOfPaymentMasterViewModel? NatureOfPaymentMaster => Page as NatureOfPaymentMasterViewModel;

    /// <summary>The hosted Nature-of-Goods (§206C TCS) master (non-null only for that column; Phase 7 slice 1).</summary>
    public NatureOfGoodsMasterViewModel? NatureOfGoodsMaster => Page as NatureOfGoodsMasterViewModel;

    /// <summary>The hosted TDS Stat-Payment deposit page (non-null only for that column; Phase 7 slice 3).</summary>
    public TdsStatPaymentViewModel? TdsStatPayment => Page as TdsStatPaymentViewModel;

    /// <summary>The hosted Challan Reconciliation report (non-null only for that column; Phase 7 slice 3).</summary>
    public ChallanReconciliationViewModel? ChallanReconciliation => Page as ChallanReconciliationViewModel;

    /// <summary>The hosted Form 26Q quarterly-TDS-return report (non-null only for that column; Phase 7 slice 4).</summary>
    public Form26QViewModel? Form26Q => Page as Form26QViewModel;

    /// <summary>The hosted TCS Stat-Payment deposit page (non-null only for that column; Phase 7 slice 6).</summary>
    public TcsStatPaymentViewModel? TcsStatPayment => Page as TcsStatPaymentViewModel;

    /// <summary>The hosted TCS Challan Reconciliation report (non-null only for that column; Phase 7 slice 6).</summary>
    public TcsChallanReconciliationViewModel? TcsChallanReconciliation => Page as TcsChallanReconciliationViewModel;

    /// <summary>The hosted Form 27EQ quarterly-TCS-return report (non-null only for that column; Phase 7 slice 6).</summary>
    public Form27EQViewModel? Form27EQ => Page as Form27EQViewModel;

    /// <summary>The hosted Form 16A TDS-certificate report (non-null only for that column; Phase 7 slice 7).</summary>
    public Form16AViewModel? Form16A => Page as Form16AViewModel;

    /// <summary>The hosted Form 27D TCS-certificate report (non-null only for that column; Phase 7 slice 7).</summary>
    public Form27DViewModel? Form27D => Page as Form27DViewModel;

    /// <summary>The hosted Form 27A return-control-chart report (non-null only for that column; Phase 7 slice 7).</summary>
    public Form27AViewModel? Form27A => Page as Form27AViewModel;

    /// <summary>The hosted PF ECR / Challan report (non-null only for that column; Phase 8 slice 4; RQ-9).</summary>
    public PfEcrReportViewModel? PfEcrReport => Page as PfEcrReportViewModel;

    /// <summary>The hosted ESI Monthly Contribution report (non-null only for that column; Phase 8 slice 5; RQ-10).</summary>
    public EsiContributionReportViewModel? EsiContributionReport => Page as EsiContributionReportViewModel;

    /// <summary>The hosted PT Deduction Register report (non-null only for that column; Phase 8 slice 6; RQ-11).</summary>
    public ProfessionalTaxRegisterViewModel? ProfessionalTaxRegister => Page as ProfessionalTaxRegisterViewModel;

    /// <summary>The hosted Price Level master (non-null only for the Price-Level creation column; Phase 6 slice 5; RQ-26).</summary>
    public PriceLevelsViewModel? PriceLevels => Page as PriceLevelsViewModel;

    /// <summary>The hosted Price List master (non-null only for the Price-List creation column; Phase 6 slice 5; RQ-27).</summary>
    public PriceListsViewModel? PriceLists => Page as PriceListsViewModel;

    /// <summary>The hosted Reorder Levels master (non-null only for the Reorder-Levels column; Phase 6 slice 6; RQ-32).</summary>
    public ReorderLevelsViewModel? ReorderLevels => Page as ReorderLevelsViewModel;

    /// <summary>The hosted F12 report-Configuration panel (non-null only for the report-config column).</summary>
    public ReportConfigViewModel? ReportConfig => Page as ReportConfigViewModel;

    /// <summary>The hosted Alt+F12 report Sort/Filter panel (non-null only for the sort/filter column).</summary>
    public ReportSortFilterViewModel? ReportSortFilter => Page as ReportSortFilterViewModel;

    /// <summary>The hosted Alt+C "Add Comparison Column" panel (non-null only for that RQ-4 column).</summary>
    public AddComparisonColumnViewModel? AddComparisonColumn => Page as AddComparisonColumnViewModel;

    /// <summary>The hosted Alt+N "Auto Columns" chooser (non-null only for that RQ-4 column).</summary>
    public AutoColumnsViewModel? AutoColumns => Page as AutoColumnsViewModel;

    /// <summary>The hosted Ctrl+S "Save View" panel (non-null only for that RQ-8 column).</summary>
    public SaveViewViewModel? SaveView => Page as SaveViewViewModel;

    /// <summary>The hosted Alt+K "Saved Views" list panel (non-null only for that RQ-8 column).</summary>
    public SavedViewsViewModel? SavedViews => Page as SavedViewsViewModel;

    /// <summary>The hosted P / Ctrl+P "Print Preview" panel (non-null only for that RQ-9 column).</summary>
    public PrintPreviewViewModel? PrintPreview => Page as PrintPreviewViewModel;

    /// <summary>The hosted F12 print-config panel over a voucher/invoice preview (non-null only for that RQ-12 column).</summary>
    public PrintConfigViewModel? PrintConfigPanel => Page as PrintConfigViewModel;

    /// <summary>The hosted E / Alt+E "Export" panel (non-null only for that RQ-14 column).</summary>
    public ExportViewModel? ExportPanel => Page as ExportViewModel;

    /// <summary>The hosted Y "Export Data" canonical-backup panel (non-null only for that RQ-19/DP-4 column).</summary>
    public ExportDataViewModel? ExportDataPanel => Page as ExportDataViewModel;

    /// <summary>The hosted O / Alt+O "Import" panel (non-null only for that RQ-20..24 column).</summary>
    public ImportDataViewModel? ImportDataPanel => Page as ImportDataViewModel;

    /// <summary>The hosted M / Ctrl+M "E-Mail" compose panel (non-null only for that RQ-25/26 column).</summary>
    public EmailComposeViewModel? EmailCompose => Page as EmailComposeViewModel;

    /// <summary>The hosted "SMTP Settings" capture panel (non-null only for that RQ-27 column).</summary>
    public SmtpSettingsViewModel? SmtpSettings => Page as SmtpSettingsViewModel;

    /// <summary>The hosted RQ-7 ledger-vouchers drill column (non-null only for a drilled TB/BS/P&amp;L ledger).</summary>
    public LedgerVouchersViewModel? LedgerVouchers => Page as LedgerVouchersViewModel;

    /// <summary>The hosted RQ-7 read-only voucher-detail drill column (non-null only for a drilled voucher).</summary>
    public VoucherDetailViewModel? VoucherDetail => Page as VoucherDetailViewModel;

    /// <summary>The index of the highlighted row within a menu column (−1 when none selectable).</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    /// Whether a bare letter ACTIVATES a hotkey row (<see cref="GatewayColumnKind.Authored"/>, the default for a
    /// hand-built menu) or FILTERS the rows (<see cref="GatewayColumnKind.DataDriven"/>, a picker over company
    /// data). See <see cref="GatewayColumnKind"/> for why the rule is by column kind.
    /// </summary>
    public GatewayColumnKind Kind { get; init; } = GatewayColumnKind.Authored;

    /// <summary>True when a bare letter should filter this column rather than activate a row.</summary>
    public bool IsTypeAheadColumn => Kind == GatewayColumnKind.DataDriven;

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

    // =========================================================== WI-9: computed bare-letter hotkeys

    /// <summary>
    /// WI-9 — assigns each selectable row a bare-letter hotkey, COMPUTED per column at build time so none of
    /// the ~176 menu rows needs a hand-picked letter (and so a row added later cannot silently collide).
    /// <para>
    /// The rule: prefer the label's FIRST letter; if another row in this column already claimed it, walk the
    /// label for the next letter still free. A row whose every letter is taken simply gets no hotkey
    /// (<see cref="MenuItemViewModel.HotKeyIndex"/> stays −1) — better a missing accelerator than two rows
    /// answering to one key. Matching is case-insensitive, and only letters are eligible (never a digit,
    /// space or punctuation).
    /// </para>
    /// <para>
    /// A <see cref="GatewayColumnKind.DataDriven"/> column is skipped entirely: there a bare letter filters,
    /// so every row is cleared to −1 (see <see cref="GatewayColumnKind"/>).
    /// </para>
    /// </summary>
    /// <summary>
    /// Letters that are ALREADY bound as bare accelerators everywhere on the Gateway cascade, and so can never
    /// be handed out as a menu hotkey: <b>O</b> opens Import and <b>Y</b> opens Export Data (both scoped only to
    /// <c>CurrentScreen == Screen.Gateway</c>, which every cascade menu column is). Those arms sit earlier in the
    /// window's first-match-wins chain, so a row given one of these letters would paint a red accelerator that
    /// silently does something else. Reserving them keeps every painted hotkey honest.
    /// <para>
    /// E / P / M are deliberately NOT reserved: their arms are gated on <c>IsExportablePage</c> /
    /// <c>IsPrintablePage</c>, both false while a menu column is on top, so they are free here.
    /// </para>
    /// </summary>
    private static readonly char[] ReservedLetters = { 'O', 'Y' };

    public void AssignHotKeys()
    {
        if (Kind == GatewayColumnKind.DataDriven)
        {
            foreach (var item in Items)
                item.HotKeyIndex = -1;
            return;
        }

        var claimed = new System.Collections.Generic.HashSet<char>(ReservedLetters);

        foreach (var item in Items)
        {
            item.HotKeyIndex = -1;
            if (!item.IsSelectable) continue;

            for (var i = 0; i < item.Label.Length; i++)
            {
                var candidate = char.ToUpperInvariant(item.Label[i]);
                if (!char.IsLetter(candidate)) continue;
                if (!claimed.Add(candidate)) continue;

                item.HotKeyIndex = i;
                break;
            }
        }
    }

    /// <summary>
    /// WI-9 — the row this column's bare <paramref name="letter"/> activates, or null when no row claimed it.
    /// Always null for a <see cref="GatewayColumnKind.DataDriven"/> column, where a letter filters instead.
    /// </summary>
    public MenuItemViewModel? FindByHotKey(char letter)
    {
        if (Kind == GatewayColumnKind.DataDriven) return null;

        var wanted = char.ToUpperInvariant(letter);
        return Items.FirstOrDefault(
            i => i.IsSelectable && i.HotKey is { } key && char.ToUpperInvariant(key) == wanted);
    }

    // =========================================================== WI-2: type-ahead over a data-driven picker

    /// <summary>The prefix typed so far in a data-driven picker column (empty when not filtering).</summary>
    public string TypeAheadPrefix { get; private set; } = string.Empty;

    /// <summary>
    /// WI-2 — extends the type-ahead prefix by one character and moves the highlight to the first row whose
    /// label starts with it, so the operator narrows a long ledger/party list by typing rather than arrowing.
    /// <para>
    /// Returns <c>false</c> (leaving the highlight and the prefix untouched) when nothing matches, so a stray
    /// keystroke never silently drags the selection onto an unrelated party — the wrong-ledger failure this
    /// campaign exists to remove. A no-op on an <see cref="GatewayColumnKind.Authored"/> column.
    /// </para>
    /// </summary>
    public bool TypeAhead(char c)
    {
        if (Kind != GatewayColumnKind.DataDriven) return false;

        // Conventional type-ahead: pressing the SAME single letter again CYCLES to the next row starting with
        // it (wrapping), rather than re-selecting the first match forever. Without this, two parties sharing a
        // first letter are unreachable by keyboard — "A" would always land on the same one.
        if (TypeAheadPrefix.Length == 1
            && char.ToUpperInvariant(TypeAheadPrefix[0]) == char.ToUpperInvariant(c))
        {
            var next = IndexOfPrefix(c.ToString(), after: SelectedIndex);
            if (next < 0) return false;

            TypeAheadPrefix = c.ToString();
            SetSelected(next);
            return true;
        }

        var candidate = TypeAheadPrefix + c;
        var index = IndexOfPrefix(candidate);

        // A fresh single letter restarts the search when the extended prefix matches nothing — the operator
        // has moved on to a different party rather than continuing to spell the previous one.
        if (index < 0 && TypeAheadPrefix.Length > 0)
        {
            candidate = c.ToString();
            index = IndexOfPrefix(candidate);
        }

        if (index < 0) return false;

        TypeAheadPrefix = candidate;
        SetSelected(index);
        return true;
    }

    /// <summary>
    /// Clears the type-ahead prefix. Called from <see cref="MainWindowViewModel"/>'s single focus choke point
    /// whenever the active column changes — so the prefix is dropped on Esc/Back, on a completed selection
    /// (which opens the next column), and on both leaving and entering a column. A prefix belongs to the column
    /// the operator is IN; carrying one across focus would silently extend the next column's first keystroke.
    /// </summary>
    public void ResetTypeAhead() => TypeAheadPrefix = string.Empty;

    /// <summary>
    /// The first selectable row whose label starts with <paramref name="prefix"/>. When <paramref name="after"/>
    /// is given the scan STARTS just past that index and wraps, which is what makes a repeated letter cycle.
    /// </summary>
    private int IndexOfPrefix(string prefix, int? after = null)
    {
        if (Items.Count == 0) return -1;

        var start = after is { } from ? from + 1 : 0;
        for (var step = 0; step < Items.Count; step++)
        {
            var i = ((start + step) % Items.Count + Items.Count) % Items.Count;
            if (Items[i].IsSelectable &&
                Items[i].Label.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
