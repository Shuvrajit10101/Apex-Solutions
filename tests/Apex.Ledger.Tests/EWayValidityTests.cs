using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-5 <b>e-Way Bill validity calculator</b> gate (Rule 138(10) Explanation 1; §2.6; risk #2). The one piece
/// of S5 with no e-Invoice precedent, so every branch is hand-worked: <c>ceil(distance / per)</c> days (200 km normal /
/// 20 km ODC, min 1); per the CBIC FAQ (an EWB generated 00:04 on 14-Mar has its "day 1" expire at midnight of 15-16 Mar
/// = <b>16-Mar 00:00</b>) "one day" expires at <b>midnight of the day FOLLOWING generation</b> (NOT gen + 24 h), so for
/// <c>N</c> days ValidUpto = midnight of <c>(generation date + N + 1)</c>; the ±8 h extension window; the 360-day cap;
/// and the derived <c>IsExpired</c> view. Pure + clock-free — the caller supplies every timestamp.
/// </summary>
public sealed class EWayValidityTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    [Fact]
    public void ValidUpto_250km_normal_is_two_days_to_midnight_of_gen_date_plus_three()
    {
        var gen = new DateTimeOffset(2025, 4, 10, 14, 0, 0, Ist); // generated 2025-04-10 14:00 IST
        // ceil(250/200) = 2 days. Rule 138(10) Explanation 1 / CBIC 14-Mar FAQ: "day 1" ends at midnight of the day
        // FOLLOWING generation (04-12 00:00), "day 2" a full day later ⇒ valid upto MIDNIGHT of 2025-04-13 (NOT gen + 48h,
        // NOT gen-date + 2 — that was the pre-fix off-by-one-day understatement).
        var validUpto = EWayValidity.ValidUpto(gen, distanceKm: 250, odc: false);
        Assert.Equal(new DateTimeOffset(2025, 4, 13, 0, 0, 0, Ist), validUpto);
    }

    [Fact]
    public void ValidUpto_250km_odc_is_thirteen_days()
    {
        var gen = new DateTimeOffset(2025, 4, 10, 14, 0, 0, Ist);
        // ceil(250/20) = 13 days ⇒ midnight of (gen date + 13 + 1) = 2025-04-24 00:00 (Rule 138(10) Explanation 1).
        var validUpto = EWayValidity.ValidUpto(gen, distanceKm: 250, odc: true);
        Assert.Equal(new DateTimeOffset(2025, 4, 24, 0, 0, 0, Ist), validUpto);
    }

    [Theory]
    // Rule 138(10) Explanation 1 / CBIC FAQ goldens: N days ⇒ midnight of (generation date + N + 1).
    [InlineData(100, false, 4, 12)]  // 100 km ⇒ 1 day ⇒ 2025-04-12 00:00 (the FAQ 14-Mar example, shifted to this gen date)
    [InlineData(250, false, 4, 13)]  // 250 km ⇒ 2 days ⇒ 2025-04-13 00:00
    [InlineData(30, true, 4, 13)]    // ODC 30 km ⇒ ceil(30/20)=2 days ⇒ 2025-04-13 00:00 (the /20 per-day, +1 model)
    public void ValidUpto_golden_law_examples(int distanceKm, bool odc, int expMonth, int expDay)
    {
        var gen = new DateTimeOffset(2025, 4, 10, 14, 0, 0, Ist); // generated 2025-04-10 14:00 IST
        var validUpto = EWayValidity.ValidUpto(gen, distanceKm, odc);
        Assert.Equal(new DateTimeOffset(2025, expMonth, expDay, 0, 0, 0, Ist), validUpto);
    }

    [Theory]
    [InlineData(1, 1)]     // any part of the first 200 km ⇒ 1 day (min)
    [InlineData(200, 1)]   // exactly 200 km ⇒ 1 day
    [InlineData(201, 2)]   // 201 km ⇒ 2 days ("or part thereof")
    [InlineData(400, 2)]
    [InlineData(401, 3)]
    public void ValidityDays_ceils_normal_cargo_with_min_one(int distanceKm, int expectedDays)
    {
        Assert.Equal(expectedDays, EWayValidity.ValidityDays(distanceKm, odc: false));
    }

    [Fact]
    public void One_day_ends_at_midnight_following_generation_not_gen_plus_24h()
    {
        var gen = new DateTimeOffset(2025, 4, 10, 14, 0, 0, Ist);
        // 100 km ⇒ 1 day. Per Rule 138(10) Explanation 1 / the CBIC 14-Mar FAQ, the whole day AFTER generation is "day 1",
        // which ends at the NEXT midnight ⇒ 00:00 on 2025-04-12 (NOT 04-11 14:00, and NOT 04-11 — the pre-fix understated
        // value that expired the EWB a full day early).
        var validUpto = EWayValidity.ValidUpto(gen, distanceKm: 100, odc: false);
        Assert.Equal(new DateTimeOffset(2025, 4, 12, 0, 0, 0, Ist), validUpto);
        Assert.NotEqual(gen.AddHours(24), validUpto);
    }

    [Fact]
    public void CanExtend_only_within_eight_hours_around_expiry()
    {
        var validUpto = new DateTimeOffset(2025, 4, 12, 0, 0, 0, Ist);
        Assert.True(EWayValidity.CanExtend(validUpto.AddHours(-8), validUpto));   // exactly 8 h before
        Assert.True(EWayValidity.CanExtend(validUpto.AddHours(8), validUpto));    // exactly 8 h after
        Assert.True(EWayValidity.CanExtend(validUpto, validUpto));               // at expiry
        Assert.False(EWayValidity.CanExtend(validUpto.AddHours(-8).AddMinutes(-1), validUpto));
        Assert.False(EWayValidity.CanExtend(validUpto.AddHours(8).AddMinutes(1), validUpto));
    }

    [Fact]
    public void Extend_recomputes_for_remaining_distance()
    {
        var gen = new DateTimeOffset(2025, 4, 10, 14, 0, 0, Ist);
        var now = new DateTimeOffset(2025, 4, 12, 6, 0, 0, Ist);
        // 300 km remaining ⇒ ceil(300/200) = 2 days from `now` (04-12) ⇒ midnight of (now date + 2 + 1) = 2025-04-15 00:00
        // (Rule 138(10) Explanation 1 +1 model; pre-fix this understated to 04-14).
        var extended = EWayValidity.ExtendValidUpto(gen, now, remainingDistanceKm: 300, odc: false);
        Assert.Equal(new DateTimeOffset(2025, 4, 15, 0, 0, 0, Ist), extended);
    }

    [Fact]
    public void Extend_past_the_360_day_cap_is_refused()
    {
        var gen = new DateTimeOffset(2025, 1, 1, 10, 0, 0, Ist);
        // `now` near the cap; a large remaining distance would push validity past gen + 360 days ⇒ fail-fast.
        var now = new DateTimeOffset(2025, 12, 25, 10, 0, 0, Ist); // ~358 days in
        Assert.Throws<InvalidOperationException>(() =>
            EWayValidity.ExtendValidUpto(gen, now, remainingDistanceKm: 4000, odc: false)); // 20 days ⇒ well past the cap
    }

    [Fact]
    public void IsExpired_is_a_derived_view_over_validUpto()
    {
        var validUpto = new DateTimeOffset(2025, 4, 12, 0, 0, 0, Ist);
        Assert.False(EWayValidity.IsExpired(validUpto.AddMinutes(-1), validUpto));
        Assert.False(EWayValidity.IsExpired(validUpto, validUpto));            // at expiry, not yet expired
        Assert.True(EWayValidity.IsExpired(validUpto.AddMinutes(1), validUpto));
    }
}
