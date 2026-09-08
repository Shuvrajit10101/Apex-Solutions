using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// A strict RFC-4180 reader used by the injection tests to read an export the way a SPREADSHEET reads it — as
/// records and fields, not as one string.
///
/// <para>🔴 <b>Why the tests need this at all.</b> The bypass this reader exists to catch was invisible to a
/// substring assertion: a bare CR inside an employee name was written unquoted, and a bare CR is a RECORD
/// TERMINATOR to Excel, LibreOffice and any strict parser. The file "contained" the guarded text, so
/// <c>Assert.Contains</c> passed while the clerk's spreadsheet saw an extra record whose first cell was a live
/// formula. Only splitting into records can see that, so a record boundary is asserted as a record boundary.</para>
/// </summary>
internal static class DelimitedRecords
{
    /// <summary>
    /// Splits <paramref name="text"/> into records of fields. Quotes are honoured (<c>""</c> is a literal quote),
    /// and OUTSIDE quotes each of CRLF, a bare CR and a bare LF ends a record — which is precisely the latitude a
    /// spreadsheet takes and the reason a raw CR may never reach an unquoted field.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch != '"') { sb.Append(ch); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = false;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    break;
                case '\r':
                case '\n':
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    fields.Add(sb.ToString());
                    sb.Clear();
                    records.Add(fields);
                    fields = new List<string>();
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        if (sb.Length > 0 || fields.Count > 0) { fields.Add(sb.ToString()); records.Add(fields); }
        return records;
    }

    /// <summary>A readable dump of the parsed records, so a failure names what the spreadsheet would have seen.</summary>
    public static string Dump(IReadOnlyList<IReadOnlyList<string>> records) =>
        string.Join(Environment.NewLine,
            records.Select((r, i) => $"  [{i}] " + string.Join(" | ", r.Select(f => "<" + Visible(f) + ">"))));

    private static string Visible(string field) =>
        field.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

/// <summary>
/// 🔴 <b>SECURITY — spreadsheet formula injection (OWASP "CSV injection") in the two HAND-ROLLED CSV exporters.</b>
///
/// <para><b>What was wrong.</b> <see cref="KeralaFloodCessReturnViewModel"/> and
/// <see cref="ProfessionalTaxRegisterViewModel"/> each wrote their own private CSV field quoter and therefore
/// bypassed the project's shared formula-injection guard entirely. Both write USER-TYPED TEXT — the company name,
/// the PT enrolment number, every employee number and NAME — into a file an accounts clerk opens in a spreadsheet.
/// A name like <c>=cmd|'/c calc'!A1</c> is EXECUTED on open. Kerala quoted every field but never neutralised one;
/// the PT register quoted only when the field held a delimiter and never neutralised one either.</para>
///
/// <para><b>Both halves of each assertion matter.</b> A test that only looked for the leading <c>'</c> would pass
/// on a guard that mangled the name — and a name is book data that must reach the clerk verbatim (ruling 18). So
/// every case here pins the WHOLE field between its delimiters: the prefix, then the payload byte for byte, then
/// the field boundary. Nothing added, nothing rewritten.</para>
///
/// <para>🔴 <b>The leading-space cases are not decoration.</b> They are the half a naive <c>field[0]</c> guard
/// misses and the half the canonical guard exists for: importers that trim on the way in (LibreOffice's "Trim
/// spaces", Google Sheets) evaluate what follows the spaces, and this product indents its own output. Removing the
/// space-skip from <see cref="Apex.Ledger.Io.SpreadsheetFormulaGuard"/> must redden exactly those two tests.</para>
///
/// <para>The ordering trap each exporter poses is different, and both are covered. Kerala quotes EVERY field, so
/// the <c>'</c> has to land INSIDE the quotes (<c>"'=cmd…"</c>, never <c>'"=cmd…"</c>). The PT register quotes only
/// CONDITIONALLY, and an employee name needs no quoting — so the prefix must survive the unquoted branch, which is
/// the branch these tests actually exercise.</para>
/// </summary>
public sealed class DelimitedExportFormulaInjectionTests : IDisposable
{
    /// <summary>The realistic attack payload: a DDE command a spreadsheet executes when the file is opened.</summary>
    private const string Payload = "=cmd|'/c calc'!A1";

