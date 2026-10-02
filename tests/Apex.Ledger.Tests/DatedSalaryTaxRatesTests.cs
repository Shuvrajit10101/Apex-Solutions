using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>DEFECT T1-26 — the §192 salary income-tax engine was DATE-BLIND.</b> This is the regression suite for the
/// fix, and every test in it <b>fails to compile on today's main</b>, because on main
/// <c>SalaryIncomeTax.Slabs(TaxRegime, AgeBand)</c> takes no date and <see cref="SalaryTaxRates"/> does not exist.
/// The headline proof is not that the code compiles with a date parameter — it is that a payslip in one financial
/// year and a payslip in another produce <b>different tax where the law differs</b>, and the <b>same</b> tax where it
/// does not.
///
/// <para><b>THE WRONG MONEY, MEASURED.</b> A ₹15,00,000 new-regime salary: the FY 2024-25 law charges ₹1,30,000 and
/// the FY 2025-26 law charges ₹97,500. Before this fix the engine charged ₹97,500 for <b>both</b>, an under-deduction
/// of <b>₹32,500 per employee per year</b> on any FY 2024-25 period in a book — and the same figure flowed
/// unchanged into Form 24Q Annexure II and Form 16 Part B, which are asserted equal to it.</para>
///
/// <para><b>SOURCES</b> for both years' tables are cited by content on <see cref="SalaryTaxRates"/> itself
/// (incometax.gov.in AY 2025-26 and AY 2026-27 salaried-individual pages, retrieved 2026-09-15). No figure asserted
/// here is derived from anything else.</para>
/// </summary>
public sealed class DatedSalaryTaxRatesTests
{
    private static readonly SalaryTaxRates Fy2024 = SalaryTaxRates.ForFinancialYear(2024);
    private static readonly SalaryTaxRates Fy2025 = SalaryTaxRates.ForFinancialYear(2025);

    // ================================================================ 1. THE HEADLINE: the years differ

    /// <summary>
    /// 🔴 The same ₹15,00,000 new-regime salary, taxed under each year's own law. This single assertion is the whole
    /// defect: the two figures were IDENTICAL before the fix.
    /// </summary>
    [Fact]
    public void The_same_salary_is_taxed_differently_in_FY2024_25_and_FY2025_26()
    {
        // FY 2024-25 slabs (0–3L nil · 3–7L 5% · 7–10L 10% · 10–12L 15% · 12–15L 20%):
        //   20,000 + 30,000 + 30,000 + 45,000 = 1,25,000 slab; cess 4% = 5,000 ⇒ ₹1,30,000.
        var taxable24 = SalaryIncomeTax.TaxableIncome(15_00_000m, 0m, TaxRegime.New, Fy2024);
        Assert.Equal(14_25_000m, taxable24); // standard deduction ₹75,000 in both years
        var c24 = SalaryIncomeTax.ComputeAnnual(taxable24, TaxRegime.New, Fy2024);
        Assert.Equal(1_25_000m, c24.SlabTax);
        Assert.Equal(5_000m, c24.Cess);
        Assert.Equal(new Money(1_30_000m), c24.AnnualTax);

        // FY 2025-26 slabs (0–4L nil · 4–8L 5% · 8–12L 10% · 12–16L 15%):
        //   20,000 + 40,000 + 33,750 = 93,750 slab; cess 4% = 3,750 ⇒ ₹97,500.
        var taxable25 = SalaryIncomeTax.TaxableIncome(15_00_000m, 0m, TaxRegime.New, Fy2025);
        var c25 = SalaryIncomeTax.ComputeAnnual(taxable25, TaxRegime.New, Fy2025);
        Assert.Equal(new Money(97_500m), c25.AnnualTax);

        // 🔴 The gap the date-blind engine was silently applying to every FY 2024-25 payroll.
        Assert.NotEqual(c24.AnnualTax, c25.AnnualTax);
        Assert.Equal(32_500m, c24.AnnualTax.Amount - c25.AnnualTax.Amount);
    }

