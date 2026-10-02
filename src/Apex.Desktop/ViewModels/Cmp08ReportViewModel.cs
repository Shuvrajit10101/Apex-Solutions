using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on a composition-return screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class CompositionFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable CMP-08 quarter (its inclusive window + a "Q1 (Apr–Jun 2025)" label). CMP-08 is a quarterly
/// self-assessed statement, so the composition tax is built one quarter at a time.</summary>
public sealed class CompositionQuarterOption
{
    public int Index { get; init; }               // 0..3 within the financial year
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string Label =>
        $"Q{Index + 1} ({From.ToString("MMM", CultureInfo.InvariantCulture)}–{To.ToString("MMM yyyy", CultureInfo.InvariantCulture)})";
    public override string ToString() => Label;
}

/// <summary>
/// The <b>Form CMP-08</b> report page (Reports → Statutory Reports → Composition Returns → CMP-08; Phase 9 slice 3;
/// RQ-16; §10 + Rule 62). A read-only projection over the pure <see cref="Cmp08"/> engine: pick a financial year +
/// quarter and it shows the sub-type + tax-on-turnover rate, the turnover base, and the CMP-08 form tables —
/// 3(i) outward tax on turnover, 3(ii) inward reverse-charge tax paid in cash, and 3(iii) tax payable by head
/// (CGST/SGST/IGST + ring-fenced Cess, ER-2). Interest (3(iv)) is modelled but zero in S3 (a carry-forward).
///
/// <para>Gated: only reachable when the company is a Composition dealer (the menu item + open path check
/// <c>Gst.RegistrationType == Composition</c>), so a Regular company is byte-identical (ER-13). MVVM boundary:
/// engine only, no Avalonia types (headlessly testable); deterministic (no clock/RNG).</para>
/// </summary>
public sealed partial class Cmp08ReportViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form CMP-08 — Composition Quarterly Statement";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _subTypeText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _turnoverBaseText = "0.00";

    // Table 3(i) — outward tax on turnover.
    [ObservableProperty] private string _outwardCgstText = "0.00";
    [ObservableProperty] private string _outwardSgstText = "0.00";
    [ObservableProperty] private string _outwardTurnoverTaxText = "0.00";

    // Table 3(ii) — inward reverse-charge tax paid in cash.
    [ObservableProperty] private string _inwardRcmCgstText = "0.00";
    [ObservableProperty] private string _inwardRcmSgstText = "0.00";
    [ObservableProperty] private string _inwardRcmIgstText = "0.00";
    [ObservableProperty] private string _inwardRcmCessText = "0.00";

    // Table 3(iii) — tax payable by head.
    [ObservableProperty] private string _payableCgstText = "0.00";
    [ObservableProperty] private string _payableSgstText = "0.00";
    [ObservableProperty] private string _payableIgstText = "0.00";
    [ObservableProperty] private string _payableCessText = "0.00";
    [ObservableProperty] private string _totalTaxPayableText = "0.00";

    // Table 3(iv) — interest (§50; zero in S3).
    [ObservableProperty] private string _interestText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private CompositionFyOption? _selectedYear;
    private CompositionQuarterOption? _selectedQuarter;

    /// <summary>The financial years the statement can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<CompositionFyOption> FinancialYears { get; } = new();

    /// <summary>The four quarters of the selected financial year.</summary>
    public ObservableCollection<CompositionQuarterOption> Quarters { get; } = new();

    public Cmp08ReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new CompositionFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        RebuildQuarters();
        _selectedQuarter = Quarters.FirstOrDefault();

        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-derives the quarters and rebuilds the statement.</summary>
    public CompositionFyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildQuarters();
            _selectedQuarter = Quarters.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedQuarter));
            Rebuild();
        }
    }

    /// <summary>The selected quarter; changing it rebuilds the statement for that quarter.</summary>
    public CompositionQuarterOption? SelectedQuarter
    {
        get => _selectedQuarter;
        set { if (SetProperty(ref _selectedQuarter, value)) Rebuild(); }
    }

    /// <summary>The currently-built CMP-08 (rebuilt on selection change). Never null after construction.</summary>
    public Cmp08 Statement { get; private set; } = default!;

    /// <summary>
    /// <b>Census 6.11 — CMP-08 can now be printed and exported.</b> Implementing
    /// <see cref="IMasterListExportSource"/> gives the page E / Alt+E (CSV / XLSX / PDF / HTML / XML / JSON /
    /// ASCII) and P / Ctrl+P, which is the whole of this row's recorded gap: the projection and the screen were
    /// already right, and the offline-JSON writer behind it is reachable from the Offline Return Files picker,
    /// but the statement an operator reads here could not be taken anywhere.
    ///
    /// <para><b>The period and the sub-type lead the snapshot, and that is not decoration.</b> CMP-08 is
    /// quarterly and the rate depends on the composition sub-type, so a page of figures with neither is
    /// ambiguous the moment it leaves this screen — two quarters of the same year export to files that cannot
    /// be told apart. They ride as leading text rows rather than as a file-name convention, because the
    /// operator chooses the file name.</para>
    ///
    /// <para><b>Cess stays on its own line</b> (ER-2): it is a Central head and must not be folded into the
    /// CGST/SGST figures a composition dealer pays.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Period", Subtitle },
            new[] { "Composition sub-type", SubTypeText },
            new[] { "Tax on turnover rate", RateText },
            new[] { "Turnover base", TurnoverBaseText },

            new[] { "3(i) Outward tax on turnover — CGST", OutwardCgstText },
            new[] { "3(i) Outward tax on turnover — SGST/UTGST", OutwardSgstText },
            new[] { "3(i) Outward tax on turnover — total", OutwardTurnoverTaxText },

            new[] { "3(ii) Inward reverse charge (paid in cash) — CGST", InwardRcmCgstText },
            new[] { "3(ii) Inward reverse charge (paid in cash) — SGST/UTGST", InwardRcmSgstText },
            new[] { "3(ii) Inward reverse charge (paid in cash) — IGST", InwardRcmIgstText },
            new[] { "3(ii) Inward reverse charge (paid in cash) — Cess", InwardRcmCessText },

            new[] { "3(iii) Tax payable — CGST", PayableCgstText },
            new[] { "3(iii) Tax payable — SGST/UTGST", PayableSgstText },
            new[] { "3(iii) Tax payable — IGST", PayableIgstText },
            new[] { "3(iii) Tax payable — Cess", PayableCessText },
            new[] { "3(iii) Tax payable — total", TotalTaxPayableText },

            new[] { "3(iv) Interest", InterestText },
        };

        return new MasterListSnapshot(
            Title,
            new[] { MasterListColumn.Text("Particulars"), MasterListColumn.Number("Amount") },
            rows);
    }

    /// <summary>Re-derives the four quarter windows for the selected financial year (Apr–Jun … Jan–Mar).</summary>
    private void RebuildQuarters()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyStart = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Quarters.Clear();
        for (var i = 0; i < 4; i++)
        {
            var from = fyStart.AddMonths(3 * i);
            var to = fyStart.AddMonths(3 * (i + 1)).AddDays(-1);
            Quarters.Add(new CompositionQuarterOption { Index = i, From = from, To = to });
        }
    }

    /// <summary>(Re)builds CMP-08 for the selected FY + quarter and refreshes the form tables.</summary>
    public void Rebuild()
    {
        var quarter = SelectedQuarter ?? Quarters.FirstOrDefault();
        var from = quarter?.From ?? _company.FinancialYearStart;
        var to = quarter?.To ?? from.AddMonths(3).AddDays(-1);
        Message = null;

        Cmp08 stmt;
        try
        {
            stmt = Cmp08.Build(_company, from, to);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            stmt = Cmp08.NotApplicable(from, to);
            Message = ex.Message;
        }

        Statement = stmt;
        var periodText = $"{ApexDate.Format(from)} to {ApexDate.Format(to)}";
        Subtitle = $"{_company.Name}  —  {periodText}";

        if (!stmt.Applicable)
        {
            SubTypeText = "Not applicable — this company is not a Composition dealer.";
            RateText = string.Empty;
            SetZeroes();
            StatusText = "CMP-08 applies only to a Composition dealer.";
            return;
        }

        SubTypeText = stmt.SubType?.ToString() ?? "—";
        RateText = $"{stmt.RateBasisPoints / 100m:0.##}% on turnover base";
        TurnoverBaseText = A(stmt.TurnoverBase);

        OutwardCgstText = A(stmt.OutwardCgst);
        OutwardSgstText = A(stmt.OutwardSgst);
        OutwardTurnoverTaxText = A(stmt.OutwardTurnoverTax);

        InwardRcmCgstText = A(stmt.InwardRcmCgst);
        InwardRcmSgstText = A(stmt.InwardRcmSgst);
        InwardRcmIgstText = A(stmt.InwardRcmIgst);
        InwardRcmCessText = A(stmt.InwardRcmCess);

        PayableCgstText = A(stmt.PayableCgst);
        PayableSgstText = A(stmt.PayableSgst);
        PayableIgstText = A(stmt.PayableIgst);
        PayableCessText = A(stmt.PayableCess);
        TotalTaxPayableText = A(stmt.TotalTaxPayable);
        InterestText = A(stmt.Interest);

        StatusText = $"Tax payable ₹{TotalTaxPayableText} (outward ₹{OutwardTurnoverTaxText} + inward RCM ₹{A(stmt.InwardRcmTax)}); Cess ₹{PayableCessText} ring-fenced.";
    }

    private void SetZeroes()
    {
        TurnoverBaseText = OutwardCgstText = OutwardSgstText = OutwardTurnoverTaxText = "0.00";
        InwardRcmCgstText = InwardRcmSgstText = InwardRcmIgstText = InwardRcmCessText = "0.00";
        PayableCgstText = PayableSgstText = PayableIgstText = PayableCessText = TotalTaxPayableText = "0.00";
        InterestText = "0.00";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
