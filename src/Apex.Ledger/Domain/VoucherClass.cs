namespace Apex.Ledger.Domain;

/// <summary>
/// A named <b>Voucher Class</b> on a <see cref="VoucherType"/> (census 9.9; schema v58) — currently the <b>Stock
/// Journal transfer class</b>, the vendor's mechanism for a one-keystroke inter-godown transfer.
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHAT THIS IS, AND WHAT IT DELIBERATELY IS NOT.</b> Census row <b>2.6</b> — the <i>general</i>
/// Voucher Class machinery (ledger pre-maps, default accounting allocations, additional-ledger rules, rounding
/// and the rest) — is ABSENT from this product and is <b>not</b> what this type builds. A Stock Journal moves
/// stock and posts <i>no ledger entry at all</i>, so the transfer class needs none of that apparatus: the vendor
/// documents exactly two fields on it, <i>"Name of Class"</i> and <i>"Use Class for Inter-Godown Transfers"</i>,
/// and those two are what this holds. Speculatively adding 2.6's fields here would be inventing a shape no source
/// attests, and would half-build another track's row inside this one.</para>
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

    public VoucherClass(Guid id, string name, bool useClassForInterGodownTransfers = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Voucher class name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
        UseClassForInterGodownTransfers = useClassForInterGodownTransfers;
    }
}
