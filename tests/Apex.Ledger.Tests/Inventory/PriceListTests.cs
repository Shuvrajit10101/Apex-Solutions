using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Price Levels / Price Lists engine tests (Phase 6 slice 5; requirements RQ-26..RQ-31, DP-A, PR-7). Covers the
/// price-level master + case-insensitive uniqueness + delete guards (RQ-26), append-only dated revisions (RQ-27),
/// the TOP-RISK #5 quantity-slab resolution (From≥ / To&lt;; boundary lands in the HIGHER slab — RQ-28), the
/// latest-Applicable-From≤voucher-date resolution (RQ-29), discount handling / deterministic effective rate
/// (DP-A), the slab-validation guards, and the Price List report (RQ-31). Pure, deterministic, paisa/micro-exact
/// — the resolver never touches posting/valuation (ER-7), so the accounting invariants are untouched.
/// </summary>
public class PriceListTests
{
    private static readonly DateOnly Fy = new(2026, 4, 1);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required PriceListService Service { get; init; }
        public required Guid ItemId { get; init; }
    }

    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Price Co", Fy);
        c.EnableMultiplePriceLevels = true;
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Laptop", grp.Id, nos.Id);
        return new Kit { Company = c, Service = new PriceListService(c), ItemId = item.Id };
    }

    // ---------------------------------------------------------------- PR-7 worked example (hard gate)

    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Pr7_retail_slabs_resolve_the_worked_example_with_boundary_going_up()
    {
        // Retail: 0–2 → 16,000 ; 2–4 → 14,850 (last slab CLOSED — qty 4 falls in no slab → null).
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
        {
            new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
            new PriceListSlab(2m, 4m, Money.FromRupees(14850m)),
        });

        Money Rate(decimal qty) =>
            PriceResolver.Resolve(kit.Company, retail.Id, kit.ItemId, qty, Apr1)!.Value.Rate;

        Assert.Equal(Money.FromRupees(14850m), Rate(3m));    // PR-7 headline: qty 3 → 14,850
        Assert.Equal(Money.FromRupees(14850m), Rate(2m));    // boundary qty 2 → the HIGHER (2–4) slab
        Assert.Equal(Money.FromRupees(16000m), Rate(1m));    // qty 1 → 16,000
        Assert.Equal(Money.FromRupees(16000m), Rate(1.999999m));
        Assert.Equal(Money.FromRupees(14850m), Rate(2.000001m));

        // qty 4 lands on the exclusive top of the last CLOSED slab → no slab → null (no auto-fill).
        Assert.Null(PriceResolver.Resolve(kit.Company, retail.Id, kit.ItemId, 4m, Apr1));
        // qty below the first slab (never, since it starts at 0) — a slab starting at 1 would leave qty 0 null.
    }

    [Fact]
    public void Open_ended_top_slab_prices_any_quantity_at_or_above_its_floor()
    {
        var kit = NewKit();
        var wholesale = kit.Service.CreateLevel("Wholesale");
        kit.Service.AddOrReviseList(wholesale.Id, kit.ItemId, Apr1, new[]
        {
            new PriceListSlab(0m, 10m, Money.FromRupees(1000m)),
            new PriceListSlab(10m, null, Money.FromRupees(900m)), // open-ended top
        });

        Assert.Equal(Money.FromRupees(900m),
            PriceResolver.Resolve(kit.Company, wholesale.Id, kit.ItemId, 10m, Apr1)!.Value.Rate);
        Assert.Equal(Money.FromRupees(900m),
            PriceResolver.Resolve(kit.Company, wholesale.Id, kit.ItemId, 10_000m, Apr1)!.Value.Rate);
    }

    // ---------------------------------------------------------------- RQ-29 latest-date wins

    [Fact]
    public void Latest_applicable_from_on_or_before_voucher_date_wins()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(16000m)) });
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Jul1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(15000m)) });

        Money? Rate(DateOnly d) =>
            PriceResolver.Resolve(kit.Company, retail.Id, kit.ItemId, 1m, d)?.Rate;

        Assert.Equal(Money.FromRupees(16000m), Rate(new DateOnly(2026, 6, 15)));  // between Apr and Jul → Apr version
        Assert.Equal(Money.FromRupees(15000m), Rate(new DateOnly(2026, 8, 15)));  // after Jul → Jul version
        Assert.Null(Rate(new DateOnly(2026, 3, 1)));                              // before the first version → null
    }

    // ---------------------------------------------------------------- RQ-27 append-only revisions

    [Fact]
    public void Revising_a_list_appends_a_new_dated_version_leaving_the_old_intact()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        var v1 = kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(16000m)) });
        var v2 = kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Jul1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(15000m)) });

        // Two rows exist; the older is untouched (append-only history).
        var versions = kit.Company.PriceListsFor(retail.Id, kit.ItemId).ToList();
        Assert.Equal(2, versions.Count);
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.Contains(versions, v => v.ApplicableFrom == Apr1 && v.Slabs[0].Rate == Money.FromRupees(16000m));
        Assert.Contains(versions, v => v.ApplicableFrom == Jul1 && v.Slabs[0].Rate == Money.FromRupees(15000m));
    }

    [Fact]
    public void A_same_or_earlier_applicable_from_is_rejected_as_not_a_revision()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Jul1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(15000m)) });

        // Same date is an edit, not a revision.
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Jul1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(14000m)) }));
        // An earlier date, too.
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(14000m)) }));
        // Only one version survived.
        Assert.Single(kit.Company.PriceListsFor(retail.Id, kit.ItemId));
    }

    // ---------------------------------------------------------------- slab validation

    [Fact]
    public void Overlapping_gapped_descending_and_double_open_ended_slabs_are_rejected()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");

        void Bad(params PriceListSlab[] slabs) =>
            Assert.Throws<InvalidOperationException>(() =>
                kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, slabs));

        // Overlap: second slab starts before the first ends.
        Bad(new PriceListSlab(0m, 5m, Money.FromRupees(100m)), new PriceListSlab(3m, 10m, Money.FromRupees(90m)));
        // Gap: second slab starts after the first ends.
        Bad(new PriceListSlab(0m, 5m, Money.FromRupees(100m)), new PriceListSlab(6m, 10m, Money.FromRupees(90m)));
        // Descending.
        Bad(new PriceListSlab(5m, 10m, Money.FromRupees(90m)), new PriceListSlab(0m, 5m, Money.FromRupees(100m)));
        // Two open-ended (a non-last NULL To).
        Bad(new PriceListSlab(0m, null, Money.FromRupees(100m)), new PriceListSlab(5m, null, Money.FromRupees(90m)));
    }

    [Fact]
    public void Non_paisa_exact_rate_negative_rate_and_out_of_range_discount_are_rejected()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");

        // Sub-paisa rate.
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
                { new PriceListSlab(0m, null, new Money(100.005m)) }));
        // Discount ≥ 100.
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(100m), discountPercent: 100m) }));
        // Negative discount.
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(100m), discountPercent: -1m) }));
    }

    [Fact]
    public void Adding_a_list_for_a_missing_level_or_item_throws()
    {
        var kit = NewKit();
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(Guid.NewGuid(), kit.ItemId, Apr1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(100m)) }));

        var retail = kit.Service.CreateLevel("Retail");
        Assert.Throws<InvalidOperationException>(() =>
            kit.Service.AddOrReviseList(retail.Id, Guid.NewGuid(), Apr1, new[]
                { new PriceListSlab(0m, null, Money.FromRupees(100m)) }));
    }

    // ---------------------------------------------------------------- discount / effective rate (DP-A)

    [Fact]
    public void Effective_unit_rate_applies_discount_deterministically_to_the_paisa()
    {
        // rate 1,000 @ 10% == 900.00.
        var s1 = new PriceListSlab(0m, null, Money.FromRupees(1000m), discountPercent: 10m);
        Assert.Equal(Money.FromRupees(900m), s1.EffectiveUnitRate);

        // 333.33 @ 33.333% resolves to a FIXED paisa value under invariant-culture decimal math (no float drift).
        var s2 = new PriceListSlab(0m, null, new Money(333.33m), discountPercent: 33.333m);
        // 333.33 × (1 − 0.33333) = 222.2200011 → round-to-paisa (away from zero) = 222.22.
        Assert.Equal(new Money(222.22m), s2.EffectiveUnitRate);

        // The resolver surfaces Rate, DiscountPercent and EffectiveUnitRate together.
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(1000m), discountPercent: 10m) });
        var resolved = PriceResolver.Resolve(kit.Company, retail.Id, kit.ItemId, 5m, Apr1)!.Value;
        Assert.Equal(Money.FromRupees(1000m), resolved.Rate);
        Assert.Equal(10m, resolved.DiscountPercent);
        Assert.Equal(Money.FromRupees(900m), resolved.EffectiveUnitRate);
    }

    // ---------------------------------------------------------------- level uniqueness + delete guards (RQ-26)

    [Fact]
    public void Level_names_are_unique_case_insensitively()
    {
        var kit = NewKit();
        kit.Service.CreateLevel("Retail");
        Assert.Throws<InvalidOperationException>(() => kit.Service.CreateLevel("retail"));
        Assert.Throws<InvalidOperationException>(() => kit.Service.CreateLevel("  "));
    }

    [Fact]
    public void Deleting_a_level_referenced_by_a_list_or_a_ledger_default_is_blocked()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(16000m)) });

        // Referenced by a price list → blocked.
        Assert.Throws<InvalidOperationException>(() => kit.Service.DeleteLevel(retail.Id));

        // A level referenced only by a ledger default is also blocked.
        var wholesale = kit.Service.CreateLevel("Wholesale");
        var debtor = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "ACME", kit.Company.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, defaultPriceLevelId: wholesale.Id);
        kit.Company.AddLedger(debtor);
        Assert.Throws<InvalidOperationException>(() => kit.Service.DeleteLevel(wholesale.Id));

        // An unreferenced level deletes cleanly.
        var spare = kit.Service.CreateLevel("Spare");
        kit.Service.DeleteLevel(spare.Id);
        Assert.Null(kit.Company.FindPriceLevel(spare.Id));
    }

    // ---------------------------------------------------------------- report (RQ-31)

    [Fact]
    public void Price_list_report_lists_only_inventory_items_grouped_level_item_date_and_is_debranded()
    {
        var kit = NewKit();
        var retail = kit.Service.CreateLevel("Retail");
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Apr1, new[]
        {
            new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
            new PriceListSlab(2m, 4m, Money.FromRupees(14850m)),
        });
        kit.Service.AddOrReviseList(retail.Id, kit.ItemId, Jul1, new[]
            { new PriceListSlab(0m, null, Money.FromRupees(15000m)) });

        var report = PriceListReport.Build(kit.Company);
        var group = Assert.Single(report.Items);
        Assert.Equal("Retail", group.PriceLevelName);
        Assert.Equal("Laptop", group.StockItemName);

        // Two dated versions, newest applicable-from first.
        Assert.Equal(2, group.Versions.Count);
        Assert.Equal(Jul1, group.Versions[0].ApplicableFrom);
        Assert.Equal(Apr1, group.Versions[1].ApplicableFrom);
        Assert.Equal(2, group.Versions[1].Slabs.Count);
        Assert.Equal(Money.FromRupees(16000m), group.Versions[1].Slabs[0].Rate);

        // De-brand (ER-8): no "Tally" anywhere in the projected labels.
        Assert.DoesNotContain("Tally", group.PriceLevelName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", group.StockItemName, StringComparison.OrdinalIgnoreCase);
    }
}
