using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-9 <b>gratuity engine</b> golden contract (RQ-14; Payment of Gratuity Act 1972 §4). The headline
/// oracle: last-drawn Basic + DA ₹26,000 over 10 completed years ⇒ (26,000 × 15 × 10) / 26 = <b>₹1,50,000</b> (cap not
/// binding). Also: the ≥6-month completed-year round-up, the ₹20,00,000 §4(3) cap, and five-year vesting.
/// </summary>
public sealed class GratuityComputationTests
{
    private static Money Cap => new(GratuityConfig.DefaultCapAmount);

    // ---------------------------------------------------------------- headline golden

    [Fact]
    public void Golden_basic_da_26000_ten_years_accrues_150000()
    {
        var accrued = Gratuity.Accrued(new Money(26_000m), completedYears: 10, Cap);
        Assert.Equal(new Money(150_000m), accrued); // 26,000 × 15 × 10 / 26 = 150,000 exactly (cap not binding)
    }

    // ---------------------------------------------------------------- the ≥6-month completed-year round-up (§4(2))

    [Theory]
    [InlineData(120, 10)] // exactly 10 years
    [InlineData(67, 6)]   // 5 years 7 months → 6 (residual 7 ≥ 6, rounds up)
    [InlineData(65, 5)]   // 5 years 5 months → 5 (residual 5 < 6, dropped)
    [InlineData(66, 6)]   // 5 years 6 months → 6 (residual exactly 6, rounds up)
    [InlineData(11, 1)]   // 11 months → 1 (residual 11 ≥ 6)
    [InlineData(5, 0)]    // 5 months → 0
    public void Completed_years_round_up_at_six_months(int monthsOfService, int expectedYears)
        => Assert.Equal(expectedYears, Gratuity.CompletedYears(monthsOfService));

    [Fact]
    public void Whole_months_between_drops_a_trailing_part_month()
    {
        // 2015-09-01 → 2021-04-01 = 5 years 7 months = 67 whole months → 6 completed years.
        var months = Gratuity.WholeMonthsBetween(new DateOnly(2015, 9, 1), new DateOnly(2021, 4, 1));
        Assert.Equal(67, months);
        Assert.Equal(6, Gratuity.CompletedYears(months));

        // 2015-11-01 → 2021-04-01 = 5 years 5 months = 65 whole months → 5 completed years.
        var months2 = Gratuity.WholeMonthsBetween(new DateOnly(2015, 11, 1), new DateOnly(2021, 4, 1));
        Assert.Equal(65, months2);
        Assert.Equal(5, Gratuity.CompletedYears(months2));
    }

    // ---------------------------------------------------------------- the ₹20,00,000 cap (§4(3))

    [Fact]
    public void A_high_wage_long_service_accrual_is_trimmed_to_the_20L_cap()
    {
        // 4,00,000 × 15 × 10 / 26 = 23,07,692.30… → rounds to 23,07,692 → trimmed to the ₹20,00,000 §4(3) cap.
        var accrued = Gratuity.Accrued(new Money(400_000m), completedYears: 10, Cap);
        Assert.Equal(new Money(2_000_000m), accrued);
    }

    [Fact]
    public void A_configurable_cap_below_the_statutory_default_binds()
    {
        var accrued = Gratuity.Accrued(new Money(26_000m), completedYears: 10, cap: new Money(100_000m));
        Assert.Equal(new Money(100_000m), accrued); // 1,50,000 trimmed to a company-set ₹1,00,000 cap
    }

    // ---------------------------------------------------------------- vesting (§4(1)): 5 years = 60 months

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    [InlineData(55, false)] // 4y7m: completed-years rounds to 5, but NOT yet vested (55 < 60 months)
    public void Vesting_is_five_years_of_whole_months(int monthsOfService, bool expectedVested)
        => Assert.Equal(expectedVested, Gratuity.IsVested(monthsOfService));

    // ---------------------------------------------------------------- degenerate

    [Fact]
    public void Zero_service_or_zero_wage_accrues_nothing()
    {
        Assert.Equal(Money.Zero, Gratuity.Accrued(new Money(26_000m), 0, Cap));
        Assert.Equal(Money.Zero, Gratuity.Accrued(Money.Zero, 10, Cap));
    }
}
