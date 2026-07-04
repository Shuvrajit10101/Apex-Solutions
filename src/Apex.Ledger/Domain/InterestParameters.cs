namespace Apex.Ledger.Domain;

/// <summary>
/// The interest-calculation parameter block hung off a <see cref="Ledger"/> (catalog §7; plan.md §5).
/// A ledger carries this only when interest is activated ("F11 + ledger Activate Interest Calculation");
/// a ledger with no block (or with <see cref="Enabled"/> false) accrues no interest, so existing ledgers
/// default off. It captures the simple/compound choice plus the Advance parameters — Rate, Per, On
/// (all / debit / credit balance), Applicability (always / post-due), Calculate-From, and Rounding.
/// </summary>
/// <remarks>
/// This is an immutable value object with no identity; it is replaced wholesale on Alter. Amounts are
/// exact decimals (NFR-3); the projection that consumes it lives in
/// <see cref="Apex.Ledger.Reports.InterestCalculation"/>.
/// </remarks>
public sealed class InterestParameters
{
    /// <summary>Whether interest is activated for the ledger. When false, no interest accrues.</summary>
    public bool Enabled { get; }

    /// <summary>Rate percentage per <see cref="Per"/> (e.g. 18 = 18% p.a. when <see cref="Per"/> is a year basis). ≥ 0.</summary>
    public decimal RatePercent { get; }

    /// <summary>The rate basis (30-day month / 365-day year / calendar month / calendar year).</summary>
    public InterestPer Per { get; }

    /// <summary>Which side of the balance interest accrues on (all / debit-only / credit-only).</summary>
    public InterestOnBalance OnBalance { get; }

    /// <summary>Whether interest runs across the whole period (Always) or only after a bill's due date (PostDue).</summary>
    public InterestApplicability Applicability { get; }

    /// <summary>
    /// The date interest is calculated <b>from</b> (catalog §7 "Calc From"). When null, the projection uses
    /// the period start (or, for <see cref="InterestApplicability.PostDue"/>, the bill due date). When set,
    /// accrual never starts before this date. Optional; a ledger-level "effective/applicability date".
    /// </summary>
    public DateOnly? CalculateFrom { get; }

    /// <summary>Simple or compound interest.</summary>
    public InterestStyle Style { get; }

    /// <summary>How the computed amount is rounded (catalog §7 "Rounding").</summary>
    public InterestRoundingMethod RoundingMethod { get; }

    /// <summary>Decimal places the <see cref="RoundingMethod"/> rounds to (0 = whole rupees). Ignored when method is None. ≥ 0.</summary>
    public int RoundingDecimals { get; }

    public InterestParameters(
        bool enabled,
        decimal ratePercent,
        InterestPer per,
        InterestOnBalance onBalance = InterestOnBalance.All,
        InterestApplicability applicability = InterestApplicability.Always,
        DateOnly? calculateFrom = null,
        InterestStyle style = InterestStyle.Simple,
        InterestRoundingMethod roundingMethod = InterestRoundingMethod.None,
        int roundingDecimals = 0)
    {
        if (ratePercent < 0m)
            throw new ArgumentException("Interest rate percent must be ≥ 0.", nameof(ratePercent));
        if (roundingDecimals < 0)
            throw new ArgumentException("Rounding decimals must be ≥ 0.", nameof(roundingDecimals));

        Enabled = enabled;
        RatePercent = ratePercent;
        Per = per;
        OnBalance = onBalance;
        Applicability = applicability;
        CalculateFrom = calculateFrom;
        Style = style;
        RoundingMethod = roundingMethod;
        RoundingDecimals = roundingDecimals;
    }

    /// <summary>
    /// Applies the configured rounding to a raw interest amount. <see cref="InterestRoundingMethod.None"/>
    /// returns the value unchanged; the others round to <see cref="RoundingDecimals"/> places using the
    /// method's direction (Normal = nearest ties-away, Upward = ceiling away from zero, Downward = floor
    /// toward zero). Negative amounts round symmetrically (magnitude rounds, sign preserved).
    /// </summary>
    public decimal ApplyRounding(decimal raw)
    {
        if (RoundingMethod == InterestRoundingMethod.None) return raw;

        var factor = Pow10(RoundingDecimals);
        var scaled = raw * factor;
        var sign = scaled < 0m ? -1m : 1m;
        var magnitude = Math.Abs(scaled);

        var roundedMagnitude = RoundingMethod switch
        {
            InterestRoundingMethod.Normal => Math.Round(magnitude, MidpointRounding.AwayFromZero),
            InterestRoundingMethod.Upward => Math.Ceiling(magnitude),
            InterestRoundingMethod.Downward => Math.Floor(magnitude),
            _ => magnitude,
        };

        return sign * roundedMagnitude / factor;
    }

    private static decimal Pow10(int places)
    {
        var f = 1m;
        for (var i = 0; i < places; i++) f *= 10m;
        return f;
    }
}
