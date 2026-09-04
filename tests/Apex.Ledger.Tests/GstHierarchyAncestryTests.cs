using System;
using System.Diagnostics;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Slice S2a's two group rungs, and the guarded ancestry walker they stand on
/// (<see cref="MasterAncestry"/>, <c>src/Apex.Ledger/Services/MasterAncestry.cs</c>).
///
/// <para>🔴 <b>ANCESTRY IS OURS, AND THIS FILE IS THE RECORD OF THE CHOICE (ruling 9).</b> Neither the corpus nor
/// the vendor says whether the "Accounting Group" and "Stock Group" rungs read the master's IMMEDIATE parent only
/// or climb the parent chain to the NEAREST ancestor bearing a block. Grounding is UNREACHED. We climb. The two
/// readings give DIFFERENT TAX on an ordinary book setup - a rate typed on a grandparent group - so the choice is
/// pinned by a named test rather than left to whichever line of code happened to be written.</para>
///
/// <para><b>Why a new walker rather than the one that already existed.</b> The only nearest-ancestor-with-a-value
/// walk in the tree, <c>ReorderStatus.ResolveDefinition</c>, has NO CYCLE GUARD - copying it verbatim would put an
/// unbounded loop on the money path, and a cyclic parent chain can already arrive in a book because
/// <c>InventoryService.EnsureStockGroupParentValid</c> guards the create/alter path and the canonical import path
/// is not guarded at all. <c>ClassificationRules.PrimaryAncestorOf</c> has a guard but walks to the PRIMARY
/// ancestor, which is the wrong shape (it would skip every intermediate block).</para>
/// </summary>
public sealed class GstHierarchyAncestryTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private const int GrandparentRateBp = 2800;
    private const int NearerRateBp = 1200;

    private static Company GstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Ancestry Probe Co", FyStart);
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

    private static MasterGstDetails Taxable(int bp) =>
        new() { Taxability = GstTaxability.Taxable, RateBasisPoints = bp };

    // ================================================================= the Stock Group rung climbs ancestry

    /// <summary>
    /// T11 (inventory side). Stock groups A -> B -> C, the item sits under C, and ONLY the grandparent A carries a
    /// block. EXPECT the item's rate to resolve to A's 2800 bp.
    ///
    /// <para>DERIVED: the immediate-parent reading answers "nothing at this rung" and falls through to the Company
    /// rung, which is empty here, and therefore to the ER-5 unresolved sentinel - a hard-blocked post. The
    /// ancestry reading answers 2800. The two are distinguishable to the paisa, which is the point.</para>
    /// </summary>
    [Fact]
    public void A_stock_group_rate_on_a_GRANDPARENT_is_inherited_by_the_item()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        var sgC = inv.CreateStockGroup("SG C", b.Id);
        a.Gst = Taxable(GrandparentRateBp);

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Deep Widget", sgC.Id, nos.Id);

        var r = gst.ResolveRate(item, salesPurchaseLedger: null, voucherDate: null);

        Assert.False(GstService.IsUnresolved(r));
        Assert.True(r.IsTaxable);
        Assert.Equal(GrandparentRateBp, r.RateBasisPoints);
    }

    /// <summary>
    /// NEAREST wins, not highest. Same A -> B -> C chain, but B also carries a block. EXPECT B's 1200 bp - the
    /// walk stops at the first ANCESTOR that carries the detail, exactly as it stops at the first RUNG that does.
    /// Without this row, a walker that climbed all the way to the primary ancestor (the shape
    /// <c>ClassificationRules.PrimaryAncestorOf</c> has) would still pass the test above.
    /// </summary>
    [Fact]
    public void The_NEAREST_stock_group_ancestor_bearing_a_block_wins()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        var sgC = inv.CreateStockGroup("SG C", b.Id);
        a.Gst = Taxable(GrandparentRateBp);
        b.Gst = Taxable(NearerRateBp);

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Deep Widget", sgC.Id, nos.Id);

        Assert.Equal(NearerRateBp, gst.ResolveRate(item, salesPurchaseLedger: null, voucherDate: null).RateBasisPoints);
    }

    // ================================================================= the Accounting Group rung climbs ancestry

    /// <summary>
    /// T11 (accounting side). Accounting groups A -> B -> C under Sales Accounts, the sales ledger sits under C,
    /// and only the grandparent A carries a block. EXPECT 2800 bp. Mirrors the inventory row above so that a
    /// walker wired correctly on one side and not the other cannot pass.
    /// </summary>
    [Fact]
    public void An_accounting_group_rate_on_a_GRANDPARENT_is_inherited_by_the_ledger()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var groups = new GroupService(c);

        var a = groups.CreateGroup("AG A", c.FindGroupByName("Sales Accounts")!.Id);
        var b = groups.CreateGroup("AG B", a.Id);
        var agC = groups.CreateGroup("AG C", b.Id);
        a.Gst = Taxable(GrandparentRateBp);

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Deep Sales", agC.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        var r = gst.ResolveRate(item: null, ledger, voucherDate: null);

        Assert.False(GstService.IsUnresolved(r));
        Assert.Equal(GrandparentRateBp, r.RateBasisPoints);
    }

    /// <summary>NEAREST wins on the accounting side too.</summary>
    [Fact]
    public void The_NEAREST_accounting_group_ancestor_bearing_a_block_wins()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var groups = new GroupService(c);

        var a = groups.CreateGroup("AG A", c.FindGroupByName("Sales Accounts")!.Id);
        var b = groups.CreateGroup("AG B", a.Id);
        var agC = groups.CreateGroup("AG C", b.Id);
        a.Gst = Taxable(GrandparentRateBp);
        b.Gst = Taxable(NearerRateBp);

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Deep Sales", agC.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        Assert.Equal(NearerRateBp, gst.ResolveRate(item: null, ledger, voucherDate: null).RateBasisPoints);
    }

    // ================================================================= T12 — the cycle guard

    /// <summary>
    /// T12 (inventory side). A stock-group parent CYCLE terminates in a NAMED domain error inside a bounded time,
    /// rather than hanging the money path.
    ///
    /// <para>The cycle is built by writing <c>ParentId</c> directly, which is exactly how one arrives in a real
    /// book: <c>InventoryService.EnsureStockGroupParentValid</c> guards <c>CreateStockGroup</c> and
    /// <c>SetStockGroupParent</c>, and the canonical import path does not go through either.</para>
    ///
    /// <para>Asserted three ways - it throws, the message NAMES the cycle, and it returns inside a wall-clock
    /// bound. "It returned" alone would pass on a walker that merely got lucky with the collection order.</para>
    /// </summary>
    [Fact]
    public void A_stock_group_parent_cycle_fails_fast_and_does_not_hang()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        a.ParentId = b.Id; // A -> B -> A, unreachable through the guarded create/alter API

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Cyclic Widget", b.Id, nos.Id);

        var sw = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidOperationException>(() => gst.ResolveRate(item, salesPurchaseLedger: null, voucherDate: null));
        sw.Stop();

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"the cycle guard took {sw.Elapsed} — it must be bounded");
    }

    /// <summary>T12 (accounting side). The same guard on the accounting-group chain.</summary>
    [Fact]
    public void An_accounting_group_parent_cycle_fails_fast_and_does_not_hang()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var groups = new GroupService(c);

        var a = groups.CreateGroup("AG A", c.FindGroupByName("Sales Accounts")!.Id);
        var b = groups.CreateGroup("AG B", a.Id);
        a.ParentId = b.Id; // AG A -> AG B -> AG A

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Cyclic Sales", b.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        var sw = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidOperationException>(() => gst.ResolveRate(item: null, ledger, voucherDate: null));
        sw.Stop();

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"the cycle guard took {sw.Elapsed} — it must be bounded");
    }

    /// <summary>
    /// 🔴 THE CYCLE GUARD MUST NOT COST AN EXISTING BOOK ITS POSTS. A rung is consulted only when the walk actually
    /// REACHES it, so a corrupt stock-group chain hanging below a stock item that answers for itself is never
    /// touched and the line rates normally at 1800 bp.
    ///
    /// <para>Without this row the fail-fast above would be a behaviour change on an existing book rather than a
    /// guard on a new rung: one bad parent id anywhere under an item would make every line on that item
    /// unpostable, including every line that resolved perfectly before slice S2a. The walk is a lazy sequence for
    /// exactly this reason.</para>
    /// </summary>
    [Fact]
    public void A_cycle_below_an_answering_item_rung_is_never_reached()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        a.ParentId = b.Id; // A -> B -> A

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Self-Rated Widget", b.Id, nos.Id);
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        Assert.Equal(1800, gst.ResolveRate(item, salesPurchaseLedger: null, voucherDate: null).RateBasisPoints);
    }

    /// <summary>
    /// A DANGLING parent id is NOT a cycle and must not be treated as one. The chain simply ends: an ancestor the
    /// book does not contain declares nothing, so the rung contributes nothing and the walk falls through to the
    /// rungs below it - here, the company default at 300 bp. OURS, labelled: no source addresses a broken chain.
    /// Recorded because the alternative (throwing) would turn one corrupt id into an unpostable book.
    /// </summary>
    [Fact]
    public void A_dangling_stock_group_parent_ends_the_climb_without_throwing()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var a = inv.CreateStockGroup("SG A");
        a.ParentId = Guid.NewGuid(); // an ancestor that is not in the book
        c.Gst!.DefaultGst = Taxable(300);

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Orphan Widget", a.Id, nos.Id);

        Assert.Equal(300, gst.ResolveRate(item, salesPurchaseLedger: null, voucherDate: null).RateBasisPoints);
    }
}
