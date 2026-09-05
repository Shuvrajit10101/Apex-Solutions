using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE HONEST STATE OF CENSUS ROW 7.16 — and the two traps the half-finished half leaves behind.</b>
///
/// <para>Row 7.16 asks for Alter + Delete on <b>eight</b> payroll master kinds. <b>Four</b> are done and driven
/// end-to-end by <see cref="PayrollMasterAlterDeleteTests"/>: employee category, employee group, payroll unit and
/// attendance/production type. The other four are NOT: <c>EmployeeMasterViewModel</c> and
/// <c>PayHeadMasterViewModel</c> implement neither <see cref="IPayrollMasterList"/> nor a <c>ForAlter</c> factory
/// (and <c>PayHeadService</c> has no Alter method at all), and the salary-structure and tax-declaration masters
/// were never considered. This fixture is the LOCK that keeps that fact true in the code rather than only in a
/// report — an over-claimed row is this project's most-repeated defect, and a report cannot go red.</para>
///
/// <para><b>Trap 1 — a destructive verb on an id-less row.</b> <c>EmployeeListRow</c> was given
/// <see cref="IPayrollMasterListRow.MasterId"/> but <c>RefreshList</c> was never updated to fill it, so every
/// employee row carried <see cref="Guid.Empty"/>. Wiring the employee master onto the Alt+D surface in that state
/// would have armed a delete confirmation naming one employee against an id that resolves to none — or, once a
/// lookup is added, to whichever record happens to answer for the empty id. The row must carry a REAL identity
/// before anything destructive is ever pointed at it, so this is fixed now rather than left as a landmine for
/// whoever finishes the slice.</para>
///
/// <para><b>Trap 2 — the shell must not silently pick these kinds up.</b> Until a kind genuinely implements the
/// interface it must not appear on <c>MainWindowViewModel.PayrollMasterScreen</c>, because appearing there is
/// what grants it the arrows, Ctrl+Enter AND Alt+D in one step.</para>
/// </summary>
public sealed class PayrollMasterHalfWiredKindsTests
{
    private static (MainWindowViewModel Vm, string TempDir) NewCompany(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexHalfWired_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.Company!.PayrollEnabled = true;
        return (vm, tempDir);
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

    /// <summary>
    /// Trap 1. Every employee row must resolve back to the employee it displays. Asserted against the ENGINE's
    /// employee — a row whose id merely differs from <see cref="Guid.Empty"/> is not enough; it has to be the
    /// right one, or a future delete deletes the wrong person.
    /// </summary>
    [AvaloniaFact]
    public void Every_employee_list_row_resolves_back_to_the_employee_it_displays()
    {
        var (vm, dir) = NewCompany("Employee Row Id Co");
        try
        {
            var payroll = new PayrollService(vm.Company!);
            var group = payroll.CreateEmployeeGroup("Primary");
            var asha = payroll.CreateEmployee("Asha", group.Id);
            var bala = payroll.CreateEmployee("Bala", group.Id);

            vm.ShowEmployeeMaster();
            var rows = vm.EmployeeMaster!.Existing;
            Assert.Equal(2, rows.Count);

            foreach (var row in rows)
                Assert.True(
                    row.MasterId != Guid.Empty,
                    $"The employee row '{row.Name}' carries Guid.Empty as its MasterId. Rows on the payroll " +
                    "master lists exist so a destructive verb can name a target; an empty id names nothing, " +
                    "and pointing Alt+D at it would confirm the deletion of one employee while acting on " +
                    "another (or on none). Fill MasterId in EmployeeMasterViewModel.RefreshList.");

            Assert.Equal(asha.Id, rows.Single(r => r.Name == "Asha").MasterId);
            Assert.Equal(bala.Id, rows.Single(r => r.Name == "Bala").MasterId);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// Trap 2. The employee master is NOT on the Alt+D / Ctrl+Enter surface yet, and must not be until it really
    /// implements <see cref="IPayrollMasterList"/> with a working <c>ForAlter</c>. When someone finishes the
    /// slice, this test goes RED — which is the correct and intended signal to delete it and add the kind to
    /// <see cref="PayrollMasterAlterDeleteTests"/> and
    /// <see cref="PayrollMasterHighlightVisibilityTests"/> instead. It is a "not yet", not a "never".
    /// </summary>
    [AvaloniaFact]
    public void The_employee_and_pay_head_masters_are_not_yet_on_the_payroll_master_verb_surface()
    {
        var (vm, dir) = NewCompany("Half Wired Co");
        try
        {
            vm.ShowEmployeeMaster();
            Assert.Equal(Screen.EmployeeMaster, vm.CurrentScreen);
            Assert.True(vm.EmployeeMaster is not null, "the employee master did not open; this would assert nothing");
            Assert.Null(vm.PayrollMasterScreen);
            Assert.False(vm.AlterHighlightedPayrollMasterRow());

            vm.ShowPayHeadMaster();
            Assert.Equal(Screen.PayHeadMaster, vm.CurrentScreen);
            Assert.True(vm.PayHeadMaster is not null, "the pay head master did not open; this would assert nothing");
            Assert.Null(vm.PayrollMasterScreen);
            Assert.False(vm.AlterHighlightedPayrollMasterRow());
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The engine-side remainder, pinned so the report and the code agree. <c>PayrollService</c> gained an Alter
    /// for each of the five master kinds it owns, but <c>PayHeadService</c> has none — so the pay-head half of
    /// row 7.16 cannot be finished in the view model alone, and any plan that says otherwise is wrong.
    /// </summary>
    [Fact]
    public void PayHeadService_still_has_no_alter_method()
    {
        var alters = typeof(PayHeadService).GetMethods()
            .Where(m => m.Name.StartsWith("Alter", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToList();

        Assert.True(alters.Count == 0,
            "PayHeadService now exposes " + string.Join(", ", alters) + ". If pay-head alteration has been " +
            "built, finish the row: wire PayHeadMasterViewModel onto IPayrollMasterList, give it ForAlter, add " +
            "the highlight bar to its row template, and delete this test.");
    }
}
