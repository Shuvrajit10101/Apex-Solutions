using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// ViewModel coverage for Phase 6 slice 5 (<b>Price Levels / Price Lists</b>; RQ-26..31; gate PR-7), driven
/// through the real shell + entry view model on a throwaway <c>.db</c>. Proves:
/// <list type="bullet">
///   <item>the Price Level / Price List <b>masters</b> create + append-only-revise through the engine;</item>
///   <item>the Sales item-invoice <b>Price-Level header</b> defaults from the party and <b>auto-fills</b> the
///     line Rate/Discount from the resolver (PR-7 worked example: qty 3 → 14,850);</item>
///   <item>an operator <b>override sticks</b> through a later Qty / Price-Level re-resolve (the dirty-flag
///     clobber guard — the highest-risk UI detail);</item>
///   <item>the whole feature is <b>gated</b> by the company flag — a non-price-level Sales screen shows no
///     header, no discount column, no auto-fill (ER-13).</item>
/// </list>
/// </summary>
public sealed class PriceListViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PriceListViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPriceListTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid RetailLevelId { get; init; }
    }

    /// <summary>
    /// A seeded company with the "Enable multiple Price Levels" flag on (when <paramref name="enablePriceLevels"/>),
    /// a "Widget" item with opening stock, a Sales ledger, a customer, a "Retail" price level and a Retail price
    /// list for the item — slabs 0–2 → 16,000 and 2–4 → 14,850 applicable from books-begin (the PR-7 worked example).
    /// </summary>
    private Kit NewKit(string companyName, bool enablePriceLevels = true, bool customerDefaultsRetail = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        if (enablePriceLevels) c.EnableMultiplePriceLevels = true;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(10000m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");

        // Price level + price list (PR-7): Retail 0–2 → 16,000 ; 2–4 → 14,850.
        var levelId = Guid.Empty;
        if (enablePriceLevels)
        {
            var pls = new PriceListService(c);
            var retail = pls.CreateLevel("Retail");
            levelId = retail.Id;
            pls.AddOrReviseList(retail.Id, item.Id, c.BooksBeginFrom, new[]
            {
                new PriceListSlab(0m, 2m, Money.FromRupees(16000m)),
                new PriceListSlab(2m, null, Money.FromRupees(14850m)),
            });
        }

        // Customer (Sundry Debtor) — optionally defaulting to the Retail level.
        var custGroup = c.FindGroupByName("Sundry Debtors")!;
        var customer = new DomainLedger(Guid.NewGuid(), "Beta Buyers", custGroup.Id, Money.Zero, openingIsDebit: false,
            defaultPriceLevelId: enablePriceLevels && customerDefaultsRetail ? levelId : (Guid?)null);
        c.AddLedger(customer);

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            SalesLedgerId = sales.Id,
            CustomerId = customer.Id,
            RetailLevelId = levelId,
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

    private static InventoryVoucherLineViewModel FillLine(VoucherEntryViewModel entry, Kit k, decimal qty)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return line;
    }

    // ================================================================ (1) masters

    [Fact]
    public void Price_level_master_creates_and_lists()
    {
        var k = NewKit("PL Level Master Co");
        var master = new PriceLevelsViewModel(k.Vm.Company!, _storage, onChanged: () => { });

        master.Name = "Wholesale";
        Assert.True(master.Create());
        Assert.Contains(master.Existing, r => r.Name == "Wholesale");

        // Persisted + engine-enforced case-insensitive uniqueness.
        master.Name = "wholesale";
        Assert.False(master.Create());
        Assert.Contains(k.Vm.Company!.PriceLevels, l => l.Name == "Wholesale");
    }

    [Fact]
    public void Price_list_master_appends_dated_versions_history_grows()
    {
        var k = NewKit("PL List Master Co");
        var c = k.Vm.Company!;
        var master = new PriceListsViewModel(c, _storage, onChanged: () => { });

        master.SelectedLevel = master.Levels.Single(l => l.Id == k.RetailLevelId);
        master.SelectedItem = master.Items.Single(i => i.Id == k.ItemId);

        // One version already exists (from NewKit). Revise with a strictly later date → history grows to 2.
        var before = c.PriceListsFor(k.RetailLevelId, k.ItemId).Count();
        master.ApplicableFromText = c.BooksBeginFrom.AddMonths(3).ToString("dd-MMM-yyyy",
            System.Globalization.CultureInfo.InvariantCulture);
        master.Slabs[0].FromText = "0";
        master.Slabs[0].ToText = "";
        master.Slabs[0].RateText = "15000";

        Assert.True(master.Save());
        Assert.Equal(before + 1, c.PriceListsFor(k.RetailLevelId, k.ItemId).Count());
        Assert.Equal(2, master.History.Count);   // append-only: old version retained
    }

    [Fact]
    public void Price_list_master_rejects_earlier_revision_date()
    {
        var k = NewKit("PL List Guard Co");
        var c = k.Vm.Company!;
        var master = new PriceListsViewModel(c, _storage, onChanged: () => { });
        master.SelectedLevel = master.Levels.Single(l => l.Id == k.RetailLevelId);
        master.SelectedItem = master.Items.Single(i => i.Id == k.ItemId);

        // Same/earlier date than the existing version is an edit, not a revision → engine rejects (friendly message).
        master.ApplicableFromText = c.BooksBeginFrom.ToString("dd-MMM-yyyy",
            System.Globalization.CultureInfo.InvariantCulture);
        master.Slabs[0].FromText = "0";
        master.Slabs[0].RateText = "9000";
        Assert.False(master.Save());
        Assert.False(string.IsNullOrEmpty(master.Message));
    }

    // ================================================================ (2) auto-fill (PR-7 + dirty guard)

    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Auto_fill_resolves_the_slab_qty3_is_14850_and_party_defaults_the_level()
    {
        var k = NewKit("PL AutoFill Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.ShowPriceLevelSelector);

        // Party carries a Retail default → the header defaults to Retail (RQ-30).
        SelectParty(entry, k.CustomerId);
        Assert.Equal(k.RetailLevelId, entry.SelectedPriceLevel?.Level?.Id);

        // qty 3 lands in the 2–4 slab → auto-fill 14,850 (PR-7; From≥ / To< boundary rule).
        var line = FillLine(entry, k, 3m);
        Assert.Equal("14,850.00", line.RateText);
        Assert.False(line.IsRateUserDirty);
    }

    [Fact]
    public void Auto_fill_boundary_qty2_goes_to_higher_slab_14850()
    {
        var k = NewKit("PL Boundary Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);

        var line = FillLine(entry, k, 2m);   // boundary → higher slab
        Assert.Equal("14,850.00", line.RateText);

        line.QuantityText = "1";             // re-resolve an un-dirtied line → lower slab
        Assert.Equal("16,000.00", line.RateText);
    }

    [Fact]
    [Trait("Category", "PhaseGate")]
    public void Operator_override_sticks_through_qty_and_level_reresolve()
    {
        var k = NewKit("PL Override Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);

        var line = FillLine(entry, k, 3m);
        Assert.Equal("14,850.00", line.RateText);   // auto-filled

        // Operator overrides the rate — this must NEVER be clobbered by a later re-resolve.
        line.RateText = "15000";
        Assert.True(line.IsRateUserDirty);

        // Changing the quantity re-resolves the price for every un-dirtied line — but this line is dirty.
        line.QuantityText = "1";                     // qty 1 would resolve 16,000 for an un-dirtied line
        Assert.Equal("15000", line.RateText);        // override stuck

        // Changing the Price-Level header also re-resolves — still must not clobber the override.
        var notApplicable = entry.PriceLevelOptions.First(o => o.IsNotApplicable);
        entry.SelectedPriceLevel = notApplicable;
        Assert.Equal("15000", line.RateText);
    }

    [Fact]
    public void Not_applicable_header_does_not_auto_fill()
    {
        var k = NewKit("PL NotApplicable Co", customerDefaultsRetail: false);
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);

        // No party default → header stays "Not Applicable" → no auto-fill; the operator types the rate.
        Assert.True(entry.SelectedPriceLevel?.IsNotApplicable);
        var line = FillLine(entry, k, 3m);
        Assert.Equal(string.Empty, line.RateText);
    }

    [Fact]
    public void Auto_fill_value_uses_net_of_discount_rate()
    {
        var k = NewKit("PL Discount Co");
        var c = k.Vm.Company!;

        // Add a Wholesale level with a 10% discount slab for the item.
        var pls = new PriceListService(c);
        var wholesale = pls.CreateLevel("Wholesale");
        pls.AddOrReviseList(wholesale.Id, k.ItemId, c.BooksBeginFrom, new[]
        {
            new PriceListSlab(0m, null, Money.FromRupees(1000m), discountPercent: 10m),
        });
        _storage.Save(c);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        entry.SelectedPriceLevel = entry.PriceLevelOptions.Single(o => o.Level?.Id == wholesale.Id);

        var line = FillLine(entry, k, 5m);
        Assert.Equal("1,000.00", line.RateText);
        Assert.Equal("10", line.DiscountText);

        // value = qty × net rate = 5 × (1000 − 10%) = 5 × 900 = 4,500.
        Assert.Equal(4500m, entry.ItemsTotal);
    }

    // ================================================================ (3) gating (ER-13)

    [Fact]
    public void Feature_off_hides_the_header_and_discount_and_auto_fill()
    {
        var k = NewKit("PL Off Co", enablePriceLevels: false);
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        // No header selector, no per-line discount column, no auto-fill — byte-identical to a plain Sales screen.
        Assert.False(entry.ShowPriceLevelSelector);
        SelectParty(entry, k.CustomerId);
        var line = FillLine(entry, k, 3m);
        Assert.False(line.ShowDiscount);
        Assert.Equal(string.Empty, line.RateText);   // operator types the rate as before
    }

    [Fact]
    public void Feature_off_does_not_surface_the_masters_in_the_create_menu()
    {
        var k = NewKit("PL Off Menu Co", enablePriceLevels: false);
        // The Create menu (Masters → Create) must not offer Price Level / Price List when the flag is off (RQ-52).
        k.Vm.ShowPriceLevelsMaster();          // gated no-op
        Assert.Null(k.Vm.PriceLevels);
        k.Vm.ShowPriceListsMaster();           // gated no-op
        Assert.Null(k.Vm.PriceLists);
    }

    // ================================================================ (4) report

    [Fact]
    public void Price_list_report_lists_inventory_items_only_and_is_debranded()
    {
        var k = NewKit("PL Report Co");
        var report = new ReportsViewModel(k.Vm.Company!, ReportKind.PriceList);

        Assert.True(report.IsPriceList);
        Assert.True(report.IsInventoryReport);
        Assert.Contains(report.Rows, r => r.Col2 == "Widget");
        Assert.Contains(report.Rows, r => r.Col1 == "Retail");
        // 14,850 slab shows; de-branded (never any "Tally").
        Assert.Contains(report.Rows, r => r.Col6 == "14,850.00");
        Assert.DoesNotContain(report.Rows, r =>
            r.Col1.Contains("Tally", StringComparison.OrdinalIgnoreCase)
            || r.Col2.Contains("Tally", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================ (5) round-trip of party default + posting

    [Fact]
    public void Party_default_level_round_trips_and_discounted_sale_posts()
    {
        var k = NewKit("PL RoundTrip Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        Assert.Equal(k.SalesLedgerId, entry.SelectedStockLedger!.Id);

        FillLine(entry, k, 3m);                       // auto-fills 14,850
        Assert.Equal(14850m * 3m, entry.ItemsTotal);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.True(reloaded.EnableMultiplePriceLevels);
        Assert.Equal(k.RetailLevelId, reloaded.FindLedgerByName("Beta Buyers")!.DefaultPriceLevelId);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.Equal(44550m, posted.InventoryLinesValue.Amount);   // 3 × 14,850
    }

    // ================================================================ (6) miss / context-change resets

    [Fact]
    public void Switching_line_to_item_without_price_list_clears_the_stale_rate()
    {
        var k = NewKit("PL Stale Rate Co");
        var c = k.Vm.Company!;

        // A second item with NO price list under any level.
        var masters = new InventoryService(c);
        var gadget = masters.CreateStockItem("Gadget",
            c.FindStockGroupByName("Goods")!.Id, c.FindUnitByName("Nos")!.Id);
        masters.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5000m));
        _storage.Save(c);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);

        var line = FillLine(entry, k, 3m);
        Assert.Equal("14,850.00", line.RateText);       // Widget auto-fills from Retail
        Assert.False(line.IsRateUserDirty);

        // Switch the un-dirtied line to an item with no price list — the prior item's rate must NOT linger.
        line.SelectedItem = entry.StockItems.Single(i => i.Id == gadget.Id);
        Assert.Equal(string.Empty, line.RateText);
    }

    [Fact]
    public void Selecting_party_without_default_level_resets_header_to_not_applicable()
    {
        var k = NewKit("PL Party Reset Co");
        var c = k.Vm.Company!;

        // A second customer with NO default price level.
        var custGroup = c.FindGroupByName("Sundry Debtors")!;
        var plain = new DomainLedger(Guid.NewGuid(), "Gamma Traders", custGroup.Id, Money.Zero,
            openingIsDebit: false, defaultPriceLevelId: null);
        c.AddLedger(plain);
        _storage.Save(c);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.CustomerId);               // Retail-defaulting party → header = Retail
        Assert.Equal(k.RetailLevelId, entry.SelectedPriceLevel?.Level?.Id);

        SelectParty(entry, plain.Id);                   // party with no default → header must reset
        Assert.True(entry.SelectedPriceLevel?.IsNotApplicable);
    }

    // ================================================================ (7) keyboard (Ctrl+A) route

    [Fact]
    public void Ctrl_a_creates_price_level_and_saves_price_list_via_activate_selected()
    {
        var k = NewKit("PL Ctrl A Co");
        var c = k.Vm.Company!;

        // Price Level master: Ctrl+A (ActivateSelected) must create the typed level.
        k.Vm.ShowPriceLevelsMaster();
        Assert.NotNull(k.Vm.PriceLevels);
        k.Vm.PriceLevels!.Name = "Wholesale";
        k.Vm.ActivateSelected();
        Assert.Contains(c.PriceLevels, l => l.Name == "Wholesale");

        // Price List master: Ctrl+A (ActivateSelected) must save a dated revision.
        k.Vm.ShowPriceListsMaster();
        Assert.NotNull(k.Vm.PriceLists);
        var master = k.Vm.PriceLists!;
        master.SelectedLevel = master.Levels.Single(l => l.Id == k.RetailLevelId);
        master.SelectedItem = master.Items.Single(i => i.Id == k.ItemId);
        var before = c.PriceListsFor(k.RetailLevelId, k.ItemId).Count();
        master.ApplicableFromText = c.BooksBeginFrom.AddMonths(3).ToString("dd-MMM-yyyy",
            System.Globalization.CultureInfo.InvariantCulture);
        master.Slabs[0].FromText = "0";
        master.Slabs[0].ToText = "";
        master.Slabs[0].RateText = "15000";
        k.Vm.ActivateSelected();
        Assert.Equal(before + 1, c.PriceListsFor(k.RetailLevelId, k.ItemId).Count());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
