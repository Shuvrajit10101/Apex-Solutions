namespace Apex.Ledger.Domain;

/// <summary>
/// Marks a ledger as a GST <b>tax ledger</b> (catalog §12; phase4 RQ-4/RQ-5). The auto-created tax ledgers
/// under Duties &amp; Taxes carry this classification so the engine and GSTR-3B can map each ledger to its tax
/// head (Central/State/Integrated) and direction (Output on sales, Input on purchases) without parsing the
/// ledger name. A ledger with no classification is an ordinary ledger.
/// </summary>
/// <remarks>Immutable value object hung off <see cref="Ledger"/> as a nullable reference.</remarks>
public sealed class LedgerGstClassification
{
    /// <summary>The tax head this ledger receives (Central/State/Integrated; Cess is a seam only).</summary>
    public GstTaxHead TaxHead { get; }

    /// <summary>Output (sales liability) or Input (purchase ITC).</summary>
    public GstTaxDirection Direction { get; }

    public LedgerGstClassification(GstTaxHead taxHead, GstTaxDirection direction)
    {
        TaxHead = taxHead;
        Direction = direction;
    }
}
