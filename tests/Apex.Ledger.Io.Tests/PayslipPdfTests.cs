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

    private static (Company C, Guid Emp) BuildGolden(
        string companyName = "Apex Reports Co",
        string employeeName = "Rajkumar Sharma",
        string bankName = "State Bank",
        string basicHeadName = "Basic",
        string pan = "ABCPS1234K")
    {
        var c = CompanyFactory.CreateSeeded(companyName, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);
        var ie = c.FindGroupByName("Indirect Expenses")!.Id;
        var cl = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead(basicHeadName, PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: ie);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: ie,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: cl);

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee(employeeName, grp, employeeNumber: "E-001", pan: pan);
        emp.BankName = bankName;
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

    /// <summary>OUR OWN name on the payslip is still de-branded — the direction of ruling 18 that does not
    /// change. The employee, bank and pay heads here are all deliberately CLEAN, so our company name is the only
    /// possible source of the token.</summary>
    [Fact]
    public void Payslip_pdf_debrands_a_tally_named_employer()
    {
        var (c, emp) = BuildGolden("Tally Solutions Pvt Ltd");
        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        var bytes = PayslipPdf.Render(slip, new PageConfig());
        Assert.DoesNotContain("tally", AsLatin1(bytes).ToLowerInvariant());
    }

    // ---------------------------------------------------------------- 🔴 RULING 18 on the payslip

    /// <summary>
    /// 🔴 <b>Everything identifying on a payslip belongs to somebody who is not us.</b> The employee's own legal
    /// name, their PAN, their BANK MASTER'S name — the exact category ruling 18 names in words — and the pay-head
    /// master names the user created. All four ran through the ER-11 de-brand, so an employee whose name carries
    /// the vendor token received a payslip naming somebody else, banked at a bank that does not exist, on the
    /// same page as their PAN; and that payslip is the document produced for loan and visa verification.
    ///
    /// <para>Two aggravating facts made this worth pinning: the identical bank field two files away in
    /// <c>PaymentAdvicePdf</c> already shipped verbatim, so the correct treatment pre-existed and was simply not
    /// propagated; and the payroll-register EXPORT of the same figures already showed the same pay-head names
    /// correctly, so one company's own two documents disagreed about what its pay heads are called.</para>
    /// </summary>
    [Fact]
    public void A_payslip_keeps_the_employees_own_name_bank_and_pay_head_names_intact()
    {
        var (c, emp) = BuildGolden(
            companyName: "Apex Reports Co",              // OURS, and deliberately CLEAN here
            employeeName: "Tally Murugan",               // theirs
            bankName: "Tally Co-operative Bank",         // a BANK MASTER — the ruling names this category
            basicHeadName: "Tally Allowance",            // a pay-head master the user created
            pan: "TALLY1234F");                          // structurally valid: 4th char L = Local Authority

        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        string s = AsLatin1(PayslipPdf.Render(slip, new PageConfig()));

        Assert.Contains("Tally Murugan", s, StringComparison.Ordinal);
        Assert.Contains("Tally Co-operative Bank", s, StringComparison.Ordinal);
        Assert.Contains("Tally Allowance", s, StringComparison.Ordinal);
        // The PAN in particular: de-branding it emitted "1234F", which is not a PAN at all.
        Assert.Contains("TALLY1234F", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both directions on ONE payslip: the employee's branded name survives while OUR branded company name in
    /// the same document's header is still stripped. This is the assertion pair a one-direction fix fails.
    /// </summary>
    [Fact]
    public void A_payslip_names_the_employee_in_full_while_still_debranding_our_own_company_name()
    {
        var (c, emp) = BuildGolden(
            companyName: "Tally Solutions Pvt Ltd",      // OURS
            employeeName: "Tally Murugan");              // theirs

        var slip = Report.BuildPayslip(c, emp, PeriodFrom, PeriodTo);
        string s = AsLatin1(PayslipPdf.Render(slip, new PageConfig()));

        Assert.Contains("Tally Murugan", s, StringComparison.Ordinal);
        // Ours lost the token but kept the rest — de-branding is not blanking.
        Assert.Contains("Solutions Pvt Ltd", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Tally Solutions", s, StringComparison.Ordinal);
    }
}
