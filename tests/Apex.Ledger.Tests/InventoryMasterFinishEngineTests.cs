using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE ENGINE HALF OF THE LAST OPEN CLAUSES ON CENSUS ROWS 3.11 AND 3.12.</b>
///
/// <list type="bullet">
///   <item><b>3.11</b> — <c>PriceListService.DeleteList</c> withdraws ONE dated version and nothing withdrew the
///     WHOLE price list for a (level, item) pair. <c>DeleteAllListsFor</c> is that act, and the tests below
///     assert it through <c>PriceResolver</c> — the production reader every invoice goes through — rather than
///     by counting rows, because what matters is the PRICE the next invoice is offered.</item>
///   <item><b>3.12</b> — <c>ReorderLevelsViewModel.IsAltering</c> was the constant <c>false</c> and the upsert
///     was the only way to change a definition. <c>CreateOrUpdate</c> mints a <c>Guid.NewGuid()</c>, so it
///     cannot BE the alter: the tests below pin that difference in both directions.</item>
/// </list>
///
/// <para><b>FIDELITY (R7, ruling 14).</b> 3.12's alter route is <b>VENDOR-ATTESTED</b> —
/// <i>"Press Alt+G (Go To) &gt; Alter Master &gt; type or select Reorder Level (Stock Item) and press
/// Enter."</i>, with <i>"Period: Select the consumption for various periods – Days, Weeks, Months, or
/// Years."</i> and a <i>Criteria</i> of Higher or Lower
/// [<c>help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/</c>,
/// opened BY CONTENT 2026-10-23]. ⚠️ 3.11's whole-list DELETE is <b>OURS</b>: the price-list page
/// [<c>help.tallysolutions.com/tally-prime/sales-purchase-voucher-entry/selling-buying-prices/</c>, opened BY
/// CONTENT the same day] documents create, revise (<i>"press Alt+R (Revise Price)"</i>) and alter
/// (<i>"You can alter a price list by overwriting the details entered in the Price List screen."</i>) and never
/// mentions deletion. That silence is recorded as silence — it is not read as permission and not as
/// prohibition — and <b>no vendor sentence is claimed for the verb</b>.</para>
/// </summary>
public class InventoryMasterFinishEngineTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static Company Seed(string name = "Cd Finish Co")
        => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    private static StockItem NewItem(Company c, string name)
    {
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        return inv.CreateStockItem(name, group.Id, unit.Id);
    }

    // ==================================================================== 3.11 — the WHOLE price list

    /// <summary>
    /// 🔴 <b>THE CLAUSE, PROVEN THROUGH THE PRODUCTION PRICE READER.</b> Three dated versions of one (level,
    /// item) price list; <c>DeleteAllListsFor</c> takes the list away as ONE act. Before, every route left a
    /// surviving version — and <c>PriceResolver</c> prices an invoice off the latest surviving version whose
    /// Applicable-From is ≤ the voucher date, so "delete them one at a time" silently falls the pair back onto
    /// the OLDEST price. Asserted on <c>PriceResolver.Resolve</c> at three dates spanning all three windows.
    /// </summary>
    [Fact]
    public void Deleting_the_whole_price_list_leaves_no_date_at_which_the_pair_is_still_priced()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");

        svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 4, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) });
        svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 7, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(110m)) });
        svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 10, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(120m)) });

        // The control: every window prices, and each window prices from ITS OWN version.
        Assert.Equal(100m, PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 5, 1))!.Value.Rate.Amount);
        Assert.Equal(110m, PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 8, 1))!.Value.Rate.Amount);
        Assert.Equal(120m, PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 11, 1))!.Value.Rate.Amount);

        var withdrawn = svc.DeleteAllListsFor(level.Id, item.Id);

        Assert.Equal(3, withdrawn);
        Assert.Empty(c.PriceLists.Where(pl => pl.PriceLevelId == level.Id && pl.StockItemId == item.Id));
        // 🔴 The artefact: NO date is still priced. A single surviving version would show up here as a non-null.
        Assert.Null(PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 5, 1)));
        Assert.Null(PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 8, 1)));
        Assert.Null(PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 11, 1)));
    }

    /// <summary>
    /// 🔴 <b>THE "ONE AT A TIME IS NOT THE SAME ACT" PROOF, which is why the clause was real.</b> Withdrawing
    /// the two NEWEST versions by <c>DeleteList</c> leaves the pair priced at the OLDEST rate on a date the
    /// operator had just re-priced — a live wrong rate on every subsequent invoice. The whole-list verb cannot
    /// reach that state because it is all-or-nothing.
    /// </summary>
    [Fact]
    public void Withdrawing_versions_one_at_a_time_falls_back_onto_the_oldest_price()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");

        svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 4, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) });
        var v2 = svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 7, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(110m)) });
        var v3 = svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 10, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(120m)) });

        svc.DeleteList(v3.Id);
        svc.DeleteList(v2.Id);

        var stillPriced = PriceResolver.Resolve(c, level.Id, item.Id, 5m, new DateOnly(2024, 11, 1));
        Assert.NotNull(stillPriced);
        Assert.Equal(100m, stillPriced!.Value.Rate.Amount);
    }

    /// <summary>
    /// The SLAB BOUNDARIES on both sides of the withdrawal, because a price list is a dated SLAB structure and a
    /// verb that silently clipped a band would misprice by quantity rather than by date. The two bands price
    /// differently before (a 9 and a 10 straddle the 0–10 / 10–onwards boundary) and NEITHER prices after.
    /// </summary>
    [Fact]
    public void Both_slab_bands_price_before_the_withdrawal_and_neither_prices_after()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");

        svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 4, 1), new[]
        {
            new PriceListSlab(0m, 10m, Money.FromRupees(100m)),
            new PriceListSlab(10m, null, Money.FromRupees(90m)),
        });

        var on = new DateOnly(2024, 5, 1);
        Assert.Equal(100m, PriceResolver.Resolve(c, level.Id, item.Id, 9m, on)!.Value.Rate.Amount);
        Assert.Equal(90m, PriceResolver.Resolve(c, level.Id, item.Id, 10m, on)!.Value.Rate.Amount);

        svc.DeleteAllListsFor(level.Id, item.Id);

        Assert.Null(PriceResolver.Resolve(c, level.Id, item.Id, 9m, on));
        Assert.Null(PriceResolver.Resolve(c, level.Id, item.Id, 10m, on));
    }

    /// <summary>The verb is scoped to its PAIR: another item under the same level, and the same item under
    /// another level, both keep their prices. A destructive verb that over-reached by one key would be caught
    /// here rather than by the operator who lost a second price list.</summary>
    [Fact]
    public void Withdrawing_one_pair_leaves_every_other_pair_priced()
    {
        var c = Seed();
        var widget = NewItem(c, "Widget");
        var hinge = NewItem(c, "Hinge");
        var svc = new PriceListService(c);
        var wholesale = svc.CreateLevel("Wholesale");
        var retail = svc.CreateLevel("Retail");

        var slab = new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) };
        svc.AddOrReviseList(wholesale.Id, widget.Id, new DateOnly(2024, 4, 1), slab);
        svc.AddOrReviseList(wholesale.Id, hinge.Id, new DateOnly(2024, 4, 1), slab);
        svc.AddOrReviseList(retail.Id, widget.Id, new DateOnly(2024, 4, 1), slab);

        svc.DeleteAllListsFor(wholesale.Id, widget.Id);

        var on = new DateOnly(2024, 5, 1);
        Assert.Null(PriceResolver.Resolve(c, wholesale.Id, widget.Id, 5m, on));
        Assert.NotNull(PriceResolver.Resolve(c, wholesale.Id, hinge.Id, 5m, on));
        Assert.NotNull(PriceResolver.Resolve(c, retail.Id, widget.Id, 5m, on));
    }

    /// <summary>A destructive verb that silently succeeds against nothing is indistinguishable from one that is
    /// broken, so an unknown level, an unknown item and a pair with no price list are all named refusals.</summary>
    [Fact]
    public void Withdrawing_a_price_list_that_is_not_there_is_a_named_refusal()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");

        Assert.Throws<InvalidOperationException>(() => svc.DeleteAllListsFor(Guid.NewGuid(), item.Id));
        Assert.Throws<InvalidOperationException>(() => svc.DeleteAllListsFor(level.Id, Guid.NewGuid()));
        var noList = Assert.Throws<InvalidOperationException>(() => svc.DeleteAllListsFor(level.Id, item.Id));
        Assert.Contains("no price list", noList.Message);
    }

    // ==================================================================== 3.12 — the reorder ALTER

    /// <summary>
    /// 🔴 <b>THE DIFFERENCE THE ROW TURNS ON.</b> <c>CreateOrUpdate</c> replaces the definition with a record
    /// carrying a DIFFERENT id; <c>Alter</c> keeps the id. This test pins both directions in one place, so a
    /// future "simplification" that routes Alter back through the upsert goes red here.
    /// </summary>
    [Fact]
    public void Alter_keeps_the_definition_id_while_the_upsert_replaces_it()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new ReorderLevelsService(c);

        var original = svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 100m);

        var altered = svc.Alter(original.Id, ReorderScope.Item, item.Id, reorderQuantity: 150m);
        Assert.Equal(original.Id, altered.Id);
        Assert.Equal(150m, Assert.Single(c.ReorderDefinitions).ReorderQuantity);

        var upserted = svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 175m);
        Assert.NotEqual(original.Id, upserted.Id);
        Assert.Equal(175m, Assert.Single(c.ReorderDefinitions).ReorderQuantity);
    }

    /// <summary>Every field is rewritten by the alter, including the Advanced period/criterion triple — the
    /// fields a blank create form silently wiped before the screen loaded them back.</summary>
    [Fact]
    public void Alter_rewrites_every_field_of_the_definition()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new ReorderLevelsService(c);
        var original = svc.CreateOrUpdate(ReorderScope.Item, item.Id,
            reorderAdvanced: true, reorderQuantity: 100m,
            minQtyAdvanced: true, minOrderQuantity: 25m,
            periodCount: 3, periodUnit: ExpiryPeriodUnit.Months, criteria: ReorderCriteria.Higher);

        svc.Alter(original.Id, ReorderScope.Item, item.Id,
            reorderAdvanced: false, reorderQuantity: 150m,
            minQtyAdvanced: true, minOrderQuantity: 40m,
            periodCount: 8, periodUnit: ExpiryPeriodUnit.Weeks, criteria: ReorderCriteria.Lower);

        var d = Assert.Single(c.ReorderDefinitions);
        Assert.Equal(original.Id, d.Id);
        Assert.False(d.ReorderAdvanced);
        Assert.Equal(150m, d.ReorderQuantity);
        Assert.True(d.MinQtyAdvanced);
        Assert.Equal(40m, d.MinOrderQuantity);
        Assert.Equal(8, d.PeriodCount);
        Assert.Equal(ExpiryPeriodUnit.Weeks, d.PeriodUnit);
        Assert.Equal(ReorderCriteria.Lower, d.Criteria);
    }

    /// <summary>
    /// 🔴 <b>THE INVARIANT AN ALTER MUST NOT BREAK.</b> At most one definition per (scope, target). Re-pointing
    /// a definition onto a pair ANOTHER definition already owns is refused by name — upserting over it would
    /// delete a second, unrelated definition the operator never named. The company is left untouched.
    /// </summary>
    [Fact]
    public void Altering_a_definition_onto_a_target_another_definition_owns_is_refused()
    {
        var c = Seed();
        var widget = NewItem(c, "Widget");
        var hinge = NewItem(c, "Hinge");
        var svc = new ReorderLevelsService(c);
        var first = svc.CreateOrUpdate(ReorderScope.Item, widget.Id, reorderQuantity: 100m);
        var second = svc.CreateOrUpdate(ReorderScope.Item, hinge.Id, reorderQuantity: 50m);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => svc.Alter(first.Id, ReorderScope.Item, hinge.Id, reorderQuantity: 999m));
        Assert.Contains("already covers", refusal.Message);

        Assert.Equal(2, c.ReorderDefinitions.Count);
        Assert.Equal(100m, c.FindReorderDefinition(first.Id)!.ReorderQuantity);
        Assert.Equal(50m, c.FindReorderDefinition(second.Id)!.ReorderQuantity);
    }

    /// <summary>Re-accepting a definition over its OWN (scope, target) is of course allowed — the uniqueness
    /// guard must not block the ordinary case of correcting one figure and saving.</summary>
    [Fact]
    public void Altering_a_definition_onto_its_own_target_is_allowed()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new ReorderLevelsService(c);
        var original = svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 100m);

        var altered = svc.Alter(original.Id, ReorderScope.Item, item.Id, reorderQuantity: 100m);

        Assert.Equal(original.Id, altered.Id);
        Assert.Single(c.ReorderDefinitions);
    }

    /// <summary>An unknown definition, an unknown target, and figures the domain refuses are all named failures
    /// that leave the company untouched (ER-12) — the validation happens before the old row is removed.</summary>
    [Fact]
    public void Alter_refuses_without_mutating_the_company()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new ReorderLevelsService(c);
        var original = svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 100m);

        Assert.Throws<InvalidOperationException>(
            () => svc.Alter(Guid.NewGuid(), ReorderScope.Item, item.Id, reorderQuantity: 150m));
        Assert.Throws<InvalidOperationException>(
            () => svc.Alter(original.Id, ReorderScope.Item, Guid.NewGuid(), reorderQuantity: 150m));
        // Advanced with no period: the ReorderDefinition constructor's own refusal, raised BEFORE the remove.
        Assert.Throws<InvalidOperationException>(
            () => svc.Alter(original.Id, ReorderScope.Item, item.Id, reorderAdvanced: true, reorderQuantity: 150m));

        var survivor = Assert.Single(c.ReorderDefinitions);
        Assert.Equal(original.Id, survivor.Id);
        Assert.Equal(100m, survivor.ReorderQuantity);
    }
}
