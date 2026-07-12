using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-5 <b>ESI Io fold-in</b> gate (RQ-9; losslessness): a company with Employees' State Insurance — the
/// establishment ESI config (EE/ER rates + 17-digit employer code), an ESI-applicable employee (10-digit IP number)
/// and ESI pay heads (the two <see cref="EsiStatutoryComponent"/> heads, a Basic + HRA flagged part-of-ESI-wages,
/// and an overtime head flagged part-of-ESI-wages + is-overtime) — <b>exports and re-imports exact in JSON AND
/// XML</b>, both byte-stable and into a fresh (differently-Guid'd) company through the engine-routed
/// <see cref="CompanyImportService"/>. An ESI-off company carries no <c>esi</c> config and every ESI flag defaults
/// off (ER-13).
/// </summary>
public sealed class CanonicalEsiRoundTripTests
{
    private static Company BuildEsiCompany()
    {
        var c = CompanyFactory.CreateSeeded("ESI Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableEsi(employeeRateBasisPoints: 75, employerRateBasisPoints: 325, employerCode: "12345678901234567");

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;
        ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfEsiWages: true);
        ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfEsiWages: true); // HRA IS part of ESI wages
        ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect,
            partOfEsiWages: true, isOvertime: true); // in the base, out of the coverage test
        ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, esiNumber: "3100123456");
        pay.SetEmployeeEsiDetails(e.Id, applicable: true, personWithDisability: true);
        return c;
    }

    private static Company Fresh() =>
        CompanyFactory.CreateSeeded("Fresh ESI Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildEsiCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildEsiCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_esi_payload()
    {
        var c = BuildEsiCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Esi_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildEsiCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // Establishment ESI config survives.
            var esi = fresh.EsiConfig!;
            Assert.NotNull(esi);
            Assert.Equal(75, esi.EmployeeRateBasisPoints);
            Assert.Equal(325, esi.EmployerRateBasisPoints);
            Assert.Equal("12345678901234567", esi.EmployerCode);

            // Per-employee ESI details survive.
            var e = fresh.Employees.Single(x => x.Name == "Sanjay Kumar");
            Assert.True(e.EsiApplicable);
            Assert.True(e.IsPersonWithDisability); // the ₹25,000-ceiling flag folds into the canonical model losslessly (F1)
            Assert.Equal("3100123456", e.EsiNumber);

            // Pay-head ESI tags survive.
            Assert.Equal(EsiStatutoryComponent.EmployeeStateInsurance, fresh.FindPayHeadByName("Employee ESI")!.EsiComponent);
            Assert.Equal(EsiStatutoryComponent.EmployerStateInsurance, fresh.FindPayHeadByName("Employer ESI")!.EsiComponent);
            Assert.True(fresh.FindPayHeadByName("Basic")!.PartOfEsiWages);
            Assert.True(fresh.FindPayHeadByName("HRA")!.PartOfEsiWages);          // HRA is included in ESI wages
            Assert.False(fresh.FindPayHeadByName("HRA")!.IsOvertime);
            Assert.True(fresh.FindPayHeadByName("Overtime")!.PartOfEsiWages);
            Assert.True(fresh.FindPayHeadByName("Overtime")!.IsOvertime);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12345")]
    public void Import_rejects_an_esi_applicable_employee_without_a_valid_10_digit_ip(string? badIp)
    {
        var source = BuildEsiCompany();
        source.Employees.Single(e => e.Name == "Sanjay Kumar").EsiNumber = badIp; // corrupt directly (bypass the service)

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("IP", StringComparison.OrdinalIgnoreCase));
        Assert.Null(fresh.FindEmployeeByName("Sanjay Kumar")); // nothing applied
    }

    [Fact]
    public void Company_without_esi_carries_no_esi_config_and_defaults_off()
    {
        // ER-13: a company not enrolled for ESI carries no `esi` config and no ESI flags.
        var c = Fresh();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Esi);
    }
}
