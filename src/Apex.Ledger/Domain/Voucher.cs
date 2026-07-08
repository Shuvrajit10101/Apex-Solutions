namespace Apex.Ledger.Domain;

/// <summary>
/// A single balanced transaction: header + entry lines (catalog §4; plan.md §4.1).
/// A posted voucher must satisfy Σ Dr = Σ Cr over its lines.
/// </summary>
public sealed class Voucher
{
    private readonly List<EntryLine> _lines;
    private readonly List<VoucherInventoryLine> _inventoryLines;
    private readonly List<PosTender> _posTenders;

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

    /// <summary>
    /// The <b>Item-Invoice</b> stock lines (catalog §10; phase3-inventory-requirements RQ-16/RQ-17; slice
    /// 3.3b) — present ONLY on a Purchase/Sales voucher run in item-invoice mode. Empty for every other
    /// voucher, so an ordinary accounting voucher behaves exactly as before. When present, the voucher both
    /// posts its balanced Dr/Cr <see cref="Lines"/> AND moves stock (inward for Purchase, outward for Sales);
    /// the two arms are posted atomically by <c>LedgerService</c> and their pairing is enforced by
    /// <c>VoucherValidator</c>. The lines' <see cref="VoucherInventoryLine.Direction"/> is stamped to the
    /// voucher-nature-implied direction at posting time.
    /// </summary>
    public IReadOnlyList<VoucherInventoryLine> InventoryLines => _inventoryLines;

    /// <summary>True iff this voucher carries item-invoice stock lines (item-invoice mode).</summary>
    public bool HasInventoryLines => _inventoryLines.Count > 0;

    /// <summary>Σ of the item-invoice line values (each qty × rate, paisa-exact) — the stock value that the
    /// pairing invariant reconciles against the voucher's stock/purchase/sales accounting amount.</summary>
    public Money InventoryLinesValue
    {
        get
        {
            var sum = Money.Zero;
            foreach (var l in _inventoryLines) sum += l.Value;
            return sum;
        }
    }

    /// <summary>
    /// The <b>POS payment tenders</b> (catalog §11; Phase 6 slice 7 RQ-39/RQ-40; DP-6) — present ONLY on a POS
    /// Sales voucher (a Sales type flagged <see cref="VoucherType.UseForPos"/>). <b>Empty for every other
    /// voucher</b>, so an ordinary sale behaves exactly as before (ER-13). When present, the single customer debit
    /// is replaced by a split of tender debits (one <see cref="Lines"/> Dr per tender, paired 1:1 with these
    /// records); the credit side (Cr Sales + Cr Output GST) is byte-identical to a normal sale. This list is pure
    /// metadata (tender kind, cash tendered/change, card/bank/cheque); the accounting effect lives in
    /// <see cref="Lines"/>. Added exactly like <see cref="InventoryLines"/> — an optional ctor param — so round-trip
    /// and reporting stay trivial (DP-6: no persisted POS session object).
    /// </summary>
    public IReadOnlyList<PosTender> PosTenders => _posTenders;

    /// <summary>True iff this voucher carries POS payment tenders (POS mode).</summary>
    public bool HasPosTenders => _posTenders.Count > 0;

    /// <summary>Σ of the POS tender <see cref="PosTender.Amount"/> shares (the posted payable split, paisa-exact) —
    /// the value the tender-reconciliation invariant checks against the bill total / total debit.</summary>
    public Money PosTendersValue
    {
        get
        {
            var sum = Money.Zero;
            foreach (var t in _posTenders) sum += t.Amount;
            return sum;
        }
    }

    /// <summary>Alt+X — number retained in sequence, zero effect on balances.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Ctrl+L — excluded from live balances until regularised.</summary>
    public bool Optional { get; set; }

    /// <summary>Ctrl+T — excluded from balances until its date is reached.</summary>
    public bool PostDated { get; set; }

    /// <summary>
    /// "Applicable upto" date for a <see cref="VoucherBaseType.ReversingJournal"/> (catalog §7): the
    /// last date on which the reversing entry is in force. Under a scenario it affects reports only for
    /// as-of dates ≤ this value; on/after it lapses (reverses out). <c>null</c> for every other voucher.
    /// </summary>
    public DateOnly? ApplicableUpto { get; set; }

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
        bool postDated = false,
        DateOnly? applicableUpto = null,
        IEnumerable<VoucherInventoryLine>? inventoryLines = null,
        IEnumerable<PosTender>? posTenders = null)
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
        ApplicableUpto = applicableUpto;
        _inventoryLines = inventoryLines?.ToList() ?? new List<VoucherInventoryLine>();
        _posTenders = posTenders?.ToList() ?? new List<PosTender>();
    }

    /// <summary>
    /// Stamps every item-invoice line's <see cref="VoucherInventoryLine.Direction"/> to
    /// <paramref name="direction"/> (Purchase ⇒ Inward, Sales ⇒ Outward), in place. Called by the posting
    /// service so the stored lines carry the voucher-nature-implied direction the on-hand engine reads.
    /// </summary>
    public void SetInventoryLineDirections(StockDirection direction)
    {
        for (var i = 0; i < _inventoryLines.Count; i++)
            if (_inventoryLines[i].Direction != direction)
                _inventoryLines[i] = _inventoryLines[i].WithDirection(direction);
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
