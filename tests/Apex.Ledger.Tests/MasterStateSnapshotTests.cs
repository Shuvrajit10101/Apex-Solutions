using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>WBB-2 — the rollback point itself.</b> <see cref="MasterStateSnapshot"/> is what makes a multi-row
/// master batch genuinely all-or-nothing: the batch is applied, and undone if any row is refused, because no
/// pre-validation pass can soundly predict the engine's cycle and same-category guards for a batch whose rows
/// judge each other.
///
/// <para>🔴 <b>THE TEST THAT MATTERS MOST HERE IS THE COMPLETENESS ONE</b>
/// (<see cref="Restore_puts_back_a_property_no_batch_path_writes"/>). The whole reason the snapshot is built by
/// reflection rather than by a hand-written field list is that a hand-written list is correct the day it is
/// written and silently incomplete a wave later — which is the exact failure class this project keeps hitting.
/// That test writes a property no master batch screen has ever touched and proves it comes back anyway, so the
/// guarantee is "every writable property", not "the five fields someone remembered".</para>
/// </summary>
public sealed class MasterStateSnapshotTests
{
    private static Company NewCompany()
    {
        var company = CompanyFactory.CreateSeeded("Snapshot Probe Co");
        return company;
    }

    [Fact]
    public void Restore_undoes_property_writes_on_an_existing_master()
    {
        var company = NewCompany();
        var group = company.Groups.First(g => !g.IsPredefined || g.ParentId is not null);
        var ledger = new DomainLedger(Guid.NewGuid(), "Probe Ltd", group.Id, new Money(1000m), true);
        company.AddLedger(ledger);

        var snapshot = MasterStateSnapshot.Capture(company);
        Assert.True(snapshot.MemberCount > 0, "the snapshot must actually have seen the book.");
        Assert.True(snapshot.PropertyValueCount > snapshot.MemberCount,
            "every master carries more than one writable property.");

        ledger.Name = "Mutated Ltd";
        ledger.OpeningBalance = new Money(7777m);
        ledger.OpeningIsDebit = false;

        snapshot.Restore();

        Assert.Equal("Probe Ltd", ledger.Name);
        Assert.Equal(new Money(1000m), ledger.OpeningBalance);
        Assert.True(ledger.OpeningIsDebit);
    }

    /// <summary>
    /// 🔴 Completeness by construction. <c>Ledger.PartyPan</c> is written by no multi-master batch path and is
    /// named nowhere in <see cref="MasterStateSnapshot"/>. It is restored because the snapshot discovers the
    /// type's writable properties at run time. If this file is ever "simplified" into an explicit field list,
    /// this test is what fails.
    /// </summary>
    [Fact]
    public void Restore_puts_back_a_property_no_batch_path_writes()
    {
        var company = NewCompany();
        var group = company.Groups.First(g => g.ParentId is not null);
        var ledger = new DomainLedger(Guid.NewGuid(), "Pan Probe Ltd", group.Id, Money.Zero, true)
        {
            PartyPan = "AAAAA1111A",
            Alias = "PP",
            MaintainBillByBill = true,
            DefaultCreditPeriodDays = 30,
        };
        company.AddLedger(ledger);

        var snapshot = MasterStateSnapshot.Capture(company);

        ledger.PartyPan = "ZZZZZ9999Z";
        ledger.Alias = "wrecked";
        ledger.MaintainBillByBill = false;
        ledger.DefaultCreditPeriodDays = null;

        snapshot.Restore();

        Assert.Equal("AAAAA1111A", ledger.PartyPan);
        Assert.Equal("PP", ledger.Alias);
        Assert.True(ledger.MaintainBillByBill);
        Assert.Equal(30, ledger.DefaultCreditPeriodDays);
    }

    [Fact]
    public void Restore_removes_masters_added_after_the_capture()
    {
        var company = NewCompany();
        var snapshot = MasterStateSnapshot.Capture(company);

        var ledgerCountBefore = company.Ledgers.Count;
        var groupCountBefore = company.Groups.Count;
        var godownCountBefore = company.Godowns.Count;
        var centreCountBefore = company.CostCentres.Count;

        var groups = new GroupService(company);
        var added = groups.CreateGroup("Added Group", company.Groups.First(g => g.ParentId is null).Id);
        company.AddLedger(new DomainLedger(Guid.NewGuid(), "Added Ltd", added.Id, Money.Zero, true));
        new InventoryService(company).CreateGodown("Added Store");
        var category = new CostCategory(Guid.NewGuid(), "Added Category");
        company.AddCostCategory(category);
        company.AddCostCentre(new CostCentre(Guid.NewGuid(), "Added Centre", category.Id));

        snapshot.Restore();

        Assert.Equal(ledgerCountBefore, company.Ledgers.Count);
        Assert.Equal(groupCountBefore, company.Groups.Count);
        Assert.Equal(godownCountBefore, company.Godowns.Count);
        Assert.Equal(centreCountBefore, company.CostCentres.Count);
        Assert.Null(company.FindLedgerByName("Added Ltd"));
        Assert.Null(company.FindGroupByName("Added Group"));
        Assert.Null(company.FindGodownByName("Added Store"));
    }

