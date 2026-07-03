using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One row of the Budget Variance report grid: a target with its budgeted figure, the actual it reached
/// over the budget's period, and the variance. <see cref="IsOver"/>/<see cref="IsUnder"/> drive the
/// variance colour (over budget = red, under budget = green). <see cref="IsTotal"/> bolds the grand total.
/// </summary>
public sealed class BudgetVarianceRow
{
    public string Target { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Budget { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Variance { get; init; } = string.Empty;

    /// <summary>Percentage variance text ("+12.5%", "−4.0%", or "—" when the budget is zero).</summary>
    public string VariancePercent { get; init; } = string.Empty;

    /// <summary>True when the actual exceeds the budget (over budget — variance shown red).</summary>
    public bool IsOver { get; init; }

    /// <summary>True when the actual is below the budget (under budget — variance shown green).</summary>
    public bool IsUnder { get; init; }

    public bool IsTotal { get; init; }
}

/// <summary>
/// The Budget Variance report ("Reports → Statements of Accounts → Budgets → Budget Variance", catalog §7):
/// pick one of the company's budgets and see, for each of its lines, the <b>Budget / Actual / Variance</b>
/// (with a variance % and over/under colouring), plus a grand total. The numbers come straight from the
/// pure <see cref="BudgetVarianceReport"/> projection over the posted vouchers. No Avalonia types ⇒
/// headlessly testable; mirrors <see cref="CostReportsViewModel"/>.
/// </summary>
public sealed partial class BudgetVarianceViewModel : ViewModelBase
{
    private const string DateFormat = "dd-MMM-yyyy";

    private readonly Company _company;

    /// <summary>The budgets the picker offers (name-sorted). Empty ⇒ the "no budgets" hint is shown.</summary>
    public ObservableCollection<Budget> Budgets { get; } = new();

    /// <summary>The report rows for the selected budget (Budget/Actual/Variance per line + grand total).</summary>
    public ObservableCollection<BudgetVarianceRow> Rows { get; } = new();

    [ObservableProperty] private Budget? _selectedBudget;
    [ObservableProperty] private string _title = "Budget Variance";
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>True when the company has no budgets yet (drives an empty-state hint in the view).</summary>
    public bool HasNoBudgets => Budgets.Count == 0;

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    public BudgetVarianceViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var b in company.Budgets.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            Budgets.Add(b);

        SelectedBudget = Budgets.FirstOrDefault();
        Rebuild();
    }

    /// <summary>Rebuilds the grid whenever a different budget is chosen.</summary>
    partial void OnSelectedBudgetChanged(Budget? value) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();

        if (SelectedBudget is null)
        {
            Title = "Budget Variance";
            Subtitle = $"{CompanyName}  —  no budgets defined. Create one under Masters → Create → Budget.";
            OnPropertyChanged(nameof(HasNoBudgets));
            return;
        }

        var report = BudgetVarianceReport.Build(_company, SelectedBudget);
        Title = $"Budget Variance — {report.BudgetName}";
        Subtitle = $"{CompanyName}  —  " +
                   $"{report.PeriodFrom.ToString(DateFormat, CultureInfo.InvariantCulture)} to " +
                   $"{report.PeriodTo.ToString(DateFormat, CultureInfo.InvariantCulture)}";

        decimal totalBudget = 0m, totalActual = 0m;
        foreach (var line in report.Lines)
        {
            totalBudget += line.Budget.Amount;
            totalActual += line.Actual.Amount;
            Rows.Add(BuildRow(
                $"{line.TargetName}  ({(line.IsGroup ? "Group" : "Ledger")})",
                BudgetMasterViewModel.TypeLabel(line.Type),
                line.Budget.Amount, line.Actual.Amount, line.Variance.Amount,
                line.VariancePercent, isTotal: false));
        }

        if (report.Lines.Count == 0)
        {
            Rows.Add(new BudgetVarianceRow { Target = "This budget has no lines.", IsTotal = false });
        }
        else
        {
            var totalVar = totalActual - totalBudget;
            decimal? totalPct = totalBudget == 0m ? null : totalVar / totalBudget * 100m;
            Rows.Add(BuildRow("Grand Total", string.Empty,
                totalBudget, totalActual, totalVar, totalPct, isTotal: true));
        }

        OnPropertyChanged(nameof(HasNoBudgets));
    }

    private static BudgetVarianceRow BuildRow(
        string target, string type,
        decimal budget, decimal actual, decimal variance, decimal? variancePercent, bool isTotal) =>
        new()
        {
            Target = target,
            Type = type,
            Budget = IndianFormat.AmountAlways(budget),
            Actual = IndianFormat.AmountAlways(actual),
            // Signed variance so over/under reads at a glance (e.g. "+6,000.00" / "−1,000.00").
            Variance = SignedAmount(variance),
            VariancePercent = variancePercent is { } p ? SignedPercent(p) : "—",
            IsOver = variance > 0m,
            IsUnder = variance < 0m,
            IsTotal = isTotal,
        };

    private static string SignedAmount(decimal value)
    {
        var magnitude = IndianFormat.AmountAlways(Math.Abs(value));
        return value > 0m ? $"+{magnitude}" : value < 0m ? $"−{magnitude}" : magnitude;
    }

    private static string SignedPercent(decimal pct)
    {
        var magnitude = Math.Abs(pct).ToString("0.0", CultureInfo.InvariantCulture);
        return pct > 0m ? $"+{magnitude}%" : pct < 0m ? $"−{magnitude}%" : $"{magnitude}%";
    }
}
