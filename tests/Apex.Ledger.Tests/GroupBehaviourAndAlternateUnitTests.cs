using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 2.2 (<b>Group behavioural flags</b>, with defect <b>T1-31</b>) and census 3.6 (<b>Alternate units per
/// stock item</b>) at the engine level — schema v60.
///
/// <para><b>R7 — every caption these tests name is vendor-verbatim.</b> The Group fields are
/// <c>help.tallysolutions.com/groups-in-tallyprime/</c>: <i>"Nature of Group"</i> (Assets / Liabilities /
/// Expenses / Income, shown only under <i>Primary</i>), <i>"Does it affect Gross Profits"</i>, <i>"Group behaves
/// like a sub-ledger"</i>, <i>"Nett Debit/Credit Balances for Reporting"</i>, <i>"Used for calculation (for
/// example: taxes, discounts)"</i>, <i>"Method to allocate when used in purchase invoice"</i> (Not Applicable /
/// Appropriate by Qty / Appropriate by Value). The Alternate Unit is
/// <c>help.tallysolutions.com/manage-stock-item-tally/</c>.</para>
///
/// <para>🔴 <b>WHAT T1-31 WAS, AND WHY IT IS TESTED FIRST.</b> A new PRIMARY accounting group could not be
/// created at all: <c>GroupService.CreateGroup</c> threw on a null parent, so the two vendor fields that appear
/// only on the primary-group screen were unreachable by construction —
/// <see cref="A_primary_group_could_not_be_created_at_all_before_v60"/> is the test that pins the fix, and
/// every "primary" test below is red on today's main for that reason alone.</para>
///
/// <para>🔴 <b>AND THE GROSS-PROFIT TESTS ARE THE WRONG-MONEY ONES.</b>
/// <see cref="A_custom_primary_head_marked_direct_lands_ABOVE_the_gross_profit_line"/> and
/// <see cref="The_four_seeded_trading_heads_are_untouched_by_the_new_flag"/> are a pair: the first proves the
/// flag is READ (not a dead setting), the second proves that reading it moved NOTHING on a book that does not
/// use it. Either one alone would be misleading.</para>
/// </summary>
public sealed class GroupBehaviourAndAlternateUnitTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewCompany() => CompanyFactory.CreateSeeded("Master Gaps Co", FyStart);

    // ============================================================ census 2.2 / T1-31 — primary groups

    /// <summary>
    /// T1-31, stated as the behaviour rather than as the internals: a primary group is creatable, its nature is
    /// the operator's own choice, and it really has no parent.
    /// </summary>
    [Fact]
    public void A_primary_group_could_not_be_created_at_all_before_v60()
    {
        var c = NewCompany();
        var service = new GroupService(c);

        var head = service.CreateGroup(
            "Trading Charges", parentId: null, alias: null, primaryNature: GroupNature.Expense);

        Assert.True(head.IsPrimary);
        Assert.Null(head.ParentId);
        Assert.Equal(GroupNature.Expense, head.Nature);
        Assert.False(head.IsPredefined);
        Assert.Same(head, c.FindGroupByName("Trading Charges"));
    }

    /// <summary>A primary group has no ancestry to derive a nature from, so one must be STATED. Defaulting it
    /// silently would put a whole new sub-tree on a side of the Balance Sheet nobody chose.</summary>
    [Fact]
    public void A_primary_group_without_a_nature_is_refused()
    {
        var c = NewCompany();
        var ex = Assert.Throws<InvalidOperationException>(
            () => new GroupService(c).CreateGroup("Headless", parentId: null));
        Assert.Contains("Nature of Group", ex.Message);
    }

    /// <summary>The vendor shows <i>Nature of Group</i> only under Primary; supplying one for a child would give
    /// the sub-tree two sources of truth for its Balance-Sheet side.</summary>
    [Fact]
    public void Supplying_a_nature_for_a_child_group_is_refused()
    {
        var c = NewCompany();
        var parent = c.FindGroupByName("Current Liabilities")!;
        var ex = Assert.Throws<InvalidOperationException>(
            () => new GroupService(c).CreateGroup("Salary Payable", parent.Id, null, GroupNature.Income));
        Assert.Contains("PRIMARY", ex.Message);
    }

    /// <summary>A child of a NEW primary head derives that head's nature, and the derivation cascades — which is
    /// what makes a custom head safe to introduce at all.</summary>
    [Fact]
    public void Children_of_a_new_primary_head_derive_its_nature()
    {
        var c = NewCompany();
        var service = new GroupService(c);
        var head = service.CreateGroup("Trading Charges", null, null, GroupNature.Expense);
        var child = service.CreateGroup("Freight Inward", head.Id);
        var grandchild = service.CreateGroup("Freight — Rail", child.Id);

        Assert.Equal(GroupNature.Expense, child.Nature);
        Assert.Equal(GroupNature.Expense, grandchild.Nature);
        Assert.Equal(GroupNature.Expense, ClassificationRules.PrimaryNatureOf(grandchild, c));
        Assert.True(ClassificationRules.IsProfitAndLossGroup(grandchild, c));
    }

    /// <summary>Altering a primary head's nature cascades to every descendant — without which a moved sub-tree
    /// keeps its old side of the statement, the silent misclassification this engine already guards at import.</summary>
    [Fact]
    public void Altering_a_primary_heads_nature_cascades_to_every_descendant()
    {
        var c = NewCompany();
        var service = new GroupService(c);
        var head = service.CreateGroup("Odd Head", null, null, GroupNature.Expense);
        var child = service.CreateGroup("Odd Child", head.Id);

        service.AlterGroup(head.Id, "Odd Head", parentId: null, alias: null, primaryNature: GroupNature.Income);

        Assert.Equal(GroupNature.Income, c.FindGroup(head.Id)!.Nature);
        Assert.Equal(GroupNature.Income, c.FindGroup(child.Id)!.Nature);
    }

    /// <summary>
    /// 🔴 A PREDEFINED primary head's nature cannot be changed. Flipping <i>Sales Accounts</i> from Income to
    /// Expense would cascade to every group and ledger beneath it and move the whole head to the other side of
    /// the statement — silently, because the book would still balance. The same reasoning already blocks
    /// renaming and re-parenting a predefined group.
    /// </summary>
    [Fact]
    public void A_predefined_primary_heads_nature_cannot_be_changed()
    {
        var c = NewCompany();
        var sales = c.FindGroupByName("Sales Accounts")!;
        var ex = Assert.Throws<InvalidOperationException>(() => new GroupService(c).AlterGroup(
            sales.Id, "Sales Accounts", parentId: null, alias: null, primaryNature: GroupNature.Expense));

        Assert.Contains("predefined group", ex.Message);
        Assert.Equal(GroupNature.Income, c.FindGroupByName("Sales Accounts")!.Nature);
    }

    /// <summary>... but its behavioural flags ARE alterable, which is what the vendor's own Group alteration
    /// screen offers on every group, predefined or not.</summary>
    [Fact]
    public void A_predefined_groups_behavioural_flags_are_still_alterable()
    {
        var c = NewCompany();
        var indirect = c.FindGroupByName("Indirect Expenses")!;
        new GroupService(c).AlterGroup(
            indirect.Id, "Indirect Expenses", parentId: null, alias: null,
            primaryNature: GroupNature.Expense,
            behaviour: new GroupBehaviour(NettBalancesForReporting: true));

        Assert.True(c.FindGroupByName("Indirect Expenses")!.NettBalancesForReporting);
        Assert.Equal(GroupNature.Expense, c.FindGroupByName("Indirect Expenses")!.Nature);
    }

    // ============================================================ census 2.2 — the five behavioural fields

    /// <summary>All five vendor fields are settable on a primary group and are stored verbatim.</summary>
    [Fact]
    public void The_five_behavioural_fields_are_stored_verbatim()
    {
        var c = NewCompany();
        var head = new GroupService(c).CreateGroup(
            "Trading Charges", null, null, GroupNature.Expense,
            new GroupBehaviour(true, true, true, true, MethodOfAppropriation.ByValue));

        Assert.True(head.BehavesLikeSubLedger);
        Assert.True(head.NettBalancesForReporting);
        Assert.True(head.UsedForCalculation);
        Assert.True(head.AffectsGrossProfits);
        Assert.Equal(MethodOfAppropriation.ByValue, head.PurchaseAllocationMethod);
    }

    /// <summary>The three non-primary-only flags and the allocation method are settable on a CHILD group too —
    /// the vendor shows them on every Group screen, unlike the two primary-only ones.</summary>
    [Fact]
    public void The_non_primary_flags_are_settable_on_a_child_group()
    {
        var c = NewCompany();
        var parent = c.FindGroupByName("Current Liabilities")!;
        var child = new GroupService(c).CreateGroup(
            "Salary Payable", parent.Id, null,
            behaviour: new GroupBehaviour(
                BehavesLikeSubLedger: true,
                NettBalancesForReporting: true,
                UsedForCalculation: true,
                PurchaseAllocationMethod: MethodOfAppropriation.ByQuantity));

        Assert.True(child.BehavesLikeSubLedger);
        Assert.True(child.NettBalancesForReporting);
        Assert.True(child.UsedForCalculation);
        Assert.Equal(MethodOfAppropriation.ByQuantity, child.PurchaseAllocationMethod);
        Assert.False(child.AffectsGrossProfits);
    }

    /// <summary>"Does it affect Gross Profits" on a CHILD is refused rather than stored: a child's placement is
    /// decided by its primary ancestor, so a value here would be written and never read.</summary>
    [Fact]
    public void Affects_gross_profits_is_refused_on_a_child_group()
    {
        var c = NewCompany();
        var parent = c.FindGroupByName("Current Liabilities")!;
        var ex = Assert.Throws<InvalidOperationException>(() => new GroupService(c).CreateGroup(
            "Salary Payable", parent.Id, null,
            behaviour: new GroupBehaviour(AffectsGrossProfits: true)));
        Assert.Contains("PRIMARY", ex.Message);
        Assert.Null(c.FindGroupByName("Salary Payable"));   // nothing was created
    }

    /// <summary>And refused on an Assets / Liabilities primary, where the Gross Profit computation never reaches
    /// it. (This guard is OURS — the vendor page names the field but not a nature restriction — and refusing is
    /// the smaller honest thing than accepting a setting that silently does nothing.)</summary>
    [Fact]
    public void Affects_gross_profits_is_refused_on_a_balance_sheet_primary()
    {
        var c = NewCompany();
        var ex = Assert.Throws<InvalidOperationException>(() => new GroupService(c).CreateGroup(
            "Odd Asset Head", null, null, GroupNature.Asset,
            new GroupBehaviour(AffectsGrossProfits: true)));
        Assert.Contains("Balance-Sheet head", ex.Message);
    }

    // ============================================================ census 2.2 — the wrong-money pair

    /// <summary>
    /// 🔴 The flag is genuinely READ. A ledger under a CUSTOM primary head marked <i>"Does it affect Gross
    /// Profits"</i> lands ABOVE the Gross Profit line; the identical head WITHOUT the flag does not.
    /// </summary>
    [Fact]
    public void A_custom_primary_head_marked_direct_lands_ABOVE_the_gross_profit_line()
    {
        var withFlag = GrossProfitWithCustomHead(affectsGrossProfits: true);
        var withoutFlag = GrossProfitWithCustomHead(affectsGrossProfits: false);

        // ₹5,000 of sales, ₹1,200 of the custom expense head.
        Assert.Equal(3800m, withFlag.Amount);     // the custom expense is a DIRECT cost
        Assert.Equal(5000m, withoutFlag.Amount);  // ... and an INDIRECT one when the flag is off
    }

    /// <summary>
    /// 🔴 The other half of the pair: turning the flag ON for each of the four SEEDED trading heads changes
    /// nothing at all, because those are matched by name and always were. This is what proves the census 2.2
    /// change moved no figure on any book that existed before it.
    /// </summary>
    [Fact]
    public void The_four_seeded_trading_heads_are_untouched_by_the_new_flag()
    {
        var baseline = TradingCompany(out var c);
        var before = ProfitAndLoss.Build(c, FyStart.AddYears(1).AddDays(-1)).GrossProfit;
        Assert.Equal(baseline, before.Amount);

        foreach (var name in new[] { "Sales Accounts", "Direct Incomes", "Purchase Accounts", "Direct Expenses" })
            c.FindGroupByName(name)!.AffectsGrossProfits = true;

        var after = ProfitAndLoss.Build(c, FyStart.AddYears(1).AddDays(-1)).GrossProfit;
        Assert.Equal(before.Amount, after.Amount);
    }

    /// <summary>Posts ₹5,000 of sales and ₹1,200 to a ledger under a custom primary Expense head, and returns
    /// the Gross Profit.</summary>
    private static Money GrossProfitWithCustomHead(bool affectsGrossProfits)
    {
        var c = NewCompany();
        var service = new GroupService(c);
        var head = service.CreateGroup(
            "Trading Charges", null, null, GroupNature.Expense,
            new GroupBehaviour(AffectsGrossProfits: affectsGrossProfits));

        var sales = AddLedger(c, "Sales", c.FindGroupByName("Sales Accounts")!.Id, openingIsDebit: false);
        var charge = AddLedger(c, "Clearing Charges", head.Id, openingIsDebit: true);
        var cash = c.Ledgers.First(l => l.Name.Equals("Cash", StringComparison.OrdinalIgnoreCase));

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var paymentType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
        var date = FyStart.AddDays(10);

        post.Post(new Voucher(Guid.NewGuid(), salesType, date, new List<EntryLine>
        {
            new(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));
        post.Post(new Voucher(Guid.NewGuid(), paymentType, date, new List<EntryLine>
        {
            new(charge.Id, Money.FromRupees(1200m), DrCr.Debit),
            new(cash.Id, Money.FromRupees(1200m), DrCr.Credit),
        }));

        return ProfitAndLoss.Build(c, FyStart.AddYears(1).AddDays(-1)).GrossProfit;
    }

    /// <summary>An ordinary trading book on the SEEDED heads only — ₹5,000 sales, ₹3,000 purchases.</summary>
    private static decimal TradingCompany(out Company company)
    {
        var c = NewCompany();
        var sales = AddLedger(c, "Sales", c.FindGroupByName("Sales Accounts")!.Id, openingIsDebit: false);
        var purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, openingIsDebit: true);
        var cash = c.Ledgers.First(l => l.Name.Equals("Cash", StringComparison.OrdinalIgnoreCase));

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var date = FyStart.AddDays(10);

        post.Post(new Voucher(Guid.NewGuid(), salesType, date, new List<EntryLine>
        {
            new(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));
        post.Post(new Voucher(Guid.NewGuid(), purchaseType, date, new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(3000m), DrCr.Debit),
            new(cash.Id, Money.FromRupees(3000m), DrCr.Credit),
        }));

        company = c;
        return 2000m;
    }

    private static Domain.Ledger AddLedger(Company c, string name, Guid groupId, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ============================================================ census 3.6 — alternate units

    /// <summary>The conversion runs BOTH ways from one stored factor, and the direction is the vendor's:
    /// the factor is base units per ONE alternate unit, so base → alternate divides.</summary>
    [Fact]
    public void The_conversion_runs_both_ways_from_one_factor()
    {
        var (c, item, _) = ItemWithAlternateUnit(factor: 10m);

        Assert.True(AlternateUnitConversion.HasAlternateUnit(item));
        Assert.Equal(2.5m, AlternateUnitConversion.ToAlternate(item, 25m));   // 25 Nos = 2.5 Box
        Assert.Equal(25m, AlternateUnitConversion.ToBase(item, 2.5m));        // 2.5 Box = 25 Nos
        Assert.Equal("1 Box = 10 Nos", AlternateUnitConversion.DescribeConversion(item, c));
    }

    /// <summary>An item with no alternate unit returns <c>null</c> rather than the base figure. A caller that
    /// got the base quantity back would print it under the alternate unit's symbol — a wrong quantity that
    /// looks entirely plausible.</summary>
    [Fact]
    public void An_item_without_an_alternate_unit_converts_to_null_not_to_the_base_figure()
    {
        var c = NewCompany();
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, nos.Id);

        Assert.False(AlternateUnitConversion.HasAlternateUnit(item));
        Assert.Null(AlternateUnitConversion.ToAlternate(item, 25m));
        Assert.Null(AlternateUnitConversion.ToBase(item, 25m));
        Assert.Null(AlternateUnitConversion.DescribeConversion(item, c));
    }

    /// <summary>Every half-state the choke point refuses, and each one is a wrong-quantity bug if it lands.</summary>
    [Fact]
    public void Every_half_set_alternate_unit_is_refused()
    {
        var c = NewCompany();
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var box = inv.CreateSimpleUnit("Box", "Boxes");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, nos.Id);

        // A unit with no factor.
        Assert.Contains("conversion factor",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, box.Id, null)).Message);
        // A factor with no unit.
        Assert.Contains("Alternate Unit",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, null, 10m)).Message);
        // Zero and negative factors.
        Assert.Contains("greater than zero",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, box.Id, 0m)).Message);
        Assert.Contains("greater than zero",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, box.Id, -3m)).Message);
        // The alternate unit equal to the base unit.
        Assert.Contains("cannot be the item's base unit",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, nos.Id, 2m)).Message);
        // An unknown unit.
        Assert.Contains("not found",
            Assert.Throws<InvalidOperationException>(() => inv.SetAlternateUnit(item, Guid.NewGuid(), 2m)).Message);

        // Nothing above left a partial state behind.
        Assert.Null(item.AlternateUnitId);
        Assert.Null(item.AlternateUnitConversion);
    }

    /// <summary>Clearing the alternate unit clears the factor WITH it, so an orphan factor cannot survive.</summary>
    [Fact]
    public void Clearing_the_alternate_unit_clears_the_factor_too()
    {
        var (_, item, inv) = ItemWithAlternateUnit(factor: 10m);
        inv.SetAlternateUnit(item, null, null);

        Assert.Null(item.AlternateUnitId);
        Assert.Null(item.AlternateUnitConversion);
        Assert.False(AlternateUnitConversion.HasAlternateUnit(item));
    }

    /// <summary>
    /// 🔴 <b>The stored quantity is the BASE one, and adding an alternate unit does not move a single figure.</b>
    /// This is the property the whole design rests on: an item's on-hand quantity and closing value are identical
    /// before and after an alternate unit is attached, because the alternate is derived and never stored.
    /// </summary>
    [Fact]
    public void Attaching_an_alternate_unit_moves_no_stored_quantity_or_value()
    {
        var c = NewCompany();
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var box = inv.CreateSimpleUnit("Box", "Boxes");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var godown = c.Godowns.First();
        var item = inv.CreateStockItem("Widget", group.Id, nos.Id);
        inv.AddOpeningBalance(item.Id, godown.Id, quantity: 120m, rate: Money.FromRupees(5m));

        var qtyBefore = c.StockOpeningBalances.Where(b => b.StockItemId == item.Id).Sum(b => b.Quantity);
        var valueBefore = inv.OpeningValueOf(item.Id);

        inv.SetAlternateUnit(item, box.Id, 10m);

        Assert.Equal(qtyBefore, c.StockOpeningBalances.Where(b => b.StockItemId == item.Id).Sum(b => b.Quantity));
        Assert.Equal(valueBefore, inv.OpeningValueOf(item.Id));
        // ... and the SAME 120 Nos simply reads as 12 Box when asked for.
        Assert.Equal(120m, qtyBefore);
        Assert.Equal(12m, AlternateUnitConversion.ToAlternate(item, qtyBefore));
    }

    private static (Company Company, StockItem Item, InventoryService Service) ItemWithAlternateUnit(decimal factor)
    {
        var c = NewCompany();
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var box = inv.CreateSimpleUnit("Box", "Boxes");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, nos.Id);
        inv.SetAlternateUnit(item, box.Id, factor);
        return (c, item, inv);
    }
}
