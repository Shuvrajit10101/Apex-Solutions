namespace Apex.Ledger.Domain;

/// <summary>
/// A single balanced transaction: header + entry lines (catalog §4; plan.md §4.1).
/// A posted voucher must satisfy Σ Dr = Σ Cr over its lines.
/// </summary>
public sealed class Voucher
{
    private readonly List<EntryLine> _lines;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The <see cref="VoucherType"/> this voucher belongs to.</summary>
    public Guid TypeId { get; }

    /// <summary>Sequence within its type (see numbering §8.3). 0 when numbering is None.</summary>
    public int Number { get; set; }

    /// <summary>Voucher date; must be ≥ the company's books-begin date.</summary>
    public DateOnly Date { get; }

    /// <summary>Free text.</summary>
    public string? Narration { get; set; }

    /// <summary>Optional party ledger (invoice types).</summary>
    public Guid? PartyId { get; set; }

    /// <summary>The entry lines; ≥ 2 and balanced for a valid voucher.</summary>
    public IReadOnlyList<EntryLine> Lines => _lines;

    /// <summary>Alt+X — number retained in sequence, zero effect on balances.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Ctrl+L — excluded from live balances until regularised.</summary>
    public bool Optional { get; set; }

    /// <summary>Ctrl+T — excluded from balances until its date is reached.</summary>
    public bool PostDated { get; set; }

    public Voucher(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<EntryLine> lines,
        int number = 0,
        string? narration = null,
        Guid? partyId = null,
        bool cancelled = false,
        bool optional = false,
        bool postDated = false)
    {
        Id = id;
        TypeId = typeId;
        Date = date;
        _lines = lines?.ToList() ?? throw new ArgumentNullException(nameof(lines));
        Number = number;
        Narration = narration;
        PartyId = partyId;
        Cancelled = cancelled;
        Optional = optional;
        PostDated = postDated;
    }

    /// <summary>Sum of debit-line magnitudes.</summary>
    public Money TotalDebit
    {
        get
        {
            var sum = 0m;
            foreach (var l in _lines)
                if (l.Side == DrCr.Debit) sum += l.Amount.Amount;
            return new Money(sum);
        }
    }

    /// <summary>Sum of credit-line magnitudes.</summary>
    public Money TotalCredit
    {
        get
        {
            var sum = 0m;
            foreach (var l in _lines)
                if (l.Side == DrCr.Credit) sum += l.Amount.Amount;
            return new Money(sum);
        }
    }
}
