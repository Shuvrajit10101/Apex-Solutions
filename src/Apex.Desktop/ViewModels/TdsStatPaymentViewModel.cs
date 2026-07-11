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
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>A remaining-payable section choice offered on the Stat-Payment form (defaults the amount/section).</summary>
public sealed class StatPaymentSectionOption
{
    public string Section { get; init; } = string.Empty;
    public Money Remaining { get; init; } = Money.Zero;
    public string Display => $"{Section}  —  remaining {IndianFormat.AmountAlways(Remaining)}";
}

/// <summary>
/// The <b>TDS Stat Payment</b> page (Payment voucher · Tally "Ctrl+F"; Phase 7 slice 3; catalog §13): deposits the
/// accrued "TDS Payable" liability into the bank and records the ITNS-281 challan it produced. It debits TDS
/// Payable and credits the chosen Bank/Cash ledger through the engine (<see cref="TdsDepositService"/> +
/// <see cref="LedgerService.Post"/>, so the balance invariant runs), then records + links the challan
/// (challan no / BSR / deposit date / section / minor head). Driving the payable back toward zero mirrors how a
/// GSTR-3B net liability is discharged by a real payment against the payable ledger.
///
/// <para>Gated: only reachable when TDS is enabled (the menu item is itself gated on
/// <see cref="Company.TdsEnabled"/>), so a non-TDS company never sees it (ER-13). MVVM boundary: engine +
/// persistence only, no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class TdsStatPaymentViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly TdsDepositService _deposit;
    private readonly DateOnly _asOf;
    private bool _updating;

    [ObservableProperty] private string _outstandingText = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private DomainLedger? _selectedBank;
    [ObservableProperty] private StatPaymentSectionOption? _selectedSection;
    [ObservableProperty] private string _challanNo = string.Empty;
    [ObservableProperty] private string _bsrCode = string.Empty;
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _minorHead = "200";
    [ObservableProperty] private string _depositDateText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastDepositSucceeded;

    /// <summary>The Bank / Cash ledgers the deposit can be paid from (cash &amp; bank group members).</summary>
    public ObservableCollection<DomainLedger> BankOptions { get; } = new();

    /// <summary>The sections still carrying an outstanding payable (defaults the amount + section on selection).</summary>
    public ObservableCollection<StatPaymentSectionOption> SectionOptions { get; } = new();

    public TdsStatPaymentViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _deposit = new TdsDepositService(company);
        _asOf = ComputeAsOf(company);
        _depositDateText = _asOf.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        LoadBankOptions();
        Rebuild();
    }

    /// <summary>The as-of date used for the outstanding-payable projection and the default deposit date.</summary>
    public DateOnly AsOf => _asOf;

    /// <summary>(Re)computes the outstanding payable + per-section remaining and refreshes the defaults.</summary>
    public void Rebuild()
    {
        var outstanding = _deposit.OutstandingPayable(_asOf);
        OutstandingText = IndianFormat.AmountAlways(outstanding);

        var from = _company.FinancialYearStart;
        var to = _company.FinancialYearStart.AddYears(1).AddDays(-1);
        var recon = ChallanReconciliation.Build(_company, from, to);

        SectionOptions.Clear();
        foreach (var s in recon.Sections.Where(s => s.IsUnderpaid))
            SectionOptions.Add(new StatPaymentSectionOption { Section = s.Section, Remaining = s.Remaining });

        // Default to the largest still-short section (so a single deposit zeroes that section's liability).
        var first = SectionOptions.OrderByDescending(o => o.Remaining.Amount).FirstOrDefault();
        SelectedSection = first;
        if (first is null)
        {
            // Nothing outstanding — clear the amount/section so an accidental post is rejected with a clear message.
            _updating = true;
            AmountText = IndianFormat.AmountAlways(outstanding);
            _updating = false;
        }
    }

    partial void OnSelectedSectionChanged(StatPaymentSectionOption? value)
    {
        if (_updating || value is null) return;
        _updating = true;
        SectionCode = value.Section;
        AmountText = value.Remaining.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        _updating = false;
    }

    /// <summary>
    /// Ctrl+A / the Deposit button: validates the amount, bank, challan no / BSR / section / minor head and the
    /// deposit date; builds + posts the Stat-Payment (Dr TDS Payable / Cr Bank) through the engine; records + links
    /// the ITNS-281 challan; persists; then refreshes the outstanding + section defaults. Returns true on success.
    /// </summary>
    public bool Deposit()
    {
        Message = null;
        LastDepositSucceeded = false;

        if (_company.Tds is not { Enabled: true })
        {
            Message = "Enable TDS (F11 → Enable TDS) before depositing.";
            return false;
        }
        if (SelectedBank is null)
        {
            Message = "Choose the Bank / Cash ledger to pay the deposit from.";
            return false;
        }
        if (!TryParseAmount(AmountText, out var amount))
        {
            Message = "The deposit amount must be a rupee amount greater than zero (e.g. 5000).";
            return false;
        }
        // "TDS Payable" is a single ledger, but the section options are per-section: guard against depositing more
        // than the whole outstanding liability (e.g. overriding one section's amount to cover several). Without this
        // cap an over-section deposit drives the payable negative (a debit) that OutstandingPayable would clamp to
        // zero and hide, while the recon re-lists the still-short sections and would invite a second, over-deposit.
        var outstanding = _deposit.OutstandingPayable(_asOf);
        if (amount.Amount > outstanding.Amount)
        {
            Message = $"The deposit {IndianFormat.AmountAlways(amount)} exceeds the outstanding TDS Payable " +
                      $"{IndianFormat.AmountAlways(outstanding)}. Deposit at most the outstanding liability.";
            return false;
        }
        var challanNo = (ChallanNo ?? string.Empty).Trim();
        var bsr = (BsrCode ?? string.Empty).Trim();
        var section = (SectionCode ?? string.Empty).Trim();
        var minorHead = (MinorHead ?? string.Empty).Trim();
        if (challanNo.Length == 0) { Message = "The challan (CIN) number is required."; return false; }
        if (bsr.Length == 0) { Message = "The BSR code of the collecting branch is required."; return false; }
        if (section.Length == 0) { Message = "The income-tax section (e.g. 194J(b)) is required."; return false; }
        if (minorHead.Length == 0) { Message = "The ITNS-281 minor head is required (200 or 400)."; return false; }
        if (!TryParseDate(DepositDateText, out var depositDate))
        {
            Message = "The deposit date must be a valid date (dd-MM-yyyy).";
            return false;
        }

        try
        {
            var statType = _deposit.EnsureStatPaymentType();
            var voucher = _deposit.BuildStatPayment(amount, SelectedBank, depositDate, statType);
            var posted = new LedgerService(_company).Post(voucher); // throws on unbalanced/invalid — never persisted
            _deposit.RecordChallan(challanNo, bsr, depositDate, amount, section, minorHead, posted);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Deposit failed: {ex.Message}";
            return false;
        }

        LastDepositSucceeded = true;
        Rebuild();
        _onChanged();
        Message = $"Deposited {IndianFormat.AmountAlways(amount)} for {section} " +
                  $"(challan {challanNo}).  Outstanding now {OutstandingText}.";
        // Clear the challan identifiers so a second deposit does not silently reuse them.
        ChallanNo = string.Empty;
        BsrCode = string.Empty;
        return true;
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

    private static bool TryParseAmount(string? text, out Money amount)
    {
        amount = Money.Zero;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return false;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var rupees) || rupees <= 0m)
            return false;
        var m = Money.FromRupees(rupees);
        if (!m.IsPaisaExact) return false;
        amount = m;
        return true;
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        var t = (text ?? string.Empty).Trim();
        return DateOnly.TryParseExact(t, new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }
}
