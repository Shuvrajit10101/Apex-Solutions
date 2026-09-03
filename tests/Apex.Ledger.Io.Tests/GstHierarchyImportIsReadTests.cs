using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>THE LIVE DEFECT SLICE S2a CLOSES, PROVED END TO END.</b> Canonical import has PARSED the three narrow
/// <see cref="MasterGstDetails"/> blocks since schema v51 - <c>ImportPlan</c> writes the Stock Group block, the
/// accounting Group block and the company <c>DefaultGst</c> - and <b>nothing read them</b>. The whole feature
/// round-tripped losslessly (<c>GstHierarchyIoTests</c> pins that) into a book where every one of the three levels
/// resolved to the ER-5 unresolved sentinel and hard-blocked the post.
///
/// <para>This file asserts the other half: that a rate typed at any of the three imported levels now RESOLVES on
/// the imported book. It is deliberately separate from <c>GstHierarchyIoTests</c>, which slice S2a's design
/// requires to pass UNTOUCHED as the cheapest possible proof that no migration was added here.</para>
///
/// <para>The three probes are kept apart on purpose: one line reaches the Stock Group rung, one reaches the
/// Accounting Group rung, and one falls through both to the Company rung. Their rates are pairwise distinct and
/// none coincides with a domain default, so a resolver that wired one rung into another's slot cannot pass.</para>
/// </summary>
public class GstHierarchyImportIsReadTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private const int StockGroupRateBp = 1237;
    private const int GroupRateBp = 631;
    private const int CompanyRateBp = 1741;

    /// <summary>
    /// A book whose GST rates live ONLY at the three levels canonical import can write, exported and re-imported.
    /// Every stock item and every sales ledger is deliberately block-less - the two rungs this application could
    /// already see contribute nothing, so each probe is answered by an imported rung or by nothing at all.
    /// </summary>
    private static Company Source()
    {
        var c = CompanyFactory.CreateSeeded("Imported Hierarchy Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            Enabled = true,
            Gstin = GstinMaharashtra,
            HomeStateCode = "27",
        });

        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");

        var mobile = inv.CreateStockGroup("Mobile");
        mobile.Gst = new MasterGstDetails
        {
            RateBasisPoints = StockGroupRateBp,
            Taxability = GstTaxability.Taxable,
            SupplyType = GstSupplyType.Goods,
        };
        inv.CreateStockItem("Handset", mobile.Id, nos.Id);

        // A block-less stock group, so the company rung stays reachable from an item.
        var accessories = inv.CreateStockGroup("Accessories");
        inv.CreateStockItem("Lanyard", accessories.Id, nos.Id);

        var consultancy = new GroupService(c).CreateGroup("Consultancy Sales", c.FindGroupByName("Sales Accounts")!.Id);
        consultancy.Gst = new MasterGstDetails
        {
            RateBasisPoints = GroupRateBp,
            Taxability = GstTaxability.Taxable,
            SupplyType = GstSupplyType.Services,
        };
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Consultancy Income", consultancy.Id, Money.Zero, openingIsDebit: false));

        c.Gst!.DefaultGst = new MasterGstDetails
        {
            RateBasisPoints = CompanyRateBp,
            Taxability = GstTaxability.Taxable,
            SupplyType = GstSupplyType.Goods,
        };
        return c;
    }

    private static Company ExportAndImport()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(Source()));
        Assert.Empty(errors);

        var target = CompanyFactory.CreateSeeded("Import Target", FyStart);
        Assert.True(new CompanyImportService(target).Apply(model!).Applied);
        return target;
    }

    /// <summary>An imported STOCK GROUP rate answers a line whose item declares nothing.</summary>
    [Fact]
    public void An_imported_stock_group_rate_now_resolves_on_the_imported_book()
    {
        var c = ExportAndImport();
        var item = c.StockItems.Single(i => i.Name == "Handset");

        var r = new GstService(c).ResolveRate(item, salesPurchaseLedger: null);

        Assert.False(GstService.IsUnresolved(r));
        Assert.True(r.IsTaxable);
        Assert.Equal(StockGroupRateBp, r.RateBasisPoints);
    }

    /// <summary>An imported ACCOUNTING GROUP rate answers a line whose sales ledger declares nothing.</summary>
    [Fact]
    public void An_imported_accounting_group_rate_now_resolves_on_the_imported_book()
    {
        var c = ExportAndImport();
        var ledger = c.Ledgers.Single(l => l.Name == "Consultancy Income");

        var r = new GstService(c).ResolveRate(item: null, ledger);

        Assert.False(GstService.IsUnresolved(r));
        Assert.Equal(GroupRateBp, r.RateBasisPoints);
    }

    /// <summary>
    /// An imported COMPANY DEFAULT answers a line that nothing above it claims - the rung the ER-5 sentinel used
    /// to fire in front of. This is the row that turns "GSTN PDF p.121's single-rate business" from an unpostable
    /// book into a postable one.
    /// </summary>
    [Fact]
    public void An_imported_company_default_now_resolves_on_the_imported_book()
    {
        var c = ExportAndImport();
        var item = c.StockItems.Single(i => i.Name == "Lanyard");

        var r = new GstService(c).ResolveRate(item, salesPurchaseLedger: null);

        Assert.False(GstService.IsUnresolved(r));
        Assert.Equal(CompanyRateBp, r.RateBasisPoints);
    }

    /// <summary>
    /// The counterweight: importing the hierarchy does NOT make everything resolve. A book with none of the three
    /// blocks still fails fast on a taxable line, so the sentinel moved behind the Company rung rather than
    /// disappearing - and a silent zero is still impossible.
    /// </summary>
    [Fact]
    public void A_book_with_no_hierarchy_blocks_at_all_still_fails_fast()
    {
        var c = CompanyFactory.CreateSeeded("Bare Co", FyStart);
        new GstService(c).EnableGst(new GstConfig { Enabled = true, Gstin = GstinMaharashtra, HomeStateCode = "27" });

        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var sg = inv.CreateStockGroup("Bare Group");
        var item = inv.CreateStockItem("Bare Widget", sg.Id, nos.Id);

        Assert.True(GstService.IsUnresolved(new GstService(c).ResolveRate(item, salesPurchaseLedger: null)));
    }
}
