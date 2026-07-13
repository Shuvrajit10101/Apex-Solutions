using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-4 <b>PF Io fold-in</b> gate (RQ-9; losslessness): a company with Provident Fund — the
/// establishment PF config (10% rate + establishment code + cap flag), a PF-applicable employee (higher-wage
/// opt-in + PF join date) and the five PF pay heads (each carrying its <see cref="PfStatutoryComponent"/>, plus a
/// Basic head flagged part-of-PF-wages) — <b>exports and re-imports exact in JSON AND XML</b>, both byte-stable
/// and into a fresh (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/>. A
/// PF-off company carries no <c>pf</c> config and every PF flag defaults off (ER-13).
/// </summary>
public sealed class CanonicalPfRoundTripTests
{
    private static Company BuildPfCompany()
    {
        var c = CompanyFactory.CreateSeeded("PF Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(
            epfRateBasisPoints: PfConfig.ReducedEpfRateBasisPoints, establishmentCode: "MHBAN0012345000", capWagesAtCeiling: false);

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;
        ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfPfWages: true);
        ph.CreatePayHead("Dearness Allowance", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfPfWages: true);
        ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect); // NOT part of PF wages
        ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, pfComponent: PfStatutoryComponent.EmployerPension);
        ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, uan: "100200300400");
        pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: true, pfJoinDate: new DateOnly(2020, 7, 1));
        return c;
    }

    private static Company Fresh() =>
        CompanyFactory.CreateSeeded("Fresh PF Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildPfCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildPfCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_pf_payload()
    {
        var c = BuildPfCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Pf_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildPfCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // Establishment PF config survives.
            var pf = fresh.PfConfig!;
            Assert.NotNull(pf);
            Assert.Equal(PfConfig.ReducedEpfRateBasisPoints, pf.EpfRateBasisPoints);
            Assert.Equal("MHBAN0012345000", pf.EstablishmentCode);
            Assert.False(pf.CapWagesAtCeiling);

            // Per-employee PF details survive.
            var e = fresh.Employees.Single(x => x.Name == "Sanjay Kumar");
            Assert.True(e.PfApplicable);
            Assert.True(e.PfContributeOnHigherWages);
            Assert.Equal(new DateOnly(2020, 7, 1), e.PfJoinDate);
            Assert.Equal("100200300400", e.Uan);

            // Pay-head PF tags survive.
            Assert.Equal(PfStatutoryComponent.EmployeeProvidentFund, fresh.FindPayHeadByName("Employee EPF")!.PfComponent);
            Assert.Equal(PfStatutoryComponent.EmployerPension, fresh.FindPayHeadByName("Employer Pension")!.PfComponent);
            Assert.True(fresh.FindPayHeadByName("Basic")!.PartOfPfWages);
            Assert.True(fresh.FindPayHeadByName("Dearness Allowance")!.PartOfPfWages);
            Assert.False(fresh.FindPayHeadByName("HRA")!.PartOfPfWages);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12345")]
    public void Import_rejects_a_pf_applicable_employee_without_a_valid_12_digit_uan(string? badUan)
    {
        // The engine enforces that a PF-applicable member carries a valid ^\d{12}$ UAN (SetEmployeePfDetails /
        // CreateEmployee.ValidateUan). A hand-edited canonical export can carry PfApplicable=true with a blank /
        // malformed UAN — a state the domain never allows — so the import pre-flight must mirror the guard and
        // reject the WHOLE batch (all-or-nothing), never materialising the illegal member.
        var source = BuildPfCompany();
        source.Employees.Single(e => e.Name == "Sanjay Kumar").Uan = badUan; // corrupt directly (bypass the service)

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("UAN", StringComparison.OrdinalIgnoreCase));
        Assert.Null(fresh.FindEmployeeByName("Sanjay Kumar")); // nothing applied
    }

    [Fact]
    public void Company_without_pf_carries_no_pf_config_and_defaults_off()
    {
        // ER-13: a company not enrolled for PF carries no `pf` config and no PF flags.
        var c = Fresh();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Pf);
    }
}
