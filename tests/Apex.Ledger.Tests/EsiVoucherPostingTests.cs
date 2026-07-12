using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-5 <b>ESI payroll-voucher posting</b> contract (RQ-9; ER-1/ER-2) — the integrated, balanced ESI
/// legs through the S3 posting path. The headline oracle is the hand-derived golden payslip: Basic ₹20,000 (ESI
/// wages), employee ESI 150 (reduces net) and employer ESI 650 (a balanced employer pair). The voucher balances
/// Dr==Cr to the paisa, the employee ESI reduces net pay, and the continuation / ≤ ₹176 waiver / gating rules post
/// exactly as computed.
/// </summary>
public sealed class EsiVoucherPostingTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("ESI Post Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private const string Ip = "3100123456";

    // ---------------------------------------------------------------- the hand-derived golden ESI voucher

    [Fact]
    public void Golden_esi_voucher_balances_and_the_employee_share_reduces_net()
    {
        var (c, empId, heads) = BuildGolden(basic: 20000m);
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);

        var v = new PayrollVoucherService(c).Post(from, to, new[] { empId });

        // Balanced to the paisa: Σ Dr == Σ Cr == 20,650 (20,000 Basic + 650 employer ESI).
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(20650m), v.TotalDebit);
        Assert.Equal(new Money(20650m), v.TotalCredit);

        // Earning: Dr Basic 20,000.
        AssertLine(v, c.FindLedgerByName("Basic")!.Id, DrCr.Debit, 20000m, PayrollLineCategory.Earning, heads.Basic);
        // Employee ESI is a deduction (Cr 150) that REDUCES NET.
        AssertLine(v, c.FindLedgerByName("Employee ESI")!.Id, DrCr.Credit, 150m, PayrollLineCategory.Deduction, heads.EmployeeEsi);
        // Net = gross 20,000 − employee ESI 150 = 19,850.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 19850m, PayrollLineCategory.NetPayable, null);
        // Employer ESI pair (Dr expense 650 / Cr payable 650), not affecting net.
        AssertLine(v, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 650m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEsi);
        AssertLine(v, c.FindLedgerByName("Employer ESI Payable")!.Id, DrCr.Credit, 650m, PayrollLineCategory.EmployerContributionPayable, heads.EmployerEsi);
    }

    [Fact]
    public void Golden_b_gross_17500_posts_the_independent_round_up_132_and_569()
    {
        var (c, empId, _) = BuildGolden(basic: 17500m);
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);

        var v = new PayrollVoucherService(c).Post(from, to, new[] { empId });

        Assert.True(VoucherValidator.IsBalanced(v));
        AssertLine(v, c.FindLedgerByName("Employee ESI")!.Id, DrCr.Credit, 132m, PayrollLineCategory.Deduction, null);
        AssertLine(v, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 569m, PayrollLineCategory.EmployerContributionExpense, null);
        // Net = 17,500 − 132 = 17,368.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 17368m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- continuation across contribution periods

    [Fact]
    public void Covered_at_cp_start_stays_covered_mid_period_on_full_wages_then_drops_from_the_next_cp()
    {
        // Structure A effective 1-Apr = ₹20,000 (≤ 21,000 at the CP1 start ⇒ covered CP1); a revision to ₹22,000
        // effective 1-Aug pushes the wage over the ceiling mid-CP1.
        var c = Seed();
        var (pay, ph, heads, empId) = BuildEsiEmployee(c);
        var ss = new SalaryStructureService(c);
        ss.DefineForEmployee(empId, new DateOnly(2025, 4, 1), Lines(heads, 20000m));
        ss.DefineForEmployee(empId, new DateOnly(2025, 8, 1), Lines(heads, 22000m));

        // August (still CP1): covered was frozen at the 1-Apr wage, but ESI is charged on the FULL ₹22,000 (no cap).
        var aug = new PayrollVoucherService(c).Post(new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 31), new[] { empId });
        Assert.True(VoucherValidator.IsBalanced(aug));
        AssertLine(aug, c.FindLedgerByName("Employee ESI")!.Id, DrCr.Credit, 165m, PayrollLineCategory.Deduction, heads.EmployeeEsi); // ceil(0.75% × 22,000)
        AssertLine(aug, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 715m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEsi); // ceil(3.25% × 22,000)
        AssertLine(aug, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 21835m, PayrollLineCategory.NetPayable, null); // 22,000 − 165

        // October (CP2 re-evaluates coverage from the 1-Oct wage ₹22,000 > 21,000) ⇒ member drops out: NO ESI.
        var oct = new PayrollVoucherService(c).Post(new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), new[] { empId });
        Assert.True(VoucherValidator.IsBalanced(oct));
        Assert.DoesNotContain(oct.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
        Assert.DoesNotContain(oct.Lines, l => l.Payroll?.PayHeadId == heads.EmployerEsi);
        // Net == gross 22,000 (no ESI deducted).
        AssertLine(oct, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 22000m, PayrollLineCategory.NetPayable, null);
    }

    [Fact]
    public void A_member_above_the_ceiling_at_the_cp_start_is_out_for_the_whole_period()
    {
        // ₹22,000 flat from 1-Apr: above the ceiling at the CP1 start ⇒ NOT covered for the whole of CP1.
        var (c, empId, heads) = BuildGolden(basic: 22000m);
        var v = new PayrollVoucherService(c).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { empId });
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 22000m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- ≤ ₹176/day employee-share waiver

    [Fact]
    public void Low_daily_wage_member_pays_no_employee_share_but_the_employer_still_pays()
    {
        // Basic ₹5,000 over a 30-day month: avg daily ≈ 166.67 ≤ 176 → EE 0; ER ceil(3.25% × 5,000) = 163.
        var (c, empId, heads) = BuildGolden(basic: 5000m);
        var v = new PayrollVoucherService(c).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { empId });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi); // employee share waived (0 ⇒ not posted)
        AssertLine(v, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 163m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEsi);
        // Net == gross 5,000 (no employee deduction).
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 5000m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- F1: person-with-disability ₹25,000 ceiling

    [Fact]
    public void Person_with_disability_reaches_the_25000_ceiling_through_the_payroll_engine()
    {
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);

        // A disabled member with coverage-test wages ₹22,000 (in the ₹21,001–25,000 band) IS covered and charged ESI.
        var (cd, disabledId, headsD) = BuildGolden(basic: 22000m, personWithDisability: true);
        Assert.True(new PayrollComputationService(cd).IsEsiCovered(disabledId, to));
        var vd = new PayrollVoucherService(cd).Post(from, to, new[] { disabledId });
        Assert.True(VoucherValidator.IsBalanced(vd));
        AssertLine(vd, cd.FindLedgerByName("Employee ESI")!.Id, DrCr.Credit, 165m, PayrollLineCategory.Deduction, headsD.EmployeeEsi);           // ceil(0.75% × 22,000)
        AssertLine(vd, cd.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 715m, PayrollLineCategory.EmployerContributionExpense, headsD.EmployerEsi); // ceil(3.25% × 22,000)

        // A NON-disabled member at the same ₹22,000 is above the ordinary ₹21,000 ceiling ⇒ NOT covered, no ESI.
        var (cn, ableId, headsN) = BuildGolden(basic: 22000m, personWithDisability: false);
        Assert.False(new PayrollComputationService(cn).IsEsiCovered(ableId, to));
        var vn = new PayrollVoucherService(cn).Post(from, to, new[] { ableId });
        Assert.DoesNotContain(vn.Lines, l => l.Payroll?.PayHeadId == headsN.EmployeeEsi);
        AssertLine(vn, cn.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 22000m, PayrollLineCategory.NetPayable, null);
    }

    [Fact]
    public void Person_with_disability_ceiling_boundary_25000_covered_25001_not_through_the_engine()
    {
        var to = new DateOnly(2025, 4, 30);

        var (cAt, atId, _) = BuildGolden(basic: 25000m, personWithDisability: true);
        Assert.True(new PayrollComputationService(cAt).IsEsiCovered(atId, to));      // exactly ₹25,000 ⇒ covered

        var (cOver, overId, _) = BuildGolden(basic: 25001m, personWithDisability: true);
        Assert.False(new PayrollComputationService(cOver).IsEsiCovered(overId, to)); // ₹25,001 ⇒ out
    }

    // ---------------------------------------------------------------- F2: mid-CP joiner continuation

    [Fact]
    public void A_mid_cp_joiner_covered_at_first_payroll_stays_covered_when_wages_rise_mid_period()
    {
        // Joins mid-CP1: first structure effective 15-May at ₹19,000 (≤ 21,000 ⇒ covered CP1), raised to ₹22,000
        // effective 1-Jul. Coverage is frozen at the member's FIRST payroll in the CP (the ₹19,000 wage), NOT
        // re-derived at each later month's period end (the bug would re-test July at ₹22,000 and wrongly drop them).
        var c = Seed();
        var (_, _, heads, empId) = BuildEsiEmployee(c);
        c.FindEmployee(empId)!.DateOfJoining = new DateOnly(2025, 5, 15);
        var ss = new SalaryStructureService(c);
        ss.DefineForEmployee(empId, new DateOnly(2025, 5, 15), Lines(heads, 19000m));
        ss.DefineForEmployee(empId, new DateOnly(2025, 7, 1), Lines(heads, 22000m));

        var comp = new PayrollComputationService(c);

        // July (still CP1): frozen coverage keeps the member IN, charged on the ACTUAL ₹22,000 (no cap).
        Assert.True(comp.IsEsiCovered(empId, new DateOnly(2025, 7, 31)));
        var jul = new PayrollVoucherService(c).Post(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 31), new[] { empId });
        Assert.True(VoucherValidator.IsBalanced(jul));
        AssertLine(jul, c.FindLedgerByName("Employee ESI")!.Id, DrCr.Credit, 165m, PayrollLineCategory.Deduction, heads.EmployeeEsi);            // ceil(0.75% × 22,000)
        AssertLine(jul, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 715m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEsi);

        // September (still within CP1): still covered.
        Assert.True(comp.IsEsiCovered(empId, new DateOnly(2025, 9, 30)));

        // October (CP2 re-evaluates at 1-Oct on the ₹22,000 wage > 21,000) ⇒ the member drops out: NO ESI.
        Assert.False(comp.IsEsiCovered(empId, new DateOnly(2025, 10, 31)));
        var oct = new PayrollVoucherService(c).Post(new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), new[] { empId });
        Assert.DoesNotContain(oct.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
    }

    // ---------------------------------------------------------------- F3: ≤ ₹176 exemption excludes overtime

    [Fact]
    public void A_one_off_overtime_month_does_not_strip_a_low_paid_worker_of_the_176_exemption()
    {
        // Base ₹4,000 over a 30-day month (avg ≈ ₹133 ≤ 176 ⇒ EE-exempt) PLUS a one-off ₹4,000 overtime. The average
        // daily wage for the ≤ ₹176 test uses the coverage-test wages (overtime EXCLUDED) ÷ the period's days, so the
        // worker stays exempt; under the OT-inflated bug (8,000 ÷ 30 ≈ 266 > 176) the exemption is wrongly lost. The
        // employer 3.25% is still paid on the FULL ₹8,000 base.
        var c = Seed();
        var (_, _, heads, empId) = BuildEsiEmployee(c);
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);
        new SalaryStructureService(c).DefineForEmployee(empId, from, LinesWithOvertime(heads, basic: 4000m, overtime: 4000m));

        var v = new PayrollVoucherService(c).Post(from, to, new[] { empId });
        Assert.True(VoucherValidator.IsBalanced(v));

        // Employee share waived (0 ⇒ not posted); employer pays ceil(3.25% × 8,000) = 260 on the full base.
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
        AssertLine(v, c.FindLedgerByName("Employer ESI")!.Id, DrCr.Debit, 260m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEsi);
        // Net == gross 8,000 (Basic 4,000 + Overtime 4,000, no employee deduction).
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 8000m, PayrollLineCategory.NetPayable, null);

        // The exemption denominator matches the days the monthly file reports (period days = 30 when no explicit paid
        // days) and the file wages are the full base incl. overtime.
        var ret = EsiMonthlyContribution.Build(c, new[] { empId }, from, to);
        Assert.Equal(30, ret.Rows.Single().NoOfDays);
        Assert.Equal(8000L, ret.Rows.Single().TotalMonthlyWages);
    }

    // ---------------------------------------------------------------- gating (ER-13)

    [Fact]
    public void Esi_is_inert_until_the_establishment_is_enrolled_even_for_an_esi_applicable_member()
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll(); // NB: ESI NOT enrolled
        var ph = new PayHeadService(c);
        var heads = CreateEsiHeads(ph, c);
        var e = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id, esiNumber: Ip);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(e.Id, new DateOnly(2025, 4, 1), Lines(heads, 20000m));

        var v = new PayrollVoucherService(c).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { e.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
        Assert.Null(c.FindLedgerByName("Employee ESI"));
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 20000m, PayrollLineCategory.NetPayable, null);
    }

    [Fact]
    public void A_non_esi_applicable_member_posts_no_esi_legs()
    {
        var c = Seed();
        var (pay, ph, heads, _) = BuildEsiEmployee(c, applicable: false);
        var e = c.Employees.Single();
        new SalaryStructureService(c).DefineForEmployee(e.Id, new DateOnly(2025, 4, 1), Lines(heads, 20000m));

        var v = new PayrollVoucherService(c).Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { e.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.EmployeeEsi);
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 20000m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- harness

    private readonly record struct EsiHeads(Guid Basic, Guid Overtime, Guid EmployeeEsi, Guid EmployerEsi);

    private static EsiHeads CreateEsiHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfEsiWages: true);
        // An overtime earning: part of the ESI contribution base, but EXCLUDED from the coverage test AND from the
        // ≤ ₹176 average-daily-wage exemption numerator (F3). Left out of the golden structures so those are unchanged.
        var ot = ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfEsiWages: true, isOvertime: true);
        var ee = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var er = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);
        return new EsiHeads(basic.Id, ot.Id, ee.Id, er.Id);
    }

    private static SalaryStructureLine[] Lines(EsiHeads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.EmployeeEsi, 1),
        new SalaryStructureLine(h.EmployerEsi, 2),
    };

    private static SalaryStructureLine[] LinesWithOvertime(EsiHeads h, decimal basic, decimal overtime) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.Overtime, 1, new Money(overtime)),
        new SalaryStructureLine(h.EmployeeEsi, 2),
        new SalaryStructureLine(h.EmployerEsi, 3),
    };

    private static (PayrollService Pay, PayHeadService PayHead, EsiHeads Heads, Guid EmployeeId) BuildEsiEmployee(
        Company c, bool applicable = true, bool personWithDisability = false)
    {
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableEsi(); // 0.75% / 3.25% default, establishment enrolled
        var ph = new PayHeadService(c);
        var heads = CreateEsiHeads(ph, c);
        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, esiNumber: Ip);
        if (applicable) pay.SetEmployeeEsiDetails(e.Id, applicable: true, personWithDisability: personWithDisability);
        return (pay, ph, heads, e.Id);
    }

    private (Company Company, Guid EmployeeId, EsiHeads Heads) BuildGolden(decimal basic, bool personWithDisability = false)
    {
        var c = Seed();
        var (_, _, heads, empId) = BuildEsiEmployee(c, personWithDisability: personWithDisability);
        new SalaryStructureService(c).DefineForEmployee(empId, new DateOnly(2025, 4, 1), Lines(heads, basic));
        return (c, empId, heads);
    }

    private static void AssertLine(Voucher v, Guid ledgerId, DrCr side, decimal amount, PayrollLineCategory category, Guid? payHeadId)
    {
        var line = v.Lines.Single(l => l.LedgerId == ledgerId && l.Side == side && l.Amount == new Money(amount));
        Assert.True(line.HasPayroll);
        Assert.Equal(category, line.Payroll!.Category);
        if (payHeadId is not null) Assert.Equal(payHeadId, line.Payroll!.PayHeadId);
    }
}
