using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-8 slice-1 Payroll masters UI surfaced in the cascade (Study Guide Ch.11;
/// RQ-1/RQ-2/RQ-3): the F11 config toggles "Maintain Payroll" + "Enable Payroll Statutory" (persisting across a
/// reload); the Payroll Masters section (Employee Category / Group / Employee, Payroll Unit, Attendance type)
/// appears under Masters → Create only when Payroll is on; each create screen creates a master through the real
/// shell view models, persists it to a throwaway .db, lists it, and surfaces its guards (blank/duplicate name,
/// employee needs a group, PAN/UAN validation, compound payroll unit needs two simple units) as friendly
/// messages without crashing the UI; and every payroll field is hidden / no-op on a company that never enables
/// Payroll (ER-13). Drives the real VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class PayrollMastersViewModelTests : IDisposable
{
    private const string ValidPan = "AAPFU0939F";   // 5 letters + 4 digits + 1 letter

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollMastersViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>Opens the F11 config page and turns Maintain Payroll on; returns the page VM.</summary>
    private GstConfigViewModel EnablePayroll(MainWindowViewModel vm, bool statutory = true)
    {
        vm.ShowGstConfig();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        if (statutory) page.PayrollStatutoryEnabled = true;
        return page;
    }

    private static string[] CreateMenuLabels(MainWindowViewModel vm)
    {
        vm.ShowCreateMenu();
        return vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
    }

    private EmployeeGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowEmployeeGroupMaster();
        Assert.Equal(Screen.EmployeeGroupMaster, vm.CurrentScreen);
        var m = vm.EmployeeGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        return vm.Company!.FindEmployeeGroupByName(name)!;
    }

    private PayrollUnit CreateSimpleUnit(MainWindowViewModel vm, string symbol, string formal, int decimals = 0)
    {
        vm.ShowPayrollUnitMaster();
        var m = vm.PayrollUnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = formal;
        m.DecimalPlacesText = decimals.ToString();
        Assert.True(m.Create());
        return vm.Company!.FindPayrollUnitByName(symbol)!;
    }

    // ============================================================ (1) F11: enable payroll + persist + ER-13

    [Fact]
    public void Enable_payroll_toggles_persist_across_reload()
    {
        const string companyName = "Payroll Enable Co";
        var vm = NewSeededCompany(companyName);
        Assert.False(vm.Company!.PayrollEnabled);
        Assert.False(vm.Company!.PayrollStatutoryEnabled);

        var page = EnablePayroll(vm, statutory: true);
        Assert.True(vm.Company!.PayrollEnabled);
        Assert.True(vm.Company!.PayrollStatutoryEnabled);

        var reloaded = Reload(companyName);
        Assert.True(reloaded.PayrollEnabled);
        Assert.True(reloaded.PayrollStatutoryEnabled);

        // Turning Payroll off clears statutory too (it is meaningless without Payroll) and persists.
        page.PayrollEnabled = false;
        Assert.False(vm.Company!.PayrollEnabled);
        Assert.False(vm.Company!.PayrollStatutoryEnabled);
        Assert.False(page.PayrollStatutoryEnabled);

        var reloaded2 = Reload(companyName);
        Assert.False(reloaded2.PayrollEnabled);
        Assert.False(reloaded2.PayrollStatutoryEnabled);
    }

    [Fact]
    public void Payroll_statutory_can_toggle_independently_while_payroll_is_on()
    {
        var vm = NewSeededCompany("Payroll Statutory Co");
        var page = EnablePayroll(vm, statutory: false);
        Assert.True(vm.Company!.PayrollEnabled);
        Assert.False(vm.Company!.PayrollStatutoryEnabled);

        page.PayrollStatutoryEnabled = true;
        Assert.True(vm.Company!.PayrollStatutoryEnabled);

        page.PayrollStatutoryEnabled = false;
        Assert.False(vm.Company!.PayrollStatutoryEnabled);
        Assert.True(vm.Company!.PayrollEnabled);   // payroll master gate stays on
    }

    // ============================================================ (2) nav gating

    [Fact]
    public void Payroll_masters_appear_in_create_menu_only_when_payroll_is_enabled()
    {
        var vm = NewSeededCompany("Payroll Nav Co");

        var before = CreateMenuLabels(vm);
        Assert.DoesNotContain("Employee Category", before);
        Assert.DoesNotContain("Employee Group", before);
        Assert.DoesNotContain("Employee", before);
        Assert.DoesNotContain("Payroll Unit", before);
        Assert.DoesNotContain("Attendance / Production Type", before);

        EnablePayroll(vm);

        var after = CreateMenuLabels(vm);
        Assert.Contains("Employee Category", after);
        Assert.Contains("Employee Group", after);
        Assert.Contains("Employee", after);
        Assert.Contains("Payroll Unit", after);
        Assert.Contains("Attendance / Production Type", after);
    }

    [Fact]
    public void Show_master_is_a_no_op_when_payroll_is_off()
    {
        var vm = NewSeededCompany("Payroll Gate Co");
        vm.ShowEmployeeMaster();
        Assert.Null(vm.EmployeeMaster);
        Assert.NotEqual(Screen.EmployeeMaster, vm.CurrentScreen);
    }

    // ============================================================ (3) Employee Category

    [Fact]
    public void Employee_category_is_created_through_the_master_and_persists()
    {
        const string companyName = "Emp Category Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        vm.ShowEmployeeCategoryMaster();
        Assert.Equal(Screen.EmployeeCategoryMaster, vm.CurrentScreen);
        var m = vm.EmployeeCategoryMaster!;
        m.Name = "On-Roll";
        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Name == "On-Roll");

        // blank + duplicate are rejected as friendly messages.
        m.Name = "  ";
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        m.Name = "On-Roll";
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindEmployeeCategoryByName("On-Roll"));
    }

    [Fact]
    public void Employee_category_captures_the_revenue_allocation_axis()
    {
        // RQ-2 / §3.2: an Employee Category mirrors CostCategory — it declares whether it allocates revenue
        // and/or non-revenue cost items (at least one must be Yes). The create UI captures both flags.
        const string companyName = "Emp Category Alloc Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        vm.ShowEmployeeCategoryMaster();
        var m = vm.EmployeeCategoryMaster!;

        // Both-off is rejected with a friendly message, nothing persisted.
        m.Name = "Neither";
        m.AllocateRevenueItems = false;
        m.AllocateNonRevenueItems = false;
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        Assert.Null(vm.Company!.FindEmployeeCategoryByName("Neither"));

        // A non-revenue category is captured and persisted.
        m.Name = "Overheads";
        m.AllocateRevenueItems = false;
        m.AllocateNonRevenueItems = true;
        Assert.True(m.Create());

        var cat = vm.Company!.FindEmployeeCategoryByName("Overheads")!;
        Assert.False(cat.AllocateRevenueItems);
        Assert.True(cat.AllocateNonRevenueItems);

        var reloaded = Reload(companyName);
        var rCat = reloaded.FindEmployeeCategoryByName("Overheads")!;
        Assert.False(rCat.AllocateRevenueItems);
        Assert.True(rCat.AllocateNonRevenueItems);
    }

    // ============================================================ (4) Employee Group (hierarchical)

    [Fact]
    public void Employee_group_nests_under_a_parent_and_persists()
    {
        const string companyName = "Emp Group Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        var head = CreateGroup(vm, "Head Office");
        Assert.True(head.IsPrimary);

        vm.ShowEmployeeGroupMaster();
        var m2 = vm.EmployeeGroupMaster!;
        m2.SelectedParent = m2.ParentOptions.Single(p => p.Group?.Id == head.Id);
        m2.Name = "Marketing";
        m2.DefineSalaryDetails = true;
        Assert.True(m2.Create());

        var child = vm.Company!.FindEmployeeGroupByName("Marketing")!;
        Assert.Equal(head.Id, child.ParentId);
        Assert.True(child.DefineSalaryDetails);

        var reloaded = Reload(companyName);
        var rHead = reloaded.FindEmployeeGroupByName("Head Office");
        var rChild = reloaded.FindEmployeeGroupByName("Marketing");
        Assert.NotNull(rHead);
        Assert.NotNull(rChild);
        Assert.Equal(rHead!.Id, rChild!.ParentId);
    }

    // ============================================================ (5) Employee

    [Fact]
    public void Employee_requires_a_group_then_creates_and_persists()
    {
        const string companyName = "Employee Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        // With no group, the Employee master cannot create — a friendly guard, no crash.
        vm.ShowEmployeeMaster();
        Assert.Equal(Screen.EmployeeMaster, vm.CurrentScreen);
        var m0 = vm.EmployeeMaster!;
        Assert.False(m0.CanCreate);
        m0.Name = "Rajkumar Sharma";
        Assert.False(m0.Create());
        Assert.NotNull(m0.Message);

        // Create a group, reopen, and create the employee with the key fields.
        CreateGroup(vm, "Marketing");
        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        Assert.True(m.CanCreate);
        m.Name = "Rajkumar Sharma";
        m.SelectedGroup = m.GroupOptions.Single(g => g.Group.Name == "Marketing");
        m.EmployeeNumber = "E-001";
        m.Designation = "Manager";
        m.Pan = ValidPan;
        m.DateOfJoiningText = "2025-04-01";
        m.SelectedRegime = m.Regimes.Single(r => r.Value == TaxRegime.New);
        Assert.True(m.Create());

        var emp = vm.Company!.FindEmployeeByName("Rajkumar Sharma")!;
        Assert.Equal("Marketing", vm.Company!.FindEmployeeGroup(emp.EmployeeGroupId)!.Name);
        Assert.Equal("E-001", emp.EmployeeNumber);
        Assert.Equal("Manager", emp.Designation);
        Assert.Equal(ValidPan, emp.Pan);
        Assert.Equal(new DateOnly(2025, 4, 1), emp.DateOfJoining);
        Assert.Equal(TaxRegime.New, emp.ApplicableTaxRegime);
        Assert.Contains(m.Existing, r => r.Name == "Rajkumar Sharma" && r.Group == "Marketing");

        var reloaded = Reload(companyName);
        var rEmp = reloaded.FindEmployeeByName("Rajkumar Sharma");
        Assert.NotNull(rEmp);
        Assert.Equal(ValidPan, rEmp!.Pan);
        Assert.Equal(new DateOnly(2025, 4, 1), rEmp.DateOfJoining);
    }

    [Fact]
    public void Employee_master_captures_all_statutory_bank_and_identity_fields()
    {
        // RQ-2: the create UI must capture the FULL master the model supports — DOB, Location, Function,
        // Aadhaar, PF a/c, and bank account/name/IFSC — not only name/PAN/UAN/ESI. (S4 PF/ECR, S5 ESI and
        // S8 salary-payment bank allocation consume exactly these fields.)
        const string companyName = "Employee Full Fields Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);
        CreateGroup(vm, "Marketing");

        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        m.Name = "Rajkumar Sharma";
        m.SelectedGroup = m.GroupOptions.Single(g => g.Group.Name == "Marketing");
        m.DateOfBirthText = "1985-06-15";
        m.Location = "Mumbai";
        m.Function = "Sales";
        m.Aadhaar = "123412341234";
        m.PfAccountNumber = "MH/BAN/1234567/000/0001234";
        m.BankAccountNumber = "1234567890";
        m.BankName = "State Bank";
        m.BankIfsc = "SBIN0001234";
        Assert.True(m.Create());

        var emp = vm.Company!.FindEmployeeByName("Rajkumar Sharma")!;
        Assert.Equal(new DateOnly(1985, 6, 15), emp.DateOfBirth);
        Assert.Equal("Mumbai", emp.Location);
        Assert.Equal("Sales", emp.Function);
        Assert.Equal("123412341234", emp.Aadhaar);
        Assert.Equal("MH/BAN/1234567/000/0001234", emp.PfAccountNumber);
        Assert.Equal("1234567890", emp.BankAccountNumber);
        Assert.Equal("State Bank", emp.BankName);
        Assert.Equal("SBIN0001234", emp.BankIfsc);

        var reloaded = Reload(companyName);
        var rEmp = reloaded.FindEmployeeByName("Rajkumar Sharma")!;
        Assert.Equal(new DateOnly(1985, 6, 15), rEmp.DateOfBirth);
        Assert.Equal("Mumbai", rEmp.Location);
        Assert.Equal("Sales", rEmp.Function);
        Assert.Equal("123412341234", rEmp.Aadhaar);
        Assert.Equal("MH/BAN/1234567/000/0001234", rEmp.PfAccountNumber);
        Assert.Equal("1234567890", rEmp.BankAccountNumber);
        Assert.Equal("State Bank", rEmp.BankName);
        Assert.Equal("SBIN0001234", rEmp.BankIfsc);
    }

    [Fact]
    public void Employee_rejects_a_bad_date_of_birth_without_crashing()
    {
        var vm = NewSeededCompany("Employee DOB Guard Co");
        EnablePayroll(vm);
        CreateGroup(vm, "Ops");

        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        m.Name = "Bad Dob";
        m.SelectedGroup = m.GroupOptions.First();
        m.DateOfBirthText = "31/31/1985";   // impossible date
        Assert.False(m.Create());
        Assert.Contains("Date of birth", m.Message!);
        Assert.Null(vm.Company!.FindEmployeeByName("Bad Dob"));   // nothing persisted
    }

    [Fact]
    public void Employee_rejects_an_invalid_pan_and_bad_date_without_crashing()
    {
        var vm = NewSeededCompany("Employee Guard Co");
        EnablePayroll(vm);
        CreateGroup(vm, "Ops");

        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        m.Name = "Bad Pan";
        m.SelectedGroup = m.GroupOptions.First();
        m.Pan = "NOTAPAN";
        Assert.False(m.Create());
        Assert.Contains("PAN", m.Message!);
        Assert.Null(vm.Company!.FindEmployeeByName("Bad Pan"));   // nothing persisted

        m.Pan = string.Empty;
        m.DateOfJoiningText = "31/31/2025";   // impossible date
        Assert.False(m.Create());
        Assert.Contains("Date of joining", m.Message!);
        Assert.Null(vm.Company!.FindEmployeeByName("Bad Pan"));
    }

    // ============================================================ (6) Payroll Unit (simple + compound)

    [Fact]
    public void Payroll_unit_simple_and_compound_create_and_persist()
    {
        const string companyName = "Payroll Unit Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        var month = CreateSimpleUnit(vm, "Month", "Months");
        var days = CreateSimpleUnit(vm, "Days", "Days");

        vm.ShowPayrollUnitMaster();
        var m = vm.PayrollUnitMaster!;
        Assert.True(m.CanBuildCompound);
        m.IsCompound = true;
        m.Symbol = "Month of 26 Days";
        m.FormalName = "Month of 26 Days";
        m.FirstUnit = m.SimpleUnits.Single(u => u.Id == month.Id);
        m.TailUnit = m.SimpleUnits.Single(u => u.Id == days.Id);
        m.ConversionFactorText = "26";
        Assert.True(m.Create());

        var compound = vm.Company!.FindPayrollUnitByName("Month of 26 Days")!;
        Assert.True(compound.IsCompound);
        Assert.Equal(26, compound.ConversionNumerator);
        Assert.Equal(month.Id, compound.FirstUnitId);
        Assert.Equal(days.Id, compound.TailUnitId);

        var reloaded = Reload(companyName);
        var rCompound = reloaded.FindPayrollUnitByName("Month of 26 Days");
        Assert.NotNull(rCompound);
        Assert.True(rCompound!.IsCompound);
        Assert.Equal(26, rCompound.ConversionNumerator);
    }

    [Fact]
    public void Payroll_unit_pre_validates_decimals_and_factor()
    {
        var vm = NewSeededCompany("Payroll Unit Guard Co");
        EnablePayroll(vm);

        vm.ShowPayrollUnitMaster();
        var m = vm.PayrollUnitMaster!;
        m.IsCompound = false;
        m.Symbol = "Hrs";
        m.FormalName = "Hours";
        m.DecimalPlacesText = "9";       // out of 0–4
        Assert.False(m.Create());
        Assert.Contains("Decimal places", m.Message!);

        // A compound unit needs two simple units first.
        m.IsCompound = true;
        Assert.False(m.CanBuildCompound);   // none created yet
    }

    // ============================================================ (7) Attendance / Production Type

    [Fact]
    public void Attendance_type_creates_with_kind_parent_and_unit_and_persists()
    {
        const string companyName = "Attendance Type Co";
        var vm = NewSeededCompany(companyName);
        EnablePayroll(vm);

        var days = CreateSimpleUnit(vm, "Days", "Days");

        vm.ShowAttendanceTypeMaster();
        Assert.Equal(Screen.AttendanceTypeMaster, vm.CurrentScreen);
        var m = vm.AttendanceTypeMaster!;
        m.Name = "Present";
        m.SelectedKind = m.Kinds.Single(k => k.Value == AttendanceTypeKind.AttendancePaid);
        m.SelectedUnit = m.UnitOptions.Single(u => u.Unit?.Id == days.Id);
        Assert.True(m.Create());

        var present = vm.Company!.FindAttendanceTypeByName("Present")!;
        Assert.Equal(AttendanceTypeKind.AttendancePaid, present.Kind);
        Assert.Equal(days.Id, present.PayrollUnitId);

        // A child type nests under the first.
        vm.ShowAttendanceTypeMaster();
        var m2 = vm.AttendanceTypeMaster!;
        m2.Name = "Overtime";
        m2.SelectedKind = m2.Kinds.Single(k => k.Value == AttendanceTypeKind.Production);
        m2.SelectedParent = m2.ParentOptions.Single(p => p.Type?.Id == present.Id);
        Assert.True(m2.Create());
        var overtime = vm.Company!.FindAttendanceTypeByName("Overtime")!;
        Assert.Equal(present.Id, overtime.ParentId);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindAttendanceTypeByName("Present"));
        Assert.Equal(present.Id, reloaded.FindAttendanceTypeByName("Overtime")!.ParentId);
    }

    // ============================================================ (7b) attendance-kind label fidelity

    [Fact]
    public void Attendance_paid_kind_is_labelled_with_the_catalog_term()
    {
        // Fidelity (requirements §1.1/§3.2): the paid present/leave calendar type is "Attendance/Leave with Pay",
        // the documented Tally term — NOT an ad-hoc "Attendance / Paid".
        var vm = NewSeededCompany("Attendance Label Co");
        EnablePayroll(vm);

        vm.ShowAttendanceTypeMaster();
        var m = vm.AttendanceTypeMaster!;

        // The picker option for the paid kind carries the catalog label.
        var paidOption = m.Kinds.Single(k => k.Value == AttendanceTypeKind.AttendancePaid);
        Assert.Equal("Attendance/Leave with Pay", paidOption.Display);

        // The existing-types "Type" column shows the same label after creating a paid type.
        m.Name = "Present";
        m.SelectedKind = paidOption;
        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Name == "Present" && r.Kind == "Attendance/Leave with Pay");
    }

    // ============================================================ (8) one page column at a time

    [Fact]
    public void Each_payroll_master_opens_as_exactly_one_page_column()
    {
        var vm = NewSeededCompany("Payroll One Column Co");
        EnablePayroll(vm);

        vm.ShowEmployeeGroupMaster();
        Assert.NotNull(vm.EmployeeGroupMaster);
        var pageCols1 = vm.Columns.Count(c => c.IsPage);
        Assert.Equal(1, pageCols1);

        // Opening a different payroll master REPLACES the page column (never stacks).
        vm.ShowPayrollUnitMaster();
        Assert.NotNull(vm.PayrollUnitMaster);
        Assert.Null(vm.EmployeeGroupMaster);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
