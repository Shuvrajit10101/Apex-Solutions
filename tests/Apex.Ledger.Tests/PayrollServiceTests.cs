using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-1 payroll-masters + F11-enable contract (RQ-1/RQ-2/RQ-3; ER-13). Covers: the idempotent
/// Enable Payroll / Enable Payroll Statutory toggles (off by default); create/alter/delete of the five payroll
/// masters (Employee Category, Employee Group, Employee, Payroll Unit, Attendance Type) with name uniqueness,
/// employee-group + attendance-type parent-cycle rejection, delete-if-referenced guards, PAN/UAN/ESI structural
/// validation, and simple/compound payroll-unit precision. No computation ships in this slice.
/// </summary>
public sealed class PayrollServiceTests
{
    private const string ValidPan = "AAPFU0939F";
    private const string ValidUan = "100200300400";               // 12 digits
    private const string ValidEsi = "31001234560000101";          // 17 digits

    private static Company NewCompany() =>
        CompanyFactory.CreateSeeded("Payroll Masters Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    // ---- F11 enable ----

    [Fact]
    public void Payroll_is_disabled_by_default()
    {
        var c = NewCompany();
        Assert.False(c.PayrollEnabled);
        Assert.False(c.PayrollStatutoryEnabled);
        Assert.Empty(c.Employees);
        Assert.Empty(c.EmployeeGroups);
        Assert.Empty(c.EmployeeCategories);
        Assert.Empty(c.PayrollUnits);
        Assert.Empty(c.AttendanceTypes);
    }

    [Fact]
    public void EnablePayroll_sets_both_flags_and_is_idempotent()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        svc.EnablePayroll();
        Assert.True(c.PayrollEnabled);
        Assert.True(c.PayrollStatutoryEnabled);
        svc.EnablePayroll();                 // idempotent
        Assert.True(c.PayrollEnabled);
        Assert.True(c.PayrollStatutoryEnabled);
    }

    [Fact]
    public void EnablePayroll_without_statutory_leaves_statutory_off()
    {
        var c = NewCompany();
        new PayrollService(c).EnablePayroll(enableStatutory: false);
        Assert.True(c.PayrollEnabled);
        Assert.False(c.PayrollStatutoryEnabled);
    }

