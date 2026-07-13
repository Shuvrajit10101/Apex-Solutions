using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-9 <b>gratuity provision voucher</b> contract (RQ-14; ER-1) — the period-end provision through the S3
/// atomic auto-ledger path. The provision is an <b>employer accounting entry</b> (not a payslip deduction): Dr Gratuity
/// Expense (Indirect Expenses) / Cr Gratuity Provision (Current Liabilities) for the <b>increase over the prior
/// balance</b> (a decrease reverses). Every voucher balances Dr==Cr to the paisa. The headline oracle: Basic + DA
/// ₹26,000 over 10 years ⇒ ₹1,50,000 provision, posted delta-only.
/// </summary>
public sealed class GratuityProvisionPostingTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Gratuity Co", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;

    private static Company BuildCompany(out Guid basicHeadId)
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableGratuity();
        var ph = new PayHeadService(c);
        // A single Basic+DA earning flagged UseForGratuity (the Act's "wages" = Basic + DA).
        basicHeadId = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), useForGratuity: true).Id;
        return c;
    }

    private static Guid AddEmployee(Company c, Guid basicHeadId, string name, DateOnly doj, DateOnly structureFrom, decimal basicDa)
    {
        var pay = new PayrollService(c);
        var group = c.FindEmployeeGroupByName("Staff") ?? pay.CreateEmployeeGroup("Staff");
        var e = pay.CreateEmployee(name, group.Id);
        c.FindEmployee(e.Id)!.DateOfJoining = doj;
        new SalaryStructureService(c).DefineForEmployee(e.Id, structureFrom,
            new[] { new SalaryStructureLine(basicHeadId, 0, new Money(basicDa)) });
        return e.Id;
    }

    private static decimal LedgerSide(Voucher v, string ledgerName, Company c, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == c.FindLedgerByName(ledgerName)!.Id && l.Side == side).Sum(l => l.Amount.Amount);

    // ---------------------------------------------------------------- headline golden: first provision

    [Fact]
    public void Golden_first_provision_posts_150000_and_balances()
    {
        var c = BuildCompany(out var basic);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m);

        var v = new PayrollVoucherService(c).PostGratuityProvision(new DateOnly(2025, 4, 1), new[] { a });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(150_000m), v.TotalDebit);
        Assert.Equal(new Money(150_000m), v.TotalCredit);
        // Dr Gratuity Expense 1,50,000 / Cr Gratuity Provision 1,50,000.
        Assert.Equal(150_000m, LedgerSide(v, PayrollVoucherService.GratuityExpenseLedgerName, c, DrCr.Debit));
        Assert.Equal(150_000m, LedgerSide(v, PayrollVoucherService.GratuityProvisionLedgerName, c, DrCr.Credit));
    }

    // ---------------------------------------------------------------- delta-only over the prior balance

    [Fact]
    public void A_second_provision_posts_only_the_increase_over_the_prior_balance()
    {
        var c = BuildCompany(out var basic);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m); // 10y → 1,50,000
        var svc = new PayrollVoucherService(c);
        svc.PostGratuityProvision(new DateOnly(2025, 4, 1), new[] { a }); // prior balance now ₹1,50,000

        // A new member vests to 5 years by 2025-05-01: 26,000 × 15 × 5 / 26 = ₹75,000; Anil is unchanged at ₹1,50,000.
        var b = AddEmployee(c, basic, "Bimal", new DateOnly(2020, 5, 1), new DateOnly(2020, 5, 1), 26_000m);
        var v2 = svc.PostGratuityProvision(new DateOnly(2025, 5, 1), new[] { a, b });

        Assert.True(VoucherValidator.IsBalanced(v2));
        // Only the ₹75,000 delta posts (not the ₹2,25,000 total).
        Assert.Equal(new Money(75_000m), v2.TotalDebit);
        Assert.Equal(75_000m, LedgerSide(v2, PayrollVoucherService.GratuityExpenseLedgerName, c, DrCr.Debit));
        Assert.Equal(75_000m, LedgerSide(v2, PayrollVoucherService.GratuityProvisionLedgerName, c, DrCr.Credit));

        // The Gratuity Provision ledger now carries the full ₹2,25,000 accumulated liability.
        Assert.Equal(new Money(225_000m), svc.PriorGratuityProvisionBalance(new DateOnly(2025, 6, 1)));
    }

    // ---------------------------------------------------------------- a decrease reverses (write-back)

    [Fact]
    public void A_fall_in_the_liability_posts_a_reversing_write_back()
    {
        var c = BuildCompany(out var basic);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m);
        var b = AddEmployee(c, basic, "Bimal", new DateOnly(2020, 5, 1), new DateOnly(2020, 5, 1), 26_000m);
        var svc = new PayrollVoucherService(c);
        svc.PostGratuityProvision(new DateOnly(2025, 5, 1), new[] { a, b }); // ₹2,25,000

        // Re-provision for Anil only (as if Bimal left): total falls to ₹1,50,000 ⇒ a ₹75,000 write-back.
        var v = svc.PostGratuityProvision(new DateOnly(2025, 6, 1), new[] { a });

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(75_000m), v.TotalDebit);
        // Reversed sides: Dr Gratuity Provision / Cr Gratuity Expense.
        Assert.Equal(75_000m, LedgerSide(v, PayrollVoucherService.GratuityProvisionLedgerName, c, DrCr.Debit));
        Assert.Equal(75_000m, LedgerSide(v, PayrollVoucherService.GratuityExpenseLedgerName, c, DrCr.Credit));
        Assert.Equal(new Money(150_000m), svc.PriorGratuityProvisionBalance(new DateOnly(2025, 7, 1)));
    }

    // ---------------------------------------------------------------- same-date true-up: only the increment posts (regression)

    [Fact]
    public void A_same_date_re_provision_posts_only_the_increment_not_the_whole_accrued_again()
    {
        // Regression (adversarial finding): the engine derived the prior provision from a STRICTLY-BEFORE balance keyed
        // on the voucher date, so a provision already dated ON the same period-end was excluded from the prior. When the
        // accrual then changed and the SAME fixed period-end was re-struck, the engine re-posted the WHOLE accrued a
        // second time (double-counting the liability) instead of only the increment — diverging from the register VM,
        // which gates on the inclusive prior. The prior must be inclusive of same-date-earlier provisions.
        var c = BuildCompany(out var basic);
        var asOn = new DateOnly(2025, 4, 1);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m); // 10y → 1,50,000
        var svc = new PayrollVoucherService(c);

        // First provision for the period end 2025-04-01 ⇒ ₹1,50,000; the ledger now carries ₹1,50,000.
        svc.PostGratuityProvision(asOn, new[] { a });
        Assert.Equal(new Money(150_000m), svc.PriorGratuityProvisionBalance(asOn.AddDays(1)));

        // A late-added active member (vested to 5 years) lifts the accrued for the SAME period end; the natural fixed
        // period-end date is re-struck. New total accrued = 1,50,000 + 75,000 = 2,25,000.
        var b = AddEmployee(c, basic, "Bimal", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1), 26_000m); // 5y → 75,000
        var v2 = svc.PostGratuityProvision(asOn, new[] { a, b });

        Assert.True(VoucherValidator.IsBalanced(v2));
        // ONLY the ₹75,000 increment posts — NOT the whole ₹2,25,000 accrued a second time.
        Assert.Equal(new Money(75_000m), v2.TotalDebit);
        Assert.Equal(new Money(75_000m), v2.TotalCredit);
        Assert.Equal(75_000m, LedgerSide(v2, PayrollVoucherService.GratuityExpenseLedgerName, c, DrCr.Debit));
        Assert.Equal(75_000m, LedgerSide(v2, PayrollVoucherService.GratuityProvisionLedgerName, c, DrCr.Credit));

        // The Gratuity Provision ledger reconciles to the TRUE accrued ₹2,25,000 — not a double-counted ₹3,75,000.
        Assert.Equal(new Money(225_000m), svc.PriorGratuityProvisionBalance(asOn.AddDays(1)));
    }

    // ---------------------------------------------------------------- unchanged ⇒ nothing to post

    [Fact]
    public void An_unchanged_provision_throws_no_delta_to_post()
    {
        var c = BuildCompany(out var basic);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m);
        var svc = new PayrollVoucherService(c);
        svc.PostGratuityProvision(new DateOnly(2025, 4, 1), new[] { a });

        // Anil's completed years are unchanged one month on ⇒ delta 0 ⇒ nothing to post.
        var ex = Assert.Throws<InvalidOperationException>(
            () => svc.PostGratuityProvision(new DateOnly(2025, 5, 1), new[] { a }));
        Assert.Contains("unchanged", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- ER-13: inert until enrolled

    [Fact]
    public void Gratuity_posting_is_blocked_until_the_establishment_enrols()
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll(); // NB: gratuity NOT enrolled
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), useForGratuity: true).Id;
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m);

        Assert.Throws<InvalidOperationException>(
            () => new PayrollVoucherService(c).PostGratuityProvision(new DateOnly(2025, 4, 1), new[] { a }));
        Assert.Null(c.FindLedgerByName(PayrollVoucherService.GratuityProvisionLedgerName));
    }

    // ---------------------------------------------------------------- the register reconciles to the posting

    [Fact]
    public void The_register_reconciles_to_the_posted_provision_with_the_vested_flag()
    {
        var c = BuildCompany(out var basic);
        var a = AddEmployee(c, basic, "Anil", new DateOnly(2015, 4, 1), new DateOnly(2015, 4, 1), 26_000m); // 10y, vested
        var cee = AddEmployee(c, basic, "Charu", new DateOnly(2022, 4, 1), new DateOnly(2022, 4, 1), 26_000m); // 3y, not vested

        var reg = GratuityProvisionRegister.Build(c, new[] { a, cee }, new DateOnly(2025, 4, 1));

        // Charu 26,000 × 15 × 3 / 26 = 45,000; Anil 1,50,000; total 1,95,000.
        Assert.Equal(195_000L, reg.TotalLiability);
        Assert.Equal(2_000_000L, reg.CapAmount);

        var anil = reg.Rows.Single(r => r.EmployeeName == "Anil");
        Assert.Equal(10, anil.CompletedYears);
        Assert.Equal(26_000L, anil.BasicPlusDa);
        Assert.Equal(150_000L, anil.AccruedGratuity);
        Assert.True(anil.Vested);

        var charu = reg.Rows.Single(r => r.EmployeeName == "Charu");
        Assert.Equal(3, charu.CompletedYears);
        Assert.Equal(45_000L, charu.AccruedGratuity);
        Assert.False(charu.Vested); // 3 years < 5-year vesting

        // The posted provision for both equals the register total.
        var v = new PayrollVoucherService(c).PostGratuityProvision(new DateOnly(2025, 4, 1), new[] { a, cee });
        Assert.Equal(new Money(195_000m), v.TotalCredit);
    }
}
