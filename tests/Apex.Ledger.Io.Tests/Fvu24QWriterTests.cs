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

    /// <summary>
    /// 🔴 <b>INVERTED BY RULING 18 — the old assertion locked in a statutory defect.</b> It read "a salaried
    /// employee's name must never carry the forbidden brand into the file", and enforced that by running the
    /// ER-11 de-brand over <c>row.EmployeeName</c>. But the employee on a 24Q Annexure-I line is the
    /// COUNTERPARTY of the §192 withholding, and the department matches that name against the PAN on the same
    /// record. Scrubbing it filed a real employee under a name that is not theirs, mismatched against their own
    /// PAN, and disagreeing with the Form 16 issued for the same row.
    ///
    /// <para>The 26Q and 27EQ equivalents were inverted when ruling 18 landed; this one was missed, and the same
    /// file kept both treatments — <c>Name()</c> on the deductee/collectee, <c>Text()</c> on the salaried
    /// employee — one line apart. The DEDUCTOR half of the file (our TAN, our responsible person) is ours and
    /// stays de-branded, which is what the second assertion holds.</para>
    /// </summary>
    [Fact]
    public void An_employees_own_name_reaches_the_24Q_file_intact_while_our_own_fields_stay_debranded()
    {
        var q1 = Form24Q.Build(SalaryCompany(employeeName: "Tally Kumar"), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));

        // The employee's legal name is filed exactly as it stands in the books...
        Assert.Contains("Tally Kumar", text, StringComparison.Ordinal);

        // ...and it is the ONLY place the token appears, so nothing of OURS leaked alongside it. Three salary DD
        // rows are written for this fixture, one per month, each naming the same employee.
        Assert.Equal(3, CountBrand(text));
    }

    /// <summary>The record-framing guard is now the only protection left on a 24Q employee name: a caret would
    /// invent a field and a newline a whole record, and the department would reject the return.</summary>
    [Fact]
    public void A_delimiter_or_newline_inside_an_employee_name_cannot_corrupt_the_24Q_record_framing()
    {
        var dirty = Form24Q.Build(SalaryCompany(employeeName: "Ka^vi\nRao"), 2025, 1);
        var clean = Form24Q.Build(SalaryCompany(employeeName: "Ka vi Rao"), 2025, 1);

        string dirtyText = Encoding.UTF8.GetString(FvuWriter.Write(dirty));

        Assert.Contains("Ka vi Rao", dirtyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Ka^vi", dirtyText, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetString(FvuWriter.Write(clean)), dirtyText);
    }

    private static int CountBrand(string text)
    {
        int n = 0;
        for (int i = text.IndexOf("tally", StringComparison.OrdinalIgnoreCase); i >= 0;
             i = text.IndexOf("tally", i + 5, StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }
        return n;
    }

    /// <summary>
    /// <b>ER-13 — the prior-year byte-identity gate for CA slice S9.</b>
    ///
    /// <para>The Income-tax Act 2025 vocabulary (§192→§392, Form 24Q→138, assessment year→tax year) applies only from
    /// <b>FY 2026-27</b>. FY 2025-26 is still live and is filed on the <b>1961-Act</b> artifacts, and §397(3)(f)
    /// permits correction statements for two years. A prior-year return that changed by even one byte would be a
    /// <b>falsified filed document</b>.</para>
    ///
    /// <para>These four digests were captured from the <b>pre-S9</b> tree and are frozen here deliberately. They are
    /// not "whatever the code produces" — if S9 (or any later vocabulary work) leaks into a FY 2025-26 export, this
    /// test fails and the export must be treated as broken, not the expectation updated.</para>
    /// </summary>
    [Theory]
    [InlineData(1, 264, "508644DF30B8E6DCF5C9BFD4805D2408488A3200C6E06FB92369E468D2C286F4")]
    [InlineData(2, 264, "515E3B5BC5D711B23838EBBC10C30AE7CF9470317E180B65804E535B52F8CC94")]
    [InlineData(3, 264, "140465D88695631A5524A134E442E1139D55AE93DB2CA76B424FD0ED75107400")]
    [InlineData(4, 268, "4DAB940DF5F4BD829D26CD21E941F2AA146B3ECBDA3916A33D5A5B611B2DC05E")]
    public void ER13_prior_year_fvu_export_is_byte_identical_to_the_pre_S9_baseline(
        int quarter, int expectedLength, string expectedSha256)
    {
        var bytes = FvuWriter.Write(Form24Q.Build(SalaryCompany(months: 12), 2025, quarter));

        Assert.Equal(expectedLength, bytes.Length);
        Assert.Equal(expectedSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));

        // The 1961-Act form number and the assessment-year framing must still be on the wire for FY 2025-26.
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("FH^24Q^", text);
        Assert.DoesNotContain("138", text.Split('\n')[0]);   // NOT the 2025-Act form number
        Assert.Contains("2025-26", text);
    }

    /// <summary>The 2025-Act vocabulary is presentation-only: a <b>FY 2026-27</b> export still carries the 24Q wire
    /// format, because S9 renamed labels and touched no writer, no schema and no computation.</summary>
    [Fact]
    public void ER13_the_vocabulary_gate_does_not_leak_into_the_export_wire_format()
    {
        var text = Encoding.UTF8.GetString(FvuWriter.Write(Form24Q.Build(SalaryCompany(months: 12), 2026, 1)));
        Assert.StartsWith("FH^24Q^", text);
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
