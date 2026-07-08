using System;
using System.Globalization;
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
/// Coverage for the <b>Additional Cost of Purchase</b> UI (Book pp.133–141; catalog §11; Phase 6 slice 3
/// RQ-16..RQ-20), driven through the real shell + view models on a throwaway <c>.db</c>. Proves:
/// (1) the additional-cost area shows ONLY when the Purchase is entered as an item invoice AND its voucher type
///     has Track Additional Costs on (and never on a Sales); (2) editing an additional-cost amount stamps each
///     item line's read-only <b>landed</b> rate to the value the SAME engine (<c>ForPurchase</c>) computes —
///     screen == engine (ER-4); (3) the ledger master's Method-of-Appropriation field is gated to a
///     Direct-Expenses ledger with the F12 configuration on, and a non-None pick persists onto the ledger;
/// (4) RQ-19 — a plain Direct-Expenses ledger with no method is not an additional-cost ledger and loads no rate.
/// </summary>
public sealed class AdditionalCostViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AdditionalCostViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAddlCostTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemAId { get; init; }
        public required Guid ItemBId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public Guid FreightByQtyId { get; set; }
        public Guid FreightByValueId { get; set; }
        public Guid PlainFreightId { get; set; }
    }

    /// <summary>
    /// A seeded company with two items (A "Nos", B "Nos"), a Purchases ledger, a supplier, and — optionally —
    /// additional-cost ledgers (Freight by Quantity / by Value) plus a plain (no-method) Direct-Expenses ledger.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var itemA = masters.CreateStockItem("Item A", grp.Id, nos.Id);
        var itemB = masters.CreateStockItem("Item B", grp.Id, nos.Id);

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");

        var kit = new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemAId = itemA.Id,
            ItemBId = itemB.Id,
            MainGodownId = c.MainLocation!.Id,
            PurchasesLedgerId = purchases.Id,
            SupplierId = supplier.Id,
        };

        kit.FreightByQtyId = AddAdditionalCostLedger(c, "Freight (by Qty)", MethodOfAppropriation.ByQuantity).Id;
        kit.FreightByValueId = AddAdditionalCostLedger(c, "Freight (by Value)", MethodOfAppropriation.ByValue).Id;
        kit.PlainFreightId = AddLedger(c, "Plain Freight", "Direct Expenses").Id; // no method → RQ-19

        _storage.Save(c);
        return kit;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static DomainLedger AddAdditionalCostLedger(Company c, string name, MethodOfAppropriation method)
    {
        var group = c.FindGroupByName("Direct Expenses")
                    ?? throw new InvalidOperationException("No 'Direct Expenses' group.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: true,
            methodOfAppropriation: method);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void FillItemLine(InventoryVoucherLineViewModel line, VoucherEntryViewModel entry,
        Guid itemId, Guid godownId, decimal qty, string rate)
    {
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private VoucherEntryViewModel OpenTrackedPurchaseInvoice(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.TrackAdditionalCosts = true;
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);
        return entry;
    }

    // ---------------------------------------------------------------- (1) gating

    [Fact]
    public void Additional_cost_area_shows_only_for_a_tracked_purchase_item_invoice()
    {
        var k = NewKit("Addl Gating Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;

        // Plain accounting mode (no item invoice) → hidden even though the type could track.
        Assert.False(entry.IsItemInvoice);
        Assert.False(entry.ShowAdditionalCosts);

        // Item-invoice on but tracking still off → hidden; the voucher-type checkbox is offered though.
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.CanTrackAdditionalCosts);
        Assert.False(entry.TrackAdditionalCosts);
        Assert.False(entry.ShowAdditionalCosts);

        // Turn tracking on → the area shows.
        entry.TrackAdditionalCosts = true;
        Assert.True(entry.ShowAdditionalCosts);

        // Toggling item-invoice back off hides the area again (byte-unchanged untracked screen, ER-13).
        k.Vm.ToggleItemInvoice();
        Assert.False(entry.ShowAdditionalCosts);
    }

    [Fact]
    public void A_sales_invoice_never_shows_the_additional_cost_area()
    {
        var k = NewKit("Addl Sales Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        Assert.True(entry.IsItemInvoice);
        Assert.False(entry.IsPurchaseInvoice);
        Assert.False(entry.CanTrackAdditionalCosts);
        Assert.False(entry.ShowAdditionalCosts);
    }

    // ---------------------------------------------------------------- (2) ER-4 screen == engine landed rate

    [Fact]
    public void Editing_additional_cost_stamps_the_engine_landed_rate_on_every_item_line()
    {
        var k = NewKit("Addl Landed Co");
        var entry = OpenTrackedPurchaseInvoice(k);

        // Two lines: A 5 @ 10,000; B 15 @ 1,000 (so by-Qty and by-Value diverge — a single item can't tell them apart).
        FillItemLine(entry.InventoryLines[0], entry, k.ItemAId, k.MainGodownId, 5m, "10000.00");
        entry.AddInventoryLine();
        FillItemLine(entry.InventoryLines[1], entry, k.ItemBId, k.MainGodownId, 15m, "1000.00");

        // Additional freight ₹1,500 apportioned BY QUANTITY → flat ₹75/unit → A landed 10,075.00, B landed 1,075.00.
        entry.AdditionalCosts[0].SelectedLedger = entry.AdditionalCostLedgers.Single(l => l.Id == k.FreightByQtyId);
        entry.AdditionalCosts[0].AmountText = "1500.00";

        // Party total = items (65,000) + additional (1,500) = 66,500.
        Assert.Equal("1,500.00", entry.AdditionalCostTotalText);
        Assert.Equal(IndianFormat.AmountAlways(66500m), entry.PartyTotalText);

        // Screen == engine: build the SAME throwaway voucher the engine consumes and compare landed rates (ER-4).
        var landed = EngineLanded(k, new[] { (k.ItemAId, 5m, 10000m), (k.ItemBId, 15m, 1000m) },
            new[] { (k.FreightByQtyId, 1500m) });

        Assert.True(entry.InventoryLines[0].ShowLanded);
        Assert.True(entry.InventoryLines[1].ShowLanded);
        Assert.Equal(IndianFormat.AmountAlways(landed[0].LandedUnitRate), entry.InventoryLines[0].LandedRateText);
        Assert.Equal(IndianFormat.AmountAlways(landed[1].LandedUnitRate), entry.InventoryLines[1].LandedRateText);
        Assert.Equal(IndianFormat.AmountAlways(10075m), entry.InventoryLines[0].LandedRateText);
        Assert.Equal(IndianFormat.AmountAlways(1075m), entry.InventoryLines[1].LandedRateText);
    }

    [Fact]
    public void By_value_apportionment_gives_different_landed_rates_than_by_quantity()
    {
        var k = NewKit("Addl ByValue Co");
        var entry = OpenTrackedPurchaseInvoice(k);

        FillItemLine(entry.InventoryLines[0], entry, k.ItemAId, k.MainGodownId, 5m, "10000.00");
        entry.AddInventoryLine();
        FillItemLine(entry.InventoryLines[1], entry, k.ItemBId, k.MainGodownId, 15m, "1000.00");

        // BY VALUE: A absorbs 1,500 × 50,000/65,000; dearer line A takes more than the flat by-qty share.
        entry.AdditionalCosts[0].SelectedLedger = entry.AdditionalCostLedgers.Single(l => l.Id == k.FreightByValueId);
        entry.AdditionalCosts[0].AmountText = "1500.00";

        var landed = EngineLanded(k, new[] { (k.ItemAId, 5m, 10000m), (k.ItemBId, 15m, 1000m) },
            new[] { (k.FreightByValueId, 1500m) });

        Assert.Equal(IndianFormat.AmountAlways(landed[0].LandedUnitRate), entry.InventoryLines[0].LandedRateText);
        Assert.Equal(IndianFormat.AmountAlways(landed[1].LandedUnitRate), entry.InventoryLines[1].LandedRateText);
        // By value, A's landed rate exceeds the by-qty 10,075 (it absorbs proportionally more).
        Assert.True(landed[0].LandedUnitRate > 10075m);
    }

    // ---------------------------------------------------------------- (3) RQ-19 — plain ledger is not an additional-cost ledger

    [Fact]
    public void A_plain_direct_expenses_ledger_is_not_an_additional_cost_ledger()
    {
        var k = NewKit("Addl RQ19 Co");
        var entry = OpenTrackedPurchaseInvoice(k);

        // The picker offers only method-carrying ledgers; the plain freight ledger is excluded (RQ-19).
        Assert.Contains(entry.AdditionalCostLedgers, l => l.Id == k.FreightByQtyId);
        Assert.Contains(entry.AdditionalCostLedgers, l => l.Id == k.FreightByValueId);
        Assert.DoesNotContain(entry.AdditionalCostLedgers, l => l.Id == k.PlainFreightId);

        // With one item and NO additional cost entered, no landed rate is stamped.
        FillItemLine(entry.InventoryLines[0], entry, k.ItemAId, k.MainGodownId, 5m, "10000.00");
        Assert.False(entry.InventoryLines[0].ShowLanded);
    }

    [Fact]
    public void Accepting_a_tracked_purchase_posts_the_additional_cost_leg_and_raises_the_party_total()
    {
        var k = NewKit("Addl Post Co");
        var entry = OpenTrackedPurchaseInvoice(k);

        FillItemLine(entry.InventoryLines[0], entry, k.ItemAId, k.MainGodownId, 5m, "10000.00");
        entry.AdditionalCosts[0].SelectedLedger = entry.AdditionalCostLedgers.Single(l => l.Id == k.FreightByQtyId);
        entry.AdditionalCosts[0].AmountText = "1500.00";

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = reloaded.Vouchers.Single(v => v.TypeId == type.Id);

        // Dr Purchases 50,000 + Dr Freight 1,500 = Cr Supplier 51,500 (balanced; the freight hits P&L, not swallowed).
        Assert.Equal(51500m, posted.TotalDebit.Amount);
        Assert.Equal(51500m, posted.TotalCredit.Amount);
        Assert.Contains(posted.Lines, l => l.LedgerId == k.FreightByQtyId && l.Side == DrCr.Debit && l.Amount.Amount == 1500m);
        Assert.Equal(51500m, posted.Lines.Single(l => l.LedgerId == k.SupplierId).Amount.Amount);
    }

    // ---------------------------------------------------------------- (4) ledger-master method field gating + persistence

    [Fact]
    public void Method_of_appropriation_field_is_gated_to_direct_expenses_and_the_f12_config()
    {
        var k = NewKit("Ledger Method Co");
        var master = new LedgerMasterViewModel(k.Vm.Company!, _storage, onChanged: () => { });

        // Default: a party group, config off → field hidden.
        master.SelectedGroup = master.Groups.Single(g => g.Name.Equals("Direct Expenses", StringComparison.OrdinalIgnoreCase));
        Assert.True(master.IsDirectExpensesGroup);
        Assert.False(master.ShowConfiguration);
        Assert.False(master.ShowAppropriation);         // config still off

        // F12 turns the ledger-screen configuration on → the method field shows (group is Direct Expenses).
        master.ToggleConfiguration();
        Assert.True(master.ShowConfiguration);
        Assert.True(master.ShowAppropriation);

        // Switch to a non-Direct-Expenses group → hidden again even with config on.
        master.SelectedGroup = master.Groups.Single(g => g.Name.Equals("Sundry Debtors", StringComparison.OrdinalIgnoreCase));
        Assert.False(master.IsDirectExpensesGroup);
        Assert.False(master.ShowAppropriation);
    }

    [Fact]
    public void Creating_a_direct_expenses_ledger_with_a_method_persists_it_as_an_additional_cost_ledger()
    {
        var k = NewKit("Ledger Persist Co");
        var master = new LedgerMasterViewModel(k.Vm.Company!, _storage, onChanged: () => { });

        master.SelectedGroup = master.Groups.Single(g => g.Name.Equals("Direct Expenses", StringComparison.OrdinalIgnoreCase));
        master.ToggleConfiguration();
        master.Name = "Packing Charges";
        master.SelectedMethod = master.MethodChoices.Single(m => m.Value == MethodOfAppropriation.ByValue);
        Assert.True(master.Create());

        var reloaded = Reload(k.CompanyName);
        var created = reloaded.Ledgers.Single(l => l.Name == "Packing Charges");
        Assert.Equal(MethodOfAppropriation.ByValue, created.MethodOfAppropriation);
        Assert.True(created.IsAdditionalCostLedger);
    }

    [Fact]
    public void A_direct_expenses_ledger_created_without_the_config_stays_a_plain_expense()
    {
        var k = NewKit("Ledger Plain Co");
        var master = new LedgerMasterViewModel(k.Vm.Company!, _storage, onChanged: () => { });

        // Config off ⇒ even a chosen method is not captured (the field wasn't shown) — the ledger stays plain (RQ-19).
        master.SelectedGroup = master.Groups.Single(g => g.Name.Equals("Direct Expenses", StringComparison.OrdinalIgnoreCase));
        master.SelectedMethod = master.MethodChoices.Single(m => m.Value == MethodOfAppropriation.ByQuantity);
        master.Name = "Carriage Inward";
        Assert.False(master.ShowAppropriation);
        Assert.True(master.Create());

        var reloaded = Reload(k.CompanyName);
        var created = reloaded.Ledgers.Single(l => l.Name == "Carriage Inward");
        Assert.Null(created.MethodOfAppropriation);
        Assert.False(created.IsAdditionalCostLedger);
    }

    // ---------------------------------------------------------------- engine helper (the authority for ER-4)

    /// <summary>Runs the SAME apportionment engine the screen uses, over a throwaway Purchase voucher of the
    /// company's Purchase type, so a test can assert screen == engine (ER-4).</summary>
    private static System.Collections.Generic.IReadOnlyList<AdditionalCostApportionment.LandedLine> EngineLanded(
        Kit k, (Guid itemId, decimal qty, decimal rate)[] items, (Guid ledgerId, decimal amount)[] costs)
    {
        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var invLines = items.Select(i => new VoucherInventoryLine(
            i.itemId, k.MainGodownId, i.qty, new Money(i.rate), StockDirection.Inward, null)).ToList();
        var costLines = costs.Select(x => new EntryLine(x.ledgerId, new Money(x.amount), DrCr.Debit)).ToList();
        var v = new Voucher(Guid.NewGuid(), type.Id, AsOf(c), costLines, inventoryLines: invLines);
        return AdditionalCostApportionment.ForPurchase(c, v);
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
