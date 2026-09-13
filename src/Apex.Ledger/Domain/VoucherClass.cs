namespace Apex.Ledger.Domain;

/// <summary>
/// A named <b>Voucher Class</b> on a <see cref="VoucherType"/> (census 9.9; schema v58) — currently the <b>Stock
/// Journal transfer class</b>, the vendor's mechanism for a one-keystroke inter-godown transfer.
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHAT THIS WAS, AND WHAT v62 ADDED TO IT.</b> This type shipped with census 9.9 holding exactly two
/// vendor fields, <i>"Name of Class"</i> and <i>"Use Class for Inter-Godown Transfers"</i>, and it was kept that
/// narrow ON PURPOSE so that it would not half-build census row <b>2.6</b> — the <i>general</i> Voucher Class
/// machinery — from a Stock Journal's point of view. A Stock Journal moves stock and posts <i>no ledger entry at
/// all</i>, so the transfer class genuinely needs none of that apparatus.</para>
///
/// <para><b>Row 2.6 has since been built, at schema v62, and it EXTENDED this type rather than starting a second
/// class system.</b> Two child collections were added — <see cref="LedgerAllocations"/> (the vendor's <i>Default
/// Accounting Allocations for all items in Invoice</i>) and <see cref="AdditionalEntries"/> (the vendor's
/// <i>Additional Accounting Entries</i>, which carry the rounding) — and nothing already here changed. Both are
/// EMPTY on every class that existed before v62, which is the literal truth about each of them: a Stock Journal
/// transfer class allocates nothing and adds no ledger. <see cref="Services.VoucherClassPosting"/> is the single
/// place those two tables turn into ledger legs.</para>
///
/// <para>🔴 <b>The row's OLD title was measured against the wrong product.</b> Census 9.9 previously read
/// "Transfer Journal as a <i>named voucher kind</i>", which is a Tally.ERP 9 artefact — TallyPrime's own
/// inventory-voucher page lists nine kinds and has no Transfer Journal among them. The mechanism in the current
/// product is this class on the Stock Journal type, which is why nothing here adds a
/// <see cref="VoucherBaseType"/>.</para>
///
/// <para><b>What the class DOES at entry.</b> When a Stock Journal is entered with a class whose
/// <see cref="UseClassForInterGodownTransfers"/> is set, the operator names a single <b>destination godown</b>
/// once and keys only the source lines; every source line is then mirrored to that destination — same item, same
/// quantity, same unit, same rate, same batch — so the two halves of the transfer cannot disagree. Without the
/// class the operator keys both halves by hand and nothing stops them diverging. See
/// <c>InterGodownTransfer.Mirror</c>, which is the single place that mirroring happens.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/voucher-types-tally/</c> ("Name of Class"; "Use Class
/// for Inter-Godown Transfers"), and the vendor's own summary of the effect: <i>"all you have to do is just enter
/// the destination godown while recording the stock journal voucher and enter the item details"</i>, with the
/// selected items mirrored to the destination including batch, rate and value.</para>
/// </remarks>
public sealed class VoucherClass
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The vendor's <b>"Name of Class"</b> — what the operator picks at the top of voucher entry (e.g. "Transfer").
    /// Unique within its voucher type (the schema enforces this with a unique index on (type, name)); a rename does
    /// not change identity.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The vendor's <b>"Use Class for Inter-Godown Transfers"</b>. When set on a class of a
    /// <see cref="VoucherBaseType.StockJournal"/> type, entry under this class mirrors every source line to one
    /// named destination godown. Defaults to <c>false</c> — a class with it off is a plain named class that
    /// changes nothing, which is a legal (if pointless) thing for an operator to create, so it is not rejected.
    /// </summary>
    public bool UseClassForInterGodownTransfers { get; set; }

    private readonly List<VoucherClassLedgerAllocation> _ledgerAllocations = new();
    private readonly List<VoucherClassAdditionalEntry> _additionalEntries = new();

    /// <summary>
    /// The vendor's <b>"Default Accounting Allocations for all items in Invoice"</b> (census 2.6; schema v62) —
    /// the LEDGER PRE-MAP. Each row names a ledger and the percentage of the item value it receives, so the
    /// operator names neither at entry. Empty on a class that pre-maps nothing, which is every class written
    /// before v62.
    /// </summary>
    public IReadOnlyList<VoucherClassLedgerAllocation> LedgerAllocations => _ledgerAllocations;

    /// <summary>
    /// The vendor's <b>"Additional Accounting Entries"</b> (census 2.6; schema v62) — freight, a per-unit duty,
    /// the invoice round-off and the like, each with its Type of Calculation, Value Basis, Rounding Method,
    /// Rounding Limit and Remove if Zero. Empty on a class that adds no ledger.
    /// </summary>
    public IReadOnlyList<VoucherClassAdditionalEntry> AdditionalEntries => _additionalEntries;

    /// <summary>Appends a default accounting allocation. Validation of the SET — that the percentages reach 100%,
    /// and that no ledger is named twice — belongs to <see cref="Services.VoucherClassService"/>, because it is a
    /// property of the whole table and not of any one row.</summary>
    public void AddLedgerAllocation(VoucherClassLedgerAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        _ledgerAllocations.Add(allocation);
    }

    /// <summary>Appends an additional accounting entry. See <see cref="AddLedgerAllocation"/> on where set-level
    /// validation lives.</summary>
    public void AddAdditionalEntry(VoucherClassAdditionalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _additionalEntries.Add(entry);
    }

    /// <summary>Removes every allocation and additional entry — used by the master screen when the operator
    /// re-keys the tables, and by the store when it reloads a class.</summary>
    public void ClearAllocationsAndEntries()
    {
        _ledgerAllocations.Clear();
        _additionalEntries.Clear();
    }

    public VoucherClass(Guid id, string name, bool useClassForInterGodownTransfers = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Voucher class name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        UseClassForInterGodownTransfers = useClassForInterGodownTransfers;
    }
}
