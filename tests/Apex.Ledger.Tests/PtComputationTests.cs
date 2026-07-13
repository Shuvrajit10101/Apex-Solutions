using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-6 <b>Professional-Tax computation</b> contract (RQ-11; Article 276(2)) — the pure
/// <see cref="ProfessionalTax"/> band selection + February over-charge + gender scope + ₹2,500/year cumulative cap.
/// The headline oracles are the A14-verified state slabs: MH man ₹12,000 → ₹200/mo (₹300 Feb, FY total ₹2,500);
/// MH man ₹9,000 → ₹175/mo; MH woman ₹12,000 → ₹0; KA ₹30,000 → ₹200/mo + ₹300 Feb = ₹2,500; KA ₹20,000 → ₹0;
/// WB ₹15,000 → ₹110/mo; WB ₹30,000 → ₹150/mo.
/// </summary>
public sealed class PtComputationTests
{
    private static Money R(decimal v) => new(v);

    // GST state codes.
    private const string MH = "27";
    private const string KA = "29";
    private const string WB = "19";

    private static PtConfig SeededConfig(string? stateCode)
    {
        var cfg = new PtConfig(stateCode);
        foreach (var s in ProfessionalTax.SeedSlabTables()) cfg.AddSlabTable(s);
        return cfg;
    }

    private static PtSlab Slab(string state, string? gender)
    {
        var cfg = SeededConfig(state);
        return cfg.ResolveSlab(gender)!;
    }

    // ---------------------------------------------------------------- Maharashtra (gender-scoped)

