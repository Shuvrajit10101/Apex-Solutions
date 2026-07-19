using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-7 <b>Form 24Q + Form 16</b> reconciliation contract (RQ-13) — the quarterly salary-TDS return
/// (Annexure I every quarter + Annexure II in Q4) and the annual Form 16 certificate as pure projections over the
/// posted §192 deduction lines. The gate: Annexure I reconciles to the posted salary-TDS credits; Form 16 Part A
/// sums to Part B tax deducted; and for a fully-trued-up year the tax deducted equals the Annexure II total tax.
/// </summary>
public sealed class Form24QForm16Tests
{
    private const string SalaryTdsPayable = "TDS on Salary";
    private const string ValidPan = "ABCDE1234F";
    private const int FyStart = 2025;

    private static Company Seed()
        => CompanyFactory.CreateSeeded("24Q Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private readonly record struct Heads(Guid Basic, Guid Tds);

    private static (Company Company, Guid EmployeeId, Heads Heads) Build(
        decimal monthlyBasic, TaxRegime regime, bool withPan = true)
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead(SalaryTdsPayable, PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = regime;
        if (withPan) emp.Pan = ValidPan;
        new SalaryStructureService(c).DefineForEmployee(e.Id, new DateOnly(2025, 4, 1), new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(monthlyBasic)),
            new SalaryStructureLine(tds.Id, 1),
        });
        return (c, e.Id, new Heads(basic.Id, tds.Id));
    }

    private static void PostFullYear(Company c, Guid empId)
    {
        var d = new DateOnly(2025, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            var from = new DateOnly(d.Year, d.Month, 1);
            var to = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
            new PayrollVoucherService(c).Post(from, to, new[] { empId });
            d = d.AddMonths(1);
        }
    }

    // ---------------------------------------------------------------- Annexure I reconciles to postings

    [Fact]
    public void Annexure_I_reconciles_to_the_posted_salary_tds_each_quarter()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        PostFullYear(c, empId);

        // NEW ₹15L → ₹8,125/mo → ₹24,375 per quarter, section 92B.
        foreach (var q in new[] { 1, 2, 3, 4 })
        {
            var f24 = Form24Q.Build(c, FyStart, q);
            Assert.Equal(new Money(24_375m), f24.TotalTdsDeducted);
            Assert.Equal(new Money(24_375m), f24.TdsForEmployee(empId));
            Assert.All(f24.Deductees, d => Assert.Equal("92B", d.SectionCode));
            Assert.All(f24.Deductees, d => Assert.Equal(ValidPan, d.Pan));
            Assert.Equal(3, f24.Deductees.Count); // three monthly deductions per quarter
        }
        // Full year across the four quarters = the annual liability ₹97,500.
        var annual = new[] { 1, 2, 3, 4 }.Sum(q => Form24Q.Build(c, FyStart, q).TotalTdsDeducted.Amount);
        Assert.Equal(97_500m, annual);
    }

    // ---------------------------------------------------------------- Annexure II is Q4-only

    [Fact]
    public void Annexure_II_appears_only_in_Q4_and_carries_the_annual_computation()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        PostFullYear(c, empId);

        Assert.Empty(Form24Q.Build(c, FyStart, 1).AnnexureII);
        Assert.Empty(Form24Q.Build(c, FyStart, 3).AnnexureII);

        var q4 = Form24Q.Build(c, FyStart, 4);
        var row = Assert.Single(q4.AnnexureII);
        Assert.Equal(empId, row.EmployeeId);
        Assert.Equal(TaxRegime.New, row.Regime);
        Assert.Equal(new Money(1_500_000m), row.GrossSalary);         // ₹1,25,000 × 12
        Assert.Equal(new Money(75_000m), row.StandardDeduction);
        Assert.Equal(new Money(1_425_000m), row.TaxableIncome);
        Assert.Equal(new Money(93_750m), row.IncomeTax);              // slab − rebate
        Assert.Equal(new Money(3_750m), row.Cess);
        Assert.Equal(new Money(97_500m), row.TotalTax);
        Assert.Equal(new Money(97_500m), row.TaxDeducted);           // fully trued-up ⇒ deducted == computed
    }

    // ---------------------------------------------------------------- old-regime Annexure II carries VI-A deductions

    [Fact]
    public void Old_regime_annexure_II_shows_chapter_via_deductions()
    {
        var decl = new TaxDeclaration { Section80C = new Money(150_000m), Section80D = new Money(25_000m) };
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.Old);
        decl.EmployeeId = empId;
        c.AddTaxDeclaration(decl);
        PostFullYear(c, empId);

        var row = Assert.Single(Form24Q.Build(c, FyStart, 4).AnnexureII);
        Assert.Equal(TaxRegime.Old, row.Regime);
        Assert.Equal(new Money(50_000m), row.StandardDeduction);
        Assert.Equal(new Money(175_000m), row.ChapterViaDeductions);
        Assert.Equal(new Money(1_275_000m), row.TaxableIncome);
        Assert.Equal(new Money(195_000m), row.IncomeTax);
        Assert.Equal(new Money(202_800m), row.TotalTax);
        Assert.Equal(new Money(202_800m), row.TaxDeducted);          // ₹16,900 × 12
    }

    // ---------------------------------------------------------------- Form 16 Part A + Part B

    [Fact]
    public void Form16_partA_sums_to_partB_tax_deducted_and_matches_annexure_II()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        PostFullYear(c, empId);

        var f16 = Form16.Build(c, empId, FyStart);
        Assert.Equal("2025-26", f16.FinancialYearLabel);
        Assert.Equal("2026-27", f16.AssessmentYearLabel);
        Assert.Equal(4, f16.PartA.Count);
        Assert.All(f16.PartA, q => Assert.Equal(new Money(24_375m), q.TdsDeducted)); // ₹8,125 × 3

        // Part A total == Part B tax deducted == the annual liability.
        Assert.Equal(new Money(97_500m), f16.TotalTdsDeducted);
        Assert.NotNull(f16.PartB);
        Assert.Equal(new Money(97_500m), f16.PartB!.TaxDeducted);
        Assert.Equal(new Money(97_500m), f16.PartB.TotalTax);
        Assert.Equal(f16.PartB.TaxDeducted, f16.TotalTdsDeducted); // certificate reconciles to Annexure II
    }

    // ---------------------------------------------------------------- F3: no-PAN §206AA floor reconciles

    [Fact]
    public void No_pan_annexure_II_and_form16_partB_report_the_206AA_floor_that_was_deducted()
    {
        // A no-PAN NEW-regime employee is withheld at the §206AA 20% floor (₹2,85,000/yr, ₹23,750/mo), not the
        // ₹97,500 average-rate. Annexure II / Form 16 Part B must report that floor as Total Tax so the certificate
        // reconciles to the Σ-posted TDS (F3); the un-branched ComputeAnnual would report ₹97,500 and never tally.
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New, withPan: false);
        PostFullYear(c, empId);

        var row = Assert.Single(Form24Q.Build(c, FyStart, 4).AnnexureII);
        Assert.Equal(new Money(285_000m), row.TaxDeducted);   // Σ posted = 12 × 23,750
        Assert.Equal(new Money(285_000m), row.TotalTax);      // reported == deducted (the 20% floor)
        Assert.Equal(row.TaxDeducted, row.TotalTax);

        var f16 = Form16.Build(c, empId, FyStart);
        Assert.NotNull(f16.PartB);
        Assert.Equal(new Money(285_000m), f16.PartB!.TotalTax);
        Assert.Equal(f16.PartB.TaxDeducted, f16.PartB.TotalTax); // certificate reconciles for the no-PAN employee
    }

    // ---------------------------------------------------------------- F6: section code derives from the deductor type

    [Fact]
    public void A_government_deductor_annexure_I_rows_carry_section_92A()
    {
        // With no explicit section code, Form 24Q derives it from the reused Phase-7 deductor category (F6): a
        // Government deductor's Annexure I rows carry 92A, not the hardcoded 92B private default.
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        c.Tds = new TdsConfig { Tan = "MUMA12345B", DeductorType = DeductorType.Government, Enabled = true };
        new PayrollVoucherService(c).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { empId });

        var f24 = Form24Q.Build(c, FyStart, 1); // section code omitted ⇒ derived from the deductor type
        Assert.NotEmpty(f24.Deductees);
        Assert.All(f24.Deductees, d => Assert.Equal("92A", d.SectionCode));

        // A private (default) deductor still derives 92B.
        var (c2, empId2, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        new PayrollVoucherService(c2).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { empId2 });
        Assert.All(Form24Q.Build(c2, FyStart, 1).Deductees, d => Assert.Equal("92B", d.SectionCode));
    }

    // ---------------------------------------------------------------- ER-13: no salary-TDS ⇒ empty return

    [Fact]
    public void A_company_without_salary_tds_yields_an_empty_return()
    {
        var c = Seed();
        var f24 = Form24Q.Build(c, FyStart, 1);
        Assert.True(f24.IsEmpty);
        Assert.Equal(Money.Zero, f24.TotalTdsDeducted);
        Assert.Empty(Form24Q.Build(c, FyStart, 4).AnnexureII);
    }

    // ------------------------------------------- ER-13 (CA S9): the certificate's period vocabulary is FY-gated

    /// <summary>
    /// <b>A FY 2025-26 certificate must reprint exactly as before S9.</b> <see cref="Form16.AssessmentYearLabel"/> is
    /// the 1961-Act value and is kept verbatim precisely so already-issued certificates reproduce; the new
    /// <see cref="Form16.PeriodCaption"/> / <see cref="Form16.PeriodLabel"/> pair must <b>agree with it</b> for a
    /// prior year, i.e. adding the gate changed nothing that a FY 2025-26 employee would see.
    /// </summary>
    [Fact]
    public void ER13_a_prior_year_form_16_still_prints_the_assessment_year_unchanged()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        PostFullYear(c, empId);

        var cert = Form16.Build(c, empId, 2025);
        Assert.Equal("2025-26", cert.FinancialYearLabel);
        Assert.Equal("2026-27", cert.AssessmentYearLabel);   // frozen — a filed document's value
        Assert.Equal("Assessment Year", cert.PeriodCaption);
        Assert.Equal("AY", cert.PeriodCaptionShort);
        Assert.Equal(cert.AssessmentYearLabel, cert.PeriodLabel);   // gate is a no-op before FY 2026-27
    }

    /// <summary>
    /// From FY 2026-27 the 2025 Act governs: the caption becomes "Tax Year" and — because the <b>tax year IS the
    /// financial year</b> — the <b>value</b> moves too, away from the retired assessment-year framing.
    /// <para>This is also where the date trap is pinned in a real certificate: the FY 2025-26 certificate above and
    /// the FY 2026-27 certificate here both display <b>"2026-27"</b>, for <b>different years</b>. Only the caption
    /// distinguishes them, which is why the caption is never a literal in the view.</para>
    /// </summary>
    [Fact]
    public void A_form_16_from_fy_2026_27_switches_to_the_tax_year_vocabulary()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        PostFullYear(c, empId);

        var cert = Form16.Build(c, empId, 2026);
        Assert.Equal("Tax Year", cert.PeriodCaption);
        Assert.Equal("2026-27", cert.PeriodLabel);            // == the financial year, NOT FY + 1
        Assert.Equal("2027-28", cert.AssessmentYearLabel);    // the retired framing, kept but no longer displayed
        Assert.NotEqual(cert.AssessmentYearLabel, cert.PeriodLabel);

        // The collision, stated outright: same numerals, different years, different captions.
        var priorYear = Form16.Build(c, empId, 2025);
        Assert.Equal(priorYear.PeriodLabel, cert.PeriodLabel);
        Assert.NotEqual(priorYear.PeriodCaption, cert.PeriodCaption);
    }
}
