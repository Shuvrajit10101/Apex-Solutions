using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-4 <b>PF payroll-voucher posting</b> contract (RQ-9; ER-1/ER-2) — the integrated, balanced PF
/// legs through the S3 posting path. The headline oracle is the hand-derived golden payslip: Basic ₹15,000 (PF
/// wages), employee EPF 1,800 (reduces net), employer EPF 550 / EPS 1,250 / EDLI 75 (balanced employer pairs) and
/// the establishment EPF-admin ₹500 (aggregate, floored once). The voucher balances Dr==Cr to the paisa, the
/// employee EPF reduces net pay, and the anti-3.67% invariant EPS + ER_EPF == EE_EPF holds on the posted legs.
/// </summary>
public sealed class PfVoucherPostingTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("PF Post Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    // ---------------------------------------------------------------- the hand-derived golden PF voucher

    [Fact]
    public void Golden_pf_voucher_balances_and_carries_the_exact_statutory_legs()
    {
        var (c, empId, heads) = BuildGolden(basic: 15000m, higherWageOptIn: false);

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { empId });

        // Balanced to the paisa: Σ Dr == Σ Cr == 17,375 (15,000 + 550 + 1,250 + 75 + 500).
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(17375m), v.TotalDebit);
        Assert.Equal(new Money(17375m), v.TotalCredit);

        // Earning: Dr Basic 15,000.
        AssertLine(v, c.FindLedgerByName("Basic")!.Id, DrCr.Debit, 15000m, PayrollLineCategory.Earning, heads.Basic);
        // Employee EPF is a deduction (Cr) that REDUCES NET.
        AssertLine(v, c.FindLedgerByName("Employee EPF")!.Id, DrCr.Credit, 1800m, PayrollLineCategory.Deduction, heads.EmployeeEpf);
        // Net = gross 15,000 − employee EPF 1,800 = 13,200.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 13200m, PayrollLineCategory.NetPayable, null);

        // Employer pairs (Dr expense / Cr payable), each balanced, none affecting net.
        AssertLine(v, c.FindLedgerByName("Employer EPF")!.Id, DrCr.Debit, 550m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEpf);
        AssertLine(v, c.FindLedgerByName("Employer EPF Payable")!.Id, DrCr.Credit, 550m, PayrollLineCategory.EmployerContributionPayable, heads.EmployerEpf);
        AssertLine(v, c.FindLedgerByName("Employer Pension")!.Id, DrCr.Debit, 1250m, PayrollLineCategory.EmployerContributionExpense, heads.Pension);
        AssertLine(v, c.FindLedgerByName("Employer Pension Payable")!.Id, DrCr.Credit, 1250m, PayrollLineCategory.EmployerContributionPayable, heads.Pension);
        AssertLine(v, c.FindLedgerByName("EDLI")!.Id, DrCr.Debit, 75m, PayrollLineCategory.EmployerContributionExpense, heads.Edli);
        AssertLine(v, c.FindLedgerByName("EDLI Payable")!.Id, DrCr.Credit, 75m, PayrollLineCategory.EmployerContributionPayable, heads.Edli);

        // Establishment EPF-admin (A/c 2): a single ₹15,000 member's 0.5% is ₹75, floored to ₹500 at the aggregate.
        var adminExp = c.FindLedgerByName(PayrollVoucherService.PfAdminExpenseLedgerName)!;
        var adminPay = c.FindLedgerByName(PayrollVoucherService.PfAdminPayableLedgerName)!;
        Assert.Equal(new Money(500m), v.Lines.Single(l => l.LedgerId == adminExp.Id && l.Side == DrCr.Debit).Amount);
        Assert.Equal(new Money(500m), v.Lines.Single(l => l.LedgerId == adminPay.Id && l.Side == DrCr.Credit).Amount);

        // The anti-3.67% invariant on the POSTED legs: EPS 1,250 + ER_EPF 550 == EE_EPF 1,800.
        var eps = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.Pension && l.Side == DrCr.Debit).Amount;
        var erEpf = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.EmployerEpf && l.Side == DrCr.Debit).Amount;
        var eeEpf = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.EmployeeEpf).Amount;
        Assert.Equal(eeEpf, eps + erEpf);
    }

    // ---------------------------------------------------------------- higher-wage opt-in

    [Fact]
    public void Higher_wage_optin_member_computes_on_full_wages_for_epf_but_capped_eps_edli()
    {
        var (c, empId, heads) = BuildGolden(basic: 20000m, higherWageOptIn: true);

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { empId });

        Assert.True(VoucherValidator.IsBalanced(v));
        AssertLine(v, c.FindLedgerByName("Employee EPF")!.Id, DrCr.Credit, 2400m, PayrollLineCategory.Deduction, heads.EmployeeEpf);
        AssertLine(v, c.FindLedgerByName("Employer Pension")!.Id, DrCr.Debit, 1250m, PayrollLineCategory.EmployerContributionExpense, heads.Pension);
        AssertLine(v, c.FindLedgerByName("Employer EPF")!.Id, DrCr.Debit, 1150m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEpf);
        AssertLine(v, c.FindLedgerByName("EDLI")!.Id, DrCr.Debit, 75m, PayrollLineCategory.EmployerContributionExpense, heads.Edli);
        // Net = 20,000 − 2,400 = 17,600.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 17600m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- finding-2/3: company-level uncap default

    [Fact]
    public void Company_default_uncap_computes_on_full_wages_even_without_a_per_employee_optin()
    {
        // Establishment default = do NOT cap (CapWagesAtCeiling=false); member did NOT opt in per-employee. The
        // company flag must drive the cap: EPF on the full ₹25,000. (When the flag was inert this posted 1,800.)
        var (c, empId, heads) = BuildGolden(basic: 25000m, higherWageOptIn: false, capWagesAtCeiling: false);

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { empId });

        Assert.True(VoucherValidator.IsBalanced(v)); // Dr == Cr to the paisa
        AssertLine(v, c.FindLedgerByName("Employee EPF")!.Id, DrCr.Credit, 3000m, PayrollLineCategory.Deduction, heads.EmployeeEpf);                  // 12% × 25,000
        AssertLine(v, c.FindLedgerByName("Employer Pension")!.Id, DrCr.Debit, 1250m, PayrollLineCategory.EmployerContributionExpense, heads.Pension); // EPS capped
        AssertLine(v, c.FindLedgerByName("Employer EPF")!.Id, DrCr.Debit, 1750m, PayrollLineCategory.EmployerContributionExpense, heads.EmployerEpf); // 3,000 − 1,250
        AssertLine(v, c.FindLedgerByName("EDLI")!.Id, DrCr.Debit, 75m, PayrollLineCategory.EmployerContributionExpense, heads.Edli);                  // EDLI capped
        // Net = 25,000 − 3,000 = 22,000.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 22000m, PayrollLineCategory.NetPayable, null);

        // anti-3.67% invariant on the POSTED legs: EPS 1,250 + ER_EPF 1,750 == EE_EPF 3,000.
        var eps = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.Pension && l.Side == DrCr.Debit).Amount;
        var erEpf = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.EmployerEpf && l.Side == DrCr.Debit).Amount;
        var eeEpf = v.Lines.Single(l => l.Payroll?.PayHeadId == heads.EmployeeEpf).Amount;
        Assert.Equal(eeEpf, eps + erEpf);
    }

    // ---------------------------------------------------------------- finding-6: PF inert until the estab is enrolled

    [Fact]
    public void Pf_is_inert_until_the_establishment_is_enrolled_even_for_a_pf_applicable_member()
    {
        // Payroll on, member PF-applicable + PF pay heads present, but the establishment is NOT enrolled for PF
        // (no EnableProvidentFund ⇒ PfConfig null). ER-13: no PF is computed before enrolment. Member PF must be as
        // inert as the establishment admin (which already skips when PfConfig is null) — otherwise A/c 2 is silently
        // understated while members contribute.
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll(); // NB: PF NOT enrolled
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfPfWages: true);
        var pf = CreatePfHeads(ph, c);
        var e = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id, uan: "100000000001");
        pay.SetEmployeePfDetails(e.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(e.Id, PeriodFrom, StructureLines(basic.Id, pf, 15000m));

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { e.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        // No member PF legs and no admin: net == gross 15,000 (symmetric with the admin gate).
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 15000m, PayrollLineCategory.NetPayable, null);
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == pf.EmployeeEpf);
        Assert.Null(c.FindLedgerByName("Employee EPF"));
        Assert.Null(c.FindLedgerByName(PayrollVoucherService.PfAdminExpenseLedgerName));
    }

    // ---------------------------------------------------------------- establishment admin aggregate (floored ONCE)

    [Fact]
    public void Multi_employee_admin_charge_sums_then_floors_once_not_per_member()
    {
        var c = Seed();
        var (pay, ph) = MinimalPf(c);
        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfPfWages: true);
        var epfHeads = CreatePfHeads(ph, c);

        var ss = new SalaryStructureService(c);
        var ids = new List<Guid>();
        for (int i = 0; i < 2; i++)
        {
            var e = pay.CreateEmployee($"E{i}", grp, uan: $"10000000000{i}");
            pay.SetEmployeePfDetails(e.Id, applicable: true);
            ss.DefineForEmployee(e.Id, PeriodFrom, StructureLines(basic.Id, epfHeads, 15000m));
            ids.Add(e.Id);
        }

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, ids);

        Assert.True(VoucherValidator.IsBalanced(v));
        // Σ EPF wages = 30,000; 0.5% = 150; floored ONCE to ₹500 (a per-member floor would be 2 × 500 = 1,000).
        var adminExp = c.FindLedgerByName(PayrollVoucherService.PfAdminExpenseLedgerName)!;
        Assert.Equal(new Money(500m), v.Lines.Single(l => l.LedgerId == adminExp.Id && l.Side == DrCr.Debit).Amount);
        // Exactly one admin expense line for the whole run (once per challan).
        Assert.Single(v.Lines, l => l.LedgerId == adminExp.Id && l.Side == DrCr.Debit);
    }

    // ---------------------------------------------------------------- gating (ER-13)

    [Fact]
    public void A_non_pf_applicable_member_posts_no_pf_legs()
    {
        var c = Seed();
        var (pay, ph) = MinimalPf(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfPfWages: true);
        var epfHeads = CreatePfHeads(ph, c);
        var e = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id, uan: "100000000009");
        // NOT PF-applicable (default false).
        new SalaryStructureService(c).DefineForEmployee(e.Id, PeriodFrom, StructureLines(basic.Id, epfHeads, 15000m));

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { e.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        // No employee-EPF deduction: net == gross 15,000.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 15000m, PayrollLineCategory.NetPayable, null);
        Assert.Null(c.FindLedgerByName("Employee EPF"));
        // No contributory member ⇒ the establishment admin still posts its ₹75 minimum (PF is enrolled).
        var adminExp = c.FindLedgerByName(PayrollVoucherService.PfAdminExpenseLedgerName)!;
        Assert.Equal(new Money(75m), v.Lines.Single(l => l.LedgerId == adminExp.Id && l.Side == DrCr.Debit).Amount);
    }

    // ---------------------------------------------------------------- harness

    private readonly record struct PfHeads(Guid Basic, Guid EmployeeEpf, Guid EmployerEpf, Guid Pension, Guid Edli);

    private (Guid EmployeeEpf, Guid EmployerEpf, Guid Pension, Guid Edli) CreatePfHeads(PayHeadService ph, Company c)
    {
        var ee = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var er = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        var eps = ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployerPension);
        var edli = ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);
        return (ee.Id, er.Id, eps.Id, edli.Id);
    }

    private static SalaryStructureLine[] StructureLines(Guid basic, (Guid EmployeeEpf, Guid EmployerEpf, Guid Pension, Guid Edli) pf, decimal basicAmount)
        => new[]
        {
            new SalaryStructureLine(basic, 0, new Money(basicAmount)),
            new SalaryStructureLine(pf.EmployeeEpf, 1),
            new SalaryStructureLine(pf.EmployerEpf, 2),
            new SalaryStructureLine(pf.Pension, 3),
            new SalaryStructureLine(pf.Edli, 4),
        };

    private (Company Company, Guid EmployeeId, PfHeads Heads) BuildGolden(
        decimal basic, bool higherWageOptIn, bool capWagesAtCeiling = true)
    {
        var c = Seed();
        var (pay, ph) = MinimalPf(c, capWagesAtCeiling);
        var basicHead = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfPfWages: true);
        var pf = CreatePfHeads(ph, c);
        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, uan: "100000000001");
        pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: higherWageOptIn);
        new SalaryStructureService(c).DefineForEmployee(e.Id, PeriodFrom, StructureLines(basicHead.Id, pf, basic));
        return (c, e.Id, new PfHeads(basicHead.Id, pf.EmployeeEpf, pf.EmployerEpf, pf.Pension, pf.Edli));
    }

    private static (PayrollService Pay, PayHeadService PayHead) MinimalPf(Company c, bool capWagesAtCeiling = true)
    {
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(capWagesAtCeiling: capWagesAtCeiling); // 12% default, establishment enrolled
        return (pay, new PayHeadService(c));
    }

    private static void AssertLine(Voucher v, Guid ledgerId, DrCr side, decimal amount, PayrollLineCategory category, Guid? payHeadId)
    {
        var line = v.Lines.Single(l => l.LedgerId == ledgerId && l.Side == side && l.Amount == new Money(amount));
        Assert.True(line.HasPayroll);
        Assert.Equal(category, line.Payroll!.Category);
        Assert.Equal(payHeadId, line.Payroll!.PayHeadId);
    }
}
