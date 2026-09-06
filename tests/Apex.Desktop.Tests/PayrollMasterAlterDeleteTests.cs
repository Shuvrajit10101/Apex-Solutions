using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 7.16 LOCK — Alter and Delete on the payroll masters.</b>
///
/// <para><b>The defect these lock.</b> The census states it once, as a capability in its own right rather than
/// eight coincidences: <c>ForAlter</c> existed in exactly three master view models tree-wide and <b>none was a
/// payroll master</b>; every payroll master view model returned zero for Alter and zero for Delete. The payroll
/// service <i>advertised</i> create/alter/delete in its own doc comment and nothing reached the last two. A
/// mis-keyed pay head, employee or attendance type could be created and then never corrected and never removed.
/// </para>
///
/// <para><b>Why these tests are written this way.</b> The engine deletes already existed and were already
/// guarded — <c>PayrollService.DeleteEmployeeCategory</c> and its six siblings shipped long ago with <b>zero
/// callers</b>. So a test that calls a service proves nothing at all about this row: the row is about REACH. Every
/// assertion below drives REAL keystrokes through <see cref="MainWindow"/>'s tunnel handler — arrow-Down to enter
/// the existing-list, Ctrl+Enter to alter, Ctrl+A to accept, Alt+D then Y to delete — and one test walks the
/// Gateway cascade itself with nothing but arrows and Enter.</para>
///
/// <para><b>The alteration is asserted by IDENTITY, not by name.</b> Every round trip renames the master and then
/// checks the count is unchanged and the SAME <c>Guid</c> now carries the new name. A "rename" that created a
/// second master would pass a name-only assertion and silently fork every historical reference.</para>
///
/// <para>🔴 <b>SCOPE — this file covers FIVE of the eight kinds, and row 7.16 is NOT closed.</b> Employee
/// category, employee group, payroll unit, attendance/production type and — since W7-D2 — the employee master
/// are driven end-to-end below. The pay head, salary structure and tax declaration masters are not built;
/// <see cref="PayrollMasterHalfWiredKindsTests"/> locks that remainder so it cannot be quietly claimed. A green
/// run of this file is evidence for five kinds and for nothing else.</para>
/// </summary>
public sealed class PayrollMasterAlterDeleteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollAlter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.Company!.PayrollEnabled = true;
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, tempDir);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Arrow-Down into the existing-list until the highlight lands on <paramref name="name"/>, then
    /// Ctrl+Enter to open it for alteration. Fails loudly when the name is not reachable by arrows.</summary>
    private static void ArrowToAndAlter(MainWindow window, MainWindowViewModel vm, string name)
    {
        var list = vm.PayrollMasterScreen;
        Assert.NotNull(list);
        for (var i = 0; i < 40; i++)
        {
            if (vm.PayrollMasterScreen?.HighlightedMasterRow?.MasterName == name)
            {
                Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
                return;
            }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    /// <summary>Arrow-Down to <paramref name="name"/>, then Alt+D and Y — the real destructive accelerator and
    /// the real confirmation, never a direct service call.</summary>
    private static void ArrowToAndDelete(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 40; i++)
        {
            if (vm.PayrollMasterScreen?.HighlightedMasterRow?.MasterName == name)
            {
                Key(window, PhysicalKey.D, RawInputModifiers.Alt);
                Key(window, PhysicalKey.Y);
                return;
            }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    /// <summary>
    /// 🔴 <b>Escape out of an ALTERATION and re-open the master's list — the journey a real operator makes, and
    /// the one the delete assertions below MUST take.</b>
    ///
    /// <para><b>Why it is needed, and why it is not a workaround.</b> Accepting an alteration with Ctrl+A does
    /// NOT put the screen back into Create mode: <c>_editingId</c> stays set, so <c>IsAltering</c> stays true and
    /// Alt+D remains — correctly — inert, because deleting the record you are part-way through editing is never
    /// what was meant (the rule <see cref="Alt_d_is_inert_while_a_payroll_master_is_open_for_alteration"/>
    /// locks). That is not payroll-specific behaviour invented here: it is exactly what
    /// <c>StockItemMasterViewModel</c>, <c>LedgerMasterViewModel</c> and <c>AccountGroupMasterViewModel</c> all
    /// do — none of the four clears its editing id on accept, and <c>StockItemAlterReachabilityTests</c> ships
    /// green over that behaviour. An earlier draft of this file assumed the screen returned to Create mode on
    /// accept and asserted a delete straight afterwards; that draft never compiled, so the expectation was never
    /// tested against anything. It is the expectation that was wrong, not the code — and giving the payroll
    /// masters a return-to-list-on-accept that the other three master families do not have would be precisely
    /// the "one kind gated differently from the others" divergence <see cref="IPayrollMasterList"/> exists to
    /// prevent. Whether ALL master alterations should return to their list on accept is a real fidelity question
    /// against the vendor, but it is a tree-wide one and is recorded rather than answered here.</para>
    ///
    /// <para>Pure keyboard throughout: Escape pops the page column back to the menu, and the master is re-drilled
    /// with the same arrows and Enter the Gateway test uses — so the delete is still proven REACHABLE, which is
    /// the whole point of row 7.16.</para>
    /// </summary>
    private static void EscapeAndReopenList(MainWindow window, MainWindowViewModel vm, string menuLabel)
    {
        Key(window, PhysicalKey.Escape);
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, menuLabel);
        Assert.False(vm.PayrollMasterScreen?.IsAltering ?? true,
            $"Re-opening '{menuLabel}' did not land on a Create-mode list, so Alt+D could not be tested.");
    }

    // ================================================================= the full-cascade reachability proof

    /// <summary>
    /// 🔴 THE ROW-7.16 TEST. From the Gateway, using <b>only keys</b>: drill Create → Payroll Masters → Employee
    /// Category, create one with Ctrl+A, arrow into the existing-list, Ctrl+Enter to arrive at an <b>Alteration</b>
    /// of that very category, rename it with Ctrl+A, and confirm the SAME id now carries the new name.
    /// </summary>
    [AvaloniaFact]
    public void Payroll_master_alteration_is_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("Payroll Reach Co");
        try
        {
            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Employee Category");
            Assert.Equal(Screen.EmployeeCategoryMaster, vm.CurrentScreen);

            var create = vm.EmployeeCategoryMaster!;
            Assert.False(create.IsAltering);
            create.Name = "Contract";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Single(vm.Company!.EmployeeCategories, c => !c.IsPredefined);

            var id = vm.Company.EmployeeCategories.Single(c => c.Name == "Contract").Id;

            ArrowToAndAlter(window, vm, "Contract");
            var alter = vm.EmployeeCategoryMaster!;
            Assert.True(alter.IsAltering, "Ctrl+Enter did not open the highlighted category for alteration");
            Assert.Equal("Contract", alter.Name);
            Assert.Equal("Employee Category Alteration", alter.Caption);

            alter.Name = "Contract Staff";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Single(vm.Company.EmployeeCategories, c => !c.IsPredefined);
            Assert.Equal("Contract Staff", vm.Company.FindEmployeeCategory(id)!.Name);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= alter round trips, per master kind

    [AvaloniaFact]
    public void Employee_group_alters_by_identity_and_deletes()
    {
        var (window, vm, dir) = NewWindow("Emp Group Co");
        try
        {
            vm.ShowEmployeeGroupMaster();
            vm.EmployeeGroupMaster!.Name = "Sales";
            Assert.True(vm.EmployeeGroupMaster.Create(), vm.EmployeeGroupMaster.Message);
            var id = vm.Company!.EmployeeGroups.Single(g => g.Name == "Sales").Id;

            ArrowToAndAlter(window, vm, "Sales");
            Assert.True(vm.EmployeeGroupMaster!.IsAltering);
            vm.EmployeeGroupMaster.Name = "Field Sales";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("Field Sales", vm.Company.FindEmployeeGroup(id)!.Name);
            Assert.Single(vm.Company.EmployeeGroups);

            EscapeAndReopenList(window, vm, "Employee Group");
            ArrowToAndDelete(window, vm, "Field Sales");
            Assert.Empty(vm.Company.EmployeeGroups);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    [AvaloniaFact]
    public void Payroll_unit_alters_by_identity_and_deletes()
    {
        var (window, vm, dir) = NewWindow("Payroll Unit Co");
        try
        {
            vm.ShowPayrollUnitMaster();
            vm.PayrollUnitMaster!.Symbol = "Days";
            vm.PayrollUnitMaster.FormalName = "Days";
            Assert.True(vm.PayrollUnitMaster.Create(), vm.PayrollUnitMaster.Message);
            var id = vm.Company!.PayrollUnits.Single(u => u.Symbol == "Days").Id;

            ArrowToAndAlter(window, vm, "Days");
            Assert.True(vm.PayrollUnitMaster!.IsAltering);
            vm.PayrollUnitMaster.FormalName = "Working Days";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("Working Days", vm.Company.FindPayrollUnit(id)!.FormalName);
            Assert.Single(vm.Company.PayrollUnits);

            EscapeAndReopenList(window, vm, "Payroll Unit");
            ArrowToAndDelete(window, vm, "Days");
            Assert.Empty(vm.Company.PayrollUnits);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    [AvaloniaFact]
    public void Attendance_type_alters_by_identity_and_deletes()
    {
        var (window, vm, dir) = NewWindow("Attendance Co");
        try
        {
            vm.ShowPayrollUnitMaster();
            vm.PayrollUnitMaster!.Symbol = "Days";
            vm.PayrollUnitMaster.FormalName = "Days";
            Assert.True(vm.PayrollUnitMaster.Create(), vm.PayrollUnitMaster.Message);

            vm.ShowAttendanceTypeMaster();
            var master = vm.AttendanceTypeMaster!;
            master.Name = "Present";
            master.SelectedUnit = master.UnitOptions.First(u => u.Unit is not null);
            Assert.True(master.Create(), master.Message);
            var id = vm.Company!.AttendanceTypes.Single(a => a.Name == "Present").Id;

            ArrowToAndAlter(window, vm, "Present");
            Assert.True(vm.AttendanceTypeMaster!.IsAltering);
            vm.AttendanceTypeMaster.Name = "Present Days";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("Present Days", vm.Company.FindAttendanceType(id)!.Name);
            Assert.Single(vm.Company.AttendanceTypes);

            EscapeAndReopenList(window, vm, "Attendance / Production Type");
            ArrowToAndDelete(window, vm, "Present Days");
            Assert.Empty(vm.Company.AttendanceTypes);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE T0-13 TEST (census 7.2 / W7-D2).</b> <see cref="Employee.DateOfLeaving"/> had <b>zero hits
    /// across all of src/Apex.Desktop</b> while three engines read it — so the field persisted, three reports
    /// consulted it, and <b>no keystroke in the product could ever set it</b>. PF <b>Form 10</b> IS the return of
    /// members leaving service during the month and ESI <b>Form 5</b> column 7(A) is "still working", so both
    /// were permanently, silently empty: a capability no user could reach, which the census counts as absent.
    ///
    /// <para>This drives the whole route with real keys — arrow into the existing-employee list, Ctrl+Enter to
    /// alter, type the date, Ctrl+A to accept — and then asserts the SAME employee id carries the date. It fails
    /// on today's <c>main</c> by construction: without the <c>ForAlter</c> factory and the shell arm,
    /// <c>PayrollMasterScreen</c> is null on the employee master and Ctrl+Enter does nothing at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void Employee_alters_by_identity_and_the_date_of_leaving_round_trips_and_deletes()
    {
        var (window, vm, dir) = NewWindow("Employee Alter Co");
        try
        {
            vm.ShowEmployeeGroupMaster();
            vm.EmployeeGroupMaster!.Name = "Sales";
            Assert.True(vm.EmployeeGroupMaster.Create(), vm.EmployeeGroupMaster.Message);

            vm.ShowEmployeeMaster();
            var master = vm.EmployeeMaster!;
            Assert.Equal("Employee Creation", master.Caption);
            master.Name = "Asha Menon";
            master.SelectedGroup = master.GroupOptions.First();
            Assert.True(master.Create(), master.Message);
            var id = vm.Company!.Employees.Single(e => e.Name == "Asha Menon").Id;
            Assert.Null(vm.Company.FindEmployee(id)!.DateOfLeaving);

            ArrowToAndAlter(window, vm, "Asha Menon");
            Assert.True(vm.EmployeeMaster!.IsAltering);
            Assert.Equal("Employee Alteration", vm.EmployeeMaster.Caption);
            // The alter form arrives PRE-FILLED — a form that opened blank would silently blank the master on
            // accept, which is the failure mode a name-only assertion would miss entirely.
            Assert.Equal("Asha Menon", vm.EmployeeMaster.Name);
            Assert.Equal(string.Empty, vm.EmployeeMaster.DateOfLeavingText);

            vm.EmployeeMaster.DateOfLeavingText = "2026-08-31";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Single(vm.Company.Employees);
            Assert.Equal(new DateOnly(2026, 8, 31), vm.Company.FindEmployee(id)!.DateOfLeaving);

            // ...and clearing the box puts the member back into service, rather than leaving the old date
            // standing. A one-way field would make a mis-keyed leaving date permanent.
            vm.EmployeeMaster.DateOfLeavingText = string.Empty;
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Null(vm.Company.FindEmployee(id)!.DateOfLeaving);

            EscapeAndReopenList(window, vm, "Employee");
            ArrowToAndDelete(window, vm, "Asha Menon");
            Assert.Empty(vm.Company.Employees);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The date of leaving survives a <b>save and reload</b>, not merely the in-memory company. The column
    /// <c>employees.date_of_leaving</c> already persisted before this slice; what was missing was any way to put
    /// a value into it, so this pins that the new write actually reaches the store.
    /// </summary>
    [AvaloniaFact]
    public void The_date_of_leaving_survives_a_reload_of_the_company()
    {
        var (window, vm, dir) = NewWindow("Employee Leaving Persist Co");
        try
        {
            vm.ShowEmployeeGroupMaster();
            vm.EmployeeGroupMaster!.Name = "Ops";
            Assert.True(vm.EmployeeGroupMaster.Create(), vm.EmployeeGroupMaster.Message);

            vm.ShowEmployeeMaster();
            var master = vm.EmployeeMaster!;
            master.Name = "Bala Iyer";
            master.SelectedGroup = master.GroupOptions.First();
            Assert.True(master.Create(), master.Message);
            var id = vm.Company!.Employees.Single(e => e.Name == "Bala Iyer").Id;

            ArrowToAndAlter(window, vm, "Bala Iyer");
            vm.EmployeeMaster!.DateOfLeavingText = "2026-07-15";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var storage = new CompanyStorage(dir);
            var reloaded = storage.Load(storage.ListCompanies().Single(e => e.Name == "Employee Leaving Persist Co"));
            Assert.Equal(new DateOnly(2026, 7, 15), reloaded.FindEmployee(id)!.DateOfLeaving);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// A leaving date before the joining date is refused with a message, and the master is left untouched — the
    /// contradiction is caught on the form that holds both dates rather than surfacing later as a PF Form 10 row
    /// in a month before the member existed.
    /// </summary>
    [AvaloniaFact]
    public void A_date_of_leaving_before_the_date_of_joining_is_refused_and_nothing_is_written()
    {
        var (window, vm, dir) = NewWindow("Employee Leaving Guard Co");
        try
        {
            vm.ShowEmployeeGroupMaster();
            vm.EmployeeGroupMaster!.Name = "Ops";
            Assert.True(vm.EmployeeGroupMaster.Create(), vm.EmployeeGroupMaster.Message);

            vm.ShowEmployeeMaster();
            var master = vm.EmployeeMaster!;
            master.Name = "Chandra Rao";
            master.SelectedGroup = master.GroupOptions.First();
            master.DateOfJoiningText = "2026-04-01";
            Assert.True(master.Create(), master.Message);
            var id = vm.Company!.Employees.Single(e => e.Name == "Chandra Rao").Id;

            ArrowToAndAlter(window, vm, "Chandra Rao");
            vm.EmployeeMaster!.DateOfLeavingText = "2026-03-31";
            Assert.False(vm.EmployeeMaster.Create());
            Assert.Contains("earlier than the date of joining", vm.EmployeeMaster.Message);
            Assert.Null(vm.Company.FindEmployee(id)!.DateOfLeaving);
            Assert.Equal(new DateOnly(2026, 4, 1), vm.Company.FindEmployee(id)!.DateOfJoining);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE UNGUARDED DELETE THAT W7-D2 WOULD OTHERWISE HAVE ARMED.</b>
    ///
    /// <para>Putting the employee master on <c>PayrollMasterScreen</c> grants it the arrows, Ctrl+Enter <b>and
    /// Alt+D</b> in one step — that is the interface's whole design. But <c>PayrollService.DeleteEmployee</c> had
    /// <b>no referential guard at all</b>: its doc comment still read <i>"No later master references an employee
    /// in this slice… the attendance/payroll-voucher guard arrives with those slices."</i> Those slices arrived
    /// years of commits ago. Until W7-D2 the method had zero callers, so the gap was theoretical; the moment
    /// Alt+D reaches it, deleting an employee who is on a posted payroll voucher leaves that voucher's line
    /// naming a member the company no longer has.</para>
    ///
    /// <para>This drives the real Alt+D + Y and requires the refusal to reach the operator in the engine's own
    /// words, with the employee still there afterwards.</para>
    /// </summary>
    [AvaloniaFact]
    public void An_employee_with_attendance_recorded_is_refused_by_alt_d_and_survives()
    {
        var (window, vm, dir) = NewWindow("Employee Delete Guard Co");
        try
        {
            var payroll = new PayrollService(vm.Company!);
            payroll.EnablePayroll();
            var group = payroll.CreateEmployeeGroup("Ops");
            var employee = payroll.CreateEmployee("Deepa Nair", group.Id);
            var unit = payroll.CreateSimplePayrollUnit("Days", "Days");
            var type = payroll.CreateAttendanceType(
                "Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: unit.Id);
            vm.Company!.AddAttendanceEntry(new AttendanceEntry(
                Guid.NewGuid(), employee.Id, type.Id,
                new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 26m));

            vm.ShowEmployeeMaster();
            ArrowToAndDelete(window, vm, "Deepa Nair");

            Assert.Contains(vm.Company!.Employees, e => e.Id == employee.Id);
            Assert.Contains("cannot be deleted", vm.Notice ?? string.Empty);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= the guard is asked before the question

    /// <summary>
    /// An employee category that employees are classified under cannot be deleted — the engine already said so
    /// (<c>PayrollService.DeleteEmployeeCategory</c>'s in-use guard) and, before this row, nothing ever asked it.
    /// The refusal must reach the operator as the engine's own words, and the master must still be there
    /// afterwards.
    ///
    /// <para><b>Why the IN-USE guard and not the PREDEFINED one.</b> Both live in the same method, but only the
    /// in-use guard can be reached from the list: a predefined category is one the company seeded, and reaching
    /// it would test the seeder as much as the delete path. An in-use category is one the operator made and then
    /// used — the case a real deletion attempt actually arrives in.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_guarded_payroll_master_is_refused_with_the_engines_own_message_and_survives()
    {
        var (window, vm, dir) = NewWindow("Guarded Co");
        try
        {
            vm.ShowEmployeeCategoryMaster();
            vm.EmployeeCategoryMaster!.Name = "On-Roll";
            Assert.True(vm.EmployeeCategoryMaster.Create(), vm.EmployeeCategoryMaster.Message);

            var category = vm.Company!.EmployeeCategories.Single(c => c.Name == "On-Roll");

            // An employee must belong to a GROUP as well as (optionally) a category — CreateEmployee refuses an
            // unknown group id — so the classifying group is created first and the category passed as the
            // optional third argument. Getting this wrong is what left this file uncompilable.
            var payroll = new PayrollService(vm.Company);
            var group = payroll.CreateEmployeeGroup("Primary");
            payroll.CreateEmployee("Asha", group.Id, category.Id);
            vm.EmployeeCategoryMaster.ReloadExisting();

            ArrowToAndDelete(window, vm, "On-Roll");

            Assert.Contains(vm.Company.EmployeeCategories, c => c.Id == category.Id);
            Assert.Contains("cannot be deleted", vm.Notice ?? string.Empty);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+D must be inert while a master is OPEN FOR ALTERATION — deleting the record you are part-way
    /// through editing is never what was meant. This mirrors the Stock Item master's own rule.</summary>
    [AvaloniaFact]
    public void Alt_d_is_inert_while_a_payroll_master_is_open_for_alteration()
    {
        var (window, vm, dir) = NewWindow("Alter Guard Co");
        try
        {
            vm.ShowEmployeeGroupMaster();
            vm.EmployeeGroupMaster!.Name = "Admin";
            Assert.True(vm.EmployeeGroupMaster.Create(), vm.EmployeeGroupMaster.Message);

            ArrowToAndAlter(window, vm, "Admin");
            Assert.True(vm.EmployeeGroupMaster!.IsAltering);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Assert.False(vm.IsAcceptPromptOpen, "Alt+D raised a delete prompt over an open alteration");
            Assert.Single(vm.Company!.EmployeeGroups);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
