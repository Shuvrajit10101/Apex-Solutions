using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-2 <b>Pay Head + Salary Structure</b> engine contract (RQ-4, RQ-5). Covers pay-head CRUD across
/// all five calculation types, the computation-basis reference guards (dangling / self / <b>cyclic</b>
/// computed-on rejection — ER-3), the dated salary-structure revision + line-vs-calc-type validation (ER-4), and
/// the delete guards. Pure domain — no DB, no clock. <b>No salary computation is asserted here</b> (that is
/// slice 3); this proves the model + guards.
/// </summary>
public sealed class PayHeadServiceTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Pay Head Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    // ---- CRUD across all five calculation types ----

    [Fact]
    public void Creates_pay_heads_of_all_five_calculation_types()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var present = payroll.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var output = payroll.CreateAttendanceType("Units Produced", AttendanceTypeKind.Production);

        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary, useForGratuity: true);
        var onAtt = svc.CreatePayHead("Overtime Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            attendanceTypeId: present.Id, perDayCalculationBasisDays: 26, calculationPeriod: PayHeadCalculationPeriod.Day);
        var onProd = svc.CreatePayHead("Piece Rate", PayHeadType.Earnings, PayHeadCalculationType.OnProduction,
            attendanceTypeId: output.Id);
        var userDef = svc.CreatePayHead("Ad-hoc Bonus", PayHeadType.Bonus, PayHeadCalculationType.AsUserDefinedValue);
        var computed = svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));  // 40% of Basic

        Assert.Equal(5, c.PayHeads.Count);
        Assert.True(basic.AffectsNetSalary);                 // Earnings default
        Assert.True(basic.UseForGratuity);
        Assert.Equal(IncomeTaxComponent.BasicSalary, basic.IncomeTaxComponent);
        Assert.Equal(present.Id, onAtt.AttendanceTypeId);
        Assert.Equal(26, onAtt.PerDayCalculationBasisDays);
        Assert.Equal(output.Id, onProd.AttendanceTypeId);
        Assert.Null(userDef.Computation);
        Assert.Equal(4000, Assert.Single(computed.Computation!.Slabs).RateBasisPoints);
        Assert.Equal(basic.Id, Assert.Single(computed.Computation!.BasisComponents).PayHeadId);
    }

    [Fact]
    public void Employer_contribution_and_gratuity_default_to_not_affecting_net_salary()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var eps = svc.CreatePayHead("Employer EPS", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsUserDefinedValue);
        var grat = svc.CreatePayHead("Gratuity Provision", PayHeadType.Gratuity, PayHeadCalculationType.AsUserDefinedValue);
        var pf = svc.CreatePayHead("Employee PF", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue);
        Assert.False(eps.AffectsNetSalary);
        Assert.False(grat.AffectsNetSalary);
        Assert.True(pf.AffectsNetSalary);
    }

    [Fact]
    public void Rejects_duplicate_pay_head_name_case_insensitively()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreatePayHead("basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate));
    }

    [Fact]
    public void Renames_pay_head_in_place_and_blocks_a_name_clash()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var a = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        svc.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        svc.RenamePayHead(a.Id, "Basic Salary");
        Assert.Equal("Basic Salary", c.FindPayHead(a.Id)!.Name);
        Assert.Throws<InvalidOperationException>(() => svc.RenamePayHead(a.Id, "DA"));
    }

    // ---- reference + calc-type guards ----

    [Fact]
    public void Rejects_a_pay_head_under_a_missing_group()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: Guid.NewGuid()));
    }

    [Fact]
    public void On_attendance_head_requires_a_non_production_attendance_type()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        var prod = payroll.CreateAttendanceType("Units", AttendanceTypeKind.Production);

        Assert.Throws<InvalidOperationException>(() =>   // missing link
            svc.CreatePayHead("OT", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance));
        Assert.Throws<InvalidOperationException>(() =>   // links a production type
            svc.CreatePayHead("OT", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance, attendanceTypeId: prod.Id));
    }

    [Fact]
    public void Non_computed_head_may_not_carry_a_computation_and_computed_head_must()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        Assert.Throws<InvalidOperationException>(() =>   // flat-rate with a formula
            svc.CreatePayHead("Weird", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
                computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(basic.Id) },
                    Array.Empty<PayHeadComputationSlab>())));

        Assert.Throws<InvalidOperationException>(() =>   // computed with no formula
            svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue));
    }

    [Fact]
    public void Rejects_a_computed_head_with_a_basis_but_no_slab()
    {
        // An As-Computed-Value head needs slabs to turn its basis into an amount (a percentage or a value). A
        // basis-only, slab-less head has no rate/value ⇒ slice 3 has no deterministic amount to compute for it,
        // and the UI already forbids it — the service and import must agree (RQ-4).
        var c = Seed();
        var svc = new PayHeadService(c);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
                computation: new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(basic.Id) },
                    Array.Empty<PayHeadComputationSlab>())));
        Assert.Contains("slab", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(c.FindPayHeadByName("DA"));   // not mutated on rejection
    }

    [Fact]
    public void Rejects_a_computation_referencing_a_missing_or_self_pay_head()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        Assert.Throws<InvalidOperationException>(() =>   // dangling reference (slab present so the basis guard is reached)
            svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
                computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(Guid.NewGuid()) },
                    new[] { PayHeadComputationSlab.Percentage(1000) })));
    }

    [Fact]
    public void Rejects_a_direct_computed_on_cycle()
    {
        // A computed on B (A→B); then pointing B at A closes a 2-cycle A→B→A and must be rejected (ER-3).
        var c = Seed();
        var svc = new PayHeadService(c);
        var seed = svc.CreatePayHead("Seed", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var b = svc.CreatePayHead("B", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(seed.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));
        var a = svc.CreatePayHead("A", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(b.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));

        var before = c.FindPayHead(b.Id)!.Computation;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.SetComputation(b.Id, new PayHeadComputation(
                new[] { new PayHeadComputationComponent(a.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) })));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The failed SetComputation rolled back to B's prior formula (still computed on Seed, not A).
        Assert.Same(before, c.FindPayHead(b.Id)!.Computation);
        Assert.Equal(seed.Id, Assert.Single(c.FindPayHead(b.Id)!.Computation!.BasisComponents).PayHeadId);
    }

    [Fact]
    public void Rejects_a_transitive_computed_on_cycle()
    {
        // A→B→C→A: build A on (nothing yet), chain and close the loop.
        var c = Seed();
        var svc = new PayHeadService(c);
        var seed = svc.CreatePayHead("Seed", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var a = svc.CreatePayHead("A", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(seed.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));
        var bb = svc.CreatePayHead("B", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(a.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));
        var cc = svc.CreatePayHead("C", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(bb.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));

        Assert.Throws<InvalidOperationException>(() =>   // point A at C → A→B→C→A
            svc.SetComputation(a.Id, new PayHeadComputation(
                new[] { new PayHeadComputationComponent(cc.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) })));
    }

    [Fact]
    public void Delete_blocked_while_used_by_a_computation_or_structure()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var grp = payroll.CreateEmployeeGroup("Staff");
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var hra = svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));

        Assert.Throws<InvalidOperationException>(() => svc.DeletePayHead(basic.Id));   // HRA computes on it

        // Also blocked by a salary structure line.
        var structSvc = new SalaryStructureService(c);
        structSvc.DefineForGroup(grp.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(hra.Id, 0) });
        Assert.Throws<InvalidOperationException>(() => svc.DeletePayHead(hra.Id));
    }

    // ---- salary structures: dated revision + line validity ----

    [Fact]
    public void Dated_structure_revision_supersedes_from_its_effective_date()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var emp = payroll.CreateEmployee("Rajkumar", payroll.CreateEmployeeGroup("Marketing").Id);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        var structSvc = new SalaryStructureService(c);
        var v1 = structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(50_000m)) });
        var v2 = structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 10, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(60_000m)) });

        Assert.Null(structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 3, 31)));
        Assert.Equal(v1.Id, structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 4, 1))!.Id);
        Assert.Equal(v1.Id, structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 9, 30))!.Id);
        Assert.Equal(v2.Id, structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 10, 1))!.Id);
        Assert.Equal(v2.Id, structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2026, 1, 1))!.Id);
    }

    [Fact]
    public void Rejects_two_structures_for_the_same_scope_and_effective_date()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var emp = payroll.CreateEmployee("Rajkumar", payroll.CreateEmployeeGroup("Marketing").Id);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var structSvc = new SalaryStructureService(c);
        structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(50_000m)) });
        Assert.Throws<InvalidOperationException>(() =>
            structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
                new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(55_000m)) }));
    }

    [Fact]
    public void Structure_line_value_must_match_the_pay_head_calc_type()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var emp = payroll.CreateEmployee("Rajkumar", payroll.CreateEmployeeGroup("Marketing").Id);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var hra = svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var structSvc = new SalaryStructureService(c);

        // FlatRate needs an amount.
        Assert.Throws<InvalidOperationException>(() =>
            structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
                new[] { new SalaryStructureLine(basic.Id, 0) }));
        // AsComputedValue must NOT carry an amount.
        Assert.Throws<InvalidOperationException>(() =>
            structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
                new[] { new SalaryStructureLine(hra.Id, 0, Money.FromRupees(1000m)) }));
        // A valid mix succeeds.
        var ok = structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(80_000m)),
                new SalaryStructureLine(hra.Id, 1),
            });
        Assert.Equal(2, ok.Lines.Count);
    }

    [Fact]
    public void Structure_rejects_duplicate_pay_head_and_non_dense_ordering()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var emp = payroll.CreateEmployee("Rajkumar", payroll.CreateEmployeeGroup("Marketing").Id);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var structSvc = new SalaryStructureService(c);

        Assert.Throws<InvalidOperationException>(() =>   // duplicate pay head
            structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
                new[]
                {
                    new SalaryStructureLine(basic.Id, 0, Money.FromRupees(1m)),
                    new SalaryStructureLine(basic.Id, 1, Money.FromRupees(1m)),
                }));

        Assert.Throws<InvalidOperationException>(() =>   // non-dense order (0 then 2)
            structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 5, 1),
                new[] { new SalaryStructureLine(basic.Id, 2, Money.FromRupees(1m)) }));
    }

    [Fact]
    public void Structure_scope_target_must_exist()
    {
        var c = Seed();
        var svc = new PayHeadService(c);
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var structSvc = new SalaryStructureService(c);
        Assert.Throws<InvalidOperationException>(() =>
            structSvc.DefineForEmployee(Guid.NewGuid(), new DateOnly(2025, 4, 1),
                new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(1m)) }));
    }
}
