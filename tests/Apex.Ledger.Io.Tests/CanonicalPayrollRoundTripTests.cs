using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-1 <b>Io fold-in</b> gate (PR-1 losslessness): a company with Payroll enabled — the two F11
/// flags, employee categories, a hierarchical employee-group tree, simple + compound payroll units, hierarchical
/// attendance types (with kind + unit link) and an employee carrying every identity/statutory/bank field + tax
/// regime — <b>exports and re-imports paisa + count exact in JSON AND XML</b>, both byte-stable and into a fresh
/// (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/>. This is the exact
/// Phase-6 carry-forward defect class (new masters silently dropped on export/import), built in here rather than
/// deferred.
/// </summary>
public class CanonicalPayrollRoundTripTests
{
    private const string ValidPan = "AAPFU0939F";
    private const string ValidUan = "100200300400";
    private const string ValidEsi = "3100123456";

    private static Company BuildPayrollCompany()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var svc = new PayrollService(c);
        svc.EnablePayroll();

        var direct = svc.CreateEmployeeCategory("Direct");   // default: revenue only
        svc.CreateEmployeeCategory("Indirect",
            allocateRevenueItems: false, allocateNonRevenueItems: true);   // non-default allocation axis
        var admin = svc.CreateEmployeeGroup("Administration");
        var mkt = svc.CreateEmployeeGroup("Marketing", parentId: admin.Id, alias: "MKT", defineSalaryDetails: true);

        var days = svc.CreateSimplePayrollUnit("Days", "Days");
        var min = svc.CreateSimplePayrollUnit("Min", "Minutes");
        var hrs = svc.CreateSimplePayrollUnit("Hrs", "Hours", decimalPlaces: 2);
        svc.CreateCompoundPayrollUnit("Hrs of 60 Min", "Hours of 60 Minutes", hrs.Id, min.Id, 60);

