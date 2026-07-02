using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The reusable voucher-entry screen — one view model for Contra (F4), Payment (F5),
/// Receipt (F6), Journal (F7), Sales (F8) and Purchase (F9). It owns the header (voucher-type
/// name, auto voucher number, date), a grid of Dr/Cr particulars lines, a live balance indicator
/// (Σ Dr vs Σ Cr — accept is blocked while unbalanced), and a narration field.
///
/// <para>MVVM boundary: this class references the engine (<see cref="LedgerService"/>) and the
/// persistence via <see cref="CompanyStorage"/>, but no Avalonia/UI types — so it is unit-testable
/// headlessly. On <see cref="Accept"/> it builds a <see cref="Voucher"/>, posts it through
/// <see cref="LedgerService.Post"/> (which rejects an unbalanced/invalid voucher), then persists the
/// whole company aggregate to its <c>.db</c> via <see cref="CompanyStorage.Save"/>.</para>
/// </summary>
public sealed partial class VoucherEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly LedgerService _service;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;

    /// <summary>The voucher type this screen is entering (Payment, Receipt, …).</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header, e.g. "Payment".</summary>
    public string TypeName => _type.Name;

    /// <summary>The company's ledgers each line's picker chooses from.</summary>
    public IReadOnlyList<DomainLedger> Ledgers { get; }

    /// <summary>The editable Dr/Cr particulars lines.</summary>
    public ObservableCollection<VoucherLineViewModel> Lines { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _narration = string.Empty;

    /// <summary>
    /// The date as editable text (dd-MMM-yyyy) for the header TextBox. Setting it with a parseable
    /// value updates <see cref="Date"/>; an unparseable value is kept as-typed and left for Accept
    /// to surface (the engine also rejects a date before books-begin).
    /// </summary>
    public string DateText
    {
        get => Date.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                Date = parsed;
        }
    }

    // Live totals / balance indicator.
    [ObservableProperty] private string _totalDebitText = "0.00";
    [ObservableProperty] private string _totalCreditText = "0.00";
    [ObservableProperty] private string _differenceText = "Balanced";
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private bool _canAccept;

    /// <summary>Error/status line surfaced under the grid (rejected posting, blank rows, …).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned to the voucher once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    public VoucherEntryViewModel(
        Company company,
        VoucherType type,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled,
        DateOnly? date = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _type = type ?? throw new ArgumentNullException(nameof(type));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));
        _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));

        _service = new LedgerService(company);
        Ledgers = company.Ledgers;

        // Default date: last voucher date, else books-begin (never before books, which Post rejects).
        var last = company.Vouchers.Count == 0
            ? (DateOnly?)null
            : company.Vouchers.Max(v => v.Date);
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);
        Title = $"{type.Name} Voucher";

        // Seed two starter lines: the first Dr, the second Cr (opens with a By/To pair).
        AddLine(DrCr.Debit);
        AddLine(DrCr.Credit);
        Recalculate();
    }

    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(DateText));

    /// <summary>Adds a blank particulars line (default side supplied); recomputes the balance.</summary>
    public VoucherLineViewModel AddLine(DrCr side = DrCr.Debit)
    {
        var line = new VoucherLineViewModel(Ledgers, Recalculate, side);
        Lines.Add(line);
        return line;
    }

    /// <summary>Removes a line (keeping a minimum of two); recomputes the balance.</summary>
    public void RemoveLine(VoucherLineViewModel line)
    {
        if (Lines.Count <= 2) return;
        Lines.Remove(line);
        Recalculate();
    }

    /// <summary>Recomputes Σ Dr, Σ Cr, the difference indicator, and whether Accept is allowed.</summary>
    public void Recalculate()
    {
        decimal dr = 0m, cr = 0m;
        foreach (var l in Lines)
        {
            if (l.Side == DrCr.Debit) dr += l.ParsedAmount;
            else cr += l.ParsedAmount;
        }

        TotalDebitText = IndianFormat.AmountAlways(dr);
        TotalCreditText = IndianFormat.AmountAlways(cr);

        var diff = dr - cr;
        IsBalanced = diff == 0m && dr > 0m;

        if (diff == 0m)
            DifferenceText = dr > 0m ? "Balanced" : "Nil";
        else if (diff > 0m)
            DifferenceText = $"Debit short/Credit excess by {IndianFormat.AmountAlways(Math.Abs(diff))}";
        else
            DifferenceText = $"Credit short/Debit excess by {IndianFormat.AmountAlways(Math.Abs(diff))}";

        // Accept requires: at least two complete lines, no half-filled row, and balanced (>0).
        var completeLines = Lines.Count(l => l.IsComplete);
        var hasHalfFilledRow = Lines.Any(l => !l.IsBlank && !l.IsComplete);
        CanAccept = IsBalanced && completeLines >= 2 && !hasHalfFilledRow;
    }

    /// <summary>
    /// Ctrl+A accept: builds the voucher from the non-blank lines, posts it (engine rejects an
    /// unbalanced/invalid voucher — nothing persists on failure), then saves the company to its
    /// <c>.db</c>. On success surfaces the assigned number and returns to the Gateway.
    /// </summary>
    public bool Accept()
    {
        Message = null;

        // Reject half-filled rows up front with a clear message (before touching the engine).
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every entered line needs a ledger and a positive amount.";
            return false;
        }

        var entryLines = Lines
            .Where(l => l.IsComplete)
            .Select(l => new EntryLine(l.SelectedLedger!.Id, new Money(l.ParsedAmount), l.Side))
            .ToList();

        if (entryLines.Count < 2)
        {
            Message = "A voucher needs at least two lines.";
            return false;
        }

        var voucher = new Voucher(
            Guid.NewGuid(),
            _type.Id,
            Date,
            entryLines,
            number: 0, // let the engine assign the automatic number
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim());

        try
        {
            var posted = _service.Post(voucher); // throws on unbalanced/invalid — never persisted
            _storage.Save(_company);             // persist the whole aggregate to the .db
            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {posted.Number} accepted.";
            _onSaved();
            return true;
        }
        catch (UnbalancedVoucherException)
        {
            Message = $"Voucher is out of balance (Dr {TotalDebitText} ≠ Cr {TotalCreditText}). Not saved.";
            return false;
        }
        catch (InvalidVoucherException ex)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    /// <summary>Esc / Alt+X cancel: discards the in-progress voucher and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();
}
