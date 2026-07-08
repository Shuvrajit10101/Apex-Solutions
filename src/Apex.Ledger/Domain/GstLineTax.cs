namespace Apex.Ledger.Domain;

/// <summary>
/// The GST tax detail carried on an <see cref="EntryLine"/> that posts to a tax ledger (catalog §12; phase4
/// RQ-12/RQ-20). It self-describes the tax line for the Tax Analysis (Alt+A) and GSTR-1/3B projections: the
/// <see cref="TaxHead"/> (Central/State/Integrated), the applied <see cref="RateBasisPoints"/> (the head's
/// rate — half the integrated rate for CGST/SGST, the full rate for IGST), and the
/// <see cref="TaxableValue"/> the tax was computed on. The line's <see cref="EntryLine.Amount"/> is the tax
/// itself (paisa-exact). A base line carries no <see cref="GstLineTax"/> at all (mirrors
/// <see cref="ForexInfo"/> on a base line).
/// </summary>
/// <remarks>Immutable value object with no identity. Amounts are paisa-exact (ER-2).</remarks>
public sealed class GstLineTax
{
    /// <summary>The tax head this line posts (Central/State/Integrated; Cess is a seam only).</summary>
    public GstTaxHead TaxHead { get; }

    /// <summary>The applied rate in basis points for this head (900 = 9% CGST half; 1800 = 18% IGST full).</summary>
    public int RateBasisPoints { get; }

    /// <summary>The taxable (assessable) value the tax was computed on (paisa-exact).</summary>
    public Money TaxableValue { get; }

    public GstLineTax(GstTaxHead taxHead, int rateBasisPoints, Money taxableValue)
    {
        if (rateBasisPoints < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (taxableValue.Amount < 0m)
            throw new ArgumentException("Taxable value must be ≥ 0.", nameof(taxableValue));
        if (!taxableValue.IsPaisaExact)
            throw new InvalidOperationException($"Taxable value {taxableValue} must be paisa-exact.");

        TaxHead = taxHead;
        RateBasisPoints = rateBasisPoints;
        TaxableValue = taxableValue;
    }
}
