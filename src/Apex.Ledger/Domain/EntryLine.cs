namespace Apex.Ledger.Domain;

/// <summary>
/// One posting within a voucher: a ledger, a positive amount, and a side
/// (catalog §4; plan.md §4.1). The side carries the direction — amounts are
/// always magnitudes &gt; 0.
/// </summary>
/// <remarks>
/// Extension seam (later phases, empty in Phase 1): bill refs, inventory,
/// cost, tax and bank allocations hang off a line. They are intentionally
/// absent here so the Phase-1 shape stays minimal.
/// </remarks>
public sealed class EntryLine
{
    /// <summary>The account posted to.</summary>
    public Guid LedgerId { get; }

    /// <summary>Magnitude, always &gt; 0 (a zero line is invalid in Phase 1).</summary>
    public Money Amount { get; }

    /// <summary>Debit or Credit.</summary>
    public DrCr Side { get; }

    public EntryLine(Guid ledgerId, Money amount, DrCr side)
    {
        LedgerId = ledgerId;
        Amount = amount;
        Side = side;
    }

    /// <summary>Signed contribution: +amount for a debit, −amount for a credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? Amount.Amount : -Amount.Amount;
}
