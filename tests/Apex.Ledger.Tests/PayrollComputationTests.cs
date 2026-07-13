using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-3 <b>salary-computation engine</b> contract (RQ-7; ER-1/ER-2/ER-3/ER-4). Covers the five
/// calculation types, computed-on dependency-order (out-of-order + chained) evaluation with a defensive cycle
/// guard, On-Attendance pro-rating, rounding, and the dated structure-in-force resolution (employee scope over
/// group scope). The headline oracle is the <b>hand-derived golden payslip</b> (Basic ₹30,000 flat + HRA 40% =
/// ₹12,000 + a flat ₹2,000 advance recovery ⇒ gross ₹42,000 / deductions ₹2,000 / net ₹40,000). Pure engine —
/// no DB, no posting (that is <see cref="PayrollVoucherPostingTests"/>).
/// </summary>
public sealed class PayrollComputationTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Payroll Engine Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    // ---------------------------------------------------------------- the hand-derived golden payslip

    [Fact]
    public void Golden_payslip_gross_deductions_net_are_exact()
    {
        var (c, emp) = BuildGoldenCompany();

        var result = new PayrollComputationService(c).Compute(emp, PeriodFrom, PeriodTo);

        Assert.Equal(new Money(42000m), result.GrossEarnings);   // Basic 30,000 + HRA 12,000
        Assert.Equal(new Money(2000m), result.TotalDeductions);  // flat advance recovery
        Assert.Equal(new Money(40000m), result.NetPayable);      // 42,000 − 2,000
        Assert.Equal(Money.Zero, result.EmployerContributions);

        // Per-head breakdown, in structure order.
        var basic = result.Lines.Single(l => l.PayHead.Name == "Basic");
        var hra = result.Lines.Single(l => l.PayHead.Name == "HRA");
        var adv = result.Lines.Single(l => l.PayHead.Name == "Advance Recovery");
        Assert.Equal(new Money(30000m), basic.Amount);
        Assert.Equal(new Money(12000m), hra.Amount);          // 40% of 30,000, computed
        Assert.Equal(new Money(2000m), adv.Amount);
        Assert.Equal(PayHeadPostingRole.Earning, basic.Role);
        Assert.Equal(PayHeadPostingRole.Earning, hra.Role);
        Assert.Equal(PayHeadPostingRole.Deduction, adv.Role);

        // Conservation: gross = deductions + net, to the paisa.
        Assert.Equal(result.GrossEarnings, result.TotalDeductions + result.NetPayable);
    }

    // ---------------------------------------------------------------- each calculation type

    [Fact]
    public void Flat_rate_returns_the_structure_line_amount()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(basic.Id, 0, new Money(45000m)) });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(45000m), r.Lines.Single().Amount);
    }

    [Fact]
    public void As_computed_value_applies_percentage_of_basis()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var da = ph.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) })); // 40% of Basic
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(50000m)),
            new SalaryStructureLine(da.Id, 1),
        });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(20000m), r.Lines.Single(l => l.PayHead.Name == "DA").Amount); // 40% of 50,000
    }

    [Fact]
    public void On_production_is_rate_times_units()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var units = pay.CreateAttendanceType("Units", AttendanceTypeKind.Production);
        var piece = ph.CreatePayHead("Piece Rate", PayHeadType.Earnings, PayHeadCalculationType.OnProduction,
            underGroupId: IndirectExpenses(c), attendanceTypeId: units.Id);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(piece.Id, 0, new Money(15m)) }); // ₹15 per unit
        new PayrollAttendanceService(c).Record(emp.Id, units.Id, PeriodFrom, PeriodTo, 800m); // 800 units

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(12000m), r.Lines.Single().Amount); // 15 × 800
    }

    [Fact]
    public void As_user_defined_value_uses_the_voucher_supplied_amount()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var bonus = ph.CreatePayHead("Diwali Bonus", PayHeadType.Bonus, PayHeadCalculationType.AsUserDefinedValue, underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(bonus.Id, 0) });

        var userDefined = new Dictionary<Guid, Money> { [bonus.Id] = new Money(5000m) };
        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo, userDefined);
        Assert.Equal(new Money(5000m), r.Lines.Single().Amount);
    }

    [Fact]
    public void Missing_user_defined_value_throws()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var bonus = ph.CreatePayHead("Diwali Bonus", PayHeadType.Bonus, PayHeadCalculationType.AsUserDefinedValue, underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[] { new SalaryStructureLine(bonus.Id, 0) });

        Assert.Throws<InvalidOperationException>(() => new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo));
    }

    // ---------------------------------------------------------------- On-Attendance pro-rating (hand-derived)

    [Fact]
    public void On_attendance_pro_rates_by_attended_units_over_basis()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        // ₹26,000 / month On-Attendance, 26-day basis → ₹1,000 per attended day.
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 26);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(26000m)) });
        // 24 present days (2 absent).
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, PeriodFrom, PeriodTo, 24m);

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(24000m), r.Lines.Single().Amount); // 26,000 × 24 / 26
    }

    [Fact]
    public void Full_attendance_pays_the_full_rate()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 26);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(26000m)) });
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, PeriodFrom, PeriodTo, 26m);

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(26000m), r.Lines.Single().Amount);
    }

    // ---------------------------------------------------------------- rounding

    [Fact]
    public void Normal_rounding_rounds_to_the_nearest_rupee()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        // 10,000 × 11 / 30 = 3,666.666… → Normal (limit ₹1) rounds to ₹3,667.
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 30,
            roundingMethod: PayHeadRoundingMethod.Normal, roundingLimit: new Money(1m));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(10000m)) });
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, PeriodFrom, PeriodTo, 11m);

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(3667m), r.Lines.Single().Amount);
    }

    [Fact]
    public void Not_applicable_rounding_snaps_to_the_paisa()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 30);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(10000m)) });
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, PeriodFrom, PeriodTo, 11m);

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(3666.67m), r.Lines.Single().Amount); // 3,666.666… → 3,666.67
    }

    // ---------------------------------------------------------------- Tally-faithful slab bands

    [Fact]
    public void Percentage_slab_with_an_upper_bound_is_marginal_capped_to_the_band()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        // "12% of Basic, up to a ₹15,000 wage ceiling" → 12% × min(Basic, 15,000).
        var capped = ph.CreatePayHead("Capped PF", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue,
            underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { new PayHeadComputationSlab(PayHeadComputationSlabType.Percentage, rateBasisPoints: 1200, toAmount: new Money(15000m)) }));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(capped.Id, 1),
        });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(1800m), r.Lines.Single(l => l.PayHead.Name == "Capped PF").Amount); // 12% of 15,000
    }

    [Theory]
    [InlineData(6000, 0)]     // ≤ 7,500 → Nil
    [InlineData(8000, 175)]   // 7,501–10,000 → ₹175
    [InlineData(30000, 200)]  // > 10,000 → ₹200
    public void Value_slabs_select_the_single_matching_band(decimal basic, decimal expected)
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basicHead = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        // A Maharashtra-style value-slab schedule (band-selection, not marginal).
        var pt = ph.CreatePayHead("PT", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue,
            underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basicHead.Id) },
                new[]
                {
                    PayHeadComputationSlab.FlatValue(Money.Zero, toAmount: new Money(7500m)),
                    PayHeadComputationSlab.FlatValue(new Money(175m), fromAmount: new Money(7500m), toAmount: new Money(10000m)),
                    PayHeadComputationSlab.FlatValue(new Money(200m), fromAmount: new Money(10000m)),
                }));
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basicHead.Id, 0, new Money(basic)),
            new SalaryStructureLine(pt.Id, 1),
        });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(expected), r.Lines.Single(l => l.PayHead.Name == "PT").Amount);
    }

    // ---------------------------------------------------------------- dependency-order evaluation

    [Fact]
    public void Computed_heads_resolve_out_of_structure_order_and_chained()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var da = ph.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) })); // 40% of Basic
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id), new PayHeadComputationComponent(da.Id) },
                new[] { PayHeadComputationSlab.Percentage(2000) })); // 20% of (Basic + DA)
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        // Structure lists HRA and DA BEFORE Basic — computation must resolve regardless of order.
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(hra.Id, 0),
            new SalaryStructureLine(da.Id, 1),
            new SalaryStructureLine(basic.Id, 2, new Money(50000m)),
        });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(20000m), r.Lines.Single(l => l.PayHead.Name == "DA").Amount);  // 40% of 50,000
        Assert.Equal(new Money(14000m), r.Lines.Single(l => l.PayHead.Name == "HRA").Amount); // 20% of 70,000
        Assert.Equal(new Money(84000m), r.GrossEarnings);
    }

    // ---------------------------------------------------------------- structure-in-force resolution

    [Fact]
    public void Employee_structure_overrides_group_structure_and_uses_the_dated_revision()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var grp = Group(c, pay);
        var emp = pay.CreateEmployee("E1", grp);
        var ss = new SalaryStructureService(c);
        // A group structure (should be overridden by the employee one).
        ss.DefineForGroup(grp, PeriodFrom, new[] { new SalaryStructureLine(basic.Id, 0, new Money(10000m)) });
        // Two dated employee revisions.
        ss.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1), new[] { new SalaryStructureLine(basic.Id, 0, new Money(30000m)) });
        ss.DefineForEmployee(emp.Id, new DateOnly(2025, 6, 1), new[] { new SalaryStructureLine(basic.Id, 0, new Money(36000m)) });

        var svc = new PayrollComputationService(c);
        Assert.Equal(new Money(30000m), svc.Compute(emp.Id, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).GrossEarnings);
        Assert.Equal(new Money(36000m), svc.Compute(emp.Id, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)).GrossEarnings);
    }

    [Fact]
    public void No_structure_in_force_throws()
    {
        var c = Seed();
        var (pay, _) = MinimalPayroll(c);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        Assert.Throws<InvalidOperationException>(() => new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo));
    }

    // ---------------------------------------------------------------- affect-net-salary flag (F1)

    [Fact]
    public void Non_affecting_earning_and_deduction_are_computed_but_excluded_from_aggregates()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        // A tracked-but-not-paid earning (₹5,000) and deduction (₹1,000), both with Affect-Net-Salary OFF.
        var perk = ph.CreatePayHead("Notional Perk", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), affectsNetSalary: false);
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate,
            underGroupId: CurrentLiabilities(c));
        var notionalDed = ph.CreatePayHead("Notional Deduction", PayHeadType.Deductions, PayHeadCalculationType.FlatRate,
            underGroupId: CurrentLiabilities(c), affectsNetSalary: false);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(perk.Id, 1, new Money(5000m)),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
            new SalaryStructureLine(notionalDed.Id, 3, new Money(1000m)),
        });

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);

        // Gross excludes the non-affecting perk; deductions exclude the non-affecting deduction.
        Assert.Equal(new Money(30000m), r.GrossEarnings);    // Basic only (perk excluded)
        Assert.Equal(new Money(2000m), r.TotalDeductions);   // Advance only (notional excluded)
        Assert.Equal(new Money(28000m), r.NetPayable);       // 30,000 − 2,000
        // But both non-affecting heads are still recorded as computed lines (visible for the payslip / later slices).
        Assert.Equal(new Money(5000m), r.Lines.Single(l => l.PayHead.Name == "Notional Perk").Amount);
        Assert.Equal(new Money(1000m), r.Lines.Single(l => l.PayHead.Name == "Notional Deduction").Amount);
    }

    // ---------------------------------------------------------------- On-Attendance boundary clipping (F3)

    [Fact]
    public void On_attendance_clips_a_straddling_weekly_entry_to_each_period_share()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        // ₹7,000 / month On-Attendance, 7-day basis → ₹1,000 per attended day (clean hand-derived numbers).
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 7);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(head.Id, 0, new Money(7000m)) });
        // ONE weekly entry (7 present days) straddling the April/May boundary: 2025-04-28..2025-05-04
        // (3 days in April: 28,29,30; 4 days in May: 1,2,3,4).
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, new DateOnly(2025, 4, 28), new DateOnly(2025, 5, 4), 7m);

        var svc = new PayrollComputationService(c);
        // April: 3/7 of the 7 days = 3 attended → 7,000 × 3 / 7 = ₹3,000.
        Assert.Equal(new Money(3000m), svc.Compute(emp.Id, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)).Lines.Single().Amount);
        // May: 4/7 = 4 attended → 7,000 × 4 / 7 = ₹4,000. The same record counts its share in BOTH periods.
        Assert.Equal(new Money(4000m), svc.Compute(emp.Id, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31)).Lines.Single().Amount);
    }

    [Fact]
    public void On_attendance_leaves_a_fully_contained_entry_unchanged()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 26);
        var emp = pay.CreateEmployee("E1", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(26000m)) });
        // A record wholly inside the April period (24 of 26 present days) contributes its whole value — no pro-rating.
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, new DateOnly(2025, 4, 5), new DateOnly(2025, 4, 20), 24m);

        var r = new PayrollComputationService(c).Compute(emp.Id, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(24000m), r.Lines.Single().Amount); // 26,000 × 24 / 26, entry counted in full
    }

    // ---------------------------------------------------------------- harness

    /// <summary>Builds the hand-derived golden company and returns (company, employeeId): Basic ₹30,000 flat +
    /// HRA 40%-of-Basic (₹12,000) + a flat ₹2,000 advance recovery.</summary>
    private static (Company Company, Guid EmployeeId) BuildGoldenCompany()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));   // 40% of Basic
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate,
            underGroupId: CurrentLiabilities(c));

        var emp = pay.CreateEmployee("Rajkumar Sharma", Group(c, pay));
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        return (c, emp.Id);
    }

    private static (PayrollService Pay, PayHeadService PayHead) MinimalPayroll(Company c)
    {
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        return (pay, new PayHeadService(c));
    }

    private static Guid Group(Company c, PayrollService pay)
    {
        var existing = c.FindEmployeeGroupByName("Staff");
        return existing?.Id ?? pay.CreateEmployeeGroup("Staff").Id;
    }
}
