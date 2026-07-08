using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
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

    /// <summary>The effective as-of upper bound for the current report — the chosen period end, or the default.</summary>
    private DateOnly _asOf => _options.Period?.To ?? _options.AsOfDate;

    /// <summary>The stock item a Stock Item Movement report is scoped to (null for the other reports).</summary>
    private Guid? _movementItemId;

    [ObservableProperty] private ReportKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _isTwoColumn; // Dr/Cr grid (TB, BS) vs single-amount (P&L, DayBook)

    public ObservableCollection<ReportRow> Rows { get; } = new();

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
        or ReportKind.PriceList;

    /// <summary>True for any of the three Phase-4 GST reports (they use their own wide GST grids, slice 4d).</summary>
    public bool IsGstReport => Kind is ReportKind.TaxAnalysis or ReportKind.Gstr1 or ReportKind.Gstr3b;

    /// <summary>True to show the accounting (Particulars/Dr/Cr/Amount) grid — a report that is neither
    /// inventory nor GST (TB / BS / P&amp;L / Day Book).</summary>
    public bool IsAccountingReport => !IsInventoryReport && !IsGstReport;

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
        or ReportKind.DeliveryNoteRegister or ReportKind.RejectionRegister;

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
    public ReportsViewModel(Company company, ReportKind kind, Guid? stockItemId = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _defaultAsOf = ComputeAsOf(company);
        _options = ReportOptions.AsOf(_defaultAsOf);
        _movementItemId = stockItemId;

        Scenarios.Add(ScenarioOption.Actual);
        foreach (var s in company.Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Scenarios.Add(new ScenarioOption(s));
        _selectedScenario = Scenarios[0];

        Show(kind);
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
        // The layout flags are computed from Kind; notify the view so the right DataTemplate shows.
        OnPropertyChanged(nameof(IsInventoryReport));
        OnPropertyChanged(nameof(IsGstReport));
        OnPropertyChanged(nameof(IsAccountingReport));
        OnPropertyChanged(nameof(IsStockSummary));
        OnPropertyChanged(nameof(IsGodownSummary));
        OnPropertyChanged(nameof(IsStockMovement));
        OnPropertyChanged(nameof(IsReorderStatus));
        OnPropertyChanged(nameof(IsPhysicalStockRegister));
        OnPropertyChanged(nameof(IsOrderRegister));
        OnPropertyChanged(nameof(IsAllocationRegister));
        OnPropertyChanged(nameof(IsBatchwise));
        OnPropertyChanged(nameof(IsBatchAgeAnalysis));
        OnPropertyChanged(nameof(IsTaxAnalysis));
        OnPropertyChanged(nameof(IsGstr1));
        OnPropertyChanged(nameof(IsGstr3b));
        OnPropertyChanged(nameof(SupportsComparative));
        OnPropertyChanged(nameof(ShowGstGrid));
        // A kind change can invalidate the extra columns (e.g. switching to a non-comparative kind); the base
        // report always rebuilds below and, if comparative, the multi-column grid rebuilds after it.
        Rows.Clear();

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
        }

        // RQ-4: after the single-column report is built, (re)build the comparative multi-column grid when any
        // extra columns are present. This composes the engine ComparativeReport over [base spec, …extras]; the
        // plain Rows above stay intact so a switch back to single column is instant and byte-for-byte the same.
        RebuildComparative();
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
        Subtitle = $"{CompanyName}  —  {AsOfOrPeriodClause}{ScenarioSuffix}{DetailSuffix}";
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
                Rows.Add(DrCrLedgerLine(particulars, r.Debit, r.Credit, r.GroupName, r.LedgerId));
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
                Rows.Add(ReportRow.DrCrLine(g.Key, dr, cr));
            }
        }

        Rows.Add(ReportRow.DrCrTotal("Grand Total", tb.TotalDebit, tb.TotalCredit));
    }

    // --------------------------------------------------------------- Balance Sheet

    private void BuildBalanceSheet()
    {
        // RQ-1 as-of + RQ-6 closing-stock basis pass through ReportOptions; scenario rides along.
        var bs = BalanceSheet.Build(_company, _asOf, _options);
        Title = "Balance Sheet";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}{DetailSuffix}";
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
                Rows.Add(LedgerLine(particulars, shown[i].Amount, shown[i].GroupName, shown[i].LedgerId));
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
                Rows.Add(ReportRow.Line(particulars, shown[i].Amount));
            }
        }

        Rows.Add(ReportRow.Total(totalLabel, total));
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
        Subtitle = $"{CompanyName}  —  {plClause}{ScenarioSuffix}{DetailSuffix}";
        IsTwoColumn = false;

        AddProfitAndLossSide("Income", "Total Income",
            pl.Income.Select(i => (i.LedgerName, i.Amount, i.LedgerId)), pl.TotalIncome);
        AddProfitAndLossSide("Expenses", "Total Expenses",
            pl.Expenses.Select(e => (e.LedgerName, e.Amount, e.LedgerId)), pl.TotalExpenses);

        var isProfit = pl.NetProfit.Amount >= 0m;
        var label = isProfit ? "Net Profit" : "Net Loss";
        var magnitude = new Money(Math.Abs(pl.NetProfit.Amount));
        Rows.Add(ReportRow.Total(label, magnitude));
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
            Rows.Add(LedgerLine(particulars, shown[i].Amount, string.Empty, shown[i].LedgerId));
        }

        Rows.Add(ReportRow.Total(totalLabel, total));
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
        // cancelled rows carry Money.Zero so a positive range filter naturally excludes them; the default view
        // leaves the date-ordered list untouched.
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
    private static string DayBookParticulars(DayBookRow r) => $"{r.VoucherTypeName} No. {r.Number}";

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
            var voucher = r.Number > 0 ? $"{r.VoucherTypeName} No. {r.Number}" : r.VoucherTypeName;
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
                Col2 = r.Number.ToString(),
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

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col4 = "No entries in this period.", IsHeader = true });
        else
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
                Col2 = $"{r.VoucherTypeName} No. {r.Number}",
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(r.OrderedQuantity),
                Col7 = IndianFormat.Quantity(r.OutstandingQuantity),
                Col8 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col4 = "No orders in this period.", IsHeader = true });
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
                var applicable = version.ApplicableFrom.ToString("dd-MMM-yyyy",
                    System.Globalization.CultureInfo.InvariantCulture);
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
                Particulars = $"{FormatDate(r.Date)}  Memo No. {r.Number}",
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
                Particulars = $"{FormatDate(r.Date)}  Rev. Jrnl No. {r.Number}",
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
                Particulars = $"{FormatDate(r.Date)}  Bill No. {r.Number}  ·  {r.Party}",
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

    private static string FormatDate(DateOnly d) => d.ToString("dd-MMM-yyyy");
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