    /// <summary>The same payload behind leading spaces — still executed by any importer that trims on the way in.</summary>
    private const string IndentedPayload = "  =cmd|'/c calc'!A1";

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ApexCsvInjection_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ───────────────────────────────────────────────── Kerala Flood Cess return (quotes EVERY field)

    private static readonly DateOnly KeralaFy = new(2019, 4, 1);

    /// <summary>A Kerala-registered company under the given (hostile) name — no storage, so the name never has to
    /// be a legal file name.</summary>
    private static Company KeralaCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, KeralaFy, KeralaFy);
        const string pan = "AAPFU0939F";
        var body = "32" + pan + "1Z0";              // 32 = Kerala
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "32",
            Gstin = body[..14] + Gstin.ComputeCheckDigit(body),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = KeralaFy,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private string ExportKeralaReturn(Company c)
    {
        var page = new KeralaFloodCessReturnViewModel(c) { ExportFolder = _tempDir };
        page.ExportReturn();
        var file = Directory.EnumerateFiles(_tempDir, "KeralaFloodCess-*.csv").SingleOrDefault();
        Assert.True(file is not null, $"the return CSV was not written; status was '{page.ExportStatus}'.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void Kerala_return_neutralises_a_formula_company_name_and_the_name_still_reads_verbatim()
    {
        var csv = ExportKeralaReturn(KeralaCompany(Payload));

        // The guard fired AND the field is still a valid RFC-4180 field: the ' is INSIDE the quotes, the payload
        // follows it byte for byte, and the closing quote comes straight after — nothing was rewritten.
        Assert.Contains("Company,\"'" + Payload + "\"", csv, StringComparison.Ordinal);

        // The unguarded field must be gone, and the prefix must not have escaped outside the quotes.
        Assert.DoesNotContain("Company,\"" + Payload + "\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("'\"" + Payload, csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Kerala_return_neutralises_a_formula_company_name_hidden_behind_leading_spaces()
    {
        var csv = ExportKeralaReturn(KeralaCompany(IndentedPayload));

        // The trigger is looked for at the first character that CARRIES the value, and the spaces themselves
        // survive after the prefix — the guard prepends, it never trims.
        Assert.Contains("Company,\"'" + IndentedPayload + "\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Company,\"" + IndentedPayload + "\"", csv, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────── Professional Tax register (quotes only CONDITIONALLY)

    private static readonly DateOnly PtFy = new(2025, 4, 1);
    private const string Karnataka = "29";

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    /// <summary>A Karnataka PT establishment with ONE employee on Basic ₹30,000 (the golden KA band ⇒ ₹200 PT),
    /// whose name is set to <paramref name="employeeName"/> AFTER creation so a leading-space payload survives the
    /// service's name trim.</summary>
    private static Company PtCompany(string companyName, string employeeName)
    {
        var c = CompanyFactory.CreateSeeded(companyName, PtFy, PtFy);

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProfessionalTax(stateCode: Karnataka);

        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            ptComponent: PtStatutoryComponent.ProfessionalTax);

        var groupId = pay.CreateEmployeeGroup("Staff").Id;
        var e = pay.CreateEmployee("Placeholder Name", groupId, employeeNumber: "E-001");
        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30_000m)),
            new SalaryStructureLine(pt.Id, 1),
        });

        // The hostile name goes on last: CreateEmployee trims, and the leading-space case is the whole point.
        c.FindEmployee(e.Id)!.Name = employeeName;
        return c;
    }

    private static string ExportPtRegister(Company c)
    {
        var page = new ProfessionalTaxRegisterViewModel(c);
        byte[]? written = null;
        Assert.True(page.ExportRegister((_, b) => written = b),
            $"the PT register did not export; status was '{page.ExportStatus}'.");
        Assert.NotNull(written);
        return Encoding.UTF8.GetString(written!);
    }

    [Fact]
    public void Pt_register_neutralises_a_formula_employee_name_and_company_name_and_both_read_verbatim()
    {
        var csv = ExportPtRegister(PtCompany(companyName: Payload, employeeName: Payload));

        // The employee name needs no RFC-4180 quoting, so this pins the CONDITIONAL branch: the ' survives on the
        // path that returns the field unquoted, and the name follows it byte for byte up to the next delimiter.
        Assert.Contains(",'" + Payload + ",", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("," + Payload + ",", csv, StringComparison.Ordinal);

        // The header block's company name is the other user-typed field on this file (BuildCsv writes LF records).
        Assert.Contains("Company,'" + Payload + "\n", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Company," + Payload + "\n", csv, StringComparison.Ordinal);

        // And the register still says what it is meant to say.
        Assert.Contains("\"30,000\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Pt_register_neutralises_a_formula_employee_name_hidden_behind_leading_spaces()
    {
        var csv = ExportPtRegister(PtCompany(companyName: "PT Injection Co", employeeName: IndentedPayload));

        Assert.Contains(",'" + IndentedPayload + ",", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("," + IndentedPayload + ",", csv, StringComparison.Ordinal);
    }

    // ───────────────────────────── the CR bypass: the guard fires on the FIRST character, the CR is not first

    /// <summary>The payload hidden BEHIND a legitimate name, so the neutraliser cannot see it: the field's first
    /// value-carrying character is <c>K</c>. The CR is what makes it dangerous.</summary>
    private const string CrPayload = "Kiran Shet\r=cmd|'/c calc'!A1";

    /// <summary>The number of records the register writes for one employee: the title, four header lines, the
    /// column-caption row, one employee row and the Total row.</summary>
    private const int PtRecordCount = 8;

    /// <summary>
    /// 🔴 <b>SECURITY REGRESSION — a bare CR inside an employee name started a NEW RECORD whose first cell was a
    /// live formula, and every earlier test in this file passed while it did.</b>
    ///
    /// <para><b>The bypass.</b> The neutraliser fires on the first character that carries the value, so
    /// <c>Kiran Shet\r=cmd|'/c calc'!A1</c> — beginning with a letter — is correctly NOT prefixed. The PT register
    /// then quotes only conditionally, and its condition tested <c>, " LF</c> and omitted <b>CR</b>. So the raw CR
    /// reached the file unquoted; Excel, LibreOffice and a strict RFC-4180 parser all treat a bare CR as a record
    /// terminator, and the clerk's spreadsheet saw an extra record whose FIRST cell was
    /// <c>=cmd|'/c calc'!A1</c> — unprefixed, and executed on open. The canonical quoter
    /// (<c>Apex.Ledger.Io.DelimitedText.Quote</c>) had always quoted on CR; the hand-rolled copy did not.</para>
    ///
    /// <para><b>Why this test parses instead of searching.</b> A substring assertion cannot fail here: the file
    /// really does contain the name and really does contain the payload. The defect is entirely a question of
    /// WHERE THE RECORD BOUNDARIES ARE, so the export is read as records and fields — the spreadsheet's view.</para>
    /// </summary>
    [Fact]
    public void Pt_register_a_CR_inside_an_employee_name_cannot_start_a_new_record()
    {
        var csv = ExportPtRegister(PtCompany(companyName: "PT Injection Co", employeeName: CrPayload));
        var records = DelimitedRecords.Parse(csv);

        // 1. The CR did not split the file. With the CR omitted from the quote set this is 9, and record [7] is
        //    the invented one.
        Assert.True(records.Count == PtRecordCount,
            $"the export parsed as {records.Count} records, expected {PtRecordCount} — a bare CR split a record:"
            + Environment.NewLine + DelimitedRecords.Dump(records));

        // 2. No record anywhere in the file begins with a cell a spreadsheet would evaluate. This is the property
        //    the attack breaks, stated directly and over EVERY record rather than the one we expect to be hit.
        foreach (var (record, i) in records.Select((r, i) => (r, i)))
        {
            var first = record.Count > 0 ? record[0] : string.Empty;
            Assert.False(first.Length > 0 && (first[0] is '=' or '+' or '-' or '@'),
                $"record [{i}] begins with an unguarded formula cell <{first}>:"
                + Environment.NewLine + DelimitedRecords.Dump(records));
        }

        // 3. And the name is still one field, byte for byte — the CR included, and with NO apostrophe, because the
        //    guard must not fire on a name that starts with a letter (ruling 18: book data ships verbatim).
        var employee = Assert.Single(records, r => r.Count == 5 && r[0] == "E-001");
        Assert.Equal(CrPayload, employee[1]);
    }

    // ───────────────────────── ESI monthly contribution: a THIRD hand-rolled exporter, .csv, never guarded

    private const string Ip = "3100123456";

    /// <summary>An ESI establishment with one Insured Person on ₹20,000 Basic (inside the ₹21,000 ceiling), whose
    /// name is set to <paramref name="ipName"/> AFTER creation so a hostile leading character survives the
    /// service's name trim.</summary>
    private static Company EsiCompany(string ipName)
    {
        var from = new DateOnly(2025, 4, 1);
        var c = CompanyFactory.CreateSeeded("ESI Injection Co", from, from);

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableEsi(employerCode: "12345678901234567");

        var ph = new PayHeadService(c);
        var indirect = IndirectExpenses(c);
        var liab = CurrentLiabilities(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfEsiWages: true);
        var ee = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var er = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Placeholder Name", pay.CreateEmployeeGroup("Staff").Id, esiNumber: Ip);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(e.Id, from, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20_000m)),
            new SalaryStructureLine(ee.Id, 1),
            new SalaryStructureLine(er.Id, 2),
        });

        c.FindEmployee(e.Id)!.Name = ipName;
        return c;
    }

    /// <summary>Drives the REAL export path — the screen's own Ctrl+A — and returns the bytes it would have
    /// written to <c>&lt;employer code&gt;_2025_04.csv</c>.</summary>
    private static string ExportEsiContributionFile(Company c)
    {
        var page = new EsiContributionReportViewModel(c);
        string? path = null;
        byte[]? written = null;
        Assert.True(page.ExportReturn((p, b) => { path = p; written = b; }),
            $"the ESI contribution file did not export; status was '{page.ExportStatus}'.");
        Assert.NotNull(written);

        // The extension is the whole reason this file is ranked with the CSVs: a clerk double-clicks it.
        Assert.EndsWith(".csv", path!, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(written!);
    }

    /// <summary>
    /// 🔴 <b>SECURITY — the ESI monthly-contribution file is written as <c>.csv</c>, carries the user-typed
    /// Insured Person NAME and IP NUMBER, and applied no formula guard at all.</b> Its two encoders replaced the
    /// delimiter and CR/LF with spaces and stopped there, so an employee named <c>=cmd|'/c calc'!A1</c> reached
    /// <c>&lt;employer code&gt;_2025_04.csv</c> raw and executed when the operator opened the file to check it
    /// before uploading to ESIC. Parsed as records, because the framing is exactly what is being asserted.
    /// </summary>
    [Fact]
    public void Esi_contribution_file_neutralises_a_formula_ip_name_on_the_real_export_path()
    {
        var csv = ExportEsiContributionFile(EsiCompany(Payload));
        var records = DelimitedRecords.Parse(csv);

        var record = Assert.Single(records);
        Assert.Equal(6, record.Count);                 // IP no · name · days · wages · reason · LWD
        Assert.Equal(Ip, record[0]);
        // Guarded, and the person's own name still reaches ESIC verbatim after the apostrophe.
        Assert.Equal("'" + Payload, record[1]);
    }

    /// <summary>
    /// 🔴 <b>The ordering proof for a writer that SANITISES instead of quoting: the guard has to run LAST.</b> This
    /// writer makes a field framing-safe by replacing the delimiter with a space — and that replacement can EXPOSE
    /// a trigger that was not first before. A name of <c>",=cmd|'/c calc'!A1"</c> begins with a comma, which is not
    /// a formula trigger, so neutralising FIRST returns it untouched; the comma then becomes a space and the file
    /// carries <c>" =cmd|'/c calc'!A1"</c>, which every importer that trims on the way in evaluates. Running the
    /// guard after the replacement catches it through the leading-space skip. Swap the two steps in
    /// <c>EsiContributionWriter.Name</c> and this test — and only this one — reddens.
    /// </summary>
    [Fact]
    public void Esi_contribution_file_guards_a_formula_that_only_the_delimiter_sanitisation_exposes()
    {
        var csv = ExportEsiContributionFile(EsiCompany("," + Payload));
        var record = Assert.Single(DelimitedRecords.Parse(csv));

        Assert.Equal(6, record.Count);                 // the comma invented no field
        Assert.Equal("' " + Payload, record[1]);       // ' then the space the comma became, then the payload
    }
}
