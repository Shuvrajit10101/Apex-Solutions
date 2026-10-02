using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.4 — THE COSTING-METHOD LIST AND THE MARKET-VALUATION DIMENSION (defect T0-2, register IV-6;
/// closed by USER RULING 26 at schema v65).</b>
///
/// <para><b>The vendor's taxonomy, cited (ruling 14) and re-opened by content 2026-09-21.</b>
/// <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c> offers <b>two separate fields</b>:
/// <b>nine Costing Methods</b> (At Zero Cost, Average Cost, FIFO, FIFO Perpetual, Last Purchase Cost, LIFO
/// Annual, LIFO Perpetual, Standard Cost, Monthly Average Cost) and <b>four Market Valuation Methods</b>
/// (At Zero Price, Average Price, Last Sales Price, Standard Price).</para>
///
/// <para>🔴 <b>WHAT CHANGED, AND WHY THIS FILE WAS REWRITTEN.</b> Its previous version pinned an INTERIM state:
/// the vendor's <i>Last Sales Price</i> was sitting in our COSTING picker, valuing closing stock at our own
/// selling rate, and the only thing that wave could do without storage and a user ruling was relabel it. It said
/// so explicitly — "if a later wave adds the column and the missing methods, this test fails and forces the
/// census row to be re-graded deliberately rather than drifting". <b>That is exactly what happened.</b> Schema
/// v65 added the Market Valuation dimension and migrated every affected book off the retired method, so these
/// tests now pin the CLOSED shape:
/// <list type="bullet">
///   <item>the retired method is in <b>no</b> picker at all;</item>
///   <item>a separate Market Valuation picker exists, with the vendor's four methods;</item>
///   <item>the divergence that REMAINS — three unshipped costing methods — is still pinned at its measured size.</item>
/// </list></para>
///
/// <para>3.4 is no longer "no market-valuation dimension at all". It is <b>six of nine costing methods plus the
/// complete four-method market-valuation dimension</b>, which is why the row is graded PARTIAL and not COMPLETE.</para>
/// </summary>
public sealed class CostingMethodClassificationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CostingMethodClassificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCostingLabelTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private static StockItemMasterViewModel OpenStockItemMaster(MainWindowViewModel vm)
    {
        vm.ShowStockItemMaster();
        return vm.StockItemMaster!;
    }

    /// <summary>
    /// 🔴 <b>THE SELLING-PRICE BASIS IS GONE FROM THE COSTING PICKER ENTIRELY — the closure of T0-2 at the point
    /// where the operator actually chooses.</b> Relabelling it was the interim fix; removing it is the real one.
    /// No costing option may mention a sale, a selling price or a market valuation, because every option in this
    /// field now values closing stock and none of them may do so at what we charge.
    ///
    /// <para><b>Red on today's main</b>, where the picker still offers "Last Sale Price (market valuation)".</para>
    /// </summary>
    [Fact]
    public void The_costing_picker_no_longer_offers_any_selling_price_basis()
    {
        var vm = NewCompany("Costing Label Co");
        var master = OpenStockItemMaster(vm);

        Assert.DoesNotContain(master.ValuationMethods, o => o.Method == StockValuationMethod.LastSaleCost);

        foreach (var option in master.ValuationMethods)
        {
            Assert.DoesNotContain("Sale", option.Display, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Price", option.Display, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("market valuation", option.Display, StringComparison.OrdinalIgnoreCase);
            // The standing brand rule: no user-visible string in this application names the reference product.
            Assert.DoesNotContain("Tally", option.Display, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 🔴 <b>THE MARKET VALUATION DIMENSION EXISTS, IS REACHABLE, AND IS THE VENDOR'S OWN FOUR-METHOD LIST.</b>
    /// It is a separate picker from the costing one — two vendor fields, not one merged list — and it defaults to
    /// At Zero Price, the only option of the four that auto-fills nothing and can therefore state no wrong number
    /// for an item that existed before the dimension did.
    ///
    /// <para><b>Red on today's main</b>, where <c>MarketValuationMethods</c> does not exist.</para>
    /// </summary>
    [Fact]
    public void The_market_valuation_picker_offers_the_vendors_four_methods()
    {
        var vm = NewCompany("Market Valuation Co");
        var master = OpenStockItemMaster(vm);

        Assert.Equal(4, master.MarketValuationMethods.Count);
        Assert.Equal(4, Enum.GetValues<MarketValuationMethod>().Length);

        Assert.Equal(
            new[] { "At Zero Price", "Average Price", "Last Sales Price", "Standard Price" },
            master.MarketValuationMethods.Select(o => o.Display).ToArray());

        // Defaults to the inert basis, and no label names the reference product.
        Assert.Equal(MarketValuationMethod.AtZeroPrice, master.SelectedMarketValuation!.Method);
        foreach (var option in master.MarketValuationMethods)
            Assert.DoesNotContain("Tally", option.Display, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The picker and the stock-item LIST must spell one option one way. They are two independent switch
    /// statements over the same enum, so a label corrected in one and not the other reads to an operator as two
    /// different settings — and this row is precisely where a second spelling would be believed.
    /// </summary>
    [Fact]
    public void The_picker_and_the_item_list_spell_every_method_identically()
    {
        var vm = NewCompany("Costing Label Agreement Co");
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");

        foreach (var option in OpenStockItemMaster(vm).ValuationMethods.ToList())
            masters.CreateStockItem("Item " + option.Method, grp.Id, nos.Id, valuationMethod: option.Method);
        _storage.Save(c);

        // Re-open the master so its Existing list is rebuilt over the items just created.
        var master = OpenStockItemMaster(vm);
        foreach (var option in master.ValuationMethods)
        {
            var row = master.Existing.Single(r => r.Name == "Item " + option.Method);
            Assert.Equal(option.Display, row.Valuation);
        }
    }

    /// <summary>
    /// 🔴 <b>AN ITEM THAT ESCAPED THE MIGRATION IS NOT LABELLED WITH A BASIS THE ENGINE NO LONGER HONOURS.</b>
    /// The retired ordinal can still reach the list from an externally-restored or hand-edited book. Since
    /// <c>StockValuationService</c> maps it onto Last Purchase Cost, a row reading "Last Sale Price" would be a
    /// lie about the operator's own Balance Sheet — so the label names what the row is actually valued at, and
    /// says where it came from.
    /// </summary>
    [Fact]
    public void An_item_still_on_the_retired_ordinal_is_labelled_by_what_it_is_actually_valued_at()
    {
        var vm = NewCompany("Escaped Migration Co");
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Stray", grp.Id, nos.Id);
        item.ValuationMethod = StockValuationMethod.LastSaleCost;   // as only a non-migrated book could be
        _storage.Save(c);

        var row = OpenStockItemMaster(vm).Existing.Single(r => r.Name == "Stray");

        Assert.Contains("Last Purchase Cost", row.Valuation, StringComparison.Ordinal);
        Assert.DoesNotContain("Sale", row.Valuation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AtZeroCost", row.Valuation, StringComparison.Ordinal);   // no raw enum spelling
    }

    /// <summary>
    /// 🔴 <b>THE REMAINING DIVERGENCE IS PINNED AT ITS MEASURED SIZE, so nobody reads ruling 26 as a full
    /// closure.</b> Six costing methods are offered where the vendor documents nine. The three still missing —
    /// FIFO Perpetual, LIFO Perpetual and Monthly Average Cost — all hang on the same unasked question of whether
    /// our FIFO/LIFO reset at the financial year (they do not), which is a separate wrong-money decision. If a
    /// later wave adds them, this test fails and forces the census row to be re-graded deliberately rather than
    /// drifting.
    /// </summary>
    [Fact]
    public void The_shipped_costing_list_is_still_six_of_the_vendors_nine()
    {
        var vm = NewCompany("Costing Divergence Co");
        var master = OpenStockItemMaster(vm);

        Assert.Equal(6, master.ValuationMethods.Count);
        // Seven enum members: the six offered, plus the RETIRED ordinal kept only so a stray value 5 maps
        // explicitly onto a real cost basis instead of falling through a default arm.
        Assert.Equal(7, Enum.GetValues<StockValuationMethod>().Length);

        // The three vendor costing methods we still do not implement are not offered under any spelling.
        foreach (var missing in new[] { "FIFO Perpetual", "LIFO Perpetual", "Monthly Average" })
            Assert.DoesNotContain(master.ValuationMethods,
                o => o.Display.Contains(missing, StringComparison.OrdinalIgnoreCase));

        // At Zero Cost IS now shipped — it was one of the missing nine in the previous version of this test.
        Assert.Contains(master.ValuationMethods, o => o.Method == StockValuationMethod.AtZeroCost);
    }
}
