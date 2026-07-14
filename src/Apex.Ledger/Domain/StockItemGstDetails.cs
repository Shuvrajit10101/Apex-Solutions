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

    // ---- Phase 9 slice 1: GST 2.0 RSP valuation + Compensation-Cess (RQ-1/RQ-2). All default off/null so an item
    // with no advanced-GST data serialises byte-identically to a Phase-4/8 item (ER-13). ----

    /// <summary>The GST valuation basis (Transaction-Value default; RSP for the tobacco carve-out). RQ-1.</summary>
    public GstValuationBasis ValuationBasis { get; set; } = GstValuationBasis.TransactionValue;

    /// <summary>
    /// Enables the <b>explicit per-item Compensation-Cess override</b> (a self-declared <see cref="CessValuationMode"/>
    /// + figures on this item). It is <b>not</b> a hard gate on HSN-inherited cess: compensation cess is HSN/goods-driven
    /// in law, so an item that leaves this <c>false</c> still inherits the dated cess-master row for its HSN
    /// (<see cref="Services.GstService.ResolveCess"/>). What suppresses cess on an exempt supply is <b>taxability</b>
    /// (an Exempt/Nil/Non-GST block resolves no cess), not this flag. RQ-2.
    /// </summary>
    public bool CessApplicable { get; set; }

    /// <summary>Per-item cess valuation-mode override; <c>null</c> ⇒ inherit from the dated cess master by HSN+date.</summary>
    public CessValuationMode? CessValuationMode { get; set; }

    /// <summary>Per-item ad-valorem cess rate override in basis points; <c>null</c> ⇒ inherit from the master.</summary>
    public int? CessRateBasisPoints { get; set; }

    /// <summary>Per-item specific per-unit cess override; <c>null</c> ⇒ inherit from the master.</summary>
    public Money? CessPerUnit { get; set; }

    /// <summary>Per-item RSP-factor override (× 1000); <c>null</c> ⇒ inherit from the master.</summary>
    public int? CessRspFactorMillis { get; set; }

    /// <summary>The declared Retail Sale Price (per unit) — drives RSP-factor cess and RSP GST valuation; <c>null</c> ⇒ unset.</summary>
    public Money? RetailSalePrice { get; set; }

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

        // Phase 9 slice 1: cess override consistency. Only enforced when the item explicitly bears cess with an
        // explicit per-item valuation-mode override (an inherit-from-master item leaves the mode null and is fine).
        if (CessApplicable && CessValuationMode is { } mode)
        {
            if (CessRateBasisPoints is < 0)
                throw new ArgumentException("A cess-bearing item's ad-valorem rate must be ≥ 0 when set.");
            if (CessPerUnit is { } pu && pu.Amount < 0)
                throw new ArgumentException("A cess-bearing item's per-unit cess must be ≥ 0 when set.");
            if (CessRspFactorMillis is < 0)
                throw new ArgumentException("A cess-bearing item's RSP factor must be ≥ 0 when set.");

            if (mode == Domain.CessValuationMode.RetailSalePriceFactor && RetailSalePrice is null)
                throw new ArgumentException("An RSP-factor cess item requires a declared Retail Sale Price.");
            if (mode == Domain.CessValuationMode.AdValorem && CessRateBasisPoints is null)
                throw new ArgumentException("An ad-valorem cess item requires a cess rate (basis points).");
            if (mode == Domain.CessValuationMode.Specific && CessPerUnit is null)
                throw new ArgumentException("A specific cess item requires a per-unit cess amount.");
        }

        if (RetailSalePrice is { } rsp && rsp.Amount < 0)
            throw new ArgumentException("Retail Sale Price must be ≥ 0 when set.");
    }
}
