using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-7 <b>§192 salary-TDS payroll-voucher posting</b> contract (RQ-12; ER-1/ER-8) — the average-rate
/// monthly income-tax through the S3 posting path. Salary-TDS is an <b>employee deduction</b> that reduces net and
/// is credited to the salary-TDS payable; there is <b>no employer contribution</b>. The headline oracles are the
/// A14 golden fixtures posted through a real pay run: NEW gross ₹15L (₹1,25,000/mo) → ₹8,125/mo; OLD gross ₹15L
/// with 80C+80D → ₹16,900/mo; the voucher balances Dr==Cr to the paisa and the TDS reduces net pay.
/// </summary>
public sealed class SalaryTdsVoucherPostingTests
{
    private const string SalaryTdsPayable = "TDS on Salary";
    private const string ValidPan = "ABCDE1234F";

    private static Company Seed()
        => CompanyFactory.CreateSeeded("TDS Post Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct Heads(Guid Basic, Guid Tds);

    private static Heads CreateHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var tds = ph.CreatePayHead(SalaryTdsPayable, PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        return new Heads(basic.Id, tds.Id);
    }

    private static SalaryStructureLine[] Lines(Heads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.Tds, 1),
    };

    private static (Company Company, Guid EmployeeId, Heads Heads) Build(
        decimal monthlyBasic, TaxRegime regime = TaxRegime.New, bool enableTds = true, bool withPan = true,
        TaxDeclaration? declaration = null, DateOnly? structureFrom = null)
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        if (enableTds) pay.EnableSalaryTds();
        var ph = new PayHeadService(c);
        var heads = CreateHeads(ph, c);
        var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = regime;
        if (withPan) emp.Pan = ValidPan;
        if (declaration is not null) { declaration.EmployeeId = e.Id; c.AddTaxDeclaration(declaration); }
        new SalaryStructureService(c).DefineForEmployee(
            e.Id, structureFrom ?? new DateOnly(2025, 4, 1), Lines(heads, monthlyBasic));
        return (c, e.Id, heads);
    }

    private static Voucher PostMonth(Company c, Guid empId, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return new PayrollVoucherService(c).Post(from, to, new[] { empId });
    }

    private static Money TdsOn(Voucher v, Company c, Guid tdsHead) =>
        v.Lines.Where(l => l.Payroll?.PayHeadId == tdsHead && l.Side == DrCr.Credit)
               .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    // ---------------------------------------------------------------- GOLDEN 1 (NEW ₹15L → ₹8,125/mo)

    [Fact]
    public void Golden_new_15L_deducts_8125_per_month_reduces_net_and_balances()
    {
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        var v = PostMonth(c, empId, 2025, 4); // April — 12 months remaining

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(125_000m), v.TotalDebit);
        Assert.Equal(new Money(125_000m), v.TotalCredit);

        // §192 TDS ₹8,125 is a deduction (Cr) that REDUCES NET; credited to "TDS on Salary".
        Assert.Equal(new Money(8_125m), TdsOn(v, c, heads.Tds));
        var tdsLine = v.Lines.Single(l => l.LedgerId == c.FindLedgerByName(SalaryTdsPayable)!.Id && l.Side == DrCr.Credit);
        Assert.Equal(PayrollLineCategory.Deduction, tdsLine.Payroll!.Category);
        // Net = gross 1,25,000 − TDS 8,125 = 1,16,875.
        var net = v.Lines.Single(l => l.Payroll?.Category == PayrollLineCategory.NetPayable);
        Assert.Equal(new Money(116_875m), net.Amount);
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.Category == PayrollLineCategory.EmployerContributionExpense);
    }

    // ---------------------------------------------------------------- GOLDEN 2 (NEW ₹12L zero-tax ceiling)

    [Fact]
    public void Golden_new_12L_taxable_deducts_nothing()
    {
        // Monthly gross ₹1,06,250 → annual ₹12,75,000 → taxable ₹12,00,000 → §87A full rebate ⇒ nil.
        var (c, empId, heads) = Build(monthlyBasic: 106_250m, TaxRegime.New);
        var v = PostMonth(c, empId, 2025, 4);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.Zero, TdsOn(v, c, heads.Tds));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.Tds); // zero ⇒ not posted
    }

    // ---------------------------------------------------------------- GOLDEN 4 (OLD ₹15L + 80C/80D → ₹16,900/mo)

    [Fact]
    public void Golden_old_15L_with_80C_80D_deducts_16900_per_month()
    {
        var decl = new TaxDeclaration { Section80C = new Money(150_000m), Section80D = new Money(25_000m) };
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.Old, declaration: decl);
        var v = PostMonth(c, empId, 2025, 4);

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(16_900m), TdsOn(v, c, heads.Tds));
        var net = v.Lines.Single(l => l.Payroll?.Category == PayrollLineCategory.NetPayable);
        Assert.Equal(new Money(108_100m), net.Amount); // 1,25,000 − 16,900
    }

    // ---------------------------------------------------------------- average-rate constancy + FY true-up

    [Fact]
    public void A_full_year_of_posted_tds_totals_the_annual_liability_97500()
    {
        // Post Apr-2025 … Mar-2026; each month's Compute reads the FY-to-date TDS from the already-posted vouchers and
        // spreads the residual over the remaining months. Constant salary ⇒ ₹8,125 every month ⇒ ₹97,500 for the year.
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        decimal total = 0m;
        var d = new DateOnly(2025, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            var v = PostMonth(c, empId, d.Year, d.Month);
            Assert.True(VoucherValidator.IsBalanced(v));
            Assert.Equal(new Money(8_125m), TdsOn(v, c, heads.Tds)); // stable each month
            total += TdsOn(v, c, heads.Tds).Amount;
            d = d.AddMonths(1);
        }
        Assert.Equal(97_500m, total);
    }

    [Fact]
    public void A_mid_year_raise_trues_up_the_remaining_months()
    {
        // Apr–Jun at ₹1,25,000 (₹8,125/mo, ₹24,375 deducted). From July a raise to ₹1,50,000 lifts the estimate; the
        // residual re-spreads over the 9 remaining months. The estimate is paid-to-date + projected (F1), so the
        // annual base is the ACTUAL FY gross ₹17,25,000 (₹3,75,000 paid + ₹13,50,000 projected), NOT ₹1,50,000 × 12.
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        var svc = new SalaryStructureService(c);
        for (var m = 4; m <= 6; m++)
        {
            var v = PostMonth(c, empId, 2025, m);
            Assert.Equal(new Money(8_125m), TdsOn(v, c, heads.Tds));
        }
        // Raise effective 1-Jul-2025: paid-to-date ₹3,75,000 + projected ₹1,50,000 × 9 = ₹17,25,000 → taxable ₹16,50,000.
        svc.DefineForEmployee(empId, new DateOnly(2025, 7, 1), Lines(heads, 150_000m));

        var newAnnual = SalaryIncomeTax.ComputeAnnual(
            SalaryIncomeTax.TaxableIncome(1_725_000m, 0m, TaxRegime.New), TaxRegime.New).AnnualTax;
        // July TDS = (newAnnual − 24,375 already) / 9 remaining, nearest rupee.
        var expectedJuly = SalaryIncomeTax.MonthlyTds(newAnnual, new Money(24_375m), monthsRemaining: 9);
        var july = PostMonth(c, empId, 2025, 7);
        Assert.True(VoucherValidator.IsBalanced(july));
        Assert.Equal(expectedJuly, TdsOn(july, c, heads.Tds));
    }

    // ---------------------------------------------------------------- F1: the year trues up to the ACTUAL FY gross

    [Fact]
    public void A_mid_year_raise_trues_the_year_up_to_the_annexure_II_tax_on_actual_gross()
    {
        // Apr–Jun ₹1,25,000 then Jul–Mar ₹1,50,000 → actual FY gross ₹17,25,000 → taxable ₹16,50,000 → tax ₹1,35,200.
        // With the paid-to-date + projected estimate (F1) the year-end Σ-posted TDS EQUALS the Annexure II total tax on
        // the actual gross; the old ×12 estimate over-projected the raised months and left the year over-withheld.
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        var svc = new SalaryStructureService(c);
        decimal total = 0m;
        var d = new DateOnly(2025, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            if (d.Month == 7) svc.DefineForEmployee(empId, new DateOnly(2025, 7, 1), Lines(heads, 150_000m));
            var v = PostMonth(c, empId, d.Year, d.Month);
            Assert.True(VoucherValidator.IsBalanced(v));
            total += TdsOn(v, c, heads.Tds).Amount;
            d = d.AddMonths(1);
        }

        var annexureII = Assert.Single(Form24Q.Build(c, 2025, 4).AnnexureII);
        Assert.Equal(new Money(1_725_000m), annexureII.GrossSalary);
        Assert.Equal(new Money(1_650_000m), annexureII.TaxableIncome);
        Assert.Equal(new Money(1_35_200m), annexureII.TotalTax);
        Assert.Equal(1_35_200m, total);                       // year-end Σ-posted == tax on actual gross
        Assert.Equal(annexureII.TotalTax.Amount, total);      // …and reconciles to Annexure II by construction
    }

    // ---------------------------------------------------------------- F1: a mid-year joiner is projected over months present

    [Fact]
    public void A_mid_year_joiner_is_not_over_deducted()
    {
        // Joins 1-Jul (first salary structure effective Jul-1): only 9 months of salary exist this FY. The estimate is
        // ₹1,25,000 × 9 = ₹11,25,000 → taxable ₹10,50,000 ≤ ₹12L ⇒ §87A full rebate ⇒ NIL. The old ×12 estimate wrongly
        // annualised to ₹15,00,000 and deducted ₹97,500/9 ≈ ₹10,833 in July (F1: over-deduction of a part-year joiner).
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New, structureFrom: new DateOnly(2025, 7, 1));
        decimal total = 0m;
        for (var m = 7; m <= 12; m++) total += TdsOn(PostMonth(c, empId, 2025, m), c, heads.Tds).Amount;
        for (var m = 1; m <= 3; m++) total += TdsOn(PostMonth(c, empId, 2026, m), c, heads.Tds).Amount;

        Assert.Equal(0m, total); // nine-month salary stays under the ₹12L zero-tax ceiling — nothing withheld all year
        var annexureII = Assert.Single(Form24Q.Build(c, 2025, 4).AnnexureII);
        Assert.Equal(new Money(1_125_000m), annexureII.GrossSalary);
        Assert.Equal(Money.Zero, annexureII.TotalTax);
    }

    // ---------------------------------------------------------------- F1: a late bonus is captured, not ×12-annualised

    [Fact]
    public void A_bonus_month_is_projected_over_remaining_months_not_annualized_twelve_fold()
    {
        // Basic ₹1,00,000/mo with a one-off ₹5,00,000 bonus paid in November (a Nov-only structure that reverts in Dec).
        // In November the estimate is paid-to-date (Apr–Oct actual) + Nov gross × the 5 remaining months — NOT Nov × 12
        // (the old bug, which annualised the bonus twelve-fold). The year is not under-withheld.
        var (c, empId, heads) = Build(monthlyBasic: 100_000m, TaxRegime.New);
        var svc = new SalaryStructureService(c);

        decimal total = 0m; Money novTds = Money.Zero;
        var d = new DateOnly(2025, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            if (d.Month == 11) svc.DefineForEmployee(empId, new DateOnly(2025, 11, 1), Lines(heads, 600_000m)); // +5L bonus
            if (d.Month == 12) svc.DefineForEmployee(empId, new DateOnly(2025, 12, 1), Lines(heads, 100_000m)); // revert
            var v = PostMonth(c, empId, d.Year, d.Month);
            Assert.True(VoucherValidator.IsBalanced(v));
            var t = TdsOn(v, c, heads.Tds);
            if (d.Month == 11) novTds = t;
            total += t.Amount;
            d = d.AddMonths(1);
        }

        // November's TDS is the paid-to-date + projected value (₹7L paid Apr–Oct + ₹6L × 5 remaining), NOT the ×12 bug.
        var novEstAnnual = 700_000m + 600_000m * 5m; // ₹37,00,000
        var novAnnualTax = SalaryIncomeTax.ComputeAnnual(
            SalaryIncomeTax.TaxableIncome(novEstAnnual, 0m, TaxRegime.New), TaxRegime.New).AnnualTax;
        Assert.Equal(SalaryIncomeTax.MonthlyTds(novAnnualTax, Money.Zero, monthsRemaining: 5), novTds);

        // The bonus is captured by year-end — the actual FY gross is fully covered (not under-withheld).
        var annexureII = Assert.Single(Form24Q.Build(c, 2025, 4).AnnexureII);
        Assert.Equal(new Money(1_700_000m), annexureII.GrossSalary); // 11 × 1L + 6L bonus month = 17L
        Assert.True(total >= annexureII.TotalTax.Amount);
    }

    // ---------------------------------------------------------------- F2: a cancelled month is not double-counted

    [Fact]
    public void A_cancelled_and_reposted_month_is_not_double_counted_in_the_true_up()
    {
        // Post April, cancel it, re-post the SAME April (a correction) → two April vouchers, one cancelled. May's
        // FY-to-date true-up must see only the LIVE April (gross ₹1,25,000 / TDS ₹8,125), never the cancelled one too;
        // counting the cancelled voucher (no guard) inflated both paid-to-date and already-deducted and skewed May (F2).
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        var apr = PostMonth(c, empId, 2025, 4);
        Assert.Equal(new Money(8_125m), TdsOn(apr, c, heads.Tds));
        new LedgerService(c).Cancel(apr.Id);
        var aprRedo = PostMonth(c, empId, 2025, 4);
        Assert.Equal(new Money(8_125m), TdsOn(aprRedo, c, heads.Tds));

        var may = PostMonth(c, empId, 2025, 5);
        Assert.True(VoucherValidator.IsBalanced(may));
        Assert.Equal(new Money(8_125m), TdsOn(may, c, heads.Tds)); // constant salary ⇒ still ₹8,125, not a skewed residual
    }

    // ---------------------------------------------------------------- §206AA no-PAN

    [Fact]
    public void No_pan_employee_withholds_the_20_percent_206AA_floor()
    {
        // Taxable ₹14,25,000: average-rate annual ₹97,500 (< 20%); §206AA lifts it to 20% = ₹2,85,000 → ₹23,750/mo.
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New, withPan: false);
        var v = PostMonth(c, empId, 2025, 4);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(23_750m), TdsOn(v, c, heads.Tds)); // 2,85,000 / 12
    }

    // ---------------------------------------------------------------- ER-13 gating

    [Fact]
    public void Salary_tds_is_inert_until_the_establishment_enables_it()
    {
        var (c, empId, heads) = Build(monthlyBasic: 125_000m, TaxRegime.New, enableTds: false);
        var v = PostMonth(c, empId, 2025, 4);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.Zero, TdsOn(v, c, heads.Tds));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.Tds);
        Assert.Null(c.FindLedgerByName(SalaryTdsPayable));
        // Full gross flows to net when no TDS is withheld.
        var net = v.Lines.Single(l => l.Payroll?.Category == PayrollLineCategory.NetPayable);
        Assert.Equal(new Money(125_000m), net.Amount);
    }

    // ---------------------------------------------------------------- computation ↔ posting reconciliation

    [Fact]
    public void The_computed_result_reconciles_to_the_posted_deduction()
    {
        var (c, empId, _) = Build(monthlyBasic: 125_000m, TaxRegime.New);
        var result = new PayrollComputationService(c).Compute(empId, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        Assert.Equal(new Money(8_125m), result.SalaryTdsDeducted);
    }
}