        var present = svc.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        svc.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay, parentId: present.Id);
        svc.CreateAttendanceType("Overtime", AttendanceTypeKind.Production, payrollUnitId: hrs.Id);

        var e = svc.CreateEmployee("Rajkumar Sharma", mkt.Id, employeeCategoryId: direct.Id,
            employeeNumber: "EMP-001", pan: ValidPan, uan: ValidUan, esiNumber: ValidEsi,
            dateOfJoining: new DateOnly(2015, 4, 1));
        e.Designation = "Manager";
        e.Function = "Sales";
        e.Location = "Mumbai";
        e.Gender = "Male";
        e.DateOfBirth = new DateOnly(1985, 6, 15);
        e.PfAccountNumber = "MH/BAN/1234567/000/0001234";
        e.Aadhaar = "123412341234";
        e.BankAccountNumber = "1234567890";
        e.BankName = "State Bank";
        e.BankIfsc = "SBIN0001234";
        e.ApplicableTaxRegime = TaxRegime.Old;

        return c;
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable_and_carries_v30_schema()
    {
        var c = BuildPayrollCompany();
        var first = CanonicalJson.Export(c);
        Assert.Contains($"\"schemaVersion\": {CanonicalMapper.SchemaVersion}", Encoding.UTF8.GetString(first));

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildPayrollCompany();
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
        var c = BuildPayrollCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Payroll_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildPayrollCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertReconciles(source, fresh);
        }
    }

    [Fact]
    public void Company_without_payroll_serialises_without_payroll_masters()
    {
        // ER-13: a company that never enables Payroll carries neither flag on nor any payroll master in the payload.
        var c = FreshTarget();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.False(model!.Company.PayrollEnabled);
        Assert.False(model.Company.PayrollStatutoryEnabled);
        Assert.Empty(model.Payload.Employees);
        Assert.Empty(model.Payload.EmployeeGroups);
        Assert.Empty(model.Payload.EmployeeCategories);
        Assert.Empty(model.Payload.PayrollUnits);
        Assert.Empty(model.Payload.AttendanceTypes);
    }

    private static void AssertProjection(CanonicalModel model)
    {
        Assert.True(model.Company.PayrollEnabled);
        Assert.True(model.Company.PayrollStatutoryEnabled);

        Assert.Equal(2, model.Payload.EmployeeCategories.Count);
        var directDto = model.Payload.EmployeeCategories.Single(x => x.Name == "Direct");
        var indirectDto = model.Payload.EmployeeCategories.Single(x => x.Name == "Indirect");
        Assert.True(directDto.AllocateRevenueItems);
        Assert.False(directDto.AllocateNonRevenueItems);
        Assert.False(indirectDto.AllocateRevenueItems);
        Assert.True(indirectDto.AllocateNonRevenueItems);
        Assert.Equal(2, model.Payload.EmployeeGroups.Count);
        Assert.Equal(4, model.Payload.PayrollUnits.Count);
        Assert.Equal(3, model.Payload.AttendanceTypes.Count);

        var mkt = model.Payload.EmployeeGroups.Single(g => g.Name == "Marketing");
        var admin = model.Payload.EmployeeGroups.Single(g => g.Name == "Administration");
        Assert.Equal(admin.Id, mkt.ParentId);
        Assert.True(mkt.DefineSalaryDetails);

        var compound = model.Payload.PayrollUnits.Single(u => u.IsCompound);
        var hrs = model.Payload.PayrollUnits.Single(u => u.Symbol == "Hrs");
        Assert.Equal(hrs.Id, compound.FirstUnitId);
        Assert.Equal(60, compound.ConversionNumerator);
        Assert.Equal(2, hrs.DecimalPlaces);

        var overtime = model.Payload.AttendanceTypes.Single(a => a.Name == "Overtime");
        Assert.Equal("Production", overtime.Kind);
        Assert.Equal(hrs.Id, overtime.PayrollUnitId);

        var emp = Assert.Single(model.Payload.Employees);
        Assert.Equal("Rajkumar Sharma", emp.Name);
        Assert.Equal(mkt.Id, emp.EmployeeGroupId);
        var direct = model.Payload.EmployeeCategories.Single(x => x.Name == "Direct");
        Assert.Equal(direct.Id, emp.EmployeeCategoryId);
        Assert.Equal(ValidPan, emp.Pan);
        Assert.Equal(ValidUan, emp.Uan);
        Assert.Equal(ValidEsi, emp.EsiNumber);
        Assert.Equal("2015-04-01", emp.DateOfJoining);
        Assert.Equal("Old", emp.ApplicableTaxRegime);
    }

    private static void AssertReconciles(Company source, Company target)
    {
        Assert.True(target.PayrollEnabled);
        Assert.True(target.PayrollStatutoryEnabled);

        // Masters reconcile count-exact.
        Assert.Equal(source.EmployeeCategories.Count, target.EmployeeCategories.Count);
        var tIndirect = target.FindEmployeeCategoryByName("Indirect")!;
        Assert.False(tIndirect.AllocateRevenueItems);
        Assert.True(tIndirect.AllocateNonRevenueItems);
        Assert.Equal(source.EmployeeGroups.Count, target.EmployeeGroups.Count);
        Assert.Equal(source.PayrollUnits.Count, target.PayrollUnits.Count);
        Assert.Equal(source.AttendanceTypes.Count, target.AttendanceTypes.Count);
        Assert.Equal(source.Employees.Count, target.Employees.Count);

        // Group hierarchy resolves to the target's own re-minted ids (by name across companies).
        var admin = target.FindEmployeeGroupByName("Administration")!;
        var mkt = target.FindEmployeeGroupByName("Marketing")!;
        Assert.Equal(admin.Id, mkt.ParentId);
        Assert.Equal("MKT", mkt.Alias);
        Assert.True(mkt.DefineSalaryDetails);

        // Compound payroll unit's components re-map to the target's re-minted simple units.
        var hrs = target.FindPayrollUnitByName("Hrs")!;
        var min = target.FindPayrollUnitByName("Min")!;
        var compound = target.FindPayrollUnitByName("Hrs of 60 Min")!;
        Assert.True(compound.IsCompound);
        Assert.Equal(hrs.Id, compound.FirstUnitId);
        Assert.Equal(min.Id, compound.TailUnitId);
        Assert.Equal(60, compound.ConversionNumerator);
        Assert.Equal(2, hrs.DecimalPlaces);

        // Attendance types re-map their parent + unit refs.
        var present = target.FindAttendanceTypeByName("Present")!;
        var absent = target.FindAttendanceTypeByName("Absent")!;
        var overtime = target.FindAttendanceTypeByName("Overtime")!;
        var days = target.FindPayrollUnitByName("Days")!;
        Assert.Equal(present.Id, absent.ParentId);
        Assert.Equal(days.Id, present.PayrollUnitId);
        Assert.Equal(hrs.Id, overtime.PayrollUnitId);
        Assert.Equal(AttendanceTypeKind.Production, overtime.Kind);

        // The employee re-maps its group + category and preserves every field (paisa/count exact — no silent drop).
        var e = Assert.Single(target.Employees);
        var direct = target.FindEmployeeCategoryByName("Direct")!;
        Assert.Equal("Rajkumar Sharma", e.Name);
        Assert.Equal(mkt.Id, e.EmployeeGroupId);
        Assert.Equal(direct.Id, e.EmployeeCategoryId);
        Assert.Equal("EMP-001", e.EmployeeNumber);
        Assert.Equal(new DateOnly(2015, 4, 1), e.DateOfJoining);
        Assert.Equal("Manager", e.Designation);
        Assert.Equal("Sales", e.Function);
        Assert.Equal("Mumbai", e.Location);
        Assert.Equal("Male", e.Gender);
        Assert.Equal(new DateOnly(1985, 6, 15), e.DateOfBirth);
        Assert.Equal(ValidPan, e.Pan);
        Assert.Equal("123412341234", e.Aadhaar);
        Assert.Equal(ValidUan, e.Uan);
        Assert.Equal("MH/BAN/1234567/000/0001234", e.PfAccountNumber);
        Assert.Equal(ValidEsi, e.EsiNumber);
        Assert.Equal("1234567890", e.BankAccountNumber);
        Assert.Equal("State Bank", e.BankName);
        Assert.Equal("SBIN0001234", e.BankIfsc);
        Assert.Equal(TaxRegime.Old, e.ApplicableTaxRegime);
    }

    private static void AssertNoTally(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }
}
