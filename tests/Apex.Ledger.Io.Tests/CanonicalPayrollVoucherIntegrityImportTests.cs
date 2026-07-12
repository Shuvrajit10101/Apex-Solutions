using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-3 <b>Payroll import pre-flight</b> regression guard (adversarial; F4). The engine-bypassing
/// import path re-maps the new v32 reference fields — a pay head's employer-contribution <b>expense</b> ledger,
/// a Payroll-voucher line's per-employee payroll detail (employee + pay head) and an attendance entry's employee /
/// type — by name. A dangling reference in a hand-edited file must be caught at <b>pre-flight</b> with a clean,
/// precise per-record message (Applied=false, target untouched), not surface as a generic post-apply rollback when
/// <see cref="ImportPlan"/> throws mid-mapping. A valid file still applies.
/// </summary>
public sealed class CanonicalPayrollVoucherIntegrityImportTests
{
    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    /// <summary>A company with attendance recorded and the golden Payroll voucher posted (Basic + HRA + advance +
    /// a 12%-of-Basic employer contribution, so the export carries an employer-expense ledger link + payroll lines
    /// + an attendance entry to dangle).</summary>
    private static Company BuildPostedCompany()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Integrity Co", PeriodFrom, PeriodFrom);
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

    private static Company FreshTarget() => CompanyFactory.CreateSeeded("Fresh Payroll Import Co", PeriodFrom, PeriodFrom);

    private static CanonicalModel ParseFrom(Company c)
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        return model!;
    }

    private static CanonicalModel WithPayload(CanonicalModel model, PayloadDto payload) =>
        model with { Payload = payload };

    [Fact]
    public void Rejects_a_dangling_employer_expense_ledger_at_preflight()
    {
        var model = ParseFrom(BuildPostedCompany());
        // Point the employer head's employer-EXPENSE ledger at a ledger that is neither imported nor present.
        var payHeads = model.Payload.PayHeads
            .Select(p => p.Name == "Employer PF" ? p with { EmployerExpenseLedgerId = Guid.NewGuid() } : p)
            .ToList();
        model = WithPayload(model, model.Payload with { PayHeads = payHeads });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("employer-contribution expense ledger", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fresh.PayHeads);   // all-or-nothing: nothing applied
    }

    [Fact]
    public void Rejects_a_dangling_payroll_line_pay_head_at_preflight()
    {
        var model = ParseFrom(BuildPostedCompany());
        // Repoint every payroll line that names a pay head to a dangling id (the net line's null pay head is left alone).
        var vouchers = model.Payload.Vouchers.Select(v =>
        {
            var lines = v.Lines
                .Select(l => l.Payroll is { PayHeadId: not null } pl
                    ? l with { Payroll = pl with { PayHeadId = Guid.NewGuid() } }
                    : l)
                .ToList();
            return v with { Lines = lines };
        }).ToList();
        model = WithPayload(model, model.Payload with { Vouchers = vouchers });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("payroll line referencing a pay head", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fresh.Vouchers, v => fresh.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
        Assert.Empty(fresh.PayHeads);
    }

    [Fact]
    public void Rejects_a_dangling_attendance_entry_employee_at_preflight()
    {
        var model = ParseFrom(BuildPostedCompany());
        var entries = model.Payload.AttendanceEntries.Select(a => a with { EmployeeId = Guid.NewGuid() }).ToList();
        model = WithPayload(model, model.Payload with { AttendanceEntries = entries });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("attendance entry references an employee", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fresh.AttendanceEntries);
        Assert.Empty(fresh.Employees);
    }

    [Fact]
    public void A_valid_payroll_export_still_imports_cleanly()
    {
        // Over-rejection guard: the un-edited valid export must still apply into a fresh company.
        var model = ParseFrom(BuildPostedCompany());
        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Single(fresh.Employees);
        Assert.Single(fresh.AttendanceEntries);
        Assert.Contains(fresh.Vouchers, v => fresh.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }
}
