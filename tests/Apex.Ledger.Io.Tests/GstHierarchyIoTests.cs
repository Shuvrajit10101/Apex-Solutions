using System.Text;
using Regex = System.Text.RegularExpressions.Regex;
using System.Xml.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The IO half of schema v51's <b>GST five-level hierarchy masters</b> (plan.md Phase 10.10 WF-1 / register IV-1):
/// the narrow <see cref="MasterGstDetails"/> block on the Stock Group, the accounting Group and the company default,
/// plus the two independent source-order options, all lossless through JSON <b>and</b> XML and through the
/// all-or-nothing import.
///
/// <para>⚠️ <b>The trap this file exists to catch.</b> A field that persists to SQLite but not to the canonical
/// document is invisible until someone exports a book and imports it somewhere else — the export silently drops the
/// stock-group rate, and the re-imported book resolves GST differently from the original. This is the recurring
/// "Io-bypass" defect class the repo has been bitten by before (see the <c>EnsureValid</c> mirrors in
/// <c>ImportPlan</c>), so every one of the three levels is asserted with its OWN distinct HSN, rate, taxability and
/// supply type: a mapper that wrote the stock-group block into the group slot passes a same-values test and fails
/// this one.</para>
///
/// <para>The two source-order fields are asserted as a <b>pair of different values</b> for the same reason — the
/// reference application ships them as two separate options, and a mapper that read one and wrote it twice would be
/// invisible to any test that set them alike.</para>
/// </summary>
public class GstHierarchyIoTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // Odd, mutually distinct values — none coincides with a domain default, and no two levels agree.
    private const string StockGroupHsn = "85171213";
    private const int StockGroupRateBp = 1237;
    private const string GroupHsn = "998313";
    private const int GroupRateBp = 631;
    private const string CompanyHsn = "7318";
    private const int CompanyRateBp = 1741;

    private static Company Seeded()
    {
        var c = CompanyFactory.CreateSeeded("Hierarchy Io Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            Enabled = true,
            Gstin = GstinMaharashtra,
            HomeStateCode = "27",
        });

        c.AddStockGroup(new StockGroup(Guid.NewGuid(), "Mobile")
        {
            Gst = new MasterGstDetails
            {
                HsnSac = StockGroupHsn,
                RateBasisPoints = StockGroupRateBp,
                Taxability = GstTaxability.Taxable,
                SupplyType = GstSupplyType.Goods,
            },
        });
        // A second stock group with NO block, so "every group gets a block" is also a failure.
        c.AddStockGroup(new StockGroup(Guid.NewGuid(), "Accessories"));

        c.AddGroup(new Group(Guid.NewGuid(), "Consultancy Sales", GroupNature.Income,
            parentId: c.FindGroupByName("Sales Accounts")!.Id)
        {
            Gst = new MasterGstDetails
            {
                HsnSac = GroupHsn,
                RateBasisPoints = GroupRateBp,
                Taxability = GstTaxability.Taxable,
                SupplyType = GstSupplyType.Services,
            },
        });

        c.Gst!.DefaultGst = new MasterGstDetails
        {
            HsnSac = CompanyHsn,
            RateBasisPoints = CompanyRateBp,
            Taxability = GstTaxability.Taxable,
            SupplyType = GstSupplyType.Goods,
        };
        // Deliberately DIFFERENT from each other, so a writer that mapped one field twice fails.
        c.Gst.SourceOfHsnSacDetails = GstDetailSource.StockItemFirst;
        c.Gst.SourceOfGstRate = GstDetailSource.LedgerFirst;

        return c;
    }

    private static void AssertHierarchy(Company c)
    {
        var sg = c.StockGroups.Single(g => g.Name == "Mobile");
        Assert.Equal(StockGroupHsn, sg.Gst!.HsnSac);
        Assert.Equal(StockGroupRateBp, sg.Gst.RateBasisPoints);
        Assert.Equal(GstSupplyType.Goods, sg.Gst.SupplyType);
        Assert.Equal(GstTaxability.Taxable, sg.Gst.Taxability);

        // The block-less stock group stays block-less — no empty block is conjured.
        Assert.Null(c.StockGroups.Single(g => g.Name == "Accessories").Gst);

        var grp = c.Groups.Single(g => g.Name == "Consultancy Sales");
        Assert.Equal(GroupHsn, grp.Gst!.HsnSac);
        Assert.Equal(GroupRateBp, grp.Gst.RateBasisPoints);
        Assert.Equal(GstSupplyType.Services, grp.Gst.SupplyType);
        Assert.All(c.Groups.Where(g => g.Name != "Consultancy Sales"), g => Assert.Null(g.Gst));

        Assert.Equal(CompanyHsn, c.Gst!.DefaultGst!.HsnSac);
        Assert.Equal(CompanyRateBp, c.Gst.DefaultGst.RateBasisPoints);
        Assert.Equal(GstSupplyType.Goods, c.Gst.DefaultGst.SupplyType);

        Assert.Equal(GstDetailSource.StockItemFirst, c.Gst.SourceOfHsnSacDetails);
        Assert.Equal(GstDetailSource.LedgerFirst, c.Gst.SourceOfGstRate);
    }

    // ================================================================ lossless round-trips

    [Fact]
    public void Json_roundTrips_every_level_of_the_hierarchy()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(Seeded()));
        Assert.Empty(errors);

        Assert.Equal(StockGroupHsn, model!.Payload.StockGroups.Single(g => g.Name == "Mobile").Gst!.HsnSac);
        Assert.Equal(GroupHsn, model.Payload.Groups.Single(g => g.Name == "Consultancy Sales").Gst!.HsnSac);
        Assert.Equal(CompanyHsn, model.Company.Gst!.DefaultGst!.HsnSac);
        Assert.Equal("StockItemFirst", model.Company.Gst.SourceOfHsnSacDetails);
        Assert.Equal("LedgerFirst", model.Company.Gst.SourceOfGstRate);

        var target = CompanyFactory.CreateSeeded("Json Target", FyStart);
        Assert.True(new CompanyImportService(target).Apply(model!).Applied);
        AssertHierarchy(target);
    }

    [Fact]
    public void Xml_roundTrips_every_level_of_the_hierarchy()
    {
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(Seeded()));
        Assert.Empty(errors);

        Assert.Equal(StockGroupHsn, model!.Payload.StockGroups.Single(g => g.Name == "Mobile").Gst!.HsnSac);
        Assert.Equal(GroupHsn, model.Payload.Groups.Single(g => g.Name == "Consultancy Sales").Gst!.HsnSac);
        Assert.Equal(CompanyHsn, model.Company.Gst!.DefaultGst!.HsnSac);
        Assert.Equal("StockItemFirst", model.Company.Gst.SourceOfHsnSacDetails);
        Assert.Equal("LedgerFirst", model.Company.Gst.SourceOfGstRate);

        var target = CompanyFactory.CreateSeeded("Xml Target", FyStart);
        Assert.True(new CompanyImportService(target).Apply(model!).Applied);
        AssertHierarchy(target);
    }

    // ================================================================ ER-13: off ⇒ byte-identical

    /// <summary>
    /// A company that never touches the hierarchy exports and re-imports with every block absent and both source
    /// orders at the shipped <c>LedgerFirst</c> — so adding v51 changed nothing for an existing document (ER-13).
    /// </summary>
    [Fact]
    public void A_company_that_never_uses_the_hierarchy_is_unchanged_by_it()
    {
        var plain = CompanyFactory.CreateSeeded("Plain Io Co", FyStart);
        plain.AddStockGroup(new StockGroup(Guid.NewGuid(), "Anything"));

        foreach (var parsed in new[]
                 {
                     CanonicalJson.Parse(CanonicalJson.Export(plain)),
                     CanonicalXml.Parse(CanonicalXml.Export(plain)),
                 })
        {
            var (model, errors) = parsed;
            Assert.Empty(errors);
            Assert.All(model!.Payload.StockGroups, g => Assert.Null(g.Gst));
            Assert.All(model.Payload.Groups, g => Assert.Null(g.Gst));

            var target = CompanyFactory.CreateSeeded("Plain Target", FyStart);
            Assert.True(new CompanyImportService(target).Apply(model).Applied);
            Assert.All(target.StockGroups, g => Assert.Null(g.Gst));
            Assert.All(target.Groups, g => Assert.Null(g.Gst));
            // No GST config at all on a non-GST company — the source orders have nowhere to live and need none.
            Assert.Null(target.Gst);
        }
    }

    /// <summary>
    /// A pre-v51 document carries no hierarchy fields at all. Both readers must import it as "no blocks, shipped
    /// order" rather than throwing or inventing a block — and, unlike the v50 flag, the safe fallback here IS the
    /// enum's zero value, so this pins that the fallback exists at all rather than which value it is.
    /// </summary>
    [Fact]
    public void A_preV51_document_with_no_hierarchy_fields_imports_as_no_blocks_and_LedgerFirst()
    {
        var gstCompany = CompanyFactory.CreateSeeded("PreV51 Io Co", FyStart);
        new GstService(gstCompany).EnableGst(new GstConfig
        {
            Enabled = true,
            Gstin = GstinMaharashtra,
            HomeStateCode = "27",
        });
        gstCompany.AddStockGroup(new StockGroup(Guid.NewGuid(), "Legacy Group"));

        // Physically strip every v51 field from a REAL export — that is what a genuine pre-v51 document is, and it
        // makes the assertion about the parser rather than about a hand-written fixture.
        var json = Encoding.UTF8.GetString(CanonicalJson.Export(gstCompany));
        Assert.Contains("\"sourceOfHsnSacDetails\"", json, StringComparison.Ordinal);   // it really is there…
        // Drop both keys, then repair the comma the removal leaves behind — these two are the LAST members of the
        // gst object (the house rule is to append new fields at the end), so a naive key strip yields invalid JSON.
        var strippedJson = Regex.Replace(json, "\"(sourceOfHsnSacDetails|sourceOfGstRate)\"\\s*:\\s*\"[^\"]*\"\\s*,?", "");
        strippedJson = Regex.Replace(strippedJson, ",(\\s*[}\\]])", "$1");
        Assert.DoesNotContain("sourceOfHsnSacDetails", strippedJson, StringComparison.Ordinal);   // …and really is gone

        var (jsonModel, jsonErrors) = CanonicalJson.Parse(Encoding.UTF8.GetBytes(strippedJson));
        Assert.Empty(jsonErrors);
        Assert.Equal("LedgerFirst", jsonModel!.Company.Gst!.SourceOfHsnSacDetails);
        Assert.Equal("LedgerFirst", jsonModel.Company.Gst.SourceOfGstRate);
        Assert.Null(jsonModel.Company.Gst.DefaultGst);

        var doc = XDocument.Parse(Encoding.UTF8.GetString(CanonicalXml.Export(gstCompany)));
        var gstEl = doc.Descendants("gst").First();
        Assert.NotNull(gstEl.Attribute("sourceOfHsnSacDetails"));
        gstEl.Attribute("sourceOfHsnSacDetails")!.Remove();
        gstEl.Attribute("sourceOfGstRate")!.Remove();

        var (xmlModel, xmlErrors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(doc.ToString()));
        Assert.Empty(xmlErrors);
        Assert.Equal("LedgerFirst", xmlModel!.Company.Gst!.SourceOfHsnSacDetails);
        Assert.Equal("LedgerFirst", xmlModel.Company.Gst.SourceOfGstRate);
        Assert.Null(xmlModel.Company.Gst.DefaultGst);

        var target = CompanyFactory.CreateSeeded("PreV51 Target", FyStart);
        Assert.True(new CompanyImportService(target).Apply(xmlModel).Applied);
        Assert.Equal(GstDetailSource.LedgerFirst, target.Gst!.SourceOfHsnSacDetails);
        Assert.Equal(GstDetailSource.LedgerFirst, target.Gst.SourceOfGstRate);
        Assert.All(target.StockGroups, g => Assert.Null(g.Gst));
    }

    // ================================================================ the Io-bypass guard

    /// <summary>
    /// The recurring Io-bypass defect class: a malformed block must be rejected in pre-flight so the whole batch
    /// rolls back, rather than being persisted through a path that never runs the domain's fail-fast validation.
    /// Asserted on all three levels, because each is applied by a different code path in <c>ImportPlan</c>.
    /// </summary>
    [Theory]
    [InlineData("stockGroup")]
    [InlineData("group")]
    [InlineData("company")]
    public void A_malformed_block_is_rejected_all_or_nothing(string level)
    {
        var c = CompanyFactory.CreateSeeded("Bad Block Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            Enabled = true,
            Gstin = GstinMaharashtra,
            HomeStateCode = "27",
        });

        // "1234567" is 7 digits — not one of the permitted 4/6/8 lengths.
        var bad = new MasterGstDetails { HsnSac = "1234567", RateBasisPoints = 1237 };
        switch (level)
        {
            case "stockGroup": c.AddStockGroup(new StockGroup(Guid.NewGuid(), "Bad SG") { Gst = bad }); break;
            case "group":
                c.AddGroup(new Group(Guid.NewGuid(), "Bad Grp", GroupNature.Income,
                    parentId: c.FindGroupByName("Sales Accounts")!.Id) { Gst = bad });
                break;
            default: c.Gst!.DefaultGst = bad; break;
        }

        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(errors);

        var target = CompanyFactory.CreateSeeded("Bad Block Target", FyStart);
        var groupsBefore = target.Groups.Count;
        var stockGroupsBefore = target.StockGroups.Count;

        var result = new CompanyImportService(target).Apply(model!);

        Assert.False(result.Applied);
        // All-or-nothing: the target is untouched, not half-populated.
        Assert.Equal(groupsBefore, target.Groups.Count);
        Assert.Equal(stockGroupsBefore, target.StockGroups.Count);
    }
}
