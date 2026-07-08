namespace Apex.Ledger.Domain;

/// <summary>
/// Optional GST details on a Stock Item or a Sales/Purchase ledger (catalog §12; phase4 RQ-8/RQ-9). Carries
/// the <see cref="HsnSac"/> code (validated 4/6/8-digit text, DP-5), the <see cref="Taxability"/>
/// (Taxable / Exempt / Nil-Rated / Non-GST), the integrated GST <see cref="RateBasisPoints"/> (the CGST/SGST
/// rates are each derived as half, RQ-12), and the <see cref="SupplyType"/> (Goods ⇒ HSN, Services ⇒ SAC).
/// The Phase-3 placeholder <see cref="StockItem.HsnSacCode"/>/<see cref="StockItem.IsTaxable"/> fields become
/// active via this block.
/// </summary>
/// <remarks>
/// Mutable value object hung off <see cref="StockItem"/> (or a sales/purchase <see cref="Ledger"/>) as a
/// nullable reference. A non-taxable block (<see cref="Taxability"/> ≠ Taxable) has a <c>null</c>
/// <see cref="RateBasisPoints"/>. Framework- and DB-agnostic.
/// </remarks>
public sealed class StockItemGstDetails
{
    /// <summary>HSN (goods) / SAC (services) classification code — 4, 6 or 8 digits; <c>null</c> when unset.</summary>
    public string? HsnSac { get; set; }

    /// <summary>The taxability of the item/line. Only <see cref="GstTaxability.Taxable"/> attracts tax.</summary>
    public GstTaxability Taxability { get; set; } = GstTaxability.Taxable;

    /// <summary>The integrated GST rate in basis points (1800 = 18%); <c>null</c> ⇒ unresolved at this level.</summary>
    public int? RateBasisPoints { get; set; }

    /// <summary>Goods (HSN) or Services (SAC).</summary>
    public GstSupplyType SupplyType { get; set; } = GstSupplyType.Goods;

    /// <summary>True iff this block is taxable (attracts tax when a rate resolves).</summary>
    public bool IsTaxable => Taxability == GstTaxability.Taxable;

    /// <summary>
    /// Validates the HSN/SAC length (4/6/8 digits, all numeric) when set, and that a non-negative rate is
    /// only present on a taxable block. Throws <see cref="ArgumentException"/> on a bad value (fail-fast).
    /// </summary>
    public void EnsureValid()
    {
        if (HsnSac is not null)
        {
            var hsn = HsnSac.Trim();
            if (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit))
                throw new ArgumentException($"HSN/SAC '{HsnSac}' must be 4, 6 or 8 digits (numeric).");
        }

        if (RateBasisPoints is < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0 when set.");

        if (RateBasisPoints is { } && Taxability != GstTaxability.Taxable && RateBasisPoints > 0)
            throw new ArgumentException(
                $"A {Taxability} item/line must not carry a positive GST rate ({RateBasisPoints} bp).");
    }
}
