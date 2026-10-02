using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census row 7.19 — the Labour Welfare Fund deduction</b>, and the dated pay-head computation slab
/// (schema v63) that finally makes it expressible.
///
/// <para><b>R7 — what the vendor actually says, which is the whole specification.</b>
/// <c>help.tallysolutions.com/tally-prime/payroll/payroll-faq/</c>, "How to create Labour Welfare Fund Pay Head?":
/// <i>"Select <b>Deductions From Employees</b> in the <b>Pay head type</b> field. In <b>Computation
/// Information</b> section, define the <b>Effective From</b> and <b>Value</b> as applicable."</i> — with the note
/// that decides everything: <i>"The value will be deducted only for the month (December) specified in the Pay
/// Head."</i> (retrieved and read 2026-09-14.) So LWF is an <b>ordinary user-defined pay head</b> whose
/// computation rows are <b>dated</b>, and the dating is what confines the levy to the month it falls due.</para>
///
/// <para>🔴 <b>NO RATE IS ASSERTED ANYWHERE IN THIS FILE, AND THAT IS DELIBERATE.</b> LWF is a <b>state</b> levy:
/// the amount, any wage ceiling, the employer/employee split and the <b>periodicity</b> (several states collect
/// half-yearly, others annually — <b>not</b> monthly) are fixed by each State's own Act and notifications. The
/// reference product seeds none of it and neither do we, so every figure below is a <b>fixture</b> typed the way
/// an operator types their own State's figure — exactly the stance <c>NpsPayHeadTests</c> takes, where "a test
/// asserting a seeded NPS rate would be asserting an invention". A national-looking rate table on a path that
/// takes money off a payslip is the most expensive defect class this project has (the Karnataka professional-tax
/// over-charge cost a Tier 0 fix plus the v55 back-fill migration), and no official State instrument could be
/// retrieved in the session that wrote this — see the branch report for the list.</para>
///
/// <para><b>What is actually pinned here.</b> The single defect that made census 7.19 unbuildable: before v63 a
/// computation slab carried <b>no date of any kind</b> and there was no month gate on a pay head, so a flat
/// deduction fired in <b>every</b> payroll period and an annual contribution came off the payslip <b>twelve
/// times a year</b>. <see cref="A_december_only_levy_is_deducted_in_december_and_in_no_other_month"/> is the
/// regression for that and <b>fails on any build without the dated window</b>. The rest guard the ways the gate
/// could be right in one place and wrong in another: the perpetual default must not move (ER-13), the boundaries
/// must be inclusive at both ends, and percentage slabs must be gated too — not just flat-value ones.</para>
/// </summary>
public sealed class LabourWelfareFundPayHeadTests
{
    // One financial year of monthly wage periods. Nothing here is a statutory figure.
    private static readonly DateOnly YearStart = new(2026, 4, 1);

    /// <summary>A fixture Basic. Not a rate — a salary.</summary>
    private const decimal BasicAmount = 30_000m;

    /// <summary>
    /// The contribution the fixture's operator typed. 🔴 This is NOT any State's LWF rate and must never be read
    /// as one; it is an arbitrary round number chosen so that a twelve-fold over-deduction is unmistakable in an
    /// assertion message (₹100 once versus ₹1,200 over the year).
    /// </summary>
    private const decimal OperatorTypedContribution = 100m;

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private sealed record Fixture(Company Company, Guid Employee, Guid LwfHeadId);

    /// <summary>
    /// One employee on a flat Basic, plus a <b>Deductions from Employees</b> head built on the vendor's LWF shape:
    /// As-Computed-Value on Basic, one flat-value slab, optionally confined to a single month by the v63 window.
    /// </summary>
    /// <param name="levyYear">Year of the month the levy falls due; <c>null</c> ⇒ an UNDATED (perpetual) slab,
    /// which is the pre-v63 shape and the thing the December case is compared against.</param>
    /// <param name="levyMonth">Calendar month the levy falls due.</param>
    private static Fixture Build(int? levyYear = null, int levyMonth = 12)
    {
        var c = CompanyFactory.CreateSeeded("LWF Co", YearStart, YearStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        var slab = levyYear is { } y
            ? PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), y, levyMonth)
            : PayHeadComputationSlab.FlatValue(new Money(OperatorTypedContribution));

