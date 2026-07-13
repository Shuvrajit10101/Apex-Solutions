using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-9 <b>statutory-bonus engine</b> golden contract (RQ-15; Payment of Bonus Act 1965). The headline
/// oracle: Basic + DA ₹18,000 (≤ ₹21,000 eligible) is §12-capped to ₹7,000 → at 8.33% ⇒ 7,000 × 12 × 8.33% =
/// <b>₹6,997</b>/yr; at 20% ⇒ <b>₹16,800</b>/yr; a ₹25,000 earner is ineligible; a mid-year joiner is prorated.
/// </summary>
public sealed class BonusComputationTests
{
    private static Money Ceiling => new(StatutoryBonus.DefaultCalculationCeiling); // ₹7,000
    private static Money NoMinWage => Money.Zero;

    // ---------------------------------------------------------------- eligibility (§2(13), §8)

    [Theory]
    [InlineData(18_000, 365, true)]  // ≤ ₹21,000 and ≥ 30 days ⇒ eligible
    [InlineData(21_000, 365, true)]  // exactly ₹21,000 ⇒ still eligible (≤)
    [InlineData(25_000, 365, false)] // > ₹21,000 ⇒ ineligible (wage)
    [InlineData(18_000, 20, false)]  // < 30 days ⇒ ineligible (days)
    public void Eligibility_is_wage_ceiling_and_min_days(int basicDa, int days, bool expected)
        => Assert.Equal(expected, StatutoryBonus.IsEligible(new Money(basicDa), days));

    // ---------------------------------------------------------------- the §12 calculation ceiling

    [Fact]
    public void Bonus_base_caps_at_7000_by_default()
        => Assert.Equal(new Money(7_000m), StatutoryBonus.BonusBaseMonthly(new Money(18_000m), Ceiling, NoMinWage));

    [Fact]
    public void Bonus_base_is_actual_when_below_the_ceiling()
        => Assert.Equal(new Money(5_000m), StatutoryBonus.BonusBaseMonthly(new Money(5_000m), Ceiling, NoMinWage));

    [Fact]
    public void A_higher_state_minimum_wage_raises_the_ceiling()
        => Assert.Equal(new Money(10_000m), StatutoryBonus.BonusBaseMonthly(new Money(18_000m), Ceiling, minimumWage: new Money(10_000m)));

    // ---------------------------------------------------------------- the headline annual figures

    [Fact]
    public void Golden_18000_capped_7000_at_833pc_is_6997()
    {
        var baseM = StatutoryBonus.BonusBaseMonthly(new Money(18_000m), Ceiling, NoMinWage); // ₹7,000
        // 7,000 × 12 × 8.33% = 6,997.2 → ₹6,997 (locked golden — the Act's 8.33%, not an exact 1/12).
        Assert.Equal(new Money(6_997m), StatutoryBonus.AnnualBonus(baseM, 833, monthsWorkedInYear: 12, prorate: true));
    }

    [Fact]
    public void Golden_18000_capped_7000_at_20pc_is_16800()
    {
        var baseM = StatutoryBonus.BonusBaseMonthly(new Money(18_000m), Ceiling, NoMinWage); // ₹7,000
        Assert.Equal(new Money(16_800m), StatutoryBonus.AnnualBonus(baseM, 2000, monthsWorkedInYear: 12, prorate: true));
    }

    // ---------------------------------------------------------------- proration

    [Fact]
    public void A_six_month_joiner_is_prorated_at_833pc()
        // 7,000 × 6 × 8.33% = 3,498.6 → ₹3,499.
        => Assert.Equal(new Money(3_499m), StatutoryBonus.AnnualBonus(new Money(7_000m), 833, monthsWorkedInYear: 6, prorate: true));

    [Fact]
    public void Proration_off_always_assumes_twelve_months()
        => Assert.Equal(new Money(6_997m), StatutoryBonus.AnnualBonus(new Money(7_000m), 833, monthsWorkedInYear: 6, prorate: false));

    // ---------------------------------------------------------------- the ₹100 floor (§10)

    [Fact]
    public void A_tiny_computed_bonus_floors_at_100()
        // 100 × 1 × 8.33% = 8.33 → below ₹100 ⇒ floored to ₹100.
        => Assert.Equal(new Money(100m), StatutoryBonus.AnnualBonus(new Money(100m), 833, monthsWorkedInYear: 1, prorate: true));

    // ---------------------------------------------------------------- the §10–§11 rate clamp

    [Theory]
    [InlineData(500, 833)]   // below 8.33% ⇒ clamped up
    [InlineData(1000, 1000)] // in band ⇒ unchanged (10%)
    [InlineData(2500, 2000)] // above 20% ⇒ clamped down
    public void Rate_is_clamped_to_the_833_to_2000_band(int rate, int expected)
        => Assert.Equal(expected, StatutoryBonus.ClampRate(rate));

    [Fact]
    public void A_zero_base_or_zero_months_pays_nothing()
    {
        Assert.Equal(Money.Zero, StatutoryBonus.AnnualBonus(Money.Zero, 833, 12, true));
        Assert.Equal(Money.Zero, StatutoryBonus.AnnualBonus(new Money(7_000m), 833, 0, true));
    }

    // ---------------------------------------------------------------- the config clamps the stored rate

    [Fact]
    public void Bonus_config_clamps_a_misentered_rate_to_the_band()
    {
        Assert.Equal(2000, new BonusConfig(rateBasisPoints: 3000).RateBasisPoints); // 30% → 20%
        Assert.Equal(833, new BonusConfig(rateBasisPoints: 100).RateBasisPoints);   // 1% → 8.33%
    }
}
