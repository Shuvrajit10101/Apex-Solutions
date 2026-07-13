using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-7 <b>§192 salary-TDS computation</b> contract (RQ-12; Finance Act 2025 / §115BAC(1A) / §87A;
/// A14-verified FY 2025-26 / AY 2026-27) — the pure <see cref="SalaryIncomeTax"/> slab / standard-deduction / §87A
/// rebate (new-regime marginal relief + old-regime cliff) / surcharge (+ marginal relief) / 4% cess / average-rate
/// monthly spread. The headline oracles are the four hand-derived A14 golden fixtures:
/// <list type="number">
///   <item>NEW gross ₹15,00,000 → taxable ₹14,25,000 → slab ₹93,750, cess ₹3,750 → annual ₹97,500, monthly ₹8,125;</item>
///   <item>NEW taxable ₹12,00,000 → slab ₹60,000, §87A rebate ₹60,000 → annual ₹0, monthly ₹0;</item>
///   <item>NEW taxable ₹12,10,000 → slab ₹61,500, marginal-relief rebate ₹51,500 → tax ₹10,000 + cess ₹400 = ₹10,400;</item>
///   <item>OLD gross ₹15,00,000, 80C ₹1,50,000 + 80D ₹25,000 → taxable ₹12,75,000 → slab ₹1,95,000, cess ₹7,800 →
///     annual ₹2,02,800, monthly ₹16,900.</item>
/// </list>
/// </summary>
public sealed class SalaryIncomeTaxTests
{
    private static Money R(decimal v) => new(v);

    // ---------------------------------------------------------------- GOLDEN fixture 1 (NEW ₹15L)

    [Fact]
    public void Golden1_new_gross_15L_is_annual_97500_monthly_8125()
    {
        var taxable = SalaryIncomeTax.TaxableIncome(grossSalary: 1_500_000m, allowedDeductions: 0m, TaxRegime.New);
        Assert.Equal(1_425_000m, taxable); // 15,00,000 − 75,000 standard deduction

        var c = SalaryIncomeTax.ComputeAnnual(taxable, TaxRegime.New);
        Assert.Equal(93_750m, c.SlabTax);          // 20,000 + 40,000 + 33,750
        Assert.Equal(0m, c.Rebate87A);             // taxable ≫ ₹12L, well beyond the relief band
        Assert.Equal(0m, c.Surcharge);             // below ₹50L
        Assert.Equal(3_750m, c.Cess);              // 4% of 93,750
        Assert.Equal(R(97_500m), c.AnnualTax);

        // Average-rate monthly spread over 12 remaining months, no prior deduction.
        Assert.Equal(R(8_125m), SalaryIncomeTax.MonthlyTds(c.AnnualTax, Money.Zero, monthsRemaining: 12));
    }

    // ---------------------------------------------------------------- GOLDEN fixture 2 (NEW ₹12L zero-tax ceiling)

    [Fact]
    public void Golden2_new_taxable_12L_is_zero_via_87A_full_rebate()
    {
        var c = SalaryIncomeTax.ComputeAnnual(1_200_000m, TaxRegime.New);
        Assert.Equal(60_000m, c.SlabTax);          // 20,000 + 40,000
        Assert.Equal(60_000m, c.Rebate87A);        // ≤ ₹12L ⇒ full rebate (cap ₹60,000)
        Assert.Equal(Money.Zero, c.AnnualTax);
        Assert.Equal(Money.Zero, SalaryIncomeTax.MonthlyTds(c.AnnualTax, Money.Zero, 12));
    }

    // ---------------------------------------------------------------- GOLDEN fixture 3 (NEW marginal relief)

    [Fact]
    public void Golden3_new_taxable_12_10L_marginal_relief_is_10400()
    {
        var c = SalaryIncomeTax.ComputeAnnual(1_210_000m, TaxRegime.New);
        Assert.Equal(61_500m, c.SlabTax);          // 20,000 + 40,000 + 1,500 (15% of 10,000)
        Assert.Equal(51_500m, c.Rebate87A);        // marginal relief = 61,500 − (12,10,000 − 12,00,000)
        Assert.Equal(10_000m, c.SlabTax - c.Rebate87A); // tax before cess capped at income over ₹12L
        Assert.Equal(400m, c.Cess);                // 4% of 10,000
        Assert.Equal(R(10_400m), c.AnnualTax);
    }

    // ---------------------------------------------------------------- GOLDEN fixture 4 (OLD ₹15L + 80C/80D)

    [Fact]
    public void Golden4_old_gross_15L_with_80C_80D_is_annual_202800_monthly_16900()
    {
        var declaration = new TaxDeclaration { Section80C = R(1_50_000m), Section80D = R(25_000m) };
        var allowed = declaration.AllowedDeductions(TaxRegime.Old);
        Assert.Equal(1_75_000m, allowed.Amount);   // 1,50,000 (80C, capped ₹1.5L) + 25,000 (80D)

        var taxable = SalaryIncomeTax.TaxableIncome(1_500_000m, allowed.Amount, TaxRegime.Old);
        Assert.Equal(1_275_000m, taxable);         // 15,00,000 − 50,000 − 1,75,000

        var c = SalaryIncomeTax.ComputeAnnual(taxable, TaxRegime.Old);
        Assert.Equal(1_95_000m, c.SlabTax);        // 12,500 + 1,00,000 + 82,500
        Assert.Equal(0m, c.Rebate87A);             // taxable ≫ ₹5L
        Assert.Equal(7_800m, c.Cess);              // 4% of 1,95,000
        Assert.Equal(R(2_02_800m), c.AnnualTax);
        Assert.Equal(R(16_900m), SalaryIncomeTax.MonthlyTds(c.AnnualTax, Money.Zero, 12));
    }

    // ---------------------------------------------------------------- new-regime slab boundaries

    [Fact]
    public void New_regime_slab_boundaries_are_marginal()
    {
        Assert.Equal(0m, SalaryIncomeTax.SlabTax(400_000m, TaxRegime.New));            // ≤ ₹4L nil
        Assert.Equal(20_000m, SalaryIncomeTax.SlabTax(800_000m, TaxRegime.New));       // 5% of 4L
        Assert.Equal(60_000m, SalaryIncomeTax.SlabTax(1_200_000m, TaxRegime.New));     // +10% of 4L
        Assert.Equal(120_000m, SalaryIncomeTax.SlabTax(1_600_000m, TaxRegime.New));    // +15% of 4L
        Assert.Equal(200_000m, SalaryIncomeTax.SlabTax(2_000_000m, TaxRegime.New));    // +20% of 4L
        Assert.Equal(300_000m, SalaryIncomeTax.SlabTax(2_400_000m, TaxRegime.New));    // +25% of 4L
        Assert.Equal(600_000m, SalaryIncomeTax.SlabTax(3_400_000m, TaxRegime.New));    // +30% of 10L above ₹24L
    }

    // ---------------------------------------------------------------- old-regime senior exemptions

    [Fact]
    public void Old_regime_below_60_slabs()
    {
        Assert.Equal(0m, SalaryIncomeTax.SlabTax(250_000m, TaxRegime.Old));            // ≤ ₹2.5L nil
        Assert.Equal(12_500m, SalaryIncomeTax.SlabTax(500_000m, TaxRegime.Old));       // 5% of 2.5L
        Assert.Equal(112_500m, SalaryIncomeTax.SlabTax(1_000_000m, TaxRegime.Old));    // +20% of 5L
        Assert.Equal(172_500m, SalaryIncomeTax.SlabTax(1_200_000m, TaxRegime.Old));    // +30% of 2L
    }

    [Fact]
    public void Old_regime_senior_and_super_senior_shift_only_the_first_nil_band()
    {
        // Senior (60–<80): nil to ₹3L ⇒ ₹2,500 less than below-60 at ₹5L.
        Assert.Equal(10_000m, SalaryIncomeTax.SlabTax(500_000m, TaxRegime.Old, SalaryIncomeTax.AgeBand.Senior));
        // Super-senior (80+): nil to ₹5L.
        Assert.Equal(0m, SalaryIncomeTax.SlabTax(500_000m, TaxRegime.Old, SalaryIncomeTax.AgeBand.SuperSenior));
        Assert.Equal(100_000m, SalaryIncomeTax.SlabTax(1_000_000m, TaxRegime.Old, SalaryIncomeTax.AgeBand.SuperSenior)); // 20% of 5L
    }

    // ---------------------------------------------------------------- §87A new-regime marginal-relief band

    [Fact]
    public void New_regime_87A_marginal_relief_zone_edges()
    {
        // At ₹12L exactly: full rebate ⇒ zero.
        Assert.Equal(Money.Zero, SalaryIncomeTax.ComputeAnnual(1_200_000m, TaxRegime.New).AnnualTax);
        // Just inside the band: tax before cess equals income above ₹12L (₹1).
        var justOver = SalaryIncomeTax.ComputeAnnual(1_200_001m, TaxRegime.New);
        Assert.Equal(1m, justOver.SlabTax - justOver.Rebate87A);
        // The relief runs out around ₹12,70,588 (slab 15% band): 60,000 = 0.85 × (income − 12L).
        var atExit = SalaryIncomeTax.ComputeAnnual(1_270_588m, TaxRegime.New);
        Assert.True(atExit.Rebate87A > 0m);
        var beyondExit = SalaryIncomeTax.ComputeAnnual(1_270_589m, TaxRegime.New);
        Assert.Equal(0m, beyondExit.Rebate87A); // marginal relief exhausted — no rebate beyond the band
    }

    // ---------------------------------------------------------------- §87A old-regime hard cliff

    [Fact]
    public void Old_regime_87A_is_a_hard_cliff_at_5L_no_marginal_relief()
    {
        Assert.Equal(Money.Zero, SalaryIncomeTax.ComputeAnnual(500_000m, TaxRegime.Old).AnnualTax); // ≤ ₹5L ⇒ rebate ₹12,500 ⇒ nil
        var justOver = SalaryIncomeTax.ComputeAnnual(500_100m, TaxRegime.Old);
        Assert.Equal(0m, justOver.Rebate87A); // no rebate one rupee over — a cliff, no relief
        Assert.True(justOver.AnnualTax.Amount > 12_000m); // the full ₹13,020 (with cess) lands, not a marginal ₹100
    }

    // ---------------------------------------------------------------- surcharge + surcharge marginal relief (> ₹50L)

    [Fact]
    public void Surcharge_10_percent_applies_above_50L_with_marginal_relief_at_the_threshold()
    {
        // At exactly ₹50L: no surcharge (income must EXCEED ₹50L).
        var at50 = SalaryIncomeTax.ComputeAnnual(5_000_000m, TaxRegime.New);
        Assert.Equal(0m, at50.Surcharge);
        // Just above ₹50L: surcharge is capped by marginal relief so total tax rises smoothly (not a cliff).
        var justOver = SalaryIncomeTax.ComputeAnnual(5_000_100m, TaxRegime.New);
        Assert.True(justOver.Surcharge < 0.10m * justOver.SlabTax); // marginal relief bites
        // Deep in the band the full 10% surcharge applies.
        var deep = SalaryIncomeTax.ComputeAnnual(6_000_000m, TaxRegime.New);
        Assert.Equal(0.10m * deep.SlabTax, deep.Surcharge);
    }

    [Fact]
    public void New_regime_surcharge_is_capped_at_25_percent_no_37_band()
    {
        var c = SalaryIncomeTax.ComputeAnnual(60_000_000m, TaxRegime.New); // ₹6cr — old regime would be 37%
        Assert.Equal(0.25m * c.SlabTax, c.Surcharge);                      // new regime capped at 25%
        var o = SalaryIncomeTax.ComputeAnnual(60_000_000m, TaxRegime.Old);
        Assert.Equal(0.37m * o.SlabTax, o.Surcharge);                      // old regime 37%
    }

    // ---------------------------------------------------------------- surcharge marginal relief honours the age band (F5)

    [Fact]
    public void Surcharge_marginal_relief_uses_the_age_adjusted_reference_tax_for_a_super_senior()
    {
        // A super-senior (80+) OLD-regime taxpayer just ₹100 over the ₹50L surcharge threshold: marginal relief caps
        // the tax-before-cess at the income-tax on ₹50L computed under the SUPER-SENIOR ₹5L nil band + the ₹100 income
        // over the threshold. Threading the age band (F5) makes the reference tax age-correct; the previous default
        // below-60 reference over-stated it and leaked surcharge.
        var c = SalaryIncomeTax.ComputeAnnual(5_000_100m, TaxRegime.Old, SalaryIncomeTax.AgeBand.SuperSenior);
        var refTaxAtThreshold = SalaryIncomeTax.SlabTax(5_000_000m, TaxRegime.Old, SalaryIncomeTax.AgeBand.SuperSenior);
        Assert.Equal(refTaxAtThreshold + 100m, c.IncomeTaxAfterRebate + c.Surcharge);
        Assert.Equal(70m, c.Surcharge); // 13,00,030 tax + 70 surcharge = 13,00,100 = ref 13,00,000 + ₹100 over ₹50L
    }

    // ---------------------------------------------------------------- average-rate monthly spread + true-up

    [Fact]
    public void Monthly_tds_spreads_the_residual_over_remaining_months_and_never_negative()
    {
        var annual = R(97_500m);
        // Fresh year: 97,500 / 12 = 8,125.
        Assert.Equal(R(8_125m), SalaryIncomeTax.MonthlyTds(annual, Money.Zero, 12));
        // Mid-year true-up: ₹40,000 already deducted, 4 months left ⇒ (97,500 − 40,000)/4 = 14,375.
        Assert.Equal(R(14_375m), SalaryIncomeTax.MonthlyTds(annual, R(40_000m), 4));
        // Over-deducted: never a negative deduction.
        Assert.Equal(Money.Zero, SalaryIncomeTax.MonthlyTds(annual, R(120_000m), 3));
    }

    [Fact]
    public void No_pan_206AA_takes_the_higher_of_average_rate_or_20_percent()
    {
        // Low earner whose average rate < 20%: §206AA lifts the annual withholding to 20% of taxable.
        var taxable = 1_425_000m;
        var withPan = SalaryIncomeTax.ComputeAnnual(taxable, TaxRegime.New).AnnualTax; // ₹97,500 (avg ≈ 6.8%)
        var noPan = SalaryIncomeTax.AnnualTaxNoPan(taxable, TaxRegime.New);
        Assert.Equal(R(285_000m), noPan);      // 20% of 14,25,000 (higher than 97,500)
        Assert.True(noPan > withPan);
    }
}
