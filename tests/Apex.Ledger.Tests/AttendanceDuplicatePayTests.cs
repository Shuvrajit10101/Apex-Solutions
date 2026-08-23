using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>T0-12 — recording the same attendance period twice silently DOUBLED the pay.</b>
/// <para>
/// <see cref="PayrollAttendanceService.Record"/> always appended a fresh <see cref="AttendanceEntry"/> with no
/// dedupe on employee × attendance type × period, and <c>PayrollComputationService</c> SUMS every matching entry.
/// So an operator who keyed the month's attendance, was unsure it had saved, and keyed it again produced two
/// identical entries and an On-Attendance pay head paid twice — with no in-app route to remove either, because
/// <see cref="PayrollAttendanceService.Delete"/> has no caller in the desktop layer and there is no voucher
/// alteration. Real money, real salaries.
/// </para>
/// <para>
/// 🔴 <b>THE FIX IS A REFUSAL, AND IT IS A DELIBERATE NARROWING OF AN ATTESTED BEHAVIOUR — labelled, not
/// disguised.</b> In the reference application an Attendance voucher is a voucher: two of them for the same period
/// sum, and the operator alters or deletes one to correct it. We refuse the exact duplicate instead, because the
/// compensating control does not exist here — census T1-1 (no voucher alteration) and T0-12's own evidence (the
/// delete service has zero desktop callers). A refusal costs the operator a keystroke and is recoverable; a silent
/// double is unrecoverable and is a wrong figure in a salary. This narrowing should be lifted when voucher
/// alteration/deletion reaches the attendance surface. It is NOT "corpus silent, ours by design" — the corpus is
/// not silent; we are deliberately narrower.
/// </para>
/// <para>
/// The refusal is <b>exact</b>: same employee, same attendance type, same From and same To. A different period —
/// including one that overlaps but does not coincide — still records, because the computation already pro-rates
/// overlapping spans and splitting a month into two genuine records is ordinary practice.
/// </para>
/// </summary>
public sealed class AttendanceDuplicatePayTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Attendance Dedupe Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;

    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    /// <summary>Builds a payroll book with one employee on a ₹26,000/26-day On-Attendance head.</summary>
    private static (Company Company, Guid EmployeeId, Guid AttendanceTypeId) BuildAttendanceBook()
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var head = ph.CreatePayHead("Attendance Pay", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c), attendanceTypeId: present.Id, perDayCalculationBasisDays: 26);
        var emp = pay.CreateEmployee("E1", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom,
            new[] { new SalaryStructureLine(head.Id, 0, new Money(26_000m)) });

        return (c, emp.Id, present.Id);
    }

    /// <summary>
    /// 🔴 THE CONSTRUCTED FAILURE. 26 present days on a ₹26,000 / 26-day head ⇒ <b>₹26,000.00</b>. Recording the
    /// identical period a second time used to append a second entry and the computation summed both, paying
    /// <b>₹52,000.00</b> — exactly double, for one month's work. The second record is now refused, the book still
    /// holds exactly ONE entry, and the payslip is ₹26,000.00.
    /// </summary>
    [Fact]
    public void Recording_the_same_period_twice_no_longer_doubles_the_pay()
    {
        var (c, empId, typeId) = BuildAttendanceBook();
        var svc = new PayrollAttendanceService(c);

        svc.Record(empId, typeId, PeriodFrom, PeriodTo, 26m);
        Assert.Equal(new Money(26_000m),
            new PayrollComputationService(c).Compute(empId, PeriodFrom, PeriodTo).GrossEarnings);

        // The re-key. Refused, naming the entry that already exists.
        var again = Assert.Throws<InvalidOperationException>(
            () => svc.Record(empId, typeId, PeriodFrom, PeriodTo, 26m));
        Assert.Contains("already", again.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Single(c.AttendanceEntries);
        var gross = new PayrollComputationService(c).Compute(empId, PeriodFrom, PeriodTo).GrossEarnings;
        Assert.Equal(new Money(26_000m), gross);
        Assert.NotEqual(new Money(52_000m), gross);   // the pre-fix figure
    }

    /// <summary>
    /// A duplicate with a DIFFERENT value is refused too — that is the more dangerous re-key, because the operator
    /// believes they are correcting the figure and instead gets the sum of both. 26 days then a "corrected" 24
    /// would have paid ₹50,000.00 (26,000 + 24,000) on a ₹26,000 head.
    /// </summary>
    [Fact]
    public void A_duplicate_period_with_a_different_value_is_refused_not_added_to_the_first()
    {
        var (c, empId, typeId) = BuildAttendanceBook();
        var svc = new PayrollAttendanceService(c);

        svc.Record(empId, typeId, PeriodFrom, PeriodTo, 26m);
        Assert.Throws<InvalidOperationException>(() => svc.Record(empId, typeId, PeriodFrom, PeriodTo, 24m));

        Assert.Single(c.AttendanceEntries);
        var gross = new PayrollComputationService(c).Compute(empId, PeriodFrom, PeriodTo).GrossEarnings;
        Assert.Equal(new Money(26_000m), gross);
        Assert.NotEqual(new Money(50_000m), gross);   // what the un-deduped sum would have paid
    }

    /// <summary>
    /// The correction route, which is what makes the refusal fair: delete the wrong entry, then record the right
    /// one. After deleting the 26-day entry and recording 24 days for the same period, the payslip is
    /// <b>₹24,000.00</b> and there is still exactly one entry.
    /// </summary>
    [Fact]
    public void Deleting_the_entry_frees_the_period_so_a_correction_can_be_recorded()
    {
        var (c, empId, typeId) = BuildAttendanceBook();
        var svc = new PayrollAttendanceService(c);

        var first = svc.Record(empId, typeId, PeriodFrom, PeriodTo, 26m);
        svc.Delete(first.Id);
        svc.Record(empId, typeId, PeriodFrom, PeriodTo, 24m);

        Assert.Single(c.AttendanceEntries);
        Assert.Equal(new Money(24_000m),
            new PayrollComputationService(c).Compute(empId, PeriodFrom, PeriodTo).GrossEarnings);
    }

    /// <summary>
    /// 🔴 THE NARROWNESS GUARD. The refusal is on the EXACT period only. Two genuine half-month records — 1–15 and
    /// 16–30 — both record and both count: 13 + 13 = 26 days ⇒ ₹26,000.00. If the dedupe were widened to "any
    /// overlapping period" this would fail, and splitting a month would become impossible.
    /// </summary>
    [Fact]
    public void Two_genuine_half_month_records_are_not_treated_as_duplicates()
    {
        var (c, empId, typeId) = BuildAttendanceBook();
        var svc = new PayrollAttendanceService(c);

        svc.Record(empId, typeId, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 15), 13m);
        svc.Record(empId, typeId, new DateOnly(2025, 4, 16), new DateOnly(2025, 4, 30), 13m);

        Assert.Equal(2, c.AttendanceEntries.Count);
        Assert.Equal(new Money(26_000m),
            new PayrollComputationService(c).Compute(empId, PeriodFrom, PeriodTo).GrossEarnings);
    }

    /// <summary>
    /// The refusal is keyed on employee AND attendance type, not on the period alone: a second employee, and a
    /// second attendance type for the same employee, both record over the identical period.
    /// </summary>
    [Fact]
    public void The_same_period_records_for_another_employee_and_another_attendance_type()
    {
        var (c, empId, typeId) = BuildAttendanceBook();
        var pay = new PayrollService(c);
        var other = pay.CreateEmployee("E2", c.FindEmployeeGroupByName("Staff")!.Id);
        var overtime = pay.CreateAttendanceType("Overtime", AttendanceTypeKind.Production);
        var svc = new PayrollAttendanceService(c);

        svc.Record(empId, typeId, PeriodFrom, PeriodTo, 26m);
        svc.Record(other.Id, typeId, PeriodFrom, PeriodTo, 26m);       // another employee
        svc.Record(empId, overtime.Id, PeriodFrom, PeriodTo, 10m);     // another type

        Assert.Equal(3, c.AttendanceEntries.Count);
    }
}
