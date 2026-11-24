using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census 6.28 clause (i) (wave 13, track ic; store schema v71) — the <b>Nature of Remittance</b> on the Nature of
/// Payment master rides the canonical JSON and XML, both byte-stable, and lands on the TARGET company's own
/// re-minted nature on import. A nature WITHOUT one emits no key and no attribute at all (ER-13).
///
/// <para>Vendor ground: help.tallysolutions.com/tds-faq-tally/ — <i>"manually enter the applicable Nature of
/// Remittance in the Nature of Payment master."</i> (opened by content 2026-11-24).</para>
///
/// <para><b>Mutations (each reddens a named test):</b> dropping <c>NatureOfRemittance = n.NatureOfRemittance</c> in
/// <c>CanonicalMapper.MapNatureOfPayment</c> → <see cref="Json_carries_the_remittance_and_import_lands_it_on_the_target_nature"/>;
/// dropping the <c>Opt("natureOfRemittance", …)</c> attribute in <c>CanonicalXml.BuildNatureOfPayment</c> →
/// <see cref="Xml_carries_the_remittance_and_import_lands_it_on_the_target_nature"/>; dropping
/// <c>natureOfRemittance: n.NatureOfRemittance</c> in <c>ImportPlan</c> → both.</para>
/// </summary>
public class CanonicalNatureOfRemittanceRoundTripTests
{
    private static Company Build(string? remittance)
    {
        var c = CompanyFactory.CreateSeeded("Remittance Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        if (remittance is not null)
        {
            var j = c.FindNatureOfPaymentByCode("194J(b)")!;
            c.Tds!.ReplaceNatureOfPayment(j.WithTerms(j.SectionCode, j.Name, j.RateWithPanBp, j.RateWithoutPanBp,
                j.FvuSectionCode, j.SingleTransactionThreshold, j.CumulativeThreshold, remittance));
        }
        return c;
    }

    private static Company Import(CanonicalModel model)
    {
        var fresh = CompanyFactory.CreateSeeded("Fresh Remittance Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var result = new CompanyImportService(fresh).Apply(model);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        return fresh;
    }

    [Fact]
    public void Json_carries_the_remittance_and_import_lands_it_on_the_target_nature()
    {
        var source = Build("27");
        var bytes = CanonicalJson.Export(source);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "\"natureOfRemittance\": \"27\""));

        var (model, errors) = CanonicalJson.Parse(bytes);
        Assert.Empty(errors);
        Assert.Equal(bytes, CanonicalJson.Export(model!));   // byte-stable

        var target = Import(model!);
        var j = target.FindNatureOfPaymentByCode("194J(b)")!;
        Assert.Equal("27", j.NatureOfRemittance);
        Assert.NotEqual(source.FindNatureOfPaymentByCode("194J(b)")!.Id, j.Id);   // the target's own nature
        Assert.All(target.NaturesOfPayment.Where(n => n.SectionCode != "194J(b)"), n => Assert.Null(n.NatureOfRemittance));
    }

    [Fact]
    public void Xml_carries_the_remittance_and_import_lands_it_on_the_target_nature()
    {
        var source = Build("16");
        var bytes = CanonicalXml.Export(source);
        Assert.Contains("natureOfRemittance=\"16\"", Encoding.UTF8.GetString(bytes));

        var (model, errors) = CanonicalXml.Parse(bytes);
        Assert.Empty(errors);
        Assert.Equal(bytes, CanonicalXml.Export(model!));

        var target = Import(model!);
        Assert.Equal("16", target.FindNatureOfPaymentByCode("194J(b)")!.NatureOfRemittance);
    }

    /// <summary>ER-13: a book that never entered a remittance exports no key and no attribute — existing exports
    /// are byte-identical. <para>Mutation: removing the WhenWritingNull attribute on
    /// <c>NatureOfPaymentDto.NatureOfRemittance</c> reddens this.</para></summary>
    [Fact]
    public void A_nature_without_a_remittance_emits_no_key_or_attribute()
    {
        var c = Build(null);
        Assert.DoesNotContain("natureOfRemittance", Encoding.UTF8.GetString(CanonicalJson.Export(c)));
        Assert.DoesNotContain("natureOfRemittance", Encoding.UTF8.GetString(CanonicalXml.Export(c)));
    }
}
