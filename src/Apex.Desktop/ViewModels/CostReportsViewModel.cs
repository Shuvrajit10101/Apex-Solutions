using System;
using System.Linq;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The two cost-centre report kinds surfaced under Reports → Statements of Accounts → Cost Centres.</summary>
public enum CostReportKind
{
    CategorySummary,
    CostCentreBreakup,
}

/// <summary>
/// Builds the cost-centre report content for the current company from the pure
/// <see cref="Apex.Ledger.Reports.CostReports"/> projections (catalog §6):
/// <list type="bullet">
/// <item><b>Category Summary</b> — the total allocated amount per cost category.</item>
/// <item><b>Cost Centre Break-up</b> — every centre's own and rolled-up totals, indented depth-first per
/// category (parents before children).</item>
/// </list>
/// The window is books-begin → the last voucher date (or the FY end when there are no vouchers), so a
/// freshly loaded company shows its full picture. Rows are <see cref="ReportRow"/> in single-amount mode
/// with right-aligned amounts. MVVM boundary: no Avalonia types ⇒ headlessly testable.
/// </summary>
public sealed partial class CostReportsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _from;
    private readonly DateOnly _to;

    [ObservableProperty] private CostReportKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The label of the leading particulars column ("Cost Category" vs "Cost Centre").</summary>
    [ObservableProperty] private string _particularsHeader = "Particulars";

    public ObservableCollection<ReportRow> Rows { get; } = new();

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    public CostReportsViewModel(Company company, CostReportKind kind)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _from = company.BooksBeginFrom;
        _to = ComputeAsOf(company);
        Show(kind);
    }

    /// <summary>Switches the displayed cost report and rebuilds its rows.</summary>
    public void Show(CostReportKind kind)
    {
        Kind = kind;
        Rows.Clear();

        switch (kind)
        {
            case CostReportKind.CategorySummary: BuildCategorySummary(); break;
            case CostReportKind.CostCentreBreakup: BuildCostCentreBreakup(); break;
        }
    }

    // --------------------------------------------------------------- Category Summary

    private void BuildCategorySummary()
    {
        var report = CostReports.BuildCategorySummary(_company, _from, _to);
        Title = "Category Summary";
        Subtitle = $"{CompanyName}  —  {FormatDate(_from)} to {FormatDate(_to)}";
        ParticularsHeader = "Cost Category";

        foreach (var cat in report.Categories)
            Rows.Add(ReportRow.Line(cat.CategoryName, cat.Total));

        if (report.Categories.Count == 0)
        {
            Rows.Add(new ReportRow { Particulars = "No cost allocations in this period.", IsHeader = true });
            return;
        }

        AddAllocatedTotal(
            report.GrandTotal, report.CategoryTotalsOverlap,
            "Cost categories are parallel axes — an amount classified under more than one category appears " +
            "in full under each of them, so the category totals above overlap. This figure is the cost " +
            "counted once per entry line, not the sum of the rows above.");
    }

    // --------------------------------------------------------------- Cost Centre Break-up

    private void BuildCostCentreBreakup()
    {
        var report = CostReports.BuildCostCentreBreakup(_company, _from, _to);
        Title = "Cost Centre Break-up";
        Subtitle = $"{CompanyName}  —  {FormatDate(_from)} to {FormatDate(_to)}";
        ParticularsHeader = "Cost Centre";

        Guid? currentCategory = null;
        foreach (var line in report.Centres)
        {
            // Emit a category sub-header when the category changes (centres are grouped by category).
            if (line.CategoryId != currentCategory)
            {
                currentCategory = line.CategoryId;
                var catName = _company.FindCostCategory(line.CategoryId)?.Name ?? "Cost Category";
                Rows.Add(new ReportRow { Particulars = catName, IsHeader = true });
            }

            // A parent centre shows its rolled-up total (own + descendants); a leaf shows its own.
            var hasChildren = report.Centres.Any(c => c.ParentId == line.CentreId);
            var amount = hasChildren ? line.RolledUpTotal : line.OwnTotal;
            Rows.Add(new ReportRow
            {
                Particulars = line.CentreName,
                Amount = IndianFormat.Amount(amount),
                Indent = 14 + line.Depth * 18,
            });
        }

        if (report.Centres.Count == 0)
        {
            Rows.Add(new ReportRow { Particulars = "No cost centres defined.", IsHeader = true });
            return;
        }

        AddAllocatedTotal(
            report.GrandTotal, report.CategoryTotalsOverlap,
            "Cost categories are parallel axes — an amount classified under more than one category is " +
            "carried in full by one centre in each of them, so the centre totals above overlap. This " +
            "figure is the cost counted once per entry line, not the sum of the rows above.");
    }

    // --------------------------------------------------------------- helpers

    /// <summary>
    /// Emits the footer total for a cost report (spec §4.2 rule C-27; gap G-2).
    /// <para><b>The relabel and the note are driven by whether the rows above actually overlap</b>
    /// (<c>CategoryTotalsOverlap</c>), never by whether a line happens to name two categories. Two distinct
    /// populations name two categories and only one of them double-counts:</para>
    /// <list type="bullet">
    /// <item>A conforming <b>parallel set</b> carries the whole amount under each category, so Σ of the rows
    /// exceeds the cost (₹15,000 of rows for one ₹5,000 expense). The footer is relabelled so it is not
    /// mistaken for a column sum, and the note explains what the figure is instead.</item>
    /// <item>A <b>pre-fix (legacy) partition</b> book carries ₹3,000.19 Branch + ₹2,000.18 Department on a
    /// ₹5,000.37 line. Its rows add up to the total exactly — nothing double-counts and not one paisa on
    /// screen has moved — so it keeps "Grand Total" and no note, exactly like a single-axis book. Relabelling
    /// it would be an unexplained change to a shipped screen, and the note would flatly contradict the two
    /// rows printed immediately above it.</item>
    /// </list>
    /// </summary>
    private void AddAllocatedTotal(Money total, bool rowsOverlap, string note)
    {
        if (!rowsOverlap)
        {
            Rows.Add(ReportRow.Total("Grand Total", total));
            return;
        }

        Rows.Add(ReportRow.Total("Total Cost Allocated", total));
        Rows.Add(new ReportRow { Particulars = note, IsHeader = true });
    }

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private static string FormatDate(DateOnly d) => ApexDate.Format(d);
}
