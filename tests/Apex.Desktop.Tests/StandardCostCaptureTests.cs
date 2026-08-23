using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>T0-3 — "Standard Cost" was offered as a valuation method whose standard rate could not be typed.</b>
/// <para>
/// The Stock Item master's Valuation dropdown has always listed <b>Standard Cost</b>, and the create path passed
/// the selection through unguarded, but <see cref="StockItem.StandardCost"/> — persisted in SQLite, round-tripped
/// through Canonical XML/JSON, and read by <see cref="StockValuationService"/>, <c>NegativeStock</c> and
/// <c>ManufacturingJournalService</c> — had <b>no input anywhere in the UI</b>. An operator who picked Standard Cost
/// got the documented silent fallback to the last purchase rate, with no warning. The census originally called this
/// reachable only through JSON/XML import and withdrew that on 2026-08-18: it is the ordinary UI path.
/// </para>
/// <para>
/// 🔴 <b>SCOPE — this is a CAPTURE fix, not a valuation-arithmetic fix.</b> Nothing in
/// <see cref="StockValuationService"/> changed. Every book already on disk has <c>StandardCost = null</c> on every
/// item, so every existing closing value is byte-identical. What changed is that the rate is now typeable, and that
/// choosing Standard Cost <b>without</b> typing one is refused at the master screen instead of silently falling
/// back. The engine's fallback is deliberately left in place — it is the correct behaviour for an item that reached
/// that state through import or through a pre-fix save, and removing it would change already-stored books.
/// </para>
/// </summary>
public sealed class StandardCostCaptureTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StandardCostCaptureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStdCostTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        return vm.Company!.FindStockGroupByName(name)!;
    }

    private Unit CreateUnit(MainWindowViewModel vm, string symbol, string formal)
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

    /// <summary>
    /// Posts a purchase of <paramref name="qty"/> units at <paramref name="rate"/> so the item has a last purchase
    /// rate and real on-hand stock, using the pure-stock inventory posting path.
    /// </summary>
    private static void PurchaseInto(Company c, StockItem item, decimal qty, decimal rate, DateOnly on)
    {
        var godown = c.Godowns.First();
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var creditor = new Domain.Ledger(Guid.NewGuid(), "Supplier-" + Guid.NewGuid().ToString("N"),
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(creditor);
        var purchases = c.FindLedgerByName("Purchase Accounts") ?? c.Ledgers.FirstOrDefault(l => l.Name == "Purchases");
        if (purchases is null)
        {
            purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases",
                c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
            c.AddLedger(purchases);
        }

        var value = Money.FromRupees(qty * rate);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, on,
            new[]
            {
                new EntryLine(purchases.Id, value, DrCr.Debit),
                new EntryLine(creditor.Id, value, DrCr.Credit),
            },
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, godown.Id, qty, Money.FromRupees(rate), StockDirection.Inward),
            }));
    }

    /// <summary>
    /// 🔴 THE CONSTRUCTED FAILURE, expressed as the capability that was missing. The screen must expose a standard
    /// rate, it must round-trip through a save + reload, and the closing valuation must then be the standard rate —
    /// not the last purchase rate. Book: 10 Nos bought at ₹150 (so the last purchase rate is ₹150), item valued at
    /// Standard Cost ₹100. Correct closing value <b>₹1,000.00</b>. Before this fix the rate could not be typed at
    /// all, <c>StandardCost</c> stayed null, and the engine's documented fallback valued the same closing stock at
    /// the last purchase rate: <b>₹1,500.00</b> — a ₹500 overstatement of closing stock, gross profit and taxable
    /// income, with nothing on screen to say so.
    /// </summary>
    [Fact]
    public void A_standard_rate_typed_on_the_stock_item_screen_values_closing_stock_at_that_rate()
    {
        var vm = NewSeededCompany("Std Cost Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-Std";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = "100";
        Assert.True(m.Create(), m.Message);

        var company = vm.Company!;
        var item = company.FindStockItemByName("Widget-Std")!;
        Assert.Equal(StockValuationMethod.StandardCost, item.ValuationMethod);
        Assert.Equal(Money.FromRupees(100m), item.StandardCost);

        var on = company.BooksBeginFrom.AddDays(30);
        PurchaseInto(company, item, 10m, 150m, on);

        var closing = new StockValuationService(company).ClosingValue(item.Id, on);
        Assert.Equal(10m, closing.Quantity);
        Assert.Equal(Money.FromRupees(1_000m), closing.Value);      // 10 × the ₹100 STANDARD rate
        Assert.NotEqual(Money.FromRupees(1_500m), closing.Value);   // the pre-fix fallback: 10 × the ₹150 purchase
    }

    /// <summary>
    /// The rate survives the round trip to disk and back — the property was always persisted, but nothing had ever
    /// written it from the screen, so this leg had never run end to end.
    /// </summary>
    [Fact]
    public void The_typed_standard_rate_survives_a_save_and_a_reload()
    {
        var vm = NewSeededCompany("Std Cost Persist Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-Persist";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = "247.75";
        Assert.True(m.Create(), m.Message);

        var entry = _storage.ListCompanies().Single(e => e.Name == "Std Cost Persist Co");
        var reloaded = _storage.Load(entry);
        Assert.Equal(Money.FromRupees(247.75m), reloaded.FindStockItemByName("Widget-Persist")!.StandardCost);
    }

    /// <summary>
    /// 🔴 THE SILENT-FALLBACK GUARD. Picking Standard Cost and leaving the rate blank is refused at the screen with
    /// a message naming the field — it is no longer accepted and then quietly valued at the last purchase rate.
    /// Nothing is created.
    /// </summary>
    [Fact]
    public void Standard_cost_valuation_without_a_standard_rate_is_refused_not_silently_fallen_back()
    {
        var vm = NewSeededCompany("Std Cost Guard Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-NoRate";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = string.Empty;

        Assert.False(m.Create());
        Assert.Contains("standard rate", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.Company!.FindStockItemByName("Widget-NoRate"));
    }

    /// <summary>
    /// 🔴 THE PAISA GUARD, AND WHY IT IS NOT DEAD CODE. On the CREATE path the engine also refuses a sub-paisa
    /// standard cost — <see cref="StockItem"/>'s constructor throws "must be to the paisa" — so deleting the screen
    /// guard changes nothing there (mutation-tested: it survived). The ALTER path is different and this is a real
    /// hole the fix had to close: <see cref="StockItem.StandardCost"/> has a public setter and the alter branch
    /// assigns it directly, <b>bypassing the constructor entirely</b>. On that path the screen guard is the only
    /// defence, and without it a sub-paisa standard rate reaches an item that the paisa store cannot round-trip —
    /// the same shape as the Wave 0 defect where one sub-paisa figure made every later save of an open company
    /// throw. <see cref="A_sub_paisa_or_negative_standard_rate_is_refused_on_the_alter_path_too"/> is the test that
    /// makes the guard live.
    /// </summary>
    [Fact]
    public void A_sub_paisa_standard_rate_is_refused()
    {
        var vm = NewSeededCompany("Std Cost Paisa Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-SubPaisa";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = "100.005";

        Assert.False(m.Create());
        Assert.Contains("paisa", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.Company!.FindStockItemByName("Widget-SubPaisa"));
    }

    /// <summary>
    /// 🔴 The alter path has no engine-side validation for this field at all — it assigns
    /// <see cref="StockItem.StandardCost"/> through its public setter, so the constructor's paisa and non-negative
    /// checks never run. The screen guard is therefore the ONLY thing standing between a typed "100.005" and an
    /// item the paisa store cannot persist. Both refusals are asserted here, and the stored value must be untouched.
    /// </summary>
    [Fact]
    public void A_sub_paisa_or_negative_standard_rate_is_refused_on_the_alter_path_too()
    {
        var vm = NewSeededCompany("Std Cost Alter Guard Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-AlterGuard";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = "60";
        Assert.True(m.Create(), m.Message);

        var itemId = vm.Company!.FindStockItemByName("Widget-AlterGuard")!.Id;
        var alter = StockItemMasterViewModel.ForAlter(vm.Company!, _storage, itemId, () => { })!;

        alter.StandardCostText = "100.005";                       // sub-paisa
        Assert.False(alter.Alter());
        Assert.Contains("paisa", alter.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Money.FromRupees(60m), vm.Company!.FindStockItem(itemId)!.StandardCost);

        alter.StandardCostText = "-5";                            // negative
        Assert.False(alter.Alter());
        Assert.Contains("0", alter.Message!);
        Assert.Equal(Money.FromRupees(60m), vm.Company!.FindStockItem(itemId)!.StandardCost);
    }

    /// <summary>
    /// The alter path writes it too, and the load path shows it. Without this the create and alter paths diverge —
    /// the exact shape of the T0-8 defect, where the alter screen silently dropped what create captured.
    /// </summary>
    [Fact]
    public void The_alter_path_loads_and_rewrites_the_standard_rate()
    {
        var vm = NewSeededCompany("Std Cost Alter Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-Alter";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.StandardCost);
        m.StandardCostText = "80";
        Assert.True(m.Create(), m.Message);

        var item = vm.Company!.FindStockItemByName("Widget-Alter")!;

        var alter = StockItemMasterViewModel.ForAlter(vm.Company!, _storage, item.Id, () => { })!;
        Assert.True(alter.IsAltering);
        Assert.Equal("80.00", alter.StandardCostText);   // the stored rate is shown, not a blank box

        alter.StandardCostText = "95.50";
        Assert.True(alter.Alter(), alter.Message);       // alter re-save
        Assert.Equal(Money.FromRupees(95.50m), vm.Company!.FindStockItemByName("Widget-Alter")!.StandardCost);

        // And the alter screen can CLEAR it back to null on a non-Standard-Cost method.
        alter.SelectedValuation = alter.ValuationMethods.Single(v => v.Method == StockValuationMethod.AverageCost);
        alter.StandardCostText = string.Empty;
        Assert.True(alter.Alter(), alter.Message);
        Assert.Null(vm.Company!.FindStockItemByName("Widget-Alter")!.StandardCost);
    }

    /// <summary>
    /// A non-Standard-Cost item may still carry a standard rate (the engine's no-rate inward fallback chain
    /// consults it, and <c>NegativeStock</c> and the Manufacturing Journal both read it), and leaving it blank on
    /// such an item is fine — the guard fires only for the Standard Cost method.
    /// </summary>
    [Fact]
    public void An_average_cost_item_needs_no_standard_rate_but_may_carry_one()
    {
        var vm = NewSeededCompany("Std Cost Optional Co");
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Widget-Avg";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.SelectedValuation = m.ValuationMethods.Single(v => v.Method == StockValuationMethod.AverageCost);
        Assert.True(m.Create(), m.Message);
        Assert.Null(vm.Company!.FindStockItemByName("Widget-Avg")!.StandardCost);

        vm.ShowStockItemMaster();
        var m2 = vm.StockItemMaster!;
        m2.Name = "Widget-Avg-WithStd";
        m2.SelectedGroup = m2.Groups.Single(g => g.Id == group.Id);
        m2.SelectedUnit = m2.Units.Single(u => u.Id == unit.Id);
        m2.SelectedValuation = m2.ValuationMethods.Single(v => v.Method == StockValuationMethod.AverageCost);
        m2.StandardCostText = "42";
        Assert.True(m2.Create(), m2.Message);
        Assert.Equal(Money.FromRupees(42m), vm.Company!.FindStockItemByName("Widget-Avg-WithStd")!.StandardCost);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
