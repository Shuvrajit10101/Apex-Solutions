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
}

/// <summary>
/// A comparative / columnar report (RQ-4): the SAME report (Balance Sheet, P&amp;L, Trial Balance or Stock
/// Summary) rendered across MULTIPLE columns, each a different <b>period</b> and/or <b>scenario</b>. Rows are
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
    /// <see cref="TotalExpenses"/>/<see cref="NetProfit"/>; Stock Summary → <see cref="StockClosingValue"/>.
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
        Money StockClosingValue = default);

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

    private static IReadOnlyList<Cell> BuildStockSummary(Company company, ColumnSpec spec, out Column column)
    {
        // Stock Summary is a flow report over [from, to]; its period spec drives the window directly. With no
        // explicit period it runs books-begin → the company's financial-year-anchored as-of (period end fallback).
        var (from, to) = StockWindow(company, spec);
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

    private static (DateOnly From, DateOnly To) StockWindow(Company company, ColumnSpec spec)
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
