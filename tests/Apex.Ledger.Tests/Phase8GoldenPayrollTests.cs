using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 <b>exit-gate GOLDEN end-to-end payroll</b> regression (S10) — the headline oracle that proves the WHOLE
/// Phase-8 chain (compute → post → payslip → statutory returns) is internally consistent to the paisa. One company
/// with every statutory enabled (PF · ESI · Professional Tax [Maharashtra] · §192 salary-TDS) runs an April payroll
/// for TWO deliberately-chosen employees — because ESI (wages ≤ ₹21,000) and §192 salary-TDS (needs a higher income)
/// realistically never coexist for one person:
/// <list type="bullet">
///   <item><b>LOW-wage</b> (Basic ₹12,000 + DA ₹6,000 = ₹18,000): exercises PF + ESI + PT.</item>
///   <item><b>HIGHER-wage</b> (Basic ₹80,000 + DA ₹20,000 + HRA ₹25,000 = ₹1,25,000): exercises PF + PT + §192.</item>
/// </list>
/// Every figure is hand-derived from the A14 statutory rules and then reconciled across the posted voucher, the
/// payslip, the payroll register, the payment advice, the PF ECR / ESI monthly-contribution / PT register / Form 24Q
/// returns, and the gratuity provision + bonus register — all reading the same deterministic computation. Nothing is
/// approximate: the run balances Dr==Cr == ₹1,47,835 and each surface agrees to the rupee.
/// </summary>
public sealed class Phase8GoldenPayrollTests
{
    // ---- The wage month (April 2025, a 30-day month; CP1 for ESI). ----
    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    private const string Maharashtra = "27"; // GST state code for the PT slab.

    // ---- Hand-derived per-employee figures (A14 rules). ----------------------------------------------------------
    // LOW-wage employee (Basic 12,000 + DA 6,000 = 18,000):
    //   PF   : wages 18,000 capped at ₹15,000 ⇒ EE EPF 12%×15,000 = 1,800; EPS 1,250; ER EPF = 1,800−1,250 = 550; EDLI 0.5%×15,000 = 75.
    //   ESI  : wages 18,000 ≤ 21,000 ⇒ covered; EE ceil(0.75%×18,000)=135; ER ceil(3.25%×18,000)=585.
    //   PT   : MH man, gross 18,000 > 10,000 ⇒ ₹200 (April, no Feb quirk).
    //   §192 : annual 18,000×12 = 2,16,000 − std 75,000 = 1,41,000 ≪ ₹12L ⇒ §87A full rebate ⇒ NIL (no TDS head).
    //   net  = 18,000 − (1,800 + 135 + 200) = 15,865.
    private const decimal LowBasic = 12_000m, LowDa = 6_000m, LowGross = 18_000m;
    private const decimal LowEmployeeEpf = 1_800m, LowEmployerEpf = 550m, LowPension = 1_250m, LowEdli = 75m;
    private const decimal LowEmployeeEsi = 135m, LowEmployerEsi = 585m, LowPt = 200m, LowNet = 15_865m;

    // HIGHER-wage employee (Basic 80,000 + DA 20,000 + HRA 25,000 = 1,25,000):
    //   PF   : wages Basic+DA = 1,00,000 capped at ₹15,000 ⇒ identical PF legs (1,800 / 550 / 1,250 / 75).
    //   ESI  : NOT applicable (a ₹1,25,000 earner is well above the ceiling) ⇒ no ESI legs.
    //   PT   : MH man, gross 1,25,000 > 10,000 ⇒ ₹200.
    //   §192 : annual 1,25,000×12 = 15,00,000 − std 75,000 = 14,25,000 ⇒ tax 97,500 (new regime) ⇒ 97,500/12 = 8,125/mo.
    //   net  = 1,25,000 − (1,800 + 200 + 8,125) = 1,14,875.
    private const decimal HighBasic = 80_000m, HighDa = 20_000m, HighHra = 25_000m, HighGross = 1_25_000m;
    private const decimal HighEmployeeEpf = 1_800m, HighEmployerEpf = 550m, HighPension = 1_250m, HighEdli = 75m;
    private const decimal HighPt = 200m, HighTds = 8_125m, HighNet = 1_14_875m;

