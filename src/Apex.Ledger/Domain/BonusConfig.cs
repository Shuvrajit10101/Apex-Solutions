namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>company-level statutory-Bonus configuration</b> (Phase 8 slice 9; catalog §14; Payment of Bonus Act 1965) —
/// the establishment's bonus policy the deterministic computation reads: the bonus <see cref="RateBasisPoints"/>
/// (clamped to the §10 8.33% floor / §11 20% ceiling), the §12 <see cref="CalculationCeiling"/> (₹7,000/month), the
/// state <see cref="MinimumWage"/> (the calc ceiling is the higher of ₹7,000 and this) and whether a mid-year
/// joiner's bonus is <see cref="Prorate"/>d by months worked. Present (non-<c>null</c> on
/// <see cref="Company.BonusConfig"/>) once the establishment computes statutory bonus; a company that never does
/// carries no config and serialises byte-identically to a pre-v37 company (ER-13). Pure data — the
/// <c>StatutoryBonus</c> engine reads these; the bonus register projects them per employee.
/// </summary>
public sealed class BonusConfig
{
    /// <summary>The §10 minimum bonus rate in basis points: <b>833</b> (8.33%).</summary>
    public const int MinRateBasisPoints = 833;

    /// <summary>The §11 maximum bonus rate in basis points: <b>2000</b> (20%).</summary>
    public const int MaxRateBasisPoints = 2000;

    /// <summary>The default bonus rate in basis points: <b>833</b> (8.33%, the §10 minimum).</summary>
    public const int DefaultRateBasisPoints = MinRateBasisPoints;

    /// <summary>The §12 default calculation ceiling in rupees: <b>₹7,000</b>/month.</summary>
    public const decimal DefaultCalculationCeiling = 7_000m;

    /// <summary>The bonus rate in basis points (100 bp = 1%); always in <c>[833, 2000]</c> (the value is clamped to the
    /// §10–§11 band on construction, so a mis-entered rate can never produce an out-of-band bonus). Default 833 (8.33%).</summary>
    public int RateBasisPoints { get; set; } = DefaultRateBasisPoints;

    /// <summary>The §12 monthly calculation ceiling; the bonus base per month is <c>min(actual Basic+DA,
    /// max(this, <see cref="MinimumWage"/>))</c>. Default ₹7,000.</summary>
    public Money CalculationCeiling { get; set; } = new Money(DefaultCalculationCeiling);

    /// <summary>The applicable state minimum wage per month; when it exceeds <see cref="CalculationCeiling"/> it
    /// raises the calc ceiling (§12, "whichever is higher"). Default ₹0 ⇒ the ceiling falls back to ₹7,000.</summary>
    public Money MinimumWage { get; set; } = Money.Zero;

    /// <summary>Whether a mid-year joiner's annual bonus is prorated by the months actually worked in the accounting
    /// year (default <c>true</c>, DP-4); when <c>false</c> a full twelve months is always assumed.</summary>
    public bool Prorate { get; set; } = true;

    public BonusConfig() { }

    public BonusConfig(
        int rateBasisPoints = DefaultRateBasisPoints,
        Money? calculationCeiling = null,
        Money? minimumWage = null,
        bool prorate = true)
    {
        var ceiling = calculationCeiling ?? new Money(DefaultCalculationCeiling);
        var minWage = minimumWage ?? Money.Zero;
        if (ceiling.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(calculationCeiling), "Bonus calculation ceiling cannot be negative.");
        if (minWage.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(minimumWage), "Bonus minimum wage cannot be negative.");
        // §10–§11: the rate is bounded to the 8.33%–20% band, so a mis-configured rate never over/under-pays.
        RateBasisPoints = Math.Clamp(rateBasisPoints, MinRateBasisPoints, MaxRateBasisPoints);
        CalculationCeiling = ceiling;
        MinimumWage = minWage;
        Prorate = prorate;
    }
}
