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

    // ---- GST reports (slice 4d) ----
    TaxAnalysis,
    Gstr1,
    Gstr3b,
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

    // ---- inventory-report layout flags (drive which DataTemplate the view shows; slice 3.4b) ----

    /// <summary>True for any of the nine inventory reports (they use the wide inventory grids, not the
    /// accounting Particulars/Dr/Cr/Amount grid).</summary>
    public bool IsInventoryReport => Kind is ReportKind.StockSummary or ReportKind.GodownSummary
        or ReportKind.StockItemMovement or ReportKind.ReceiptNoteRegister or ReportKind.DeliveryNoteRegister
        or ReportKind.RejectionRegister or ReportKind.PhysicalStockRegister or ReportKind.OrderRegister
        or ReportKind.ReorderStatus;

    /// <summary>True for any of the three Phase-4 GST reports (they use their own wide GST grids, slice 4d).</summary>
    public bool IsGstReport => Kind is ReportKind.TaxAnalysis or ReportKind.Gstr1 or ReportKind.Gstr3b;

    /// <summary>True to show the accounting (Particulars/Dr/Cr/Amount) grid — a report that is neither
    /// inventory nor GST (TB / BS / P&amp;L / Day Book).</summary>
    public bool IsAccountingReport => !IsInventoryReport && !IsGstReport;

    public bool IsStockSummary => Kind == ReportKind.StockSummary;
    public bool IsGodownSummary => Kind == ReportKind.GodownSummary;
    public bool IsStockMovement => Kind == ReportKind.StockItemMovement;
    public bool IsReorderStatus => Kind == ReportKind.ReorderStatus;
    public bool IsPhysicalStockRegister => Kind == ReportKind.PhysicalStockRegister;
    public bool IsOrderRegister => Kind == ReportKind.OrderRegister;

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
    /// The scenario picker options: "Actual (no scenario)" first (a null <see cref="ScenarioOption.Scenario"/>),
    /// then every scenario defined on the company. Only meaningful for the balance reports (TB / P&amp;L / BS);
    /// the Day Book always shows the real books. Empty of scenarios ⇒ only the Actual option is offered.
    /// </summary>
    public ObservableCollection<ScenarioOption> Scenarios { get; } = new();

    /// <summary>The chosen scenario option; changing it rebuilds the current report under that scenario.</summary>
    [ObservableProperty] private ScenarioOption? _selectedScenario;

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
        OnPropertyChanged(nameof(IsTaxAnalysis));
        OnPropertyChanged(nameof(IsGstr1));
        OnPropertyChanged(nameof(IsGstr3b));
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

            case ReportKind.TaxAnalysis: BuildTaxAnalysis(); break;
            case ReportKind.Gstr1: BuildGstr1(); break;
            case ReportKind.Gstr3b: BuildGstr3b(); break;
        }
    }

    /// <summary>
    /// Drills a Stock-Summary item row into that item's Stock Item Movement report — the keyboard-first
    /// drill (Enter on a highlighted item row) and the double-click drill both route here. Raises
    /// <see cref="DrillToMovementRequested"/> so the shell opens the movement report as a new page column.
    /// A no-op unless this is the Stock Summary and the row carries a drill item id.
    /// </summary>
    public void Drill(ReportRow? row)
    {
        if (Kind != ReportKind.StockSummary) return;
        if (row?.DrillStockItemId is { } itemId)
            DrillToMovementRequested?.Invoke(itemId);
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
                Rows.Add(ReportRow.DrCrLine(particulars, r.Debit, r.Credit, r.GroupName));
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
            bs.Liabilities.Select(l => (l.Name, l.GroupName, l.Amount)), bs.TotalLiabilities);
        AddBalanceSheetSide("Assets", "Total Assets",
            bs.Assets.Select(a => (a.Name, a.GroupName, a.Amount)), bs.TotalAssets);
    }

    /// <summary>Adds one Balance-Sheet side (Liabilities/Assets) honouring RQ-2 summary + RQ-6 hide-zero/percent.</summary>
    private void AddBalanceSheetSide(
        string header, string totalLabel,
        IEnumerable<(string Name, string GroupName, Money Amount)> lines, Money total)
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
                Rows.Add(ReportRow.Line(particulars, shown[i].Amount, shown[i].GroupName));
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
            pl.Income.Select(i => (i.LedgerName, i.Amount)), pl.TotalIncome);
        AddProfitAndLossSide("Expenses", "Total Expenses",
            pl.Expenses.Select(e => (e.LedgerName, e.Amount)), pl.TotalExpenses);

        var isProfit = pl.NetProfit.Amount >= 0m;
        var label = isProfit ? "Net Profit" : "Net Loss";
        var magnitude = new Money(Math.Abs(pl.NetProfit.Amount));
        Rows.Add(ReportRow.Total(label, magnitude));
    }

    /// <summary>Adds one P&amp;L side (Income/Expenses) honouring RQ-2 summary + RQ-6 hide-zero/percent.</summary>
    private void AddProfitAndLossSide(
        string header, string totalLabel,
        IEnumerable<(string LedgerName, Money Amount)> lines, Money total)
    {
        Rows.Add(new ReportRow { Particulars = header, IsHeader = true });

        // P&L lines carry no group key on the projection; the summary roll-up keys on the section header so a
        // rolled-up side collapses to a single "Income"/"Expenses" total row (still Σ==detailed).
        var all = lines.ToList();
        IReadOnlyList<(string Name, Money Amount)> shown = _options.Detailed
            ? all.Select(l => (l.LedgerName, l.Amount)).ToList()
            : ReportGrouping.RollUp(all, _ => header, l => l.Amount).Select(g => (g.Key, g.Amount)).ToList();

        if (_options.HideZeroBalances)
            shown = ReportConfig.HideZeroBalances(shown, l => l.Amount);

        // RQ-3: the sort/filter VIEW acts within this side (Income vs Expenses), preserving the two-section
        // structure. Magnitude = |amount|.
        shown = _sortFilter.Apply(shown, l => l.Name, l => new Money(Math.Abs(l.Amount.Amount)));

        var pct = _options.ShowPercentages ? ReportConfig.Percentages(shown, l => l.Amount) : null;
        for (var i = 0; i < shown.Count; i++)
        {
            var particulars = pct is null ? shown[i].Name : shown[i].Name + PercentSuffix(pct[i]);
            Rows.Add(ReportRow.Line(particulars, shown[i].Amount));
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

    // --------------------------------------------------------------- Reorder Status (Item | Closing | Reorder Level | Shortfall | Suggested)

    private void BuildReorderStatus()
    {
        var rs = Report.BuildReorderStatus(_company, _asOf);
        Title = "Reorder Status";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        foreach (var r in rs.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.ItemName,
                Col2 = IndianFormat.Quantity(r.ClosingQuantity),
                Col3 = IndianFormat.Quantity(r.ReorderLevel),
                Col4 = IndianFormat.Quantity(r.Shortfall),
                Col5 = IndianFormat.Quantity(r.SuggestedOrderQuantity),
            });

        if (rs.Rows.Count == 0)
            Rows.Add(new ReportRow { Col1 = "All items are above their reorder levels.", IsHeader = true });
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
