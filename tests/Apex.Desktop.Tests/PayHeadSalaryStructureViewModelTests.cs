using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-8 slice-2 <b>Pay Head</b> master and <b>Salary Details</b> (structure) UI
/// surfaced in the cascade (Study Guide pp.198–212; RQ-4/RQ-5). Both screens are gated behind F11 "Maintain
/// Payroll" (ER-13), appear under Masters → Create → Payroll Masters, drive the real shell view models over a
/// throwaway .db, create through the <c>PayHeadService</c> / <c>SalaryStructureService</c> engines, adapt their
/// visible fields to the chosen Calculation Type, and surface every engine guard (blank/duplicate name, a
/// computed-on cycle, a computed head with no basis, a structure line whose value contradicts its pay head's
/// calculation type, a duplicate effective-from) as friendly messages without crashing the UI. A dated revision
/// is captured as a new version. Drives the real VMs — no UI toolkit.
/// </summary>
public sealed class PayHeadSalaryStructureViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayHeadSalaryStructureViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPayHeadTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static string[] CreateMenuLabels(MainWindowViewModel vm)
    {
        vm.ShowCreateMenu();
        return vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
    }

    /// <summary>Creates a Flat-Rate earnings pay head through the master screen and returns it.</summary>
    private PayHead CreateFlatEarning(MainWindowViewModel vm, string name)
    {
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = name;
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        Assert.True(m.Create(), m.Message);
        return vm.Company!.FindPayHeadByName(name)!;
    }

    private Employee CreateEmployee(MainWindowViewModel vm, string group, string name)
    {
        vm.ShowEmployeeGroupMaster();
        var g = vm.EmployeeGroupMaster!;
        if (vm.Company!.FindEmployeeGroupByName(group) is null)
        {
            g.Name = group;
            Assert.True(g.Create());
        }

        vm.ShowEmployeeMaster();
        var e = vm.EmployeeMaster!;
        e.Name = name;
        e.SelectedGroup = e.GroupOptions.Single(o => o.Group.Name == group);
        Assert.True(e.Create(), e.Message);
        return vm.Company!.FindEmployeeByName(name)!;
    }

    // ============================================================ (1) nav gating

    [Fact]
    public void Pay_head_and_salary_details_appear_in_create_menu_only_when_payroll_is_enabled()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "PayHead Nav Co";
        vm.CreateCompany();

        var before = CreateMenuLabels(vm);
        Assert.DoesNotContain("Pay Head", before);
        Assert.DoesNotContain("Salary Details", before);

        vm.ShowGstConfig();
        vm.GstConfig!.PayrollEnabled = true;

        var after = CreateMenuLabels(vm);
        Assert.Contains("Pay Head", after);
        Assert.Contains("Salary Details", after);
    }

    [Fact]
    public void Show_pay_head_master_is_a_no_op_when_payroll_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "PayHead Gate Co";
        vm.CreateCompany();

        vm.ShowPayHeadMaster();
        Assert.Null(vm.PayHeadMaster);
        Assert.NotEqual(Screen.PayHeadMaster, vm.CurrentScreen);

        vm.ShowSalaryStructureMaster();
        Assert.Null(vm.SalaryDetails);
        Assert.NotEqual(Screen.SalaryStructureMaster, vm.CurrentScreen);
    }

    // ============================================================ (2) Pay Head — flat + persist + guards

    [Fact]
    public void Flat_rate_pay_head_is_created_through_the_master_and_persists()
    {
        const string companyName = "PayHead Flat Co";
        var vm = NewPayrollCompany(companyName);

        vm.ShowPayHeadMaster();
        Assert.Equal(Screen.PayHeadMaster, vm.CurrentScreen);
        var m = vm.PayHeadMaster!;
        m.Name = "Basic";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        m.SelectedIncomeTaxComponent = m.IncomeTaxComponents.Single(i => i.Value == IncomeTaxComponent.BasicSalary);
        m.UseForGratuity = true;
        Assert.True(m.Create(), m.Message);
        Assert.Contains(m.Existing, r => r.Name == "Basic");

        var basic = vm.Company!.FindPayHeadByName("Basic")!;
        Assert.Equal(PayHeadType.Earnings, basic.Type);
        Assert.Equal(PayHeadCalculationType.FlatRate, basic.CalculationType);
        Assert.Equal(IncomeTaxComponent.BasicSalary, basic.IncomeTaxComponent);
        Assert.True(basic.UseForGratuity);
        Assert.True(basic.AffectsNetSalary);   // earnings default

        // blank + duplicate rejected as friendly messages
        m.Name = "  ";
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        m.Name = "Basic";
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.FindPayHeadByName("Basic"));
    }

    // ============================================================ (3) Pay Head — computed value + basis + slab

    [Fact]
    public void As_computed_value_pay_head_captures_basis_and_percentage_slab()
    {
        const string companyName = "PayHead Computed Co";
        var vm = NewPayrollCompany(companyName);

        var basic = CreateFlatEarning(vm, "Basic");

        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "DA";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
        Assert.True(m.ShowComputationEditor);

        // basis = Basic
        m.SelectedBasisPayHead = m.BasisPayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        m.AddBasisComponent();
        Assert.Single(m.BasisComponents);

        // slab = 40% of basis
        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        m.SlabRateOrValueText = "40";
        m.AddSlab();
        Assert.Single(m.Slabs);

        Assert.True(m.Create(), m.Message);

        var da = vm.Company!.FindPayHeadByName("DA")!;
        Assert.Equal(PayHeadCalculationType.AsComputedValue, da.CalculationType);
        Assert.NotNull(da.Computation);
        Assert.Single(da.Computation!.BasisComponents);
        Assert.Equal(basic.Id, da.Computation.BasisComponents[0].PayHeadId);
        Assert.Single(da.Computation.Slabs);
        Assert.Equal(PayHeadComputationSlabType.Percentage, da.Computation.Slabs[0].SlabType);
        Assert.Equal(4000, da.Computation.Slabs[0].RateBasisPoints);   // 40%

        var reloaded = Reload(companyName);
        var rDa = reloaded.FindPayHeadByName("DA")!;
        Assert.Equal(4000, rDa.Computation!.Slabs[0].RateBasisPoints);
    }

    [Fact]
    public void As_computed_value_requires_at_least_one_basis_component()
    {
        var vm = NewPayrollCompany("PayHead No Basis Co");
        CreateFlatEarning(vm, "Basic");

        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "HRA";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
        // add a slab but no basis
        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        m.SlabRateOrValueText = "20";
        m.AddSlab();

        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        Assert.Null(vm.Company!.FindPayHeadByName("HRA"));
    }

    [Fact]
    public void Computed_basis_editor_dedups_and_builds_a_valid_multi_basis_head()
    {
        // The computed-on CYCLE guard (ER-3) needs a back-edge (A→B then B→A) which only an Alter can create; the
        // create-only screen builds forward edges to PRE-EXISTING heads only, so a cycle (or a self-reference) is
        // structurally unreachable here — that guard is covered against the Alter path in the engine's
        // PayHeadServiceTests. What IS reachable through this UI is the basis editor's own integrity: a duplicate
        // basis component is ignored, and a valid multi-basis computed head is accepted and reaches the engine.
        var vm = NewPayrollCompany("PayHead Basis Co");
        var bHead = CreateFlatEarning(vm, "B");

        // A = computed on B (a valid single-basis head)
        vm.ShowPayHeadMaster();
        var mA = vm.PayHeadMaster!;
        mA.Name = "A";
        mA.SelectedType = mA.Types.Single(t => t.Value == PayHeadType.Earnings);
        mA.SelectedCalcType = mA.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
        mA.SelectedBasisPayHead = mA.BasisPayHeadOptions.Single(o => o.PayHead.Id == bHead.Id);
        mA.AddBasisComponent();
        mA.SelectedSlabType = mA.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        mA.SlabRateOrValueText = "10";
        mA.AddSlab();
        Assert.True(mA.Create(), mA.Message);
        var aHead = vm.Company!.FindPayHeadByName("A")!;

        // A NEW computed head on A + B (valid multi-basis), and a duplicate basis add is ignored.
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "C";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
        m.SelectedBasisPayHead = m.BasisPayHeadOptions.Single(o => o.PayHead.Id == aHead.Id);
        m.AddBasisComponent();
        m.SelectedBasisPayHead = m.BasisPayHeadOptions.Single(o => o.PayHead.Id == bHead.Id);
        m.AddBasisComponent();
        var countAfterTwo = m.BasisComponents.Count;
        Assert.Equal(2, countAfterTwo);
        // adding B again is a friendly no-op (the engine would also reject a duplicate component)
        m.SelectedBasisPayHead = m.BasisPayHeadOptions.Single(o => o.PayHead.Id == bHead.Id);
        m.AddBasisComponent();
        Assert.Equal(countAfterTwo, m.BasisComponents.Count);
        Assert.NotNull(m.Message);   // "already in the basis"

        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        m.SlabRateOrValueText = "5";
        m.AddSlab();
        Assert.True(m.Create(), m.Message);

        var c = vm.Company!.FindPayHeadByName("C")!;
        Assert.Equal(2, c.Computation!.BasisComponents.Count);
    }

    [Fact]
    public void On_attendance_pay_head_requires_an_attendance_type_link()
    {
        var vm = NewPayrollCompany("PayHead Attendance Co");

        // Create a paid attendance type first.
        vm.ShowAttendanceTypeMaster();
        var at = vm.AttendanceTypeMaster!;
        at.Name = "Present";
        at.SelectedKind = at.Kinds.Single(k => k.Value == AttendanceTypeKind.AttendancePaid);
        Assert.True(at.Create());
        var present = vm.Company!.FindAttendanceTypeByName("Present")!;

        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "Attendance Pay";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.OnAttendance);
        Assert.True(m.ShowAttendanceLink);

        // without an attendance type, blocked
        Assert.False(m.Create());
        Assert.NotNull(m.Message);

        // link it and it creates
        m.SelectedAttendanceType = m.AttendanceTypeOptions.Single(o => o.Type.Id == present.Id);
        Assert.True(m.Create(), m.Message);

        var ph = vm.Company!.FindPayHeadByName("Attendance Pay")!;
        Assert.Equal(present.Id, ph.AttendanceTypeId);
    }

    // ============================================================ (4) Salary Details — structure + revision

    [Fact]
    public void Salary_structure_is_defined_for_an_employee_and_persists()
    {
        const string companyName = "Salary Structure Co";
        var vm = NewPayrollCompany(companyName);

        var basic = CreateFlatEarning(vm, "Basic");
        var emp = CreateEmployee(vm, "Marketing", "Rajkumar Sharma");

        vm.ShowSalaryStructureMaster();
        Assert.Equal(Screen.SalaryStructureMaster, vm.CurrentScreen);
        var m = vm.SalaryDetails!;
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-04-01";
        var row = m.Lines[0];
        row.SelectedPayHead = row.PayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        row.AmountText = "80000";
        Assert.True(m.Save(), m.Message);

        var structure = vm.Company!.SalaryStructures.Single(s => s.ScopeId == emp.Id);
        Assert.Equal(new DateOnly(2025, 4, 1), structure.EffectiveFrom);
        Assert.Single(structure.Lines);
        Assert.Equal(basic.Id, structure.Lines[0].PayHeadId);
        Assert.Equal(80000m, structure.Lines[0].Amount!.Value.Amount);
        Assert.Contains(m.History, h => h.EffectiveFrom.Contains("2025"));

        var reloaded = Reload(companyName);
        var rStruct = reloaded.SalaryStructures.Single(s => s.ScopeId == emp.Id);
        Assert.Equal(80000m, rStruct.Lines[0].Amount!.Value.Amount);
    }

    [Fact]
    public void Salary_structure_line_value_must_match_the_pay_head_calc_type()
    {
        // A computed head must NOT carry a structure-line amount (ER-2) — surfaced from the engine as a message.
        var vm = NewPayrollCompany("Salary Calc Type Co");
        var basic = CreateFlatEarning(vm, "Basic");

        // A computed DA (40% of Basic)
        vm.ShowPayHeadMaster();
        var mp = vm.PayHeadMaster!;
        mp.Name = "DA";
        mp.SelectedType = mp.Types.Single(t => t.Value == PayHeadType.Earnings);
        mp.SelectedCalcType = mp.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
        mp.SelectedBasisPayHead = mp.BasisPayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        mp.AddBasisComponent();
        mp.SelectedSlabType = mp.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        mp.SlabRateOrValueText = "40";
        mp.AddSlab();
        Assert.True(mp.Create(), mp.Message);
        var da = vm.Company!.FindPayHeadByName("DA")!;

        var emp = CreateEmployee(vm, "Marketing", "Rajkumar Sharma");

        vm.ShowSalaryStructureMaster();
        var m = vm.SalaryDetails!;
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-04-01";
        // put an amount against the COMPUTED head — must be rejected (blank required for computed)
        var row = m.Lines[0];
        row.SelectedPayHead = row.PayHeadOptions.Single(o => o.PayHead.Id == da.Id);
        row.AmountText = "5000";
        Assert.False(m.Save());
        Assert.NotNull(m.Message);
        Assert.Empty(vm.Company!.SalaryStructures);
    }

    [Fact]
    public void Salary_structure_captures_a_dated_revision()
    {
        const string companyName = "Salary Revision Co";
        var vm = NewPayrollCompany(companyName);
        var basic = CreateFlatEarning(vm, "Basic");
        var emp = CreateEmployee(vm, "Marketing", "Rajkumar Sharma");

        // first structure effective 2025-04-01, Basic = 80,000
        vm.ShowSalaryStructureMaster();
        var m = vm.SalaryDetails!;
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-04-01";
        m.Lines[0].SelectedPayHead = m.Lines[0].PayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        m.Lines[0].AmountText = "80000";
        Assert.True(m.Save(), m.Message);

        // a duplicate effective-from is rejected (friendly)
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-04-01";
        m.Lines[0].SelectedPayHead = m.Lines[0].PayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        m.Lines[0].AmountText = "90000";
        Assert.False(m.Save());
        Assert.NotNull(m.Message);

        // a later dated revision is a NEW version, both retained
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-10-01";
        m.Lines[0].SelectedPayHead = m.Lines[0].PayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        m.Lines[0].AmountText = "90000";
        Assert.True(m.Save(), m.Message);

        var structures = vm.Company!.SalaryStructures.Where(s => s.ScopeId == emp.Id).ToList();
        Assert.Equal(2, structures.Count);
        Assert.Equal(2, m.History.Count);

        var reloaded = Reload(companyName);
        Assert.Equal(2, reloaded.SalaryStructures.Count(s => s.ScopeId == emp.Id));
    }

    // ============================================================ (4b) group-level structure (RQ-5) + copy-from

    [Fact]
    public void Group_level_salary_structure_is_definable_through_the_ui_and_persists()
    {
        // RQ-5: a Salary Structure must be definable at the Employee GROUP level, not only per employee.
        const string companyName = "Group Salary Structure Co";
        var vm = NewPayrollCompany(companyName);
        var basic = CreateFlatEarning(vm, "Basic");
        CreateEmployee(vm, "Marketing", "Rajkumar Sharma");   // also creates the "Marketing" group
        var group = vm.Company!.FindEmployeeGroupByName("Marketing")!;

        vm.ShowSalaryStructureMaster();
        var m = vm.SalaryDetails!;
        m.SelectedScope = m.Scopes.Single(s => s.Value == SalaryStructureScope.EmployeeGroup);
        m.SelectedEmployeeGroup = m.EmployeeGroups.Single(g => g.Id == group.Id);
        m.EffectiveFromText = "2025-04-01";
        var row = m.Lines[0];
        row.SelectedPayHead = row.PayHeadOptions.Single(o => o.PayHead.Id == basic.Id);
        row.AmountText = "70000";
        Assert.True(m.Save(), m.Message);

        var structure = vm.Company!.SalaryStructures.Single(
            s => s.Scope == SalaryStructureScope.EmployeeGroup && s.ScopeId == group.Id);
        Assert.Equal(basic.Id, structure.Lines[0].PayHeadId);
        Assert.Equal(70000m, structure.Lines[0].Amount!.Value.Amount);
        Assert.Contains(m.History, h => h.EffectiveFrom.Contains("2025"));

        var reloaded = Reload(companyName);
        Assert.Equal(1, reloaded.SalaryStructures.Count(
            s => s.Scope == SalaryStructureScope.EmployeeGroup && s.ScopeId == group.Id));
    }

    [Fact]
    public void Copy_from_parent_seeds_the_grid_from_the_employees_group_structure()
    {
        // "Copy From Parent Value" must actually pre-populate the grid from the parent group's structure
        // (not leave a single blank row).
        var vm = NewPayrollCompany("Salary Copy Parent Co");
        var basic = CreateFlatEarning(vm, "Basic");
        var emp = CreateEmployee(vm, "Marketing", "Rajkumar Sharma");
        var group = vm.Company!.FindEmployeeGroupByName("Marketing")!;

        // A group-level structure to inherit from (Basic = 70,000).
        new SalaryStructureService(vm.Company!).DefineForGroup(group.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(70000m)) });

        vm.ShowSalaryStructureMaster();
        var m = vm.SalaryDetails!;
        m.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        m.EffectiveFromText = "2025-05-01";
        m.SelectedStartType = m.StartTypes.Single(s => s.Value == SalaryStructureStartType.CopyFromParent);

        Assert.Contains(m.Lines, r =>
            r.SelectedPayHead?.PayHead.Id == basic.Id && r.AmountText.Replace(",", "").Contains("70000"));
    }

    [Fact]
    public void Copy_from_employee_seeds_the_grid_from_a_source_employee_structure()
    {
        var vm = NewPayrollCompany("Salary Copy Employee Co");
        var basic = CreateFlatEarning(vm, "Basic");
        var source = CreateEmployee(vm, "Marketing", "Source Emp");
        var target = CreateEmployee(vm, "Marketing", "Target Emp");

        new SalaryStructureService(vm.Company!).DefineForEmployee(source.Id, new DateOnly(2025, 4, 1),
            new[] { new SalaryStructureLine(basic.Id, 0, Money.FromRupees(55000m)) });

        vm.ShowSalaryStructureMaster();
        var m = vm.SalaryDetails!;
        m.SelectedEmployee = m.Employees.Single(e => e.Id == target.Id);
        m.EffectiveFromText = "2025-05-01";
        m.SelectedStartType = m.StartTypes.Single(s => s.Value == SalaryStructureStartType.CopyFromEmployee);
        m.SelectedSourceEmployee = m.SourceEmployees.Single(e => e.Id == source.Id);

        Assert.Contains(m.Lines, r =>
            r.SelectedPayHead?.PayHead.Id == basic.Id && r.AmountText.Replace(",", "").Contains("55000"));
    }

    // ============================================================ (5) one page column at a time

    [Fact]
    public void Pay_head_and_salary_details_open_as_exactly_one_page_column()
    {
        var vm = NewPayrollCompany("PayHead One Column Co");

        vm.ShowPayHeadMaster();
        Assert.NotNull(vm.PayHeadMaster);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));

        vm.ShowSalaryStructureMaster();
        Assert.NotNull(vm.SalaryDetails);
        Assert.Null(vm.PayHeadMaster);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