    /// <summary>
    /// The §87A ceiling moved from ₹7,00,000 to ₹12,00,000 between the two years, and it is the single largest
    /// year-over-year difference for an ordinary salary. At a taxable ₹8,00,000 the older law charges ₹31,200 and
    /// the newer charges nothing at all.
    /// </summary>
    [Fact]
    public void The_87A_ceiling_move_changes_an_ordinary_salary_from_31200_to_zero()
    {
        // FY 2024-25: slab 20,000 + 10,000 = 30,000. Taxable ₹8L is ABOVE the ₹7L ceiling and the marginal-relief
        // band is already exhausted (30,000 − 1,00,000 < 0), so no rebate. 30,000 + 1,200 cess = ₹31,200.
        var c24 = SalaryIncomeTax.ComputeAnnual(8_00_000m, TaxRegime.New, Fy2024);
        Assert.Equal(30_000m, c24.SlabTax);
        Assert.Equal(0m, c24.Rebate87A);
        Assert.Equal(new Money(31_200m), c24.AnnualTax);

        // FY 2025-26: slab 5% × ₹4L = 20,000, taxable is under the ₹12L ceiling ⇒ fully rebated.
        var c25 = SalaryIncomeTax.ComputeAnnual(8_00_000m, TaxRegime.New, Fy2025);
        Assert.Equal(20_000m, c25.SlabTax);
        Assert.Equal(20_000m, c25.Rebate87A);
        Assert.Equal(Money.Zero, c25.AnnualTax);
    }

    /// <summary>
    /// The FY 2024-25 §87A marginal-relief band exists and is anchored on <b>that</b> year's ₹7,00,000 ceiling, not
    /// on the newer ₹12,00,000 one — proving the rebate limb is dated, not just the slabs. A half-dated engine (slabs
    /// dated, rebate not) would be a NEW wrong-money path, so this is asserted directly.
    /// </summary>
    [Fact]
    public void The_87A_marginal_relief_band_is_anchored_on_its_own_years_ceiling()
    {
        // ₹7,10,000: slab = 20,000 + 10% × 10,000 = 21,000; relief = 21,000 − 10,000 = 11,000 rebate;
        // tax before cess = 10,000, capped at the income above the ₹7L ceiling. Cess 400 ⇒ ₹10,400.
        var c = SalaryIncomeTax.ComputeAnnual(7_10_000m, TaxRegime.New, Fy2024);
        Assert.Equal(21_000m, c.SlabTax);
        Assert.Equal(11_000m, c.Rebate87A);
        Assert.Equal(10_000m, c.IncomeTaxAfterRebate);
        Assert.Equal(new Money(10_400m), c.AnnualTax);

        // At exactly the ceiling the rebate is full and the tax is nil.
        Assert.Equal(Money.Zero, SalaryIncomeTax.ComputeAnnual(7_00_000m, TaxRegime.New, Fy2024).AnnualTax);

        // The ceiling and cap published for each year are the ones actually used.
        Assert.Equal(7_00_000m, Fy2024.NewRegimeRebateTaxableCeiling);
        Assert.Equal(25_000m, Fy2024.NewRegimeRebateCap);
        Assert.Equal(12_00_000m, Fy2025.NewRegimeRebateTaxableCeiling);
        Assert.Equal(60_000m, Fy2025.NewRegimeRebateCap);
    }

