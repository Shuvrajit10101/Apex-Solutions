using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-10 (CA point 12: "Multiple units should be allowed for same item … 1 dozen should be equal to 12").
///
/// <para>Covers the two load-bearing halves and the money question:</para>
/// <list type="bullet">
///   <item><b>A — conversion direction.</b> The corpus fixes <c>1 × FirstUnit = Factor × TailUnit</c> with
///     First the LARGER unit (Tally Prime Book: "Doz (Dozen) of 12 Nos"; Study Guide table:
///     Dozen/12/Pcs, Kg/1000/Grams, Box/20/Pcs). So a compound unit's base measure is its TAIL.</item>
///   <item><b>B — a line unit is reachable.</b> Proven by driving the REAL menu path (Gateway → Inventory
///     Vouchers → Stock Journal) rather than by constructing the entry view model directly.</item>
///   <item><b>C — rate semantics.</b> The rate is PER THE LINE UNIT DISPLAYED: "2 Doz-Nos @ ₹10" is ₹20,
///     not ₹240. Locked end-to-end here: line total, base-unit stock quantity and stock VALUE all agree.</item>
/// </list>
/// Drives the real shell + page view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class LineUnitConversionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public LineUnitConversionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexLineUnitTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers (all drive the real masters)

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static Unit CreateSimpleUnit(MainWindowViewModel vm, string symbol, string formal)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = formal;
        m.DecimalPlacesText = "0";
        Assert.True(m.Create());
        return vm.Company!.FindUnitByName(symbol)!;
    }

    /// <summary>Creates a compound unit the corpus way: FIRST = the larger unit, TAIL = the smaller.</summary>
    private static Unit CreateCompoundUnit(
        MainWindowViewModel vm, string symbol, string formal, Unit first, Unit tail, int factor)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        Assert.True(m.CanBuildCompound);
        m.IsCompound = true;
        m.Symbol = symbol;
        m.FormalName = formal;
        m.FirstUnit = m.SimpleUnits.Single(u => u.Id == first.Id);
        m.TailUnit = m.SimpleUnits.Single(u => u.Id == tail.Id);
        m.ConversionFactorText = factor.ToString();
        Assert.True(m.Create(), m.Message);
        return vm.Company!.FindUnitByName(symbol)!;
    }

    private static StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        return vm.Company!.FindStockGroupByName(name)!;
    }

    private static StockItem CreateItem(MainWindowViewModel vm, string name, StockGroup group, Unit baseUnit)
    {
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.True(m.CanCreate);
        m.Name = name;
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == baseUnit.Id);
        Assert.True(m.Create(), m.Message);
        return vm.Company!.FindStockItemByName(name)!;
    }

    /// <summary>
    /// Highlights the menu row with <paramref name="label"/> using the REAL arrow API and activates it —
    /// the same path a keyboard user walks. Fails loudly if the row is not present/reachable.
    /// </summary>
    private static void DriveMenuTo(MainWindowViewModel vm, string label)
    {
        var target = -1;
        for (var i = 0; i < vm.Menu.Count; i++)
            if (vm.Menu[i].IsSelectable && vm.Menu[i].Label == label) { target = i; break; }
        Assert.True(target >= 0,
            $"\"{label}\" is not a selectable row in the current menu — the screen is UNREACHABLE. "
            + $"Rows: {string.Join(" | ", vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label))}");

        var guard = 0;
        while (vm.SelectedIndex != target)
        {
            vm.MoveDown();
            Assert.True(++guard <= vm.Menu.Count * 2, $"Could not arrow onto \"{label}\".");
        }
        Assert.Equal(label, vm.Menu[vm.SelectedIndex].Label);
        vm.ActivateSelected();
    }

    // ================================================================ A — direction

    [Fact]
    public void A_compound_units_base_measure_is_its_tail_the_smaller_unit()
    {
        var vm = NewSeededCompany("Direction Co");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var g = CreateSimpleUnit(vm, "g", "Grams");
        var kg = CreateSimpleUnit(vm, "Kg", "Kilograms");

        // The corpus's own three examples: Dozen/12/Pcs, Kg/1000/Grams, Box/20/Pcs.
        var dozNos = CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        var kgG = CreateCompoundUnit(vm, "Kg-g", "Kilogram of 1000 Grams", kg, g, 1000);

        // Scaling by the factor lands in the TAIL (the smaller unit) — so the base measure is the tail.
        Assert.Equal(12m, dozNos.QuantityInBaseMeasure(1m));
        Assert.Equal(nos.Id, dozNos.BaseMeasureUnitId);
        Assert.Equal(1000m, kgG.QuantityInBaseMeasure(1m));
        Assert.Equal(g.Id, kgG.BaseMeasureUnitId);

        // A simple unit is its own base measure and converts nothing.
        Assert.Equal(nos.Id, nos.BaseMeasureUnitId);
        Assert.Equal(7m, nos.QuantityInBaseMeasure(7m));
    }

    [Fact]
    public void A_rate_converts_inversely_so_the_line_total_is_invariant()
    {
        var vm = NewSeededCompany("Rate Direction Co");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var dozNos = CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);

        // "2 Doz @ ₹10" — quantity scales UP by 12, so the rate must scale DOWN by 12.
        var qtyBase = dozNos.QuantityInBaseMeasure(2m);      // 24 Nos
        var rateBase = dozNos.RateInBaseMeasure(10m);        // ₹0.8333… per Nos

        Assert.Equal(24m, qtyBase);
        // The line TOTAL is what must be exact: 24 × 0.8333… = 20.00 to the paisa, never 240.
        Assert.Equal(20.00m, Math.Round(qtyBase * rateBase, 2, MidpointRounding.AwayFromZero));
        Assert.NotEqual(240m, Math.Round(qtyBase * rateBase, 2, MidpointRounding.AwayFromZero));
    }

    // ================================================================ B — reachability (drive the real UI)

    [Fact]
    public void B_line_unit_picker_is_reachable_by_driving_the_real_menu_to_a_stock_journal()
    {
        var vm = NewSeededCompany("Reachable Co");
        var grp = CreateGroup(vm, "Goods");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var dozNos = CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        CreateItem(vm, "Egg", grp, nos);

        // Walk the REAL navigation: Gateway → Inventory Vouchers → highlight "Stock Journal" → activate.
        vm.ShowInventoryVouchersMenu();
        DriveMenuTo(vm, "Stock Journal");
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);

        var entry = vm.InventoryVoucherEntry!;
        var line = entry.Lines.First();

        // Before an item is picked there is nothing to state a unit in.
        Assert.False(line.ShowUnit);

        // Picking the Nos-measured item surfaces BOTH its base unit and the compound reducing to it.
        line.SelectedItem = line.StockItems.Single(i => i.Name == "Egg");
        Assert.True(line.ShowUnit, "The unit picker never became visible on a real, menu-reached entry line.");
        Assert.Equal(new[] { "Nos", "Doz-Nos" }, line.UnitOptions.Select(u => u.Symbol).ToArray());

        // It defaults to the item's base unit, so an untouched line behaves exactly as before.
        Assert.Equal(nos.Id, line.SelectedUnit!.Id);
        Assert.Null(line.UnitId);

        // Choosing the compound stamps it and converts.
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == dozNos.Id);
        line.QuantityText = "2";
        Assert.Equal(dozNos.Id, line.UnitId);
        Assert.Equal(24m, line.ParsedQuantityInBaseUnit);
    }

    [Fact]
    public void B_picker_offers_only_units_that_reduce_to_the_items_own_base_unit()
    {
        var vm = NewSeededCompany("Filter Co");
        var grp = CreateGroup(vm, "Goods");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var g = CreateSimpleUnit(vm, "g", "Grams");
        var kg = CreateSimpleUnit(vm, "Kg", "Kilograms");
        CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        CreateCompoundUnit(vm, "Kg-g", "Kilogram of 1000 Grams", kg, g, 1000);
        CreateItem(vm, "Egg", grp, nos);       // measured in Nos
        CreateItem(vm, "Flour", grp, g);       // measured in grams

        vm.ShowInventoryVouchersMenu();
        DriveMenuTo(vm, "Stock Journal");
        var line = vm.InventoryVoucherEntry!.Lines.First();

        line.SelectedItem = line.StockItems.Single(i => i.Name == "Egg");
        Assert.Equal(new[] { "Nos", "Doz-Nos" }, line.UnitOptions.Select(u => u.Symbol).ToArray());
        Assert.DoesNotContain(line.UnitOptions, u => u.Symbol == "Kg-g");   // never a mass unit on a count item

        // Switching the item re-filters AND re-defaults — a stale, now-illegal pick cannot survive.
        line.SelectedUnit = line.UnitOptions.Single(u => u.Symbol == "Doz-Nos");
        line.SelectedItem = line.StockItems.Single(i => i.Name == "Flour");
        Assert.Equal(new[] { "g", "Kg-g" }, line.UnitOptions.Select(u => u.Symbol).ToArray());
        Assert.Equal("g", line.SelectedUnit!.Symbol);
    }

    [Fact]
    public void B_engine_rejects_a_line_unit_that_does_not_reduce_to_the_items_base_unit()
    {
        var vm = NewSeededCompany("Guard Co");
        var grp = CreateGroup(vm, "Goods");
        var g = CreateSimpleUnit(vm, "g", "Grams");
        var kg = CreateSimpleUnit(vm, "Kg", "Kilograms");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var kgG = CreateCompoundUnit(vm, "Kg-g", "Kilogram of 1000 Grams", kg, g, 1000);
        var egg = CreateItem(vm, "Egg", grp, nos);

        var company = vm.Company!;
        var posting = new InventoryPostingService(company);
        var type = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);

        // "1 Kg-g" of a Nos-measured item would silently scale by 1000. The engine must refuse it.
        var ex = Assert.Throws<InvalidOperationException>(() => posting.Post(new InventoryVoucher(
            Guid.NewGuid(), type.Id, company.BooksBeginFrom,
            new[]
            {
                new InventoryAllocation(egg.Id, company.MainLocation!.Id, 1m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kgG.Id),
            })));
        Assert.Contains("does not reduce to the item's base unit", ex.Message);
    }

    // ================================================================ C — rate semantics, end to end

    [Fact]
    public void C_rate_is_per_the_line_unit_so_two_dozen_at_ten_rupees_is_twenty_rupees()
    {
        var vm = NewSeededCompany("Rate Semantics Co");
        var grp = CreateGroup(vm, "Goods");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var dozNos = CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        CreateItem(vm, "Egg", grp, nos);

        // Enter a Receipt Note through the REAL menu, in DOZENS, at ₹10 per dozen.
        vm.ShowInventoryVouchersMenu();
        DriveMenuTo(vm, "Receipt Note");
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);

        var entry = vm.InventoryVoucherEntry!;
        var line = entry.Lines.First();
        line.SelectedItem = line.StockItems.Single(i => i.Name == "Egg");
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == dozNos.Id);
        line.QuantityText = "2";
        line.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        var company = vm.Company!;
        var item = company.FindStockItemByName("Egg")!;
        var asOf = entry.Date;

        // (i) the LINE is persisted in the unit it was entered in — 2, stamped Doz-Nos.
        var alloc = company.InventoryVouchers.SelectMany(v => v.Allocations).Single(a => a.StockItemId == item.Id);
        Assert.Equal(2m, alloc.Quantity);
        Assert.Equal(dozNos.Id, alloc.UnitId);

        // (ii) STOCK QUANTITY is accumulated in the item's BASE unit — 2 Doz = 24 Nos.
        Assert.Equal(24m, new InventoryLedger(company).OnHand(item.Id, company.MainLocation!.Id, asOf));

        // (iii) STOCK VALUE is the line total — ₹20.00, NOT ₹240.00 (the 12× error this locks out).
        var value = new StockValuationService(company).ClosingValue(item.Id, asOf).Value;
        Assert.Equal(Money.FromRupees(20m), value);
        Assert.NotEqual(Money.FromRupees(240m), value);

        // (iv) PERSISTENCE round-trip. The line unit needs no schema change — inventory_allocations.unit_id
        // already exists at CreateV1 and InventoryAllocationDto already carries it — but that is only worth
        // relying on if it is PROVEN, so reload the company from the .db and re-assert every number above.
        var entryRow = _storage.ListCompanies().Single(e => e.Name == "Rate Semantics Co");
        var reloaded = _storage.Load(entryRow);
        var rItem = reloaded.FindStockItemByName("Egg")!;
        var rAlloc = reloaded.InventoryVouchers.SelectMany(v => v.Allocations)
            .Single(a => a.StockItemId == rItem.Id);

        Assert.Equal(2m, rAlloc.Quantity);
        Assert.Equal(dozNos.Id, rAlloc.UnitId);          // the unit SURVIVED the round-trip
        Assert.Equal(24m, new InventoryLedger(reloaded).OnHand(rItem.Id, reloaded.MainLocation!.Id, asOf));
        Assert.Equal(Money.FromRupees(20m),
            new StockValuationService(reloaded).ClosingValue(rItem.Id, asOf).Value);
    }

    [Fact]
    public void C_a_line_left_in_the_base_unit_stamps_no_unit_and_values_exactly_as_before()
    {
        var vm = NewSeededCompany("Base Unit Co");
        var grp = CreateGroup(vm, "Goods");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        CreateItem(vm, "Egg", grp, nos);

        vm.ShowInventoryVouchersMenu();
        DriveMenuTo(vm, "Receipt Note");
        var entry = vm.InventoryVoucherEntry!;
        var line = entry.Lines.First();
        line.SelectedItem = line.StockItems.Single(i => i.Name == "Egg");
        line.QuantityText = "24";        // left on the default (base) unit
        line.RateText = "0.85";
        Assert.True(entry.Accept(), entry.Message);

        var company = vm.Company!;
        var item = company.FindStockItemByName("Egg")!;
        var alloc = company.InventoryVouchers.SelectMany(v => v.Allocations).Single(a => a.StockItemId == item.Id);

        // ER-13: a base-unit line stamps NO unit id at all, so its persisted/exported shape is unchanged.
        Assert.Null(alloc.UnitId);
        Assert.Equal(24m, alloc.Quantity);
        Assert.Equal(24m, new InventoryLedger(company).OnHand(item.Id, company.MainLocation!.Id, entry.Date));
        Assert.Equal(Money.FromRupees(20.40m), new StockValuationService(company).ClosingValue(item.Id, entry.Date).Value);
    }

    [Fact]
    public void C_stock_journal_balances_a_dozen_out_against_twelve_nos_in()
    {
        var vm = NewSeededCompany("Journal Balance Co");
        var grp = CreateGroup(vm, "Goods");
        var doz = CreateSimpleUnit(vm, "Doz", "Dozens");
        var nos = CreateSimpleUnit(vm, "Nos", "Numbers");
        var dozNos = CreateCompoundUnit(vm, "Doz-Nos", "Dozen of 12 Numbers", doz, nos, 12);
        var egg = CreateItem(vm, "Egg", grp, nos);

        // Seed 24 Nos on hand so the outward leg is coverable.
        var company = vm.Company!;
        new InventoryService(company).AddOpeningBalance(
            egg.Id, company.MainLocation!.Id, 24m, Money.FromRupees(1m));
        var wh2 = new InventoryService(company).CreateGodown("WH2");

        vm.ShowInventoryVouchersMenu();
        DriveMenuTo(vm, "Stock Journal");
        var entry = vm.InventoryVoucherEntry!;

        // Source: 1 Doz-Nos out. Destination: 12 Nos in. Different units, same base quantity.
        var src = entry.Lines.First();
        src.SelectedItem = src.StockItems.Single(i => i.Name == "Egg");
        src.SelectedUnit = src.UnitOptions.Single(u => u.Id == dozNos.Id);
        src.QuantityText = "1";

        var dst = entry.DestinationLines.First();
        dst.SelectedItem = dst.StockItems.Single(i => i.Name == "Egg");
        dst.SelectedGodown = dst.Godowns.Single(g => g.Name == "WH2");
        dst.QuantityText = "12";        // base unit

        // The balance check normalises BOTH sides to the base unit, so these balance.
        Assert.True(entry.IsBalanced,
            $"1 Doz-Nos out did not balance 12 Nos in — the balance check is not normalising. {entry.BalanceText}");
        Assert.True(entry.Accept(), entry.Message);

        var ledger = new InventoryLedger(company);
        Assert.Equal(12m, ledger.OnHand(egg.Id, company.MainLocation.Id, entry.Date));   // 24 − 12
        Assert.Equal(12m, ledger.OnHand(egg.Id, wh2.Id, entry.Date));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held .db handle must not fail the run */ }
    }
}
