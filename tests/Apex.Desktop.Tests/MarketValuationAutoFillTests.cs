using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.4 — THE MARKET VALUATION AUTO-FILL IS WIRED TO A REAL SALES LINE.</b>
///
/// <para><b>Why this file exists.</b> <see cref="MarketValuationService"/> shipped at schema v65 with
/// <b>zero production callers</b>: a grep over <c>src/</c> returned only its own declaration and three
/// doc-comment mentions. An operator could set <i>Market val.</i> on a stock item, save, reopen and see the
/// value round-trip — and then record a Sales voucher where the rate auto-filled with nothing, forever. That is
/// this project's oldest recurring failure (Voucher Class, the master delete services, <c>ConvertMemorandum</c>):
/// a service graded as progress with no route in. Every test below <b>fails on today's main</b>, because on main
/// nothing calls the service.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>https://help.tallysolutions.com/stock-valuation-methods-tallyprime/</c>
/// ("How to Apply Stock Valuation Methods in TallyPrime | TallyHelp"), opened and read by content 2026-10-01:
/// "<i>Market Valuation Methods help you to auto-fill the selling price of the items while recording sales.</i>"
/// The per-method figures asserted here are <b>computed by hand</b> in each test's own remarks, not read back
/// out of the engine.</para>
///
/// <para>🔴 <b>AND THE BOUNDARY IS ASSERTED, NOT PROMISED.</b>
/// <see cref="Market_valuation_method_moves_no_closing_stock_figure_at_all"/> pins the whole point of ruling 26:
/// a market valuation method must never reach closing stock. It carries the hand-computed overstatement that
/// would appear if it did.</para>
/// </summary>
public sealed class MarketValuationAutoFillTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MarketValuationAutoFillTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMktValAutoFill_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    /// <summary>
    /// A book holding one item — <b>"Widget", opening 100 Nos at ₹100/unit (₹10,000 of stock)</b> — a Sales
    /// ledger and a customer. The item's market valuation method is whatever the caller asks for; its
    /// <b>costing</b> method is left at Average Cost throughout, so the closing-stock figure is an independent
    /// control on every auto-fill assertion.
    /// </summary>
    private Kit NewKit(string companyName, MarketValuationMethod method, Money? standardPrice = null)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        item.MarketValuationMethod = method;
        item.StandardPrice = standardPrice;
        // 🔴 The 4th argument is a per-unit RATE, not a total value — 100 Nos @ ₹100 = ₹10,000 of opening stock.
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            CustomerId = customer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    /// <summary>Opens a fresh Sales item invoice with the customer picked — the operator's own route.</summary>
    private static VoucherEntryViewModel OpenSalesItemInvoice(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        SelectParty(entry, k.CustomerId);
        return entry;
    }

    private static InventoryVoucherLineViewModel FillLine(
        VoucherEntryViewModel entry, Kit k, decimal qty)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        return line;
    }

    /// <summary>
    /// Posts one Sales invoice through the real entry screen at an explicitly TYPED rate (typing dirties the line,
    /// so the auto-fill never clobbers the figure this helper is asked to post).
    /// </summary>
    private static void PostSale(Kit k, decimal qty, decimal rate)
    {
        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, qty);
        line.RateText = rate.ToString(CultureInfo.InvariantCulture);
        Assert.True(line.IsRateUserDirty);
        Assert.True(entry.Accept(), entry.Message);
    }

    /// <summary>Two sales: <b>10 @ ₹150</b> then <b>20 @ ₹180</b>. Every figure below is derived from these.</summary>
    private static void PostTheTwoSales(Kit k)
    {
        PostSale(k, 10m, 150m);
        PostSale(k, 20m, 180m);
    }

    // ================================================================ (1) each method's own figure

    /// <summary>
    /// 🔴 <b>Last Sales Price.</b> "<i>The selling price of the stock item is based on the last price at which the
    /// stock item was sold.</i>" Hand-computed: the two sales are 10 @ ₹150 then 20 @ ₹180, so the <b>last</b>
    /// rated sale rate is <b>₹180.00</b> — not the average, not the cost.
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Last_sales_price_auto_fills_the_most_recent_sale_rate()
    {
        var k = NewKit("MV LastSales Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);

        Assert.Equal("180.00", line.RateText);
        Assert.False(line.IsRateUserDirty);               // auto-filled, not typed
        Assert.Equal(5m * 180m, entry.ItemsTotal);        // 900 — the stamped rate really drives the total
    }

    /// <summary>
    /// 🔴 <b>Average Price.</b> "<i>the average rate at which you will sell the stock item</i>" — total sale amount
    /// ÷ total quantity sold. Hand-computed from the same two sales:
    /// <c>(10 × 150) + (20 × 180) = 1,500 + 3,600 = 5,100</c>; <c>10 + 20 = 30</c>;
    /// <c>5,100 ÷ 30 = </c><b>₹170.00</b>. Deliberately a different figure from both ₹150 and ₹180, so a method
    /// mix-up cannot pass.
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Average_price_auto_fills_total_sale_amount_over_total_quantity()
    {
        var k = NewKit("MV AveragePrice Co", MarketValuationMethod.AveragePrice);
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);

        Assert.Equal("170.00", line.RateText);
        Assert.Equal(5m * 170m, entry.ItemsTotal);        // 850
    }

    /// <summary>
    /// <b>Standard Price</b> auto-fills the per-item rate the operator set, and is <b>independent of the sales
    /// history</b> — ₹195.50 here while the book's last sale was ₹180 and its average ₹170.
    /// </summary>
    [Fact]
    public void Standard_price_auto_fills_the_items_own_standing_rate()
    {
        var k = NewKit("MV StandardPrice Co", MarketValuationMethod.StandardPrice,
            standardPrice: Money.FromRupees(195.50m));
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 4m);

        Assert.Equal("195.50", line.RateText);
        Assert.Equal(4m * 195.50m, entry.ItemsTotal);     // 782.00 — paisa-exact
    }

    /// <summary>
    /// 🔴 <b>ER-13 — At Zero Price fills NOTHING, and that is what keeps every pre-v65 book byte-identical.</b>
    /// <c>AtZeroPrice</c> is ordinal 0 and therefore the stored method of every item that existed before the v65
    /// migration, so this is the case the overwhelming majority of real books are in: the rate field stays empty
    /// and the operator types it, exactly as before the dimension existed. "<i>By default there will be no rate
    /// included when recording the voucher.</i>"
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void At_zero_price_fills_nothing_so_an_existing_book_is_unchanged()
    {
        var k = NewKit("MV ZeroPrice Co", MarketValuationMethod.AtZeroPrice);
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);

        Assert.Equal(string.Empty, line.RateText);
        Assert.False(line.IsRateUserDirty);
    }

    /// <summary>
    /// A <b>Standard Price</b> item whose standing rate was never set fills nothing rather than substituting a
    /// COST. Auto-filling the cost would invoice at zero margin — a quieter failure than filling nothing.
    /// </summary>
    [Fact]
    public void Standard_price_with_no_rate_set_fills_nothing_and_never_falls_back_to_cost()
    {
        var k = NewKit("MV StandardPriceUnset Co", MarketValuationMethod.StandardPrice);
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);

        Assert.Equal(string.Empty, line.RateText);        // NOT "100.00", the ₹100/unit cost
    }

    /// <summary>
    /// An item never sold has nothing to compute a <b>Last Sales Price</b> from, so the line is left for the
    /// operator — again never the cost.
    /// </summary>
    [Fact]
    public void An_item_never_sold_fills_nothing_under_last_sales_price()
    {
        var k = NewKit("MV NeverSold Co", MarketValuationMethod.LastSalesPrice);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);

        Assert.Equal(string.Empty, line.RateText);
    }

    // ================================================================ (2) the operator still owns the field

    /// <summary>
    /// 🔴 <b>AN OPERATOR OVERRIDE STICKS.</b> The auto-fill is a suggestion. A typed rate survives a later
    /// quantity change, which re-runs the whole pass. (This is the clobber trap the price-level auto-fill records;
    /// the market-valuation pass must honour the same dirty flag or a typed invoice silently re-prices itself.)
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void An_operator_typed_rate_is_never_clobbered_by_the_auto_fill()
    {
        var k = NewKit("MV Override Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);

        var entry = OpenSalesItemInvoice(k);
        var line = FillLine(entry, k, 5m);
        Assert.Equal("180.00", line.RateText);

        line.RateText = "21000";
        Assert.True(line.IsRateUserDirty);

        line.QuantityText = "9";                          // re-runs the pass for every un-dirtied line
        Assert.Equal("21000", line.RateText);
        Assert.Equal(9m * 21000m, entry.ItemsTotal);      // 189,000 — the override, not ₹180
    }

    // ================================================================ (3) scope: where it must NOT fire

    /// <summary>
    /// 🔴 <b>A PURCHASE LINE IS NEVER AUTO-FILLED FROM A MARKET VALUATION METHOD.</b> The figure is our own
    /// SELLING price; stamping it onto a purchase would book the stock inward at our margin and land in closing
    /// stock — the T0-2 defect (ruling 26) running in the opposite direction, and the one case where this screen
    /// could move the Balance Sheet.
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void A_purchase_item_invoice_is_not_auto_filled_from_a_selling_price()
    {
        var k = NewKit("MV Purchase Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);
        var c = k.Vm.Company!;
        AddLedger(c, "Purchases", "Purchase Accounts");
        AddLedger(c, "Acme Supplies", "Sundry Creditors");
        _storage.Save(c);

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsPurchaseSideInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Name == "Acme Supplies");

        var line = FillLine(entry, k, 5m);
        Assert.Equal(string.Empty, line.RateText);
    }

    /// <summary>
    /// <b>A Credit Note (sales return) is not re-priced either.</b> "<i>while recording sales</i>" is the cited
    /// scope, and a return is credited at what the customer was actually invoiced — the same reasoning the
    /// price-level selector already applies to both notes (census 4.7/4.8).
    /// </summary>
    [Fact]
    public void A_credit_note_is_not_re_priced_from_the_market_valuation_method()
    {
        var k = NewKit("MV CreditNote Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        SelectParty(entry, k.CustomerId);

        var line = FillLine(entry, k, 2m);
        Assert.Equal(string.Empty, line.RateText);
    }

    /// <summary>
    /// 🔴 <b>THE TRIPWIRE FOR THE <c>!IsAltering</c> GUARD, AND IT IS LABELLED AS A TRIPWIRE RATHER THAN DRESSED
    /// UP AS PROOF OF THE GUARD.</b>
    ///
    /// <para><b>Measured, not assumed:</b> a posted <b>sales item invoice cannot be re-opened at all</b> in this
    /// build. <c>VoucherAlterationEligibility</c> refuses the whole Sales arm by name ("<i>This voucher was
    /// entered as a SALES ITEM INVOICE…</i>", because the list rate and the price-level discount behind the posted
    /// effective rate are not stored), so <c>Ctrl+Enter</c> on the Day Book row returns <c>Refused</c> and no
    /// altering screen exists for the market-valuation pass to run on. The <c>!IsAltering</c> clause in
    /// <c>AllowsMarketValuationAutoFill</c> is therefore <b>defensive and unreachable today</b> — said plainly,
    /// because a test that claimed to exercise it would be a dead guard dressed as a live one.</para>
    ///
    /// <para><b>What this test is for.</b> The day that refusal is lifted (it is already scoped as needing "a
    /// schema column for the list rate and the discount"), a rehydrated line is <b>not rate-dirty</b> — so an
    /// unguarded pass would overwrite the rate the invoice was actually raised at with today's market figure:
    /// ₹150 posted, ₹180 stamped, retrospective silent money loss on a document already given to a customer. This
    /// test goes red at exactly that moment and sends whoever lifts the arm to this file.</para>
    /// </summary>
    [Fact]
    public void A_posted_sales_item_invoice_is_still_unalterable_so_the_IsAltering_guard_is_untested()
    {
        var k = NewKit("MV Alter Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);

        var c = k.Vm.Company!;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var first = c.Vouchers
            .Where(v => v.TypeId == salesType.Id)
            .OrderBy(v => v.Number)
            .First();
        Assert.Equal(150m, first.InventoryLines.Single().Rate.Amount);

        // The operator's own route: Day Book → highlight the row → Ctrl+Enter (RequestAlterHighlightedVoucher).
        k.Vm.OpenReport(ReportKind.DayBook);
        k.Vm.Reports!.SelectedRow = k.Vm.Reports!.Rows.Single(r => r.DrillVoucherId == first.Id);

        Assert.Equal(VoucherAlterationRequest.Refused, k.Vm.RequestAlterHighlightedVoucher());
        Assert.Contains("SALES ITEM INVOICE", k.Vm.Notice);
        Assert.Null(k.Vm.VoucherEntry);

        // 🔴 WHEN THE ASSERTION ABOVE FLIPS, THIS IS THE CONTRACT THAT MUST BE WRITTEN INSTEAD: open the
        // alteration, assert the line still reads 150 (not the ₹180 Last Sales Price), and delete this test.
    }

    // ================================================================ (4) precedence against the price list

    /// <summary>
    /// 🔴 <b>A RESOLVED PRICE-LIST SLAB WINS; the market valuation method fills only where the price list is
    /// silent.</b> A price list is an explicit per-level, per-quantity, dated agreement; a market valuation method
    /// is the item's own standing basis. Both halves are asserted on ONE screen, because precedence that is only
    /// asserted in the winning direction is not precedence: qty 3 resolves the ₹14,850 slab and must read 14,850
    /// (<b>not</b> the ₹180 Last Sales Price), and the second item, which has no price list at all, must read
    /// ₹180 on the very same voucher.
    /// </summary>
    [Fact]
    [Trait("Category", "PhaseGate")]
    public void A_resolved_price_list_slab_wins_and_an_item_with_no_slab_still_gets_the_market_rate()
    {
        var k = NewKit("MV Precedence Co", MarketValuationMethod.LastSalesPrice);
        PostTheTwoSales(k);

        var c = k.Vm.Company!;
        c.EnableMultiplePriceLevels = true;

        // A second item that shares the market valuation method and the sales history pattern, but is on NO
        // price list — it is the "price list silent" half of the precedence rule.
        var masters = new InventoryService(c);
        var gadget = masters.CreateStockItem("Gadget",
            c.FindStockGroupByName("Goods")!.Id, c.FindUnitByName("Nos")!.Id);
        gadget.MarketValuationMethod = MarketValuationMethod.LastSalesPrice;
        masters.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));

        var pls = new PriceListService(c);
        var retail = pls.CreateLevel("Retail");
        pls.AddOrReviseList(retail.Id, k.ItemId, c.BooksBeginFrom, new[]
        {
            new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
            new PriceListSlab(2m, null, Money.FromRupees(14850m)),
        });
        _storage.Save(c);

        // Gadget needs its own sale history so "₹180" below is its OWN last sales price, not Widget's.
        var gadgetKit = new Kit
        {
            Vm = k.Vm, CompanyName = k.CompanyName, ItemId = gadget.Id,
            GodownId = k.GodownId, CustomerId = k.CustomerId,
        };
        PostSale(gadgetKit, 10m, 180m);

        var entry = OpenSalesItemInvoice(k);
        entry.SelectedPriceLevel = entry.PriceLevelOptions.Single(o => o.Level?.Id == retail.Id);

        var widgetLine = FillLine(entry, k, 3m);
        Assert.Equal("14,850.00", widgetLine.RateText);     // the slab wins over ₹180

        entry.AddInventoryLine();
        var gadgetLine = FillLine(entry, gadgetKit, 3m);
        Assert.Equal("180.00", gadgetLine.RateText);        // no slab for Gadget → the market rate fills
    }

    // ================================================================ (5) the ruling-26 boundary, in money

    /// <summary>
    /// 🔴🔴 <b>THE WHOLE POINT OF RULING 26, ASSERTED IN RUPEES: A MARKET VALUATION METHOD MOVES NO CLOSING-STOCK
    /// FIGURE AT ALL.</b>
    ///
    /// <para><b>Hand-computed.</b> Opening 100 Nos valued ₹10,000 ⇒ ₹100/unit. Two sales take 30 units out
    /// (10 + 20), and an outward consumes at the running average without changing it. Closing quantity
    /// <c>100 − 30 = 70</c>; closing value <c>70 × ₹100 = </c><b>₹7,000.00</b>, under Average Cost, for <b>every
    /// one of the four market valuation methods</b>.</para>
    ///
    /// <para><b>The failure this would catch.</b> If a selling price leaked into the valuation the way the retired
    /// <c>LastSaleCost</c> ordinal did, a Last-Sales-Price book would report <c>70 × ₹180 = ₹12,600</c> —
    /// Stock-in-Hand overstated by <b>₹5,600</b>, COGS understated by the same, and the entire unrealised margin
    /// booked as profit. An Average-Price book would report <c>70 × ₹170 = ₹11,900</c>, overstated by ₹4,900.
    /// Both are asserted away below.</para>
    /// </summary>
    [Theory]
    [Trait("Category", "PhaseGate")]
    [InlineData(MarketValuationMethod.AtZeroPrice)]
    [InlineData(MarketValuationMethod.AveragePrice)]
    [InlineData(MarketValuationMethod.LastSalesPrice)]
    [InlineData(MarketValuationMethod.StandardPrice)]
    public void Market_valuation_method_moves_no_closing_stock_figure_at_all(MarketValuationMethod method)
    {
        var name = "MV Boundary Co " + method;
        var k = NewKit(name, method, standardPrice: Money.FromRupees(195.50m));
        PostTheTwoSales(k);

        var reloaded = Reload(name);
        var item = reloaded.FindStockItemByName("Widget")!;
        Assert.Equal(method, item.MarketValuationMethod);                 // the dimension really round-tripped
        Assert.Equal(195.50m, Assert.IsType<Money>(item.StandardPrice).Amount);   // …and so did the selling price
        Assert.Equal(StockValuationMethod.AverageCost, item.ValuationMethod);

        var closing = new StockValuationService(reloaded)
            .ClosingValue(item.Id, DateOnly.FromDateTime(DateTime.Today).AddYears(5));

        Assert.Equal(70m, closing.Quantity);
        Assert.Equal(7000m, closing.Value.Amount);                        // NOT 12,600 and NOT 11,900
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
}
