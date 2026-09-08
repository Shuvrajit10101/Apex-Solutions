using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>6.25 — GST Classification / Nature-of-Transaction master</b> (schema v61). The vendor defines it
/// as "<i>a powerful tool for recording tax rates and other details for categories of goods and services that
/// attract a common GST rate</i>"
/// (<c>help.tallysolutions.com/tally-prime/gst-master-setup/india-gst-create-use-and-update-gst-classifications-tally/</c>),
/// created at <i>Gateway of Tally &gt; Create &gt; … GST Classification</i>, with <i>Central Tax</i> and
/// <i>State Tax</i> shown auto-calculated from the <i>Integrated Tax rate</i>.
///
/// <para>🔴 <b>THE TEST THAT MATTERS MOST HERE IS THE ONE THAT PROVES NOTHING CHANGED.</b> A classification is
/// applied by ASSIGNMENT — the vendor copies its details onto the target master — so it must NOT become a live
/// sixth level of the rate hierarchy. If it did, creating one would silently re-rate documents on existing books.
/// <see cref="Creating_a_classification_changes_no_resolved_rate_anywhere"/> is that guard.</para>
/// </summary>
public class GstClassificationMasterTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static Company Build()
    {
        var c = CompanyFactory.CreateSeeded("Classification Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    [Fact]
    public void A_new_gst_company_holds_no_classifications()
    {
        // ER-13: the collection is empty on every pre-v61 book and on every book that never creates one.
        Assert.Empty(Build().Gst!.Classifications);
    }

    [Fact]
    public void A_classification_records_the_vendors_hsn_and_rate_sections()
    {
        var c = Build();
        var gc = new GstClassification(
            Guid.NewGuid(), "Textiles 5%", hsnSac: "520100", description: "Cotton, not carded",
            taxability: GstTaxability.Taxable, rateBasisPoints: 500,
            supplyType: GstSupplyType.Goods, natureOfTransaction: "Interstate Sales Taxable");
        c.Gst!.AddClassification(gc);
        c.Gst.EnsureValid();

        var stored = Assert.Single(c.Gst.Classifications);
        Assert.Equal("Textiles 5%", stored.Name);
        Assert.Equal("520100", stored.HsnSac);
        Assert.Equal("Cotton, not carded", stored.Description);
        Assert.Equal(500, stored.RateBasisPoints);
        Assert.Equal("Interstate Sales Taxable", stored.NatureOfTransaction);
    }

    [Fact]
    public void Central_and_state_tax_are_derived_halves_that_always_sum_to_the_integrated_rate()
    {
        // The vendor shows both auto-calculated, so they are computed rather than stored — two stored halves
        // could drift from the integrated rate they are supposed to split.
        var even = new GstClassification(Guid.NewGuid(), "18%", rateBasisPoints: 1800);
        Assert.Equal(900, even.CentralTaxBasisPoints);
        Assert.Equal(900, even.StateTaxBasisPoints);

        // 🔴 An ODD rate is where a naive "bp / 2 for both" silently loses a basis point. The remainder rides on
        // the Central half so the two ALWAYS reconstruct the integrated rate exactly.
        var odd = new GstClassification(Guid.NewGuid(), "0.25%", rateBasisPoints: 25);
        Assert.Equal(13, odd.CentralTaxBasisPoints);
        Assert.Equal(12, odd.StateTaxBasisPoints);
        Assert.Equal(25, odd.CentralTaxBasisPoints + odd.StateTaxBasisPoints);

        // No rate declared ⇒ no halves to show, rather than a misleading 0%.
        var none = new GstClassification(Guid.NewGuid(), "Unrated");
        Assert.Null(none.CentralTaxBasisPoints);
        Assert.Null(none.StateTaxBasisPoints);
    }

    [Fact]
    public void A_classification_obeys_the_same_three_rules_a_stock_groups_gst_block_obeys()
    {
        // These are MasterGstDetails.EnsureValid's rules. They have to hold here too, because ToDetails() COPIES
        // this block onto such a master — a value rejected there must not be smuggled in through here.
        Assert.Throws<ArgumentException>(() =>
            new GstClassification(Guid.NewGuid(), "Bad HSN", hsnSac: "12345"));      // 5 digits
        Assert.Throws<ArgumentException>(() =>
            new GstClassification(Guid.NewGuid(), "Bad HSN", hsnSac: "ABCDEFGH"));   // non-numeric
        Assert.Throws<ArgumentException>(() =>
            new GstClassification(Guid.NewGuid(), "Negative", rateBasisPoints: -500));
        Assert.Throws<ArgumentException>(() =>
            new GstClassification(Guid.NewGuid(), "Exempt but rated",
                taxability: GstTaxability.Exempt, rateBasisPoints: 1800));
        Assert.Throws<ArgumentException>(() => new GstClassification(Guid.NewGuid(), "   "));
    }

    [Fact]
    public void Two_classifications_may_not_share_a_name()
    {
        var c = Build();
        c.Gst!.AddClassification(new GstClassification(Guid.NewGuid(), "Textiles 5%", rateBasisPoints: 500));
        // Case-insensitively — the name is how the operator picks one, so two that differ only in case would be
        // indistinguishable in the picker.
        c.Gst.AddClassification(new GstClassification(Guid.NewGuid(), "TEXTILES 5%", rateBasisPoints: 500));
        Assert.Throws<ArgumentException>(() => c.Gst.EnsureValid());
    }

    [Fact]
    public void Assigning_a_classification_copies_its_details_onto_the_target_master()
    {
        var gc = new GstClassification(
            Guid.NewGuid(), "Electronics 18%", hsnSac: "847130", rateBasisPoints: 1800,
            supplyType: GstSupplyType.Goods);

        // The vendor's "tax details automatically populate from the classification". A COPY, taken now.
        var details = gc.ToDetails();
        Assert.Equal("847130", details.HsnSac);
        Assert.Equal(1800, details.RateBasisPoints);
        Assert.Equal(GstTaxability.Taxable, details.Taxability);
        Assert.Equal(GstSupplyType.Goods, details.SupplyType);

        // …and it validates as the master block it is about to become, which is the whole point of applying the
        // same three rules on construction.
        details.EnsureValid();
    }

    [Fact]
    public void Creating_a_classification_changes_no_resolved_rate_anywhere()
    {
        var c = Build();
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var gst = new GstService(c);
        var before = gst.ResolveRate(item, null, FyStart);

        // 🔴 A classification that CONTRADICTS the item, created but never assigned. If the master had been
        // wired in as a live hierarchy level, this line would move the item's rate from 18% to 5% — re-rating
        // every future invoice on an existing book as a side effect of creating a master. It must not.
        c.Gst!.AddClassification(new GstClassification(
            Guid.NewGuid(), "Contradicts the item", hsnSac: "999999", rateBasisPoints: 500));
        c.Gst.EnsureValid();

        var after = gst.ResolveRate(item, null, FyStart);
        Assert.Equal(before.RateBasisPoints, after.RateBasisPoints);
        Assert.Equal(1800, after.RateBasisPoints);
    }
}