    /// <summary>
    /// 🔴 The negative control, and it matters as much as the positive one: the <b>old regime did NOT change</b>
    /// between these two years, so it must compute IDENTICALLY. A fix that made every year differ would be as wrong
    /// as one that made none of them differ — it would just fail in the other direction.
    /// </summary>
    [Fact]
    public void The_old_regime_is_unchanged_between_the_two_years_and_must_compute_identically()
    {
        foreach (var taxable in new[] { 2_50_000m, 5_00_000m, 5_00_100m, 10_00_000m, 12_75_000m, 60_00_000m })
        {
            var a = SalaryIncomeTax.ComputeAnnual(taxable, TaxRegime.Old, Fy2024);
            var b = SalaryIncomeTax.ComputeAnnual(taxable, TaxRegime.Old, Fy2025);
            Assert.Equal(a.SlabTax, b.SlabTax);
            Assert.Equal(a.Rebate87A, b.Rebate87A);
            Assert.Equal(a.Surcharge, b.Surcharge);
            Assert.Equal(a.AnnualTax, b.AnnualTax);
        }

        // Including the age-band tables, which are published per year and happen to coincide.
        foreach (var age in new[] { SalaryIncomeTax.AgeBand.Below60, SalaryIncomeTax.AgeBand.Senior, SalaryIncomeTax.AgeBand.SuperSenior })
            Assert.Equal(
                SalaryIncomeTax.SlabTax(10_00_000m, TaxRegime.Old, Fy2024, age),
                SalaryIncomeTax.SlabTax(10_00_000m, TaxRegime.Old, Fy2025, age));

        // The standard deduction also did not move, so it must not fork either.
        Assert.Equal(Fy2024.StandardDeduction(TaxRegime.Old), Fy2025.StandardDeduction(TaxRegime.Old));
        Assert.Equal(Fy2024.StandardDeduction(TaxRegime.New), Fy2025.StandardDeduction(TaxRegime.New));
    }

    /// <summary>§115BAC publishes ONE table for every individual: the new regime must not vary with the age band,
    /// in either year. (The old regime's basic exemption is the only thing age moves.)</summary>
    [Fact]
    public void The_new_regime_does_not_vary_with_age_in_either_year()
    {
        foreach (var rates in new[] { Fy2024, Fy2025 })
        foreach (var age in new[] { SalaryIncomeTax.AgeBand.Senior, SalaryIncomeTax.AgeBand.SuperSenior })
            Assert.Equal(
                SalaryIncomeTax.SlabTax(15_00_000m, TaxRegime.New, rates),
                SalaryIncomeTax.SlabTax(15_00_000m, TaxRegime.New, rates, age));
    }

    // ================================================================ 2. Resolution by date

