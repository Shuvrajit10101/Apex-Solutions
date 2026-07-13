using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-2 <b>Io fold-in</b> gate (PR-2 losslessness): a company with pay heads of every calculation type
/// (including As-Computed-Value heads carrying a computed-on basis + percentage/slab bands) and dated salary
/// structures (a revision + lines with per-employee amounts) <b>exports and re-imports paisa + count exact in
/// JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company through the engine-routed
/// <see cref="CompanyImportService"/>, with the computed-on and structure-line pay-head references re-mapped
/// correctly. No "Tally" appears in the payload; a payroll-off company drops nothing (ER-13).
/// </summary>
public class CanonicalPayHeadRoundTripTests
{
    private static Company Build()
    {
        var c = CompanyFactory.CreateSeeded("PayHead Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
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
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        svc.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.HouseRentAllowance,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id), new PayHeadComputationComponent(da.Id) },
                new[] { PayHeadComputationSlab.Percentage(2000) }));
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
            });
        structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(80_000m)),
                new SalaryStructureLine(da.Id, 1),
                new SalaryStructureLine(pt.Id, 2),
                new SalaryStructureLine(ot.Id, 3, Money.FromRupees(500m)),
            }, SalaryStructureStartType.CopyFromParent);
        structSvc.DefineForEmployee(emp.Id, new DateOnly(2025, 10, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(90_000m)) });

        return c;
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable_and_carries_v31_schema()
    {
        var c = Build();
        var first = CanonicalJson.Export(c);
        Assert.Contains($"\"schemaVersion\": {CanonicalMapper.SchemaVersion}", Encoding.UTF8.GetString(first));

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));   // byte-for-byte
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = Build();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_payroll_payload()
    {
        var c = Build();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = Build();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertReconciles(fresh);
        }
    }

    [Fact]
    public void Company_without_payroll_serialises_without_pay_heads_or_structures()
    {
        var c = FreshTarget();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.PayHeads);
        Assert.Empty(model.Payload.SalaryStructures);
    }

    private static void AssertProjection(CanonicalModel model)
    {
        Assert.Equal(5, model.Payload.PayHeads.Count);
        var hra = model.Payload.PayHeads.Single(p => p.Name == "HRA");
        Assert.Equal("AsComputedValue", hra.CalculationType);
        Assert.Equal(2, hra.ComputationComponents.Count);
        Assert.Equal(2000, hra.ComputationSlabs.Single().RateBasisPoints);
        var basicDto = model.Payload.PayHeads.Single(p => p.Name == "Basic");
        Assert.Equal(hra.ComputationComponents[0].PayHeadId, basicDto.Id);   // computed-on references Basic's id

        var pt = model.Payload.PayHeads.Single(p => p.Name == "Professional Tax");
        Assert.Equal(2, pt.ComputationSlabs.Count);
        Assert.Equal(20000L, pt.ComputationSlabs[1].ValuePaisa);             // ₹200 = 20000 paisa
        Assert.Equal(1000000L, pt.ComputationSlabs[1].FromAmountPaisa);      // ₹10,000
        Assert.Null(pt.ComputationSlabs[1].ToAmountPaisa);

        Assert.Equal(3, model.Payload.SalaryStructures.Count);
        var groupStruct = model.Payload.SalaryStructures.Single(s => s.Scope == "EmployeeGroup");
        Assert.Equal(7000000L, groupStruct.Lines[0].AmountPaisa);            // ₹70,000
        Assert.Null(groupStruct.Lines[1].AmountPaisa);                       // DA computed → no amount
    }

    private static void AssertReconciles(Company target)
    {
        Assert.Equal(5, target.PayHeads.Count);
        Assert.Equal(3, target.SalaryStructures.Count);

        var basic = target.FindPayHeadByName("Basic")!;
        var da = target.FindPayHeadByName("DA")!;
        var hra = target.FindPayHeadByName("HRA")!;
        var pt = target.FindPayHeadByName("Professional Tax")!;
        var ot = target.FindPayHeadByName("Overtime")!;

        // Basic (flat-rate) fields + rounding.
        Assert.Equal(PayHeadCalculationType.FlatRate, basic.CalculationType);
        Assert.True(basic.UseForGratuity);
        Assert.Equal(PayHeadRoundingMethod.Normal, basic.RoundingMethod);
        Assert.Equal(Money.FromRupees(1m), basic.RoundingLimit);
        Assert.NotNull(basic.UnderGroupId);
        Assert.Equal("Indirect Expenses", target.FindGroup(basic.UnderGroupId!.Value)!.Name);

        // HRA computed on Basic + DA (re-mapped to the target's own ids), order preserved, 20% slab.
        Assert.Equal(2, hra.Computation!.BasisComponents.Count);
        Assert.Equal(basic.Id, hra.Computation!.BasisComponents[0].PayHeadId);
        Assert.Equal(da.Id, hra.Computation!.BasisComponents[1].PayHeadId);
        Assert.Equal(2000, hra.Computation!.Slabs.Single().RateBasisPoints);

        // PT slabbed value bands preserved to the paisa.
        Assert.Equal(2, pt.Computation!.Slabs.Count);
        Assert.Equal(Money.FromRupees(200m), pt.Computation!.Slabs[1].Value);
        Assert.Equal(Money.FromRupees(10_000m), pt.Computation!.Slabs[1].FromAmount);

        // On-Attendance head re-maps its attendance type.
        Assert.Equal(PayHeadCalculationType.OnAttendance, ot.CalculationType);
        Assert.Equal(26, ot.PerDayCalculationBasisDays);
        Assert.NotNull(ot.AttendanceTypeId);
        Assert.Equal("Present", target.FindAttendanceType(ot.AttendanceTypeId!.Value)!.Name);

        // Salary structures: the employee revision resolves by date and re-maps pay-head + scope refs.
        var emp = target.FindEmployeeByName("Rajkumar Sharma")!;
        var grp = target.FindEmployeeGroupByName("Marketing")!;
        var structSvc = new SalaryStructureService(target);
        var aprEmp = structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 9, 30))!;
        var octEmp = structSvc.InForceOn(SalaryStructureScope.Employee, emp.Id, new DateOnly(2025, 10, 1))!;
        Assert.Equal(4, aprEmp.Lines.Count);
        Assert.Equal(basic.Id, aprEmp.Lines[0].PayHeadId);
        Assert.Equal(Money.FromRupees(80_000m), aprEmp.Lines[0].Amount);
        Assert.Equal(pt.Id, aprEmp.Lines[2].PayHeadId);
        Assert.Equal(ot.Id, aprEmp.Lines[3].PayHeadId);
        Assert.Equal(Money.FromRupees(500m), aprEmp.Lines[3].Amount);
        Assert.Single(octEmp.Lines);
        Assert.Equal(Money.FromRupees(90_000m), octEmp.Lines[0].Amount);

        var groupStruct = structSvc.InForceOn(SalaryStructureScope.EmployeeGroup, grp.Id, new DateOnly(2025, 4, 1))!;
        Assert.Equal(2, groupStruct.Lines.Count);
        Assert.Equal(Money.FromRupees(70_000m), groupStruct.Lines[0].Amount);
    }

    private static void AssertNoTally(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }
}
