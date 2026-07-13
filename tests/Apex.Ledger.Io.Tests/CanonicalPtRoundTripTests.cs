using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-6 <b>Professional-Tax Io fold-in</b> gate (RQ-11; losslessness): a company enrolled for PT — the
/// establishment PT config (active state, registration number, wage basis) with the seeded editable per-state slab
/// tables (Maharashtra men/women incl. the February over-charge, Karnataka, West Bengal) and a PT pay head — <b>exports
/// and re-imports exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company through the
/// engine-routed <see cref="CompanyImportService"/>. A PT-off company carries no <c>pt</c> config and every PT flag
/// defaults off (ER-13).
/// </summary>
public sealed class CanonicalPtRoundTripTests
{
    private const string MH = "27";

    private static Company BuildPtCompany()
    {
        var c = CompanyFactory.CreateSeeded("PT Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProfessionalTax(stateCode: MH, registrationNumber: "27999999999P");

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;
        ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect);
        ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, ptComponent: PtStatutoryComponent.ProfessionalTax);

        var e = pay.CreateEmployee("Ravi Kumar", pay.CreateEmployeeGroup("Staff").Id);
        c.FindEmployee(e.Id)!.Gender = "Male";
        return c;
    }

    private static Company Fresh() =>
        CompanyFactory.CreateSeeded("Fresh PT Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildPtCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildPtCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_pt_payload()
    {
        var c = BuildPtCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Pt_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildPtCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // Establishment PT config survives.
            var pt = fresh.PtConfig!;
            Assert.NotNull(pt);
            Assert.Equal(MH, pt.StateCode);
            Assert.Equal("27999999999P", pt.RegistrationNumber);
            Assert.Equal(PtWageBasis.GrossEarnings, pt.WageBasis);

            // The seeded slab tables survive (Maharashtra men/women, Karnataka, West Bengal).
            Assert.Equal(4, pt.SlabTables.Count);
            var mhMale = pt.SlabTables.Single(s => s.StateCode == MH && s.GenderScope == PtGenderScope.Male);
            var topBand = mhMale.Bands.Last();
            Assert.Equal(new Money(200m), topBand.MonthlyAmount);
            // The February over-charge folds losslessly (the {month, amount} override).
            var feb = Assert.Single(topBand.MonthOverrides);
            Assert.Equal(2, feb.Month);
            Assert.Equal(new Money(300m), feb.Amount);

            // The active slab still computes: MH man ₹12,000 → ₹200 (₹300 Feb).
            var slab = pt.ResolveSlab("Male")!;
            Assert.Equal(new Money(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 4));
            Assert.Equal(new Money(300m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 2));

            // Pay-head PT tag survives.
            Assert.Equal(PtStatutoryComponent.ProfessionalTax, fresh.FindPayHeadByName("Professional Tax")!.PtComponent);
        }
    }

    [Fact]
    public void Import_rejects_a_pt_config_with_an_invalid_state_code()
    {
        var source = BuildPtCompany();
        source.PtConfig!.StateCode = "ZZ"; // corrupt directly (bypass the service)

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("state code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_rejects_a_pt_head_mis_typed_as_an_employer_contribution()
    {
        // F3: a Professional-Tax head must post as an employee deduction; typed as an employer contribution it would
        // import a phantom self-balancing employer PT pair. The direct-construction import path bypasses PayHeadService,
        // so the pre-flight mirrors the master-boundary guard and rejects the whole batch.
        var source = BuildPtCompany();
        source.FindPayHeadByName("Professional Tax")!.Type = PayHeadType.EmployersStatutoryContributions; // corrupt (bypass service)

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("statutory component", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Company_without_pt_carries_no_pt_config_and_defaults_off()
    {
        // ER-13: a company not enrolled for PT carries no `pt` config and no PT flags.
        var c = Fresh();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Pt);
    }
}
