namespace Apex.Ledger.Domain;

/// <summary>
/// One posting within a voucher: a ledger, a positive amount, and a side
/// (catalog §4; plan.md §4.1). The side carries the direction — amounts are
/// always magnitudes &gt; 0.
/// </summary>
/// <remarks>
/// Extension seam (later phases): inventory, cost, tax and bank allocations hang off a
/// line too. <b>Phase 2</b> adds <see cref="BillAllocations"/> — the bill-wise slices for a
/// line whose ledger maintains balances bill-by-bill; a line with no bill-wise ledger simply
/// carries an empty list, so existing non-bill-wise vouchers are unaffected. The same slice
/// adds <see cref="CostAllocations"/> — the cost-centre slices for a line whose ledger has
/// cost centres applicable. The banking slice adds <see cref="BankAllocation"/> — a single
/// optional bank detail (transaction type + instrument + bank date) for a line whose ledger is a
/// bank account. All three are OPTIONAL trailing params, so existing callers are unaffected.
/// </remarks>
public sealed class EntryLine
{
    private readonly List<BillAllocation> _billAllocations;
    private readonly List<CostAllocation> _costAllocations;

    /// <summary>The account posted to.</summary>
    public Guid LedgerId { get; }

    /// <summary>Magnitude, always &gt; 0 (a zero line is invalid in Phase 1).</summary>
    public Money Amount { get; }

    /// <summary>Debit or Credit.</summary>
    public DrCr Side { get; }

    /// <summary>
    /// The bill-wise allocations for this line (catalog §5). Empty for a non-bill-wise line.
    /// When present, their amounts must sum to <see cref="Amount"/> (enforced at posting).
    /// </summary>
    public IReadOnlyList<BillAllocation> BillAllocations => _billAllocations;

    /// <summary>True iff this line carries one or more bill-wise allocations.</summary>
    public bool HasBillAllocations => _billAllocations.Count > 0;

    /// <summary>
    /// The cost-centre allocations for this line (catalog §6). Empty for a line whose ledger has no
    /// cost centres applicable. When present, their amounts must sum to <see cref="Amount"/> (enforced
    /// at posting).
    /// </summary>
    public IReadOnlyList<CostAllocation> CostAllocations => _costAllocations;

    /// <summary>True iff this line carries one or more cost-centre allocations.</summary>
    public bool HasCostAllocations => _costAllocations.Count > 0;

    /// <summary>
    /// The bank-allocation detail for this line (catalog §8), or <c>null</c> for a line whose ledger is
    /// not a bank account. When present, its whole detail covers this line's amount (a bank line is not
    /// split); its <see cref="Domain.BankAllocation.BankDate"/> drives Bank Reconciliation.
    /// </summary>
    public BankAllocation? BankAllocation { get; }

    /// <summary>True iff this line carries a bank allocation.</summary>
    public bool HasBankAllocation => BankAllocation is not null;

    public EntryLine(
        Guid ledgerId,
        Money amount,
        DrCr side,
        IEnumerable<BillAllocation>? billAllocations = null,
        IEnumerable<CostAllocation>? costAllocations = null,
        BankAllocation? bankAllocation = null)
    {
        LedgerId = ledgerId;
        Amount = amount;
        Side = side;
        _billAllocations = billAllocations?.ToList() ?? new List<BillAllocation>();
        _costAllocations = costAllocations?.ToList() ?? new List<CostAllocation>();
        BankAllocation = bankAllocation;
    }

    /// <summary>Signed contribution: +amount for a debit, −amount for a credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? Amount.Amount : -Amount.Amount;

    /// <summary>Σ of the bill-allocation magnitudes on this line.</summary>
    public Money BillAllocationTotal
    {
        get
        {
            var sum = 0m;
            foreach (var a in _billAllocations) sum += a.Amount.Amount;
            return new Money(sum);
        }
    }

    /// <summary>Σ of the cost-allocation magnitudes on this line.</summary>
    public Money CostAllocationTotal
    {
        get
        {
            var sum = 0m;
            foreach (var a in _costAllocations) sum += a.Amount.Amount;
            return new Money(sum);
        }
    }
}
