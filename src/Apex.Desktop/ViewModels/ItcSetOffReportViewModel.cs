using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using SetOff = Apex.Ledger.Services.GstSetOffService;

namespace Apex.Desktop.ViewModels;

/// <summary>One Table-6.1 credit-utilisation line of the ITC set-off (creditHead → liabilityHead + amount).</summary>
public sealed class ItcSetOffLineRowVm
{
    public string CreditHead { get; init; } = string.Empty;
    public string LiabilityHead { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Rule-88A / §49A ITC set-off</b> report page — <b>display only</b> (Reports → Statutory Reports → GST Returns
/// (Advanced) → ITC Set-Off; Phase 9 UI-1; RQ-21). Projects the pure <see cref="SetOff.Allocate"/> allocator over a
/// demand whose liability side comes from the selected FY's GSTR-3B (forward output tax = liability; RCM output, incl.
/// its ring-fenced cess, = cash-only) and whose <b>credit side is the real Input-ledger pool</b> read from
/// <see cref="ElectronicLedgersView"/> — the same pool the Electronic Ledgers screen shows, net of reversals and prior
/// set-off and inclusive of the opening balance, so the two screens agree by construction. It shows the compliant
/// default per-head Table-6.1 utilisation, the residual cash payable and the carried-forward credit.
/// <b>It posts nothing</b> — this is a read-only what-if projection; the actual set-off is
/// posted by the S7 engine, not here. Gated: Regular GST company (ER-13). MVVM boundary: engine only; deterministic.
/// </summary>
public sealed partial class ItcSetOffReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "ITC Set-Off (Rule 88A) — projection";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Demand — liability + credit per head (the set-off inputs, derived from GSTR-3B).
    [ObservableProperty] private string _liabCgstText = "0.00";
    [ObservableProperty] private string _liabSgstText = "0.00";
    [ObservableProperty] private string _liabIgstText = "0.00";
    [ObservableProperty] private string _liabRcmCashText = "0.00";
    [ObservableProperty] private string _creditCgstText = "0.00";
    [ObservableProperty] private string _creditSgstText = "0.00";
    [ObservableProperty] private string _creditIgstText = "0.00";
    [ObservableProperty] private string _creditCessText = "0.00";

    // Residual cash payable per head + closing credit per head.
    [ObservableProperty] private string _cashCgstText = "0.00";
    [ObservableProperty] private string _cashSgstText = "0.00";
    [ObservableProperty] private string _cashIgstText = "0.00";
    [ObservableProperty] private string _cashCessText = "0.00";
    [ObservableProperty] private string _cashRcmText = "0.00";
    [ObservableProperty] private string _totalCashText = "0.00";
    [ObservableProperty] private string _totalCreditUtilisedText = "0.00";
    [ObservableProperty] private string _closingCgstText = "0.00";
    [ObservableProperty] private string _closingSgstText = "0.00";
    [ObservableProperty] private string _closingIgstText = "0.00";
    [ObservableProperty] private string _closingCessText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;

    private GstAdvFyOption? _selectedYear;

    /// <summary>The financial years the set-off can be projected for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The Table-6.1 credit-utilisation lines of the compliant default allocation.</summary>
    public ObservableCollection<ItcSetOffLineRowVm> Lines { get; } = new();

    public ItcSetOffReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-projects the set-off.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-projected allocation (rebuilt on selection change).</summary>
    public SetOff.SetOffAllocation Allocation { get; private set; } = default!;

    /// <summary>(Re)projects the Rule-88A set-off for the selected financial year's GSTR-3B.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Lines.Clear();

        var g3b = Gstr3b.Build(_company, fyFrom, fyTo);

        // The credit side is the REAL Input-ledger pool, read from the same ElectronicLedgersView the Electronic
        // Ledgers screen projects — so the two screens agree on the available credit BY CONSTRUCTION. This is what the
        // portal's Table 6.1 set-off actually draws on: net of the Rule-42/43/37/37A reversals AND of any prior
        // set-off, and inclusive of the credit carried forward from the prior FY. It must NOT be built from
        // g3b.Itc* (Table 4(A)), which is GROSS availment: the reversals post on a Journal-base stat-adjustment that
        // Gstr3b.ReadSide(Input) deliberately excludes (they surface in 4(B)(1)/(2) instead), so a gross credit side
        // counts already-reversed ITC as still available — projecting "cash payable 0.00" when cash IS owed — and
        // silently drops the opening balance. The forward + RCM ITC additions are already inside these pools (the
        // RCM dual leg debits Input {head}), so they are NOT added again here.
        var pools = ElectronicLedgersView.Build(_company, fyFrom, fyTo);

        var demand = new SetOff.SetOffDemand(
            LiabCgst: P(g3b.OutwardCgst), LiabSgst: P(g3b.OutwardSgst), LiabIgst: P(g3b.OutwardIgst), LiabCess: 0,
            // RCM output is cash-only (ER-3) and never enters the credit steps. TotalRcmOutward covers only the
            // CGST/SGST/IGST heads, so the ring-fenced RCM cess (ER-2) is added explicitly — without it a cess-bearing
            // RCM supply showed its cess ITC as credit while its matching cash liability stayed invisible.
            LiabRcmCash: P(g3b.TotalRcmOutward) + P(g3b.RcmOutwardCess),
            CreditCgst: P(pools.CreditCgst),
            CreditSgst: P(pools.CreditSgst),
            CreditIgst: P(pools.CreditIgst),
            CreditCess: P(pools.CreditCess));

        var alloc = SetOff.Allocate(demand);
        Allocation = alloc;

        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  " +
                   $"({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})  —  projection only, posts nothing";

        LiabCgstText = R(demand.LiabCgst); LiabSgstText = R(demand.LiabSgst); LiabIgstText = R(demand.LiabIgst);
        LiabRcmCashText = R(demand.LiabRcmCash);
        CreditCgstText = R(demand.CreditCgst); CreditSgstText = R(demand.CreditSgst);
        CreditIgstText = R(demand.CreditIgst); CreditCessText = R(demand.CreditCess);

        foreach (var l in alloc.Lines)
            Lines.Add(new ItcSetOffLineRowVm
            {
                CreditHead = HeadName(l.CreditHead),
                LiabilityHead = HeadName(l.LiabilityHead),
                Amount = R(l.AmountPaisa),
            });

        CashCgstText = R(alloc.CashCgst); CashSgstText = R(alloc.CashSgst); CashIgstText = R(alloc.CashIgst);
        CashCessText = R(alloc.CashCess); CashRcmText = R(alloc.CashRcm);
        TotalCashText = R(alloc.TotalCash);
        TotalCreditUtilisedText = R(alloc.TotalCreditUtilised);
        ClosingCgstText = R(alloc.ClosingCgst); ClosingSgstText = R(alloc.ClosingSgst);
        ClosingIgstText = R(alloc.ClosingIgst); ClosingCessText = R(alloc.ClosingCess);

        StatusText = $"Credit utilised ₹{TotalCreditUtilisedText}  ·  residual cash payable ₹{TotalCashText} " +
                     $"(incl. cash-only RCM ₹{CashRcmText})  ·  credit carried forward ₹{R(alloc.ClosingCgst + alloc.ClosingSgst + alloc.ClosingIgst + alloc.ClosingCess)}.";
    }

    private static string HeadName(GstTaxHead head) => head switch
    {
        GstTaxHead.Central => "CGST",
        GstTaxHead.State => "SGST/UTGST",
        GstTaxHead.Integrated => "IGST",
        GstTaxHead.Cess => "Cess",
        _ => head.ToString(),
    };

    private static long P(Money m) => (long)Math.Round(m.Amount * 100m, MidpointRounding.AwayFromZero);
    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
