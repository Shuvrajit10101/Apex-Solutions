using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// v67 (census row <b>2.7</b>) — the Cost <b>CATEGORY</b> alias survives a JSON and an XML round trip.
///
/// <para>🔴 <b>WHY THIS FILE EXISTS, AND IT IS NOT "parity for its own sake".</b> v67 closed row 2.7's gap by
/// adding <c>cost_categories.alias</c>, <c>CostCategory.Alias</c>, a mapper leg, a reader and the master
/// screen. <b><see cref="CostCategoryDto"/> was the SAME ASYMMETRY ONE LAYER UP</b>:
/// <see cref="CostCentreDto.Alias"/> had always carried an alias and the category DTO had none, so an
/// export → import round trip would have <b>silently dropped</b> an alias the master had just accepted. A
/// closed storage gap with an open transport gap is an overstated closure, and the row is being proposed for
/// promotion on the strength of the whole cell being satisfied.</para>
///
/// <para><b>ER-13.</b> The XML attribute is emitted with <c>Opt()</c>, exactly as the cost CENTRE alias is, so
/// a category with no alias emits no attribute at all and a document from a book that sets none is
/// byte-identical to a pre-v67 one. A pre-v67 document, which has the attribute nowhere, reads back as
/// <c>null</c> ⇒ no alias.</para>
///
/// <para><b>R7.</b> <c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>, opened BY CONTENT
/// 2026-10-16, in the Cost <b>CATEGORY</b> section: <i>"Name &amp; alias: As in other masters, you can specify
/// multiple aliases."</i> ⚠️ ONE alias, not the vendor's plural — divergence <c>IV-86</c>, shared with every
/// other master in this product.</para>
/// </summary>
public sealed class CanonicalCostCategoryAliasRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static Company BuildCompany()
    {
        var c = CompanyFactory.CreateSeeded("Alias Exporter", FyStart);
        c.AddCostCategory(new CostCategory(Guid.NewGuid(), "Departments") { Alias = "DEPT" });
        // A second category with NO alias, so the test can tell "every category got DEPT" from
        // "the right one got DEPT" — the shape a mapper that bound one value for every row would pass.
        c.AddCostCategory(new CostCategory(Guid.NewGuid(), "Branches"));
        return c;
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = CompanyFactory.CreateSeeded("Fresh Alias Co", FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        return target;
    }

    private static void AssertAliasSurvived(Company target)
    {
        Assert.Equal("DEPT", target.FindCostCategoryByName("Departments")!.Alias);
        Assert.Null(target.FindCostCategoryByName("Branches")!.Alias);
    }

    [Fact]
    public void The_cost_category_alias_round_trips_through_json_and_xml()
    {
        var c = BuildCompany();

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));      // byte-stable
        Assert.Equal("DEPT", jsonModel!.Payload.CostCategories.Single(x => x.Name == "Departments").Alias);
        AssertAliasSurvived(ImportInto(jsonModel!));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));         // byte-stable
        Assert.Equal("DEPT", xmlModel!.Payload.CostCategories.Single(x => x.Name == "Departments").Alias);
        AssertAliasSurvived(ImportInto(xmlModel!));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>ER-13: a book that sets NO category alias emits a document with no <c>alias</c> attribute on any
    /// <c>costCategory</c> element at all</b> — so a pre-v67 reader sees exactly the document it saw before,
    /// and a pre-v67 document read by this build yields <c>null</c> rather than an empty string.
    /// </summary>
    [Fact]
    public void A_category_with_no_alias_emits_no_attribute_and_a_pre_v67_document_reads_back_as_null()
    {
        var plain = CompanyFactory.CreateSeeded("No Alias Co", FyStart);
        plain.AddCostCategory(new CostCategory(Guid.NewGuid(), "Branches"));

        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(plain));
        Assert.Contains("<costCategory", xml);
        foreach (var element in xml.Split("<costCategory").Skip(1))
            Assert.DoesNotContain("alias=", element.Split('>')[0]);

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(xml));
        Assert.Empty(errors);
        Assert.All(model!.Payload.CostCategories, x => Assert.Null(x.Alias));
        Assert.All(ImportInto(model!).CostCategories, x => Assert.Null(x.Alias));
    }
}
