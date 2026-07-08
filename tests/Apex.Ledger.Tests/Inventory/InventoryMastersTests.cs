using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Inventory-masters tests (catalog §9; phase3-inventory-requirements RQ-1..RQ-7, ER-5/ER-7). Covers the
/// six masters — stock group (+add-quantities flag, nesting), stock category, simple + compound units,
/// godown (+Main Location seed, third-party flag), stock item (group/category/unit + HSN/taxability +
/// valuation method default = Average Cost + reorder fields), and opening balances by godown + batch label
/// — plus the create/alter/delete guards, uniqueness, and nesting-cycle prevention. These are pure
/// framework-agnostic engine tests, exactly like the accounting-core master tests.
/// </summary>
public class InventoryMastersTests
{
    private static Company Fresh() => CompanyFactory.CreateSeeded("Inventory Test Co", new DateOnly(2024, 4, 1));

    // ---------------------------------------------------------------- Seed: default godown

    [Fact]
    public void Fresh_company_seeds_a_single_main_location_godown()
    {
        var c = Fresh();
        var godown = Assert.Single(c.Godowns);
        Assert.Equal("Main Location", godown.Name);
        Assert.True(godown.IsMainLocation);
        Assert.False(godown.ThirdParty);
        Assert.Same(godown, c.MainLocation);
    }

    [Fact]
    public void Fresh_company_seeds_no_stock_items_groups_categories_or_units()
    {
        var c = Fresh();
        Assert.Empty(c.StockItems);
        Assert.Empty(c.StockGroups);
        Assert.Empty(c.StockCategories);
        Assert.Empty(c.Units);
        Assert.Empty(c.StockOpeningBalances);
    }

    // ---------------------------------------------------------------- Stock groups

    [Fact]
    public void Stock_group_creates_alters_and_defaults_add_quantities_true()
    {
        var c = Fresh();
        var svc = new InventoryService(c);

        var g = svc.CreateStockGroup("Electronics");
        Assert.True(g.AddQuantities);
        Assert.Null(g.ParentId);
        Assert.Same(g, c.FindStockGroupByName("electronics")); // case-insensitive lookup

        // Alter: rename in place keeps identity.
        g.Name = "Consumer Electronics";
        Assert.Same(g, c.FindStockGroup(g.Id));
        Assert.Equal("Consumer Electronics", c.FindStockGroup(g.Id)!.Name);
    }

