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

    /// <summary>
    /// True iff this is a dedicated <b>RCM output-liability</b> tax ledger (Phase 9 slice 2; RQ-7 — "RCM Output CGST/SGST/
    /// IGST/Cess"). These carry <c>Direction == Output</c> just like the normal Output ledgers, so this flag disambiguates
    /// them: the cash-only RCM liability (§49(4)) is <b>excluded</b> from "output tax" and never settled from the credit
    /// ledger, and <c>GstService.FindTaxLedger</c> filters on <c>IsReverseCharge == false</c> so a normal sale never posts
    /// here. Default false ⇒ an ordinary tax ledger, byte-identical to a v38 ledger (ER-13).
    /// </summary>
    public bool IsReverseCharge { get; }

    public LedgerGstClassification(GstTaxHead taxHead, GstTaxDirection direction, bool isReverseCharge = false)
    {
        TaxHead = taxHead;
        Direction = direction;
        IsReverseCharge = isReverseCharge;
    }
}
