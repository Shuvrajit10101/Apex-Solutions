using System;
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
}
