using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>WAVE 29 / TRACK U1, CLUSTER C2 — THE SIX NEW DELETE GUARDS, AND THE REFUSAL IS THE SUBJECT.</b>
///
/// <para><b>Why a refusal is what gets tested here and a success is only the control.</b> Delete is the one
/// destructive verb these six masters gained, and the failure mode is not "the delete is rejected when it should
/// have worked" — it is the opposite. <c>SqliteCompanyStore</c> runs <c>PRAGMA foreign_keys = ON</c> and
/// <c>Save</c> is a delete-all + full re-insert, so a master that leaves the in-memory aggregate while a sibling
/// row still names it does not dangle quietly: the very next Save raises <c>SQLITE_CONSTRAINT_FOREIGNKEY</c>,
/// and so does every later save on the open company. The book on screen can never be written again by any
/// screen. Every <c>Assert.Throws</c> below is therefore a test that the operator's book survives.</para>
///
/// <para><b>🔴 THE FOUR TESTS THAT FAIL ON TODAY'S MAIN, NAMED SO A REVIEWER CAN CHECK THEM.</b> Before this
/// wave, <c>InventoryService.DeleteGodown</c> counted the default location, child godowns and opening balances
/// and NOTHING ELSE, and <c>DeleteUnit</c> counted <c>stock_items.base_unit_id</c> and the compound components
/// and nothing else. So:
/// <list type="bullet">
///   <item><see cref="A_godown_named_by_a_posted_invoice_line_is_refused"/> — the godown left memory and the
///     book became unsavable.</item>
///   <item><see cref="A_godown_named_by_a_batch_master_is_refused"/> — likewise.</item>
///   <item><see cref="A_unit_a_posted_invoice_line_is_measured_in_is_refused"/> — the quantity lost its
///     unit.</item>
///   <item><see cref="A_unit_used_as_a_stock_items_alternate_unit_is_refused"/> — the column arrived at census
///     3.6 / schema v60 and the guard was never revisited.</item>
/// </list>
/// The cost-master tests do not "fail on main" in the ordinary sense — there was no cost delete service at all,
/// so <see cref="CostMasterService"/> did not compile on main. That is a larger gap, not a smaller one.</para>
///
/// <para><b>FIDELITY (R7 / ruling 14 — the vendor's published documentation).</b> The godown conditions are
/// attested almost clause for clause at
/// <i>help.tallysolutions.com/tally-prime/inventory/inventory-storage-using-godowns-locations-tally/</i>: Alt+D
/// deletes, and it is refused unless the godown stores no stock items, was not used in any transaction and is
/// not a parent of other godowns — plus "You cannot delete the default godown". The cost conditions are attested
/// at <i>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</i>: a cost category is deletable "if no
/// Cost Centre or Profit Centre has been grouped under it", and a cost centre's allocated expenses must first be
/// moved "to another Cost Centre". The unit's compound clause is attested on the Units of Measure pages.
/// <b>Everything beyond those clauses — the exact counts, the breakdown wording, and the extra foreign-key
/// columns our schema declares — is OURS</b>, and is here because the schema demands it, not because a vendor
/// page said so. Nothing in this file may be re-labelled as fidelity to any other product.</para>
/// </summary>
public class MasterVerbsW29DeletionGuardTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly On = new(2024, 4, 10);

    private static Company Seed(string name = "W29 Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    /// <summary>A stock item on a fresh group + unit, so each guard test owns its own masters and no test can
    /// pass because a sibling happened to leave the company in the right shape.</summary>
    private static StockItem NewItem(Company c, string name, Guid groupId, Guid unitId, Guid? categoryId = null)
        => new InventoryService(c).CreateStockItem(name, groupId, unitId, categoryId);

    /// <summary>
    /// A stock group and a base unit to hang items off. <c>CompanyFactory.CreateSeeded</c> seeds NO inventory
    /// masters — no stock group and no unit — so a test that reached for <c>c.StockGroups.First()</c> would throw
    /// "Sequence contains no elements" before it ever exercised a guard, and would then be indistinguishable from
    /// a guard that failed to refuse. Creating them explicitly is what makes each test's subject the guard.
    /// </summary>
    private static (Guid GroupId, Guid UnitId) Basics(Company c)
    {
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        return (group.Id, unit.Id);
    }

    private static DomainLedger Expense(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Indirect Expenses")!.Id,
                                 Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger Income(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sales Accounts")!.Id,
                                 Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger Party(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
                                 Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts a real Sales item-invoice through <see cref="LedgerService"/>, so the inventory line under
    /// test is genuinely posted rather than hand-attached to a detached voucher.</summary>
    private static Voucher PostItemInvoice(
        Company c, StockItem item, Guid godownId, decimal rupees = 1000m, Guid? unitId = null)
    {
        var party = c.FindLedgerByName("W29 Buyer") ?? Party(c, "W29 Buyer");
        var sales = c.FindLedgerByName("W29 Sales") ?? Income(c, "W29 Sales");
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);

        return new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), type.Id, On,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
            },
            partyId: party.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, godownId, 1m, Money.FromRupees(rupees), unitId: unitId),
            }));
    }

    // ================================================================= census 3.7 — the GODOWN guard

    /// <summary>🔴 <b>FAILS ON TODAY'S MAIN.</b> The pre-wave service counted opening balances only, so a godown
    /// named by a POSTED invoice line was deletable and the next Save broke the book permanently.</summary>
    [Fact]
    public void A_godown_named_by_a_posted_invoice_line_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var site = inv.CreateGodown("Site A");
        var (groupId, unitId) = Basics(c);
        var item = NewItem(c, "Widget", groupId, unitId);
        PostItemInvoice(c, item, site.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteGodown(site.Id));
        Assert.Contains("Site A", ex.Message);
        Assert.Contains("invoice line", ex.Message);
        // The godown SURVIVES — a refusal that had already mutated the aggregate would be the very defect.
        Assert.Contains(c.Godowns, g => g.Id == site.Id);
    }

    /// <summary>🔴 <b>FAILS ON TODAY'S MAIN.</b> <c>batch_masters.godown_id</c> was one of the nine uncounted
    /// columns — a master that merely NAMES the godown, with no movement anywhere.</summary>
    [Fact]
    public void A_godown_named_by_a_batch_master_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var site = inv.CreateGodown("Site B");
        var (groupId, unitId) = Basics(c);
        var item = NewItem(c, "Tablet", groupId, unitId);
        c.AddBatchMaster(new BatchMaster(Guid.NewGuid(), item.Id, "LOT-1", godownId: site.Id));

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteGodown(site.Id));
        Assert.Contains("Site B", ex.Message);
        Assert.Contains("batch", ex.Message);
        Assert.Contains(c.Godowns, g => g.Id == site.Id);
    }

    /// <summary>The vendor's own "You cannot delete the default godown in TallyPrime", and structural for us —
    /// every inventory line falls back to it.</summary>
    [Fact]
    public void The_default_location_is_never_deletable()
    {
        var c = Seed();
        var ex = Assert.Throws<InvalidOperationException>(() => new InventoryService(c).DeleteGodown(c.MainLocation!.Id));
        Assert.Contains("default", ex.Message);
        Assert.NotNull(c.MainLocation);
    }

    /// <summary>The vendor's "is not a parent of other godowns".</summary>
    [Fact]
    public void A_parent_godown_is_refused_while_a_sub_godown_remains()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var parent = inv.CreateGodown("North");
        inv.CreateGodown("North-1", parentId: parent.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteGodown(parent.Id));
        Assert.Contains("sub-godown", ex.Message);
        Assert.Contains(c.Godowns, g => g.Id == parent.Id);
    }

    /// <summary>The CONTROL. An unreferenced godown really does go — without it every assertion above could pass
    /// against a guard that simply refused everything.</summary>
    [Fact]
    public void An_unreferenced_godown_deletes()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var spare = inv.CreateGodown("Spare Yard");

        inv.DeleteGodown(spare.Id);

        Assert.DoesNotContain(c.Godowns, g => g.Id == spare.Id);
    }

    /// <summary>
    /// 🔴 <b>THE SIX GODOWN COLUMNS THAT HAD CORRECT CODE AND NO PROOF.</b> Each of
    /// <c>inventory_allocations.godown_id</c> (both sides), <c>order_lines.godown_id</c>,
    /// <c>physical_stock_lines.godown_id</c>, <c>bom_lines.godown_id</c>,
    /// <c>pos_voucher_type_config.default_godown_id</c>, <c>job_work_orders.fg_godown_id</c> and
    /// <c>job_work_order_lines.godown_id</c> is counted by <c>EnsureGodownDeletable</c> — and every one of them
    /// was, until this test, provable-by-nothing.
    ///
    /// <para><b>Measured, not assumed.</b> Replacing each of those tallies with a literal <c>0</c> — one
    /// mutation per column — left the ENTIRE suite green, on every project. <c>MasterDeletionForeignKeyCoverageTests</c>
    /// does not close this: it proves the <i>list</i> of guarded columns matches the schema's declared foreign
    /// keys, which is a different claim from "the guard actually counts each one". A column can sit in
    /// <see cref="MasterDeletionRules.GuardedForeignKeyColumns"/>, satisfy that lock, and be counted by nothing.
    /// That gap is what this test closes.</para>
    ///
    /// <para>Written as one test per column rather than a loop so a failure names the column that regressed.</para>
    /// </summary>
    [Theory]
    [InlineData("inventory_allocations.godown_id", "inventory-voucher line")]
    [InlineData("order_lines.godown_id", "inventory-voucher line")]
    [InlineData("physical_stock_lines.godown_id", "inventory-voucher line")]
    [InlineData("bom_lines.godown_id", "bill-of-materials line")]
    [InlineData("pos_voucher_type_config.default_godown_id", "POS default location")]
    [InlineData("job_work_orders.fg_godown_id", "job-work order")]
    [InlineData("job_work_order_lines.godown_id", "job-work component line")]
    public void Every_remaining_godown_foreign_key_refuses_the_delete(string column, string expectedWording)
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var site = inv.CreateGodown("Held Yard");
        var (groupId, unitId) = Basics(c);
        var item = NewItem(c, $"Item for {column}", groupId, unitId);

        switch (column)
        {
            case "inventory_allocations.godown_id":
            {
                var other = inv.CreateGodown("Other Yard");
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal);
                c.AddInventoryVoucher(InventoryVoucher.StockJournal(
                    Guid.NewGuid(), type.Id, On,
                    source: new[] { new InventoryAllocation(item.Id, site.Id, 2m, StockDirection.Outward) },
                    destination: new[] { new InventoryAllocation(item.Id, other.Id, 2m, StockDirection.Inward) }));
                break;
            }
            case "order_lines.godown_id":
            {
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PurchaseOrder);
                c.AddInventoryVoucher(InventoryVoucher.Order(
                    Guid.NewGuid(), type.Id, On,
                    new[] { new OrderLine(item.Id, site.Id, 5m, Money.FromRupees(100m)) }));
                break;
            }
            case "physical_stock_lines.godown_id":
            {
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);
                c.AddInventoryVoucher(InventoryVoucher.PhysicalStock(
                    Guid.NewGuid(), type.Id, On,
                    new[] { new PhysicalStockLine(item.Id, site.Id, 7m, batchLabel: null) }));
                break;
            }
            case "bom_lines.godown_id":
            {
                var finished = NewItem(c, "Finished Good", groupId, unitId);
                c.AddBillOfMaterials(new BillOfMaterials(
                    Guid.NewGuid(), finished.Id, "BOM-1", 1m,
                    new[] { new BomLine(BomLineType.Component, item.Id, 4m, godownId: site.Id) }));
                break;
            }
            case "pos_voucher_type_config.default_godown_id":
            {
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
                type.PosConfig = new PosConfig { DefaultGodownId = site.Id };
                break;
            }
            case "job_work_orders.fg_godown_id":
            {
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
                c.AddInventoryVoucher(InventoryVoucher.JobWork(
                    Guid.NewGuid(), type.Id, On,
                    new JobWorkOrder(
                        JobWorkDirection.Out, "JW-1", item.Id, 10m,
                        new[] { new JobWorkOrderLine(item.Id, JobWorkComponentTrack.PendingToIssue, 3m) },
                        finishedGoodGodownId: site.Id)));
                break;
            }
            case "job_work_order_lines.godown_id":
            {
                var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
                c.AddInventoryVoucher(InventoryVoucher.JobWork(
                    Guid.NewGuid(), type.Id, On,
                    new JobWorkOrder(
                        JobWorkDirection.Out, "JW-2", item.Id, 10m,
                        new[] { new JobWorkOrderLine(item.Id, JobWorkComponentTrack.PendingToIssue, 3m, godownId: site.Id) })));
                break;
            }
            default:
                Assert.Fail($"Unhandled column '{column}' — add its arrangement rather than letting it pass.");
                return;
        }

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteGodown(site.Id));
        Assert.Contains("Held Yard", ex.Message);
        Assert.Contains(expectedWording, ex.Message);
        Assert.Contains(c.Godowns, g => g.Id == site.Id);
    }

    // ================================================================= census 3.5 — the UNIT guard

    /// <summary>🔴 <b>FAILS ON TODAY'S MAIN.</b> <c>voucher_inventory_lines.unit_id</c> was never counted, so a
    /// unit a posted line's QUANTITY and RATE are both expressed in could be deleted out from under it.</summary>
    [Fact]
    public void A_unit_a_posted_invoice_line_is_measured_in_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var (groupId, baseUnitId) = Basics(c);
        // 🔴 THE UNIT UNDER TEST IS REFERENCED BY THE LINE AND BY NOTHING ELSE, deliberately, so that only the
        // line-level column can be what refuses. VoucherValidator requires a line's unit to reduce to the item's
        // base unit, so it has to be a COMPOUND whose tail is that base unit ("Dz-Nos" = 12 Nos). "Dz-Nos" is
        // itself no item's base or alternate unit, and — being the compound rather than a component — it is not
        // caught by the vendor's compound clause either (that counts units whose First/Tail IS the subject).
        var dz = inv.CreateSimpleUnit("Dz", "Dozen");
        var dozenOfNos = inv.CreateCompoundUnit("Dz-Nos", "Dozen of Numbers", dz.Id, baseUnitId, 12);
        var item = NewItem(c, "Oil", groupId, baseUnitId);
        PostItemInvoice(c, item, c.MainLocation!.Id, unitId: dozenOfNos.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteUnit(dozenOfNos.Id));
        Assert.Contains("Dz-Nos", ex.Message);
        Assert.Contains("measured", ex.Message);
        Assert.Contains(c.Units, u => u.Id == dozenOfNos.Id);
    }

    /// <summary>🔴 <b>FAILS ON TODAY'S MAIN.</b> <c>stock_items.alternate_unit_id</c> arrived at census 3.6
    /// (schema v60) and the delete guard was never revisited when it landed.</summary>
    [Fact]
    public void A_unit_used_as_a_stock_items_alternate_unit_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var (groupId, baseUnitId) = Basics(c);
        var box = inv.CreateSimpleUnit("Box", "Box");
        var item = NewItem(c, "Bolt", groupId, baseUnitId);
        item.AlternateUnitId = box.Id;

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteUnit(box.Id));
        Assert.Contains("Box", ex.Message);
        Assert.Contains("alternate unit", ex.Message);
        Assert.Contains(c.Units, u => u.Id == box.Id);
    }

    /// <summary>The vendor's attested compound clause — a unit that is part of a compound measure.</summary>
    [Fact]
    public void A_component_of_a_compound_unit_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var (_, nosId) = Basics(c);
        var gross = inv.CreateSimpleUnit("Gr", "Gross");
        inv.CreateCompoundUnit("Gr-Nos", "Gross of Nos", gross.Id, nosId, 144);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteUnit(gross.Id));
        Assert.Contains("compound unit", ex.Message);
        Assert.Contains(c.Units, u => u.Id == gross.Id);
    }

    /// <summary>The CONTROL for the unit guard.</summary>
    /// <summary>
    /// 🔴 <c>inventory_allocations.unit_id</c> — the last unit column with no proof behind it. Zeroing this tally
    /// left the whole suite green, exactly as the seven godown columns above did.
    ///
    /// <para><b>The unit under test is deliberately NOT any item's base unit.</b> If it were,
    /// <c>stock_items.base_unit_id</c> would answer first and this test would pass without the allocation tally
    /// ever being consulted — the same wrong-reason trap that made the cost-category assertion vacuous.</para>
    /// </summary>
    [Fact]
    public void A_unit_a_stock_journal_allocation_is_measured_in_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var (groupId, baseUnitId) = Basics(c);
        var item = NewItem(c, "Cable", groupId, baseUnitId);

        // A SECOND unit, used only on the allocation — never as a base or alternate unit of any item.
        var box = inv.CreateSimpleUnit("Box", "Boxes");
        Assert.DoesNotContain(c.StockItems, i => i.BaseUnitId == box.Id || i.AlternateUnitId == box.Id);

        var from = inv.CreateGodown("From Yard");
        var to = inv.CreateGodown("To Yard");
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.StockJournal);
        c.AddInventoryVoucher(InventoryVoucher.StockJournal(
            Guid.NewGuid(), type.Id, On,
            source: new[] { new InventoryAllocation(item.Id, from.Id, 2m, StockDirection.Outward, unitId: box.Id) },
            destination: new[] { new InventoryAllocation(item.Id, to.Id, 2m, StockDirection.Inward, unitId: box.Id) }));

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteUnit(box.Id));
        Assert.Contains("Box", ex.Message);
        Assert.Contains("inventory-voucher line", ex.Message);
        Assert.Contains(c.Units, u => u.Id == box.Id);
    }

    [Fact]
    public void An_unreferenced_unit_deletes()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var spare = inv.CreateSimpleUnit("Spr", "Spare");

        inv.DeleteUnit(spare.Id);

        Assert.DoesNotContain(c.Units, u => u.Id == spare.Id);
    }

    // ============================================== census 3.1 / 3.2 — STOCK GROUP and STOCK CATEGORY

    [Fact]
    public void A_stock_group_with_an_item_filed_under_it_is_refused_and_deletes_once_the_item_moves()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var group = inv.CreateStockGroup("Fasteners");
        var other = inv.CreateStockGroup("Sundries");
        var (_, unitId) = Basics(c);
        var item = NewItem(c, "Rivet", group.Id, unitId);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteStockGroup(group.Id));
        Assert.Contains("Fasteners", ex.Message);
        Assert.Contains("stock item", ex.Message);

        // …and the remedy the message NAMES actually works. A refusal that cannot be cleared is a dead end.
        item.StockGroupId = other.Id;
        inv.DeleteStockGroup(group.Id);
        Assert.DoesNotContain(c.StockGroups, g => g.Id == group.Id);
    }

    [Fact]
    public void A_stock_group_with_a_sub_group_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var parent = inv.CreateStockGroup("Hardware");
        inv.CreateStockGroup("Hardware-Bolts", parentId: parent.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteStockGroup(parent.Id));
        Assert.Contains("sub-group", ex.Message);
        Assert.Contains(c.StockGroups, g => g.Id == parent.Id);
    }

    [Fact]
    public void A_stock_category_with_an_item_filed_under_it_is_refused()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var category = inv.CreateStockCategory("Imported");
        var (groupId, unitId) = Basics(c);
        NewItem(c, "Gasket", groupId, unitId, categoryId: category.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => inv.DeleteStockCategory(category.Id));
        Assert.Contains("Imported", ex.Message);
        Assert.Contains("stock item", ex.Message);
        Assert.Contains(c.StockCategories, x => x.Id == category.Id);
    }

    [Fact]
    public void An_unreferenced_stock_category_deletes()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var category = inv.CreateStockCategory("Unused");

        inv.DeleteStockCategory(category.Id);

        Assert.DoesNotContain(c.StockCategories, x => x.Id == category.Id);
    }

    // ================================================================= census 2.7 — the COST CATEGORY guard

    /// <summary>The vendor's own stated condition: deletable "if no Cost Centre or Profit Centre has been grouped
    /// under it".</summary>
    [Fact]
    public void A_cost_category_with_a_centre_grouped_under_it_is_refused()
    {
        var c = Seed();
        var category = new CostCategory(Guid.NewGuid(), "Departments");
        c.AddCostCategory(category);
        c.AddCostCentre(new CostCentre(Guid.NewGuid(), "Sales Dept", category.Id));

        var ex = Assert.Throws<InvalidOperationException>(() => new CostMasterService(c).DeleteCostCategory(category.Id));
        Assert.Contains("Departments", ex.Message);
        Assert.Contains("cost centre", ex.Message);
        Assert.Contains(c.CostCategories, x => x.Id == category.Id);
    }

    /// <summary>The seeded Primary Cost Category is the fallback every centre sits under.</summary>
    [Fact]
    public void The_predefined_cost_category_is_never_deletable()
    {
        var c = Seed();
        var primary = c.FindCostCategoryByName("Primary Cost Category")!;

        var ex = Assert.Throws<InvalidOperationException>(() => new CostMasterService(c).DeleteCostCategory(primary.Id));
        Assert.Contains("predefined", ex.Message);
        Assert.Contains(c.CostCategories, x => x.Id == primary.Id);
    }

    [Fact]
    public void An_empty_user_cost_category_deletes()
    {
        var c = Seed();
        var category = new CostCategory(Guid.NewGuid(), "Projects");
        c.AddCostCategory(category);

        new CostMasterService(c).DeleteCostCategory(category.Id);

        Assert.DoesNotContain(c.CostCategories, x => x.Id == category.Id);
    }

    // ================================================================= census 2.8 — the COST CENTRE guard

    /// <summary>🔴 THE TRANSACTION REFUSAL, and the vendor's own remedy: move the allocations "to another Cost
    /// Centre and then delete it". The allocation is POSTED through the real engine.</summary>
    [Fact]
    public void A_cost_centre_carrying_a_posted_allocation_is_refused()
    {
        var c = Seed();
        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var delhi = new CostCentre(Guid.NewGuid(), "Delhi", category.Id);
        c.AddCostCentre(delhi);

        var salaries = Expense(c, "Salaries");
        var cash = c.FindLedgerByName("Cash")!;
        var journal = c.FindVoucherTypeByName("Journal")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, On, new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(9000m), DrCr.Debit, billAllocations: null,
                          costAllocations: new[] { new CostAllocation(category.Id, delhi.Id, Money.FromRupees(9000m)) }),
            new EntryLine(cash.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var ex = Assert.Throws<InvalidOperationException>(() => new CostMasterService(c).DeleteCostCentre(delhi.Id));
        Assert.Contains("Delhi", ex.Message);
        Assert.Contains("allocate", ex.Message);
        Assert.Contains(c.CostCentres, x => x.Id == delhi.Id);
    }

    /// <summary>
    /// 🔴 <c>cost_allocations.category_id</c> — <b>THE COST CATEGORY'S OWN TRANSACTION REFUSAL, ON A CATEGORY THAT
    /// IS NOT THE PREDEFINED ONE.</b>
    ///
    /// <para><b>Why the "not predefined" half is the whole point of this test.</b> A sibling test used to assert
    /// this refusal using <c>Primary Cost Category</c>, and it passed — but for the WRONG REASON: that category is
    /// <see cref="CostCategory.IsPredefined"/>, so <c>EnsureCostCategoryDeletable</c> threw on its FIRST guard and
    /// never reached the allocation count at all. Measured by mutation: replacing the whole
    /// <c>cost_allocations.category_id</c> tally with <c>0</c> left the entire suite GREEN. The allocation guard
    /// was correct code with no proof behind it, which is exactly how it would later be "simplified" away.</para>
    ///
    /// <para>What that costs if it regresses: the category leaves the aggregate while a POSTED voucher line still
    /// carries its Guid in <c>cost_allocations.category_id</c>, so the next Save raises
    /// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> and the open book can never be written again — with the operator's
    /// posted cost analysis orphaned on the way out.</para>
    /// </summary>
    [Fact]
    public void A_cost_category_carrying_a_posted_allocation_is_refused()
    {
        var c = Seed();
        // NOT the predefined category — otherwise the IsPredefined guard answers first and this proves nothing.
        var category = new CostCategory(Guid.NewGuid(), "Departments");
        c.AddCostCategory(category);
        Assert.False(category.IsPredefined);

        var centre = new CostCentre(Guid.NewGuid(), "Delhi", category.Id);
        c.AddCostCentre(centre);

        var salaries = Expense(c, "Salaries");
        var cash = c.FindLedgerByName("Cash")!;
        var journal = c.FindVoucherTypeByName("Journal")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, On, new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(9000m), DrCr.Debit, billAllocations: null,
                          costAllocations: new[] { new CostAllocation(category.Id, centre.Id, Money.FromRupees(9000m)) }),
            new EntryLine(cash.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        // Remove the centre from the picture so the "cost centre grouped under it" guard cannot be what fires.
        // The POSTED allocation is then the only thing standing between this category and deletion.
        c.RemoveCostCentre(centre);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new CostMasterService(c).DeleteCostCategory(category.Id));
        Assert.Contains("Departments", ex.Message);
        Assert.Contains("allocate", ex.Message);
        Assert.DoesNotContain("cost centre", ex.Message);
        Assert.Contains(c.CostCategories, x => x.Id == category.Id);
    }

    [Fact]
    public void A_cost_centre_with_a_sub_centre_is_refused()
    {
        var c = Seed();
        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var parent = new CostCentre(Guid.NewGuid(), "West", category.Id);
        c.AddCostCentre(parent);
        c.AddCostCentre(new CostCentre(Guid.NewGuid(), "West-Pune", category.Id, parentId: parent.Id));

        var ex = Assert.Throws<InvalidOperationException>(() => new CostMasterService(c).DeleteCostCentre(parent.Id));
        Assert.Contains("sub-centre", ex.Message);
        Assert.Contains(c.CostCentres, x => x.Id == parent.Id);
    }

    /// <summary><c>godowns.job_cost_centre_id</c> (census 9.6) — a godown designated as this centre's job. This
    /// column is OURS, not a vendor clause, and it is counted because the schema declares it.</summary>
    [Fact]
    public void A_cost_centre_a_godown_is_designated_to_is_refused()
    {
        var c = Seed();
        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var job = new CostCentre(Guid.NewGuid(), "Bridge Project", category.Id);
        c.AddCostCentre(job);
        new InventoryService(c).CreateGodown("Bridge Site", jobCostCentreId: job.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => new CostMasterService(c).DeleteCostCentre(job.Id));
        Assert.Contains("Bridge Project", ex.Message);
        Assert.Contains("job", ex.Message);
        Assert.Contains(c.CostCentres, x => x.Id == job.Id);
    }

    [Fact]
    public void An_unreferenced_cost_centre_deletes()
    {
        var c = Seed();
        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var spare = new CostCentre(Guid.NewGuid(), "Spare Centre", category.Id);
        c.AddCostCentre(spare);

        new CostMasterService(c).DeleteCostCentre(spare.Id);

        Assert.DoesNotContain(c.CostCentres, x => x.Id == spare.Id);
    }

    // ================================================================= the stale-id case

    /// <summary>
    /// 🔴 <b>A DELETED MASTER CANNOT BE RESURRECTED BY A STALE ID.</b> The shell's list rows carry a Guid, and a
    /// row rendered before a delete still holds the id of a master that is now gone. Every one of these six
    /// delete verbs must therefore REFUSE a second call on the same id rather than silently succeeding (which
    /// would let a double Alt+D report success twice) or throwing something that is not an operator-readable
    /// refusal. Each service re-resolves the id and raises "not found".
    /// </summary>
    [Fact]
    public void Deleting_through_a_stale_id_is_refused_on_every_one_of_the_six_masters()
    {
        var c = Seed();
        var inv = new InventoryService(c);
        var cost = new CostMasterService(c);
        var category = c.FindCostCategoryByName("Primary Cost Category")!;

        var godown = inv.CreateGodown("Gone Yard");
        var unit = inv.CreateSimpleUnit("Gnu", "Gone Unit");
        var group = inv.CreateStockGroup("Gone Group");
        var stockCategory = inv.CreateStockCategory("Gone Category");
        var costCategory = new CostCategory(Guid.NewGuid(), "Gone Cost Category");
        c.AddCostCategory(costCategory);
        var centre = new CostCentre(Guid.NewGuid(), "Gone Centre", category.Id);
        c.AddCostCentre(centre);

        inv.DeleteGodown(godown.Id);
        inv.DeleteUnit(unit.Id);
        inv.DeleteStockGroup(group.Id);
        inv.DeleteStockCategory(stockCategory.Id);
        cost.DeleteCostCategory(costCategory.Id);
        cost.DeleteCostCentre(centre.Id);

        Assert.Throws<InvalidOperationException>(() => inv.DeleteGodown(godown.Id));
        Assert.Throws<InvalidOperationException>(() => inv.DeleteUnit(unit.Id));
        Assert.Throws<InvalidOperationException>(() => inv.DeleteStockGroup(group.Id));
        Assert.Throws<InvalidOperationException>(() => inv.DeleteStockCategory(stockCategory.Id));
        Assert.Throws<InvalidOperationException>(() => cost.DeleteCostCategory(costCategory.Id));
        Assert.Throws<InvalidOperationException>(() => cost.DeleteCostCentre(centre.Id));
    }
}