    [Fact]
    public void Mh_man_12000_is_200_per_month_and_300_in_february()
    {
        var slab = Slab(MH, "Male");
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, month: 4));   // April
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, month: 1));   // January
        Assert.Equal(R(300m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, month: 2));   // February over-charge
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, month: 3));   // March
    }

    [Fact]
    public void Mh_man_9000_is_175_per_month_with_no_february_quirk()
    {
        var slab = Slab(MH, "Male");
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 9000m, month: 5));
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 9000m, month: 2)); // no override on the ₹175 band
    }

    [Fact]
    public void Mh_man_band_boundaries_are_inclusive()
    {
        var slab = Slab(MH, "Male");
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 7500m, 6));   // ≤7,500 Nil
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 7501m, 6));      // 7,501 → 175
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 10000m, 6));     // 10,000 → 175
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 10001m, 6));     // 10,001 → 200
    }

    [Fact]
    public void Fractional_ptwage_selects_the_band_by_half_up_rounding_matching_the_register()
    {
        // F1: band selection rounds the PT-wage to whole rupees HALF-UP (away from zero) — the same rounding the PT
        // register displays the wage with — so the band always agrees with the shown wage. A pro-rated gross of
        // ₹10,000.50 rounds to ₹10,001 ⇒ the >₹10,000 (₹200) band; flooring would wrongly keep the ₹175 band while the
        // register shows ₹10,001 next to it.
        var slab = Slab(MH, "Male");
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 10000.50m, 6)); // half-up → 10,001 → ₹200
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 10000.49m, 6)); // just under the midpoint → 10,000 → ₹175
        // Whole-rupee wages are unaffected by the rounding.
        Assert.Equal(R(175m), ProfessionalTax.MonthlyBeforeCap(slab, 10000m, 6));
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 10001m, 6));
    }

    [Fact]
    public void Mh_woman_12000_is_exempt()
    {
        var slab = Slab(MH, "Female");
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 6));  // ≤25,000 Nil for women
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 25000m, 6));
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 25001m, 6));     // >25,000 → 200
        Assert.Equal(R(300m), ProfessionalTax.MonthlyBeforeCap(slab, 25001m, 2));     // Feb over-charge
    }

    [Fact]
    public void Mh_resolves_the_male_table_when_gender_is_unset()
    {
        // An unset/unknown gender in a gender-scoped state falls back to the (male) table — never silently exempt.
        var cfg = SeededConfig(MH);
        var slab = cfg.ResolveSlab(gender: null)!;
        Assert.Equal(PtGenderScope.Male, slab.GenderScope);
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 6));
    }

    // ---------------------------------------------------------------- Karnataka (no gender)

    [Fact]
    public void Ka_30000_is_200_per_month_plus_300_february_totalling_2500()
    {
        var slab = Slab(KA, "Male"); // KA ignores gender (Any table wins)
        Assert.Equal(PtGenderScope.Any, slab.GenderScope);
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 30000m, 6));
        Assert.Equal(R(300m), ProfessionalTax.MonthlyBeforeCap(slab, 30000m, 2));
        Assert.Equal(2500m, FullYearTotal(slab, 30000m));
    }

    [Fact]
    public void Ka_20000_is_exempt_below_the_25000_threshold()
    {
        var slab = Slab(KA, null);
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 20000m, 6));
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 24999m, 6));
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 25000m, 6)); // ≥25,000 → 200
    }

    // ---------------------------------------------------------------- West Bengal (no gender, no Feb quirk)

    [Fact]
    public void Wb_15000_is_110_per_month()
    {
        var slab = Slab(WB, null);
        Assert.Equal(R(110m), ProfessionalTax.MonthlyBeforeCap(slab, 15000m, 6));
        Assert.Equal(R(110m), ProfessionalTax.MonthlyBeforeCap(slab, 15000m, 2)); // no Feb override in WB
    }

    [Fact]
    public void Wb_30000_is_150_per_month()
    {
        var slab = Slab(WB, null);
        Assert.Equal(R(150m), ProfessionalTax.MonthlyBeforeCap(slab, 30000m, 6));
        Assert.Equal(Money.Zero, ProfessionalTax.MonthlyBeforeCap(slab, 10000m, 6)); // ≤10,000 Nil
        Assert.Equal(R(200m), ProfessionalTax.MonthlyBeforeCap(slab, 40001m, 6));    // >40,000 → 200
    }

    // ---------------------------------------------------------------- None (no active state)

    [Fact]
    public void No_active_state_resolves_no_slab_so_pt_is_zero()
    {
        var cfg = SeededConfig(stateCode: null); // "None"
        Assert.Null(cfg.ResolveSlab("Male"));
    }

    // ---------------------------------------------------------------- the ₹2,500/year hard cap

    [Fact]
    public void Annual_cap_trims_the_last_deduction_to_2500()
    {
        Assert.Equal(R(100m), ProfessionalTax.ApplyAnnualCap(R(200m), priorFyCumulative: R(2400m))); // only ₹100 head-room
        Assert.Equal(Money.Zero, ProfessionalTax.ApplyAnnualCap(R(200m), priorFyCumulative: R(2500m))); // cap reached
        Assert.Equal(R(200m), ProfessionalTax.ApplyAnnualCap(R(300m), priorFyCumulative: R(2300m)));  // ₹200 head-room of ₹300
        Assert.Equal(R(200m), ProfessionalTax.ApplyAnnualCap(R(200m), priorFyCumulative: R(0m)));     // untrimmed
    }

    [Fact]
    public void A_full_year_of_the_top_band_never_exceeds_2500()
    {
        // MH man ₹12,000: ₹200×11 + ₹300 (Feb) = exactly ₹2,500 — the cap does not even bite.
        Assert.Equal(2500m, FullYearTotal(Slab(MH, "Male"), 12000m));
        // WB ₹30,000: ₹150×12 = ₹1,800 (under the cap).
        Assert.Equal(1800m, FullYearTotal(Slab(WB, null), 30000m));
        // WB ₹40,001: ₹200×12 = ₹2,400 (under the cap).
        Assert.Equal(2400m, FullYearTotal(Slab(WB, null), 40001m));
    }

    [Fact]
    public void A_misconfigured_over_2500_slab_is_trimmed_at_year_end()
    {
        // A hand-mis-configured flat ₹250/month slab would sum to ₹3,000 over a year; the ₹2,500 cap trims it.
        var bad = new PtSlab(Guid.NewGuid(), "99", PtGenderScope.Any, new[]
        {
            new PtSlabBand(R(0m), null, R(250m)),
        });
        Assert.Equal(2500m, FullYearTotal(bad, 50000m));
    }

    // ---------------------------------------------------------------- financial-year window

    [Fact]
    public void Financial_year_starts_on_1_april()
    {
        Assert.Equal(new DateOnly(2025, 4, 1), ProfessionalTax.FinancialYearStart(new DateOnly(2025, 4, 1)));
        Assert.Equal(new DateOnly(2025, 4, 1), ProfessionalTax.FinancialYearStart(new DateOnly(2025, 12, 31)));
        Assert.Equal(new DateOnly(2025, 4, 1), ProfessionalTax.FinancialYearStart(new DateOnly(2026, 1, 15)));
        Assert.Equal(new DateOnly(2025, 4, 1), ProfessionalTax.FinancialYearStart(new DateOnly(2026, 3, 31)));
        Assert.Equal(new DateOnly(2026, 4, 1), ProfessionalTax.FinancialYearStart(new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Seed_yields_four_tables_mh_men_mh_women_ka_wb()
    {
        var seed = ProfessionalTax.SeedSlabTables();
        Assert.Equal(4, seed.Count);
        Assert.Contains(seed, s => s.StateCode == MH && s.GenderScope == PtGenderScope.Male);
        Assert.Contains(seed, s => s.StateCode == MH && s.GenderScope == PtGenderScope.Female);
        Assert.Contains(seed, s => s.StateCode == KA && s.GenderScope == PtGenderScope.Any);
        Assert.Contains(seed, s => s.StateCode == WB && s.GenderScope == PtGenderScope.Any);
    }

    // ---- helper: simulate a full Apr–Mar year applying the ₹2,500 cap against a running FY cumulative ----

    private static decimal FullYearTotal(PtSlab slab, decimal ptWages)
    {
        decimal cumulative = 0m;
        foreach (var month in new[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 1, 2, 3 }) // Apr … Mar
        {
            var monthly = ProfessionalTax.MonthlyBeforeCap(slab, ptWages, month);
            var capped = ProfessionalTax.ApplyAnnualCap(monthly, new Money(cumulative));
            cumulative += capped.Amount;
        }
        return cumulative;
    }
}
