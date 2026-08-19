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

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
