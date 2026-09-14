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

/// <summary>One quarter row of the GSTR-4 Table-5 roll-up (its label + the already-rounded CMP-08 figures).</summary>
public sealed class Gstr4QuarterRowVm
{
    public string Quarter { get; init; } = string.Empty;
    public string TurnoverBase { get; init; } = string.Empty;
    public string OutwardTax { get; init; } = string.Empty;
    public string InwardRcmTax { get; init; } = string.Empty;
    public string TotalPayable { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form GSTR-4</b> report page (Reports → Statutory Reports → Composition Returns → GSTR-4; Phase 9 slice 3;
/// RQ-16; Rule 62). A read-only projection over the pure <see cref="Gstr4"/> engine for a chosen financial year:
/// the four quarters' CMP-08 self-assessed liability (Table 5), the light inward-supply tables 4A/4B/4C/4D (value +
/// cash RCM tax, no ITC — a composition dealer claims none) and the annual composition + RCM tax (Table 6, which
/// reconciles to Σ Table 5 by construction).
///
/// <para>Gated: only reachable when the company is a Composition dealer (byte-identical for a Regular company,
/// ER-13). MVVM boundary: engine only, no Avalonia types (headlessly testable); deterministic (no clock/RNG).</para>
/// </summary>
public sealed partial class Gstr4ReportViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form GSTR-4 — Composition Annual Return";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _subTypeText = string.Empty;

    // Table 4A/4B/4C/4D — inward supplies (value only; no ITC).
    [ObservableProperty] private string _registeredValueText = "0.00";
    [ObservableProperty] private string _reverseChargeValueText = "0.00";
    [ObservableProperty] private string _reverseChargeTaxText = "0.00";
    [ObservableProperty] private string _unregisteredValueText = "0.00";
    [ObservableProperty] private string _importServiceValueText = "0.00";

    // Table 6 — annual liability.
    [ObservableProperty] private string _annualCompositionTaxText = "0.00";
    [ObservableProperty] private string _annualRcmTaxText = "0.00";
    [ObservableProperty] private string _annualTotalText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private CompositionFyOption? _selectedYear;

    /// <summary>The financial years the return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<CompositionFyOption> FinancialYears { get; } = new();

    /// <summary>The four quarter rows of the selected FY (Table 5 roll-up).</summary>
    public ObservableCollection<Gstr4QuarterRowVm> Quarters { get; } = new();

    public Gstr4ReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new CompositionFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the annual return.</summary>
    public CompositionFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built GSTR-4 (rebuilt on selection change). Never null after construction.</summary>
    public Gstr4 Return { get; private set; } = default!;

    /// <summary>(Re)builds GSTR-4 for the selected financial year and refreshes the quarter roll-up + inward tables.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Message = null;
        Quarters.Clear();

        Gstr4 ret;
        try
        {
            ret = Gstr4.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Return = new Gstr4(fyFrom, fyTo, false, null, Array.Empty<Cmp08>(), null,
                new Gstr4Inward(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero));
            Message = ex.Message;
            SetNotApplicable();
            return;
        }

        Return = ret;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  ({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})";

        if (!ret.Applicable)
        {
            SetNotApplicable();
            return;
        }

        SubTypeText = ret.SubType?.ToString() ?? "—";

        var qLabels = new[] { "Q1 (Apr–Jun)", "Q2 (Jul–Sep)", "Q3 (Oct–Dec)", "Q4 (Jan–Mar)" };
        for (var i = 0; i < ret.Quarters.Count; i++)
        {
            var q = ret.Quarters[i];
            Quarters.Add(new Gstr4QuarterRowVm
            {
                Quarter = i < qLabels.Length ? qLabels[i] : $"Q{i + 1}",
                TurnoverBase = A(q.TurnoverBase),
                OutwardTax = A(q.OutwardTurnoverTax),
                InwardRcmTax = A(q.InwardRcmTax),
                TotalPayable = A(q.TotalTaxPayable),
            });
        }

        RegisteredValueText = A(ret.Inward.RegisteredValue);
        ReverseChargeValueText = A(ret.Inward.ReverseChargeValue);
        ReverseChargeTaxText = A(ret.Inward.ReverseChargeTax);
        UnregisteredValueText = A(ret.Inward.UnregisteredValue);
        ImportServiceValueText = A(ret.Inward.ImportServiceValue);

        AnnualCompositionTaxText = A(ret.AnnualCompositionTax);
        AnnualRcmTaxText = A(ret.AnnualRcmTax);
        AnnualTotalText = A(new Money(ret.AnnualCompositionTax.Amount + ret.AnnualRcmTax.Amount));

        StatusText = $"Annual tax ₹{AnnualTotalText} (composition ₹{AnnualCompositionTaxText} + inward RCM ₹{AnnualRcmTaxText}); reconciles to Σ of the four quarters.";
    }

    /// <summary>
    /// <b>Census 6.11 — the second half of that row's output gap.</b> CMP-08 gained E / Alt+E and P / Ctrl+P
    /// through <see cref="IMasterListExportSource"/>; row 6.11 covers <b>both</b> composition returns, so GSTR-4
    /// without an export path would have left the row exactly as PARTIAL as it started.
    ///
    /// <para><b>Two columns, and the quarter grid is unrolled into them on purpose.</b> The screen shows Table 5
    /// as a five-column grid, so the obvious move is a five-column snapshot. It is rejected: only the Table-5
    /// rows have five figures — the Table 4A–4D inward values and the Table 6 annual figures are single amounts,
    /// and they would sit under a column captioned <i>"Turnover Base"</i> or <i>"Outward Tax"</i>, which is a
    /// caption that lies about the figure beneath it. That is the precise defect census 6.9 was filed for on the
    /// GSTR-3B screen, and it is not worth re-introducing here to save sixteen rows.</para>
    ///
    /// <para><b>Mirrors <see cref="Cmp08ReportViewModel.ToMasterListSnapshot"/> deliberately.</b> CMP-08 and
    /// GSTR-4 are the same dealer's quarterly and annual returns, reconciled against each other at year end —
    /// Table 6 reconciles to Σ Table 5 by construction. An accountant laying the two exports side by side must
    /// not have to re-learn the layout to do that reconciliation.</para>
    ///
    /// <para><b>The sub-type leads the snapshot</b> for the same reason it leads the CMP-08 one: the composition
    /// rate depends on it, so a page of figures without it is ambiguous once it leaves this screen.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Period", Subtitle },
            new[] { "Composition sub-type", SubTypeText },
        };

