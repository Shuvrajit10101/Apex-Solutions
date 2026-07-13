using Apex.Ledger;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-5 <b>ESI computation</b> contract (RQ-9; ESIC — ESI Central Rules 1950, Rule 51) — the pure
/// <see cref="EsiContribution"/> employee/employer split. The headline oracles are the two hand-derived golden
/// members, each side rounded <b>up</b> (ceiling) to the next whole rupee <b>independently</b>:
/// <list type="bullet">
///   <item>(A) gross ₹20,000 → EE 150 / ER 650 (total 800);</item>
///   <item>(B) gross ₹17,500 → EE 132 / ER 569 (total <b>701</b> = 132 + 569, NOT <c>ceil(700)</c> — the
///     independent round-up bites).</item>
/// </list>
/// Also covers the ≤ ₹176 daily-wage employee-share waiver (EE 0, ER still paid), the no-cap-on-the-base rule, the
/// once-per-period frozen coverage test (overtime excluded), and the contribution-period boundaries.
/// </summary>
public sealed class EsiComputationTests
{
    private static Money R(decimal v) => new(v);

    // A daily wage safely above the ₹176 exemption (so the employee share applies) for the golden amount cases.
    private const decimal HighDaily = 1000m;

    // ---------------------------------------------------------------- golden members

    [Fact]
    public void Golden_a_gross_20000()
    {
        var c = EsiContribution.ComputeMember(20000m, HighDaily);
        Assert.Equal(R(150m), c.EmployeeContribution);   // ceil(0.75% × 20,000) = ceil(150.00) = 150
        Assert.Equal(R(650m), c.EmployerContribution);   // ceil(3.25% × 20,000) = ceil(650.00) = 650
        Assert.Equal(R(20000m), c.ContributionWages);
    }

    [Fact]
    public void Golden_b_gross_17500_the_independent_round_up_bites()
    {
        var c = EsiContribution.ComputeMember(17500m, HighDaily);
        Assert.Equal(R(132m), c.EmployeeContribution);   // ceil(131.25) = 132
        Assert.Equal(R(569m), c.EmployerContribution);   // ceil(568.75) = 569

        // The total is 701 = 132 + 569 — each side ceiled INDEPENDENTLY, never a single ceil(4% × 17,500) = ceil(700) = 700.
        Assert.Equal(701m, c.EmployeeContribution.Amount + c.EmployerContribution.Amount);
        Assert.NotEqual(700m, c.EmployeeContribution.Amount + c.EmployerContribution.Amount);
    }

    // ---------------------------------------------------------------- ≤ ₹176/day employee-share waiver

    [Fact]
    public void Average_daily_wage_at_or_below_176_waives_the_employee_share_but_not_the_employer_share()
    {
        // A ₹5,000/month earner over a 30-day month: avg daily ≈ 166.67 ≤ 176 → EE 0; ER still ceil(3.25% × 5,000) = 163.
        var c = EsiContribution.ComputeMember(5000m, averageDailyWage: 5000m / 30m);
        Assert.Equal(Money.Zero, c.EmployeeContribution);
        Assert.Equal(R(163m), c.EmployerContribution); // ceil(162.50) = 163

        // Exactly at the ₹176 boundary the waiver still applies (≤, not <).
        var atBoundary = EsiContribution.ComputeMember(5280m, averageDailyWage: 176m);
        Assert.Equal(Money.Zero, atBoundary.EmployeeContribution);
        Assert.Equal(R(172m), atBoundary.EmployerContribution); // ceil(3.25% × 5,280) = ceil(171.60) = 172

        // Just above the boundary the employee share applies again.
        var aboveBoundary = EsiContribution.ComputeMember(5280m, averageDailyWage: 176.01m);
        Assert.Equal(R(40m), aboveBoundary.EmployeeContribution); // ceil(0.75% × 5,280) = ceil(39.60) = 40
    }

    // ---------------------------------------------------------------- coverage (frozen at the CP start)

    [Fact]
    public void Coverage_is_decided_against_the_21000_ceiling()
    {
        Assert.True(EsiContribution.IsCovered(21000m));   // exactly at the ceiling ⇒ covered
        Assert.True(EsiContribution.IsCovered(20000m));
        Assert.False(EsiContribution.IsCovered(21000.01m));
        Assert.False(EsiContribution.IsCovered(22000m));

        // Person-with-disability ceiling is ₹25,000.
        Assert.True(EsiContribution.IsCovered(25000m, personWithDisability: true));
        Assert.False(EsiContribution.IsCovered(25000.01m, personWithDisability: true));
    }

    [Fact]
    public void The_contribution_base_is_the_actual_wages_with_no_21000_cap()
    {
        // A member covered at the CP start whose wage rose to ₹22,000 is charged on the FULL 22,000 (no cap).
        var c = EsiContribution.ComputeMember(22000m, HighDaily);
        Assert.Equal(R(165m), c.EmployeeContribution);   // ceil(0.75% × 22,000) = ceil(165.00) = 165
        Assert.Equal(R(715m), c.EmployerContribution);   // ceil(3.25% × 22,000) = ceil(715.00) = 715
        Assert.Equal(R(22000m), c.ContributionWages);
    }

    // ---------------------------------------------------------------- contribution-period boundaries (Apr–Sep / Oct–Mar)

    [Fact]
    public void Contribution_periods_are_april_september_and_october_march()
    {
        // CP1 = 1 Apr – 30 Sep.
        Assert.Equal(new DateOnly(2025, 4, 1), EsiContribution.ContributionPeriodStart(new DateOnly(2025, 8, 20)));
        Assert.Equal(new DateOnly(2025, 9, 30), EsiContribution.ContributionPeriodEnd(new DateOnly(2025, 8, 20)));

        // CP2 = 1 Oct – 31 Mar (Oct–Dec in the same year, Jan–Mar in the next).
        Assert.Equal(new DateOnly(2025, 10, 1), EsiContribution.ContributionPeriodStart(new DateOnly(2025, 11, 5)));
        Assert.Equal(new DateOnly(2026, 3, 31), EsiContribution.ContributionPeriodEnd(new DateOnly(2025, 11, 5)));
        Assert.Equal(new DateOnly(2025, 10, 1), EsiContribution.ContributionPeriodStart(new DateOnly(2026, 2, 15)));
        Assert.Equal(new DateOnly(2026, 3, 31), EsiContribution.ContributionPeriodEnd(new DateOnly(2026, 2, 15)));
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void Negative_wages_and_rates_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EsiContribution.ComputeMember(-1m, HighDaily));
        Assert.Throws<ArgumentOutOfRangeException>(() => EsiContribution.ComputeMember(20000m, HighDaily, employeeRateBasisPoints: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EsiContribution.ComputeMember(20000m, HighDaily, employerRateBasisPoints: -1));
    }
}
