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
        // The allocation persists through Paisa.FromMoney (SqliteCompanyStore InsertBillAllocations), which
        // THROWS on a sub-paisa figure. Refuse it at the one choke point every caller flows through instead, the
        // way AdditionalCostLine / GstLineTax / TcsLineTax / GstChallan already do. Without it the screens' only
        // gate is an exact-SUM check — and 33.335 + 66.665 == 100.00 passes it, so a sub-paisa allocation reached
        // a POSTED voucher and blew up in the store, on two Accept paths that Post before they Save and do not
        // roll back. Both the canonical-XML import (ImportPlan.BuildBillAllocation) and the SQLite read path
        // build from INTEGER paisa, so neither can ever trip this.
        //
        // FitsPaisaStore, not IsPaisaExact: "storable" is magnitude AND exactness, and a guard that tested only
        // the second half would THROW OverflowException on the very input it exists to refuse — the paisa
        // predicate scales by a hundred, which overflows decimal past ~7.9e26, and the store's own conversion then
        // narrows to long, which overflows past 17 rupee digits. FitsPaisaStore owns that branch ORDER (magnitude
        // first); re-deriving it here would be the D3 drift the campaign removes.
        if (!PaisaConversion.FitsPaisaStore(amount.Amount))
            throw new InvalidOperationException(
                $"A bill allocation amount {amount.Amount} cannot be stored as integer paisa: it must be "
              + $"paisa-exact (2 decimal places) and no larger than {PaisaConversion.MaxStorableRupees}.");
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
