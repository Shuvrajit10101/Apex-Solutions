using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using SetOff = Apex.Ledger.Services.GstSetOffService;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable return period on the Run Set-Off screen (a <c>yyyy-MM</c> month inside the chosen FY — the
/// period key the engine's <see cref="SetOff.PostSetOff"/> is idempotent on).</summary>
public sealed class GstPeriodOption
{
    public required DateOnly MonthStart { get; init; }

    /// <summary>The engine's period key (<c>yyyy-MM</c>).</summary>
    public string Period => MonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public string Label => MonthStart.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>One cash cell of the electronic cash ledger (a major head × the Tax minor head) + what it can fund.</summary>
public sealed class GstCashCellRowVm
{
    public string Head { get; init; } = string.Empty;
    public string Available { get; init; } = string.Empty;
    public string Required { get; init; } = string.Empty;

    /// <summary>True when the cell holds at least the residual cash this period needs (drives the row colour).</summary>
    public bool IsFunded { get; init; }
}

/// <summary>
/// The <b>Run Set-Off (Rule 88A) &amp; Pay</b> action screen (Reports → Statutory Reports → GST Actions → Run Set-Off
/// &amp; Pay; Phase 9 UI-2; RQ-21). The interactive twin of the UI-1 <see cref="ItcSetOffReportViewModel"/>
/// projection: it <b>previews</b> the compliant Rule-88A allocation for a chosen return period, and then — only on an
/// explicit action — <b>posts</b> it via <see cref="SetOff.PostSetOff"/> and discharges the residual cash via
/// <see cref="GstDepositService"/> (a PMT-06 challan funds the electronic cash ledger; the discharge then draws on it).
///
/// <para>The demand mirrors the projection screen exactly, so the two never disagree: the <b>liability</b> is the
/// selected month's GSTR-3B forward output tax (its RCM output, incl. the ring-fenced cess, is <b>cash-only</b> — ER-3
/// — and never enters the credit steps), and the <b>credit</b> is the real Input-ledger pool read from
/// <see cref="ElectronicLedgersView"/> cumulatively to that month's end — net of Rule-42/43/37/37A reversals and of any
/// prior set-off, inclusive of the opening balance.</para>
///
/// <para><b>Engine guards surfaced, never crashed into:</b> the credit ledger may discharge <b>Tax only</b> (§49(4) —
/// never interest / penalty / fee); CGST and SGST credit <b>never cross</b> (Rule 88A, enforced by the allocator);
/// a cash discharge against an <b>underfunded</b> cash cell is refused (deposit a PMT-06 challan first); and a
/// PMT-06 without a <b>CIN</b> is refused (the cash ledger is credited only once the bank confirms).</para>
///
/// <para><b>Opening this screen posts nothing</b> — it only previews. Gated: Regular GST company (ER-13).</para>
/// </summary>
public sealed partial class RunSetOffViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly GstDepositService _deposit;
    private readonly SetOff _setOff;

    [ObservableProperty] private string _title = "Run Set-Off (Rule 88A) & Pay";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;

    // The preview (demand → allocation).
    [ObservableProperty] private string _liabCgstText = "0.00";
    [ObservableProperty] private string _liabSgstText = "0.00";
    [ObservableProperty] private string _liabIgstText = "0.00";
    [ObservableProperty] private string _liabRcmCashText = "0.00";
    [ObservableProperty] private string _creditCgstText = "0.00";
    [ObservableProperty] private string _creditSgstText = "0.00";
    [ObservableProperty] private string _creditIgstText = "0.00";
    [ObservableProperty] private string _creditCessText = "0.00";
    [ObservableProperty] private string _totalCashText = "0.00";
    [ObservableProperty] private string _totalCreditUtilisedText = "0.00";

    /// <summary>True once a set-off Journal exists for the selected period (the action is idempotent per period).</summary>
    [ObservableProperty] private bool _isPosted;

    /// <summary>True when the allocation utilises no credit at all — <see cref="SetOff.PostSetOff"/> posts no Journal
    /// for an all-cash period (it returns null), so the screen says so rather than claiming a posting.</summary>
    [ObservableProperty] private bool _hasCreditLines;

    // The PMT-06 challan form (funds the electronic cash ledger before a discharge can draw on it).
    [ObservableProperty] private DomainLedger? _selectedBank;
    [ObservableProperty] private GstTaxHead _challanHead = GstTaxHead.Central;
    [ObservableProperty] private string _challanAmountText = string.Empty;
    [ObservableProperty] private string _cpin = string.Empty;
    [ObservableProperty] private string _cin = string.Empty;

    private GstAdvFyOption? _selectedYear;
    private GstPeriodOption? _selectedPeriod;

    /// <summary>The financial years the set-off can be run for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The twelve return periods of the selected financial year.</summary>
    public ObservableCollection<GstPeriodOption> Periods { get; } = new();

    /// <summary>The Table-6.1 credit-utilisation lines of the previewed allocation.</summary>
    public ObservableCollection<ItcSetOffLineRowVm> Lines { get; } = new();

    /// <summary>The four cash cells (CGST/SGST/IGST/Cess × Tax) — what is available vs what this period still needs.</summary>
    public ObservableCollection<GstCashCellRowVm> CashCells { get; } = new();

    /// <summary>The Bank / Cash ledgers a PMT-06 challan can be paid from.</summary>
    public ObservableCollection<DomainLedger> BankOptions { get; } = new();

    /// <summary>The major heads a challan can be deposited into.</summary>
    public ObservableCollection<GstTaxHead> ChallanHeads { get; } =
        new(new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated, GstTaxHead.Cess });

    public RunSetOffViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _deposit = new GstDepositService(company);
        _setOff = new SetOff(company);

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });
        _selectedYear = FinancialYears.FirstOrDefault();

        LoadBankOptions();
        BuildPeriods();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the period list and re-previews.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) { BuildPeriods(); Rebuild(); } }
    }

    /// <summary>The selected return period; changing it re-previews the allocation.</summary>
    public GstPeriodOption? SelectedPeriod
    {
        get => _selectedPeriod;
        set { if (SetProperty(ref _selectedPeriod, value)) Rebuild(); }
    }

    /// <summary>The previewed allocation for the selected period (rebuilt on any selection change). Posting is a
    /// separate, explicit action — building this projects only.</summary>
    public SetOff.SetOffAllocation Allocation { get; private set; } = default!;

    /// <summary>The demand the allocation was built from (liability + credit per head).</summary>
    public SetOff.SetOffDemand Demand { get; private set; }

    private void BuildPeriods()
    {
        Periods.Clear();
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        for (var i = 0; i < 12; i++)
            Periods.Add(new GstPeriodOption { MonthStart = first.AddMonths(i) });

        // Default to the month the books actually carry activity in (the earliest month with a voucher), else month 1.
        var firstActivity = _company.Vouchers
            .Select(v => v.Date)
            .Where(d => d >= first && d < first.AddYears(1))
            .DefaultIfEmpty(first)
            .Min();
        _selectedPeriod = Periods.FirstOrDefault(p => p.MonthStart.Month == firstActivity.Month
                                                      && p.MonthStart.Year == firstActivity.Year)
                          ?? Periods.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPeriod));
    }

    /// <summary>(Re)previews the Rule-88A allocation for the selected period. <b>Posts nothing.</b></summary>
    public void Rebuild()
    {
        Lines.Clear();
        CashCells.Clear();

        var period = SelectedPeriod;
        if (period is null) { StatusText = "No return period selected."; return; }

        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var monthFrom = period.MonthStart;
        var monthTo = monthFrom.AddMonths(1).AddDays(-1);

        // Liability = THIS period's 3B. Credit = the Input pool cumulatively to this period's end (credit carries
        // forward; a month's liability is discharged out of everything accumulated up to it).
        var g3b = Gstr3b.Build(_company, monthFrom, monthTo);
        var pools = ElectronicLedgersView.Build(_company, fyFrom, monthTo);

        // The demand must describe the period as if THIS period's own set-off had not been posted yet — because
        // PostSetOff replaces the period's posting wholesale, and the residual cash is a property of the period, not
        // of what is left after it. The posted set-off Journal already consumed its credit out of the Input pool, so
        // that credit is ADDED BACK here. Without this the preview double-counts the posting: the liability stays
        // gross (Gstr3b excludes the stat-adjustment) while the pool reads as consumed, so a re-preview claims the
        // WHOLE liability now falls to cash, and re-running the set-off would post a different, smaller allocation
        // against a pool it had already drawn on. Adding it back makes the preview stable and PostSetOff genuinely
        // idempotent — running it twice yields the same allocation.
        var postedBack = PostedSetOffCreditFor(period.Period);

        var demand = new SetOff.SetOffDemand(
            LiabCgst: P(g3b.OutwardCgst), LiabSgst: P(g3b.OutwardSgst), LiabIgst: P(g3b.OutwardIgst), LiabCess: 0,
            // RCM output is cash-only (ER-3); TotalRcmOutward omits the ring-fenced cess (ER-2), so it is added here.
            LiabRcmCash: P(g3b.TotalRcmOutward) + P(g3b.RcmOutwardCess),
            CreditCgst: P(pools.CreditCgst) + postedBack(GstTaxHead.Central),
            CreditSgst: P(pools.CreditSgst) + postedBack(GstTaxHead.State),
            CreditIgst: P(pools.CreditIgst) + postedBack(GstTaxHead.Integrated),
            CreditCess: P(pools.CreditCess) + postedBack(GstTaxHead.Cess));

        Demand = demand;
        var alloc = SetOff.Allocate(demand);
        Allocation = alloc;
        HasCreditLines = alloc.Lines.Count > 0;

        foreach (var l in alloc.Lines)
            Lines.Add(new ItcSetOffLineRowVm
            {
                CreditHead = HeadName(l.CreditHead),
                LiabilityHead = HeadName(l.LiabilityHead),
                Amount = R(l.AmountPaisa),
            });

        // What this period still owes in cash = its residual MINUS whatever has already been discharged inside the
        // period's own window. Without this netting the screen would invite the taxpayer to pay the same residual
        // twice (the engine's cash discharge carries no period key of its own to stop them).
        //
        // The residual is the forward head's cash PLUS its share of the cash-only RCM. RCM is cash-only (ER-3) and
        // never credit-offset, but it is still a real liability that has to be dischargeable — leaving it out of the
        // demand meant the four cells all read 0.00, the discharge had nothing to pay, and (once the forward heads
        // were credit-covered) the screen told the taxpayer the period was settled while the RCM was outstanding.
        // The engine's SetOffDemand collapses RCM to a lump, so the per-head split is read back off the SAME 3B the
        // demand was built from — Σ per-head == alloc.CashRcm exactly, by construction.
        _cashDue = new[]
            {
                (Head: GstTaxHead.Central, Name: "CGST", Residual: alloc.CashCgst + P(g3b.RcmOutwardCgst)),
                (Head: GstTaxHead.State, Name: "SGST/UTGST", Residual: alloc.CashSgst + P(g3b.RcmOutwardSgst)),
                (Head: GstTaxHead.Integrated, Name: "IGST", Residual: alloc.CashIgst + P(g3b.RcmOutwardIgst)),
                (Head: GstTaxHead.Cess, Name: "Cess", Residual: alloc.CashCess + P(g3b.RcmOutwardCess)),
            }
            .Select(x => (x.Head, x.Name, Due: Math.Max(0, x.Residual - DischargedCashFor(x.Head, monthFrom, monthTo))))
            .ToList();

        foreach (var (head, name, due) in _cashDue)
            AddCashCell(name, head, due);

        LiabCgstText = R(demand.LiabCgst); LiabSgstText = R(demand.LiabSgst); LiabIgstText = R(demand.LiabIgst);
        LiabRcmCashText = R(demand.LiabRcmCash);
        CreditCgstText = R(demand.CreditCgst); CreditSgstText = R(demand.CreditSgst);
        CreditIgstText = R(demand.CreditIgst); CreditCessText = R(demand.CreditCess);
        TotalCashText = R(alloc.TotalCash);
        TotalCreditUtilisedText = R(alloc.TotalCreditUtilised);

        IsPosted = _company.GstSetoffLines.Any(l => l.Period == period.Period);

        Subtitle = $"{_company.Name}  —  {period.Label}  —  preview; nothing is posted until you run it";
        StatusText = IsPosted
            ? $"Set-off already run for {period.Label} — credit utilised ₹{TotalCreditUtilisedText}; " +
              $"residual cash ₹{TotalCashText} (incl. cash-only RCM ₹{R(alloc.CashRcm)}). Running again replaces it."
            : $"Preview — credit utilised ₹{TotalCreditUtilisedText}  ·  residual cash payable ₹{TotalCashText} " +
              $"(incl. cash-only RCM ₹{R(alloc.CashRcm)}).";
    }

    /// <summary>This period's still-due cash per head (its residual net of what was already discharged inside the
    /// period). Rebuilt on every preview; the discharge action pays exactly this.</summary>
    private List<(GstTaxHead Head, string Name, long Due)> _cashDue = new();

    /// <summary>The credit this period's ALREADY-POSTED set-off consumed, per head — added back to the pool so the
    /// preview describes the period rather than the aftermath of its own posting.</summary>
    private Func<GstTaxHead, long> PostedSetOffCreditFor(string period)
    {
        var byHead = _company.GstSetoffLines
            .Where(l => l.Period == period && !l.IsCash)
            .GroupBy(l => l.CreditHead)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.AmountPaisa));
        return head => byHead.TryGetValue(head, out var v) ? v : 0L;
    }

    /// <summary>
    /// The cash already drawn for a head by <b>this period's own 3B cash discharge</b> — the netting that stops the
    /// screen inviting a double payment (the engine keys a discharge only by date, so it cannot stop them itself).
    ///
    /// <para>Scoped to the 3B discharge <b>specifically</b>, not to any cash draw in the month: a <b>DRC-03</b>
    /// voluntary payment draws Cr-cash legs carrying an <i>identical</i> <c>CashPayment</c> tag on the same cell, so a
    /// date-window read silently counted it as if the period's 3B liability had been paid — and reduced the cash the
    /// screen demanded. The discriminator is structural rather than a narration match: only
    /// <see cref="GstDepositService.PostCashDischarge"/> debits the <b>Output {head}</b> ledger with a
    /// <c>CashPayment</c> tag; a DRC-03 debits the ITC-reversal cost ledger and carries no such Dr tag.</para>
    /// </summary>
    private long DischargedCashFor(GstTaxHead head, DateOnly from, DateOnly to)
    {
        var cashLedger = _company.FindLedgerByName(GstService.ElectronicCashLedgerName);
        if (cashLedger is null) return 0L;

        var drawn = 0m;
        foreach (var v in _company.Vouchers)
        {
            if (v.Cancelled || v.Date < from || v.Date > to) continue;

            // Is this voucher a 3B cash discharge of THIS head's output tax at all?
            var isThreeBDischarge = v.Lines.Any(l => l.Side == DrCr.Debit
                && l.Gst is { Adjustment: GstAdjustmentKind.CashPayment } dg && dg.TaxHead == head);
            if (!isThreeBDischarge) continue;

            foreach (var line in v.Lines)
                if (line.LedgerId == cashLedger.Id && line.Side == DrCr.Credit
                    && line.Gst is { Adjustment: GstAdjustmentKind.CashPayment } g
                    && g.TaxHead == head && g.RateBasisPoints == (int)GstMinorHead.Tax)
                    drawn += line.Amount.Amount;
        }
        return (long)Math.Round(drawn * 100m, MidpointRounding.AwayFromZero);
    }

    private void AddCashCell(string name, GstTaxHead head, long requiredPaisa)
    {
        var available = _deposit.AvailableCash(head, GstMinorHead.Tax);
        CashCells.Add(new GstCashCellRowVm
        {
            Head = name,
            Available = IndianFormat.AmountAlways(available),
            Required = R(requiredPaisa),
            IsFunded = P(available) >= requiredPaisa,
        });
    }

    // ---------------------------------------------------------------- the explicit actions (the only mutators)

    [RelayCommand] private void RunSetOffAction() => PostSetOff();
    [RelayCommand] private void DepositChallanAction() => DepositChallan();
    [RelayCommand] private void PayResidualCashAction() => PayResidualCash();

    /// <summary>
    /// <b>Runs</b> the previewed Rule-88A set-off for the selected period: posts the Table-6.1 credit-utilisation
    /// Journal through <see cref="SetOff.PostSetOff"/> (which is idempotent per period — re-running replaces the prior
    /// posting). Re-previews afterwards so the credit pool and the closing balances reflect the posting.
    /// </summary>
    public bool PostSetOff()
    {
        Message = null;
        LastActionSucceeded = false;

        var period = SelectedPeriod;
        if (period is null) return Fail("Select a return period first.");
        if (Allocation is null) return Fail("Nothing to run — preview the set-off first.");

        // An all-cash period utilises no credit: PostSetOff would delete any prior posting and return null. Say so
        // rather than reporting a Journal that was never written.
        if (Allocation.Lines.Count == 0)
            return Fail($"No credit is available to utilise for {period.Label} — " +
                        $"the whole liability (₹{TotalCashText}) falls to cash. Deposit a challan and discharge it.");

        Voucher? journal;
        try
        {
            // Belt-and-braces: prove the allocation the user is about to post is legal before posting it. The
            // allocator only ever produces compliant allocations, so this can only fire if the demand shifted.
            SetOff.EnsureLegal(Demand, Allocation);
            journal = _setOff.PostSetOff(period.Period, Allocation, MonthEnd(period));
        }
        catch (InvalidOperationException ex)   // an illegal utilisation, or a missing Output/Input ledger
        {
            return Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        if (journal is null)
            return Fail($"No set-off Journal was posted for {period.Label} — the allocation utilises no credit.");

        _storage.Save(_company);
        var utilised = TotalCreditUtilisedText;
        var cash = TotalCashText;
        Rebuild();
        LastActionSucceeded = true;
        Message = $"Set-off run for {period.Label} — ₹{utilised} of credit utilised. " +
                  (cash == "0.00" ? "Nothing is payable in cash." : $"₹{cash} remains payable in cash.");
        _onChanged();
        return true;
    }

    /// <summary>
    /// Deposits a <b>PMT-06 challan</b> into the electronic cash ledger: debits the chosen major head's cash cell and
    /// credits the bank. The cash ledger is credited only against a real <b>CIN</b> (the engine refuses a challan
    /// without one — a CPIN alone is just an unpaid intent).
    /// </summary>
    public bool DepositChallan()
    {
        Message = null;
        LastActionSucceeded = false;

        var period = SelectedPeriod;
        if (period is null) return Fail("Select a return period first.");
        if (SelectedBank is null) return Fail("Choose the bank / cash ledger the challan is paid from.");
        if (!TryParseRupees(ChallanAmountText, out var amount))
            return Fail($"'{ChallanAmountText}' is not a valid rupee amount.");

        try
        {
            _deposit.PostPmt06(ChallanHead, GstMinorHead.Tax, amount, SelectedBank, MonthEnd(period),
                cpin: Cpin.Trim(), cin: Cin.Trim());
        }
        catch (InvalidOperationException ex)   // no CIN ⇒ the cash ledger is not credited; or a non-paisa amount
        {
            return Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        _storage.Save(_company);
        Rebuild();
        LastActionSucceeded = true;
        Message = $"PMT-06 challan deposited — ₹{IndianFormat.AmountAlways(amount)} into the " +
                  $"{HeadName(ChallanHead)} cash ledger (CIN {Cin.Trim()}).";
        _onChanged();
        return true;
    }

    /// <summary>
    /// Discharges the period's <b>residual cash</b> liability head-by-head out of the electronic cash ledger
    /// (<see cref="GstDepositService.PostCashDischarge"/> — which draws the <b>Tax</b> minor head and refuses an
    /// underfunded cell). All-or-nothing per run: every head is checked to be funded <b>before</b> the first is
    /// discharged, so a half-paid period is impossible.
    /// </summary>
    public bool PayResidualCash()
    {
        Message = null;
        LastActionSucceeded = false;

        var period = SelectedPeriod;
        if (period is null) return Fail("Select a return period first.");
        if (Allocation is null) return Fail("Preview the set-off first.");

        // Only what is STILL due — already-discharged cash is netted out, so a second run cannot pay it twice. The due
        // list now carries the cash-only RCM too, so "nothing left" genuinely means nothing left (it used to mean
        // "nothing left of the four forward heads", which is how a credit-covered period with an outstanding RCM
        // liability got reported as discharged in full).
        var due = _cashDue.Where(x => x.Due > 0).Select(x => (x.Head, Paisa: x.Due)).ToList();

        if (due.Count == 0)
            return Fail(Allocation.TotalCash == 0
                ? $"Nothing is payable in cash for {period.Label} — the credit ledger covered the liability."
                : $"The cash liability for {period.Label} has already been discharged in full.");

        // Check EVERY head is funded before discharging ANY of it (the engine refuses per-head; without this pre-check
        // an under-funded second head would leave the first head paid and the period half-discharged).
        try
        {
            foreach (var (head, paisa) in due)
                _deposit.EnsureCashAvailable(head, GstMinorHead.Tax, new Money(paisa / 100m));
        }
        catch (InvalidOperationException)
        {
            var short_ = due.Where(d => P(_deposit.AvailableCash(d.Head, GstMinorHead.Tax)) < d.Paisa)
                            .Select(d => $"{HeadName(d.Head)} (need ₹{R(d.Paisa)}, have ₹{IndianFormat.AmountAlways(_deposit.AvailableCash(d.Head, GstMinorHead.Tax))})");
            return Fail("The electronic cash ledger is underfunded — deposit a PMT-06 challan first: " +
                        string.Join(", ", short_) + ". Nothing was discharged.");
        }

        try
        {
            foreach (var (head, paisa) in due)
                _deposit.PostCashDischarge(head, new Money(paisa / 100m), MonthEnd(period));
        }
        catch (InvalidOperationException ex)   // a missing Output ledger
        {
            return Fail(ex.Message);
        }

        _storage.Save(_company);
        var paid = R(due.Sum(d => d.Paisa));
        Rebuild();
        LastActionSucceeded = true;
        Message = $"Residual cash discharged for {period.Label} — ₹{paid} paid out of the electronic cash ledger.";
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    private void LoadBankOptions()
    {
        BankOptions.Clear();
        foreach (var l in _company.Ledgers
                     .Where(l => ClassificationRules.IsCashOrBankLedger(l, _company))
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            BankOptions.Add(l);
        SelectedBank = BankOptions.FirstOrDefault();
    }

    private static DateOnly MonthEnd(GstPeriodOption p) => p.MonthStart.AddMonths(1).AddDays(-1);

    internal static string HeadName(GstTaxHead head) => head switch
    {
        GstTaxHead.Central => "CGST",
        GstTaxHead.State => "SGST/UTGST",
        GstTaxHead.Integrated => "IGST",
        GstTaxHead.Cess => "Cess",
        _ => head.ToString(),
    };

    /// <summary>Parses a typed rupee amount; rejects anything non-numeric, non-positive or sub-paisa.</summary>
    private static bool TryParseRupees(string text, out Money amount)
    {
        amount = Money.Zero;
        if (!decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var r))
            return false;
        if (r <= 0m) return false;
        var scaled = r * 100m;
        if (scaled != decimal.Truncate(scaled)) return false;
        amount = new Money(r);
        return true;
    }

    private static long P(Money m) => (long)Math.Round(m.Amount * 100m, MidpointRounding.AwayFromZero);
    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
