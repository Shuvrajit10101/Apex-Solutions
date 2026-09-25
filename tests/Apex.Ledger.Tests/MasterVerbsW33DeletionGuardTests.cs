using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>WAVE 33 / CLUSTER C3 — THE ENGINE HALF OF THE EIGHT RESIDUAL MASTER-DELETE VERBS.</b>
/// Census 2.9, 2.10, 2.11, 3.8, 3.9, 3.10, 3.11, 3.12.
///
/// <para><b>Why the refusals are the subject and the successes are the control.</b> Same reasoning as
/// <see cref="MasterVerbsW29DeletionGuardTests"/>, restated because it is the whole point of this file:
/// <c>SqliteCompanyStore</c> runs <c>PRAGMA foreign_keys = ON</c> and <c>Save</c> is a delete-all + full
/// re-insert, so a master that leaves the in-memory aggregate while a sibling row still names it does not
/// dangle quietly — the very next Save raises <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> and so does every later save
/// on the open company. Each <c>Assert.Throws</c> below is a test that the operator's book survives.</para>
///
/// <para>🔴 <b>THE TEST THAT FAILS ON TODAY'S <c>main</c>, NAMED SO A REVIEWER CAN CHECK IT WITHOUT READING THE
/// WHOLE FILE:</b> <see cref="A_BOM_a_job_work_order_was_filled_from_is_refused"/>. On main,
/// <c>BomService.DeleteBom</c> has <b>no referential guard of any kind</b> — it removes the BOM and returns —
/// while <c>job_work_orders.fill_components_bom_id</c> is a real <c>REFERENCES bill_of_materials(id)</c> column
/// written from <c>Company.InventoryVouchers</c>, a collection that knows nothing about whether the BOM
/// survived. The omission was UNREACHABLE on main only because <c>DeleteBom</c> had <b>zero production
/// callers</b>; wave 33 wires Alt+D onto the BOM master, and that keystroke is what would have made it
/// reachable. The other new guard, <see cref="MasterDeletionRules.EnsureCurrencyDeletable"/>, did not exist at
/// all on main, so every currency test here fails to compile there — a larger gap, not a smaller one.</para>
///
/// <para><b>FIDELITY (R7 / ruling 14).</b> 🔴 <b>NO VENDOR CLAIM IS MADE ANYWHERE IN THIS FILE, and that is a
/// deliberate choice rather than an omission.</b> The vendor's published documentation was not consulted for a
/// BOM, currency, budget, scenario, price-list-version or reorder-definition deletion rule, so none is cited;
/// this campaign has already produced fabricated vendor claims, and a plausible-looking URL is worse than an
/// honest "ours". What IS borrowed is the attested <i>shape</i> the corpus gives for a ledger — a master
/// carrying transactions cannot be deleted, and the refusal names the remedy — extended by us to masters the
/// sources do not discuss. Every message string, every count and the base-currency refusal are <b>OURS,
/// unverified by design</b>.</para>
///
/// <para><b>And the ABSENCES are asserted too.</b> Budget, Scenario, Price List and Reorder Levels get no guard,
/// because the DDL says each one's only inbound foreign key is a child row written from the parent's own object
/// graph — or there is none at all. <see cref="A_budget_with_lines_deletes_because_its_lines_are_its_own"/> and
/// its siblings pin that as a measured decision, so a later reader finds a test rather than a silence and a
/// future reviewer cannot mistake the absence for an oversight.</para>
/// </summary>
public class MasterVerbsW33DeletionGuardTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly On = new(2024, 4, 10);

    private static Company Seed(string name = "W33 Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    /// <summary>A stock group and a base unit to hang items off — <c>CompanyFactory.CreateSeeded</c> seeds no
    /// inventory masters, so a test reaching for <c>StockGroups.First()</c> would throw before it exercised a
    /// guard and be indistinguishable from a guard that failed to refuse.</summary>
    private static (Guid GroupId, Guid UnitId) Basics(Company c)
    {
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        return (group.Id, unit.Id);
    }

    private static StockItem NewItem(Company c, string name)
    {
        var (groupId, unitId) = Basics(c);
        return new InventoryService(c).CreateStockItem(name, groupId, unitId);
    }

    // ========================================================== census 3.9 — BILL OF MATERIALS

    /// <summary>
    /// 🔴 <b>THE TEST THAT FAILS ON <c>main</c>.</b> A job-work order filled from a BOM holds that BOM's Guid in
    /// <c>job_work_orders.fill_components_bom_id</c>. Deleting the BOM must be refused, by name and with the
    /// count, or the open company can never be saved again.
    /// </summary>
    [Fact]
    public void A_BOM_a_job_work_order_was_filled_from_is_refused()
    {
        var c = Seed();
        var finished = NewItem(c, "Cabinet");
        var part = NewItem(c, "Hinge");

        var bom = new BomService(c).CreateBom(
            finished.Id, "Cabinet Standard", 1m,
            new[] { new BomLine(BomLineType.Component, part.Id, 4m) });

        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
        c.AddInventoryVoucher(InventoryVoucher.JobWork(
            Guid.NewGuid(), type.Id, On,
            new JobWorkOrder(
                JobWorkDirection.Out, "JW-1", finished.Id, 10m,
                new[] { new JobWorkOrderLine(part.Id, JobWorkComponentTrack.PendingToIssue, 40m) },
                fillComponentsBomId: bom.Id)));

        var ex = Assert.Throws<InvalidOperationException>(() => new BomService(c).DeleteBom(bom.Id));

        // The MASTER is named, the COUNT is stated, and the remedy is real — the three things an operator needs.
        Assert.Contains("Cabinet Standard", ex.Message);
        Assert.Contains("1 job-work order was filled from it", ex.Message);
        Assert.Contains("Delete those orders first", ex.Message);

        // 🔴 THE ASSERTION THAT MAKES THIS ABOUT THE BOOK RATHER THAN ABOUT A STRING: nothing left memory.
        Assert.Contains(c.BillsOfMaterials, b => b.Id == bom.Id);
    }

    /// <summary>Two orders filled from the same BOM read as "2 job-work orders were filled from it" — the
    /// plural is spelled rather than concatenated, because a refusal read under pressure must not say
    /// "1 orders".</summary>
    [Fact]
    public void The_BOM_refusal_counts_every_order_and_spells_the_plural()
    {
        var c = Seed();
        var finished = NewItem(c, "Cabinet");
        var part = NewItem(c, "Hinge");
        var bom = new BomService(c).CreateBom(
            finished.Id, "Cabinet Standard", 1m,
            new[] { new BomLine(BomLineType.Component, part.Id, 4m) });

        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
        foreach (var no in new[] { "JW-1", "JW-2" })
            c.AddInventoryVoucher(InventoryVoucher.JobWork(
                Guid.NewGuid(), type.Id, On,
                new JobWorkOrder(
                    JobWorkDirection.Out, no, finished.Id, 10m,
                    new[] { new JobWorkOrderLine(part.Id, JobWorkComponentTrack.PendingToIssue, 40m) },
                    fillComponentsBomId: bom.Id)));

        var ex = Assert.Throws<InvalidOperationException>(() => new BomService(c).DeleteBom(bom.Id));
        Assert.Contains("2 job-work orders were filled from it", ex.Message);
    }

    /// <summary>
    /// The control, and it is load-bearing: a job-work order that was filled MANUALLY carries
    /// <c>FillComponentsBomId == null</c>, so it must NOT block an unrelated BOM. A guard that counted every
    /// job-work order would pass every refusal test above and make the BOM master undeletable in any company
    /// that does job work at all.
    /// </summary>
    [Fact]
    public void A_manually_filled_job_work_order_does_not_block_an_unrelated_BOM()
    {
        var c = Seed();
        var finished = NewItem(c, "Cabinet");
        var part = NewItem(c, "Hinge");
        var bom = new BomService(c).CreateBom(
            finished.Id, "Cabinet Standard", 1m,
            new[] { new BomLine(BomLineType.Component, part.Id, 4m) });

        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
        c.AddInventoryVoucher(InventoryVoucher.JobWork(
            Guid.NewGuid(), type.Id, On,
            new JobWorkOrder(
                JobWorkDirection.Out, "JW-MANUAL", finished.Id, 10m,
                new[] { new JobWorkOrderLine(part.Id, JobWorkComponentTrack.PendingToIssue, 40m) })));

        new BomService(c).DeleteBom(bom.Id);
        Assert.DoesNotContain(c.BillsOfMaterials, b => b.Id == bom.Id);
    }

    /// <summary>The guard runs BEFORE anything is removed, so a refused delete is never half-applied — the
    /// finished good is still flagged as a manufactured item afterwards.</summary>
    [Fact]
    public void A_refused_BOM_delete_leaves_the_finished_goods_components_flag_alone()
    {
        var c = Seed();
        var finished = NewItem(c, "Cabinet");
        var part = NewItem(c, "Hinge");
        var bom = new BomService(c).CreateBom(
            finished.Id, "Cabinet Standard", 1m,
            new[] { new BomLine(BomLineType.Component, part.Id, 4m) });
        Assert.True(c.FindStockItem(finished.Id)!.SetComponents);

        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
        c.AddInventoryVoucher(InventoryVoucher.JobWork(
            Guid.NewGuid(), type.Id, On,
            new JobWorkOrder(
                JobWorkDirection.Out, "JW-1", finished.Id, 10m,
                new[] { new JobWorkOrderLine(part.Id, JobWorkComponentTrack.PendingToIssue, 40m) },
                fillComponentsBomId: bom.Id)));

        Assert.Throws<InvalidOperationException>(() => new BomService(c).DeleteBom(bom.Id));
        Assert.True(c.FindStockItem(finished.Id)!.SetComponents);
    }

    // ========================================================== census 2.11 — CURRENCY

    private static Currency Foreign(Company c, string symbol, string formalName)
    {
        var cur = new Currency(Guid.NewGuid(), symbol, formalName);
        c.AddCurrency(cur);
        return cur;
    }

    /// <summary>The base currency is refused outright — not with a count, because the reason is not that
    /// something points at it but that the whole book is denominated in it.</summary>
    [Fact]
    public void The_base_currency_is_refused_outright()
    {
        var c = Seed();
        var basec = c.BaseCurrency;
        Assert.NotNull(basec);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureCurrencyDeletable(c, basec!));
        Assert.Contains("base currency", ex.Message);
        Assert.Contains("cannot be removed at all", ex.Message);
    }

    /// <summary>A currency a posted entry line was entered in is refused with the count and the attested
    /// remedy — the extension of the corpus's own ledger rule to a master it does not name.</summary>
    [Fact]
    public void A_currency_a_posted_entry_line_is_entered_in_is_refused()
    {
        var c = Seed();
        var usd = Foreign(c, "$", "USD");

        var party = new DomainLedger(Guid.NewGuid(), "W33 Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                     Money.Zero, openingIsDebit: true);
        var sales = new DomainLedger(Guid.NewGuid(), "W33 Sales", c.FindGroupByName("Sales Accounts")!.Id,
                                     Money.Zero, openingIsDebit: false);
        c.AddLedger(party);
        c.AddLedger(sales);

        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), type.Id, On,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(8300m), DrCr.Debit,
                              forex: new ForexInfo(usd.Id, Money.FromRupees(100m), 83m)),
                new EntryLine(sales.Id, Money.FromRupees(8300m), DrCr.Credit),
            }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureCurrencyDeletable(c, usd));
        Assert.Contains("USD", ex.Message);
        Assert.Contains("1 posted entry line is entered in it", ex.Message);
        Assert.Contains("Delete those transactions first", ex.Message);
        Assert.Contains(c.Currencies, x => x.Id == usd.Id);
    }

    /// <summary>A currency a LEDGER is denominated in is refused — <c>ledgers.currency_id</c>. A ledger left
    /// pointing at a deleted currency is the unsavable-company case, so the wording is the shared
    /// "other masters and settings name it" one rather than the transaction one.</summary>
    [Fact]
    public void A_currency_a_ledger_is_denominated_in_is_refused()
    {
        var c = Seed();
        var usd = Foreign(c, "$", "USD");
        var party = new DomainLedger(Guid.NewGuid(), "US Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                     Money.Zero, openingIsDebit: true) { CurrencyId = usd.Id };
        c.AddLedger(party);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureCurrencyDeletable(c, usd));
        Assert.Contains("USD", ex.Message);
        Assert.Contains("1 ledger denominated in it", ex.Message);
        Assert.Contains("could not be saved again", ex.Message);
        Assert.Contains(c.Currencies, x => x.Id == usd.Id);
    }

    /// <summary>A currency with a dated rate-of-exchange quote is refused — <c>exchange_rates.currency_id</c>.
    /// This is the clause most easily forgotten, because the rates list sits on the SAME screen as the
    /// currencies list and reads like part of the same record; it is a top-level row with its own foreign
    /// key.</summary>
    [Fact]
    public void A_currency_with_a_rate_of_exchange_quote_is_refused()
    {
        var c = Seed();
        var usd = Foreign(c, "$", "USD");
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, On, 83m));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureCurrencyDeletable(c, usd));
        Assert.Contains("USD", ex.Message);
        Assert.Contains("1 rate-of-exchange quote", ex.Message);
        Assert.Contains(c.Currencies, x => x.Id == usd.Id);
    }

    /// <summary>Both non-transaction clauses at once are reported TOGETHER, so an operator who clears one is
    /// not sent back to discover the other. A guard that returned on the first hit would pass the two tests
    /// above and fail here.</summary>
    [Fact]
    public void The_currency_refusal_reports_every_blocking_category_at_once()
    {
        var c = Seed();
        var usd = Foreign(c, "$", "USD");
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "US Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                     Money.Zero, openingIsDebit: true) { CurrencyId = usd.Id });
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, On, 83m));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MasterDeletionRules.EnsureCurrencyDeletable(c, usd));
        Assert.Contains("1 ledger denominated in it", ex.Message);
        Assert.Contains("1 rate-of-exchange quote", ex.Message);
    }

    /// <summary>The control: an unreferenced foreign currency is accepted, so the guard is a rule and not a
    /// blanket refusal.</summary>
    [Fact]
    public void An_unreferenced_foreign_currency_is_accepted()
    {
        var c = Seed();
        var eur = Foreign(c, "€", "EUR");
        MasterDeletionRules.EnsureCurrencyDeletable(c, eur);   // does not throw
    }

    /// <summary>A SECOND currency's ledger must not block the first — the guard filters by id, and a guard that
    /// counted every forex ledger would pass every refusal test in this file while making the master
    /// undeletable in any multi-currency company.</summary>
    [Fact]
    public void Another_currencys_ledger_does_not_block_this_one()
    {
        var c = Seed();
        var usd = Foreign(c, "$", "USD");
        var eur = Foreign(c, "€", "EUR");
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "US Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                     Money.Zero, openingIsDebit: true) { CurrencyId = usd.Id });

        MasterDeletionRules.EnsureCurrencyDeletable(c, eur);   // does not throw
    }

    // ========================================================== the MEASURED ABSENCES
    //
    // 🔴 These four tests assert that a delete is PERMITTED. They exist because "there is no guard" is a claim
    // about the schema that a reader deserves to be able to falsify, and because a future slice that adds a
    // refusal to one of these masters should have to come here and change a test that says why.

    /// <summary>census 2.9 — a budget's LINES are written from <c>Budget.Lines</c>, so they leave with it and
    /// no <c>budget_lines.budget_id</c> row is ever orphaned. Nothing else in the schema points at a budget.</summary>
    [Fact]
    public void A_budget_with_lines_deletes_because_its_lines_are_its_own()
    {
        var c = Seed();
        var cash = c.FindLedgerByName("Cash")
                   ?? c.Ledgers.First();
        var budget = new Budget(Guid.NewGuid(), "FY Budget", FyStart, FyStart.AddYears(1),
            lines: new[] { BudgetLine.ForLedger(cash.Id, BudgetType.OnClosingBalance,
                                                Money.FromRupees(1000m)) });
        c.AddBudget(budget);

        Assert.True(c.RemoveBudget(budget));
        Assert.DoesNotContain(c.Budgets, b => b.Id == budget.Id);
    }

    /// <summary>census 2.10 — a scenario points AT voucher types; nothing points at a scenario. Its
    /// <c>scenario_voucher_types</c> rows are written from <c>Scenario.IncludedTypeIds</c>.</summary>
    [Fact]
    public void A_scenario_that_includes_voucher_types_deletes()
    {
        var c = Seed();
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal);
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includedTypeIds: new[] { type.Id });
        c.AddScenario(scenario);

        Assert.True(c.RemoveScenario(scenario));
        Assert.DoesNotContain(c.Scenarios, s => s.Id == scenario.Id);
        // The voucher type is untouched — the reference runs the other way.
        Assert.Contains(c.VoucherTypes, t => t.Id == type.Id);
    }

    /// <summary>census 3.11 — one dated version is withdrawn and the PRECEDING version survives, which is the
    /// whole behaviour: the history is append-only, and this is the only way to take an entry back out.</summary>
    [Fact]
    public void One_price_list_version_is_withdrawn_and_the_earlier_one_survives()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new PriceListService(c);
        var level = svc.CreateLevel("Wholesale");

        var v1 = svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 4, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) });
        var v2 = svc.AddOrReviseList(level.Id, item.Id, new DateOnly(2024, 7, 1),
            new[] { new PriceListSlab(0m, null, Money.FromRupees(110m)) });

        svc.DeleteList(v2.Id);

        Assert.DoesNotContain(c.PriceLists, pl => pl.Id == v2.Id);
        Assert.Contains(c.PriceLists, pl => pl.Id == v1.Id);
    }

    /// <summary>census 3.12 — nothing in the schema references <c>reorder_definitions(id)</c>, so a definition
    /// deletes unconditionally and the stock item it scoped is untouched.</summary>
    [Fact]
    public void A_reorder_definition_deletes_and_leaves_its_item_alone()
    {
        var c = Seed();
        var item = NewItem(c, "Widget");
        var svc = new ReorderLevelsService(c);
        var def = svc.CreateOrUpdate(ReorderScope.Item, item.Id, reorderQuantity: 25m);

        svc.Delete(def.Id);

        Assert.DoesNotContain(c.ReorderDefinitions, d => d.Id == def.Id);
        Assert.NotNull(c.FindStockItem(item.Id));
    }

    /// <summary>An unknown id is a named failure rather than a silent no-op, on every one of the four services
    /// this slice reaches — the shell turns the throw into a notice, and a silent return would leave the
    /// operator believing a delete happened.</summary>
    [Fact]
    public void Deleting_an_unknown_id_throws_rather_than_silently_doing_nothing()
    {
        var c = Seed();
        var missing = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => new BomService(c).DeleteBom(missing));
        Assert.Throws<InvalidOperationException>(() => new BatchService(c).DeleteBatch(missing));
        Assert.Throws<InvalidOperationException>(() => new PriceListService(c).DeleteList(missing));
        Assert.Throws<InvalidOperationException>(() => new PriceListService(c).DeleteLevel(missing));
        Assert.Throws<InvalidOperationException>(() => new ReorderLevelsService(c).Delete(missing));
    }
}