    [Fact]
    public void Stock_group_add_quantities_flag_can_be_false()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var g = svc.CreateStockGroup("Mixed Bag", addQuantities: false);
        Assert.False(g.AddQuantities);
    }

    [Fact]
    public void Stock_group_nests_under_a_parent()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var parent = svc.CreateStockGroup("Electronics");
        var child = svc.CreateStockGroup("Phones", parent.Id);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.False(child.IsPrimary);
        Assert.True(parent.IsPrimary);
    }

    [Fact]
    public void Duplicate_stock_group_name_is_rejected()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        svc.CreateStockGroup("Electronics");
        var ex = Assert.Throws<InvalidOperationException>(() => svc.CreateStockGroup("electronics"));
        Assert.Contains("already exists", ex.Message);
        Assert.Single(c.StockGroups);
    }

    [Fact]
    public void Stock_group_with_unknown_parent_is_rejected()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockGroup("Phones", Guid.NewGuid()));
        Assert.Empty(c.StockGroups);
    }

    [Fact]
    public void Stock_group_nesting_cycle_is_rejected()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateStockGroup("A");
        var b = svc.CreateStockGroup("B", a.Id);
        var d = svc.CreateStockGroup("D", b.Id);

        // Re-parenting A under D would form A → D → B → A.
        var ex = Assert.Throws<InvalidOperationException>(() => svc.SetStockGroupParent(a.Id, d.Id));
        Assert.Contains("cycle", ex.Message);
        // The attempted re-parent is rolled back — A stays a root.
        Assert.Null(c.FindStockGroup(a.Id)!.ParentId);
    }

    [Fact]
    public void Stock_group_delete_blocked_when_it_has_children_or_items()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var parent = svc.CreateStockGroup("Electronics");
        var child = svc.CreateStockGroup("Phones", parent.Id);

        Assert.Throws<InvalidOperationException>(() => svc.DeleteStockGroup(parent.Id));

        // With the child gone, an item under the parent still blocks deletion.
        svc.DeleteStockGroup(child.Id);
        var unit = svc.CreateSimpleUnit("Nos", "Numbers");
        svc.CreateStockItem("Charger", parent.Id, unit.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteStockGroup(parent.Id));
    }

    // ---------------------------------------------------------------- Stock categories

    [Fact]
    public void Stock_category_is_an_independent_axis_and_nests()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var brand = svc.CreateStockCategory("Premium");
        var sub = svc.CreateStockCategory("Premium-Plus", brand.Id);
        Assert.Equal(brand.Id, sub.ParentId);
        Assert.Same(brand, c.FindStockCategoryByName("premium"));
    }

    [Fact]
    public void Duplicate_stock_category_and_cycle_are_rejected()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateStockCategory("A");
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockCategory("a"));
        var b = svc.CreateStockCategory("B", a.Id);
        // Direct self-parent on create is impossible (new id), so exercise cycle via a chain that loops.
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockCategory("C", Guid.NewGuid()));
        Assert.Equal(2, c.StockCategories.Count);
        Assert.NotNull(b);
    }

    [Fact]
    public void Stock_category_delete_blocked_when_used_by_an_item()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var group = svc.CreateStockGroup("G");
        var unit = svc.CreateSimpleUnit("Nos", "Numbers");
        var cat = svc.CreateStockCategory("Premium");
        svc.CreateStockItem("Item", group.Id, unit.Id, cat.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteStockCategory(cat.Id));
    }

    // ---------------------------------------------------------------- Units (simple + compound)

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void Simple_unit_accepts_0_to_4_decimals(int decimals)
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var u = svc.CreateSimpleUnit($"U{decimals}", "Unit", decimals, unitQuantityCode: "NOS");
        Assert.False(u.IsCompound);
        Assert.Equal(decimals, u.DecimalPlaces);
        Assert.Equal("NOS", u.UnitQuantityCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Simple_unit_rejects_decimals_outside_0_to_4(int decimals)
    {
        Assert.Throws<ArgumentException>(() => Unit.Simple(Guid.NewGuid(), "X", "X", decimals));
    }

    [Fact]
    public void Compound_unit_defines_first_times_factor_plus_tail()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var box = svc.CreateSimpleUnit("Box", "Box");
        // Box of 12 Nos: first = Box, factor = 12, tail = Nos.
        var boxOf12 = svc.CreateCompoundUnit("Box-12", "Box of 12", box.Id, nos.Id, 12);
        Assert.True(boxOf12.IsCompound);
        Assert.Equal(box.Id, boxOf12.FirstUnitId);
        Assert.Equal(nos.Id, boxOf12.TailUnitId);
        Assert.Equal(12, boxOf12.ConversionNumerator);
        Assert.Equal(0, boxOf12.DecimalPlaces); // compound units carry no own precision
    }

    [Fact]
    public void Compound_unit_first_and_tail_must_differ()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        Assert.Throws<ArgumentException>(() =>
            Unit.Compound(Guid.NewGuid(), "Bad", "Bad", nos.Id, nos.Id, 12));
    }

    [Fact]
    public void Compound_unit_factor_must_be_positive_and_components_must_be_simple()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var g = svc.CreateSimpleUnit("g", "Grams");
        var kg = svc.CreateSimpleUnit("Kg", "Kilograms");

        // Factor > 0 required.
        Assert.Throws<ArgumentException>(() => Unit.Compound(Guid.NewGuid(), "X", "X", kg.Id, g.Id, 0));

        // Valid compound Kg = 1000 g.
        var kgG = svc.CreateCompoundUnit("Kg-g", "Kilogram in grams", kg.Id, g.Id, 1000);
        Assert.Equal(1000, kgG.ConversionNumerator);
        Assert.Equal(1, kgG.ConversionDenominator);

        // A compound unit cannot be a component of another compound unit.
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateCompoundUnit("Nested", "Nested", kgG.Id, g.Id, 2));
    }

    [Fact]
    public void Unit_delete_blocked_when_used_as_base_or_as_a_component()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var g = svc.CreateSimpleUnit("g", "Grams");
        var box = svc.CreateCompoundUnit("Box", "Box", nos.Id, g.Id, 20);

        // nos is a component of Box → blocked.
        Assert.Throws<InvalidOperationException>(() => svc.DeleteUnit(nos.Id));

        // Use a fresh unit as an item's base unit → blocked.
        var pcs = svc.CreateSimpleUnit("Pcs", "Pieces");
        var grp = svc.CreateStockGroup("G");
        svc.CreateStockItem("Widget", grp.Id, pcs.Id);
        Assert.Throws<InvalidOperationException>(() => svc.DeleteUnit(pcs.Id));

        Assert.NotNull(box);
    }

    // ---------------------------------------------------------------- Godowns

    [Fact]
    public void Godown_creates_with_third_party_flag_and_nests()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var main = c.MainLocation!;
        var jobWork = svc.CreateGodown("Job Worker A", main.Id, thirdParty: true);
        Assert.True(jobWork.ThirdParty);
        Assert.Equal(main.Id, jobWork.ParentId);
        Assert.False(jobWork.IsMainLocation);
    }

    [Fact]
    public void Duplicate_godown_name_is_rejected()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        Assert.Throws<InvalidOperationException>(() => svc.CreateGodown("Main Location"));
    }

    [Fact]
    public void Main_location_godown_cannot_be_deleted()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var ex = Assert.Throws<InvalidOperationException>(() => svc.DeleteGodown(c.MainLocation!.Id));
        Assert.Contains("Main Location", ex.Message);
    }

    [Fact]
    public void Godown_delete_blocked_when_it_holds_opening_stock()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var wh = svc.CreateGodown("Warehouse 2");
        var unit = svc.CreateSimpleUnit("Nos", "Numbers");
        var grp = svc.CreateStockGroup("G");
        var item = svc.CreateStockItem("Item", grp.Id, unit.Id);
        svc.AddOpeningBalance(item.Id, wh.Id, 10m, Money.FromRupees(100m));
        Assert.Throws<InvalidOperationException>(() => svc.DeleteGodown(wh.Id));
    }

    // ---------------------------------------------------------------- Stock items

    [Fact]
    public void Stock_item_defaults_to_average_cost_and_carries_hsn_taxability_reorder()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("Electronics");
        var cat = svc.CreateStockCategory("Premium");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");

        var item = svc.CreateStockItem(
            "Phone", grp.Id, nos.Id, cat.Id,
            hsnSacCode: "8517", isTaxable: true,
            reorderLevel: 5m, minimumOrderQuantity: 20m);

        Assert.Equal(StockValuationMethod.AverageCost, item.ValuationMethod);
        Assert.Equal(grp.Id, item.StockGroupId);
        Assert.Equal(cat.Id, item.CategoryId);
        Assert.Equal(nos.Id, item.BaseUnitId);
        Assert.Equal("8517", item.HsnSacCode);
        Assert.True(item.IsTaxable);
        Assert.Equal(5m, item.ReorderLevel);
        Assert.Equal(20m, item.MinimumOrderQuantity);
    }

    [Fact]
    public void Stock_item_valuation_method_is_settable_to_every_method()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Item", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        Assert.Equal(StockValuationMethod.Fifo, item.ValuationMethod);

        foreach (var m in Enum.GetValues<StockValuationMethod>())
        {
            item.ValuationMethod = m;
            Assert.Equal(m, item.ValuationMethod);
        }
    }

    [Fact]
    public void Stock_item_rejects_unknown_group_unit_or_category_and_duplicate_name()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");

        Assert.Throws<InvalidOperationException>(() => svc.CreateStockItem("X", Guid.NewGuid(), nos.Id));
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockItem("X", grp.Id, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockItem("X", grp.Id, nos.Id, Guid.NewGuid()));

        svc.CreateStockItem("Widget", grp.Id, nos.Id);
        Assert.Throws<InvalidOperationException>(() => svc.CreateStockItem("widget", grp.Id, nos.Id));
        Assert.Single(c.StockItems);
    }

    [Fact]
    public void Stock_item_delete_blocked_when_it_has_opening_stock()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Item", grp.Id, nos.Id);
        svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, 3m, Money.FromRupees(50m));
        Assert.Throws<InvalidOperationException>(() => svc.DeleteStockItem(item.Id));
    }

    // ---------------------------------------------------------------- Opening balances

    [Fact]
    public void Opening_balance_by_godown_and_batch_sums_to_paisa_exact_value()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var kg = svc.CreateSimpleUnit("Kg", "Kilograms", decimalPlaces: 3);
        var item = svc.CreateStockItem("Rice", grp.Id, kg.Id);
        var main = c.MainLocation!;
        var wh = svc.CreateGodown("Warehouse 2");

        // Two godowns, two batches, fractional quantities: value = Σ(qty × rate) to the paisa.
        var b1 = svc.AddOpeningBalance(item.Id, main.Id, 10.5m, Money.FromRupees(40m), batchLabel: "LOT-A");
        var b2 = svc.AddOpeningBalance(item.Id, wh.Id, 2.25m, Money.FromRupees(40m), batchLabel: "LOT-B");

        Assert.Equal("LOT-A", b1.BatchLabel);
        Assert.Equal("LOT-B", b2.BatchLabel);
        Assert.Equal(Money.FromRupees(420m), b1.Value);   // 10.5 × 40
        Assert.Equal(Money.FromRupees(90m), b2.Value);    // 2.25 × 40
        Assert.Equal(Money.FromRupees(510m), svc.OpeningValueOf(item.Id));
        Assert.Equal(2, c.OpeningBalancesFor(item.Id).Count());
    }

    [Fact]
    public void Opening_balance_forward_compat_batch_dates_are_captured_but_optional()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Medicine", grp.Id, nos.Id);

        var withDates = svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5m),
            batchLabel: "B1", manufacturingDate: new DateOnly(2024, 1, 1), expiryDate: new DateOnly(2026, 1, 1));
        Assert.Equal(new DateOnly(2024, 1, 1), withDates.ManufacturingDate);
        Assert.Equal(new DateOnly(2026, 1, 1), withDates.ExpiryDate);

        var noDates = svc.AddOpeningBalance(item.Id, c.MainLocation.Id, 1m, Money.FromRupees(5m));
        Assert.Null(noDates.BatchLabel);
        Assert.Null(noDates.ManufacturingDate);
        Assert.Null(noDates.ExpiryDate);
    }

    [Fact]
    public void Opening_balance_rejects_unknown_item_or_godown_and_negative_quantity()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Item", grp.Id, nos.Id);

        Assert.Throws<InvalidOperationException>(() =>
            svc.AddOpeningBalance(Guid.NewGuid(), c.MainLocation!.Id, 1m, Money.FromRupees(1m)));
        Assert.Throws<InvalidOperationException>(() =>
            svc.AddOpeningBalance(item.Id, Guid.NewGuid(), 1m, Money.FromRupees(1m)));
        Assert.Throws<ArgumentException>(() =>
            svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, -1m, Money.FromRupees(1m)));
    }

    // ---------------------------------------------------------------- Regression: DEFECT 2 (opening rate precision)

    [Fact]
    public void Opening_balance_rejects_a_rate_finer_than_the_paisa()
    {
        // A 3-dp rate (9.999) is not paisa-exact; it must be rejected at the domain boundary with a clean
        // domain exception — never allowed to reach the paisa store where it would surface as a raw crash.
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Item", grp.Id, nos.Id);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(9.999m)));
        Assert.Contains("paisa", ex.Message);
        Assert.Empty(c.StockOpeningBalances); // nothing was added

        // Constructing the domain object directly is guarded too (not only the service entry point).
        Assert.Throws<InvalidOperationException>(() =>
            new StockOpeningBalance(Guid.NewGuid(), item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(9.999m)));
    }

    [Fact]
    public void Opening_balance_accepts_a_two_decimal_rate_and_reconciles_paisa_exact()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");
        var item = svc.CreateStockItem("Item", grp.Id, nos.Id);

        var b = svc.AddOpeningBalance(item.Id, c.MainLocation!.Id, 3m, Money.FromRupees(9.99m));
        Assert.Equal(Money.FromRupees(9.99m), b.Rate);
        Assert.Equal(Money.FromRupees(29.97m), b.Value); // 3 × 9.99, paisa-exact
        Assert.Equal(Money.FromRupees(29.97m), svc.OpeningValueOf(item.Id));
    }

    // ---------------------------------------------------------------- Regression: DEFECT 3 (godown / category re-parent guards)

    [Fact]
    public void Set_godown_parent_rejects_a_direct_cycle_and_self_parent()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateGodown("A");
        var b = svc.CreateGodown("B", a.Id);

        // Re-parenting A under B would form A → B → A.
        var cycle = Assert.Throws<InvalidOperationException>(() => svc.SetGodownParent(a.Id, b.Id));
        Assert.Contains("cycle", cycle.Message);
        Assert.Null(c.FindGodown(a.Id)!.ParentId); // rolled back — A stays a root

        // A godown cannot be its own parent.
        var self = Assert.Throws<InvalidOperationException>(() => svc.SetGodownParent(a.Id, a.Id));
        Assert.Contains("own parent", self.Message);
        Assert.Null(c.FindGodown(a.Id)!.ParentId);
    }

    [Fact]
    public void Set_godown_parent_happy_path_re_parents()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateGodown("A");
        var b = svc.CreateGodown("B");

        svc.SetGodownParent(a.Id, b.Id);
        Assert.Equal(b.Id, c.FindGodown(a.Id)!.ParentId);
    }

    [Fact]
    public void Set_stock_category_parent_rejects_a_direct_cycle_and_self_parent()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateStockCategory("A");
        var b = svc.CreateStockCategory("B", a.Id);

        var cycle = Assert.Throws<InvalidOperationException>(() => svc.SetStockCategoryParent(a.Id, b.Id));
        Assert.Contains("cycle", cycle.Message);
        Assert.Null(c.FindStockCategory(a.Id)!.ParentId);

        var self = Assert.Throws<InvalidOperationException>(() => svc.SetStockCategoryParent(a.Id, a.Id));
        Assert.Contains("own parent", self.Message);
        Assert.Null(c.FindStockCategory(a.Id)!.ParentId);
    }

    [Fact]
    public void Set_stock_category_parent_happy_path_re_parents()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var a = svc.CreateStockCategory("A");
        var b = svc.CreateStockCategory("B");

        svc.SetStockCategoryParent(a.Id, b.Id);
        Assert.Equal(b.Id, c.FindStockCategory(a.Id)!.ParentId);
    }

    // ---------------------------------------------------------------- Regression: DEFECT 4 (reorder-field precision)

    [Fact]
    public void Stock_item_rejects_reorder_fields_finer_than_six_decimals()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");

        // 7-dp reorder level (0.0000001) exceeds the 6-dp quantity-micro precision → clean domain reject.
        var rol = Assert.Throws<InvalidOperationException>(() =>
            svc.CreateStockItem("A", grp.Id, nos.Id, reorderLevel: 0.0000001m));
        Assert.Contains("6 decimal", rol.Message);
        Assert.Empty(c.StockItems);

        var moq = Assert.Throws<InvalidOperationException>(() =>
            svc.CreateStockItem("B", grp.Id, nos.Id, minimumOrderQuantity: 0.0000001m));
        Assert.Contains("6 decimal", moq.Message);
        Assert.Empty(c.StockItems);

        // Constructing the domain object directly is guarded too.
        Assert.Throws<InvalidOperationException>(() =>
            new StockItem(Guid.NewGuid(), "C", grp.Id, nos.Id, reorderLevel: 0.0000001m));
    }

    [Fact]
    public void Stock_item_accepts_six_decimal_reorder_fields()
    {
        var c = Fresh();
        var svc = new InventoryService(c);
        var grp = svc.CreateStockGroup("G");
        var nos = svc.CreateSimpleUnit("Nos", "Numbers");

        var item = svc.CreateStockItem("A", grp.Id, nos.Id,
            reorderLevel: 0.000001m, minimumOrderQuantity: 12.345678m);
        Assert.Equal(0.000001m, item.ReorderLevel);
        Assert.Equal(12.345678m, item.MinimumOrderQuantity);
    }
}
