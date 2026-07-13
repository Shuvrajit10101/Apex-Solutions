using System;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase 8 slice 8 — the <b>Payslip PDF</b> rendered through the same deterministic, de-branded pipeline as the GST
/// tax invoice / TDS certificates (<see cref="PdfWriter"/> + <see cref="IndianAmountInWords"/>). The payslip's
/// figures match the <see cref="Payslip"/> projection exactly (which itself reconciles to the payroll computation);
/// every PDF is byte-identical across two runs, carries no third-party brand (even from a "Tally"-named employer),
/// and prints the net pay in Indian amount-in-words.
/// </summary>
public sealed class PayslipPdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    private static (Company C, Guid Emp) BuildGolden(string companyName = "Apex Reports Co")
    {
        var c = CompanyFactory.CreateSeeded(companyName, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);
        var ie = c.FindGroupByName("Indirect Expenses")!.Id;
        var cl = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: ie);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: ie,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: cl);

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPS1234K");
        emp.BankName = "State Bank";
        emp.BankAccountNumber = "1234567890";
        emp.BankIfsc = "SBIN0001234";

        new SalaryStructureService(c).DefineForEmployee(emp.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        // The payslip projects the POSTED Payroll voucher, so post the run before rendering.
        new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { emp.Id });
        return (c, emp.Id);
    }

    [Fact]
    public void Payslip_renders_a_valid_debranded_pdf_with_net_and_words()
    {
        var (c, emp) = BuildGolden();
        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        var bytes = PayslipPdf.Render(slip, new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.Contains("/Producer (Apex Solutions)", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());

        // Identity + figures appear on the payslip.
        Assert.Contains("Rajkumar Sharma", s);
        Assert.Contains("Basic", s);
        Assert.Contains("HRA", s);
        Assert.Contains("30,000.00", s);
        Assert.Contains("12,000.00", s);
        Assert.Contains("42,000.00", s); // gross
        Assert.Contains("40,000.00", s); // net

        // Net pay in Indian amount-in-words.
        Assert.Contains(IndianAmountInWords.Convert(40000m), s);
    }

    [Fact]
    public void Payslip_pdf_is_byte_identical_across_two_runs()
    {
        var (c, emp) = BuildGolden();
        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        var a = PayslipPdf.Render(slip, new PageConfig());
        var b = PayslipPdf.Render(slip, new PageConfig());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Payslip_pdf_debrands_a_tally_named_employer()
    {
        var (c, emp) = BuildGolden("Tally Solutions Pvt Ltd");
        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        var bytes = PayslipPdf.Render(slip, new PageConfig());
        Assert.DoesNotContain("tally", AsLatin1(bytes).ToLowerInvariant());
    }
}
