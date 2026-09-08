using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census row 7.18 — the NPS pay head</b> (employee §80CCD(1B) / employer §80CCD(2) as a payroll component).
///
/// <para><b>What these tests are actually guarding, and what they deliberately do NOT.</b> There is no NPS
/// arithmetic in this product and there is not meant to be: the vendor's own NPS pay head is an ordinary
/// <i>As Computed Value</i> head whose Percentage slab the operator types
/// (<c>help.tallysolutions.com/docs/te9rel61/Payroll/Employees_NPS_Deduction_Pay_Head.htm</c>), so a test asserting
/// a seeded NPS rate would be asserting an invention. What can go silently wrong is <b>everything around</b> the
/// amount, and that is what is pinned here:</para>
/// <list type="number">
///   <item><b>The wrong accounting side.</b> NPS Tier-I is the one statutory tag in this book that is legitimately
///   two-sided; Tier-II is employee-only, because <i>"Any contribution by the employer towards NPS will fall under
///   the Tier I account of the scheme"</i>. A mis-typed statutory head posts a phantom, self-balancing pair — the
///   exact failure the PF/ESI/PT role guard exists for, and the one NPS slips past because
///   <c>RequiredStatutoryRole</c> can only express a single role.</item>
///   <item><b>Silent capture by a statutory engine.</b> An NPS head must stay on the generic calculation path. If
///   some future dispatch treats "tagged" as "computed by us", the operator's typed percentage stops reaching the
///   payslip and the amount changes without anyone editing a master.</item>
///   <item><b>Double-counting the §80CCD relief.</b> Relief reaches the §192 estimate through the employee's
///   DECLARED figure. If a posted NPS head ever also fed the estimate, every NPS employee would be under-withheld
///   and the annual true-up would hide it until March. The test below pins that the estimate does not move.</item>
///   <item><b>A roll-up that silently drops the new type.</b> The Payroll Statutory Summary declared NPS
///   unsupported on its own face; that note is now empty, so the row must actually appear in its place.</item>
/// </list>
/// </summary>
public sealed class NpsPayHeadTests
{
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);

    // Basic ₹30,000 with NPS at the operator's typed 10% ⇒ ₹3,000 on each side. Nothing derives 10%; it is typed
    // into the fixture exactly as an operator types it into the Percentage slab.
    private const decimal BasicAmount = 30_000m;
    private const int TenPercentBasisPoints = 1000;
    private const decimal ExpectedNps = 3_000m;

    private sealed record Fixture(Company Company, Guid Employee, Guid BasicId, Guid EmployeeNpsId, Guid EmployerNpsId);

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    /// <summary>
    /// One April wage month, one employee on Basic ₹30,000, with both NPS heads on the vendor's shape: the employee
    /// deduction is <i>Employees' Statutory Deductions</i> under <b>Current Liabilities</b>, the employer
    /// contribution is <i>Employer's Statutory Contributions</i> under <b>Indirect Expenses</b>, and both are
    /// As-Computed-Value at a typed Percentage of Basic Pay.
    /// </summary>
    private static Fixture Build(bool post = true)
    {
        var c = CompanyFactory.CreateSeeded("NPS Co", From, From);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        var onBasicAtTenPercent = new PayHeadComputation(
            new[] { new PayHeadComputationComponent(basic.Id) },
            new[] { PayHeadComputationSlab.Percentage(TenPercentBasisPoints) });

        var employeeNps = ph.CreatePayHead("Employee NPS Deduction", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI,
            computation: onBasicAtTenPercent);

        var employerNps = ph.CreatePayHead("Employer NPS Contribution", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(TenPercentBasisPoints) }));

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = From;

        new SalaryStructureService(c).DefineForEmployee(emp.Id, From, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(BasicAmount)),
            new SalaryStructureLine(employeeNps.Id, 1),
            new SalaryStructureLine(employerNps.Id, 2),
        });

        if (post) new PayrollVoucherService(c).Post(From, To, new[] { emp.Id });
        return new Fixture(c, emp.Id, basic.Id, employeeNps.Id, employerNps.Id);
    }

    // ------------------------------------------------------------------------------ the accounting side rule

    /// <summary>
    /// 🔴 <b>Tier-I is valid on BOTH sides — and that is the whole reason NPS needed its own guard.</b> Every other
    /// statutory component in this book maps to exactly one posting role, so <c>RequiredStatutoryRole</c> returns
    /// one. NPS Tier-I does not: the employee's deduction and the employer's contribution carry the SAME vendor
    /// statutory pay type and differ only by pay-head type. Building both in one company must simply work.
    /// </summary>
    [Fact]
    public void An_nps_tier_one_head_is_accepted_as_an_employee_deduction_and_as_an_employer_contribution()
    {
        var f = Build(post: false);

        var employee = f.Company.FindPayHead(f.EmployeeNpsId)!;
        var employer = f.Company.FindPayHead(f.EmployerNpsId)!;

        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, employee.IncomeTaxComponent);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, employer.IncomeTaxComponent);
        Assert.Equal(PayHeadPostingRole.Deduction, PayrollComputationService.RoleOf(employee.Type));
        Assert.Equal(PayHeadPostingRole.EmployerContribution, PayrollComputationService.RoleOf(employer.Type));

        // The employee side reduces net pay; the employer side is cost and does not (the vendor's "Affect net
        // salary — Yes" / "No"). Getting this backwards is a silent net-pay error, not a compile error.
        Assert.True(employee.AffectsNetSalary);
        Assert.False(employer.AffectsNetSalary);
    }

    /// <summary>
    /// 🔴 <b>Tier-II on the employer side is refused.</b> The scheme has no employer Tier-II: <i>"Any contribution
    /// by the employer towards NPS will fall under the Tier I account of the scheme."</i> Accepting it would post an
    /// employer expense/payable pair for a side of the scheme that does not exist, and it would foot perfectly.
    /// </summary>
    [Fact]
    public void An_nps_tier_two_head_is_refused_on_the_employer_side_and_named_in_the_message()
    {
        var c = CompanyFactory.CreateSeeded("NPS Reject Co", From, From);
        new PayrollService(c).EnablePayroll();
        var ph = new PayHeadService(c);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ph.CreatePayHead("Employer NPS Tier II", PayHeadType.EmployersStatutoryContributions,
                PayHeadCalculationType.AsUserDefinedValue, underGroupId: IndirectExpenses(c),
                incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierII));

        Assert.Contains(NationalPensionScheme.TierIIName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Employer NPS Tier II", ex.Message, StringComparison.Ordinal);
        // Nothing was written: the service never mutates the company on a rejected master.
        Assert.Empty(c.PayHeads);
    }

    /// <summary>Tier-II IS valid as an employee deduction — the rejection above must be about the side, not about
    /// Tier-II being unusable. A guard that refused both sides would pass the test above for the wrong reason.</summary>
    [Fact]
    public void An_nps_tier_two_head_is_accepted_as_an_employee_deduction()
    {
        var c = CompanyFactory.CreateSeeded("NPS Tier II Co", From, From);
        new PayrollService(c).EnablePayroll();

        var head = new PayHeadService(c).CreatePayHead("Employee NPS Tier II",
            PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: CurrentLiabilities(c),
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierII);

        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierII, head.IncomeTaxComponent);
        Assert.Single(c.PayHeads);
    }

    /// <summary>The side rule, stated once, over every pay-head type — so a future type cannot quietly acquire an
    /// NPS side nobody sourced.</summary>
    [Theory]
    [InlineData(PayHeadType.EmployeesStatutoryDeductions, true, true)]
    [InlineData(PayHeadType.EmployersStatutoryContributions, true, false)]
    [InlineData(PayHeadType.Earnings, false, false)]
    [InlineData(PayHeadType.Deductions, false, false)]
    [InlineData(PayHeadType.EmployersOtherCharges, false, false)]
    [InlineData(PayHeadType.Gratuity, false, false)]
    [InlineData(PayHeadType.LoansAndAdvances, false, false)]
    [InlineData(PayHeadType.Reimbursements, false, false)]
    [InlineData(PayHeadType.Bonus, false, false)]
    [InlineData(PayHeadType.NotApplicable, false, false)]
    public void The_nps_side_rule_allows_tier_one_on_both_statutory_types_and_tier_two_on_the_employee_one(
        PayHeadType type, bool tierIAllowed, bool tierIIAllowed)
    {
        Assert.Equal(tierIAllowed,
            NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.NationalPensionSchemeTierI, type));
        Assert.Equal(tierIIAllowed,
            NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.NationalPensionSchemeTierII, type));

        // A non-NPS component is not this rule's business on ANY type — the guard must not become a general
        // whitelist that starts refusing ordinary heads.
        Assert.True(NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.NotApplicable, type));
        Assert.True(NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.BasicSalary, type));
    }

    // ------------------------------------------------------------------------------ the amount, and whose it is

    /// <summary>
    /// 🔴 <b>An NPS head stays on the GENERIC computation path.</b> The amount is the operator's typed Percentage of
    /// Basic Pay — ₹3,000 on ₹30,000 at 10% — on both sides, and the employee side lands in deductions and the
    /// employer side in employer cost. If a future dispatch ever captured the NPS tag into a statutory engine, this
    /// figure would change with no master edit; that is the defect this pins.
    /// </summary>
    [Fact]
    public void An_nps_head_is_computed_by_its_own_typed_percentage_and_posted_on_the_right_side()
    {
        var f = Build();
        var result = new PayrollComputationService(f.Company).Compute(f.Employee, From, To);

        var employeeLine = result.Lines.Single(l => l.PayHead.Id == f.EmployeeNpsId);
        var employerLine = result.Lines.Single(l => l.PayHead.Id == f.EmployerNpsId);

        Assert.Equal(new Money(ExpectedNps), employeeLine.Amount);
        Assert.Equal(new Money(ExpectedNps), employerLine.Amount);
        Assert.Equal(PayHeadPostingRole.Deduction, employeeLine.Role);
        Assert.Equal(PayHeadPostingRole.EmployerContribution, employerLine.Role);

        // Net pay carries the employee's NPS and NOT the employer's — the identity the whole two-sided design
        // exists to keep. Gross 30,000 − 3,000 = 27,000, with employer cost 3,000 sitting outside it.
        Assert.Equal(new Money(BasicAmount), result.GrossEarnings);
        Assert.Equal(new Money(ExpectedNps), result.TotalDeductions);
        Assert.Equal(new Money(BasicAmount - ExpectedNps), result.NetPayable);
        Assert.Equal(new Money(ExpectedNps), result.EmployerContributions);
    }

    /// <summary>
    /// The posted voucher carries the two NPS legs in the right payroll categories. A computation that is right and
    /// a posting that is wrong reads identically on the computation tests alone.
    /// </summary>
    [Fact]
    public void The_posted_payroll_voucher_carries_the_employee_nps_as_a_deduction_and_the_employer_nps_as_a_payable()
    {
        var f = Build();
        var lines = f.Company.Vouchers.SelectMany(v => v.Lines)
            .Where(l => l.Payroll is not null)
            .Select(l => l.Payroll!)
            .ToList();

        var employee = lines.Single(p => p.PayHeadId == f.EmployeeNpsId);
        Assert.Equal(PayrollLineCategory.Deduction, employee.Category);
        Assert.Equal(new Money(ExpectedNps), employee.Amount);

        // The employer side posts a BALANCED PAIR — an expense debit and a payable credit — not one line. Asserting
        // a single employer line is how a half-posted employer contribution passes: the payable would be raised
        // with no cost booked, and the trial balance would still foot because the payroll voucher balances overall.
        var employer = lines.Where(p => p.PayHeadId == f.EmployerNpsId).ToList();
        Assert.Equal(2, employer.Count);
        Assert.Equal(new Money(ExpectedNps), employer.Single(p => p.Category == PayrollLineCategory.EmployerContributionExpense).Amount);
        Assert.Equal(new Money(ExpectedNps), employer.Single(p => p.Category == PayrollLineCategory.EmployerContributionPayable).Amount);
    }

    // ------------------------------------------------------------------------------ the statutory roll-up

    /// <summary>
    /// 🔴 <b>The divergence the Payroll Statutory Summary declared is CLOSED, and closed by a row rather than by
    /// deleting a sentence.</b> Both NPS heads roll into one <c>NationalPensionScheme</c> type, the vendor's
    /// per-pay-head detail level lists each, and the note that used to say NPS is unsupported is empty.
    /// </summary>
    [Fact]
    public void The_payroll_statutory_summary_rolls_up_both_nps_sides_and_declares_no_divergence()
    {
        var f = Build();

        var summary = PayrollStatutorySummary.Build(f.Company, From, To);
        var nps = summary.Rows.Single(r => r.HeadType == PayrollStatutoryHeadType.NationalPensionScheme);

        Assert.Equal(NationalPensionScheme.SummaryCaption, nps.Caption);
        Assert.Equal(new Money(ExpectedNps * 2m), nps.Payable);   // employee 3,000 + employer 3,000
        Assert.Equal(Money.Zero, nps.Paid);
        Assert.Equal(new Money(ExpectedNps), nps.Details.Single(d => d.PayHeadName == "Employee NPS Deduction").Payable);
        Assert.Equal(new Money(ExpectedNps), nps.Details.Single(d => d.PayHeadName == "Employer NPS Contribution").Payable);

        Assert.Empty(PayrollStatutorySummary.UnsupportedTypeNote);
    }

    /// <summary>Classification is pinned directly, both ways: an NPS head is NPS, and an ordinary earning is still
    /// nothing. A classifier that swept every tagged head into NPS would pass the roll-up test above.</summary>
    [Fact]
    public void Nps_classification_covers_both_tiers_and_nothing_else()
    {
        var f = Build(post: false);

        Assert.Equal(PayrollStatutoryHeadType.NationalPensionScheme,
            PayrollStatutorySummary.ClassifyHead(f.Company.FindPayHead(f.EmployeeNpsId)!));
        Assert.Equal(PayrollStatutoryHeadType.NationalPensionScheme,
            PayrollStatutorySummary.ClassifyHead(f.Company.FindPayHead(f.EmployerNpsId)!));
        Assert.Null(PayrollStatutorySummary.ClassifyHead(f.Company.FindPayHead(f.BasicId)!));

        Assert.True(NationalPensionScheme.IsNpsComponent(IncomeTaxComponent.NationalPensionSchemeTierI));
        Assert.True(NationalPensionScheme.IsNpsComponent(IncomeTaxComponent.NationalPensionSchemeTierII));
        Assert.False(NationalPensionScheme.IsNpsComponent(IncomeTaxComponent.TaxDeductedAtSource));
        Assert.False(NationalPensionScheme.IsNpsComponent(IncomeTaxComponent.NotApplicable));
    }

    // ------------------------------------------------------------------------------ the tax interaction

    /// <summary>
    /// 🔴 <b>THE POSTED NPS PAY HEAD MUST NOT MOVE THE §192 ESTIMATE.</b> §80CCD relief reaches the estimate through
    /// the employee's DECLARED <c>Section80CCD1B</c> / <c>Section80CCD2Employer</c> figure and through nothing else.
    /// If a posted NPS head also fed it, every NPS employee would get the relief twice and be under-withheld all
    /// year, with the shortfall surfacing only in the March true-up. Two companies, identical but for the NPS heads,
    /// must produce the identical monthly TDS.
    /// <para>The employee's NPS deduction is also correctly absent from taxable gross for a second reason: taxable
    /// gross is the sum of <b>earning</b> heads, and NPS on the employee side is a deduction.</para>
    /// </summary>
    [Fact]
    public void An_nps_pay_head_does_not_change_the_section_192_estimate()
    {
        var withNps = BuildTdsCompany(includeNps: true);
        var withoutNps = BuildTdsCompany(includeNps: false);

        var a = new PayrollComputationService(withNps.Company).Compute(withNps.Employee, From, To);
        var b = new PayrollComputationService(withoutNps.Company).Compute(withoutNps.Employee, From, To);

        var tdsWith = a.Lines.Single(l => l.PayHead.IncomeTaxComponent == IncomeTaxComponent.TaxDeductedAtSource).Amount;
        var tdsWithout = b.Lines.Single(l => l.PayHead.IncomeTaxComponent == IncomeTaxComponent.TaxDeductedAtSource).Amount;

        Assert.Equal(tdsWithout, tdsWith);

        // …and the estimate is a real, non-zero withholding, so the equality above is not two zeroes agreeing.
        Assert.True(tdsWith.Amount > 0m, "the TDS fixture withheld nothing, so the comparison proves nothing.");
    }

    /// <summary>A ₹1,20,000-a-month employee (well past the new-regime rebate) with salary-TDS enrolled, optionally
    /// carrying the two NPS heads.</summary>
    private static Fixture BuildTdsCompany(bool includeNps)
    {
        var c = CompanyFactory.CreateSeeded($"NPS TDS Co {includeNps}", From, From);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);
        var tds = ph.CreatePayHead("Income Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);

        var lines = new List<SalaryStructureLine>
        {
            new(basic.Id, 0, new Money(1_20_000m)),
            new(tds.Id, 1),
        };

        var employeeNpsId = Guid.Empty;
        var employerNpsId = Guid.Empty;
        if (includeNps)
        {
            var employeeNps = ph.CreatePayHead("Employee NPS Deduction", PayHeadType.EmployeesStatutoryDeductions,
                PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
                incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI,
                computation: new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(basic.Id) },
                    new[] { PayHeadComputationSlab.Percentage(TenPercentBasisPoints) }));
            var employerNps = ph.CreatePayHead("Employer NPS Contribution", PayHeadType.EmployersStatutoryContributions,
                PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
                incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI,
                computation: new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(basic.Id) },
                    new[] { PayHeadComputationSlab.Percentage(TenPercentBasisPoints) }));
            employeeNpsId = employeeNps.Id;
            employerNpsId = employerNps.Id;
            lines.Add(new SalaryStructureLine(employeeNps.Id, 2));
            lines.Add(new SalaryStructureLine(employerNps.Id, 3));
        }

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = From;
        new SalaryStructureService(c).DefineForEmployee(emp.Id, From, lines);

        return new Fixture(c, emp.Id, basic.Id, employeeNpsId, employerNpsId);
    }
}
