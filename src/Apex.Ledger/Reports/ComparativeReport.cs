using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>Which single-column report family a comparative report projects side by side (RQ-4).</summary>
public enum ComparativeReportKind
{
    /// <summary>The Trial Balance (closing Dr/Cr per ledger) — see <see cref="Reports.TrialBalance"/>.</summary>
    TrialBalance,

    /// <summary>The Trading + Profit &amp; Loss statement — see <see cref="Reports.ProfitAndLoss"/>.</summary>
    ProfitAndLoss,

    /// <summary>The Balance Sheet — see <see cref="Reports.BalanceSheet"/>.</summary>
    BalanceSheet,

    /// <summary>The Stock Summary (per item closing value) — see <see cref="Reports.StockSummary"/>.</summary>
    StockSummary,

    /// <summary>
    /// The Cash Flow statement (per counterparty ledger, signed cash released) — see <see cref="Reports.CashFlow"/>.
    /// <para>Vendor-attested as a columnar report: <i>"Press Alt+N (Auto Column) and select the required period in
    /// the Column Details field"</i> and <i>"you can configure the columns to appear quarterly, weekly, daily,
    /// fortnightly, and so on"</i> — <c>help.tallysolutions.com/cash-flow-and-projection-report-tally/</c>, opened by
    /// content 2026-10-20.</para>
    /// </summary>
    CashFlow,

    /// <summary>
    /// The Funds Flow statement (per balance-sheet ledger, signed funds provided) — see <see cref="Reports.FundsFlow"/>.
    /// <para>Vendor-attested as a columnar report: <i>"The Funds Flow displays the movement of working capital for
    /// each month"</i> and <i>"add periodic columns like monthly, quarterly, weekly, and so on for a comparative view
    /// of the funds flow across periods"</i> — <c>help.tallysolutions.com/funds-flow-report-tally/</c>, opened by
    /// content 2026-10-20.</para>
    /// </summary>
    FundsFlow,
}

/// <summary>
/// The bucket size an Alt+N auto-column axis repeats at. The members are the vendor's own list, opened by content
/// 2026-10-20: <i>"add periodic columns like monthly, quarterly, weekly, and so on"</i>
/// (<c>help.tallysolutions.com/funds-flow-report-tally/</c>) and <i>"you can configure the columns to appear
/// quarterly, weekly, daily, fortnightly, and so on"</i>
/// (<c>help.tallysolutions.com/cash-flow-and-projection-report-tally/</c>). <see cref="Yearly"/> is ours, for a
/// multi-year window; both vendor lists end in "and so on" and neither is exhaustive.
/// </summary>
public enum ColumnPeriodicity
{
    /// <summary>One column per day.</summary>
    Daily,

    /// <summary>One column per 7-day block from the period's first day.</summary>
    Weekly,

    /// <summary>One column per 14-day block from the period's first day.</summary>
    Fortnightly,

    /// <summary>One column per calendar month (the long-standing default axis).</summary>
    Monthly,

    /// <summary>One column per calendar quarter.</summary>
    Quarterly,

    /// <summary>One column per 12-month block from the period's first day.</summary>
    Yearly,
}

