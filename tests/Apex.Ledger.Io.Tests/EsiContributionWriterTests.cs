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

    // ---------------------------------------------------------------- 🔴 RULING 18: the IP's own identity

    private static EsiContributionReturn ReturnWith(string ipNumber, string ipName) =>
        new("31000123456789", To, new[] { new EsiContributionRow(ipNumber, ipName, 30, 20000, null, null) });

    /// <summary>
    /// 🔴 <b>An Insured Person's legal name and IP number are theirs, not ours.</b> Both used to run through the
    /// ER-11 de-brand, so a real IP whose name carries the vendor token was filed against their IP number under a
    /// name that is not theirs. The fixture name deliberately CARRIES the token; a clean one proves nothing.
    /// </summary>
    [Fact]
    public void An_insured_persons_own_name_reaches_the_contribution_file_intact()
    {
        var text = Encoding.UTF8.GetString(EsiContributionWriter.Write(ReturnWith(Ip, "TALLY MURUGAN")));

        Assert.Contains("TALLY MURUGAN", text, StringComparison.Ordinal);
        Assert.Equal(Ip + ",TALLY MURUGAN,30,20000,,", text.TrimEnd('\n'));
    }

    /// <summary>
    /// With the de-brand gone, the record-framing guard is the only thing stopping a comma or a newline in a
    /// user-typed IP name from shifting every later field on the line, or inventing a whole extra IP row. The
    /// fixture actually contains both.
    /// </summary>
    [Fact]
    public void A_delimiter_or_newline_inside_an_ip_name_cannot_corrupt_the_record_framing()
    {
        var text = Encoding.UTF8.GetString(EsiContributionWriter.Write(ReturnWith(Ip, "Rao, S.\nKumar")));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // One row (the newline invented none)...
        var line = Assert.Single(lines);
        // ...with exactly six fields (the comma invented none), so Days/Wages are still read in the right column.
        Assert.Equal(6, line.Split(',').Length);
        // The comma became a space (beside the one already there, hence two) and the newline became a single one.
        Assert.Equal(Ip + ",Rao  S. Kumar,30,20000,,", line);
    }

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
