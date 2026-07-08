using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Reorder-Level master + service tests (Phase 6 slice 6; requirements RQ-32..RQ-35). Covers the
/// <see cref="ReorderDefinition"/> constructor validation (negative / over-precision quantities rejected; an
/// Advanced figure requires a positive period + a Higher/Lower criterion), the deterministic rolling-window
/// arithmetic (<see cref="ReorderDefinition.WindowStart"/>), and the <see cref="ReorderLevelsService"/> CRUD:
/// create per Item/Group/Category, target-not-found throws, upsert replaces on (scope, target), delete, and
/// no company mutation on a validation failure. Pure, deterministic — like the accounting core.
/// </summary>
public class ReorderLevelsTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static (Company Company, InventoryService Masters, ReorderLevelsService Svc) NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Reorder Co", FyStart);
        var masters = new InventoryService(c);
        return (c, masters, new ReorderLevelsService(c));
    }

    private static Guid NewItem(InventoryService masters)
    {
        var grp = masters.CreateStockGroup($"G-{Guid.NewGuid():N}");
        var unit = masters.CreateSimpleUnit($"U{Guid.NewGuid():N}".Substring(0, 6), "Numbers");
        return masters.CreateStockItem($"I-{Guid.NewGuid():N}", grp.Id, unit.Id).Id;
    }

    // ---------------------------------------------------------------- ReorderDefinition validation

    [Fact]
    public void Definition_rejects_a_negative_quantity()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReorderDefinition(Guid.NewGuid(), ReorderScope.Item, Guid.NewGuid(), reorderQuantity: -1m));
    }

    [Fact]
    public void Definition_rejects_a_quantity_finer_than_six_decimal_places()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ReorderDefinition(Guid.NewGuid(), ReorderScope.Item, Guid.NewGuid(), minOrderQuantity: 1.1234567m));
    }

    [Fact]
    public void Definition_rejects_advanced_without_a_period_or_criterion()
    {
        var target = Guid.NewGuid();
        // Advanced reorder level but no period.
        Assert.Throws<InvalidOperationException>(() =>
            new ReorderDefinition(Guid.NewGuid(), ReorderScope.Item, target,
                reorderAdvanced: true, reorderQuantity: 10m, criteria: ReorderCriteria.Higher));
        // Advanced min qty but no criterion.
        Assert.Throws<InvalidOperationException>(() =>
            new ReorderDefinition(Guid.NewGuid(), ReorderScope.Item, target,
                minQtyAdvanced: true, minOrderQuantity: 10m, periodCount: 2, periodUnit: ExpiryPeriodUnit.Weeks));
        // Non-positive period count.
        Assert.Throws<InvalidOperationException>(() =>
            new ReorderDefinition(Guid.NewGuid(), ReorderScope.Item, target,
                reorderAdvanced: true, periodCount: 0, periodUnit: ExpiryPeriodUnit.Days, criteria: ReorderCriteria.Lower));
    }

    [Fact]
    public void Definition_window_start_uses_calendar_arithmetic()
    {
        var asOf = new DateOnly(2024, 3, 15);
        ReorderDefinition Def(int count, ExpiryPeriodUnit unit) =>
            new(Guid.NewGuid(), ReorderScope.Item, Guid.NewGuid(),
                reorderAdvanced: true, periodCount: count, periodUnit: unit, criteria: ReorderCriteria.Higher);

        Assert.Equal(new DateOnly(2024, 3, 5), Def(10, ExpiryPeriodUnit.Days).WindowStart(asOf));
        Assert.Equal(new DateOnly(2024, 3, 1), Def(2, ExpiryPeriodUnit.Weeks).WindowStart(asOf));
        Assert.Equal(new DateOnly(2024, 1, 15), Def(2, ExpiryPeriodUnit.Months).WindowStart(asOf));
        Assert.Equal(new DateOnly(2023, 3, 15), Def(1, ExpiryPeriodUnit.Years).WindowStart(asOf));
    }

    // ---------------------------------------------------------------- service CRUD

    [Fact]
    public void Service_creates_definitions_for_each_scope()
    {
        var (c, masters, svc) = NewKit();
        var group = masters.CreateStockGroup("Widgets");
        var category = masters.CreateStockCategory("Premium");
        var unit = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Gizmo", group.Id, unit.Id, categoryId: category.Id).Id;

        svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 5m);
        svc.CreateOrUpdate(ReorderScope.Group, group.Id, reorderQuantity: 10m);
        svc.CreateOrUpdate(ReorderScope.Category, category.Id, reorderQuantity: 15m);

        Assert.Equal(3, c.ReorderDefinitions.Count);
        Assert.Equal(5m, c.FindReorderDefinition(ReorderScope.Item, item)!.ReorderQuantity);
        Assert.Equal(10m, c.FindReorderDefinition(ReorderScope.Group, group.Id)!.ReorderQuantity);
        Assert.Equal(15m, c.FindReorderDefinition(ReorderScope.Category, category.Id)!.ReorderQuantity);
    }

    [Fact]
    public void Service_throws_when_the_target_does_not_exist()
    {
        var (c, _, svc) = NewKit();
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateOrUpdate(ReorderScope.Item, Guid.NewGuid(), reorderQuantity: 5m));
        Assert.Empty(c.ReorderDefinitions);
    }

    [Fact]
    public void Service_upserts_the_definition_for_a_scope_target()
    {
        var (c, masters, svc) = NewKit();
        var item = NewItem(masters);
        var first = svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 5m);
        var second = svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 20m, minOrderQuantity: 30m);

        var def = Assert.Single(c.ReorderDefinitions);   // replaced, not duplicated
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(20m, def.ReorderQuantity);
        Assert.Equal(30m, def.MinOrderQuantity);
    }

    [Fact]
    public void Service_delete_removes_the_definition()
    {
        var (c, masters, svc) = NewKit();
        var item = NewItem(masters);
        var def = svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 5m);
        svc.Delete(def.Id);
        Assert.Empty(c.ReorderDefinitions);
        Assert.Throws<InvalidOperationException>(() => svc.Delete(def.Id));
    }

    [Fact]
    public void Service_does_not_mutate_the_company_on_a_validation_failure()
    {
        var (c, masters, svc) = NewKit();
        var item = NewItem(masters);
        svc.CreateOrUpdate(ReorderScope.Item, item, reorderQuantity: 5m);

        // An Advanced upsert missing its period must throw and leave the ORIGINAL definition intact (ER-12).
        Assert.Throws<InvalidOperationException>(() =>
            svc.CreateOrUpdate(ReorderScope.Item, item, reorderAdvanced: true, reorderQuantity: 99m,
                criteria: ReorderCriteria.Higher));

        var def = Assert.Single(c.ReorderDefinitions);
        Assert.Equal(5m, def.ReorderQuantity);   // unchanged
        Assert.False(def.ReorderAdvanced);
    }
}