/// <summary>
/// A comparative / columnar report (RQ-4): the SAME report (Balance Sheet, P&amp;L, Trial Balance, Stock
/// Summary, Cash Flow or Funds Flow) rendered across MULTIPLE columns, each a different <b>period</b> and/or
/// <b>scenario</b> (scenario columns only on the kinds <see cref="SupportsScenarioColumns"/> admits). Rows are
/// the union of line keys (ledger / group / item) aligned across the columns; a key absent in a column shows a
/// <c>null</c> (blank) value there. Each column keeps its own totals.
/// <para>This projection <b>composes</b> the existing single-column builders — it never re-derives accounting.
/// For each <see cref="ColumnSpec"/> it invokes the corresponding <c>Build</c> (Trial Balance / P&amp;L /
/// Balance Sheet / Stock Summary) under that column's period + scenario, then merges the resulting rows by a
/// stable key. The default single-column path (one spec) is byte-for-byte the plain report, so nothing
/// regresses.</para>
/// A <b>pure</b> value type — no UI, no persistence.
/// </summary>
public sealed record ComparativeReport(
    ComparativeReportKind Kind,
    IReadOnlyList<ComparativeReport.Column> Columns,
    IReadOnlyList<ComparativeRow> Rows)
{
    /// <summary>
    /// One comparison column = a display <see cref="Label"/> plus an optional <see cref="Period"/> window and an
    /// optional <see cref="Scenario"/>. A <c>null</c> period runs the report at its natural as-of (books-begin →
    /// the caller's default); a <c>null</c> scenario uses the actual books. Ordered; the base column is first.
    /// <para><see cref="Options"/> is an optional full report-options carrier: when supplied the column STARTS FROM
    /// those options (preserving <c>AsOfDate</c>, <c>Detailed</c>, <c>HideZeroBalances</c>, <c>ShowPercentages</c>,
    /// <c>ClosingStock</c> and <c>Scenario</c>) so a column can reproduce the exact single-column report beside it;
    /// <see cref="Period"/> / <see cref="Scenario"/> are then overlaid only when explicitly set on this spec. When
    /// <c>null</c> the legacy behaviour applies exactly (period → as-of, else the financial-year-end fallback).</para>
    /// </summary>
    public sealed record ColumnSpec(
        string Label, PeriodRange? Period = null, Scenario? Scenario = null, ReportOptions? Options = null);

    /// <summary>
    /// A built comparison column: its <see cref="Label"/> plus the per-column totals appropriate to the report
    /// kind. Only the fields for the active <see cref="ComparativeReportKind"/> are meaningful; the rest stay
    /// <see cref="Money.Zero"/>. Trial Balance → <see cref="TotalDebit"/>/<see cref="TotalCredit"/>; Balance Sheet
    /// → <see cref="TotalLiabilities"/>/<see cref="TotalAssets"/>; P&amp;L → <see cref="TotalIncome"/>/
    /// <see cref="TotalExpenses"/>/<see cref="NetProfit"/>; Stock Summary → <see cref="StockClosingValue"/>;
    /// Cash Flow → <see cref="CashOpening"/>/<see cref="CashNetFlow"/>/<see cref="CashClosing"/>; Funds Flow →
    /// <see cref="TotalSources"/>/<see cref="TotalApplications"/>.
    /// </summary>
    public sealed record Column(
        string Label,
        Money TotalDebit = default,
        Money TotalCredit = default,
        Money TotalLiabilities = default,
        Money TotalAssets = default,
        Money TotalIncome = default,
        Money TotalExpenses = default,
        Money NetProfit = default,
        Money StockClosingValue = default,
        Money CashOpening = default,
        Money CashNetFlow = default,
        Money CashClosing = default,
        Money TotalSources = default,
        Money TotalApplications = default);

    /// <summary>
    /// Builds the comparative report for <paramref name="kind"/> across the ordered <paramref name="specs"/>.
    /// Each spec's single-column report is built once (composing the existing builders) and its rows are merged
    /// by a stable key into aligned <see cref="ComparativeRow"/> values. A key missing from a column carries a
    /// <c>null</c> (blank) value at that column's index. The row order follows first appearance across the
    /// columns in order, then within the last-seen ordering is stable.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="specs"/> is empty.</exception>
    public static ComparativeReport Build(Company company, ComparativeReportKind kind, IReadOnlyList<ColumnSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(specs);
        if (specs.Count == 0)
            throw new ArgumentException("A comparative report needs at least one column spec.", nameof(specs));

        // 🔴 Refuse a scenario column on a scenario-blind kind rather than composing one that silently reproduces
        // the actual books under a scenario's name. See SupportsScenarioColumns for why this is a refusal.
        if (!SupportsScenarioColumns(kind))
        {
            for (var i = 0; i < specs.Count; i++)
            {
                if (specs[i].Scenario is null && specs[i].Options?.Scenario is null) continue;
                throw new ArgumentException(
                    $"{kind} is built from unscenarioed ledger movements, so it cannot compose a scenario column; "
                    + $"column {i} ('{specs[i].Label}') carries one. Use period columns for this report.",
                    nameof(specs));
            }
        }

        var columnCount = specs.Count;
        var columns = new List<Column>(columnCount);

        // Ordered merge: key → (label, groupName, per-column values). The order list preserves first appearance.
        var order = new List<string>();
        var byKey = new Dictionary<string, RowAccumulator>(StringComparer.Ordinal);

        for (var col = 0; col < columnCount; col++)
        {
            var spec = specs[col];
            IReadOnlyList<Cell> cells;
            Column column;
            switch (kind)
            {
                case ComparativeReportKind.TrialBalance: cells = BuildTrialBalance(company, spec, out column); break;
                case ComparativeReportKind.ProfitAndLoss: cells = BuildProfitAndLoss(company, spec, out column); break;
                case ComparativeReportKind.BalanceSheet: cells = BuildBalanceSheet(company, spec, out column); break;
                case ComparativeReportKind.StockSummary: cells = BuildStockSummary(company, spec, out column); break;
                case ComparativeReportKind.CashFlow: cells = BuildCashFlow(company, spec, out column); break;
                case ComparativeReportKind.FundsFlow: cells = BuildFundsFlow(company, spec, out column); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown report kind.");
            }
            columns.Add(column);

            foreach (var cell in cells)
            {
                if (!byKey.TryGetValue(cell.Key, out var acc))
                {
                    acc = new RowAccumulator(cell.Key, cell.Label, cell.GroupName, columnCount);
                    byKey[cell.Key] = acc;
                    order.Add(cell.Key);
                }
                acc.Values[col] = cell.Value;
                // Prefer a non-null group name if a later column supplies one the first lacked.
                acc.GroupName ??= cell.GroupName;
            }
        }

        var rows = new List<ComparativeRow>(order.Count);
        foreach (var key in order)
        {
            var acc = byKey[key];
            rows.Add(new ComparativeRow(acc.Key, acc.Label, acc.GroupName, acc.Values));
        }

        return new ComparativeReport(kind, columns, rows);
    }

    // ---- column builders (each composes a single-column report and emits its cells) ----------

    private static IReadOnlyList<Cell> BuildTrialBalance(Company company, ColumnSpec spec, out Column column)
    {
        var options = OptionsFor(company, spec);
        var report = TrialBalance.Build(company, options);
        column = new Column(spec.Label, TotalDebit: report.TotalDebit, TotalCredit: report.TotalCredit);

        var cells = new List<Cell>(report.Rows.Count);
        foreach (var r in report.Rows)
        {
            // A Trial Balance row is a single closing figure on exactly one side; the sign encodes Dr/Cr.
            // Persist the SIGNED closing so two columns compare like-for-like and blanks stay unambiguous.
            var value = r.Debit != Money.Zero ? r.Debit : -r.Credit;
            cells.Add(new Cell(Key: r.LedgerName, Label: r.LedgerName, GroupName: r.GroupName, Value: value));
        }
        return cells;
    }

    private static IReadOnlyList<Cell> BuildProfitAndLoss(Company company, ColumnSpec spec, out Column column)
    {
        var options = OptionsFor(company, spec);
        var report = ProfitAndLoss.Build(company, options.AsOfDate, options);
        column = new Column(
            spec.Label,
            TotalIncome: report.TotalIncome,
            TotalExpenses: report.TotalExpenses,
            NetProfit: report.NetProfit);

        var cells = new List<Cell>(report.Income.Count + report.Expenses.Count);
        // Income lines carry a positive magnitude; expense lines a negative one, so a single column of signed
        // values reads unambiguously (credit income up, debit expense down) and blanks stay blank.
        foreach (var line in report.Income)
            cells.Add(new Cell(Key: "I:" + line.LedgerName, Label: line.LedgerName, GroupName: "Income", Value: line.Amount));
        foreach (var line in report.Expenses)
            cells.Add(new Cell(Key: "E:" + line.LedgerName, Label: line.LedgerName, GroupName: "Expenses", Value: -line.Amount));
        return cells;
    }

    private static IReadOnlyList<Cell> BuildBalanceSheet(Company company, ColumnSpec spec, out Column column)
    {
        var options = OptionsFor(company, spec);
        var report = BalanceSheet.Build(company, options.AsOfDate, options);
        column = new Column(
            spec.Label,
            TotalLiabilities: report.TotalLiabilities,
            TotalAssets: report.TotalAssets);

        var cells = new List<Cell>(report.Liabilities.Count + report.Assets.Count);
        // Liabilities positive, assets negative — a signed column keeps the two sides distinct while a shared
        // key (name) lines a head up across columns even when its side does not change.
        foreach (var line in report.Liabilities)
            cells.Add(new Cell(Key: "L:" + line.Name, Label: line.Name, GroupName: line.GroupName, Value: line.Amount));
        foreach (var line in report.Assets)
            cells.Add(new Cell(Key: "A:" + line.Name, Label: line.Name, GroupName: line.GroupName, Value: -line.Amount));
        return cells;
    }

    /// <summary>
    /// Whether <paramref name="kind"/> can compose a <b>scenario</b> column, as distinct from a period column.
    ///
    /// <para>🔴 <b>This is a refusal, not an omission, and it exists to stop a silently-wrong column.</b>
    /// <see cref="Reports.CashFlow"/> and <see cref="Reports.FundsFlow"/> are scenario-blind <i>by construction</i>:
    /// both are built from <c>LedgerBalances.SignedMovement</c> / the no-scenario <c>SignedClosing</c> overload, and
    /// <c>SignedMovement</c> has no scenario overload at all. Composing a "scenario" column for either would
    /// therefore run the <b>actual</b> books and emit a column byte-identical to the base one under a scenario's
    /// name — a figure that looks like a what-if answer and is not. <see cref="Build"/> rejects such a spec rather
    /// than returning it, so the failure is loud at the engine boundary instead of plausible on screen.</para>
    ///
    /// <para>The vendor documents only <b>period</b> auto-columns for these two reports ("select the required period
    /// in the Column Details field"; "add periodic columns like monthly, quarterly, weekly"), so nothing attested is
    /// lost by the refusal. Lifting it means plumbing a scenario through both statements first.</para>
    /// </summary>
    public static bool SupportsScenarioColumns(ComparativeReportKind kind)
        => kind is not (ComparativeReportKind.CashFlow or ComparativeReportKind.FundsFlow);

    /// <summary>
    /// Composes a Cash-Flow column over the spec's window. Cells are keyed by the <b>counterparty ledger name</b>
    /// with a SIGNED value: positive = cash released (an inflow), negative = cash absorbed (an outflow).
    ///
    /// <para>🔴 <b>Keyed by ledger, NOT by ledger-and-side, and that is deliberate.</b> A counterparty routinely
    /// flips side between periods — a debtor that paid down in April (inflow) and grew in May (outflow) is ONE
    /// ledger. Keying by side would split it into two rows, each blank in the other's column, which duplicates the
    /// line and drops the comparison the column was added to make. The sign carries the side instead.</para>
    /// </summary>
    private static IReadOnlyList<Cell> BuildCashFlow(Company company, ColumnSpec spec, out Column column)
    {
        var (from, to) = FlowWindow(company, spec);
        var report = CashFlow.Build(company, new PeriodRange(from, to));
        column = new Column(
            spec.Label,
            CashOpening: report.OpeningBalance,
            CashNetFlow: report.NetCashFlow,
            CashClosing: report.ClosingBalance);

        var cells = new List<Cell>(report.Inflows.Count + report.Outflows.Count);
        foreach (var line in report.Inflows)
            cells.Add(new Cell(Key: line.Name, Label: line.Name, GroupName: line.GroupName, Value: line.Amount));
        foreach (var line in report.Outflows)
            cells.Add(new Cell(Key: line.Name, Label: line.Name, GroupName: line.GroupName, Value: -line.Amount));
        return cells;
    }

    /// <summary>
    /// Composes a Funds-Flow column over the spec's window. Cells are keyed by the <b>balance-sheet ledger name</b>
    /// with a SIGNED value: positive = a source of funds, negative = an application of funds.
    ///
    /// <para>🔴 <b>The synthetic operations line is emitted ONCE, under one stable key.</b> The single-column
    /// statement folds the period result in as "Funds From Operations" (a source) or "Funds Lost In Operations" (an
    /// application) — two captions for one quantity, on opposite sides. Carried through verbatim, a book that
    /// profits in April and loses in May would produce TWO comparative rows, each blank where the other has a
    /// figure, and the reader would see no operations line at all in half the columns. It is instead emitted from
    /// the statement's own <see cref="Reports.FundsFlow.FundsFromOperations"/> under one key, signed; the folded
    /// line is skipped in the loop below by its known position rather than by matching its caption text.</para>
    /// </summary>
    private static IReadOnlyList<Cell> BuildFundsFlow(Company company, ColumnSpec spec, out Column column)
    {
        var (from, to) = FlowWindow(company, spec);
        var report = FundsFlow.Build(company, new PeriodRange(from, to));
        column = new Column(
            spec.Label,
            TotalSources: report.TotalSources,
            TotalApplications: report.TotalApplications);

        var cells = new List<Cell>(report.Sources.Count + report.Applications.Count);

        // FundsFlow.Build INSERTS the folded operations line at index 0 of whichever side it lands on, and nowhere
        // else; its sign says which side that was. Skipping by that position is exact and cannot drift with the
        // caption, whereas matching "Funds From Operations" as a string would break the moment the caption changes.
        var ops = report.FundsFromOperations.Amount;
        var skipSource = ops > 0m ? 0 : -1;
        var skipApplication = ops < 0m ? 0 : -1;

        for (var i = 0; i < report.Sources.Count; i++)
        {
            if (i == skipSource) continue;
            var line = report.Sources[i];
            cells.Add(new Cell(Key: line.Name, Label: line.Name, GroupName: line.GroupName, Value: line.Amount));
        }
        for (var i = 0; i < report.Applications.Count; i++)
        {
            if (i == skipApplication) continue;
            var line = report.Applications[i];
            cells.Add(new Cell(Key: line.Name, Label: line.Name, GroupName: line.GroupName, Value: -line.Amount));
        }

        // One signed operations row, always under the same key so it lines up across every column.
        // FundsFromOperations is defined as (applications − sources) BEFORE folding, i.e. positive = a source.
        if (ops != 0m)
        {
            cells.Add(new Cell(
                Key: OperationsRowKey,
                Label: "Funds From Operations",
                GroupName: "Profit & Loss A/c",
                Value: report.FundsFromOperations));
        }

        return cells;
    }

    /// <summary>The stable merge key for the Funds-Flow operations row (see <see cref="BuildFundsFlow"/>). Prefixed
    /// so it can never collide with a real ledger name, which is what the other keys are.</summary>
    internal const string OperationsRowKey = "\u0000ops";

    private static IReadOnlyList<Cell> BuildStockSummary(Company company, ColumnSpec spec, out Column column)
    {
        // Stock Summary is a flow report over [from, to]; its period spec drives the window directly. With no
        // explicit period it runs books-begin → the company's financial-year-anchored as-of (period end fallback).
        var (from, to) = FlowWindow(company, spec);
        var report = StockSummary.Build(company, to, from);
        column = new Column(spec.Label, StockClosingValue: report.TotalClosingValue);

        var cells = new List<Cell>(report.Rows.Count);
        foreach (var r in report.Rows)
            cells.Add(new Cell(Key: r.StockItemId.ToString(), Label: r.ItemName, GroupName: r.GroupName, Value: r.ClosingValue));
        return cells;
    }

    // ---- spec → options / window mapping -----------------------------------------------------

    /// <summary>
    /// Maps a <see cref="ColumnSpec"/> to <see cref="ReportOptions"/>.
    /// <para>When <see cref="ColumnSpec.Options"/> is supplied the column STARTS FROM those options — preserving the
    /// caller's <c>AsOfDate</c>, <c>Detailed</c>, <c>HideZeroBalances</c>, <c>ShowPercentages</c>, <c>ClosingStock</c>
    /// and <c>Scenario</c> — and only overlays <see cref="ColumnSpec.Period"/> / <see cref="ColumnSpec.Scenario"/>
    /// when those are explicitly set on the spec. A spec whose <c>Options</c> carries an explicit as-of and a null
    /// period therefore builds AS-OF that date (NOT the financial-year-end fallback), so a base column reproduces the
    /// exact single-column report beside it.</para>
    /// <para>When <see cref="ColumnSpec.Options"/> is <c>null</c> the legacy behaviour applies exactly: the period (if
    /// any) sets the flow window and the as-of; otherwise the as-of falls back to the company's financial-year end
    /// (the natural "current" close), which for a whole-year spec equals the single-column default.</para>
    /// </summary>
    private static ReportOptions OptionsFor(Company company, ColumnSpec spec)
    {
        if (spec.Options is { } baseOptions)
        {
            // Start from the caller's full options; overlay only what the spec explicitly sets.
            var options = baseOptions;
            if (spec.Period is { } specPeriod) options = options.WithPeriod(specPeriod);
            if (spec.Scenario is { } specScenario) options = options.WithScenario(specScenario);
            return options;
        }

        var legacy = spec.Period is { } p
            ? ReportOptions.ForPeriod(p)
            : ReportOptions.AsOf(DefaultAsOf(company));
        return legacy.WithScenario(spec.Scenario);
    }

    /// <summary>
    /// The flow window <c>[From, To]</c> a spec resolves to, for the kinds whose builder takes a window rather than
    /// a full <see cref="ReportOptions"/> (Stock Summary, Cash Flow, Funds Flow). Shared so all three bucket a
    /// period identically.
    /// </summary>
    private static (DateOnly From, DateOnly To) FlowWindow(Company company, ColumnSpec spec)
    {
        if (spec.Period is { } p) return (p.From, p.To);
        // A carried Options set drives the window off its own effective period (books-begin → its as-of), so a
        // Stock Summary column honours the caller's as-of just like the accounting kinds. No Options → FY-end fallback.
        if (spec.Options is { } o)
        {
            var window = o.EffectivePeriod(company);
            return (window.From, window.To);
        }
        return (company.BooksBeginFrom, DefaultAsOf(company));
    }

    /// <summary>The natural as-of when a spec omits a period: the financial-year end (a full year from FY start).</summary>
    private static DateOnly DefaultAsOf(Company company)
        => company.FinancialYearStart.AddYears(1).AddDays(-1);

    // ---- monthly auto-column axis (Alt+N) ----------------------------------------------------

    /// <summary>
    /// Splits <paramref name="period"/> into one column per calendar month it spans (Alt+N monthly auto-columns).
    /// Each column's window is <c>[month-start, month-end]</c> clamped to the overall period, so the first/last
    /// partial months honour the period bounds. Labels are <c>"MMM yyyy"</c> (invariant). The union of the monthly
    /// windows is exactly the input period, so — for a flow report — the monthly columns sum back to the full one.
    /// </summary>
    public static IReadOnlyList<ColumnSpec> MonthlyColumns(PeriodRange period)
        => PeriodicColumns(period, ColumnPeriodicity.Monthly);

    /// <summary>
    /// Splits <paramref name="period"/> into one column per <paramref name="periodicity"/> bucket it spans
    /// (Alt+N auto-columns). Every bucket is clamped to the overall period, so the first and last buckets honour
    /// the period bounds, and the union of the buckets is exactly the input period — for a flow report the columns
    /// therefore sum back to the full one. Labels are culture-free.
    ///
    /// <para><b>Grounding.</b> The periodicity list is the vendor's own, opened by content 2026-10-20:
    /// <i>"add periodic columns like monthly, quarterly, weekly, and so on"</i>
    /// (<c>help.tallysolutions.com/funds-flow-report-tally/</c>) and <i>"you can configure the columns to appear
    /// quarterly, weekly, daily, fortnightly, and so on"</i>
    /// (<c>help.tallysolutions.com/cash-flow-and-projection-report-tally/</c>). Yearly is added for a multi-year
    /// window; both pages end their list with "and so on" and neither enumerates exhaustively.</para>
    ///
    /// <para>🔴 <b>Bucket anchoring is OURS where the calendar does not settle it (divergence IV-107,
    /// <c>docs/invented-vs-cloned.md</c> §2I — PROVISIONAL, see that section's header).</b> No page we
    /// have opened states where a bucket starts. Months and quarters are anchored to the CALENDAR (and for the
    /// standard Indian financial year beginning 1 April the four calendar quarters and the four FY quarters
    /// coincide exactly, so the anchor is not visible there). Days, weeks, fortnights and years have no calendar
    /// anchor to borrow — a "week" has no vendor-stated start day — so those are fixed-length blocks measured from
    /// the period's own first day. Monthly's behaviour is byte-for-byte what it was before this method existed.</para>
    /// </summary>
    public static IReadOnlyList<ColumnSpec> PeriodicColumns(PeriodRange period, ColumnPeriodicity periodicity)
    {
        var specs = new List<ColumnSpec>();
        if (!period.IsValid) return specs;

        return periodicity switch
        {
            ColumnPeriodicity.Daily => FixedBlocks(period, days: 1),
            ColumnPeriodicity.Weekly => FixedBlocks(period, days: 7),
            ColumnPeriodicity.Fortnightly => FixedBlocks(period, days: 14),
            ColumnPeriodicity.Monthly => CalendarMonthBlocks(period),
            ColumnPeriodicity.Quarterly => CalendarQuarterBlocks(period),
            ColumnPeriodicity.Yearly => YearBlocks(period),
            _ => throw new ArgumentOutOfRangeException(
                nameof(periodicity), periodicity, "Unknown column periodicity."),
        };
    }

    /// <summary>Calendar months clipped to the period — the original <see cref="MonthlyColumns"/> behaviour.</summary>
    private static List<ColumnSpec> CalendarMonthBlocks(PeriodRange period)
    {
        var specs = new List<ColumnSpec>();
        var cursor = new DateOnly(period.From.Year, period.From.Month, 1);
        while (cursor <= period.To)
        {
            var monthStart = cursor;
            var monthEnd = cursor.AddMonths(1).AddDays(-1);
            var from = monthStart < period.From ? period.From : monthStart;
            var to = monthEnd > period.To ? period.To : monthEnd;
            var label = monthStart.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            specs.Add(new ColumnSpec(label, new PeriodRange(from, to), Scenario: null));
            cursor = cursor.AddMonths(1);
        }
        return specs;
    }

    /// <summary>Calendar quarters (Jan–Mar, Apr–Jun, Jul–Sep, Oct–Dec) clipped to the period.</summary>
    private static List<ColumnSpec> CalendarQuarterBlocks(PeriodRange period)
    {
        var specs = new List<ColumnSpec>();
        var firstMonthOfQuarter = ((period.From.Month - 1) / 3) * 3 + 1;
        var cursor = new DateOnly(period.From.Year, firstMonthOfQuarter, 1);
        while (cursor <= period.To)
        {
            var quarterEnd = cursor.AddMonths(3).AddDays(-1);
            var from = cursor < period.From ? period.From : cursor;
            var to = quarterEnd > period.To ? period.To : quarterEnd;
            var label = $"Q{(cursor.Month - 1) / 3 + 1} {cursor.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            specs.Add(new ColumnSpec(label, new PeriodRange(from, to), Scenario: null));
            cursor = cursor.AddMonths(3);
        }
        return specs;
    }

    /// <summary>Successive 12-month blocks measured from the period's first day (a financial year is not a
    /// calendar year, so this anchor follows the window the operator chose).</summary>
    private static List<ColumnSpec> YearBlocks(PeriodRange period)
    {
        var specs = new List<ColumnSpec>();
        var cursor = period.From;
        while (cursor <= period.To)
        {
            var blockEnd = cursor.AddYears(1).AddDays(-1);
            var to = blockEnd > period.To ? period.To : blockEnd;
            var label = $"{Iso(cursor)}–{Iso(to)}";
            specs.Add(new ColumnSpec(label, new PeriodRange(cursor, to), Scenario: null));
            cursor = blockEnd.AddDays(1);
        }
        return specs;
    }

    /// <summary>Successive fixed-length day blocks measured from the period's first day, the last one clipped.</summary>
    private static List<ColumnSpec> FixedBlocks(PeriodRange period, int days)
    {
        var specs = new List<ColumnSpec>();
        var cursor = period.From;
        while (cursor <= period.To)
        {
            var blockEnd = cursor.AddDays(days - 1);
            var to = blockEnd > period.To ? period.To : blockEnd;
            var label = days == 1 ? Iso(cursor) : $"{Iso(cursor)}–{Iso(to)}";
            specs.Add(new ColumnSpec(label, new PeriodRange(cursor, to), Scenario: null));
            cursor = blockEnd.AddDays(1);
        }
        return specs;
    }

    /// <summary>Culture-free <c>dd-MMM-yyyy</c> for a bucket label, so a test comparing labels never moves with
    /// the machine locale.</summary>
    private static string Iso(DateOnly d)
        => d.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// One column per scenario defined on the company, over a shared <paramref name="period"/> (Alt+N scenario
    /// auto-columns). When <paramref name="includeActualColumn"/> is <c>true</c> a leading "Actual" column
    /// (no scenario) is prepended so the what-if columns read against the real books. Labels are the scenario names.
    /// </summary>
    public static IReadOnlyList<ColumnSpec> ScenarioColumns(
        Company company, PeriodRange period, bool includeActualColumn = true)
    {
        var specs = new List<ColumnSpec>();
        if (includeActualColumn)
            specs.Add(new ColumnSpec("Actual", period, Scenario: null));
        foreach (var scenario in company.Scenarios)
            specs.Add(new ColumnSpec(scenario.Name, period, scenario));
        return specs;
    }

    // ---- internal accumulation types ---------------------------------------------------------

    /// <summary>A single builder output cell: its stable merge key, display label, group and signed value.</summary>
    private readonly record struct Cell(string Key, string Label, string? GroupName, Money Value);

    private sealed class RowAccumulator
    {
        public string Key { get; }
        public string Label { get; }
        public string? GroupName { get; set; }
        public Money?[] Values { get; }

        public RowAccumulator(string key, string label, string? groupName, int columnCount)
        {
            Key = key;
            Label = label;
            GroupName = groupName;
            Values = new Money?[columnCount];
        }
    }
}

/// <summary>
/// One comparative row (RQ-4): a line identity (<see cref="Key"/>) with a display <see cref="Label"/> and an
/// optional <see cref="GroupName"/>, carrying one value per column aligned to the comparative's column order.
/// A <c>null</c> value marks a column where this key is <b>absent</b> (a blank cell) — distinct from a genuine
/// zero. Values are SIGNED (Trial Balance: +Dr / −Cr; P&amp;L: +income / −expense; Balance Sheet: +liability /
/// −asset; Stock Summary: closing value), so a column reads unambiguously and the UI can format each side.
/// </summary>
public sealed record ComparativeRow(
    string Key,
    string Label,
    string? GroupName,
    IReadOnlyList<Money?> Values);
