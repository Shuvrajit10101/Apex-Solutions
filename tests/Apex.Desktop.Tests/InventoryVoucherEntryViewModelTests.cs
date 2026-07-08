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
/// End-to-end coverage for the eight stock/order voucher-entry screens (catalog §10;
/// phase3-inventory-requirements RQ-8..RQ-15) driven through the real shell + entry view models against a
/// seeded company on a throwaway <c>.db</c>. For each type it opens the screen, fills lines, Accepts, and
/// asserts the <see cref="InventoryVoucher"/> POSTED + PERSISTED (reload the company) and that on-hand moved
/// as expected (or not, for PO/SO). Plus pre-validation: over-delivery surfaces the no-negative Message
/// without throwing; an unbalanced Stock Journal is blocked; blank/invalid lines are handled. No UI toolkit.
/// </summary>
public sealed class InventoryVoucherEntryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InventoryVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInvVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
    }

    /// <summary>A seeded company with one "Widget" (Nos) item and 100 units opening in Main Location.</summary>
    private Kit NewKit(string companyName, decimal openingQty = 100m)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        if (openingQty > 0m)
            masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(100m));
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
        };
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal OnHand(Company c, Guid itemId, Guid godownId) =>
        new InventoryLedger(c).OnHand(itemId, godownId, AsOf(c));

    /// <summary>Fills the entry VM's first primary line with (item, godown, qty[, rate]).</summary>
    private static void FillLine(InventoryVoucherEntryViewModel entry, Kit k, decimal qty, string? rate = null)
    {
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (rate is not null) line.RateText = rate;
    }

    // ---------------------------------------------------------------- (1) Purchase Order / (2) Sales Order

    [Theory]
    [InlineData(VoucherBaseType.PurchaseOrder)]
    [InlineData(VoucherBaseType.SalesOrder)]
    public void Order_voucher_posts_and_persists_but_does_not_move_stock(VoucherBaseType baseType)
    {
        var k = NewKit($"Order {baseType} Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenInventoryVoucher(baseType);
        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.True(entry.IsOrder);

        FillLine(entry, k, 25m, rate: "12.50");
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
        Assert.True(entry.SavedNumber >= 1);
        Assert.Equal(Screen.Gateway, k.Vm.CurrentScreen);

        // POSTED as an order (no stock effect) — on-hand unchanged.
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == baseType);
        Assert.Single(k.Vm.Company!.InventoryVouchers, v => v.TypeId == type.Id);
        Assert.Equal(before, OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId));

        // PERSISTED: reload and the order (with its order line) survives.
        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == baseType);
        var rVoucher = reloaded.InventoryVouchers.Single(v => v.TypeId == rType.Id);
        Assert.Single(rVoucher.OrderLines);
        Assert.Equal(25m, rVoucher.OrderLines[0].Quantity);
        Assert.Equal(before, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- (3) Receipt Note (GRN) — inward

    [Fact]
    public void Receipt_note_moves_stock_inward_and_persists()
    {
        var k = NewKit("GRN Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.Contains("Inward", entry.DirectionHint);
        FillLine(entry, k, 40m, rate: "105.00");
        entry.Lines[0].BatchLabel = "LOT-A";
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before + 40m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- (4) Delivery Note — outward

    [Fact]
    public void Delivery_note_moves_stock_outward_and_persists()
    {
        var k = NewKit("Delivery Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.DeliveryNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.Contains("Outward", entry.DirectionHint);
        FillLine(entry, k, 30m);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before - 30m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- (5) Rejection In — inward

    [Fact]
    public void Rejection_in_moves_stock_inward_and_persists()
    {
        var k = NewKit("Rejection In Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.RejectionIn);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 5m);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before + 5m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- (6) Rejection Out — outward

    [Fact]
    public void Rejection_out_moves_stock_outward_and_persists()
    {
        var k = NewKit("Rejection Out Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.RejectionOut);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 7m);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before - 7m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- (7) Stock Journal — transfer

    [Fact]
    public void Stock_journal_transfers_between_godowns_when_balanced_and_persists()
    {
        var k = NewKit("Stock Journal Co");
        var masters = new InventoryService(k.Vm.Company!);
        var wh2 = masters.CreateGodown("Warehouse 2");
        _storage.Save(k.Vm.Company!);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.StockJournal);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.True(entry.IsStockJournal);
        Assert.Single(entry.DestinationLines);   // seeded with one source + one destination line

        // Source: 20 out of Main Location; Destination: 20 into Warehouse 2 (balanced).
        FillLine(entry, k, 20m);
        var dest = entry.DestinationLines[0];
        dest.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        dest.SelectedGodown = entry.Godowns.Single(g => g.Id == wh2.Id);
        dest.QuantityText = "20";
        entry.Recalculate();

        Assert.True(entry.IsBalanced);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(80m, OnHand(reloaded, k.ItemId, k.MainGodownId));   // 100 − 20
        Assert.Equal(20m, OnHand(reloaded, k.ItemId, wh2.Id));           // 0 + 20
    }

    [Fact]
    public void Unbalanced_stock_journal_is_blocked_and_nothing_persists()
    {
        var k = NewKit("Stock Journal Imbalance Co");
        var masters = new InventoryService(k.Vm.Company!);
        var wh2 = masters.CreateGodown("Warehouse 2");
        _storage.Save(k.Vm.Company!);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.StockJournal);
        var entry = k.Vm.InventoryVoucherEntry!;

        // Source 20 out, Destination 15 in — deliberately imbalanced.
        FillLine(entry, k, 20m);
        var dest = entry.DestinationLines[0];
        dest.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        dest.SelectedGodown = entry.Godowns.Single(g => g.Id == wh2.Id);
        dest.QuantityText = "15";
        entry.Recalculate();

        Assert.False(entry.IsBalanced);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());               // does not throw; surfaces a friendly message
        Assert.Contains("balance", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);

        // Nothing persisted.
        var reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.StockJournal);
        Assert.DoesNotContain(reloaded.InventoryVouchers, v => v.TypeId == type.Id);
    }

    // ---------------------------------------------------------------- (8) Physical Stock — counted qty

    [Fact]
    public void Physical_stock_sets_on_hand_to_the_counted_quantity_and_persists()
    {
        var k = NewKit("Physical Stock Co");
        Assert.Equal(100m, OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId));

        k.Vm.OpenInventoryVoucher(VoucherBaseType.PhysicalStock);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.True(entry.IsPhysicalStock);
        Assert.Equal("Counted Qty", entry.QuantityHeader);

        // Counted 90 → the new book quantity (an implicit −10 adjustment, DP-3).
        FillLine(entry, k, 90m);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(90m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    [Fact]
    public void Physical_stock_allows_a_zero_counted_quantity()
    {
        var k = NewKit("Physical Zero Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.PhysicalStock);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 0m);                     // counted 0 is valid (≥ 0)
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(0m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    // ---------------------------------------------------------------- pre-validation

    [Fact]
    public void Over_delivery_surfaces_the_no_negative_message_and_does_not_throw()
    {
        var k = NewKit("Over Delivery Co", openingQty: 10m);   // only 10 on hand

        k.Vm.OpenInventoryVoucher(VoucherBaseType.DeliveryNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 25m);                    // deliver 25 — would drive on-hand negative
        Assert.True(entry.CanAccept);               // shape is valid; the engine is the authority

        // Accept returns false and surfaces the engine's no-negative message — never crashes.
        Assert.False(entry.Accept());
        Assert.Contains("negative", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);

        // Nothing persisted; on-hand unchanged.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(10m, OnHand(reloaded, k.ItemId, k.MainGodownId));
    }

    [Fact]
    public void A_blank_line_never_enables_accept_and_a_half_filled_line_is_reported()
    {
        var k = NewKit("Blank Line Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;

        // Wholly blank → Accept disabled, and Accept() is a no-op returning false.
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());

        // Half-filled (item but no quantity) → still can't accept, and the message names the problem.
        entry.Lines[0].SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        entry.Recalculate();
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
    }

    [Fact]
    public void Rate_beyond_the_paisa_keeps_the_line_incomplete()
    {
        var k = NewKit("Rate Paisa Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 5m, rate: "10.005");     // sub-paisa rate → line stays incomplete
        Assert.False(entry.Lines[0].IsComplete);
        Assert.False(entry.CanAccept);
    }

    [Fact]
    public void Quantity_beyond_six_decimals_keeps_the_line_incomplete()
    {
        var k = NewKit("Qty Precision Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        FillLine(entry, k, 0m);
        entry.Lines[0].QuantityText = "1.1234567"; // 7 dp → beyond 6-dp precision
        entry.Recalculate();
        Assert.False(entry.Lines[0].IsComplete);
        Assert.False(entry.CanAccept);
    }

    // ---------------------------------------------------------------- nav / wiring

    [Fact]
    public void Each_inventory_voucher_type_opens_its_entry_screen()
    {
        foreach (var bt in new[]
                 {
                     VoucherBaseType.PurchaseOrder, VoucherBaseType.SalesOrder,
                     VoucherBaseType.ReceiptNote, VoucherBaseType.DeliveryNote,
                     VoucherBaseType.RejectionIn, VoucherBaseType.RejectionOut,
                     VoucherBaseType.StockJournal, VoucherBaseType.PhysicalStock,
                 })
        {
            var k = NewKit($"Open {bt} Co");
            k.Vm.OpenInventoryVoucher(bt);
            Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);
            Assert.NotNull(k.Vm.InventoryVoucherEntry);
            Assert.Equal(bt, k.Vm.InventoryVoucherEntry!.Type.BaseType);
            // Exactly one page column open, reachable through the GatewayColumn accessor.
            Assert.Equal(1, k.Vm.Columns.Count(c => c.IsPage));
            Assert.Same(k.Vm.InventoryVoucherEntry, k.Vm.Columns[^1].InventoryVoucher);
        }
    }

    [Fact]
    public void Inventory_voucher_groups_nest_under_transactions_vouchers()
    {
        var k = NewKit("Inv Nav Co");

        // Vouchers submenu exposes the two inventory groups.
        k.Vm.ShowVouchersMenu();
        var voucherLabels = k.Vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Contains("Order Vouchers", voucherLabels);
        Assert.Contains("Inventory Vouchers", voucherLabels);

        // Order Vouchers group → PO + SO pages.
        k.Vm.ShowOrderVouchersMenu();
        Assert.Equal(GatewayMenu.OrderVouchers, k.Vm.CurrentGatewayMenu);
        var orderLabels = k.Vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Equal(new[] { "Purchase Order", "Sales Order" }, orderLabels);

        // Inventory Vouchers group → the six stock-moving pages incl. Physical Stock (F10 menu path).
        k.Vm.ShowInventoryVouchersMenu();
        Assert.Equal(GatewayMenu.InventoryVouchers, k.Vm.CurrentGatewayMenu);
        var invLabels = k.Vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Equal(
            new[] { "Receipt Note", "Delivery Note", "Rejection In", "Rejection Out", "Stock Journal", "Physical Stock" },
            invLabels);

        // Activating a page item opens the entry screen (proves OpenPageOf routing matches the labels):
        // drive the highlight to "Stock Journal" via the public arrow API, then activate it.
        while (k.Vm.Menu[k.Vm.SelectedIndex].Label != "Stock Journal") k.Vm.MoveDown();
        k.Vm.ActivateSelected();
        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);
        Assert.Equal(VoucherBaseType.StockJournal, k.Vm.InventoryVoucherEntry!.Type.BaseType);
    }

    [Fact]
    public void Esc_cancels_an_inventory_voucher_back_to_the_vouchers_area()
    {
        var k = NewKit("Cancel Co");
        k.Vm.ShowInventoryVouchersMenu();
        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        Assert.Equal(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);

        k.Vm.CancelVoucher();                       // Alt+X path → BackFromPage
        Assert.NotEqual(Screen.InventoryVoucherEntry, k.Vm.CurrentScreen);
        Assert.Null(k.Vm.InventoryVoucherEntry);
    }

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
