using System;
using System.Collections.Generic;
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
/// <para>Row 7.16 asks for Alter + Delete on <b>eight</b> payroll master kinds. <b>Six</b> are done and driven
/// end-to-end by <see cref="PayrollMasterAlterDeleteTests"/>: employee category, employee group, payroll unit,
/// attendance/production type, the <b>employee</b> master (W7-D2) and — since W28 V3 — the <b>pay head</b>
/// (census 7.6 / defect T2-38). The other two are NOT: the salary-structure and tax-declaration masters were
/// never considered, and the salary structure is blocked one level further back in the same way the pay head was
/// (<c>PayrollService</c> has no alter or delete for it). This fixture is the LOCK that keeps that fact true in
/// the code rather than only in a report — an over-claimed row is this project's most-repeated defect, and a
/// report cannot go red.</para>
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
    /// Trap 2, RE-AIMED at the remainder (W28 V3). The two PAY HEAD clauses that used to live here — "the pay
    /// head master is not yet on the verb surface" and "PayHeadService still has no Alter method" — were
    /// retired because both became FALSE: <c>PayHeadService.AlterPayHead</c> exists, the view model implements
    /// <see cref="IPayrollMasterList"/> and carries <c>ForAlter</c>, and the kind is driven end-to-end by
    /// <c>PayrollMasterAlterDeleteTests.Pay_head_alters_a_mistyped_rate_by_identity_and_deletes</c> plus the
    /// highlight fixture.
    /// That is the retirement this file's own remarks prescribe, and the coverage is asserted in the OTHER
    /// direction there rather than merely dropped — which is what stops a retirement being a quiet loss.
    ///
    /// <para>What is left is the honest remainder of row 7.16: <b>SIX of eight</b>. The salary-structure and
    /// tax-declaration masters are still absent, and the salary structure is not merely unwired —
    /// <c>PayrollService</c> has no alter or delete for it either, and what "alter" means for a structure already
    /// used to pay a period is an open scoping question (census 7.7). When someone closes those, this test goes
    /// RED, which is the intended signal to retire it the same way.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_salary_structure_master_is_not_yet_on_the_payroll_master_verb_surface()
    {
        var (vm, dir) = NewCompany("Half Wired Co");
        try
        {
            vm.ShowSalaryStructureMaster();
            Assert.Equal(Screen.SalaryStructureMaster, vm.CurrentScreen);
            Assert.True(vm.SalaryDetails is not null,
                "the salary structure master did not open; this would assert nothing");
            Assert.Null(vm.PayrollMasterScreen);
            Assert.False(vm.AlterHighlightedPayrollMasterRow());
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The engine-side remainder, pinned so the report and the code agree. <c>PayrollService</c> gained an Alter
    /// for each of the five master kinds it owns and <c>PayHeadService</c> gained one at W28 V3 — but the salary
    /// structure has none, so the salary-structure half of row 7.16 cannot be finished in the view model alone
    /// and any plan that says otherwise is wrong.
    ///
    /// <para>🔴 <b>IT IS A CLOSED ALLOW-LIST, NOT A NAME SUBSTRING, AND THAT IS THE POINT (review finding
    /// F10).</b> The retired version of this lock matched any method on <c>PayHeadService</c> whose name STARTED
    /// WITH "Alter" — a closed question, because that service owned one master. Re-aiming it at
    /// <c>PayrollService</c>, which already owns five legitimate Alters, needed a way to exclude them, and the
    /// first attempt additionally required the name to CONTAIN "SalaryStructure". That quietly narrowed the lock
    /// to a naming guess: <c>AlterStructure</c>, <c>AlterSalaryDetails</c> or <c>AlterStructureLine</c> would all
    /// have slipped past it and the lock would have stayed GREEN through the very landing it exists to detect.
    /// Listing the five shipped pairs BY NAME instead makes the question closed again — any new alter or delete
    /// verb on this service, whatever it is called, reddens this test and has to be accounted for.</para>
    /// </summary>
    [Fact]
    public void PayrollService_still_has_no_alter_for_the_salary_structure()
    {
        // The alter/delete verbs PayrollService is known to own — the five master kinds of row 7.16 that were
        // already wired. Anything outside this set is a new master verb that has not been accounted for.
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "AlterEmployeeCategory", "DeleteEmployeeCategory",
            "AlterEmployeeGroup", "DeleteEmployeeGroup",
            "AlterEmployee", "DeleteEmployee",
            "AlterPayrollUnit", "DeletePayrollUnit",
            "AlterAttendanceType", "DeleteAttendanceType",
        };

        var unexpected = typeof(PayrollService).GetMethods()
            .Where(m => m.Name.StartsWith("Alter", StringComparison.Ordinal)
                        || m.Name.StartsWith("Delete", StringComparison.Ordinal))
            .Select(m => m.Name)
            .Where(n => !known.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(unexpected.Count == 0,
            "PayrollService now exposes " + string.Join(", ", unexpected) + ". If salary-structure alteration " +
            "has been built, finish the row: wire SalaryStructureMasterViewModel onto IPayrollMasterList, give " +
            "it ForAlter, add the highlight bar to its row template, and delete this test. If this is a new " +
            "verb for one of the five kinds already wired, add its name to the allow-list above — deliberately, " +
            "not reflexively.");

        // The five it DOES own are asserted present, so the allow-list cannot go stale into vacuity: a renamed
        // or deleted verb would otherwise leave the set matching nothing and the test passing for no reason.
        var shipped = typeof(PayrollService).GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in known)
            Assert.True(shipped.Contains(name),
                $"PayrollService no longer exposes {name}; the allow-list in this lock is stale.");
    }

    /// <summary>
    /// 🔴 <b>The counter-lock for the half that DID land.</b> Retiring the two pay-head clauses above could have
    /// been a silent loss of coverage, so the fact they were retired FOR A REASON is asserted here: the pay head
    /// master must now resolve as a payroll master list, and <c>PayHeadService</c> must really expose an Alter.
    /// If either regresses, this goes red rather than the absence simply going unnoticed.
    /// </summary>
    [AvaloniaFact]
    public void The_pay_head_master_is_now_on_the_payroll_master_verb_surface()
    {
        var (vm, dir) = NewCompany("Pay Head Wired Co");
        try
        {
            Assert.Contains(
                typeof(PayHeadService).GetMethods(),
                m => m.Name == "AlterPayHead");

            vm.ShowPayHeadMaster();
            Assert.Equal(Screen.PayHeadMaster, vm.CurrentScreen);
            Assert.NotNull(vm.PayHeadMaster);
            Assert.NotNull(vm.PayrollMasterScreen);
            Assert.Equal("pay head", vm.PayrollMasterScreen!.MasterKindLabel);
        }
        finally { Cleanup(dir); }
    }
}
