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
/// <b>T0-12, desktop half — the correction route that makes the duplicate refusal fair.</b>
/// <para>
/// The engine's <see cref="PayrollAttendanceService.Delete"/> shipped with <b>zero callers in
/// <c>src/Apex.Desktop</c></b>, so a wrongly-keyed attendance figure was permanent: the operator's only recourse
/// was to record it again, which ADDED to it and paid an On-Attendance head twice. The screen now (a) surfaces the
/// engine's refusal of an exact-duplicate period as a message instead of silently doubling, and (b) offers a Remove
/// on every recorded row so the wrong entry can actually be taken out and the right one recorded.
/// </para>
/// <para>
/// The attendance entry screen had no desktop test file at all before this one.
/// </para>
/// </summary>
public sealed class AttendanceEntryRemovalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AttendanceEntryRemovalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAttendanceTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private (MainWindowViewModel Vm, Employee Emp, AttendanceType Type) NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var c = vm.Company!;
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var type = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var emp = pay.CreateEmployee("Asha Rao", pay.CreateEmployeeGroup("Staff").Id);
        _storage.Save(c);
        return (vm, emp, type);
    }

    private static void Key(AttendanceVoucherEntryViewModel m, Employee emp, AttendanceType type, string value)
    {
        var row = m.Rows.Last();
        row.SelectedEmployee = m.Employees.Single(e => e.Id == emp.Id);
        row.SelectedAttendanceType = m.AttendanceTypes.Single(t => t.Id == type.Id);
        row.ValueText = value;
    }

    /// <summary>
    /// 🔴 THE CONSTRUCTED FAILURE, at the screen. Keying the same period twice used to record two entries and
    /// double the pay. The second Accept is now refused with a message that says so, and the book still holds
    /// exactly one entry.
    /// </summary>
    [Fact]
    public void A_second_identical_attendance_run_is_refused_with_a_message()
    {
        var (vm, emp, type) = NewPayrollCompany("Attendance Screen Co");

        vm.ShowAttendanceVoucher();
        var m = vm.AttendanceVoucher!;
        m.PeriodFromText = "01-04-2025";
        m.PeriodToText = "30-04-2025";
        Key(m, emp, type, "26");
        Assert.True(m.Accept(), m.Message);
        Assert.Single(vm.Company!.AttendanceEntries);

        // The re-key of the identical period.
        Key(m, emp, type, "26");
        Assert.False(m.Accept());
        Assert.Contains("already recorded", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(vm.Company!.AttendanceEntries);
    }

    /// <summary>
    /// 🔴 THE REMOVE ROUTE. The recorded row carries the entry id, the Remove command deletes it through the
    /// engine, and the deletion survives a save + reload — so the operator can correct a wrong figure by removing
    /// it and recording the right one. Without this the duplicate refusal would leave a wrong attendance figure
    /// permanently uncorrectable.
    /// </summary>
    [Fact]
    public void Remove_deletes_the_recorded_entry_and_frees_the_period_for_a_correction()
    {
        var (vm, emp, type) = NewPayrollCompany("Attendance Remove Co");

        vm.ShowAttendanceVoucher();
        var m = vm.AttendanceVoucher!;
        m.PeriodFromText = "01-04-2025";
        m.PeriodToText = "30-04-2025";
        Key(m, emp, type, "26");
        Assert.True(m.Accept(), m.Message);

        var row = Assert.Single(m.RecentEntries);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal("Asha Rao", row.Employee);

        m.RemoveEntryCommand.Execute(row);

        Assert.Empty(vm.Company!.AttendanceEntries);
        Assert.Empty(m.RecentEntries);
        Assert.Contains("Removed", m.Message!, StringComparison.OrdinalIgnoreCase);

        var entry = _storage.ListCompanies().Single(e => e.Name == "Attendance Remove Co");
        Assert.Empty(_storage.Load(entry).AttendanceEntries);

        // The period is free again, so the corrected figure records.
        Key(m, emp, type, "24");
        Assert.True(m.Accept(), m.Message);
        Assert.Equal(24m, Assert.Single(vm.Company!.AttendanceEntries).Value);
    }

    /// <summary>
    /// Removing an entry that is no longer there reports rather than crashes the screen — the row list is a
    /// snapshot and two screens can be open over one book.
    /// </summary>
    [Fact]
    public void Removing_an_already_deleted_entry_reports_instead_of_crashing()
    {
        var (vm, emp, type) = NewPayrollCompany("Attendance Stale Co");

        vm.ShowAttendanceVoucher();
        var m = vm.AttendanceVoucher!;
        m.PeriodFromText = "01-04-2025";
        m.PeriodToText = "30-04-2025";
        Key(m, emp, type, "26");
        Assert.True(m.Accept(), m.Message);

        var row = Assert.Single(m.RecentEntries);
        new PayrollAttendanceService(vm.Company!).Delete(row.Id);   // deleted behind the screen's back

        m.RemoveEntryCommand.Execute(row);
        Assert.Contains("Could not remove", m.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 🔴 T0-12 FOLLOW-ON — the PARTIAL-BATCH window the duplicate refusal opened, and its two cases.
    //
    // Making Record throw on a duplicate was right, but it was the first Record guard this screen does not
    // pre-validate, so it was the first that could fire MID-BATCH. Record mutates the open company row by row via
    // AddAttendanceEntry; _storage.Save runs only AFTER the loop. Measured on the un-pre-validated build, a 5-row
    // run whose 3rd row duplicated left the company holding THREE entries against ONE on disk, with the read-back
    // list still showing one — two entries neither saved nor displayed. Pressing Remove on the one visible row
    // then took disk from 1 entry to 2 GHOSTS: the operator asked to delete an entry and silently committed two
    // they had been told were not recorded.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A payroll book with N named employees and one attendance type.</summary>
    private (MainWindowViewModel Vm, Employee[] Emps, AttendanceType Type) NewPayrollCompany(string name, int employees)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var c = vm.Company!;
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var type = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var group = pay.CreateEmployeeGroup("Staff").Id;
        var emps = Enumerable.Range(1, employees).Select(i => pay.CreateEmployee($"E{i}", group)).ToArray();
        _storage.Save(c);
        return (vm, emps, type);
    }

    private AttendanceVoucherEntryViewModel OpenApril(MainWindowViewModel vm)
    {
        vm.ShowAttendanceVoucher();
        var m = vm.AttendanceVoucher!;
        m.PeriodFromText = "01-04-2025";
        m.PeriodToText = "30-04-2025";
        return m;
    }

    private Company OnDisk(string companyName)
        => _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    /// <summary>
    /// 🔴 CASE 1 — a line duplicating an ALREADY-RECORDED entry. Row 3 of a 5-row run collides with an entry
    /// already on the book. The run is refused BEFORE any row mutates the company, so the company still holds the
    /// one pre-existing entry, disk agrees, and the displayed list agrees — where the un-pre-validated build left
    /// 3 in memory, 1 on disk and 1 displayed. The message names the LINE. Pressing Remove afterwards writes no
    /// ghosts, which is the operation that used to commit them.
    /// </summary>
    [Fact]
    public void A_line_duplicating_a_recorded_entry_refuses_the_whole_run_before_any_row_is_recorded()
    {
        const string name = "Attendance Batch Recorded Co";
        var (vm, emps, type) = NewPayrollCompany(name, 5);
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);

        // E3's April is already on the book AND on disk.
        new PayrollAttendanceService(vm.Company!).Record(emps[2].Id, type.Id, from, to, 26m);
        _storage.Save(vm.Company!);

        var m = OpenApril(vm);
        foreach (var e in emps) Key(m, e, type, "26");   // five lines; the third collides

        Assert.False(m.Accept());

        // Nothing from the run reached the company — the whole point. 3-in-memory-against-1-on-disk was the defect.
        Assert.Single(vm.Company!.AttendanceEntries);
        Assert.Equal(emps[2].Id, vm.Company!.AttendanceEntries[0].EmployeeId);
        Assert.Single(OnDisk(name).AttendanceEntries);
        Assert.Single(m.RecentEntries);                  // the display is not stale

        // The operator is told WHICH line, and that nothing was recorded.
        Assert.Contains("Line 3", m.Message!, StringComparison.Ordinal);
        Assert.Contains("E3", m.Message!, StringComparison.Ordinal);
        Assert.Contains("already recorded", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was recorded", m.Message!, StringComparison.OrdinalIgnoreCase);

        // 🔴 The operation that used to persist the ghosts. Removing the one real entry must empty the book, not
        // commit E1 and E2 alongside it.
        m.RemoveEntryCommand.Execute(m.RecentEntries.Single());
        Assert.Empty(vm.Company!.AttendanceEntries);
        Assert.Empty(OnDisk(name).AttendanceEntries);
    }

    /// <summary>
    /// 🔴 CASE 2 — two lines of ONE run duplicating EACH OTHER. Nothing is on the book, so checking each line
    /// against the company alone sees no collision: the earlier line is not in the company yet and does not get
    /// there until the record loop. Without a per-run check the loop still threw mid-batch. Here line 3 repeats
    /// line 1's employee × type over the same period; the run is refused with the company, disk and display all
    /// still empty.
    /// </summary>
    [Fact]
    public void Two_lines_of_one_run_duplicating_each_other_are_refused_before_any_row_is_recorded()
    {
        const string name = "Attendance Batch SelfDup Co";
        var (vm, emps, type) = NewPayrollCompany(name, 5);

        var m = OpenApril(vm);
        Key(m, emps[0], type, "26");
        Key(m, emps[1], type, "26");
        Key(m, emps[0], type, "24");   // line 3 repeats line 1 — the "correction" that would have been ADDED
        Key(m, emps[3], type, "26");

        Assert.False(m.Accept());

        Assert.Empty(vm.Company!.AttendanceEntries);     // was 2 in memory against 0 on disk
        Assert.Empty(OnDisk(name).AttendanceEntries);
        Assert.Empty(m.RecentEntries);

        Assert.Contains("Line 3", m.Message!, StringComparison.Ordinal);
        Assert.Contains("E1", m.Message!, StringComparison.Ordinal);
        Assert.Contains("earlier line", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was recorded", m.Message!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pre-validation must not OVER-refuse. A run may legitimately carry the same employee twice under
    /// different attendance types, and the same type for many employees — the refusal is on employee × type over
    /// the run's one period, exactly as the engine's is. All four lines record.
    /// </summary>
    [Fact]
    public void A_run_repeating_an_employee_under_a_different_type_still_records_every_line()
    {
        const string name = "Attendance Batch Wide Co";
        var (vm, emps, type) = NewPayrollCompany(name, 5);
        var overtime = new PayrollService(vm.Company!).CreateAttendanceType("Overtime", AttendanceTypeKind.Production);

        var m = OpenApril(vm);             // opened after both types exist, so the screen lists both
        Key(m, emps[0], type, "26");
        Key(m, emps[0], overtime, "10");   // same employee, different type — legitimate
        Key(m, emps[1], type, "26");       // same type, different employee — legitimate
        Key(m, emps[2], type, "24");

        Assert.True(m.Accept(), m.Message);
        Assert.Equal(4, vm.Company!.AttendanceEntries.Count);
        Assert.Equal(4, OnDisk(name).AttendanceEntries.Count);
    }

    /// <summary>
    /// 🔴 THE ROLL-BACK, which is the general form of the same defect. The pre-validation is a second layer, not
    /// the only one: <see cref="PayrollAttendanceService.Record"/> keeps its own guards and can still throw
    /// mid-run for a condition this screen cannot pre-check. Here an employee is deleted from the book behind the
    /// screen's back (two screens over one company — the same premise as the stale-Remove test above), so the
    /// row's cached <see cref="Employee"/> passes every screen check and Record throws "not found" on line 2 with
    /// line 1 already in the company. The part-run is undone, so the company and disk agree on empty and no later
    /// save can commit line 1.
    /// </summary>
    [Fact]
    public void An_engine_throw_midway_through_a_run_rolls_the_already_recorded_lines_back()
    {
        const string name = "Attendance Batch Rollback Co";
        var (vm, emps, type) = NewPayrollCompany(name, 2);

        var m = OpenApril(vm);
        Key(m, emps[0], type, "26");
        Key(m, emps[1], type, "26");

        // Deleted from another screen after the rows were keyed; m.Employees still holds the object.
        new PayrollService(vm.Company!).DeleteEmployee(emps[1].Id);

        Assert.False(m.Accept());
        Assert.Contains("Could not record attendance", m.Message!, StringComparison.OrdinalIgnoreCase);

        // Line 1 was recorded into the company before line 2 threw; the roll-back takes it back out.
        Assert.Empty(vm.Company!.AttendanceEntries);
        Assert.Empty(OnDisk(name).AttendanceEntries);

        // And the ghost cannot be committed by a later save from any screen.
        _storage.Save(vm.Company!);
        Assert.Empty(OnDisk(name).AttendanceEntries);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
