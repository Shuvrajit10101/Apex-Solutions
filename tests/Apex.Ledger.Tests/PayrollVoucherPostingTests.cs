using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-3 <b>Payroll voucher posting</b> contract (RQ-7; ER-1/ER-2) — the integrated balanced
/// accounting post + auto-created payroll ledgers. The headline oracle is the hand-derived golden voucher:
/// <c>Dr Basic 30,000 / Dr HRA 12,000 == Cr Advance Recovery 2,000 / Cr Salary Payable 40,000</c>, balanced to
/// the paisa, with the auto-created ledgers under the right groups and every line carrying its
/// <see cref="PayrollLineDetail"/>. Also covers auto-ledger idempotency + non-destructive reuse, multi-employee
/// runs, the balanced employer-contribution pair, and the negative-net guard.
/// </summary>
public sealed class PayrollVoucherPostingTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Payroll Post Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    // ---------------------------------------------------------------- the hand-derived golden voucher

    [Fact]
    public void Golden_payroll_voucher_balances_dr_equals_cr_to_the_paisa()
    {
        var (c, emp, heads) = BuildGolden();

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp });

        // Σ Dr == Σ Cr == ₹42,000 to the paisa.
        Assert.Equal(new Money(42000m), v.TotalDebit);
        Assert.Equal(new Money(42000m), v.TotalCredit);
        Assert.True(VoucherValidator.IsBalanced(v));

        // The exact four lines.
        var basicLed = c.FindLedgerByName("Basic")!;
        var hraLed = c.FindLedgerByName("HRA")!;
        var advLed = c.FindLedgerByName("Advance Recovery")!;
        var payableLed = c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;

        AssertLine(v, basicLed.Id, DrCr.Debit, 30000m, PayrollLineCategory.Earning, heads.Basic);
        AssertLine(v, hraLed.Id, DrCr.Debit, 12000m, PayrollLineCategory.Earning, heads.Hra);
        AssertLine(v, advLed.Id, DrCr.Credit, 2000m, PayrollLineCategory.Deduction, heads.Advance);
        AssertLine(v, payableLed.Id, DrCr.Credit, 40000m, PayrollLineCategory.NetPayable, null);

        // Every line self-describes its employee + amount == line amount (the payslip reads it back).
        foreach (var line in v.Lines)
        {
            Assert.True(line.HasPayroll);
            Assert.Equal(emp, line.Payroll!.EmployeeId);
            Assert.Equal(line.Amount, line.Payroll!.Amount);
        }

        // The posted voucher keeps the Trial Balance balanced (real books).
        Assert.Equal(
            LedgerBalances.SignedClosing(c, basicLed, PeriodTo) + LedgerBalances.SignedClosing(c, hraLed, PeriodTo),
            -(LedgerBalances.SignedClosing(c, advLed, PeriodTo) + LedgerBalances.SignedClosing(c, payableLed, PeriodTo)));
    }

    [Fact]
    public void Auto_created_ledgers_sit_under_the_right_groups_and_populate_the_pay_head_link()
    {
        var (c, emp, heads) = BuildGolden();
        new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp });

        var basicLed = c.FindLedgerByName("Basic")!;
        var hraLed = c.FindLedgerByName("HRA")!;
        var advLed = c.FindLedgerByName("Advance Recovery")!;
        var payableLed = c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;

        // Earnings under Indirect Expenses (Expense nature); deduction + net under Current Liabilities.
        Assert.Equal(IndirectExpenses(c), basicLed.GroupId);
        Assert.Equal(IndirectExpenses(c), hraLed.GroupId);
        Assert.Equal(CurrentLiabilities(c), advLed.GroupId);
        Assert.Equal(CurrentLiabilities(c), payableLed.GroupId);

        // Opening sides: expense Dr, liabilities Cr.
        Assert.True(basicLed.OpeningIsDebit);
        Assert.False(advLed.OpeningIsDebit);
        Assert.False(payableLed.OpeningIsDebit);

        // The pay head's LedgerId is populated (idempotent link).
        Assert.Equal(basicLed.Id, c.FindPayHead(heads.Basic)!.LedgerId);
        Assert.Equal(advLed.Id, c.FindPayHead(heads.Advance)!.LedgerId);
    }

    [Fact]
    public void Posting_twice_reuses_the_same_ledgers_no_duplicates()
    {
        var (c, emp, _) = BuildGolden();
        var svc = new PayrollVoucherService(c);
        svc.Post(PeriodFrom, PeriodTo, new[] { emp });
        var ledgerCountAfterFirst = c.Ledgers.Count;

        svc.Post(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31), new[] { emp });
        Assert.Equal(ledgerCountAfterFirst, c.Ledgers.Count); // no new ledgers on the second run
        Assert.Single(c.Ledgers, l => l.Name == "Basic");
        Assert.Single(c.Ledgers, l => l.Name == PayrollVoucherService.SalaryPayableLedgerName);
    }

    [Fact]
    public void A_user_pre_created_ledger_is_reused_not_relocated()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        // User pre-creates the "Basic" ledger under a DIFFERENT group (Direct Expenses).
        var directExp = c.FindGroupByName("Direct Expenses")!;
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Basic", directExp.Id, Money.Zero, openingIsDebit: true));

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(basic.Id, 0, new Money(30000m)) });

        new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id });

        var basicLed = c.FindLedgerByName("Basic")!;
        Assert.Equal(directExp.Id, basicLed.GroupId); // NOT relocated to Indirect Expenses
        Assert.Single(c.Ledgers, l => l.Name == "Basic");
        Assert.Equal(basicLed.Id, c.FindPayHead(basic.Id)!.LedgerId);
    }

    // ---------------------------------------------------------------- employer contributions (balanced pair)

    [Fact]
    public void Employer_contribution_posts_a_balanced_dr_cr_pair()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var employerPf = ph.CreatePayHead("Employer PF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) })); // 12% of Basic
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(employerPf.Id, 1),
        });

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id });

        Assert.True(VoucherValidator.IsBalanced(v));

        // Employer expense (Dr) under Indirect Expenses; employer payable (Cr) under Current Liabilities.
        var expLed = c.FindLedgerByName("Employer PF")!;
        var payableLed = c.FindLedgerByName("Employer PF Payable")!;
        Assert.Equal(IndirectExpenses(c), expLed.GroupId);
        Assert.Equal(CurrentLiabilities(c), payableLed.GroupId);
        AssertLine(v, expLed.Id, DrCr.Debit, 3600m, PayrollLineCategory.EmployerContributionExpense, employerPf.Id);
        AssertLine(v, payableLed.Id, DrCr.Credit, 3600m, PayrollLineCategory.EmployerContributionPayable, employerPf.Id);

        // Employee net (Basic 30,000, no deductions) still Cr Salary Payable 30,000. Employer pair is separate.
        var netLed = c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;
        AssertLine(v, netLed.Id, DrCr.Credit, 30000m, PayrollLineCategory.NetPayable, null);

        // The pay head links both sides.
        var head = c.FindPayHead(employerPf.Id)!;
        Assert.Equal(expLed.Id, head.EmployerExpenseLedgerId);
        Assert.Equal(payableLed.Id, head.LedgerId);
    }

    // ---------------------------------------------------------------- multi-employee run

    [Fact]
    public void Multi_employee_run_posts_one_balanced_voucher_per_employee_detail()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var e1 = pay.CreateEmployee("E1", grp);
        var e2 = pay.CreateEmployee("E2", grp);
        var ss = new SalaryStructureService(c);
        ss.DefineForEmployee(e1.Id, PeriodFrom, new[] { new SalaryStructureLine(basic.Id, 0, new Money(30000m)) });
        ss.DefineForEmployee(e2.Id, PeriodFrom, new[] { new SalaryStructureLine(basic.Id, 0, new Money(45000m)) });

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { e1.Id, e2.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(75000m), v.TotalDebit);   // 30,000 + 45,000 expense
        Assert.Equal(new Money(75000m), v.TotalCredit);  // 30,000 + 45,000 net

        var netLed = c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;
        var e1Net = v.Lines.Single(l => l.LedgerId == netLed.Id && l.Payroll!.EmployeeId == e1.Id);
        var e2Net = v.Lines.Single(l => l.LedgerId == netLed.Id && l.Payroll!.EmployeeId == e2.Id);
        Assert.Equal(new Money(30000m), e1Net.Amount);
        Assert.Equal(new Money(45000m), e2Net.Amount);
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void Negative_net_pay_is_rejected()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var advance = ph.CreatePayHead("Advance", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(10000m)),
            new SalaryStructureLine(advance.Id, 1, new Money(12000m)), // deduction > earning
        });

        Assert.Throws<InvalidOperationException>(() => new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id }));
        Assert.DoesNotContain(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll); // nothing persisted
    }

    [Fact]
    public void Posting_without_payroll_enabled_throws()
    {
        var c = Seed();
        var pay = new PayrollService(c); // not enabled
        Assert.Throws<InvalidOperationException>(() =>
            new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { Guid.NewGuid() }));
    }

    // ---------------------------------------------------------------- affect-net-salary flag posting (F1)

    [Fact]
    public void Non_affecting_earning_and_deduction_are_excluded_but_employer_pair_still_posts()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var perk = ph.CreatePayHead("Notional Perk", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), affectsNetSalary: false);
        var advance = ph.CreatePayHead("Advance", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));
        var notionalDed = ph.CreatePayHead("Notional Deduction", PayHeadType.Deductions, PayHeadCalculationType.FlatRate,
            underGroupId: CurrentLiabilities(c), affectsNetSalary: false);
        var employerPf = ph.CreatePayHead("Employer PF", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsComputedValue,
            underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) })); // 12% of Basic
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(perk.Id, 1, new Money(5000m)),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
            new SalaryStructureLine(notionalDed.Id, 3, new Money(1000m)),
            new SalaryStructureLine(employerPf.Id, 4),
        });

        var v = new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id });

        Assert.True(VoucherValidator.IsBalanced(v));
        // The non-affecting earning + deduction post NO leg — and no ledger is even auto-created for them.
        Assert.DoesNotContain(v.Lines, l => l.Payroll!.PayHeadId == perk.Id);
        Assert.DoesNotContain(v.Lines, l => l.Payroll!.PayHeadId == notionalDed.Id);
        Assert.Null(c.FindLedgerByName("Notional Perk"));
        Assert.Null(c.FindLedgerByName("Notional Deduction"));
        // Affecting legs only: Dr Basic 30,000, Cr Advance 2,000, Cr Salary Payable 28,000.
        AssertLine(v, c.FindLedgerByName("Basic")!.Id, DrCr.Debit, 30000m, PayrollLineCategory.Earning, basic.Id);
        AssertLine(v, c.FindLedgerByName("Advance")!.Id, DrCr.Credit, 2000m, PayrollLineCategory.Deduction, advance.Id);
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 28000m, PayrollLineCategory.NetPayable, null);
        // The employer pair STILL posts (an employer head's Affect-Net flag is false by default, but it does not gate the pair).
        AssertLine(v, c.FindLedgerByName("Employer PF")!.Id, DrCr.Debit, 3600m, PayrollLineCategory.EmployerContributionExpense, employerPf.Id);
        AssertLine(v, c.FindLedgerByName("Employer PF Payable")!.Id, DrCr.Credit, 3600m, PayrollLineCategory.EmployerContributionPayable, employerPf.Id);
        // Trial-balance conserving: Σ Dr == Σ Cr == 33,600.
        Assert.Equal(new Money(33600m), v.TotalDebit);
        Assert.Equal(new Money(33600m), v.TotalCredit);
    }

    // ---------------------------------------------------------------- atomicity of a rejected run (F2)

    [Fact]
    public void Rejected_negative_net_run_creates_no_ledgers_and_sets_no_pay_head_link()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var advance = ph.CreatePayHead("Advance", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(10000m)),
            new SalaryStructureLine(advance.Id, 1, new Money(12000m)), // deduction > earning ⇒ negative net
        });
        var ledgerCountBefore = c.Ledgers.Count;

        Assert.Throws<InvalidOperationException>(() => new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id }));

        // Atomic (F2): no orphan ledgers, no mutated pay-head links, nothing posted.
        Assert.Equal(ledgerCountBefore, c.Ledgers.Count);
        Assert.Null(c.FindLedgerByName("Basic"));
        Assert.Null(c.FindLedgerByName("Advance"));
        Assert.Null(c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName));
        Assert.Null(c.FindPayHead(basic.Id)!.LedgerId);
        Assert.Null(c.FindPayHead(advance.Id)!.LedgerId);
        Assert.DoesNotContain(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }

    [Fact]
    public void A_run_rejected_at_posting_rolls_back_the_auto_created_ledgers_and_links()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(basic.Id, 0, new Money(30000m)) });
        var ledgerCountBefore = c.Ledgers.Count;

        // The pure pre-pass passes, so the ledgers/links ARE created during assembly — then the validator rejects
        // a voucher dated before BooksBeginFrom inside LedgerService.Post, which must roll the whole run back (F2).
        var beforeBooks = c.BooksBeginFrom.AddDays(-1);
        // LedgerService.Post rejects the pre-books date with InvalidVoucherException; the exact type is not the
        // point — the rollback is. ThrowsAny keeps the test robust to the rejection's exception type.
        Assert.ThrowsAny<Exception>(() =>
            new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id }, voucherDate: beforeBooks));

        Assert.Equal(ledgerCountBefore, c.Ledgers.Count);
        Assert.Null(c.FindLedgerByName("Basic"));
        Assert.Null(c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName));
        Assert.Null(c.FindPayHead(basic.Id)!.LedgerId);
        Assert.DoesNotContain(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }

    // ---------------------------------------------------------------- negative computed line guard (F5)

    [Fact]
    public void A_negative_computed_earning_is_rejected_with_a_clean_domain_error_not_an_unbalanced_voucher()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        // A computed earning with a NEGATIVE flat-value slab (a reachable config — the slab carries no value ≥ 0 guard).
        var negEarning = ph.CreatePayHead("Bad Allowance", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.FlatValue(new Money(-1000m)) }));
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(negEarning.Id, 1),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id }));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unbalanced", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Atomic: the run created nothing.
        Assert.Null(c.FindLedgerByName("Basic"));
        Assert.DoesNotContain(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }

    // ---------------------------------------------------------------- harness

    private readonly record struct GoldenHeads(Guid Basic, Guid Hra, Guid Advance);

    private static (Company Company, Guid EmployeeId, GoldenHeads Heads) BuildGolden()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));
        var emp = pay.CreateEmployee("Rajkumar Sharma", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        return (c, emp.Id, new GoldenHeads(basic.Id, hra.Id, advance.Id));
    }

    private static (PayrollService Pay, PayHeadService PayHead) MinimalPayroll(Company c)
    {
        var pay = new PayrollService(c);
        pay.EnablePayroll();
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
