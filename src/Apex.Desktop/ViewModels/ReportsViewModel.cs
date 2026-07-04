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

/// <summary>The four Phase-1 report kinds surfaced in the reports viewer.</summary>
public enum ReportKind
{
    TrialBalance,
    BalanceSheet,
    ProfitAndLoss,
    DayBook,
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
    private readonly DateOnly _asOf;

    [ObservableProperty] private ReportKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _isTwoColumn; // Dr/Cr grid (TB, BS) vs single-amount (P&L, DayBook)

    public ObservableCollection<ReportRow> Rows { get; } = new();

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

    public ReportsViewModel(Company company, ReportKind kind)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _asOf = ComputeAsOf(company);

        Scenarios.Add(ScenarioOption.Actual);
        foreach (var s in company.Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Scenarios.Add(new ScenarioOption(s));
        _selectedScenario = Scenarios[0];

        Show(kind);
    }

    /// <summary>Rebuilds the current report whenever the scenario picker changes.</summary>
    partial void OnSelectedScenarioChanged(ScenarioOption? value) => Show(Kind);

    /// <summary>Switches the displayed report and rebuilds its rows (under the selected scenario, if any).</summary>
    public void Show(ReportKind kind)
    {
        Kind = kind;
        OnPropertyChanged(nameof(SupportsScenario));
        Rows.Clear();

        switch (kind)
        {
            case ReportKind.TrialBalance: BuildTrialBalance(); break;
            case ReportKind.BalanceSheet: BuildBalanceSheet(); break;
            case ReportKind.ProfitAndLoss: BuildProfitAndLoss(); break;
            case ReportKind.DayBook: BuildDayBook(); break;
        }
    }

    /// <summary>" under scenario <name>" suffix for the subtitle, or empty when showing the actual books.</summary>
    private string ScenarioSuffix =>
        CurrentScenario is { } s ? $"  —  under scenario “{s.Name}”" : string.Empty;

    // --------------------------------------------------------------- Trial Balance

    private void BuildTrialBalance()
    {
        var tb = TrialBalance.Build(_company, _asOf, CurrentScenario);
        Title = "Trial Balance";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = true;

        foreach (var row in tb.Rows.OrderBy(r => r.GroupName).ThenBy(r => r.LedgerName))
            Rows.Add(ReportRow.DrCrLine(row.LedgerName, row.Debit, row.Credit, row.GroupName));

        Rows.Add(ReportRow.DrCrTotal("Grand Total", tb.TotalDebit, tb.TotalCredit));
    }

    // --------------------------------------------------------------- Balance Sheet

    private void BuildBalanceSheet()
    {
        var bs = BalanceSheet.Build(_company, _asOf, scenario: CurrentScenario);
        Title = "Balance Sheet";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Liabilities", IsHeader = true });
        foreach (var l in bs.Liabilities.Where(l => l.Amount != Money.Zero))
            Rows.Add(ReportRow.Line(l.Name, l.Amount, l.GroupName));
        Rows.Add(ReportRow.Total("Total Liabilities", bs.TotalLiabilities));

        Rows.Add(new ReportRow { Particulars = "Assets", IsHeader = true });
        foreach (var a in bs.Assets.Where(a => a.Amount != Money.Zero))
            Rows.Add(ReportRow.Line(a.Name, a.Amount, a.GroupName));
        Rows.Add(ReportRow.Total("Total Assets", bs.TotalAssets));
    }

    // --------------------------------------------------------------- Profit & Loss

    private void BuildProfitAndLoss()
    {
        var pl = ProfitAndLoss.Build(_company, _asOf, ClosingStockMode.AsPostedLedger, CurrentScenario);
        Title = "Profit & Loss A/c";
        Subtitle = $"{CompanyName}  —  for the period ending {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Income", IsHeader = true });
        foreach (var i in pl.Income)
            Rows.Add(ReportRow.Line(i.LedgerName, i.Amount));
        Rows.Add(ReportRow.Total("Total Income", pl.TotalIncome));

        Rows.Add(new ReportRow { Particulars = "Expenses", IsHeader = true });
        foreach (var e in pl.Expenses)
            Rows.Add(ReportRow.Line(e.LedgerName, e.Amount));
        Rows.Add(ReportRow.Total("Total Expenses", pl.TotalExpenses));

        var isProfit = pl.NetProfit.Amount >= 0m;
        var label = isProfit ? "Net Profit" : "Net Loss";
        var magnitude = new Money(Math.Abs(pl.NetProfit.Amount));
        Rows.Add(ReportRow.Total(label, magnitude));
    }

    // --------------------------------------------------------------- Day Book

    private void BuildDayBook()
    {
        var from = _company.BooksBeginFrom;
        var rows = DayBook.Build(_company, from, _asOf);
        Title = "Day Book";
        Subtitle = $"{CompanyName}  —  {FormatDate(from)} to {FormatDate(_asOf)}";
        IsTwoColumn = false;

        foreach (var r in rows)
        {
            var particulars = $"{r.VoucherTypeName} No. {r.Number}";
            var secondary = r.PartyOrParticulars ?? string.Empty;
            var amt = IndianFormat.Amount(r.Amount);
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  {particulars}",
                Secondary = r.IsCancelled ? "(Cancelled) " + secondary : secondary,
                Amount = amt,
            });
        }

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No vouchers in this period.", IsHeader = true });
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
