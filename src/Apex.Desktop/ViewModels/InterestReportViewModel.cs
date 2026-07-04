using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One presentation row of the Interest Calculation report (catalog §7): an interest-enabled ledger's
/// accrued interest over the period — principal, rate, day-count and the (right-aligned) interest amount.
/// A ledger with a PostDue block contributes one row per open bill (its <see cref="BillReference"/> shows).
/// <see cref="IsTotal"/> marks the grand-total footer row.
/// </summary>
public sealed class InterestReportRow
{
    public string Ledger { get; init; } = string.Empty;

    /// <summary>The bill reference for a PostDue per-bill row (blank for a whole-balance row).</summary>
    public string Reference { get; init; } = string.Empty;

    public string Principal { get; init; } = string.Empty;
    public string Rate { get; init; } = string.Empty;
    public string Days { get; init; } = string.Empty;

    /// <summary>The accrued interest, formatted right-aligned in the grid.</summary>
    public string Interest { get; init; } = string.Empty;

    public bool IsTotal { get; init; }
}

/// <summary>
/// The Interest Calculation report page (Reports → Statements of Accounts → Interest Calculation;
/// catalog §7). Builds the pure <see cref="InterestCalculation"/> projection over the company's period
/// (books-begin → the last voucher date, or the financial-year end when there are no vouchers) and exposes
/// one <see cref="InterestReportRow"/> per interest-bearing balance/bill plus a total row. UI-toolkit-free
/// so it is headlessly testable; no DB, no engine changes — a projection over the posted vouchers.
/// </summary>
public sealed partial class InterestReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Interest Calculation";
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The report rows (one per accrued balance/bill) followed by the grand-total row.</summary>
    public ObservableCollection<InterestReportRow> Rows { get; } = new();

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    public InterestReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var from = company.BooksBeginFrom;
        var to = ComputeAsOf(company);
        if (to < from) to = from;

        var report = InterestCalculation.Build(company, from, to);

        Subtitle = $"{CompanyName}  —  {FormatDate(from)} to {FormatDate(to)}";

        foreach (var line in report.Lines)
        {
            Rows.Add(new InterestReportRow
            {
                Ledger = line.LedgerName,
                Reference = line.BillReference ?? string.Empty,
                Principal = $"{IndianFormat.Amount(line.Principal)} {(line.PrincipalIsDebit ? "Dr" : "Cr")}",
                Rate = $"{line.RatePercent:0.##}%",
                Days = line.Days.ToString(),
                Interest = IndianFormat.Amount(line.Interest),
            });
        }

        if (report.Lines.Count == 0)
        {
            Rows.Add(new InterestReportRow
            {
                Ledger = "No interest-enabled ledgers with an accruing balance in this period.",
            });
        }

        Rows.Add(new InterestReportRow
        {
            Ledger = "Total Interest",
            Interest = IndianFormat.AmountAlways(report.TotalInterest),
            IsTotal = true,
        });
    }

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;

        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private static string FormatDate(DateOnly d) => d.ToString("dd-MMM-yyyy");
}
