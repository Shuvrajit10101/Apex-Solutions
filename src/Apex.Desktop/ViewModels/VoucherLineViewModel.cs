using System;
using System.Collections.Generic;
using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One Dr/Cr particulars line in the voucher-entry grid: the picked ledger, the side
/// (Debit/Credit — Tally's Dr/By and Cr/To), and the amount typed as text. Parsing/validation
/// is deferred to the parent <see cref="VoucherEntryViewModel"/>; this class only holds the
/// editable state and raises change notifications so the live balance updates as the user types.
/// </summary>
public sealed partial class VoucherLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The company's ledgers the picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<DomainLedger> Ledgers { get; }

    /// <summary>The two sides a line can post to (Dr = Debit, Cr = Credit).</summary>
    public IReadOnlyList<DrCr> Sides { get; } = new[] { DrCr.Debit, DrCr.Credit };

    [ObservableProperty] private DomainLedger? _selectedLedger;
    [ObservableProperty] private DrCr _side = DrCr.Debit;
    [ObservableProperty] private string _amountText = string.Empty;

    public VoucherLineViewModel(IReadOnlyList<DomainLedger> ledgers, Action onChanged, DrCr side = DrCr.Debit)
    {
        Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _side = side;
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value) => _onChanged();
    partial void OnSideChanged(DrCr value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();

    /// <summary>True when this line is fully specified: a ledger picked and a positive amount typed.</summary>
    public bool IsComplete => SelectedLedger is not null && TryParseAmount(out var amt) && amt > 0m;

    /// <summary>True when the row has been touched at all (ledger or amount) — a blank row is ignored.</summary>
    public bool IsBlank => SelectedLedger is null && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>The parsed amount (0 when unparsable/blank).</summary>
    public decimal ParsedAmount => TryParseAmount(out var amt) ? amt : 0m;

    /// <summary>Signed contribution to the Dr−Cr balance: +amount for a debit, −amount for a credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? ParsedAmount : -ParsedAmount;

    private bool TryParseAmount(out decimal amount)
        => decimal.TryParse(
            (AmountText ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out amount);
}
