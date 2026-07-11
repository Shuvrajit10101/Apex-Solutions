using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-8 slice-1 payroll-masters persistence contract: a company with Payroll enabled, employee categories,
/// hierarchical employee groups, employees (all identity/statutory/bank fields + tax regime), simple + compound
/// payroll units and hierarchical attendance types SAVES and RELOADS at <see cref="Schema.CurrentVersion"/>,
/// preserving every master, hierarchy and field; and a company that never enables Payroll reloads with the flags
/// off and no payroll masters (ER-13).
/// </summary>
public sealed class PayrollRoundTripTests
{
    private const string ValidPan = "AAPFU0939F";
    private const string ValidUan = "100200300400";
    private const string ValidEsi = "31001234560000101";

    private static Company SeedWithPayroll()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Persist Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var svc = new PayrollService(c);
        svc.EnablePayroll();

        var direct = svc.CreateEmployeeCategory("Direct",
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
        e.DateOfLeaving = null;
        e.PfAccountNumber = "MH/BAN/1234567/000/0001234";
        e.Aadhaar = "123412341234";
        e.BankAccountNumber = "1234567890";
        e.BankName = "State Bank";
        e.BankIfsc = "SBIN0001234";
        e.ApplicableTaxRegime = TaxRegime.Old;

        return c;
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Payroll_masters_and_flags_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-payroll-rt-{Guid.NewGuid():N}.db");
        try
        {
            var original = SeedWithPayroll();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 30);
            }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            // F11 flags.
            Assert.True(r.PayrollEnabled);
            Assert.True(r.PayrollStatutoryEnabled);

            // Categories — the revenue/non-revenue allocation axis survives the round-trip.
            var cat = Assert.Single(r.EmployeeCategories);
            Assert.Equal("Direct", cat.Name);
            Assert.False(cat.AllocateRevenueItems);
            Assert.True(cat.AllocateNonRevenueItems);

            // Groups + hierarchy + alias + define-salary flag.
            Assert.Equal(2, r.EmployeeGroups.Count);
            var admin = r.FindEmployeeGroupByName("Administration")!;
            var mkt = r.FindEmployeeGroupByName("Marketing")!;
            Assert.True(admin.IsPrimary);
            Assert.Equal(admin.Id, mkt.ParentId);
            Assert.Equal("MKT", mkt.Alias);
            Assert.True(mkt.DefineSalaryDetails);

            // Payroll units: 3 simple + 1 compound; compound wiring + precision preserved.
            Assert.Equal(4, r.PayrollUnits.Count);
            var hrs = r.FindPayrollUnitByName("Hrs")!;
            Assert.Equal(2, hrs.DecimalPlaces);
            var compound = r.FindPayrollUnitByName("Hrs of 60 Min")!;
            Assert.True(compound.IsCompound);
            Assert.Equal(hrs.Id, compound.FirstUnitId);
            Assert.Equal(60, compound.ConversionNumerator);

            // Attendance types: kind + hierarchy + unit link.
            Assert.Equal(3, r.AttendanceTypes.Count);
            var present = r.FindAttendanceTypeByName("Present")!;
            var absent = r.FindAttendanceTypeByName("Absent")!;
            var ot = r.FindAttendanceTypeByName("Overtime")!;
            Assert.Equal(AttendanceTypeKind.AttendancePaid, present.Kind);
            Assert.Equal(present.Id, absent.ParentId);
            Assert.Equal(AttendanceTypeKind.LeaveWithoutPay, absent.Kind);
            Assert.Equal(hrs.Id, ot.PayrollUnitId);

            // Employee: every field + FK + tax regime preserved.
            var e = Assert.Single(r.Employees);
            Assert.Equal("Rajkumar Sharma", e.Name);
            Assert.Equal(mkt.Id, e.EmployeeGroupId);
            Assert.Equal(cat.Id, e.EmployeeCategoryId);
            Assert.Equal("EMP-001", e.EmployeeNumber);
            Assert.Equal(new DateOnly(2015, 4, 1), e.DateOfJoining);
            Assert.Null(e.DateOfLeaving);
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
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Company_without_payroll_reloads_with_flags_off_and_no_masters()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-payroll-off-{Guid.NewGuid():N}.db");
        try
        {
            var original = CompanyFactory.CreateSeeded("No Payroll Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            Assert.False(r.PayrollEnabled);
            Assert.False(r.PayrollStatutoryEnabled);
            Assert.Empty(r.Employees);
            Assert.Empty(r.EmployeeGroups);
            Assert.Empty(r.EmployeeCategories);
            Assert.Empty(r.PayrollUnits);
            Assert.Empty(r.AttendanceTypes);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }
}
