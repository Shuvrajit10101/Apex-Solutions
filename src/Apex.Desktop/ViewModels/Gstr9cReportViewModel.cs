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

/// <summary>
/// The <b>Form GSTR-9C</b> reconciliation-statement report page (Reports → Statutory Reports → Annual Returns →
/// GSTR-9C; Phase 9 UI-1; RQ-17). A read-only projection over the pure <see cref="Gstr9c"/> engine for a chosen
/// financial year: reconciles the annual return (<see cref="Gstr9"/>) turnover, tax and net ITC to the audited books,
/// with the unreconciled-difference lines COMPUTED AND SHOWN (never forced to zero) —
/// <list type="bullet">
///   <item><b>Part A Table 5</b> — 5A books turnover → 5Q return turnover → 5R unreconciled.</item>
///   <item><b>Part III Tables 9–11</b> — tax per return vs tax per books → 11 unreconciled tax.</item>
///   <item><b>Part B Table 12</b> — 12A ITC per books → 12E net ITC per GSTR-9 → 12F unreconciled.</item>
/// </list>
/// Gated: only reachable for a Regular GST company (Composition / GST-off ⇒ not-applicable, ER-13). MVVM boundary:
/// engine only, no Avalonia types (headlessly testable); deterministic.
/// </summary>
public sealed partial class Gstr9cReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form GSTR-9C — Reconciliation Statement";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _gstinText = string.Empty;

    // Part A Table 5 — gross-turnover reconciliation.
    [ObservableProperty] private string _booksTurnoverText = "0.00";
    [ObservableProperty] private string _returnTurnoverText = "0.00";
    [ObservableProperty] private string _unreconciledTurnoverText = "0.00";

    // Part III Tables 9–11 — tax reconciliation.
    [ObservableProperty] private string _taxPerReturnText = "0.00";
    [ObservableProperty] private string _taxPerBooksText = "0.00";
    [ObservableProperty] private string _unreconciledTaxText = "0.00";

    // Part B Table 12 — net-ITC reconciliation.
    [ObservableProperty] private string _booksItcText = "0.00";
    [ObservableProperty] private string _returnItcText = "0.00";
    [ObservableProperty] private string _unreconciledItcText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the statement can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    public Gstr9cReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the reconciliation statement.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built GSTR-9C (rebuilt on selection change). Never null after construction.</summary>
    public Gstr9c Statement { get; private set; } = default!;

    /// <summary>(Re)builds GSTR-9C for the selected financial year and refreshes the reconciliation lines.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Message = null;

        Gstr9c stmt;
        try
        {
            stmt = Gstr9c.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            stmt = new Gstr9c(fyFrom, fyTo, false, _company.Gst?.Gstin, _company.Name);
            Message = ex.Message;
        }

        Statement = stmt;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  " +
                   $"({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})";
        GstinText = string.IsNullOrWhiteSpace(stmt.Gstin) ? "GSTIN —" : $"GSTIN {stmt.Gstin}";

        if (!stmt.Applicable)
        {
            SetZeroes();
            StatusText = "Not applicable — GSTR-9C reconciles a Regular taxpayer's GSTR-9 to the audited books.";
            return;
        }

        BooksTurnoverText = A(stmt.Table5ABooksTurnover);
        ReturnTurnoverText = A(stmt.Table5QReturnTurnover);
        UnreconciledTurnoverText = A(stmt.Table5RUnreconciledTurnover);

        TaxPerReturnText = A(stmt.Table9TaxPerReturn);
        TaxPerBooksText = A(stmt.Table9TaxPerBooks);
        UnreconciledTaxText = A(stmt.Table11UnreconciledTax);

        BooksItcText = A(stmt.Table12ABooksItc);
        ReturnItcText = A(stmt.Table12EReturnItc);
        UnreconciledItcText = A(stmt.Table12FUnreconciledItc);

        StatusText = $"Unreconciled turnover (5R) ₹{UnreconciledTurnoverText}  ·  tax (11) ₹{UnreconciledTaxText}  ·  " +
                     $"net ITC (12F) ₹{UnreconciledItcText}. The differences are computed and shown, never forced to zero.";
    }

    private void SetZeroes()
    {
        BooksTurnoverText = ReturnTurnoverText = UnreconciledTurnoverText = "0.00";
        TaxPerReturnText = TaxPerBooksText = UnreconciledTaxText = "0.00";
        BooksItcText = ReturnItcText = UnreconciledItcText = "0.00";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
