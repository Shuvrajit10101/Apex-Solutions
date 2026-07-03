using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One Dr/Cr particulars line in the voucher-entry grid: the picked ledger, the side
/// (Debit/Credit — Dr/By and Cr/To), and the amount typed as text. Parsing/validation
/// is deferred to the parent <see cref="VoucherEntryViewModel"/>; this class only holds the
/// editable state and raises change notifications so the live balance updates as the user types.
///
/// <para><b>Bill-wise (catalog §5):</b> when the picked ledger maintains balances bill-by-bill,
/// <see cref="IsBillWise"/> turns on and the line owns a "Bill-wise Details" sub-panel — a list of
/// <see cref="BillAllocationRowViewModel"/> whose amounts must <b>sum to the line amount</b> (the
/// split). A non-bill-wise line carries no allocations, so existing vouchers are unaffected.</para>
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

    /// <summary>True when the picked ledger maintains balances bill-by-bill ⇒ show the sub-panel.</summary>
    [ObservableProperty] private bool _isBillWise;

    /// <summary>Running text under the sub-panel: allocated total vs the line amount, with the shortfall.</summary>
    [ObservableProperty] private string _billSummary = string.Empty;

    /// <summary>The editable bill-wise allocation rows for this line (empty for a non-bill-wise line).</summary>
    public ObservableCollection<BillAllocationRowViewModel> BillAllocations { get; } = new();

    public VoucherLineViewModel(IReadOnlyList<DomainLedger> ledgers, Action onChanged, DrCr side = DrCr.Debit)
    {
        Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _side = side;
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value)
    {
        SyncBillWise();
        _onChanged();
    }

    partial void OnSideChanged(DrCr value) => _onChanged();

    partial void OnAmountTextChanged(string value)
    {
        RecomputeBillSummary();
        _onChanged();
    }

    /// <summary>
    /// Reflects the picked ledger's bill-by-bill flag: when a bill-wise ledger is chosen the sub-panel
    /// turns on and a first New-Ref row is seeded (defaulting its amount + name to the line so the common
    /// single-bill case needs no typing); when a non-bill-wise ledger is chosen the panel and its rows are
    /// cleared, so switching a line back never leaves stray allocations behind.
    /// </summary>
    private void SyncBillWise()
    {
        var on = SelectedLedger?.MaintainBillByBill == true;
        if (on == IsBillWise && (!on || BillAllocations.Count > 0))
        {
            RecomputeBillSummary();
            return;
        }

        IsBillWise = on;
        if (on)
        {
            if (BillAllocations.Count == 0)
                AddBillAllocation(BillRefType.NewRef);
        }
        else
        {
            BillAllocations.Clear();
        }
        RecomputeBillSummary();
    }

    /// <summary>Adds a blank bill-wise allocation row; recomputes the split summary and the balance.</summary>
    public BillAllocationRowViewModel AddBillAllocation(BillRefType refType = BillRefType.NewRef)
    {
        var row = new BillAllocationRowViewModel(OnBillRowChanged, refType);
        BillAllocations.Add(row);
        RecomputeBillSummary();
        return row;
    }

    /// <summary>Removes a bill-wise allocation row (keeps at least one on a bill-wise line).</summary>
    public void RemoveBillAllocation(BillAllocationRowViewModel row)
    {
        if (BillAllocations.Count <= 1) return;
        BillAllocations.Remove(row);
        RecomputeBillSummary();
        _onChanged();
    }

    private void OnBillRowChanged()
    {
        RecomputeBillSummary();
        _onChanged();
    }

    /// <summary>Σ of the allocation row magnitudes on this line.</summary>
    public decimal BillAllocatedTotal
    {
        get
        {
            var sum = 0m;
            foreach (var a in BillAllocations) sum += a.ParsedAmount;
            return sum;
        }
    }

    /// <summary>
    /// True when the bill-wise split is valid: not bill-wise (no constraint), or the touched rows are all
    /// complete and their amounts sum EXACTLY to the line amount (the split, enforced by the engine too).
    /// </summary>
    public bool BillSplitOk
    {
        get
        {
            if (!IsBillWise) return true;
            if (BillAllocations.Any(a => !a.IsBlank && !a.IsComplete)) return false;
            var complete = BillAllocations.Where(a => a.IsComplete).ToList();
            if (complete.Count == 0) return false;
            return complete.Sum(a => a.ParsedAmount) == ParsedAmount && ParsedAmount > 0m;
        }
    }

    private void RecomputeBillSummary()
    {
        if (!IsBillWise) { BillSummary = string.Empty; return; }

        var allocated = BillAllocatedTotal;
        var line = ParsedAmount;
        var diff = line - allocated;
        if (diff == 0m && line > 0m)
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  fully allocated";
        else if (diff > 0m)
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  {Fmt(diff)} unallocated";
        else
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  over-allocated by {Fmt(-diff)}";
    }

    private static string Fmt(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The domain bill allocations for this line — the complete rows turned into <see cref="BillAllocation"/>.
    /// Empty for a non-bill-wise line (so the built <see cref="EntryLine"/> carries none).
    /// </summary>
    public IReadOnlyList<BillAllocation> ToBillAllocations()
    {
        if (!IsBillWise) return Array.Empty<BillAllocation>();
        return BillAllocations.Where(a => a.IsComplete).Select(a => a.ToAllocation()).ToList();
    }

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
