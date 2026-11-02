using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The <b>F6 ageing criteria</b> panel (census 11.13, advancing 11.9 / T2-24; IV-315..IV-318) — the step after the
/// ageing basis on Bills Receivable / Payable and Overdue Receivables / Payables, hosted as its own cascading page
/// column to the right of the report, exactly like the Ctrl+B <see cref="BasisOfValuesViewModel"/>.
///
/// <para><b>Fidelity (ruling 14).</b> help.tallysolutions.com/tally-prime/analysis-verification/outstandings-tally/
/// (opened by content 2026-11-02), Receivables - Age Wise: <i>"Select the appropriate Ageing Methods from Ageing by
/// Bill Date or Ageing by Due Date and press Enter."</i> then <i>"Specify the required ageing criteria and press
/// Enter."</i>; help.tallysolutions.com/manage-receivables-outstanding-tally/ (same day): <i>"Press F6 (Ageing
/// Method) to configure the report view as per ageing by bill dates or by due dates and set the ageing
/// windows."</i> Neither page spells the criteria's shape out. The From / To day rows are the vendor's own F6 shape
/// on Stock Ageing Analysis (help.tallysolutions.com/stock-ageing-analysis-report-tally/, same day: <i>"Enter the
/// From and To days."</i>), borrowed here and labelled as ours (IV-316).</para>
///
/// <para>The panel only collects and parses. The rules live in the engine (<c>Outstandings.TryWindowsFor</c>), and
/// the report re-projects through the ONE <c>Outstandings.Build</c> call, so the panel cannot age a bill itself.</para>
/// </summary>
public sealed partial class AgeingCriteriaViewModel : ViewModelBase
{
    /// <summary>How many From / To rows the panel offers (IV-316, ours).</summary>
    public const int RowCount = 6;

    private readonly ReportsViewModel _report;

    /// <summary>The basis the operator just chose on the F6 picker.</summary>
    public AgeingMethod Method { get; }

    /// <summary>The column title / heading.</summary>
    public string Title => "Ageing Criteria — F6";

    /// <summary>The report this panel configures and the basis chosen, for the heading line.</summary>
    public string ReportTitle => $"{_report.Title}  —  {ReportsViewModel.AgeingMethodLabel(Method)}";

    /// <summary>What the days count, in the operator's terms.</summary>
    public string Hint => Method == AgeingMethod.ByBillDate
        ? "Days since each bill's date. Period 1 starts from 0. Leave the last To blank for an open-ended period."
        : "Days past each bill's due date. Bills not yet overdue are shown as Not due, so period 1 starts from 1. "
          + "Leave the last To blank for an open-ended period.";

    /// <summary>The From / To rows; blank rows are ignored.</summary>
    public ObservableCollection<AgeingPeriodRow> Rows { get; } = new();

    /// <summary>The refusal shown when the periods are not accepted.</summary>
    [ObservableProperty] private string _status = string.Empty;

    public AgeingCriteriaViewModel(ReportsViewModel report, AgeingMethod method)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Method = method;

        // Seed with the windows in force when they hold on this basis, else the basis's own default periods — so
        // Enter straight away is the report the operator would get without the criteria step.
        var seed = report.AgeingPeriods is { } current && Outstandings.TryWindowsFor(method, current, out _) is not null
            ? current
            : Outstandings.DefaultPeriodsFor(method);
        for (var i = 0; i < RowCount; i++)
        {
            var row = new AgeingPeriodRow((i + 1).ToString(CultureInfo.InvariantCulture));
            if (i < seed.Count)
            {
                row.From = seed[i].From.ToString(CultureInfo.InvariantCulture);
                row.To = seed[i].To?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            }
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Enter / Ctrl+A — parses the rows and hands basis + periods to the report. Returns false (and sets
    /// <see cref="Status"/>) when a row does not parse or the engine refuses the periods; the report is then left
    /// exactly as it was.
    /// </summary>
    public bool Accept()
    {
        var periods = new List<AgeingPeriod>();
        foreach (var row in Rows)
        {
            var from = (row.From ?? string.Empty).Trim();
            var to = (row.To ?? string.Empty).Trim();
            if (from.Length == 0 && to.Length == 0) continue;
            if (!int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var f))
            {
                Status = $"Period {row.Number}: From must be a whole number of days.";
                return false;
            }
            int? t = null;
            if (to.Length > 0)
            {
                if (!int.TryParse(to, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    Status = $"Period {row.Number}: To must be a whole number of days, or blank for open-ended.";
                    return false;
                }
                t = parsed;
            }
            periods.Add(new AgeingPeriod(f, t));
        }

        var error = _report.SetAgeingCriteria(Method, periods);
        if (error.Length > 0)
        {
            Status = error;
            return false;
        }
        Status = string.Empty;
        return true;
    }
}

/// <summary>One From / To row of the F6 ageing criteria panel.</summary>
public sealed partial class AgeingPeriodRow : ObservableObject
{
    public AgeingPeriodRow(string number) => Number = number;

    /// <summary>The 1-based row number shown beside the boxes.</summary>
    public string Number { get; }

    [ObservableProperty] private string _from = string.Empty;

    [ObservableProperty] private string _to = string.Empty;
}
