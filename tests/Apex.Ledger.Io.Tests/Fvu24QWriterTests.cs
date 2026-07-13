using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase 8 slice 7 (F4) — the <see cref="FvuWriter"/> NSDL FVU-compatible flat file for <b>Form 24Q</b> (§192
/// salary-TDS), the salary sibling of the Form 26Q writer. Proves the file <b>extends the shared FH/BH/DD/FT record
/// framing</b> (form type 24Q), is <b>deterministic + byte-stable</b>, <b>de-branded</b> (no third-party brand can
/// leak even from an employee name), and that its file-trailer control total equals the Annexure I total, so
/// "Annexure I total == FVU control total" is real + testable. Salary-TDS carries no challan (CD) block yet (that
/// Phase-7 deposit-path integration is a documented carry-forward). An empty return still yields a valid header-only
/// file (ER-13).
/// </summary>
public class Fvu24QWriterTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string ValidPan = "ABCDE1234F";

    private static Company SalaryCompany(string employeeName = "Anita Rao", int months = 3, decimal monthlyBasic = 125_000m)
    {
        var c = CompanyFactory.CreateSeeded("Salary Return Co", FyStart, FyStart);
        // §192 reuses the Phase-7 deductor identity (TAN / person responsible) — enable TDS to populate it.
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = "MUMA12345B", DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = "AAPFU0939F",
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        var e = pay.CreateEmployee(employeeName, pay.CreateEmployeeGroup("Staff").Id);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = TaxRegime.New;
        emp.Pan = ValidPan;
        new SalaryStructureService(c).DefineForEmployee(e.Id, FyStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(monthlyBasic)),
            new SalaryStructureLine(tds.Id, 1),
        });
        var d = FyStart;
        for (var i = 0; i < months; i++)
        {
            var from = new DateOnly(d.Year, d.Month, 1);
            var to = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
            new PayrollVoucherService(c).Post(from, to, new[] { e.Id });
            d = d.AddMonths(1);
        }
        return c;
    }

    [Fact]
    public void File_is_byte_identical_across_two_runs_and_carries_the_24Q_header()
    {
        var q1 = Form24Q.Build(SalaryCompany(), 2025, 1);
        var a = FvuWriter.Write(q1);
        var b = FvuWriter.Write(q1);
        Assert.Equal(a, b);                     // deterministic — no clock/RNG
        Assert.NotEmpty(a);
        Assert.StartsWith($"FH^24Q^{FvuWriter.FvuVersion}^MUMA12345B^2025-26^Q1^Company^", Encoding.UTF8.GetString(a));
    }

    [Fact]
    public void File_trailer_total_equals_the_annexure_I_control_total()
    {
        var q1 = Form24Q.Build(SalaryCompany(months: 3), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // FT^ddCount^Σ§192TDS^annexureIICount^annexureIITax — the file's control total is the Σ TDS field.
        var ft = Assert.Single(lines, l => l.StartsWith("FT^"));
        var fileControlTotal = decimal.Parse(ft.Split('^')[2], CultureInfo.InvariantCulture);
        Assert.Equal(q1.ControlTotals.TotalTdsDeducted.Amount, fileControlTotal);
        Assert.Equal(q1.TotalTdsDeducted.Amount, fileControlTotal);   // == the Annexure I total
        Assert.Equal(24_375m, fileControlTotal);                      // 3 × ₹8,125

        // Three salary DD (deductee) rows; no CD (challan) block — the deposit path is a documented carry-forward.
        Assert.Equal(3, lines.Count(l => l.StartsWith("DD^")));
        Assert.DoesNotContain(lines, l => l.StartsWith("CD^"));
    }

    [Fact]
    public void File_never_contains_the_third_party_brand_even_from_an_employee_name()
    {
        var q1 = Form24Q.Build(SalaryCompany(employeeName: "Tally Kumar"), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_return_still_yields_a_valid_header_only_file()
    {
        var c = CompanyFactory.CreateSeeded("Empty Salary Co", FyStart, FyStart);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(Form24Q.Build(c, 2025, 1)));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("FH^24Q^", lines[0]);
        Assert.StartsWith("BH^", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("DD^"));
        Assert.Equal("FT^0^0.00^0^0.00", lines[^1]);
    }
}
