using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-8 slice-3 <b>Attendance + Payroll voucher</b> persistence contract (v32; ER-13). A company that posts the
/// golden payroll voucher (Basic ₹30,000 + HRA 40% + a flat ₹2,000 advance recovery → net ₹40,000) with a
/// recorded attendance entry SAVES and RELOADS at <see cref="Schema.CurrentVersion"/>, preserving the balanced
/// posting, every line's <see cref="PayrollLineDetail"/> (employee / pay head / category / amount, paisa-exact),
/// the auto-created pay-head ledger links, and the attendance entry.
/// </summary>
public sealed class PayrollVoucherRoundTripTests
{
    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Payroll_voucher_and_attendance_survive_save_reload_paisa_exact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-payvoucher-rt-{Guid.NewGuid():N}.db");
        try
        {
            var (original, empId, basicId, employerPfId) = BuildAndPost();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 32);
            }

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            // The posted payroll voucher reloads balanced with its per-line payroll detail intact.
            var v = Assert.Single(r.Vouchers, x => r.FindVoucherType(x.TypeId)!.BaseType == VoucherBaseType.Payroll);
            Assert.Equal(v.TotalDebit, v.TotalCredit);
            Assert.True(v.Lines.All(l => l.HasPayroll));

            var netLed = r.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;
            var net = v.Lines.Single(l => l.LedgerId == netLed.Id);
            Assert.Equal(new Money(40000m), net.Amount);
            Assert.Equal(PayrollLineCategory.NetPayable, net.Payroll!.Category);
            Assert.Equal(empId, net.Payroll!.EmployeeId);
            Assert.Null(net.Payroll!.PayHeadId);

            var basicLed = r.FindLedgerByName("Basic")!;
            var basicLine = v.Lines.Single(l => l.LedgerId == basicLed.Id);
            Assert.Equal(new Money(30000m), basicLine.Amount);
            Assert.Equal(PayrollLineCategory.Earning, basicLine.Payroll!.Category);
            Assert.Equal(basicId, basicLine.Payroll!.PayHeadId);

            // The employer pair reloads on both sides with the pay head's dual ledger link.
            var reloadedPf = r.FindPayHead(employerPfId)!;
            Assert.NotNull(reloadedPf.LedgerId);
            Assert.NotNull(reloadedPf.EmployerExpenseLedgerId);
            var pfExpLine = v.Lines.Single(l => l.LedgerId == reloadedPf.EmployerExpenseLedgerId);
            var pfPayLine = v.Lines.Single(l => l.LedgerId == reloadedPf.LedgerId && l.Payroll!.Category == PayrollLineCategory.EmployerContributionPayable);
            Assert.Equal(pfExpLine.Amount, pfPayLine.Amount);
            Assert.Equal(PayrollLineCategory.EmployerContributionExpense, pfExpLine.Payroll!.Category);

            // The recorded attendance entry survives paisa/count exact.
            var att = Assert.Single(r.AttendanceEntries);
            Assert.Equal(empId, att.EmployeeId);
            Assert.Equal(26m, att.Value);
            Assert.Equal(PeriodFrom, att.FromDate);
            Assert.Equal(PeriodTo, att.ToDate);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    private static (Company Company, Guid EmployeeId, Guid BasicId, Guid EmployerPfId) BuildAndPost()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Voucher Persist Co", PeriodFrom, PeriodFrom);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liabilities = c.FindGroupByName("Current Liabilities")!.Id;
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: liabilities);
        var employerPf = ph.CreatePayHead("Employer PF", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsComputedValue,
            underGroupId: liabilities,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) })); // 12% of Basic

        var emp = pay.CreateEmployee("Rajkumar Sharma", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
            new SalaryStructureLine(employerPf.Id, 3),
        });
        new PayrollAttendanceService(c).Record(emp.Id, present.Id, PeriodFrom, PeriodTo, 26m);
        new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id });

        return (c, emp.Id, basic.Id, employerPf.Id);
    }
}
