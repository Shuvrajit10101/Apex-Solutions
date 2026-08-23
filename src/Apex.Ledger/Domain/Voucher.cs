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

    // ---------------------------------------------------------------- the provisional-state vector + Cancelled
    //
    // 🔴 THE FOUR SETTERS BELOW ARE `internal`, AND THAT IS THE POINT OF THIS BLOCK.
    //
    // Each of these four decides whether the voucher affects live balances AT ALL. `LedgerBalance.cs` opens its
    // as-of test with `if (v.Cancelled || v.Optional) return false;`, PostDated withholds a voucher until its date
    // is reached, and ApplicableUpto is what makes a Reversing Journal lapse. So writing one of them moves the
    // books by a WHOLE voucher - every figure it touches - with no verb, no guard, no warning and no figure ON the
    // voucher changing.
    //
    // `LedgerService.Replace` refuses a change to all four BY NAME (see its section 7.4 guard, and the measured
    // swings recorded there). But that refusal binds Replace and nothing else: while these setters were PUBLIC,
    // any caller anywhere could do the same move directly, and the refusal was a guard that existed in exactly one
    // caller. Narrowing them to `internal` makes the engine assembly the only thing that can move a posted
    // voucher's lifecycle state, which is what the Replace guard was always asserting.
    //
    // The ONE production caller outside this assembly was the Alt+X save-failure rollback in
    // MainWindowViewModel, writing `voucher.Cancelled = false`. It is now `LedgerService.DiscardUncommittedCancel`,
    // which had to change anyway - the rollback also has to discard the edit-log entry the Cancel appended.
    //
    // The constructor still takes all four, so building a voucher is unaffected; only moving one after it is
    // posted is now the engine's business. `InternalsVisibleTo("Apex.Ledger.Tests")` keeps the fixtures that key
    // these directly compiling, and is scoped to the test assembly.
    //
    // NOT DONE, and named rather than left to be rediscovered: `InventoryVoucher.Cancelled` / `.PostDated` carry
    // the identical public setters on the pure-stock aggregate.

    /// <summary>Alt+X — number retained in sequence, zero effect on balances. Setter <c>internal</c>: see the
    /// banner above.</summary>
    public bool Cancelled { get; internal set; }

    /// <summary>Ctrl+L — excluded from live balances until regularised. Setter <c>internal</c>: see the banner
    /// above.</summary>
    public bool Optional { get; internal set; }

    /// <summary>Ctrl+T — excluded from balances until its date is reached. Setter <c>internal</c>: see the
    /// banner above.</summary>
    public bool PostDated { get; internal set; }

    /// <summary>
    /// "Applicable upto" date for a <see cref="VoucherBaseType.ReversingJournal"/> (catalog §7): the
    /// last date on which the reversing entry is in force. Under a scenario it affects reports only for
    /// as-of dates ≤ this value; on/after it lapses (reverses out). <c>null</c> for every other voucher.
    /// </summary>
    public DateOnly? ApplicableUpto { get; internal set; }

    /// <summary>
    /// The <b>counterparty document number</b> (numbering-design-v2 §8): the OTHER party's number on an
    /// ordinary Purchase/Sales voucher — captured as "Supplier Invoice No." on a Purchase and "Reference No."
    /// on a Sales. Pure free text: it receives <b>no</b> auto prefix/suffix/width/numbering (that is our own
    /// <see cref="Number"/>), and it is a DISTINCT field from the GST credit/debit-note-only
    /// <c>original_invoice_number</c> (which references OUR earlier invoice). <c>null</c>/empty for every voucher
    /// without one, so a voucher that carries no reference is behaviourally and serialisation-identical to today
    /// (ER-13).
    /// </summary>
    public string? ReferenceNo { get; set; }

    /// <summary>
    /// The counterparty document's date (numbering-design-v2 §8), captured alongside <see cref="ReferenceNo"/>
    /// for fidelity (Tally shows it). <c>null</c> for every voucher without a captured reference.
    /// </summary>
    public DateOnly? ReferenceDate { get; set; }

    /// <summary>
    /// True iff this voucher was posted from the <b>Accounting Invoice</b> (service-invoice) entry mode — a Sales
    /// invoice billed as service-income LEDGER lines with SAC-based GST and <b>no stock</b>
    /// (<see cref="HasInventoryLines"/> stays false). Schema v49; default <c>false</c>, so every voucher that predates
    /// the flag — and every hand-keyed As-Voucher entry, item invoice and plain voucher — reads exactly as before
    /// (ER-13).
    ///
    /// <para><b>Why this is persisted and not inferred.</b> The print gate used to decide "this is a service invoice"
    /// by looking for engine-stamped <c>GstLineTax</c> forward legs on a ledger-only Sales voucher. That inference is
    /// wrong in both directions: a <b>zero-rated</b> (LUT/export, 0%) and a <b>wholly-exempt</b> service invoice post
    /// NO tax leg at all, yet both ARE valid Rule-46 tax invoices and must print as one; and the exclusion of
    /// hand-keyed sales rested on "no other path currently stamps <c>GstLineTax</c> on a ledger-only Sales voucher" —
    /// true of today's code, not a structural property. Recording WHAT THE USER DID at posting time makes both
    /// directions structural: the document type is a fact about the voucher, not a guess re-derived from its tax.</para>
    ///
    /// <para>Read-only after construction, deliberately: the printed document type of an issued invoice must not be
    /// flippable after the fact.</para>
    /// </summary>
    public bool IsAccountingInvoice { get; }

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
        IEnumerable<PosTender>? posTenders = null,
        string? referenceNo = null,
        DateOnly? referenceDate = null,
        bool isAccountingInvoice = false)
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
        ReferenceNo = referenceNo;
        ReferenceDate = referenceDate;
        IsAccountingInvoice = isAccountingInvoice;
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

    /// <summary>
    /// Puts each item-invoice line's <see cref="VoucherInventoryLine.Direction"/> back to
    /// <paramref name="directions"/>[i] — the UNDO of <see cref="SetInventoryLineDirections"/>.
    ///
    /// <para><b>Why per-line and not a single direction.</b> <c>LedgerService.Replace</c> stamps directions BEFORE
    /// validating (the pairing invariant and the on-hand engine must read the canonical direction), so a REJECTED
    /// replacement was handed back to the caller with its lines rewritten. Restoring needs the incoming directions
    /// exactly as they were, which a single-direction setter cannot express for a mixed list.</para>
    ///
    /// <para><c>internal</c>: the posting services are the only legitimate callers, and this is an undo of their
    /// own stamp, not a public way to key a direction.</para>
    /// </summary>
    internal void RestoreInventoryLineDirections(IReadOnlyList<StockDirection> directions)
    {
        ArgumentNullException.ThrowIfNull(directions);
        if (directions.Count != _inventoryLines.Count) return;

        for (var i = 0; i < _inventoryLines.Count; i++)
            if (_inventoryLines[i].Direction != directions[i])
                _inventoryLines[i] = _inventoryLines[i].WithDirection(directions[i]);
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
