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
/// 🔴 <b>CENSUS 3.4 — THE COSTING-METHOD LIST, AND THE MARKET-VALUATION METHOD SITTING IN IT (defect T0-2,
/// register IV-6).</b>
///
/// <para><b>The vendor's taxonomy, measured 2026-09-15 and cited (ruling 14).</b> help.tallysolutions.com, "How to
/// Apply Stock Valuation Methods in TallyPrime", offers <b>two separate fields</b>: <b>nine Costing Methods</b>
/// (At Zero Cost, Average Cost, FIFO, FIFO Perpetual, Last Purchase Cost, LIFO Annual, LIFO Perpetual, Standard
/// Cost, Monthly Average Cost) and <b>four Market Valuation Methods</b> (At Zero Price, Average Price,
/// <b>Last Sales Price</b>, Standard Price). We ship <b>six</b>, all in the costing slot, and one of them —
/// <c>LastSaleCost</c> — is the vendor's <i>Last Sales Price</i>, a MARKET VALUATION method.</para>
///
/// <para>🔴 <b>WHAT THIS SLICE COULD AND COULD NOT DO.</b> Moving the method to where it belongs is not a code
/// edit: there is no Market Valuation field to move it to, so it needs a stored column AND a user ruling about
/// books that already chose it — their closing stock changes the day it moves. Both are reported, not attempted.
/// <b>What needed no storage is the LABEL</b>, and that is what these tests pin: the picker no longer calls a
/// selling-price basis a <i>cost</i>. An operator who chooses it can now see what it is before the Balance Sheet
/// tells them — which is how T0-2 reached real books ("closing stock valued at SELLING price").</para>
///
/// <para>These tests do <b>not</b> claim the row is closed. 3.4 stays PARTIAL: six of nine, no market-valuation
/// dimension at all, and the misfiled method still valuing closing stock the way it always did.</para>
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
    /// 🔴 <b>THE PICKER NO LONGER CALLS A SELLING PRICE A COST.</b> The option reads
    /// <i>"Last Sale Price (market valuation)"</i>, naming the basis the operator is actually choosing.
    ///
    /// <para><b>Red on today's main</b>, where it reads "Last Sale Cost" — a label that asserts the one thing that
    /// is false about it, in the field where the choice is made.</para>
    ///
    /// <para>The label deliberately does NOT name the reference product: no user-visible string in this
    /// application may, and the asserted absence below is the standing brand rule, not a stylistic preference.</para>
    /// </summary>
    [Fact]
    public void The_misfiled_market_valuation_method_is_not_labelled_as_a_cost()
    {
        var vm = NewCompany("Costing Label Co");
        var master = OpenStockItemMaster(vm);

        var option = master.ValuationMethods.Single(o => o.Method == StockValuationMethod.LastSaleCost);

        Assert.Equal("Last Sale Price (market valuation)", option.Display);
        Assert.DoesNotContain("Cost", option.Display, StringComparison.Ordinal);
        Assert.Contains("market valuation", option.Display, StringComparison.Ordinal);
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
    /// 🔴 <b>THE DIVERGENCE IS PINNED AT ITS MEASURED SIZE, so nobody reads the relabel as a closure.</b> Six
    /// methods are offered where the vendor documents nine costing methods, and the Market Valuation dimension is
    /// absent entirely. If a later wave adds the column and the missing methods, this test fails and forces the
    /// census row to be re-graded deliberately rather than drifting.
    /// </summary>
    [Fact]
    public void The_shipped_list_is_still_six_of_nine_with_no_market_valuation_dimension()
    {
        var vm = NewCompany("Costing Divergence Co");
        var master = OpenStockItemMaster(vm);

        Assert.Equal(6, master.ValuationMethods.Count);
        Assert.Equal(6, Enum.GetValues<StockValuationMethod>().Length);

        // None of the vendor's four costing methods we do not implement is offered under any spelling.
        foreach (var missing in new[] { "At Zero Cost", "FIFO Perpetual", "LIFO Perpetual", "Monthly Average" })
            Assert.DoesNotContain(master.ValuationMethods,
                o => o.Display.Contains(missing, StringComparison.OrdinalIgnoreCase));

        // And there is no market-valuation FIELD — only the one misfiled method sitting in the costing list.
        Assert.Single(master.ValuationMethods.Where(
            o => o.Display.Contains("market valuation", StringComparison.OrdinalIgnoreCase)));
    }
}
