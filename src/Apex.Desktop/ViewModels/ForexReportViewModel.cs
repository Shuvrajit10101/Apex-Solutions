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

/// <summary>
/// One presentation row of the Forex Gain/Loss (unrealized revaluation) report (catalog §2/§20
/// Multi-currency; plan.md §10 C-1): a foreign-currency ledger's open balance revalued at an as-of rate,
/// showing the forex balance, the booked base (at transaction rates), the as-of rate, the revalued base,
/// and the signed gain/loss (Gain / Loss). <see cref="IsTotal"/> marks the net footer row.
/// </summary>
public sealed class ForexReportRow
{
    public string Ledger { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;

    /// <summary>The net foreign balance held, right-aligned with a Dr/Cr side.</summary>
    public string ForexBalance { get; init; } = string.Empty;

    /// <summary>The base value booked at the original transaction rates.</summary>
    public string BookedBase { get; init; } = string.Empty;

    /// <summary>The revaluation rate applied (base ₹ per 1 foreign unit).</summary>
    public string Rate { get; init; } = string.Empty;

    /// <summary>The base value at the as-of rate.</summary>
    public string RevaluedBase { get; init; } = string.Empty;

    /// <summary>The signed gain/loss magnitude, right-aligned.</summary>
    public string GainLoss { get; init; } = string.Empty;

    /// <summary>"Gain" / "Loss" / "" for the direction badge column.</summary>
    public string Direction { get; init; } = string.Empty;

    public bool IsTotal { get; init; }
}

/// <summary>
/// The Forex Gain/Loss report page (Reports → Statements of Accounts → Forex Gain/Loss; catalog §2/§20;
/// plan.md §10 C-1). Revalues every open foreign-currency ledger balance at the rate in force on an
/// editable <b>as-of date</b> (or an explicit override rate per currency) and shows the per-ledger
/// unrealized gain/loss plus the net. A "Book adjustment" action posts the balanced adjusting Journal
/// (each foreign ledger moved to its revalued base; the contra to the <b>Forex Gain/Loss</b> ledger)
/// through the engine and persists the company — a real, revertible book entry, not just a display.
///
/// <para>MVVM boundary: references the engine (<see cref="ForexGainLoss"/> / <see cref="LedgerService"/>)
/// and persistence via <see cref="CompanyStorage"/>, but no Avalonia/UI types — so it is headlessly
/// testable. A pure projection until "Book adjustment" is invoked.</para>
/// </summary>
public sealed partial class ForexReportViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "Forex Gain/Loss (Unrealized Revaluation)";
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The as-of revaluation date typed as text (dd-MMM-yyyy); Recompute re-runs the revaluation.</summary>
    [ObservableProperty] private string _asOfText;

    /// <summary>The net gain/loss headline ("Net unrealized gain ₹3,000.00" / "…loss…" / "No forex exposure").</summary>
    [ObservableProperty] private string _netSummary = string.Empty;

    /// <summary>True while the net revaluation is a gain (drives the headline colour in the view).</summary>
    [ObservableProperty] private bool _isNetGain = true;

    /// <summary>True once there is at least one revaluation line the adjustment can book.</summary>
    [ObservableProperty] private bool _canBook;

    /// <summary>Status/error surfaced under the grid (a booked adjustment, a bad date, a missing ledger).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The revaluation rows (one per open foreign-currency ledger) followed by the net row.</summary>
    public ObservableCollection<ForexReportRow> Rows { get; } = new();

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    public ForexReportViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        _asOfText = ComputeDefaultAsOf(company)
            .ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