    [Fact]
    public void DisablePayroll_clears_both_flags_without_deleting_masters()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        svc.EnablePayroll();
        svc.CreateEmployeeGroup("Marketing");
        svc.DisablePayroll();
        Assert.False(c.PayrollEnabled);
        Assert.False(c.PayrollStatutoryEnabled);
        Assert.Single(c.EmployeeGroups);      // masters survive a disable (data is not deleted)
    }

    // ---- Employee categories ----

    [Fact]
    public void Create_employee_category_is_unique()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        svc.CreateEmployeeCategory("Direct");
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployeeCategory("direct"));  // case-insensitive
        Assert.Single(c.EmployeeCategories);
    }

    [Fact]
    public void Create_employee_category_carries_revenue_allocation_flags()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);

        // Default mirrors CostCategory: allocates revenue, not non-revenue.
        var def = svc.CreateEmployeeCategory("Default");
        Assert.True(def.AllocateRevenueItems);
        Assert.False(def.AllocateNonRevenueItems);

        // Explicit non-revenue axis is captured.
        var indirect = svc.CreateEmployeeCategory("Indirect",
            allocateRevenueItems: false, allocateNonRevenueItems: true);
        Assert.False(indirect.AllocateRevenueItems);
        Assert.True(indirect.AllocateNonRevenueItems);

        // Mirror of the CostCategory "≥1 must be Yes" invariant: both-off is rejected, nothing persisted.
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateEmployeeCategory("Neither", allocateRevenueItems: false, allocateNonRevenueItems: false));
        Assert.Equal(2, c.EmployeeCategories.Count);
    }

    [Fact]
    public void Delete_employee_category_blocked_while_referenced_by_an_employee()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var cat = svc.CreateEmployeeCategory("Direct");
        var grp = svc.CreateEmployeeGroup("Production");
        svc.CreateEmployee("Ravi", grp.Id, employeeCategoryId: cat.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteEmployeeCategory(cat.Id));
        Assert.Single(c.EmployeeCategories);
    }

    // ---- Employee groups (hierarchical) ----

    [Fact]
    public void Create_employee_group_is_unique_and_hierarchical()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var admin = svc.CreateEmployeeGroup("Administration");
        var hr = svc.CreateEmployeeGroup("HR", parentId: admin.Id, defineSalaryDetails: true);
        Assert.Equal(admin.Id, hr.ParentId);
        Assert.True(hr.DefineSalaryDetails);
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployeeGroup("hr"));         // duplicate
    }

    [Fact]
    public void Create_employee_group_with_missing_parent_is_rejected()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployeeGroup("Orphan", parentId: Guid.NewGuid()));
    }

    [Fact]
    public void Employee_group_reparent_that_forms_a_cycle_is_rejected_and_rolled_back()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var a = svc.CreateEmployeeGroup("A");
        var b = svc.CreateEmployeeGroup("B", parentId: a.Id);
        // Making A a child of B would form A→B→A.
        Assert.Throws<InvalidOperationException>(() => svc.SetEmployeeGroupParent(a.Id, b.Id));
        Assert.Null(c.FindEmployeeGroup(a.Id)!.ParentId);   // rolled back
    }

    [Fact]
    public void Delete_employee_group_blocked_while_it_has_children_or_employees()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var parent = svc.CreateEmployeeGroup("Parent");
        var child = svc.CreateEmployeeGroup("Child", parentId: parent.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteEmployeeGroup(parent.Id)); // has child
        svc.DeleteEmployeeGroup(child.Id);                                                  // leaf ok
        var withEmp = svc.CreateEmployeeGroup("Sales");
        svc.CreateEmployee("Meena", withEmp.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteEmployeeGroup(withEmp.Id)); // has employee
    }

    // ---- Employees ----

    [Fact]
    public void Create_employee_requires_an_existing_group_and_unique_name()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var grp = svc.CreateEmployeeGroup("Marketing");
        var e = svc.CreateEmployee("Rajkumar Sharma", grp.Id, pan: ValidPan, uan: ValidUan, esiNumber: ValidEsi,
            dateOfJoining: new DateOnly(2015, 4, 1));
        Assert.Equal(grp.Id, e.EmployeeGroupId);
        Assert.Equal(ValidPan, e.Pan);
        Assert.Equal(TaxRegime.New, e.ApplicableTaxRegime);
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployee("rajkumar sharma", grp.Id));       // dup
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployee("New Hire", Guid.NewGuid()));       // no group
    }

    [Theory]
    [InlineData("BADPAN")]        // too short / wrong shape
    [InlineData("1234567890")]    // all digits
    public void Create_employee_with_invalid_pan_is_rejected(string badPan)
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var grp = svc.CreateEmployeeGroup("Marketing");
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployee("X", grp.Id, pan: badPan));
        Assert.Empty(c.Employees);
    }

    [Fact]
    public void Create_employee_with_invalid_uan_or_esi_is_rejected()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var grp = svc.CreateEmployeeGroup("Marketing");
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployee("Y", grp.Id, uan: "12345"));          // not 12 digits
        Assert.Throws<InvalidOperationException>(() => svc.CreateEmployee("Z", grp.Id, esiNumber: "12AB"));     // not 17 digits
        Assert.Empty(c.Employees);
    }

    [Fact]
    public void Create_employee_with_missing_category_is_rejected()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var grp = svc.CreateEmployeeGroup("Marketing");
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateEmployee("W", grp.Id, employeeCategoryId: Guid.NewGuid()));
    }

    // ---- Payroll units (simple + compound) ----

    [Fact]
    public void Create_simple_and_compound_payroll_units()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var min = svc.CreateSimplePayrollUnit("Min", "Minutes");
        var hrs = svc.CreateSimplePayrollUnit("Hrs", "Hours", decimalPlaces: 2);
        var compound = svc.CreateCompoundPayrollUnit("Hrs of 60 Min", "Hours of 60 Minutes", hrs.Id, min.Id, 60);
        Assert.True(compound.IsCompound);
        Assert.Equal(60, compound.ConversionNumerator);
        Assert.Equal(hrs.Id, compound.FirstUnitId);
        Assert.Throws<InvalidOperationException>(() => svc.CreateSimplePayrollUnit("Hrs", "Dup"));  // unique
    }

    [Fact]
    public void Simple_payroll_unit_precision_must_be_0_to_4()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        Assert.Throws<ArgumentException>(() => svc.CreateSimplePayrollUnit("Bad", "Bad", decimalPlaces: 5));
    }

    [Fact]
    public void Delete_payroll_unit_blocked_while_a_component_or_used_by_attendance_type()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var min = svc.CreateSimplePayrollUnit("Min", "Minutes");
        var hrs = svc.CreateSimplePayrollUnit("Hrs", "Hours");
        svc.CreateCompoundPayrollUnit("Hrs of 60 Min", "Hours of 60 Minutes", hrs.Id, min.Id, 60);
        Assert.Throws<InvalidOperationException>(() => svc.DeletePayrollUnit(min.Id)); // component of compound

        var days = svc.CreateSimplePayrollUnit("Days", "Days");
        svc.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeletePayrollUnit(days.Id)); // used by attendance type
    }

    // ---- Attendance / production types (hierarchical) ----

    [Fact]
    public void Create_attendance_types_of_each_kind()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var days = svc.CreateSimplePayrollUnit("Days", "Days");
        var present = svc.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        svc.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay);
        svc.CreateAttendanceType("Overtime", AttendanceTypeKind.Production);
        svc.CreateAttendanceType("Special", AttendanceTypeKind.UserDefined, parentId: present.Id);
        Assert.Equal(4, c.AttendanceTypes.Count);
        Assert.Equal(days.Id, present.PayrollUnitId);
        Assert.Throws<InvalidOperationException>(() => svc.CreateAttendanceType("present", AttendanceTypeKind.AttendancePaid)); // dup
    }

    [Fact]
    public void Attendance_type_reparent_cycle_is_rejected_and_rolled_back()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var a = svc.CreateAttendanceType("A", AttendanceTypeKind.AttendancePaid);
        var b = svc.CreateAttendanceType("B", AttendanceTypeKind.AttendancePaid, parentId: a.Id);
        Assert.Throws<InvalidOperationException>(() => svc.SetAttendanceTypeParent(a.Id, b.Id));
        Assert.Null(c.FindAttendanceType(a.Id)!.ParentId);
    }

    [Fact]
    public void Delete_attendance_type_blocked_while_it_has_children()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        var parent = svc.CreateAttendanceType("Parent", AttendanceTypeKind.AttendancePaid);
        var child = svc.CreateAttendanceType("Child", AttendanceTypeKind.AttendancePaid, parentId: parent.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteAttendanceType(parent.Id));
        svc.DeleteAttendanceType(child.Id);
        svc.DeleteAttendanceType(parent.Id);   // now a leaf
        Assert.Empty(c.AttendanceTypes);
    }

    [Fact]
    public void Create_attendance_type_with_missing_payroll_unit_is_rejected()
    {
        var c = NewCompany();
        var svc = new PayrollService(c);
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateAttendanceType("Bad", AttendanceTypeKind.Production, payrollUnitId: Guid.NewGuid()));
    }
}
