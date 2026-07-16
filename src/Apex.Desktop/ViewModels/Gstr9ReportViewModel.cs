using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One HSN row of the GSTR-9 Table-17 outward summary (its label + already-formatted figures).</summary>
public sealed class Gstr9HsnRowVm
{
    public string HsnSac { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Uqc { get; init; } = string.Empty;
    public string Quantity { get; init; } = string.Empty;
    public string TaxableValue { get; init; } = string.Empty;
    public string TotalTax { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Form GSTR-9</b> annual-return report page (Reports → Statutory Reports → Annual Returns → GSTR-9; Phase 9
/// UI-1; RQ-17). A read-only projection over the pure <see cref="Gstr9"/> engine for a chosen financial year: pick a
/// year and it shows Parts II–VI — Table 4 (outward + inward-RCM tax payable), Table 5 (exempt/nil/non-GST + total
/// turnover), Table 6 (ITC availed), Table 7 (ITC reversed by rule), Table 8 (ITC reconciliation 8A/8B/8D), Table 9
/// (tax paid through ITC vs cash) and Table 17 (outward HSN summary), plus the foot-check totals.
///
/// <para>Gated: only reachable for a Regular GST company (a Composition dealer files 9A, a GST-off company nothing —
/// both yield a not-applicable projection, ER-13). MVVM boundary: engine only, no Avalonia types (headlessly
/// testable); deterministic (no clock/RNG beyond the default FY).</para>
/// </summary>
public sealed partial class Gstr9ReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form GSTR-9 — Annual Return";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _gstinText = string.Empty;

    // Part II Table 4 — outward + inward-RCM on which tax IS payable.
    [ObservableProperty] private string _table4CgstText = "0.00";
    [ObservableProperty] private string _table4SgstText = "0.00";
    [ObservableProperty] private string _table4IgstText = "0.00";
    [ObservableProperty] private string _table4CessText = "0.00";
    [ObservableProperty] private string _table4RcmCgstText = "0.00";
    [ObservableProperty] private string _table4RcmSgstText = "0.00";
    [ObservableProperty] private string _table4RcmIgstText = "0.00";
    [ObservableProperty] private string _table4RcmCessText = "0.00";
    [ObservableProperty] private string _table4TaxableValueText = "0.00";
    [ObservableProperty] private string _table4TotalTaxText = "0.00";

    // Part II Table 5 — outward on which tax is NOT payable.
    [ObservableProperty] private string _table5ExemptText = "0.00";
    [ObservableProperty] private string _table5NTurnoverText = "0.00";

    // Part III Table 6 — ITC availed.
    [ObservableProperty] private string _table6CgstText = "0.00";
    [ObservableProperty] private string _table6SgstText = "0.00";
    [ObservableProperty] private string _table6IgstText = "0.00";
    [ObservableProperty] private string _table6CessText = "0.00";
    [ObservableProperty] private string _table6ReclaimedText = "0.00";
    [ObservableProperty] private string _table6ItcAvailedText = "0.00";

    // Part III Table 7 — ITC reversed by rule.
    [ObservableProperty] private string _table7Rule37Text = "0.00";
    [ObservableProperty] private string _table7Rule42Text = "0.00";
    [ObservableProperty] private string _table7Rule43Text = "0.00";
    [ObservableProperty] private string _table7Section17_5Text = "0.00";
    [ObservableProperty] private string _table7OtherText = "0.00";
    [ObservableProperty] private string _table7CessText = "0.00";
    [ObservableProperty] private string _table7ItcReversedText = "0.00";

    // Part III Table 8 — ITC reconciliation.
    [ObservableProperty] private string _table8AText = "0.00";
    [ObservableProperty] private string _table8ACessText = "0.00";
    [ObservableProperty] private string _table8BText = "0.00";
    [ObservableProperty] private string _table8DText = "0.00";
    [ObservableProperty] private string _netItcText = "0.00";

    // Part IV Table 9 — tax paid.
    [ObservableProperty] private string _table9PaidThroughItcText = "0.00";
    [ObservableProperty] private string _table9PaidInCashText = "0.00";
    [ObservableProperty] private string _table9PayableText = "0.00";

    // Part VI Table 17 — outward HSN summary foot.
    [ObservableProperty] private string _table17TaxableValueText = "0.00";
    [ObservableProperty] private string _table17TotalTaxText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The Table-17 outward HSN summary rows.</summary>
    public ObservableCollection<Gstr9HsnRowVm> HsnRows { get; } = new();

    public Gstr9ReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the annual return.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built GSTR-9 (rebuilt on selection change). Never null after construction.</summary>
    public Gstr9 Return { get; private set; } = default!;

    /// <summary>(Re)builds GSTR-9 for the selected financial year and refreshes every table.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Message = null;
        HsnRows.Clear();

        Gstr9 ret;
        try
        {
            ret = Gstr9.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ret = new Gstr9(fyFrom, fyTo, false, _company.Gst?.Gstin, _company.Name);
            Message = ex.Message;
        }

        Return = ret;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  " +
                   $"({fyFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} to {fyTo.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)})";
        GstinText = string.IsNullOrWhiteSpace(ret.Gstin) ? "GSTIN —" : $"GSTIN {ret.Gstin}";

        if (!ret.Applicable)
        {
            SetZeroes();
            StatusText = "Not applicable — GSTR-9 is filed only by a Regular GST taxpayer.";
            return;
        }

        Table4CgstText = A(ret.Table4Cgst); Table4SgstText = A(ret.Table4Sgst);
        Table4IgstText = A(ret.Table4Igst); Table4CessText = A(ret.Table4Cess);
        Table4RcmCgstText = A(ret.Table4RcmCgst); Table4RcmSgstText = A(ret.Table4RcmSgst);
        Table4RcmIgstText = A(ret.Table4RcmIgst); Table4RcmCessText = A(ret.Table4RcmCess);
        Table4TaxableValueText = A(ret.Table4TaxableValue); Table4TotalTaxText = A(ret.Table4TotalTax);

        Table5ExemptText = A(ret.Table5ExemptNilNonGst); Table5NTurnoverText = A(ret.Table5NTurnover);

        Table6CgstText = A(ret.Table6Cgst); Table6SgstText = A(ret.Table6Sgst);
        Table6IgstText = A(ret.Table6Igst); Table6CessText = A(ret.Table6Cess);
        Table6ReclaimedText = A(ret.Table6HReclaimed); Table6ItcAvailedText = A(ret.Table6ItcAvailed);

        Table7Rule37Text = A(ret.Table7Rule37); Table7Rule42Text = A(ret.Table7Rule42);
        Table7Rule43Text = A(ret.Table7Rule43); Table7Section17_5Text = A(ret.Table7Section17_5);
        Table7OtherText = A(ret.Table7Other); Table7CessText = A(ret.Table7Cess);
        Table7ItcReversedText = A(ret.Table7ItcReversed);

        Table8AText = A(ret.Table8A); Table8ACessText = A(ret.Table8ACess);
        Table8BText = A(ret.Table8B); Table8DText = A(ret.Table8D); NetItcText = A(ret.NetItc);

        Table9PaidThroughItcText = A(ret.Table9PaidThroughItc);
        Table9PaidInCashText = A(ret.Table9PaidInCash);
        Table9PayableText = A(ret.Table9Payable);

        foreach (var h in ret.Table17Hsn)
            HsnRows.Add(new Gstr9HsnRowVm
            {
                HsnSac = h.HsnSac,
                Description = h.Description,
                Uqc = h.Uqc ?? string.Empty,
                Quantity = h.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                TaxableValue = A(h.TaxableValue),
                TotalTax = A(h.TotalTax),
            });
        Table17TaxableValueText = A(ret.Table17TaxableValue);
        Table17TotalTaxText = A(ret.Table17TotalTax);

        StatusText = $"Tax payable ₹{Table9PayableText}  ·  ITC availed ₹{Table6ItcAvailedText}, reversed ₹{Table7ItcReversedText} " +
                     $"(net ₹{NetItcText})  ·  Table 8D (8A − 8B) ₹{Table8DText}. Every figure = Σ the year's monthly returns.";
    }

    private void SetZeroes()
    {
        Table4CgstText = Table4SgstText = Table4IgstText = Table4CessText = "0.00";
        Table4RcmCgstText = Table4RcmSgstText = Table4RcmIgstText = Table4RcmCessText = "0.00";
        Table4TaxableValueText = Table4TotalTaxText = "0.00";
        Table5ExemptText = Table5NTurnoverText = "0.00";
        Table6CgstText = Table6SgstText = Table6IgstText = Table6CessText = Table6ReclaimedText = Table6ItcAvailedText = "0.00";
        Table7Rule37Text = Table7Rule42Text = Table7Rule43Text = Table7Section17_5Text = Table7OtherText = Table7CessText = Table7ItcReversedText = "0.00";
        Table8AText = Table8ACessText = Table8BText = Table8DText = NetItcText = "0.00";
        Table9PaidThroughItcText = Table9PaidInCashText = Table9PayableText = "0.00";
        Table17TaxableValueText = Table17TotalTaxText = "0.00";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
