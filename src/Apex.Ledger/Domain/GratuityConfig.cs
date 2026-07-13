namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>company-level Gratuity configuration</b> (Phase 8 slice 9; catalog §14; Payment of Gratuity Act 1972) — the
/// establishment's gratuity-provision policy the deterministic accrual reads: the statutory <see cref="CapAmount"/>
/// (₹20,00,000 per §4(3), configurable because a government notification can move it), the <see cref="WageBasis"/>
/// (Basic + DA) and which employees a provision run accrues for (<see cref="Population"/>). Present (non-<c>null</c>
/// on <see cref="Company.GratuityConfig"/>) once the establishment provisions for gratuity; a company that never does
/// carries no config and serialises byte-identically to a pre-v37 company (ER-13). Pure data — framework-, DB-,
/// clock- and RNG-free. The <c>Gratuity</c> engine reads the cap; the <c>GratuityProvision</c> service posts the
/// period-end provision voucher for the increase over the prior provision balance.
/// </summary>
public sealed class GratuityConfig
{
    /// <summary>The statutory gratuity ceiling in rupees: <b>₹20,00,000</b> (§4(3), w.e.f. 29-Mar-2018). This is the
    /// Payment-of-Gratuity-Act cap, <b>not</b> the income-tax §10(10) exemption limit.</summary>
    public const decimal DefaultCapAmount = 2_000_000m;

    /// <summary>The maximum accrued gratuity per employee (the Act's §4(3) ceiling); an accrued figure is trimmed to
    /// this. Default <see cref="DefaultCapAmount"/> (₹20,00,000); configurable so a revised notification is a data
    /// change, not a code change.</summary>
    public Money CapAmount { get; set; } = new Money(DefaultCapAmount);

    /// <summary>The wage basis the accrual is computed on; default <see cref="GratuityWageBasis.BasicAndDearnessAllowance"/>
    /// (the Act's "wages" = Basic + DA).</summary>
    public GratuityWageBasis WageBasis { get; set; } = GratuityWageBasis.BasicAndDearnessAllowance;

    /// <summary>Which employees a provision run accrues for; default <see cref="GratuityProvisionPopulation.AllActiveEmployees"/>
    /// (the liability builds pre-vesting, with a "Vested (≥5 yrs)" flag on the register).</summary>
    public GratuityProvisionPopulation Population { get; set; } = GratuityProvisionPopulation.AllActiveEmployees;

    public GratuityConfig() { }

    public GratuityConfig(
        Money capAmount,
        GratuityWageBasis wageBasis = GratuityWageBasis.BasicAndDearnessAllowance,
        GratuityProvisionPopulation population = GratuityProvisionPopulation.AllActiveEmployees)
    {
        if (capAmount.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(capAmount), "Gratuity cap cannot be negative.");
        CapAmount = capAmount;
        WageBasis = wageBasis;
        Population = population;
    }
}
