using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-3 <b>Io fold-in</b> gate (losslessness): a company that has recorded attendance and posted the
/// golden Payroll voucher (Basic ₹30,000 + HRA 40% + a flat ₹2,000 advance recovery + a 12%-of-Basic employer
/// contribution) <b>exports and re-imports paisa + count exact in JSON AND XML</b>, both byte-stable and into a
/// fresh (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/> — the
/// per-employee payroll line detail, the balanced posting, the auto-created ledger links and the attendance
/// entries all survive with no silent drop. A company with Payroll unused carries neither (ER-13).
/// </summary>
public class CanonicalPayrollVoucherRoundTripTests
{
    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    private static Company BuildPostedCompany()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Voucher Traders", PeriodFrom, PeriodFrom);
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
                new[] { PayHeadComputationSlab.Percentage(1200) }));

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
        return c;
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", PeriodFrom, PeriodFrom);

    [Fact]
    public void Json_round_trips_byte_stable_and_carries_v32_schema()
    {
        var c = BuildPostedCompany();
        var first = CanonicalJson.Export(c);
        Assert.Contains($"\"schemaVersion\": {CanonicalMapper.SchemaVersion}", Encoding.UTF8.GetString(first));

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildPostedCompany();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = BuildPostedCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildPostedCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertReconciles(source, fresh);
        }
    }

    [Fact]
    public void Company_without_payroll_serialises_without_attendance_or_payroll_lines()
    {
        var c = FreshTarget();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.AttendanceEntries);
        Assert.All(model.Payload.Vouchers, v => Assert.All(v.Lines, l => Assert.Null(l.Payroll)));
    }

    private static void AssertReconciles(Company source, Company target)
    {
        // Attendance entries reconcile count + value exact.
        Assert.Equal(source.AttendanceEntries.Count, target.AttendanceEntries.Count);
        Assert.Equal(26m, Assert.Single(target.AttendanceEntries).Value);

        // The Payroll voucher reconciles: balanced, every line carries payroll detail, net = ₹40,000.
        var sourceV = source.Vouchers.Single(v => source.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
        var targetV = target.Vouchers.Single(v => target.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
        Assert.Equal(sourceV.TotalDebit, targetV.TotalDebit);
        Assert.Equal(targetV.TotalDebit, targetV.TotalCredit);
        Assert.Equal(sourceV.Lines.Count, targetV.Lines.Count);
        Assert.True(targetV.Lines.All(l => l.HasPayroll));

        var targetEmp = Assert.Single(target.Employees);
        Assert.All(targetV.Lines, l => Assert.Equal(targetEmp.Id, l.Payroll!.EmployeeId));

        var netLed = target.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;
        var net = targetV.Lines.Single(l => l.LedgerId == netLed.Id);
        Assert.Equal(new Money(40000m), net.Amount);
        Assert.Equal(PayrollLineCategory.NetPayable, net.Payroll!.Category);
        Assert.Null(net.Payroll!.PayHeadId);

        // The auto-created pay-head ledger links re-resolve into the target's re-minted ids.
        var pf = target.FindPayHeadByName("Employer PF")!;
        Assert.NotNull(pf.LedgerId);
        Assert.NotNull(pf.EmployerExpenseLedgerId);
        Assert.Contains(targetV.Lines, l => l.LedgerId == pf.EmployerExpenseLedgerId && l.Payroll!.Category == PayrollLineCategory.EmployerContributionExpense);
        Assert.Contains(targetV.Lines, l => l.LedgerId == pf.LedgerId && l.Payroll!.Category == PayrollLineCategory.EmployerContributionPayable);
    }

    private static void AssertNoTally(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }
}
