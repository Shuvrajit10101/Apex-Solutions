using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The Phase-5 slice-1 report parameter object (RQ-1 period/as-of, RQ-2 detailed↔summary,
/// RQ-6 F12 configuration). It is a pure, immutable value carried into a report build; the
/// engine reads it and never mutates it or persists it. Defaults reproduce the pre-slice
/// behaviour exactly: <see cref="Detailed"/> = ledger-level, no rows hidden, no percentages,
/// closing stock <see cref="ClosingStockMode.AsPostedLedger"/>.
/// </summary>
/// <remarks>
/// <para><b>As-of vs period.</b> <see cref="AsOfDate"/> is always set (a balance is a point-in-time
/// figure). <see cref="Period"/> is optional: when set (Alt+F2 in the UI) flow reports use its
/// <c>[From,To]</c> window; when null they run from the books-begin date to <see cref="AsOfDate"/>,
/// which for a full year equals the legacy behaviour.</para>
/// </remarks>
public sealed record ReportOptions
{
    /// <summary>The as-of date for balance reports and the upper bound of the default flow window (RQ-1).</summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>The explicit <c>[From,To]</c> window (RQ-1, Alt+F2), or <c>null</c> for the default window.</summary>
    public PeriodRange? Period { get; init; }

    /// <summary>Detailed (ledger/item-level) when true; group-level roll-up (summary) when false (RQ-2, Alt+F1).</summary>
    public bool Detailed { get; init; } = true;

    /// <summary>Hide rows whose balance is exactly zero (RQ-6 F12).</summary>
    public bool HideZeroBalances { get; init; }

    /// <summary>Show each row's percentage of its section/column total (RQ-6 F12).</summary>
    public bool ShowPercentages { get; init; }

    /// <summary>Closing-stock valuation basis passed through to the P&amp;L / Balance-Sheet build (RQ-6 F12).</summary>
    public ClosingStockMode ClosingStock { get; init; } = ClosingStockMode.AsPostedLedger;

    /// <summary>The scenario the figures are computed under, or <c>null</c> for the actual books.</summary>
    public Scenario? Scenario { get; init; }

    /// <summary>Builds options for a plain as-of report (the common case, default config).</summary>
    public static ReportOptions AsOf(DateOnly asOf) => new() { AsOfDate = asOf };

    /// <summary>Builds options for an explicit period window; the as-of defaults to the window end.</summary>
    public static ReportOptions ForPeriod(PeriodRange period) =>
        new() { AsOfDate = period.To, Period = period };

    // ---- Fluent "with" helpers (records are immutable; these return copies) ----

    public ReportOptions WithDetailed(bool detailed) => this with { Detailed = detailed };
    public ReportOptions WithHideZeroBalances(bool hide) => this with { HideZeroBalances = hide };
    public ReportOptions WithShowPercentages(bool show) => this with { ShowPercentages = show };
    public ReportOptions WithClosingStock(ClosingStockMode mode) => this with { ClosingStock = mode };
    public ReportOptions WithPeriod(PeriodRange period) => this with { Period = period, AsOfDate = period.To };
    public ReportOptions WithScenario(Scenario? scenario) => this with { Scenario = scenario };

    /// <summary>The effective flow window: the explicit <see cref="Period"/>, or books-begin → <see cref="AsOfDate"/>.</summary>
    public PeriodRange EffectivePeriod(Company company) =>
        Period ?? new PeriodRange(company.BooksBeginFrom, AsOfDate);
}
