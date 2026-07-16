namespace Apex.Ledger.Services;

/// <summary>
/// The <b>e-Way Bill validity calculator</b> (Phase 9 slice 5; Rule 138(10); §2.6). A <b>pure, clock-free</b> static
/// helper — the caller supplies <c>generatedAt</c> / <c>now</c> (like <c>EWayValidity</c>'s sibling
/// <c>EInvoiceService.Cancel(today)</c>), so no <c>DateTime.Now</c> ever enters the engine. This is the one piece of S5
/// with no e-Invoice precedent, so its arithmetic is fully specified and each branch is golden-tested.
/// <para>
/// <b>The subtle rule (risk #2):</b> "one day" is <b>NOT</b> generation + 24 h — it ends at <b>midnight of the day
/// FOLLOWING generation</b>; each further day adds a full midnight-to-midnight window. Distance is <c>ceil(distance / per)</c>
/// (min 1 day; "or part thereof"), where <c>per</c> = 200 km normally and <b>20 km</b> for over-dimensional cargo /
/// multimodal-shipping. Extension is a ±8 h window around expiry with a hard <b>360-day</b> cap from first generation.
/// </para>
/// </summary>
public static class EWayValidity
{
    /// <summary>Normal cargo: one validity day per 200 km (or part thereof).</summary>
    public const int NormalKmPerDay = 200;

    /// <summary>Over-Dimensional-Cargo / multimodal-shipping: one validity day per 20 km (or part thereof).</summary>
    public const int OverDimensionalKmPerDay = 20;

    /// <summary>The maximum total life of an EWB (incl. extensions), from first generation (eff. 01-Jan-2025).</summary>
    public const int MaxTotalValidityDays = 360;

    /// <summary>The number of validity days for a distance — <c>ceil(distanceKm / per)</c>, minimum 1 ("or part thereof").
    /// <paramref name="odc"/> flips the per-day distance from 200 km to 20 km.</summary>
    public static int ValidityDays(int distanceKm, bool odc)
    {
        if (distanceKm < 0)
            throw new ArgumentException("Distance must be ≥ 0 km.", nameof(distanceKm));
        var per = odc ? OverDimensionalKmPerDay : NormalKmPerDay;
        return Math.Max(1, (int)Math.Ceiling(distanceKm / (double)per));
    }

    /// <summary>
    /// The validity end for an EWB generated at <paramref name="generatedAt"/> covering <paramref name="distanceKm"/>.
    /// Per <b>Rule 138(10) Explanation 1</b> + the CBIC FAQ (an EWB generated 00:04 on 14-Mar has its "day 1" expire at
    /// midnight of 15-16 Mar = <b>16-Mar 00:00</b>): "one day" expires at <b>midnight of the day FOLLOWING generation</b>
    /// — i.e. the whole day after the generation day is the first validity day — and each additional day adds a full
    /// midnight-to-midnight window. For <c>N</c> validity days: <c>ValidUpto = midnight of (generation date + N + 1)</c>.
    /// Worked example: generated 2025-04-10 14:00, 100 km ⇒ 1 day ⇒ valid upto <b>00:00 on 2025-04-12</b>; 250 km ⇒
    /// ceil(250/200)=2 days ⇒ <b>00:00 on 2025-04-13</b>. The <c>+1</c> model matches the 360-day cap in
    /// <see cref="ExtendValidUpto"/> (<c>generation date + 1 + 360</c>).
    /// </summary>
    public static DateTimeOffset ValidUpto(DateTimeOffset generatedAt, int distanceKm, bool odc)
    {
        var days = ValidityDays(distanceKm, odc);
        // Midnight of (generation date + days + 1): the first validity day is the whole day FOLLOWING generation, ending
        // at the next midnight (day + 2 for day 1), and each further day adds a full midnight-to-midnight window.
        return new DateTimeOffset(generatedAt.Date.AddDays(days + 1), generatedAt.Offset);
    }

    /// <summary>True iff <paramref name="now"/> is inside the extension window — 8 h before / after expiry (§2.6).</summary>
    public static bool CanExtend(DateTimeOffset now, DateTimeOffset validUpto) =>
        now >= validUpto.AddHours(-8) && now <= validUpto.AddHours(8);

    /// <summary>
    /// The extended validity for the remaining distance, re-computed from <paramref name="now"/>. Capped at
    /// <see cref="MaxTotalValidityDays"/> days from <paramref name="generatedAt"/> (first generation) — an extension that
    /// would push the validity past the cap is <b>refused</b> (fail-fast). Clock-free: the caller supplies
    /// <paramref name="now"/> (which must be inside the <see cref="CanExtend"/> window — the caller checks that).
    /// </summary>
    public static DateTimeOffset ExtendValidUpto(
        DateTimeOffset generatedAt, DateTimeOffset now, int remainingDistanceKm, bool odc)
    {
        var extended = ValidUpto(now, remainingDistanceKm, odc);
        var cap = new DateTimeOffset(generatedAt.Date.AddDays(1 + MaxTotalValidityDays), generatedAt.Offset);
        if (extended > cap)
            throw new InvalidOperationException(
                $"An e-Way Bill's total validity cannot exceed {MaxTotalValidityDays} days from first generation.");
        return extended;
    }

    /// <summary>True iff the validity window has elapsed as of <paramref name="now"/> (a <b>derived</b> view — the stored
    /// status stays Generated; expiry is never written into the aggregate, like a post-dated voucher).</summary>
    public static bool IsExpired(DateTimeOffset now, DateTimeOffset validUpto) => now > validUpto;
}
