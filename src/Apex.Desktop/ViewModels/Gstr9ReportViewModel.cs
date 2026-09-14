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
public sealed partial class Gstr9ReportViewModel : ViewModelBase, IMasterListExportSource
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
                   $"({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})";
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

    /// <summary>
    /// <b>Census 6.12 — the annual return gains an exit.</b> GSTR-9 is signed off by a proprietor or a chartered
    /// accountant who is not the person driving this screen, so a figure that cannot leave it cannot be reviewed
    /// by the person who carries the liability for it. E / Alt+E and P / Ctrl+P are the whole of this row's
    /// recorded gap, alongside <see cref="Gstr9cReportViewModel"/>.
    ///
    /// <para><b>Every part is labelled by its statutory table number</b>, not by the screen's own shorthand. An
    /// annual return is reconciled table-by-table against the twelve monthly GSTR-3Bs and the GSTR-1s; a reviewer
    /// doing that needs "6  ITC availed" to be findable by the number they are reading off the portal, not by a
    /// caption this product invented.</para>
    ///
    /// <para><b>Table 17 is folded to two rows per HSN rather than six.</b> The grid shows HSN, description, UQC,
    /// quantity, taxable value and tax; the first four identify the row and the last two are the money. Carrying
    /// the identity in the label keeps every money figure in the single <see cref="MasterListColumn.Number"/>
    /// column, so a spreadsheet can foot Table 17 against its own total — which is the one arithmetic check a
    /// reviewer actually performs on this table. The foot total is carried too, so the check is possible without
    /// re-adding the rows.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Period", Subtitle },
            new[] { "Registration", GstinText },

            // Part II Table 4 — outward + inward RCM on which tax IS payable.
            new[] { "4  Taxable value (outward, tax payable)", Table4TaxableValueText },
            new[] { "4  Outward tax — CGST", Table4CgstText },
            new[] { "4  Outward tax — SGST/UTGST", Table4SgstText },
            new[] { "4  Outward tax — IGST", Table4IgstText },
            new[] { "4  Outward tax — Cess", Table4CessText },
            new[] { "4  Inward reverse charge — CGST", Table4RcmCgstText },
            new[] { "4  Inward reverse charge — SGST/UTGST", Table4RcmSgstText },
            new[] { "4  Inward reverse charge — IGST", Table4RcmIgstText },
            new[] { "4  Inward reverse charge — Cess", Table4RcmCessText },
            new[] { "4  Total tax payable", Table4TotalTaxText },

            // Part II Table 5 — outward on which tax is NOT payable.
            new[] { "5  Exempt / nil-rated / non-GST outward", Table5ExemptText },
            new[] { "5  Total turnover", Table5NTurnoverText },

            // Part III Table 6 — ITC availed.
            new[] { "6  ITC availed — CGST", Table6CgstText },
            new[] { "6  ITC availed — SGST/UTGST", Table6SgstText },
            new[] { "6  ITC availed — IGST", Table6IgstText },
            new[] { "6  ITC availed — Cess", Table6CessText },
            new[] { "6  ITC reclaimed", Table6ReclaimedText },
            new[] { "6  Total ITC availed", Table6ItcAvailedText },

            // Part III Table 7 — ITC reversed, by rule.
            new[] { "7  ITC reversed — rule 37", Table7Rule37Text },
            new[] { "7  ITC reversed — rule 42", Table7Rule42Text },
            new[] { "7  ITC reversed — rule 43", Table7Rule43Text },
            new[] { "7  ITC reversed — section 17(5)", Table7Section17_5Text },
            new[] { "7  ITC reversed — other", Table7OtherText },
            new[] { "7  ITC reversed — Cess", Table7CessText },
            new[] { "7  Total ITC reversed", Table7ItcReversedText },

            // Part III Table 8 — ITC reconciliation.
            new[] { "8A  ITC as per GSTR-2A / 2B", Table8AText },
            new[] { "8A  ITC as per GSTR-2A / 2B — Cess", Table8ACessText },
            new[] { "8B  ITC availed per this return", Table8BText },
            new[] { "8D  Difference", Table8DText },
            new[] { "Net ITC", NetItcText },

            // Part IV Table 9 — tax paid.
            new[] { "9  Tax payable", Table9PayableText },
            new[] { "9  Paid through ITC", Table9PaidThroughItcText },
            new[] { "9  Paid in cash", Table9PaidInCashText },
        };

        // Part VI Table 17 — outward HSN summary. Identity in the label, money in the numeric column.
        foreach (var h in HsnRows)
        {
            var identity = $"17  {h.HsnSac}  {h.Description}  ({h.Quantity} {h.Uqc})".TrimEnd();
            rows.Add(new[] { identity + " — taxable value", h.TaxableValue });
            rows.Add(new[] { identity + " — total tax", h.TotalTax });
        }

        rows.Add(new[] { "17  Total taxable value", Table17TaxableValueText });
        rows.Add(new[] { "17  Total tax", Table17TotalTaxText });

        return new MasterListSnapshot(
            Title,
            new[] { MasterListColumn.Text("Particulars"), MasterListColumn.Number("Amount") },
            rows);
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
