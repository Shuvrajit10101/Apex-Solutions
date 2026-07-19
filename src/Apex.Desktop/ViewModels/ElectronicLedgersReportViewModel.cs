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

/// <summary>One head's row of the electronic <b>Credit</b> ledger (its window movements + closing available ITC).</summary>
public sealed class ElectronicCreditRowVm
{
    public string Head { get; init; } = string.Empty;
    public string Additions { get; init; } = string.Empty;
    public string Utilised { get; init; } = string.Empty;
    public string Reversed { get; init; } = string.Empty;
    public string Closing { get; init; } = string.Empty;
}

/// <summary>One head's row of the electronic <b>Liability</b> ledger (output + cash-only RCM outstanding).</summary>
public sealed class ElectronicLiabilityRowVm
{
    public string Head { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public string Rcm { get; init; } = string.Empty;
}

/// <summary>One (major, minor)-head cell of the electronic <b>Cash</b> ledger.</summary>
public sealed class ElectronicCashRowVm
{
    public string Cell { get; init; } = string.Empty;
    public string Balance { get; init; } = string.Empty;
}

/// <summary>
/// The GST <b>electronic ledgers</b> report page (Reports → Statutory Reports → GST Returns (Advanced) → Electronic
/// Ledgers; Phase 9 UI-1; RQ-20). A read-only projection over the pure <see cref="ElectronicLedgersView"/> engine for
/// a chosen financial year, mirroring the portal's three ledgers: the <b>Credit</b> ledger (per-head available ITC +
/// its window additions / set-off utilisation / reversals), the <b>Liability</b> ledger (per-head outstanding output +
/// cash-only RCM) and the <b>Cash</b> ledger (per (major, minor)-head cell + balance). Cess is ring-fenced (ER-2); a
/// company that never accrued reads all-zero (ER-13). MVVM boundary: engine only, no Avalonia types; deterministic.
/// </summary>
public sealed partial class ElectronicLedgersReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Electronic Ledgers";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _totalCreditText = "0.00";
    [ObservableProperty] private string _totalLiabilityText = "0.00";
    [ObservableProperty] private string _totalRcmLiabilityText = "0.00";
    [ObservableProperty] private string _cashBalanceText = "0.00";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>True when at least one (major, minor) cash cell carries a deposit — drives the Cash-ledger grid vs its
    /// empty-state note (a company that never deposited would otherwise show a bare header, ER-13).</summary>
    [ObservableProperty] private bool _hasCashCells;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the statement can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The four Credit-ledger head rows (CGST/SGST/IGST/Cess).</summary>
    public ObservableCollection<ElectronicCreditRowVm> CreditRows { get; } = new();

    /// <summary>The four Liability-ledger head rows (CGST/SGST/IGST/Cess).</summary>
    public ObservableCollection<ElectronicLiabilityRowVm> LiabilityRows { get; } = new();

    /// <summary>The Cash-ledger (major, minor) cells that carry a deposit.</summary>
    public ObservableCollection<ElectronicCashRowVm> CashRows { get; } = new();

    public ElectronicLedgersReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the three ledgers.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built electronic-ledgers view (rebuilt on selection change).</summary>
    public ElectronicLedgersView View { get; private set; } = default!;

    /// <summary>(Re)builds the electronic ledgers for the selected financial year.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Message = null;
        CreditRows.Clear();
        LiabilityRows.Clear();
        CashRows.Clear();

        ElectronicLedgersView view;
        try
        {
            view = ElectronicLedgersView.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            View = ElectronicLedgersView.Build(_company, fyFrom, fyFrom); // safe fallback (empty window)
            Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}";
            StatusText = "Unable to build the electronic ledgers for this period.";
            return;
        }

        View = view;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  " +
                   $"({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})";

        CreditRows.Add(Credit("CGST", view.CreditAdditionsCgst, view.CreditUtilisedCgst, view.CreditReversedCgst, view.CreditCgst));
        CreditRows.Add(Credit("SGST/UTGST", view.CreditAdditionsSgst, view.CreditUtilisedSgst, view.CreditReversedSgst, view.CreditSgst));
        CreditRows.Add(Credit("IGST", view.CreditAdditionsIgst, view.CreditUtilisedIgst, view.CreditReversedIgst, view.CreditIgst));
        CreditRows.Add(Credit("Cess", view.CreditAdditionsCess, view.CreditUtilisedCess, view.CreditReversedCess, view.CreditCess));

        LiabilityRows.Add(Liab("CGST", view.LiabilityCgst, view.RcmLiabilityCgst));
        LiabilityRows.Add(Liab("SGST/UTGST", view.LiabilitySgst, view.RcmLiabilitySgst));
        LiabilityRows.Add(Liab("IGST", view.LiabilityIgst, view.RcmLiabilityIgst));
        LiabilityRows.Add(Liab("Cess", view.LiabilityCess, view.RcmLiabilityCess));

        foreach (var cell in view.CashCells.OrderBy(c => (int)c.Key.Major).ThenBy(c => (int)c.Key.Minor))
            CashRows.Add(new ElectronicCashRowVm { Cell = $"{cell.Key.Major} · {cell.Key.Minor}", Balance = A(cell.Value) });
        HasCashCells = CashRows.Count > 0;

        TotalCreditText = A(view.TotalCredit);
        TotalLiabilityText = A(view.TotalLiability);
        TotalRcmLiabilityText = A(view.TotalRcmLiability);
        CashBalanceText = A(view.CashBalance);

        StatusText = $"Credit available ₹{TotalCreditText}  ·  liability outstanding ₹{TotalLiabilityText} " +
                     $"(RCM cash ₹{TotalRcmLiabilityText})  ·  cash balance ₹{CashBalanceText}.";
    }

    private static ElectronicCreditRowVm Credit(string head, Money add, Money used, Money rev, Money closing) =>
        new() { Head = head, Additions = A(add), Utilised = A(used), Reversed = A(rev), Closing = A(closing) };

    private static ElectronicLiabilityRowVm Liab(string head, Money output, Money rcm) =>
        new() { Head = head, Output = A(output), Rcm = A(rcm) };

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
