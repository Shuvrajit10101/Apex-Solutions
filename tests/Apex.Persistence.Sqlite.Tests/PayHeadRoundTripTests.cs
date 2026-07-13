using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-8 slice-2 <b>Pay Heads + dated Salary Structures</b> persistence contract: a company with pay heads of
/// every calculation type (including an As-Computed-Value head with a computed-on basis + a percentage slab and a
/// slabbed value head) and dated salary structures (a revision + lines carrying per-employee amounts) SAVES and
/// RELOADS at <see cref="Schema.CurrentVersion"/>, preserving every field, computation component/slab, line and
/// paisa amount; and a company that never uses Payroll reloads with no pay heads / structures (ER-13).
/// </summary>
public sealed class PayHeadRoundTripTests
{
    private static (Company Company, Guid BasicId, Guid HraId, Guid PtId, Guid OtId, Guid EmpId, Guid GrpId) Build()
    {
        var c = CompanyFactory.CreateSeeded("PayHead Persist Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var grp = payroll.CreateEmployeeGroup("Marketing");
        var emp = payroll.CreateEmployee("Rajkumar Sharma", grp.Id);
        var present = payroll.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);

        var svc = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var duties = c.FindGroupByName("Duties & Taxes")!.Id;

        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.BasicSalary, useForGratuity: true,
            roundingMethod: PayHeadRoundingMethod.Normal, roundingLimit: Money.FromRupees(1m));
        var da = svc.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: indirect, useForGratuity: true,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));      // DA = 40% of Basic
        var hra = svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.HouseRentAllowance,
            computation: new PayHeadComputation(
                new[]
                {
                    new PayHeadComputationComponent(basic.Id),
                    new PayHeadComputationComponent(da.Id),
                },
                new[] { PayHeadComputationSlab.Percentage(2000) }));      // HRA = 20% of (Basic + DA)
        var pt = svc.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue,
            underGroupId: duties,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id), new PayHeadComputationComponent(da.Id) },
                new[]
                {
                    PayHeadComputationSlab.FlatValue(Money.Zero, toAmount: Money.FromRupees(10_000m)),
                    PayHeadComputationSlab.FlatValue(Money.FromRupees(200m), fromAmount: Money.FromRupees(10_000m)),
                }));
        var ot = svc.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: indirect, attendanceTypeId: present.Id, perDayCalculationBasisDays: 26,
            calculationPeriod: PayHeadCalculationPeriod.Day);

        var structSvc = new SalaryStructureService(c);
        structSvc.DefineForGroup(grp.Id, new DateOnly(2025, 4, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(70_000m)),
                new SalaryStructureLine(da.Id, 1),
                new SalaryStructureLine(hra.Id, 2),
            }, SalaryStructureStartType.StartAfresh);
        // Employee-level dated revision (a raise from October), copied from parent.
        structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(80_000m)),
                new SalaryStructureLine(da.Id, 1),
                new SalaryStructureLine(hra.Id, 2),
                new SalaryStructureLine(pt.Id, 3),
                new SalaryStructureLine(ot.Id, 4, Money.FromRupees(500m)),
            }, SalaryStructureStartType.CopyFromParent);
        structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 10, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(90_000m)),
                new SalaryStructureLine(da.Id, 1),
            }, SalaryStructureStartType.StartAfresh);

        return (c, basic.Id, hra.Id, pt.Id, ot.Id, emp.Id, grp.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Pay_heads_and_structures_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-payhead-rt-{Guid.NewGuid():N}.db");
        try
        {
            var (original, basicId, hraId, ptId, otId, empId, grpId) = Build();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 31);
            }

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            Assert.Equal(5, r.PayHeads.Count);

            var basic = r.FindPayHead(basicId)!;
            Assert.Equal(PayHeadCalculationType.FlatRate, basic.CalculationType);
            Assert.Equal(IncomeTaxComponent.BasicSalary, basic.IncomeTaxComponent);
            Assert.True(basic.UseForGratuity);
            Assert.Equal(PayHeadRoundingMethod.Normal, basic.RoundingMethod);
            Assert.Equal(Money.FromRupees(1m), basic.RoundingLimit);
            Assert.NotNull(basic.UnderGroupId);
            Assert.Null(basic.Computation);

            var hra = r.FindPayHead(hraId)!;
            Assert.Equal(PayHeadCalculationType.AsComputedValue, hra.CalculationType);
            Assert.Equal(2, hra.Computation!.BasisComponents.Count);
            Assert.Equal(basicId, hra.Computation!.BasisComponents[0].PayHeadId);   // order preserved
            Assert.Equal(2000, Assert.Single(hra.Computation!.Slabs).RateBasisPoints);

            var pt = r.FindPayHead(ptId)!;
            Assert.Equal(2, pt.Computation!.Slabs.Count);
            var band0 = pt.Computation!.Slabs[0];
            var band1 = pt.Computation!.Slabs[1];
            Assert.Equal(PayHeadComputationSlabType.FlatValue, band1.SlabType);
            Assert.Equal(Money.FromRupees(200m), band1.Value);
            Assert.Equal(Money.FromRupees(10_000m), band0.ToAmount);
            Assert.Equal(Money.FromRupees(10_000m), band1.FromAmount);
            Assert.Null(band1.ToAmount);

            var ot = r.FindPayHead(otId)!;
            Assert.Equal(PayHeadCalculationType.OnAttendance, ot.CalculationType);
            Assert.Equal(26, ot.PerDayCalculationBasisDays);
            Assert.Equal(PayHeadCalculationPeriod.Day, ot.CalculationPeriod);
            Assert.NotNull(ot.AttendanceTypeId);

            // Salary structures: 1 group + 2 employee versions; the employee revision resolves by date.
            Assert.Equal(3, r.SalaryStructures.Count);
            var structSvc = new SalaryStructureService(r);
            var aprEmp = structSvc.InForceOn(SalaryStructureScope.Employee, empId, new DateOnly(2025, 9, 30))!;
            var octEmp = structSvc.InForceOn(SalaryStructureScope.Employee, empId, new DateOnly(2025, 10, 1))!;
            Assert.Equal(5, aprEmp.Lines.Count);
            Assert.Equal(Money.FromRupees(80_000m), aprEmp.Lines[0].Amount);
            Assert.Equal(SalaryStructureStartType.CopyFromParent, aprEmp.StartType);
            Assert.Null(aprEmp.Lines[1].Amount);                                     // DA computed → no line amount
            Assert.Equal(Money.FromRupees(500m), aprEmp.Lines[4].Amount);            // OT rate
            Assert.Equal(2, octEmp.Lines.Count);
            Assert.Equal(Money.FromRupees(90_000m), octEmp.Lines[0].Amount);

            var grp = structSvc.InForceOn(SalaryStructureScope.EmployeeGroup, grpId, new DateOnly(2025, 4, 1))!;
            Assert.Equal(3, grp.Lines.Count);
            Assert.Equal(Money.FromRupees(70_000m), grp.Lines[0].Amount);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Company_without_payroll_reloads_with_no_pay_heads_or_structures()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-payhead-off-{Guid.NewGuid():N}.db");
        try
        {
            var original = CompanyFactory.CreateSeeded("No Payroll Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            Assert.Empty(r.PayHeads);
            Assert.Empty(r.SalaryStructures);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
