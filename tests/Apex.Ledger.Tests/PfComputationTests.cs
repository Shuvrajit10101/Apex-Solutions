using Apex.Ledger;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-4 <b>Provident-Fund computation</b> contract (RQ-9; EPFO ContributionRate) — the pure
/// <see cref="PfContribution"/> EPF/EPS/EDLI split + establishment admin charge. The headline oracles are the two
/// hand-derived golden members:
/// <list type="bullet">
///   <item>(a) EPF wages ₹15,000 → EE_EPF 1,800 / EPS 1,250 / ER_EPF 550 / EDLI 75 / A/c2 500 / A/c22 0;</item>
///   <item>(b) higher-wage opt-in ₹20,000 → 2,400 / 1,250 / 1,150 / 75 / 500 / 0.</item>
/// </list>
/// Every case asserts the anti-3.67% invariant <c>EPS + ER_EPF == EE_EPF</c> (the employer total equals the
/// employee total, proving the residual is never a hardcoded 3.67%).
/// </summary>
public sealed class PfComputationTests
{
    private static Money R(decimal v) => new(v);

    // ---------------------------------------------------------------- golden members

    [Fact]
    public void Golden_a_epf_wages_15000_at_ceiling()
    {
        var c = PfContribution.ComputeMember(15000m, contributeOnHigherWages: false);

        Assert.Equal(R(1800m), c.EmployeeEpf);
        Assert.Equal(R(1250m), c.EmployerPension);
        Assert.Equal(R(550m), c.EmployerEpf);
        Assert.Equal(R(75m), c.Edli);
        Assert.Equal(R(15000m), c.EpfWages);
        Assert.Equal(R(15000m), c.EpsWages);
        Assert.Equal(R(15000m), c.EdliWages);

        // The anti-3.67% invariant: employer EPF + EPS == employee EPF (12% of EPF wages).
        Assert.Equal(c.EmployeeEpf, c.EmployerPension + c.EmployerEpf);

        // A/c 2 estab floor applies at the aggregate: a single ₹15,000 member's 0.5% is ₹75, floored to ₹500.
        Assert.Equal(R(500m), PfContribution.ComputeAdminCharge(new[] { c.EpfWages }));
        Assert.Equal(Money.Zero, PfContribution.EdliAdminCharge); // A/c 22 NIL
    }

    [Fact]
    public void Golden_b_higher_wage_optin_20000()
    {
        var c = PfContribution.ComputeMember(20000m, contributeOnHigherWages: true);

        Assert.Equal(R(2400m), c.EmployeeEpf);   // 12% of the full 20,000
        Assert.Equal(R(1250m), c.EmployerPension); // 8.33% of the CAPPED 15,000, capped 1,250
        Assert.Equal(R(1150m), c.EmployerEpf);   // 2,400 − 1,250
        Assert.Equal(R(75m), c.Edli);            // 0.5% of the capped 15,000
        Assert.Equal(R(20000m), c.EpfWages);     // EPF wages uncapped on opt-in
        Assert.Equal(R(15000m), c.EpsWages);
        Assert.Equal(R(15000m), c.EdliWages);

        Assert.Equal(c.EmployeeEpf, c.EmployerPension + c.EmployerEpf);

        // A/c 2 uses the EPF-wage basis (20,000): 0.5% = 100, floored to 500.
        Assert.Equal(R(500m), PfContribution.ComputeAdminCharge(new[] { c.EpfWages }));
    }

    [Fact]
    public void A_high_earner_without_optin_is_capped_at_the_ceiling()
    {
        var c = PfContribution.ComputeMember(20000m, contributeOnHigherWages: false);

        Assert.Equal(R(1800m), c.EmployeeEpf);   // 12% of the CAPPED 15,000
        Assert.Equal(R(1250m), c.EmployerPension);
        Assert.Equal(R(550m), c.EmployerEpf);
        Assert.Equal(R(75m), c.Edli);
        Assert.Equal(R(15000m), c.EpfWages);     // EPF wages capped when not opted in
        Assert.Equal(c.EmployeeEpf, c.EmployerPension + c.EmployerEpf);
    }

    // ---------------------------------------------------------------- the natural 3.67% (never hardcoded)

    [Fact]
    public void Below_ceiling_the_employer_epf_is_the_natural_residual_not_a_hardcoded_3_67_percent()
    {
        // ₹10,000 wages: EE = 12% = 1,200; EPS = 8.33% = 833; ER_EPF = 1,200 − 833 = 367 (= 3.67% of 10,000),
        // but DERIVED by subtraction — never re-computed as 3.67%.
        var c = PfContribution.ComputeMember(10000m, contributeOnHigherWages: false);
        Assert.Equal(R(1200m), c.EmployeeEpf);
        Assert.Equal(R(833m), c.EmployerPension);
        Assert.Equal(R(367m), c.EmployerEpf);
        Assert.Equal(c.EmployeeEpf, c.EmployerPension + c.EmployerEpf);
    }

    // ---------------------------------------------------------------- 10% special-establishment rate

    [Fact]
    public void The_reduced_10_percent_rate_lowers_only_the_epf_share_not_eps_or_edli()
    {
        var c = PfContribution.ComputeMember(15000m, contributeOnHigherWages: false,
            epfRateBasisPoints: PfContribution.ReducedEpfRateBasisPoints);

        Assert.Equal(R(1500m), c.EmployeeEpf);   // 10% of 15,000
        Assert.Equal(R(1250m), c.EmployerPension); // EPS unchanged (8.33%, capped)
        Assert.Equal(R(250m), c.EmployerEpf);    // 1,500 − 1,250
        Assert.Equal(R(75m), c.Edli);            // EDLI unchanged
        Assert.Equal(c.EmployeeEpf, c.EmployerPension + c.EmployerEpf);
    }

    // ---------------------------------------------------------------- establishment admin (A/c 2) aggregate + floor

    [Fact]
    public void Admin_charge_floors_the_aggregate_once_not_per_member()
    {
        // Three ₹15,000 members: Σ = 45,000; 0.5% = 225; floored ONCE to ₹500 (a per-member floor would be 1,500).
        var three = new[] { R(15000m), R(15000m), R(15000m) };
        Assert.Equal(R(500m), PfContribution.ComputeAdminCharge(three));

        // A large establishment exceeds the floor: Σ = 1,000,000; 0.5% = 5,000 (> 500).
        var big = new[] { R(500000m), R(500000m) };
        Assert.Equal(R(5000m), PfContribution.ComputeAdminCharge(big));
    }

    [Fact]
    public void Admin_charge_is_75_when_there_is_no_contributory_member()
    {
        Assert.Equal(R(75m), PfContribution.ComputeAdminCharge(Array.Empty<Money>()));
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void Negative_wages_and_an_invalid_rate_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PfContribution.ComputeMember(-1m, false));
        Assert.Throws<ArgumentException>(() => PfContribution.ComputeMember(15000m, false, epfRateBasisPoints: 999));
    }
}