        // The vendor's own words: "Select Deductions From Employees in the Pay head type field."
        var lwf = ph.CreatePayHead("Labour Welfare Fund", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { slab }));

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = YearStart;

        new SalaryStructureService(c).DefineForEmployee(emp.Id, YearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(BasicAmount)),
            new SalaryStructureLine(lwf.Id, 1),
        });

        return new Fixture(c, emp.Id, lwf.Id);
    }

    /// <summary>The LWF amount on the payslip for the calendar month <paramref name="monthOffset"/> months after
    /// the April the fixture starts in (0 = April … 8 = December … 11 = March).</summary>
    private static decimal LwfFor(Fixture f, int monthOffset)
    {
        var first = YearStart.AddMonths(monthOffset);
        var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
        var result = new PayrollComputationService(f.Company).Compute(f.Employee, first, last);
        return result.Lines.Single(l => l.PayHead.Id == f.LwfHeadId).Amount.Amount;
    }

    // ─────────────────────────────────────────────────────────── the regression census 7.19 was blocked on

    /// <summary>
    /// 🔴 <b>THE ROW'S REASON FOR EXISTING. FAILS ON ANY BUILD WITHOUT THE v63 DATED WINDOW.</b>
    ///
    /// <para>A Labour Welfare Fund contribution confined to December must be deducted <b>once</b> — in December —
    /// and in none of the other eleven months. Before v63 there was no date on a computation slab and no month
    /// gate anywhere on a pay head, so this same head deducted in <b>all twelve</b> periods: the annual levy came
    /// off the salary twelve times over. That is a strictly worse instance of the ₹100-a-year Karnataka
    /// professional-tax over-charge that already cost a Tier 0 fix plus the v55 back-fill migration, and it is
    /// why this census row was held ABSENT — <b>blocked on storage</b> — rather than guessed at.</para>
    ///
    /// <para>The whole financial year is walked rather than "December and one other month", because a gate that
    /// is subtly wrong (off by a month, or open at one end) passes a two-point check and fails on the twelve-point
    /// one. The annual total is asserted separately: it is the figure an employee would actually be out of pocket
    /// by, and the one a per-month assertion alone would let drift.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void A_december_only_levy_is_deducted_in_december_and_in_no_other_month()
    {
        var f = Build(levyYear: 2026, levyMonth: 12);

        // April 2026 … March 2027. December 2026 is offset 8.
        const int december = 8;
        decimal annual = 0m;
        for (var m = 0; m < 12; m++)
        {
            var amount = LwfFor(f, m);
            annual += amount;
            var period = YearStart.AddMonths(m);
            if (m == december)
                Assert.True(amount == OperatorTypedContribution,
                    $"The levy month {period:MMM yyyy} should deduct {OperatorTypedContribution} but deducted {amount}.");
            else
                Assert.True(amount == 0m,
                    $"{period:MMM yyyy} is not the levy month and must deduct nothing, but deducted {amount}. "
                  + "A once-a-year Labour Welfare Fund contribution is being taken every month.");
        }

        // Once a year, not twelve times: the figure the employee is actually out of pocket by.
        Assert.Equal(OperatorTypedContribution, annual);
    }

    /// <summary>
    /// 🔴 <b>The pre-v63 behaviour must be EXACTLY preserved for an undated slab (ER-13).</b> Both bounds null
    /// means "in force in every period", so an existing pay head — every pay head in every shipped book — keeps
    /// deducting every month to the same paisa. This is the control for the test above: it proves the December
    /// result comes from the <i>window</i> and not from some unrelated change to slab evaluation.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void An_undated_slab_still_deducts_in_every_period_exactly_as_before_v63()
    {
        var f = Build(levyYear: null);

        for (var m = 0; m < 12; m++)
            Assert.Equal(OperatorTypedContribution, LwfFor(f, m));
    }

    /// <summary>
    /// <b>Both ends of the window are inclusive, and the anchor is the period END date.</b> A slab effective
    /// 01-Dec-2026 → 31-Dec-2026 is in force for the December period and out of force the day after. An exclusive
    /// upper bound would silently skip the levy month altogether — a deduction that never happens, which shows up
    /// on nobody's payslip and only surfaces later as an unremitted statutory liability.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void The_effective_window_is_inclusive_at_both_ends()
    {
        var slab = PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), 2026, 12);

        Assert.False(slab.IsInForceOn(new DateOnly(2026, 11, 30)), "the day before the window must be out of force");
        Assert.True(slab.IsInForceOn(new DateOnly(2026, 12, 1)), "the first day of the window must be in force");
        Assert.True(slab.IsInForceOn(new DateOnly(2026, 12, 31)), "the last day of the window must be in force");
        Assert.False(slab.IsInForceOn(new DateOnly(2027, 1, 1)), "the day after the window must be out of force");
    }

    /// <summary>
    /// <b>An open-ended window behaves as the vendor's succession model.</b> "Effective From" with no end date is
    /// in force from that date onward forever — which is the reference product's own shape, where a later-dated
    /// row supersedes an earlier one going forward. Pinning it stops a future change from quietly treating a
    /// missing end date as "one month".
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void An_open_ended_effective_from_is_in_force_from_that_date_onward()
    {
        var slab = PayHeadComputationSlab.FlatValue(
            new Money(OperatorTypedContribution), effectiveFrom: new DateOnly(2026, 12, 1));

        Assert.False(slab.IsInForceOn(new DateOnly(2026, 11, 30)));
        Assert.True(slab.IsInForceOn(new DateOnly(2026, 12, 1)));
        Assert.True(slab.IsInForceOn(new DateOnly(2030, 6, 30)));
    }

    /// <summary>
    /// 🔴 <b>PERCENTAGE slabs are gated too, not only flat-value ones.</b> The date check sits ahead of the
    /// type switch on purpose. Had it been placed inside the flat-value branch — the obvious place, since LWF is
    /// a flat amount — every percentage slab would have stayed silently perpetual, and a dated percentage
    /// deduction would have been wrong in exactly the way this whole change exists to prevent, while the
    /// flat-value tests above all passed.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void A_dated_percentage_slab_is_gated_by_the_same_window()
    {
        var c = CompanyFactory.CreateSeeded("LWF Pct Co", YearStart, YearStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        // 1% of Basic, confined to December 2026.
        var pct = new PayHeadComputationSlab(
            PayHeadComputationSlabType.Percentage, rateBasisPoints: 100,
            effectiveFrom: new DateOnly(2026, 12, 1), effectiveTo: new DateOnly(2026, 12, 31));

        var head = ph.CreatePayHead("Dated Percentage Deduction", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { pct }));

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = YearStart;
        new SalaryStructureService(c).DefineForEmployee(emp.Id, YearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(BasicAmount)),
            new SalaryStructureLine(head.Id, 1),
        });

        var f = new Fixture(c, emp.Id, head.Id);
        Assert.Equal(BasicAmount * 0.01m, LwfFor(f, 8));   // December 2026
        Assert.Equal(0m, LwfFor(f, 7));                    // November 2026
        Assert.Equal(0m, LwfFor(f, 9));                    // January 2027
    }

    /// <summary>
    /// <b>An inverted window is refused at construction.</b> A slab whose end precedes its start can never be in
    /// force, so it would produce a deduction that simply never happens — invisible on the payslip, and only
    /// discovered when the statutory remittance does not reconcile. Failing fast is the cheaper error.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void An_inverted_effective_window_is_refused_and_the_message_names_both_dates()
    {
        var ex = Assert.Throws<ArgumentException>(() => PayHeadComputationSlab.FlatValue(
            new Money(OperatorTypedContribution),
            effectiveFrom: new DateOnly(2026, 12, 31), effectiveTo: new DateOnly(2026, 12, 1)));

        Assert.Contains("2026-12-31", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2026-12-01", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A same-day window is legal</b> (start == end). It is the natural way to express a levy that falls on a
    /// single date, and refusing it would push users into the inverted-or-widened workarounds this guard exists
    /// to prevent.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void A_single_day_effective_window_is_accepted()
    {
        var slab = PayHeadComputationSlab.FlatValue(
            new Money(OperatorTypedContribution),
            effectiveFrom: new DateOnly(2026, 12, 31), effectiveTo: new DateOnly(2026, 12, 31));

        Assert.True(slab.IsInForceOn(new DateOnly(2026, 12, 31)));
        Assert.False(slab.IsInForceOn(new DateOnly(2026, 12, 30)));
    }

    /// <summary>
    /// <b>A half-yearly levy is expressible too — the row is not a December special case.</b> Several States
    /// collect LWF half-yearly rather than annually, so two dated slabs on one head must fire in their own two
    /// months and nowhere else. Nothing here asserts which States, or what they charge: the periodicity is the
    /// operator's, and only the mechanism is ours.
    /// </summary>
    [Fact]
    [Trait("Category", "Payroll")]
    public void Two_dated_slabs_express_a_half_yearly_levy_and_fire_only_in_their_own_months()
    {
        var c = CompanyFactory.CreateSeeded("LWF Half Yearly Co", YearStart, YearStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        // June 2026 and December 2026 — offsets 2 and 8 from the April start.
        var head = ph.CreatePayHead("Labour Welfare Fund", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[]
                {
                    PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), 2026, 6),
                    PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), 2026, 12),
                }));

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = YearStart;
        new SalaryStructureService(c).DefineForEmployee(emp.Id, YearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(BasicAmount)),
            new SalaryStructureLine(head.Id, 1),
        });

        var f = new Fixture(c, emp.Id, head.Id);
        decimal annual = 0m;
        for (var m = 0; m < 12; m++)
        {
            var amount = LwfFor(f, m);
            annual += amount;
            var expected = m is 2 or 8 ? OperatorTypedContribution : 0m;
            Assert.True(amount == expected,
                $"{YearStart.AddMonths(m):MMM yyyy} expected {expected} but deducted {amount}.");
        }

        // 🔴 Twice a year, NOT twelve times — the arithmetic the whole row turns on.
        Assert.Equal(OperatorTypedContribution * 2m, annual);
    }
}
