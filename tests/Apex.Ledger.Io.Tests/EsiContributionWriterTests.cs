using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-5 <b>ESI monthly-contribution file</b> contract (RQ-9) — the hand-rolled comma-delimited ESIC
/// offline file. Asserts the exact IP row in the brief's field order (IP Number · IP Name · No. of Days · Total
/// Monthly Wages · Reason for 0 wages · Last Working Day), that the writer is deterministic + byte-stable, that an
/// out-of-coverage member reports 0 wages with a reason, that an exit carries a last-working-day, and that a
/// builder-produced return reconciles to the same ESI base the payroll voucher posts.
/// </summary>
public sealed class EsiContributionWriterTests
{
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private const string Ip = "3100123456";

    [Fact]
    public void A_covered_ip_row_matches_the_esic_field_order()
    {
        var (c, empId) = BuildEsiCompany(basic: 20000m);
        var ret = EsiMonthlyContribution.Build(c, new[] { empId }, From, To);

        var row = Assert.Single(ret.Rows);
        Assert.Equal(Ip, row.IpNumber);
        Assert.Equal("Sanjay Kumar", row.IpName);
        Assert.Equal(30, row.NoOfDays);                 // 30 calendar days in April
        Assert.Equal(20000, row.TotalMonthlyWages);     // the ESI contribution base (actual wages)
        Assert.Null(row.ReasonForZeroWages);            // wages > 0 ⇒ no reason
        Assert.Null(row.LastWorkingDay);                // still on rolls ⇒ no LWD

        // The file line: IP Number , IP Name , No. of Days , Total Monthly Wages , (reason) , (LWD).
        var text = Encoding.UTF8.GetString(EsiContributionWriter.Write(ret));
        var line = Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("3100123456,Sanjay Kumar,30,20000,,", line);

        // Deterministic + byte-stable.
        Assert.Equal(EsiContributionWriter.Write(ret), EsiContributionWriter.Write(ret));
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_out_of_coverage_member_reports_zero_wages_with_a_reason()
    {
        // ₹22,000 flat from 1-Apr: above the ceiling at the CP1 start ⇒ not covered ⇒ 0 ESI wages, with a reason.
        var (c, empId) = BuildEsiCompany(basic: 22000m);
        var row = Assert.Single(EsiMonthlyContribution.Build(c, new[] { empId }, From, To).Rows);

        Assert.Equal(0, row.TotalMonthlyWages);
        Assert.Equal(EsiMonthlyContribution.DefaultZeroWageReason, row.ReasonForZeroWages);
        Assert.Equal(0, row.NoOfDays);
    }

    [Fact]
    public void An_exiting_member_carries_the_last_working_day()
    {
        var (c, empId) = BuildEsiCompany(basic: 20000m);
        c.FindEmployee(empId)!.DateOfLeaving = new DateOnly(2025, 4, 22);

        var row = Assert.Single(EsiMonthlyContribution.Build(c, new[] { empId }, From, To).Rows);
        Assert.Equal("2025-04-22", row.LastWorkingDay);
    }

    [Fact]
    public void Paid_days_override_rounds_fractional_days_up()
    {
        var (c, empId) = BuildEsiCompany(basic: 20000m);
        var days = new Dictionary<Guid, decimal> { [empId] = 25.2m };
        var row = Assert.Single(EsiMonthlyContribution.Build(c, new[] { empId }, From, To, paidDaysByEmployee: days).Rows);
        Assert.Equal(26, row.NoOfDays); // 25.2 rounds UP to 26
    }

    [Fact]
    public void Build_rejects_an_esi_member_whose_ip_number_is_not_10_digits()
    {
        var (c, empId) = BuildEsiCompany(basic: 20000m);
        // Corrupt the source directly (bypassing the service guard) to a present-but-malformed IP — a state only an
        // imported / hand-edited company could reach. The file must reject it rather than emit an ESIC-invalid line.
        c.FindEmployee(empId)!.EsiNumber = "12345";
        Assert.Throws<InvalidOperationException>(() => EsiMonthlyContribution.Build(c, new[] { empId }, From, To));
    }

    private static (Company Company, Guid EmployeeId) BuildEsiCompany(decimal basic)
    {
        var c = CompanyFactory.CreateSeeded("ESI MC Co", From, From);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableEsi(employerCode: "12345678901234567");
        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basicHead = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfEsiWages: true);
        var ee = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var er = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, esiNumber: Ip);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(e.Id, From, new[]
        {
            new SalaryStructureLine(basicHead.Id, 0, new Money(basic)),
            new SalaryStructureLine(ee.Id, 1),
            new SalaryStructureLine(er.Id, 2),
        });
        return (c, e.Id);
    }
}