    // Run aggregates.
    private const decimal PfAdminAggregate = 500m;                 // Σ EPF wages 30,000 ⇒ 0.5% = 150, floored once to ₹500.
    private const decimal GrandTotal = 1_47_835m;                  // Σ Dr == Σ Cr (earnings + employer expenses).
    private const decimal GrandGross = 1_43_000m;                  // 18,000 + 1,25,000.
    private const decimal GrandNet = 1_30_740m;                    // 15,865 + 1,14,875.

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct Heads(
        Guid Basic, Guid Da, Guid Hra,
        Guid EmployeeEpf, Guid EmployerEpf, Guid Pension, Guid Edli,
        Guid EmployeeEsi, Guid EmployerEsi, Guid Pt, Guid Tds);

    private sealed record Fixture(Company Company, Guid Low, Guid High, Heads Heads);

    /// <summary>Builds one company with PF + ESI + PT(MH) + §192 + gratuity + bonus all enabled, the shared pay heads,
    /// and the two employees with their dated April salary structures. No attendance is recorded ⇒ full month.</summary>
    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("Phase 8 Golden Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund();            // 12%, cap wages at the ₹15,000 ceiling (default)
        pay.EnableEsi();                      // 0.75% EE / 3.25% ER (default)
        pay.EnableProfessionalTax(stateCode: Maharashtra);
        pay.EnableSalaryTds();                // §192 average-rate monthly income-tax
        pay.EnableGratuity();                 // ₹20,00,000 cap, Basic+DA basis, all active employees
        pay.EnableStatutoryBonus(rateBasisPoints: 833); // §10 minimum 8.33%

        var ph = new PayHeadService(c);
        var ie = IndirectExpenses(c);
        var cl = CurrentLiabilities(c);

        // Earnings. Basic + DA are PF/ESI wages AND the gratuity/bonus "Basic + DA" basis; HRA is neither.
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: ie, incomeTaxComponent: IncomeTaxComponent.BasicSalary,
            useForGratuity: true, partOfPfWages: true, partOfEsiWages: true);
        var da = ph.CreatePayHead("Dearness Allowance", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: ie, useForGratuity: true, partOfPfWages: true, partOfEsiWages: true);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: ie);

        // PF heads.
        var employeeEpf = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var employerEpf = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        var pension = ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, pfComponent: PfStatutoryComponent.EmployerPension);
        var edli = ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);

        // ESI heads.
        var employeeEsi = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var employerEsi = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        // PT + §192 heads.
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, ptComponent: PtStatutoryComponent.ProfessionalTax);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: cl, incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);

        var heads = new Heads(basic.Id, da.Id, hra.Id, employeeEpf.Id, employerEpf.Id, pension.Id, edli.Id,
            employeeEsi.Id, employerEsi.Id, pt.Id, tds.Id);

        var staff = pay.CreateEmployeeGroup("Staff").Id;

        // LOW-wage: PF + ESI + PT.
        var low = pay.CreateEmployee("Ramesh Iyer", staff, employeeNumber: "E-001", pan: "ABCPK1234M",
            uan: "100000000001", esiNumber: "3100123456", dateOfJoining: new DateOnly(2015, 4, 1));
        low.Gender = "Male";
        low.BankName = "State Bank"; low.BankAccountNumber = "1111111111"; low.BankIfsc = "SBIN0001111";
        low.ApplicableTaxRegime = TaxRegime.New;
        pay.SetEmployeePfDetails(low.Id, applicable: true);
        pay.SetEmployeeEsiDetails(low.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(low.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(LowBasic)),
            new SalaryStructureLine(da.Id, 1, new Money(LowDa)),
            new SalaryStructureLine(employeeEpf.Id, 2),
            new SalaryStructureLine(employerEpf.Id, 3),
            new SalaryStructureLine(pension.Id, 4),
            new SalaryStructureLine(edli.Id, 5),
            new SalaryStructureLine(employeeEsi.Id, 6),
            new SalaryStructureLine(employerEsi.Id, 7),
            new SalaryStructureLine(pt.Id, 8),
        });

        // HIGHER-wage: PF + PT + §192 (no ESI).
        var high = pay.CreateEmployee("Priya Menon", staff, employeeNumber: "E-002", pan: "ABCDE1234F",
            uan: "100000000002", dateOfJoining: new DateOnly(2015, 4, 1));
        high.Gender = "Male";
        high.BankName = "HDFC Bank"; high.BankAccountNumber = "2222222222"; high.BankIfsc = "HDFC0002222";
        high.ApplicableTaxRegime = TaxRegime.New;
        pay.SetEmployeePfDetails(high.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(high.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(HighBasic)),
            new SalaryStructureLine(da.Id, 1, new Money(HighDa)),
            new SalaryStructureLine(hra.Id, 2, new Money(HighHra)),
            new SalaryStructureLine(employeeEpf.Id, 3),
            new SalaryStructureLine(employerEpf.Id, 4),
            new SalaryStructureLine(pension.Id, 5),
            new SalaryStructureLine(edli.Id, 6),
            new SalaryStructureLine(pt.Id, 7),
            new SalaryStructureLine(tds.Id, 8),
        });

        return new Fixture(c, low.Id, high.Id, heads);
    }

    private static Voucher PostRun(Fixture f) =>
        new PayrollVoucherService(f.Company).Post(PeriodFrom, PeriodTo, new[] { f.Low, f.High });

    // ============================================================ the balanced posted voucher (the core oracle)

    [Fact]
    public void Golden_end_to_end_payroll_voucher_balances_and_carries_every_statutory_leg()
    {
        var f = Build();
        var v = PostRun(f);
        var c = f.Company;
        var h = f.Heads;

        // Σ Dr == Σ Cr == ₹1,47,835 to the paisa.
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(GrandTotal), v.TotalDebit);
        Assert.Equal(new Money(GrandTotal), v.TotalCredit);

        // ---- LOW-wage employee: earnings, PF, ESI, PT, net. ----
        Assert.Equal(new Money(LowBasic), Posted(v, f.Low, h.Basic, DrCr.Debit));
        Assert.Equal(new Money(LowDa), Posted(v, f.Low, h.Da, DrCr.Debit));
        Assert.Equal(new Money(LowEmployeeEpf), Posted(v, f.Low, h.EmployeeEpf, DrCr.Credit));   // reduces net
        Assert.Equal(new Money(LowEmployerEpf), Posted(v, f.Low, h.EmployerEpf, DrCr.Debit));
        Assert.Equal(new Money(LowPension), Posted(v, f.Low, h.Pension, DrCr.Debit));
        Assert.Equal(new Money(LowEdli), Posted(v, f.Low, h.Edli, DrCr.Debit));
        Assert.Equal(new Money(LowEmployeeEsi), Posted(v, f.Low, h.EmployeeEsi, DrCr.Credit));    // reduces net
        Assert.Equal(new Money(LowEmployerEsi), Posted(v, f.Low, h.EmployerEsi, DrCr.Debit));
        Assert.Equal(new Money(LowPt), Posted(v, f.Low, h.Pt, DrCr.Credit));                      // reduces net
        Assert.Equal(new Money(LowNet), PostedNet(v, f.Low));
        // The low earner has NO §192 TDS (below the ₹12L zero-tax ceiling): the head posts nothing.
        Assert.Equal(Money.Zero, Posted(v, f.Low, h.Tds, DrCr.Credit));
        // Anti-3.67% invariant on the posted legs: EPS 1,250 + ER_EPF 550 == EE_EPF 1,800.
        Assert.Equal(Posted(v, f.Low, h.EmployeeEpf, DrCr.Credit),
                     Posted(v, f.Low, h.Pension, DrCr.Debit) + Posted(v, f.Low, h.EmployerEpf, DrCr.Debit));

        // ---- HIGHER-wage employee: earnings, PF, PT, §192, net; NO ESI. ----
        Assert.Equal(new Money(HighBasic), Posted(v, f.High, h.Basic, DrCr.Debit));
        Assert.Equal(new Money(HighDa), Posted(v, f.High, h.Da, DrCr.Debit));
        Assert.Equal(new Money(HighHra), Posted(v, f.High, h.Hra, DrCr.Debit));
        Assert.Equal(new Money(HighEmployeeEpf), Posted(v, f.High, h.EmployeeEpf, DrCr.Credit));
        Assert.Equal(new Money(HighEmployerEpf), Posted(v, f.High, h.EmployerEpf, DrCr.Debit));
        Assert.Equal(new Money(HighPension), Posted(v, f.High, h.Pension, DrCr.Debit));
        Assert.Equal(new Money(HighEdli), Posted(v, f.High, h.Edli, DrCr.Debit));
        Assert.Equal(new Money(HighPt), Posted(v, f.High, h.Pt, DrCr.Credit));
        Assert.Equal(new Money(HighTds), Posted(v, f.High, h.Tds, DrCr.Credit));                  // §192, reduces net
        Assert.Equal(new Money(HighNet), PostedNet(v, f.High));
        Assert.Equal(Money.Zero, Posted(v, f.High, h.EmployeeEsi, DrCr.Credit));                  // not ESI-covered

        // ---- Establishment PF-admin (A/c 2): one aggregate ₹500 line, floored once for the run. ----
        Assert.Equal(new Money(PfAdminAggregate),
            LedgerDrTotal(v, c, PayrollVoucherService.PfAdminExpenseLedgerName));
        Assert.Single(v.Lines, l => l.LedgerId == c.FindLedgerByName(PayrollVoucherService.PfAdminExpenseLedgerName)!.Id
                                    && l.Side == DrCr.Debit);

        // ---- Run aggregates: gross, net and grand total all reconcile. ----
        Assert.Equal(new Money(GrandGross), new Money(v.Lines
            .Where(l => l.Payroll?.Category == PayrollLineCategory.Earning && l.Side == DrCr.Debit)
            .Sum(l => l.Amount.Amount)));
        Assert.Equal(new Money(GrandNet), new Money(v.Lines
            .Where(l => l.Payroll?.Category == PayrollLineCategory.NetPayable && l.Side == DrCr.Credit)
            .Sum(l => l.Amount.Amount)));
    }

    // ============================================================ payslip reconciliation

    [Fact]
    public void Payslips_reconcile_to_the_posted_net_and_foot_gross_to_deductions_plus_net()
    {
        var f = Build();
        var v = PostRun(f);
        var c = f.Company;

        var low = Report.BuildPayslip(c, f.Low, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(LowGross), low.GrossEarnings);
        Assert.Equal(new Money(LowEmployeeEpf + LowEmployeeEsi + LowPt), low.TotalDeductions);      // 2,135
        Assert.Equal(new Money(LowNet), low.NetPayable);
        Assert.Equal(low.GrossEarnings, low.TotalDeductions + low.NetPayable);                       // foots
        Assert.Equal(low.NetPayable, PostedNet(v, f.Low));                                           // == posted

        var high = Report.BuildPayslip(c, f.High, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(HighGross), high.GrossEarnings);
        Assert.Equal(new Money(HighEmployeeEpf + HighPt + HighTds), high.TotalDeductions);           // 10,125
        Assert.Equal(new Money(HighNet), high.NetPayable);
        Assert.Equal(high.GrossEarnings, high.TotalDeductions + high.NetPayable);
        Assert.Equal(high.NetPayable, PostedNet(v, f.High));

        // Earnings + deductions foot to the aggregates on each slip.
        Assert.Equal(new Money(HighGross), new Money(high.Earnings.Sum(l => l.Amount.Amount)));
        Assert.Equal(new Money(HighEmployeeEpf + HighPt + HighTds), new Money(high.Deductions.Sum(l => l.Amount.Amount)));
    }

    // ============================================================ payroll register + payment advice

    [Fact]
    public void Payroll_register_and_payment_advice_reconcile_to_the_posted_run()
    {
        var f = Build();
        var v = PostRun(f);
        var c = f.Company;
        var ids = new[] { f.Low, f.High };

        var reg = Report.BuildPayrollRegister(c, ids, PeriodFrom, PeriodTo);

        var lowRow = reg.Rows.Single(r => r.EmployeeId == f.Low);
        Assert.Equal(new Money(LowGross), lowRow.GrossEarnings);
        Assert.Equal(new Money(LowEmployeeEpf), lowRow.EmployeePf);
        Assert.Equal(new Money(LowEmployeeEsi), lowRow.EmployeeEsi);
        Assert.Equal(new Money(LowPt), lowRow.ProfessionalTax);
        Assert.Equal(Money.Zero, lowRow.IncomeTax);
        Assert.Equal(new Money(LowNet), lowRow.NetPayable);

        var highRow = reg.Rows.Single(r => r.EmployeeId == f.High);
        Assert.Equal(new Money(HighEmployeeEpf), highRow.EmployeePf);
        Assert.Equal(Money.Zero, highRow.EmployeeEsi);
        Assert.Equal(new Money(HighPt), highRow.ProfessionalTax);
        Assert.Equal(new Money(HighTds), highRow.IncomeTax);                                          // §192 lands in the IncomeTax column
        Assert.Equal(new Money(HighNet), highRow.NetPayable);

        // Every row: statutory + other columns foot to total deductions, gross − deductions == net.
        foreach (var r in reg.Rows)
        {
            Assert.Equal(r.TotalDeductions, new Money(r.ProfessionalTax.Amount + r.EmployeePf.Amount
                + r.EmployeeEsi.Amount + r.IncomeTax.Amount + r.OtherDeductions.Amount));
            Assert.Equal(r.NetPayable, r.GrossEarnings - r.TotalDeductions);
        }

        Assert.Equal(new Money(GrandGross), reg.TotalGrossEarnings);
        Assert.Equal(new Money(GrandNet), reg.TotalNetPayable);

        // Payment advice net matches the posted net-payable Cr lines.
        var advice = Report.BuildPaymentAdvice(c, ids, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(GrandNet), advice.TotalNetPayable);
        Assert.Equal(new Money(LowNet), advice.Rows.Single(r => r.EmployeeName == "Ramesh Iyer").NetPayable);
        Assert.Equal(new Money(HighNet), advice.Rows.Single(r => r.EmployeeName == "Priya Menon").NetPayable);
        var postedNet = v.Lines.Where(l => l.Payroll?.Category == PayrollLineCategory.NetPayable && l.Side == DrCr.Credit)
            .Sum(l => l.Amount.Amount);
        Assert.Equal(new Money(postedNet), advice.TotalNetPayable);
    }

    // ============================================================ statutory returns reconcile to the posting

    [Fact]
    public void Statutory_returns_reconcile_to_the_posted_figures()
    {
        var f = Build();
        var v = PostRun(f);
        var c = f.Company;
        var ids = new[] { f.Low, f.High };

        // ---- PF ECR 2.0 (S4): both PF members; A/c 1 == Σ posted employee + employer EPF. ----
        var ecr = PfEcr.Build(c, ids, PeriodFrom, PeriodTo);
        Assert.Equal(2, ecr.Members.Count);
        var lowMember = ecr.Members.Single(m => m.Uan == "100000000001");
        Assert.Equal((long)LowEmployeeEpf, lowMember.EmployeeShareEpf);
        Assert.Equal((long)LowPension, lowMember.EpsContribution);
        Assert.Equal((long)LowEmployerEpf, lowMember.EmployerShareEpf);
        Assert.Equal(15_000L, lowMember.EpfWages);                                  // wages capped at the ₹15,000 ceiling
        // A/c 1 = Σ (EE + ER) EPF = (1,800+550) × 2 = 4,700; A/c 10 = Σ EPS = 2,500; A/c 21 = Σ EDLI = 150; A/c 2 = 500.
        var postedEpf = Posted(v, f.Low, f.Heads.EmployeeEpf, DrCr.Credit) + Posted(v, f.Low, f.Heads.EmployerEpf, DrCr.Debit)
                      + Posted(v, f.High, f.Heads.EmployeeEpf, DrCr.Credit) + Posted(v, f.High, f.Heads.EmployerEpf, DrCr.Debit);
        Assert.Equal(4_700L, ecr.Totals.Account1);
        Assert.Equal((long)postedEpf.Amount, ecr.Totals.Account1);
        Assert.Equal(2_500L, ecr.Totals.Account10);
        Assert.Equal(150L, ecr.Totals.Account21);
        Assert.Equal((long)PfAdminAggregate, ecr.Totals.Account2);

        // ---- ESI monthly-contribution file (S5): only the covered LOW earner appears, wages ₹18,000, 30 days. ----
        var esi = EsiMonthlyContribution.Build(c, ids, PeriodFrom, PeriodTo);
        var esiRow = Assert.Single(esi.Rows);
        Assert.Equal("3100123456", esiRow.IpNumber);
        Assert.Equal(30, esiRow.NoOfDays);
        Assert.Equal((long)LowGross, esiRow.TotalMonthlyWages);
        // The file wage drives the posted employee ESI: ceil(0.75% × 18,000) = 135.
        Assert.Equal(new Money(LowEmployeeEsi), Posted(v, f.Low, f.Heads.EmployeeEsi, DrCr.Credit));

        // ---- Professional-Tax register (S6): both members ₹200, MH; TotalPt == Σ posted PT. ----
        var pt = ProfessionalTaxRegister.Build(c, ids, PeriodFrom, PeriodTo);
        Assert.Equal(Maharashtra, pt.StateCode);
        Assert.Equal((long)(LowPt + HighPt), pt.TotalPt);                           // 400
        var postedPt = Posted(v, f.Low, f.Heads.Pt, DrCr.Credit) + Posted(v, f.High, f.Heads.Pt, DrCr.Credit);
        Assert.Equal((long)postedPt.Amount, pt.TotalPt);

        // ---- Form 24Q Annexure I (S7), Q1 (April ∈ Apr–Jun): exactly the HIGH earner's §192 ₹8,125. ----
        var form24Q = Form24Q.Build(c, 2025, 1);
        Assert.Single(form24Q.Deductees);                                           // only the §192 employee
        Assert.Equal(new Money(HighTds), form24Q.TotalTdsDeducted);
        Assert.Equal(new Money(HighTds), form24Q.TdsForEmployee(f.High));
        Assert.Equal(Posted(v, f.High, f.Heads.Tds, DrCr.Credit), form24Q.TdsForEmployee(f.High));
    }

    // ============================================================ gratuity provision + bonus register (S9)

    [Fact]
    public void Gratuity_provision_and_bonus_register_reconcile_over_both_employees()
    {
        var f = Build();
        PostRun(f); // the monthly run is independent of the gratuity/bonus projections
        var c = f.Company;
        var ids = new[] { f.Low, f.High };
        var asOn = new DateOnly(2025, 4, 30);

        // ---- Gratuity register: Basic + DA × 15 × 10 completed years ÷ 26 (both joined 2015-04-01). ----
        //   LOW  : 18,000  × 15 × 10 / 26 = 1,03,846 (half-up).
        //   HIGH : 1,00,000 × 15 × 10 / 26 = 5,76,923.
        const long lowAccrued = 1_03_846L, highAccrued = 5_76_923L, totalLiability = lowAccrued + highAccrued;
        var reg = GratuityProvisionRegister.Build(c, ids, asOn);
        Assert.Equal(totalLiability, reg.TotalLiability);                           // 6,80,769
        Assert.Equal(2_000_000L, reg.CapAmount);
        Assert.Equal(lowAccrued, reg.Rows.Single(r => r.EmployeeName == "Ramesh Iyer").AccruedGratuity);
        Assert.Equal(highAccrued, reg.Rows.Single(r => r.EmployeeName == "Priya Menon").AccruedGratuity);

        // The posted provision (first ⇒ full liability) equals the register total to the paisa.
        var provision = new PayrollVoucherService(c).PostGratuityProvision(asOn, ids);
        Assert.True(VoucherValidator.IsBalanced(provision));
        Assert.Equal(new Money(totalLiability), provision.TotalCredit);
        Assert.Equal(totalLiability, LedgerDrTotal(provision, c, PayrollVoucherService.GratuityExpenseLedgerName).Amount);

        // ---- Bonus register: only the ≤ ₹21,000 LOW earner (capped ₹7,000 × 12 × 8.33% = ₹6,997); HIGH excluded. ----
        var bonus = BonusRegister.Build(c, ids, PeriodFrom);
        var bonusRow = Assert.Single(bonus.Rows);
        Assert.Equal("Ramesh Iyer", bonusRow.EmployeeName);
        Assert.True(bonusRow.Eligible);
        Assert.Equal(7_000L, bonusRow.CappedBase);
        Assert.Equal(8.33m, bonusRow.RatePercent);
        Assert.Equal(6_997L, bonusRow.AnnualBonus);
        Assert.Equal(6_997L, bonus.TotalBonus);
        Assert.DoesNotContain(bonus.Rows, r => r.EmployeeName == "Priya Menon"); // > ₹21,000 ⇒ excluded
    }

    // ============================================================ harness

    /// <summary>Σ the posted amount on one employee's pay-head line for the given side (0 ⇒ the head posted nothing).</summary>
    private static Money Posted(Voucher v, Guid employeeId, Guid payHeadId, DrCr side) =>
        v.Lines.Where(l => l.Payroll is { } p && p.EmployeeId == employeeId && p.PayHeadId == payHeadId && l.Side == side)
               .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    /// <summary>The posted net-payable credit for one employee.</summary>
    private static Money PostedNet(Voucher v, Guid employeeId) =>
        v.Lines.Where(l => l.Payroll is { } p && p.EmployeeId == employeeId
                        && p.Category == PayrollLineCategory.NetPayable && l.Side == DrCr.Credit)
               .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    /// <summary>Σ the debit legs on a named ledger in a voucher.</summary>
    private static Money LedgerDrTotal(Voucher v, Company c, string ledgerName) =>
        v.Lines.Where(l => l.LedgerId == c.FindLedgerByName(ledgerName)!.Id && l.Side == DrCr.Debit)
               .Aggregate(Money.Zero, (a, l) => a + l.Amount);
}
