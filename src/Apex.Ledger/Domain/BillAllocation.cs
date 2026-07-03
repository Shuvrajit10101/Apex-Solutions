namespace Apex.Ledger.Domain;

/// <summary>
/// One bill-wise allocation hung off an <see cref="EntryLine"/> whose ledger maintains
/// balances bill-by-bill (catalog §5; plan.md §5). It ties a slice of the line amount to a
/// named bill reference of a given <see cref="BillRefType"/>, with an optional due date used
/// for ageing. A single line may carry several allocations whose amounts <b>sum to the line
/// amount</b> (the "split" behaviour), so this is a value object with no identity of its own.
/// </summary>
/// <remarks>
/// The <see cref="Amount"/> is a magnitude &gt; 0 (it inherits the line's Dr/Cr side). For
/// <see cref="BillRefType.NewRef"/> and <see cref="BillRefType.Advance"/>, <see cref="Name"/> is
/// the new bill's reference id. For <see cref="BillRefType.AgstRef"/>, <see cref="Name"/> is the
/// id of the existing open bill this allocation knocks off. For <see cref="BillRefType.OnAccount"/>,
/// <see cref="Name"/> may be empty (unallocated). Bill references carry the GST-inclusive amount.
/// </remarks>
public sealed class BillAllocation
{
    /// <summary>New/Agst/Advance/On-Account.</summary>
    public BillRefType RefType { get; }

    /// <summary>The bill reference id. Required except for <see cref="BillRefType.OnAccount"/>.</summary>
    public string Name { get; }

    /// <summary>Allocated magnitude, always &gt; 0. Inherits the parent line's Dr/Cr side.</summary>
    public Money Amount { get; }

    /// <summary>
    /// Explicit due date, or <c>null</c> to derive it from the voucher date + credit period.
    /// Never set for <see cref="BillRefType.Advance"/> / <see cref="BillRefType.OnAccount"/>.
    /// </summary>
    public DateOnly? DueDate { get; }

    /// <summary>
    /// Credit period in days, used when <see cref="DueDate"/> is null: due date = voucher date +
    /// this many days. Null ⇒ due on the voucher date (no credit period).
    /// </summary>
    public int? CreditPeriodDays { get; }

    public BillAllocation(
        BillRefType refType,
        string name,
        Money amount,
        DateOnly? dueDate = null,
        int? creditPeriodDays = null)
    {
        if (amount.Amount <= 0m)
            throw new ArgumentException("A bill allocation amount must be > 0.", nameof(amount));
        if (refType != BillRefType.OnAccount && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "A bill reference name is required for New/Agst/Advance allocations.", nameof(name));
        if (creditPeriodDays is < 0)
            throw new ArgumentException("Credit period days must be ≥ 0.", nameof(creditPeriodDays));

        RefType = refType;
        Name = name ?? string.Empty;
        Amount = amount;
        DueDate = dueDate;
        CreditPeriodDays = creditPeriodDays;
    }

    /// <summary>
    /// The effective due date for ageing: the explicit <see cref="DueDate"/> if set, else the
    /// voucher date advanced by the allocation's own <see cref="CreditPeriodDays"/>, else by the
    /// ledger's default credit period (<paramref name="ledgerDefaultCreditDays"/>), else 0.
    /// Advance/On-Account have no meaningful due date and simply return the voucher date.
    /// </summary>
    public DateOnly EffectiveDueDate(DateOnly voucherDate, int? ledgerDefaultCreditDays = null)
        => DueDate ?? voucherDate.AddDays(CreditPeriodDays ?? ledgerDefaultCreditDays ?? 0);
}
