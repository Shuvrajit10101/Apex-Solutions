using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.6 — THE TWO-CONVERSIONS DEFECT, FOUND BY MEASUREMENT 2026-10-23 AND FIXED AT THE CHOKE
/// POINT.</b>
///
/// <para><b>WHAT ROW 3.6 CLAIMS, AND THE HOLE IN IT.</b> The row records its design decision as load-bearing:
/// <i>"the base unit is stored and the alternate is ALWAYS derived … so no stock table gained a quantity column,
/// no second quantity exists, and 'a report and an invoice disagree' is impossible <b>by construction</b> rather
/// than by test"</i>. That is true about STORAGE and says nothing about CONVERSIONS — and a second conversion
/// existed. A <b>compound</b> unit carries its own exact factor
/// (<see cref="Unit.QuantityInBaseMeasure"/>: "1 Box-Nos = 20 Nos"), and
/// <c>InventoryVoucherLineViewModel.RefreshUnitOptions</c> offers every compound unit whose
/// <see cref="Unit.BaseMeasureUnitId"/> is the item's base unit as a LINE unit. Setting such a unit as the
/// item's Alternate Unit with a DIFFERENT factor gave one (item, unit) pair two live, both-reachable
/// conversions.</para>
///
/// <para>🔴 <b>IT WAS REACHABLE FROM THE UI, not only from the service.</b>
/// <c>StockItemMasterViewModel.RefreshPickers</c> fills the Alternate Units picker from
/// <c>_company.Units</c> with no filter at all, so every compound unit in the company is selectable there. One
/// ordinary save was enough.</para>
///
/// <para><b>FIDELITY (R7, ruling 14).</b> The vendor attests the FEATURE and its field —
/// <i>"The <b>Alternate units</b> field appears"</i> and the operator <i>"provide[s] the conversion factor
/// between the simple or compound units and alternative units"</i>
/// [<c>help.tallysolutions.com/manage-stock-item-tally/</c>, the citation row 3.6 already carries]. ⚠️ It does
/// NOT say what happens when the alternate unit is a compound of the item's own base unit, so the REFUSAL below
/// is <b>OURS</b> and is labelled ours. It is the narrowest rule that removes the contradiction: refuse only
/// when the unit is compound, reduces to THIS item's base unit (i.e. exactly when a voucher line would also
/// offer it), and the two factors disagree. An agreeing factor is allowed, and a compound unit reducing to some
/// other measure is untouched.</para>
/// </summary>
public class AlternateUnitTwoConversionsTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>Simple Nos + simple Box + the compound "Box-Nos" = 20 Nos, and one item based in Nos — the exact
    /// shape the Unit master can build and the line-unit picker then offers.</summary>
    private static (Company C, StockItem Item, Unit Nos, Unit BoxNos) Seed()
    {
        var c = CompanyFactory.CreateSeeded("Cd AltUnit Co", FyStart, FyStart);
        var inv = new InventoryService(c);
        var group = inv.CreateStockGroup("Primary");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var box = inv.CreateSimpleUnit("Box", "Boxes");
        var boxNos = inv.CreateCompoundUnit("Box-Nos", "Box of 20 Numbers", box.Id, nos.Id, 20);
        var item = inv.CreateStockItem("Widget", group.Id, nos.Id);
        return (c, item, nos, boxNos);
    }

    /// <summary>
    /// 🔴 <b>THE DEFECT, STATED AS THE TWO NUMBERS IT PRODUCES.</b> Reverting the fifth refusal in
    /// <c>InventoryService.SetAlternateUnit</c> makes this test red, and it is red on exactly the two
    /// assertions at the end: with a factor of 10 against a unit whose own factor is 20, a voucher line keyed
    /// "2 Box-Nos" posts 40 Nos while every alternate display of 40 Nos reads "4 Box-Nos". The refusal names
    /// both factors so the operator can retype the right one.
    /// </summary>
    [Fact]
    public void A_compound_alternate_unit_whose_factor_disagrees_with_its_own_is_refused_by_name()
    {
        var (c, item, _, boxNos) = Seed();
        var inv = new InventoryService(c);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => inv.SetAlternateUnit(item, boxNos.Id, 10m));

        Assert.Contains("Box-Nos", refusal.Message);
        Assert.Contains("20", refusal.Message);          // the unit's own factor
        Assert.Contains("10", refusal.Message);          // the one that was asked for
        Assert.Contains("TWO different conversions", refusal.Message);

        // The item is left untouched — a refusal that half-wrote the pair would be the half-state the other four
        // refusals in that method exist to prevent.
        Assert.Null(item.AlternateUnitId);
        Assert.Null(item.AlternateUnitConversion);
        Assert.False(AlternateUnitConversion.HasAlternateUnit(item));
    }

    /// <summary>
    /// 🔴 <b>THE CONTRADICTION THE REFUSAL PREVENTS, computed from the two conversions rather than asserted as a
    /// message.</b> This test does NOT go through the service — it builds the state the service now refuses and
    /// shows what the product would have printed. It is the arithmetic record of WHY the guard exists, and it is
    /// the assertion a reader should check first: 2 Box-Nos posts 40 Nos through the UNIT, while 40 Nos reads
    /// back as 4 Box-Nos through the ITEM. Both are quantities and they disagree by exactly 20/10.
    /// </summary>
    [Fact]
    public void The_two_conversions_really_do_disagree_which_is_what_makes_the_refusal_necessary()
    {
        var (c, item, _, boxNos) = Seed();

        // The state the guard now refuses, set directly on the entity so the arithmetic can be shown.
        item.AlternateUnitId = boxNos.Id;
        item.AlternateUnitConversion = 10m;

        // What a VOUCHER LINE would post for "2 Box-Nos" — the compound unit's own factor.
        var postedBaseQuantity = boxNos.QuantityInBaseMeasure(2m);
        Assert.Equal(40m, postedBaseQuantity);

        // What every ALTERNATE DISPLAY of those same 40 Nos would read — the item's factor.
        var displayedAlternate = AlternateUnitConversion.ToAlternate(item, postedBaseQuantity);
        Assert.Equal(4m, displayedAlternate);

        // 🔴 The operator keyed 2 and the report says 4. That is the "a report and an invoice disagree" hazard
        // row 3.6 calls impossible by construction, and it was one ordinary save away.
        Assert.NotEqual(2m, displayedAlternate);
    }

    /// <summary>An AGREEING factor is allowed: there is then one conversion, not two, and the voucher line and
    /// the report round-trip the same number. Over-refusing every compound alternate would have been the easy
    /// fix and would have removed a capability the vendor's own sentence names.</summary>
    [Fact]
    public void A_compound_alternate_unit_whose_factor_agrees_is_accepted_and_round_trips()
    {
        var (c, item, _, boxNos) = Seed();
        var inv = new InventoryService(c);

        inv.SetAlternateUnit(item, boxNos.Id, 20m);

        Assert.Equal(boxNos.Id, item.AlternateUnitId);
        Assert.Equal(20m, item.AlternateUnitConversion);

        // One conversion: 2 Box-Nos posts 40 Nos and 40 Nos reads back as 2 Box-Nos.
        var posted = boxNos.QuantityInBaseMeasure(2m);
        Assert.Equal(40m, posted);
        Assert.Equal(2m, AlternateUnitConversion.ToAlternate(item, posted));
        Assert.Equal("1 Box-Nos = 20 Nos", AlternateUnitConversion.DescribeConversion(item, c));
    }

    /// <summary>
    /// A compound unit that reduces to some OTHER measure is untouched by the guard, and that matters: the
    /// voucher line never offers it for this item (the picker filters on
    /// <see cref="Unit.BaseMeasureUnitId"/> == the item's base unit), so no second conversion for this item can
    /// exist and there is nothing to refuse. Refusing it anyway would be a rule wider than its defect.
    /// </summary>
    [Fact]
    public void A_compound_unit_reducing_to_another_measure_is_still_a_legal_alternate_unit()
    {
        var (c, item, _, _) = Seed();
        var inv = new InventoryService(c);
        var gram = inv.CreateSimpleUnit("g", "Grams");
        var kilo = inv.CreateSimpleUnit("Kg", "Kilograms");
        var kgG = inv.CreateCompoundUnit("Kg-g", "Kilogram of 1000 Grams", kilo.Id, gram.Id, 1000);

        // The item is based in Nos; "Kg-g" reduces to g, so the line-unit filter would never offer it here.
        Assert.NotEqual(item.BaseUnitId, kgG.BaseMeasureUnitId);

        inv.SetAlternateUnit(item, kgG.Id, 2.5m);

        Assert.Equal(kgG.Id, item.AlternateUnitId);
        Assert.Equal(2.5m, item.AlternateUnitConversion);
    }

    /// <summary>A SIMPLE alternate unit is the ordinary case and is untouched — the vendor's own worked example
    /// ("1 Box = 10 Nos") must keep working, and a guard that broke it would be caught here.</summary>
    [Fact]
    public void A_simple_alternate_unit_is_untouched_by_the_guard()
    {
        var (c, item, _, _) = Seed();
        var inv = new InventoryService(c);
        var box = c.Units.First(u => u.Symbol == "Box");

        inv.SetAlternateUnit(item, box.Id, 10m);

        Assert.Equal(box.Id, item.AlternateUnitId);
        Assert.Equal(10m, item.AlternateUnitConversion);
        Assert.Equal("1 Box = 10 Nos", AlternateUnitConversion.DescribeConversion(item, c));
    }
}
