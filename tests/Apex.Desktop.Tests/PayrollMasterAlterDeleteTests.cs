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
/// <para>🔴 <b>SCOPE — this file covers FOUR of the eight kinds, and row 7.16 is NOT closed.</b> Employee
/// category, employee group, payroll unit and attendance/production type are driven end-to-end below. The
/// employee, pay head, salary structure and tax declaration masters are not built;
/// <see cref="PayrollMasterHalfWiredKindsTests"/> locks that remainder so it cannot be quietly claimed. A green
/// run of this file is evidence for four kinds and for nothing else.</para>
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