        Recompute();
    }

    /// <summary>
    /// Re-runs the unrealized-forex revaluation at the current <see cref="AsOfText"/> date and rebuilds the
    /// rows + net summary. No-op-safe on a bad date (surfaces a message and leaves the last result).
    /// </summary>
    public void Recompute()
    {
        Message = null;
        if (!DateOnly.TryParseExact((AsOfText ?? string.Empty).Trim(), "dd-MMM-yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
        {
            Message = "As-of date must be dd-MMM-yyyy (e.g. 31-Mar-2024).";
            return;
        }

        var reval = ForexGainLoss.Revalue(_company, asOf);

        Rows.Clear();
        foreach (var l in reval.Lines)
        {
            Rows.Add(new ForexReportRow
            {
                Ledger = l.LedgerName,
                Currency = CurrencyName(l.CurrencyId),
                ForexBalance = $"{Fmt(l.ForexBalance.Amount)} {(l.BalanceIsDebit ? "Dr" : "Cr")}",
                BookedBase = IndianFormat.Amount(l.BookedBase),
                Rate = l.AsOfRate.ToString("#,##0.####", CultureInfo.InvariantCulture),
                RevaluedBase = IndianFormat.Amount(l.RevaluedBase),
                GainLoss = IndianFormat.Amount(new Money(Math.Abs(l.GainLoss))),
                Direction = l.GainLoss == 0m ? "—" : l.GainLoss > 0m ? "Gain" : "Loss",
            });
        }

        Subtitle = $"{CompanyName}  —  revalued as at {asOf:dd-MMM-yyyy}";

        if (reval.Lines.Count == 0)
        {
            Rows.Add(new ForexReportRow
            {
                Ledger = "No open foreign-currency balances to revalue as at this date.",
            });
            NetSummary = "No forex exposure";
            IsNetGain = true;
            CanBook = false;
            return;
        }

        var net = reval.NetGainLoss;
        Rows.Add(new ForexReportRow
        {
            Ledger = "Net Unrealized Gain / (Loss)",
            GainLoss = IndianFormat.AmountAlways(new Money(Math.Abs(net))),
            Direction = net == 0m ? "—" : net > 0m ? "Gain" : "Loss",
            IsTotal = true,
        });

        IsNetGain = net >= 0m;
        NetSummary = net == 0m
            ? "Net revaluation is nil (gains and losses cancel)."
            : net > 0m
                ? $"Net unrealized gain ₹{IndianFormat.AmountAlways(new Money(net))}"
                : $"Net unrealized loss ₹{IndianFormat.AmountAlways(new Money(-net))}";
        // Bookable when at least one ledger actually moved (a net-zero revaluation across several ledgers
        // still books per-ledger adjustments).
        CanBook = reval.Lines.Any(l => l.GainLoss != 0m);
    }

    partial void OnAsOfTextChanged(string value) => Recompute();

    /// <summary>
    /// Posts the balanced adjusting Journal for the current revaluation through the engine and persists the
    /// company: each foreign-currency ledger moves to its revalued base; the contra goes to the seeded/found
    /// <b>Forex Gain/Loss</b> ledger. Returns the posted voucher, or null when there is nothing to book or a
    /// prerequisite (Journal type / Forex Gain/Loss ledger) is missing (surfaced as a message). After a
    /// successful booking the revaluation re-runs (now nil, since the balances match the as-of rate).
    /// </summary>
    public Voucher? BookAdjustment()
    {
        Message = null;
        if (!DateOnly.TryParseExact((AsOfText ?? string.Empty).Trim(), "dd-MMM-yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
        {
            Message = "As-of date must be dd-MMM-yyyy (e.g. 31-Mar-2024).";
            return null;
        }

        var journalType = _company.VoucherTypes.FirstOrDefault(t =>
            t.BaseType == VoucherBaseType.Journal && t.IsActive)
            ?? _company.VoucherTypes.FirstOrDefault(t => t.BaseType == VoucherBaseType.Journal);
        if (journalType is null)
        {
            Message = "No Journal voucher type is configured to book the adjustment.";
            return null;
        }

        var forexGl = ResolveOrCreateForexGainLossLedger();
        if (forexGl is null)
        {
            Message = "Could not resolve a Forex Gain/Loss ledger to book the adjustment.";
            return null;
        }

        var reval = ForexGainLoss.Revalue(_company, asOf);
        var adjusting = ForexGainLoss.BuildAdjustingJournal(_company, reval, journalType.Id, forexGl.Id);
        if (adjusting is null)
        {
            Message = "Nothing to book — the revaluation is nil.";
            return null;
        }

        try
        {
            var posted = new LedgerService(_company).Post(adjusting);
            _storage.Save(_company);
            _onChanged();
            Message = $"Revaluation booked as Journal No. {posted.Number} on {asOf:dd-MMM-yyyy}.";
            Recompute();
            return posted;
        }
        catch (InvalidVoucherException ex)
        {
            Message = $"Could not book the adjustment: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Finds the conventional <b>Forex Gain/Loss</b> ledger, creating it under Indirect Expenses (and
    /// persisting) if the company does not have one yet — so booking works out of the box.
    /// </summary>
    private DomainLedger? ResolveOrCreateForexGainLossLedger()
    {
        var existing = _company.FindLedgerByName(ForexGainLoss.ForexGainLossLedgerName);
        if (existing is not null) return existing;

        var group = _company.FindGroupByName("Indirect Expenses");
        if (group is null) return null;

        var ledger = new DomainLedger(
            Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName, group.Id,
            Money.Zero, openingIsDebit: true);
        _company.AddLedger(ledger);
        _storage.Save(_company);
        return ledger;
    }

    private static DateOnly ComputeDefaultAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private string CurrencyName(Guid currencyId) =>
        _company.FindCurrency(currencyId) is { } c ? $"{c.FormalName} ({c.Symbol})" : "?";

    private static string Fmt(decimal v) => v.ToString("#,##0.##", CultureInfo.InvariantCulture);
}
