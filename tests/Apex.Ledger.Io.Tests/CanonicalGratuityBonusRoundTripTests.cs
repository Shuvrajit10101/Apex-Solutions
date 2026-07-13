using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-9 <b>Gratuity + statutory-Bonus Io fold-in</b> gate (RQ-14/15; losslessness): a company enrolled for
/// gratuity provisioning + statutory bonus — the establishment gratuity config (cap, wage basis, population) and bonus
/// config (rate, calc ceiling, minimum wage, prorate) — <b>exports and re-imports exact in JSON AND XML</b>, both
/// byte-stable and into a fresh (differently-Guid'd) company through the engine-routed
/// <see cref="CompanyImportService"/>. A company that provisions neither carries no <c>gratuity</c>/<c>bonus</c> config
/// (ER-13).
/// </summary>
public sealed class CanonicalGratuityBonusRoundTripTests
{
    private static Company BuildCompany()
    {
        var c = CompanyFactory.CreateSeeded("Grat/Bonus Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableGratuity(cap: new Money(1_500_000m), population: GratuityProvisionPopulation.VestedOnly);
        pay.EnableStatutoryBonus(rateBasisPoints: 1500, calculationCeiling: new Money(8_000m),
            minimumWage: new Money(5_000m), prorate: false);
        return c;
    }

    private static Company Fresh() =>
        CompanyFactory.CreateSeeded("Fresh GB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = BuildCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            var g = fresh.GratuityConfig!;
            Assert.NotNull(g);
            Assert.Equal(new Money(1_500_000m), g.CapAmount);
            Assert.Equal(GratuityWageBasis.BasicAndDearnessAllowance, g.WageBasis);
            Assert.Equal(GratuityProvisionPopulation.VestedOnly, g.Population);

            var b = fresh.BonusConfig!;
            Assert.NotNull(b);
            Assert.Equal(1500, b.RateBasisPoints);
            Assert.Equal(new Money(8_000m), b.CalculationCeiling);
            Assert.Equal(new Money(5_000m), b.MinimumWage);
            Assert.False(b.Prorate);
        }
    }

    [Fact]
    public void Import_rejects_a_gratuity_config_with_a_negative_cap()
    {
        var source = BuildCompany();
        source.GratuityConfig!.CapAmount = new Money(-1m); // corrupt directly (bypass the service)

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Company_without_gratuity_or_bonus_carries_no_config_and_defaults_off()
    {
        // ER-13: a company that provisions neither carries no gratuity/bonus config.
        var c = Fresh();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Gratuity);
        Assert.Null(model!.Company.Bonus);
    }
}
