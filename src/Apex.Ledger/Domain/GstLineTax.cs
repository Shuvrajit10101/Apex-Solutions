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

    /// <summary>
    /// True iff this is a <b>reverse-charge</b> tax line (Phase 9 slice 2; RQ-7). The RCM output-liability line and the
    /// RCM input-ITC line both carry it; a normal forward-charge line leaves it false (default) so a v38 line is
    /// byte-identical (ER-13). It is the tag the GSTR-3B / GSTR-1 projections read to bucket 3.1(d) / 4A(2) / 4A(3)
    /// and to <b>exclude</b> RCM lines from the normal outward/ITC buckets — a pure projection, never a recompute (ER-9).
    /// </summary>
    public bool IsReverseCharge { get; }

    /// <summary>
    /// The RCM ITC bucket this line belongs to (Phase 9 slice 2; RQ-7/RQ-11): <c>ImportOfServices</c> → GSTR-3B 4A(2),
    /// <c>OtherRcm</c> → 4A(3). Set on the RCM <b>input</b> ITC line; <c>null</c> on the RCM <b>output</b> liability line
    /// (a liability, not ITC) and on every forward-charge line.
    /// </summary>
    public RcmItcScheme? RcmScheme { get; }

    /// <summary>
    /// The GST <b>adjustment</b> discriminant (Phase 9 slice 7; RQ-21/RQ-22/RQ-27), or <c>null</c> for an ordinary
    /// <b>forward</b> outward/ITC tax line. A non-null value tags a posted Rule-88A set-off, a cash discharge, or an
    /// ITC reversal / reclaim line — so the Table 6.1 (set-off) and Table 4(B) (reversal) projections classify it
    /// without recomputing (ER-9) and, critically, <b>without polluting</b> the existing outward/ITC sums (the
    /// adjustment vouchers are Journal/Payment base ⇒ already excluded from <c>PostedGstVouchers</c>). Default
    /// <c>null</c> keeps a v43 line byte-identical (ER-13), mirroring how <see cref="RcmScheme"/> was added in S2.
    /// </summary>
    public GstAdjustmentKind? Adjustment { get; }

    public GstLineTax(
        GstTaxHead taxHead, int rateBasisPoints, Money taxableValue,
        bool isReverseCharge = false, RcmItcScheme? rcmScheme = null, GstAdjustmentKind? adjustment = null)
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
        IsReverseCharge = isReverseCharge;
        RcmScheme = rcmScheme;
        Adjustment = adjustment;
    }
}