        // Table 5 — the four quarters' self-assessed CMP-08 liability, unrolled one figure per row.
        foreach (var q in Quarters)
        {
            rows.Add(new[] { $"5  {q.Quarter} — turnover base", q.TurnoverBase });
            rows.Add(new[] { $"5  {q.Quarter} — outward tax on turnover", q.OutwardTax });
            rows.Add(new[] { $"5  {q.Quarter} — inward reverse-charge tax", q.InwardRcmTax });
            rows.Add(new[] { $"5  {q.Quarter} — total tax payable", q.TotalPayable });
        }

        // Tables 4A–4D — inward supplies, value only (a composition dealer claims no ITC).
        rows.Add(new[] { "4A  Inward supplies from a registered supplier — value", RegisteredValueText });
        rows.Add(new[] { "4B  Inward supplies liable to reverse charge — value", ReverseChargeValueText });
        rows.Add(new[] { "4B  Inward supplies liable to reverse charge — tax paid in cash", ReverseChargeTaxText });
        rows.Add(new[] { "4C  Inward supplies from an unregistered supplier — value", UnregisteredValueText });
        rows.Add(new[] { "4D  Import of service — value", ImportServiceValueText });

        // Table 6 — the annual liability. Reconciles to Σ Table 5 by construction.
        rows.Add(new[] { "6  Annual composition tax on turnover", AnnualCompositionTaxText });
        rows.Add(new[] { "6  Annual inward reverse-charge tax", AnnualRcmTaxText });
        rows.Add(new[] { "6  Annual total tax payable", AnnualTotalText });

        return new MasterListSnapshot(
            Title,
            new[] { MasterListColumn.Text("Particulars"), MasterListColumn.Number("Amount") },
            rows);
    }

    private void SetNotApplicable()
    {
        SubTypeText = "Not applicable — this company is not a Composition dealer.";
        RegisteredValueText = ReverseChargeValueText = ReverseChargeTaxText = "0.00";
        UnregisteredValueText = ImportServiceValueText = "0.00";
        AnnualCompositionTaxText = AnnualRcmTaxText = AnnualTotalText = "0.00";
        StatusText = "GSTR-4 applies only to a Composition dealer.";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