    [Fact]
    public void Restore_reinstates_a_master_removed_after_the_capture()
    {
        var company = NewCompany();
        var group = company.Groups.First(g => g.ParentId is not null);
        var ledger = new DomainLedger(Guid.NewGuid(), "Doomed Ltd", group.Id, new Money(42m), true);
        company.AddLedger(ledger);

        var snapshot = MasterStateSnapshot.Capture(company);
        var countBefore = company.Ledgers.Count;

        Assert.True(company.RemoveLedger(ledger));
        Assert.Null(company.FindLedger(ledger.Id));

        snapshot.Restore();

        Assert.Equal(countBefore, company.Ledgers.Count);
        var back = company.FindLedger(ledger.Id);
        Assert.NotNull(back);
        Assert.Equal("Doomed Ltd", back!.Name);
        Assert.Equal(new Money(42m), back.OpeningBalance);
    }

    /// <summary>
    /// The arm the multi-group batch depends on: <c>AlterGroup</c> ends in <c>RecomputeNatureFor</c>, which
    /// rewrites <c>Nature</c> on every descendant of the altered group. Those descendants are not rows of any
    /// batch, so a rollback that only put the written rows back would leave the book mis-classified.
    /// </summary>
    [Fact]
    public void Restore_undoes_the_group_nature_cascade_over_untouched_descendants()
    {
        var company = NewCompany();
        var groups = new GroupService(company);
        var debtors = company.Groups.Single(g => g.Name == "Sundry Debtors");
        var expenses = company.Groups.Single(g => g.Name == "Indirect Expenses");

        var parent = groups.CreateGroup("Cascade Parent", debtors.Id);
        var child = groups.CreateGroup("Cascade Child", parent.Id);
        var grandchild = groups.CreateGroup("Cascade Grandchild", child.Id);

        var natureBefore = parent.Nature;
        Assert.NotEqual(expenses.Nature, natureBefore);
        Assert.Equal(natureBefore, grandchild.Nature);

        var snapshot = MasterStateSnapshot.Capture(company);

        groups.AlterGroup(parent.Id, parent.Name, expenses.Id, parent.Alias);
        Assert.Equal(expenses.Nature, company.FindGroup(grandchild.Id)!.Nature);   // the cascade really fired

        snapshot.Restore();

        Assert.Equal(debtors.Id, company.FindGroup(parent.Id)!.ParentId);
        Assert.Equal(natureBefore, company.FindGroup(parent.Id)!.Nature);
        Assert.Equal(natureBefore, company.FindGroup(child.Id)!.Nature);
        Assert.Equal(natureBefore, company.FindGroup(grandchild.Id)!.Nature);
    }

    [Fact]
    public void Restore_covers_stock_items_godowns_and_cost_centres()
    {
        var company = NewCompany();
        var inventory = new InventoryService(company);
        var stockGroup = inventory.CreateStockGroup("Probe Stock Group");
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var item = inventory.CreateStockItem("Probe Item", stockGroup.Id, unit.Id);
        var godown = inventory.CreateGodown("Probe Store");
        var category = new CostCategory(Guid.NewGuid(), "Probe Category");
        company.AddCostCategory(category);
        var centre = new CostCentre(Guid.NewGuid(), "Probe Centre", category.Id, alias: "PC");
        company.AddCostCentre(centre);

        var snapshot = MasterStateSnapshot.Capture(company);

        item.Name = "Wrecked Item";
        item.HsnSacCode = "99999999";
        godown.Name = "Wrecked Store";
        godown.ThirdParty = true;
        centre.Name = "Wrecked Centre";
        centre.Alias = null;

        snapshot.Restore();

        Assert.Equal("Probe Item", item.Name);
        Assert.Null(item.HsnSacCode);
        Assert.Equal("Probe Store", godown.Name);
        Assert.False(godown.ThirdParty);
        Assert.Equal("Probe Centre", centre.Name);
        Assert.Equal("PC", centre.Alias);
    }

    /// <summary>Restoring twice is the same as restoring once, so a caller need not reason about whether an
    /// earlier arm already restored.</summary>
    [Fact]
    public void Restore_is_idempotent()
    {
        var company = NewCompany();
        var group = company.Groups.First(g => g.ParentId is not null);
        var ledger = new DomainLedger(Guid.NewGuid(), "Twice Ltd", group.Id, Money.Zero, true);
        company.AddLedger(ledger);

        var snapshot = MasterStateSnapshot.Capture(company);
        var countBefore = company.Ledgers.Count;

        ledger.Name = "Once";
        snapshot.Restore();
        snapshot.Restore();

        Assert.Equal("Twice Ltd", ledger.Name);
        Assert.Equal(countBefore, company.Ledgers.Count);
    }
}
