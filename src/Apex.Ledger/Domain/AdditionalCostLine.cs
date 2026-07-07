namespace Apex.Ledger.Domain;

/// <summary>
/// One <b>additional-cost line</b> carried on a Stock-Journal <b>transfer</b> <see cref="InventoryVoucher"/>
/// (Phase 6 slice 3 RQ-20): a reference to an additional-cost <see cref="Ledger"/> (a Direct-Expenses ledger
/// with a non-null <see cref="Ledger.MethodOfAppropriation"/>) plus the <see cref="Amount"/> to apportion across
/// the transfer's destination allocations. It exists ONLY for the Stock-Journal-transfer variant — a Purchase
/// item-invoice needs no such row because there the additional cost is an ordinary <see cref="EntryLine"/> Dr to
/// the Direct-Expenses ledger and the engine derives the apportionment from that line + the ledger's method.
/// </summary>
public sealed class AdditionalCostLine
{
    /// <summary>The additional-cost <see cref="Ledger"/> this cost posts to; its
    /// <see cref="Ledger.MethodOfAppropriation"/> decides by-quantity vs by-value apportionment.</summary>
    public Guid LedgerId { get; }

    /// <summary>The paisa-exact additional-cost amount to apportion across the destination allocations.</summary>
    public Money Amount { get; }

    public AdditionalCostLine(Guid ledgerId, Money amount)
    {
        if (amount.Amount < 0m)
            throw new ArgumentException("An additional-cost amount must be ≥ 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException(
                $"Additional-cost amount {amount.Amount} must be to the paisa (2 decimal places).");

        LedgerId = ledgerId;
        Amount = amount;
    }
}