    [Theory]
    // The financial year runs April to March, so the year boundary is 31-Mar / 1-Apr, not 31-Dec.
    [InlineData(2025, 4, 1, 2025)]
    [InlineData(2025, 12, 31, 2025)]
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2024, 4, 1, 2024)]
    [InlineData(2024, 3, 31, 2023)]
    public void The_financial_year_of_a_payroll_period_is_resolved_on_the_April_boundary(int y, int m, int d, int expectedFy)
    {
        Assert.Equal(expectedFy, SalaryTaxRates.FinancialYearStartYearOf(new DateOnly(y, m, d)));
        Assert.Equal(expectedFy, SalaryTaxRates.ForPeriod(new DateOnly(y, m, d)).FinancialYearStartYear);
    }

    /// <summary>A March period and an April period one day apart must resolve to DIFFERENT years — and, across the
    /// 2025 boundary, to different tax. This is the test a date-blind engine cannot pass at all.</summary>
    [Fact]
    public void One_day_across_the_April_boundary_changes_which_years_law_applies()
    {
        var march = SalaryTaxRates.ForPeriod(new DateOnly(2025, 3, 31)); // FY 2024-25
        var april = SalaryTaxRates.ForPeriod(new DateOnly(2025, 4, 30)); // FY 2025-26
        Assert.Equal(2024, march.FinancialYearStartYear);
        Assert.Equal(2025, april.FinancialYearStartYear);

        Assert.Equal(new Money(1_30_000m), SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, march).AnnualTax);
        Assert.Equal(new Money(97_500m), SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, april).AnnualTax);
    }

    // ================================================================ 3. Unnotified years are FLAGGED, not faked

    /// <summary>
    /// 🔴 FY 2026-27 (AY 2027-28) has no published rate table — a search restricted to the Department's own domains
    /// returned none on 2026-09-15 — so none is shipped. The engine carries the latest notified year forward so a
    /// live payroll does not break, and <b>says so</b>. The difference from the defect being fixed is exactly this:
    /// the substitution used to be silent and undiscoverable.
    /// </summary>
    [Fact]
    public void An_unnotified_year_carries_the_latest_table_forward_and_is_flagged_provisional()
    {
        var fy2026 = SalaryTaxRates.ForFinancialYear(2026);
        Assert.True(fy2026.IsProvisional);
        Assert.Equal(2026, fy2026.FinancialYearStartYear);
        Assert.Equal(2025, fy2026.NotifiedFinancialYearStartYear);

        var c = SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, fy2026);
        Assert.True(c.RatesAreProvisional);
        Assert.Equal("2026-27", c.RatesFinancialYearLabel);
        Assert.Equal("2025-26", c.RatesNotifiedFinancialYearLabel);
        Assert.NotNull(c.ProvisionalRatesNote);
        Assert.Contains("2026-27", c.ProvisionalRatesNote!, StringComparison.Ordinal);
        Assert.Contains("provisional", c.ProvisionalRatesNote!, StringComparison.Ordinal);

        // The figures are the carried-forward year's — stated, not hidden.
        Assert.Equal(SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, Fy2025).AnnualTax, c.AnnualTax);
    }

    /// <summary>A period OLDER than the earliest shipped table is flagged the same way, rather than being priced on
    /// a fabricated table for a year nobody sourced.</summary>
    [Fact]
    public void A_year_older_than_the_earliest_notified_table_is_also_flagged_provisional()
    {
        var fy2020 = SalaryTaxRates.ForFinancialYear(2020);
        Assert.True(fy2020.IsProvisional);
        Assert.Equal(2024, fy2020.NotifiedFinancialYearStartYear);
        Assert.NotNull(SalaryIncomeTax.ComputeAnnual(10_00_000m, TaxRegime.New, fy2020).ProvisionalRatesNote);
    }

    /// <summary>The two shipped years are NOT provisional, and the published list of notified years says exactly
    /// which they are — so a reader can tell what this build can and cannot compute without carry-over.</summary>
    [Fact]
    public void The_two_sourced_years_are_notified_and_are_the_only_ones()
    {
        Assert.Equal(new[] { 2024, 2025 }, SalaryTaxRates.NotifiedFinancialYears);
        Assert.False(Fy2024.IsProvisional);
        Assert.False(Fy2025.IsProvisional);
        Assert.Null(SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, Fy2025).ProvisionalRatesNote);
    }

    // ================================================================ 4. The 4% cess ruling

    /// <summary>
    /// 🔴 <b>LIMB (c) OF THE CESS RULING, TESTED EXPLICITLY: NO EXISTING BOOK CHANGES BEHAVIOUR ON UPGRADE.</b> A
    /// company that has set no cess rate — which is every book that existed before v64 — charges the statutory 4%,
    /// and every golden figure is untouched. A silent change to a shipped payroll deduction is the worst failure
    /// available on this path, so it is asserted rather than reasoned about.
    /// </summary>
    [Fact]
    public void A_company_that_has_set_no_cess_rate_charges_the_statutory_4_percent_unchanged()
    {
        var c = CompanyFactory.CreateSeeded("Cess Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        Assert.Empty(c.IncomeTaxCessRates);
        Assert.Null(c.ResolveIncomeTaxCessRate(new DateOnly(2026, 3, 31)));

        var rates = SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2026, 3, 31));
        Assert.Equal(0.04m, rates.CessRate);
        Assert.False(rates.CessRateIsCompanyOverride);

        // The Phase-8 golden fixture, recomputed through the company path: identical to the pre-v64 figure.
        var computed = SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, rates);
        Assert.Equal(3_750m, computed.Cess);
        Assert.Equal(new Money(97_500m), computed.AnnualTax);
        Assert.False(computed.CessRateIsCompanyOverride);
    }

    /// <summary>Limb (b): a company can set its own rate, and it changes the tax.</summary>
    [Fact]
    public void A_company_can_set_its_own_cess_rate_and_it_changes_the_tax()
    {
        var c = CompanyFactory.CreateSeeded("Cess Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 500)); // 5%

        var rates = SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2026, 3, 31));
        Assert.Equal(0.05m, rates.CessRate);
        Assert.True(rates.CessRateIsCompanyOverride);

        // 5% of the ₹93,750 slab tax = ₹4,687.50 ⇒ ₹4,688 half-up; annual = 93,750 + 4,688.
        var computed = SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, rates);
        Assert.Equal(4_688m, computed.Cess);
        Assert.Equal(new Money(98_438m), computed.AnnualTax);
        Assert.True(computed.CessRateIsCompanyOverride);

        // …and the slabs are NOT company-editable: only the cess moved.
        Assert.Equal(93_750m, computed.SlabTax);
    }

    /// <summary>
    /// 🔴 Limb (a): the override is <b>dated</b>, so a past payroll re-computes on the rate that was live WHEN IT
    /// RAN, not on today's. A rate effective 1-Oct-2025 must not reach a September payslip.
    /// </summary>
    [Fact]
    public void A_cess_rate_applies_only_from_its_effective_date_backwards_periods_keep_the_old_rate()
    {
        var c = CompanyFactory.CreateSeeded("Cess Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 10, 1), 600)); // 6% from Oct

        // September: no row at or before the period end ⇒ the statutory 4%.
        var sep = SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2025, 9, 30));
        Assert.Equal(0.04m, sep.CessRate);
        Assert.False(sep.CessRateIsCompanyOverride);

        // October onward: the company's 6%.
        Assert.Equal(0.06m, SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2025, 10, 31)).CessRate);
        Assert.Equal(0.06m, SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2026, 3, 31)).CessRate);

        // A second, later row supersedes it forward only.
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2026, 1, 1), 300)); // 3% from Jan
        Assert.Equal(0.06m, SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2025, 12, 31)).CessRate);
        Assert.Equal(0.03m, SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2026, 1, 31)).CessRate);
        // …and the September period is STILL 4% — the past was not re-priced.
        Assert.Equal(0.04m, SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2025, 9, 30)).CessRate);
    }

    /// <summary>Setting a rate for a date that already has one replaces it, so the screen cannot accumulate two
    /// contradictory rates for the same day.</summary>
    [Fact]
    public void Two_rates_for_the_same_effective_date_collapse_to_the_later_entry()
    {
        var c = CompanyFactory.CreateSeeded("Cess Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 400));
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 500));
        Assert.Single(c.IncomeTaxCessRates);
        Assert.Equal(500, c.IncomeTaxCessRates[0].RateBasisPoints);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void A_cess_rate_outside_0_to_100_percent_is_refused(int basisPoints) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), basisPoints));

    // ================================================================ 5. The return and the certificate

    /// <summary>
    /// 🔴 Form 24Q Annexure II — and therefore Form 16 Part B, which is asserted equal to it — is priced on the
    /// financial year the RETURN IS FOR. This is the limb of T1-26 that reaches a <b>filed government return</b>: on
    /// main the FY 2024-25 Annexure II carried FY 2025-26 figures.
    /// </summary>
    [Fact]
    public void Annexure_II_is_priced_on_the_financial_year_the_return_is_for()
    {
        // The company's own resolution for each FY's 31-March, which is what BuildAnnexureII anchors on.
        var c = CompanyFactory.CreateSeeded("24Q Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var r24 = SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2025, 3, 31));
        var r25 = SalaryTaxRates.ForCompanyPeriod(c, new DateOnly(2026, 3, 31));

        Assert.Equal(2024, r24.FinancialYearStartYear);
        Assert.Equal(2025, r25.FinancialYearStartYear);
        Assert.NotEqual(
            SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, r24).AnnualTax,
            SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, r25).AnnualTax);
    }

    /// <summary>
    /// End to end through the real return builder: a full FY 2024-25 payroll produces an Annexure II total tax of
    /// ₹1,30,000 — the FY 2024-25 figure — where the date-blind engine produced ₹97,500. The certificate's standard
    /// deduction is also read off that year's table.
    /// </summary>
    [Fact]
    public void A_full_FY2024_25_payroll_files_the_FY2024_25_tax_not_the_FY2025_26_tax()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2024, monthlyBasic: 1_25_000m);
        PostFullYear(c, empId, fyStart: 2024);

        var row = Assert.Single(Form24Q.Build(c, 2024, 4).AnnexureII);
        Assert.Equal(new Money(15_00_000m), row.GrossSalary);
        Assert.Equal(new Money(75_000m), row.StandardDeduction);   // FY 2024-25 new-regime SD
        Assert.Equal(new Money(14_25_000m), row.TaxableIncome);
        Assert.Equal(new Money(1_25_000m), row.IncomeTax);          // slab tax after §87A
        Assert.Equal(new Money(5_000m), row.Cess);
        Assert.Equal(new Money(1_30_000m), row.TotalTax);           // NOT the ₹97,500 the defect produced

        // The tax actually withheld over the year reconciles to it (the §192 average-rate spread trues up).
        Assert.Equal(new Money(1_30_000m), row.TaxDeducted);
    }

    /// <summary>The same shape one year later files the FY 2025-26 figure — so the two years genuinely diverge
    /// through the whole posting + return pipeline, not only in the pure calculator.</summary>
    [Fact]
    public void A_full_FY2025_26_payroll_files_the_FY2025_26_tax()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2025, monthlyBasic: 1_25_000m);
        PostFullYear(c, empId, fyStart: 2025);

        var row = Assert.Single(Form24Q.Build(c, 2025, 4).AnnexureII);
        Assert.Equal(new Money(93_750m), row.IncomeTax);
        Assert.Equal(new Money(3_750m), row.Cess);
        Assert.Equal(new Money(97_500m), row.TotalTax);
    }

    /// <summary>A company's own cess rate reaches the filed return too — the rate is not quietly confined to the
    /// payslip while the certificate keeps printing 4%.</summary>
    [Fact]
    public void A_company_cess_override_reaches_the_annexure_II_figures()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2025, monthlyBasic: 1_25_000m);
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 500)); // 5%
        PostFullYear(c, empId, fyStart: 2025);

        var row = Assert.Single(Form24Q.Build(c, 2025, 4).AnnexureII);
        Assert.Equal(new Money(93_750m), row.IncomeTax);
        Assert.Equal(new Money(4_688m), row.Cess);      // 5%, half-up
        Assert.Equal(new Money(98_438m), row.TotalTax);
    }

    // ================================================================ Form 16 Part B — the DISCLOSURE, not just
    //                                                                   the arithmetic

    /// <summary>
    /// 🔴 <b>The certificate states which financial year's law priced it.</b> The arithmetic limb of T1-26 was fixed
    /// the moment <c>BuildAnnexureII</c> resolved a dated table — but the <b>disclosure</b> initially reached only
    /// the Income Tax Computation report, and Form 16 Part B is the document an <b>employee</b> files a return from.
    /// Its reader has no access to the book that produced it and cannot otherwise tell a figure priced on the right
    /// year from one priced on the wrong year.
    /// </summary>
    [Fact]
    public void The_form_16_certificate_states_which_years_tables_priced_part_B()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2024, monthlyBasic: 1_25_000m);
        PostFullYear(c, empId, fyStart: 2024);

        var cert = Form16.Build(c, empId, 2024);

        Assert.NotNull(cert.PartB);
        // It names THIS certificate's year — 2024-25 — not the year the old hard-coded footnote named.
        Assert.Contains("2024-25", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.DoesNotContain("2025-26", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.Contains("Health and Education Cess at 4%", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.Contains("as notified for that year", cert.RateBasisNote, StringComparison.Ordinal);

        // FY 2024-25 IS notified in this build, so there is nothing provisional to disclose.
        Assert.False(cert.RatesAreProvisional);
        Assert.Null(cert.ProvisionalRatesNote);

        // And the basis describes the figures that are actually on the certificate.
        Assert.Equal(new Money(1_30_000m), cert.PartB!.TotalTax);
    }

    /// <summary>
    /// 🔴 <b>A certificate for a year whose rates are NOT notified says so on its face.</b> This is the case the
    /// defect made invisible: the figures are priced on a neighbouring year's law, and before this fix nothing
    /// anywhere told the employee holding the certificate.
    /// </summary>
    [Fact]
    public void The_form_16_certificate_discloses_a_year_whose_rates_are_not_notified()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2026, monthlyBasic: 1_25_000m);
        PostFullYear(c, empId, fyStart: 2026);

        var cert = Form16.Build(c, empId, 2026);

        Assert.True(cert.RatesAreProvisional);
        Assert.NotNull(cert.ProvisionalRatesNote);
        Assert.Contains("FY 2026-27", cert.ProvisionalRatesNote!, StringComparison.Ordinal);
        Assert.Contains("have not been notified", cert.ProvisionalRatesNote!, StringComparison.Ordinal);
        Assert.Contains("provisional", cert.ProvisionalRatesNote!, StringComparison.Ordinal);
        // The basis line names BOTH years — the one applied and the one it was borrowed from.
        Assert.Contains("FY 2025-26", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.Contains("FY 2026-27", cert.RateBasisNote, StringComparison.Ordinal);
    }

    /// <summary>A company that set its own cess rate gets a certificate that says the rate was <b>this book's</b>,
    /// not the statute's — so a reader can tell a lawful departure from an error.</summary>
    [Fact]
    public void The_form_16_certificate_distinguishes_a_company_cess_rate_from_the_statutory_one()
    {
        var (c, empId) = BuildSalaryCompany(fyStart: 2025, monthlyBasic: 1_25_000m);
        c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 500)); // 5%
        PostFullYear(c, empId, fyStart: 2025);

        var cert = Form16.Build(c, empId, 2025);

        Assert.Contains("Health and Education Cess at 5%", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.Contains("this company's own dated rate", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.DoesNotContain("as notified for that year", cert.RateBasisNote, StringComparison.Ordinal);
        Assert.Equal(new Money(4_688m), cert.PartB!.Cess);
    }

    // ================================================================ fixture

    private static (Company Company, Guid EmployeeId) BuildSalaryCompany(int fyStart, decimal monthlyBasic)
    {
        var start = new DateOnly(fyStart, 4, 1);
        var c = CompanyFactory.CreateSeeded("Dated Tax Co", start, start);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = TaxRegime.New;
        emp.Pan = "ABCDE1234F";
        new SalaryStructureService(c).DefineForEmployee(e.Id, start, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(monthlyBasic)),
            new SalaryStructureLine(tds.Id, 1),
        });
        return (c, e.Id);
    }

    private static void PostFullYear(Company c, Guid empId, int fyStart)
    {
        var d = new DateOnly(fyStart, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            var from = new DateOnly(d.Year, d.Month, 1);
            var to = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
            new PayrollVoucherService(c).Post(from, to, new[] { empId });
            d = d.AddMonths(1);
        }
    }
}
