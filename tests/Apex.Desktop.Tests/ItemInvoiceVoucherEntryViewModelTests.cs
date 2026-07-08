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
/// End-to-end coverage for the <b>item-invoice mode</b> (Ctrl+I) on the Purchase (F9) / Sales (F8)
/// accounting voucher-entry screen (catalog §10; slice 3.4c), driven through the real shell + entry
/// view model against a seeded company on a throwaway <c>.db</c>. Proves: an item-invoice Purchase posts
/// BOTH the accounting legs (Dr Purchases / Cr Supplier = Σ items) AND stock (on-hand increases), and
/// persists; an item-invoice Sales decreases stock but is BLOCKED (Message, no crash, nothing persisted)
/// when it would drive on-hand negative; a plain Purchase/Sales with no items still works exactly as
/// before; and pre-validation (zero/negative rate, no party, blank line) surfaces a Message. Reuses the
/// headless-safe seeding pattern (no UI toolkit).
/// </summary>
public sealed class ItemInvoiceVoucherEntryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ItemInvoiceVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexItemInvoiceTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    /// <summary>
    /// A seeded company with one "Widget" (Nos) item + <paramref name="openingQty"/> units in Main Location,
    /// plus a Purchases ledger (Purchase Accounts), a Sales ledger (Sales Accounts), a supplier (Sundry
    /// Creditors) and a customer (Sundry Debtors) — the ledgers an item-invoice needs.
    /// </summary>
    private Kit NewKit(string companyName, decimal openingQty = 100m)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;

        // Inventory masters: one Widget item with opening stock.
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        if (openingQty > 0m)
            masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(100m));

        // Accounting ledgers the two auto-derived legs post to.
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            PurchasesLedgerId = purchases.Id,
            SalesLedgerId = sales.Id,
            SupplierId = supplier.Id,
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

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal OnHand(Company c, Guid itemId, Guid godownId) =>
        new InventoryLedger(c).OnHand(itemId, godownId, AsOf(c));

    /// <summary>Fills the entry VM's first item line with (item, godown, qty, rate).</summary>
    private static void FillItemLine(VoucherEntryViewModel entry, Kit k, decimal qty, string rate)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    // ---------------------------------------------------------------- (1) Purchase item-invoice

    [Fact]
    public void Item_invoice_purchase_posts_accounts_and_stock_and_persists()
    {
        var k = NewKit("Item Invoice Purchase Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.VoucherEntry!;

        // Turn on item-invoice mode (Ctrl+I).
        Assert.True(entry.CanBeItemInvoice);
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.IsPurchaseInvoice);

        // Supplier + Purchases value ledger auto-defaulted; select the supplier explicitly.
        SelectParty(entry, k.SupplierId);
        Assert.Equal(k.PurchasesLedgerId, entry.SelectedStockLedger!.Id);

        // 10 Widgets @ 50.00 = 500.00.
        FillItemLine(entry, k, 10m, "50.00");
        Assert.Equal("500.00", entry.ItemsTotalText);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
        Assert.True(entry.SavedNumber >= 1);
        Assert.Equal(Screen.Gateway, k.Vm.CurrentScreen);

        // POSTED: on-hand up by 10; the accounting legs are Dr Purchases 500 / Cr Supplier 500.
        Assert.Equal(before + 10m, OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId));
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(500m, posted.TotalDebit.Amount);
        Assert.Equal(500m, posted.TotalCredit.Amount);
        Assert.True(posted.HasInventoryLines);
        var drLine = posted.Lines.Single(l => l.Side == DrCr.Debit);
        var crLine = posted.Lines.Single(l => l.Side == DrCr.Credit);
        Assert.Equal(k.PurchasesLedgerId, drLine.LedgerId);
        Assert.Equal(k.SupplierId, crLine.LedgerId);

        // PERSISTED: reload and both the accounting legs and the stock movement survive.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before + 10m, OnHand(reloaded, k.ItemId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.True(rPosted.HasInventoryLines);
        Assert.Equal(500m, rPosted.InventoryLinesValue.Amount);
    }

    // ---------------------------------------------------------------- (2) Sales item-invoice (happy + blocked)

    [Fact]
    public void Item_invoice_sales_decreases_stock_and_persists()
    {
        var k = NewKit("Item Invoice Sales Co");                 // 100 on hand
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.False(entry.IsPurchaseInvoice);

        SelectParty(entry, k.CustomerId);
        Assert.Equal(k.SalesLedgerId, entry.SelectedStockLedger!.Id);

        // Sell 30 Widgets @ 80.00 = 2400.00.
        FillItemLine(entry, k, 30m, "80.00");
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // POSTED: on-hand down by 30; Dr Customer 2400 / Cr Sales 2400.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before - 30m, OnHand(reloaded, k.ItemId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.Equal(k.CustomerId, rPosted.Lines.Single(l => l.Side == DrCr.Debit).LedgerId);
        Assert.Equal(k.SalesLedgerId, rPosted.Lines.Single(l => l.Side == DrCr.Credit).LedgerId);
    }

    [Fact]
    public void Item_invoice_sales_that_would_drive_stock_negative_is_blocked_and_nothing_persists()
    {
        var k = NewKit("Item Invoice Oversell Co", openingQty: 10m);   // only 10 on hand
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);

        // Sell 25 — more than the 10 on hand.
        FillItemLine(entry, k, 25m, "80.00");
        Assert.True(entry.CanAccept);                       // shape is valid; the engine is the authority

        // Accept returns false and surfaces the engine's no-negative message — never crashes.
        Assert.False(entry.Accept());
        Assert.Contains("negative", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);

        // Nothing persisted — no accounting leg, no stock movement.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before, OnHand(reloaded, k.ItemId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        Assert.DoesNotContain(reloaded.Vouchers, v => v.TypeId == rType.Id);
    }

    // ---------------------------------------------------------------- (3) plain mode unchanged

    [Fact]
    public void Plain_purchase_without_item_invoice_still_posts_two_legs()
    {
        var k = NewKit("Plain Purchase Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        Assert.False(entry.IsItemInvoice);                  // defaults to plain accounting mode

        // Hand-balance a plain Purchase: Dr Purchases 750 / Cr Supplier 750.
        entry.Lines[0].SelectedLedger = k.Vm.Company!.FindLedger(k.PurchasesLedgerId);
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "750";
        entry.Lines[1].SelectedLedger = k.Vm.Company!.FindLedger(k.SupplierId);
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "750";

        Assert.True(entry.IsBalanced);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // POSTED as a plain accounting voucher — no stock effect.
        Assert.Equal(before, OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId));
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.False(posted.HasInventoryLines);
        Assert.Equal(750m, posted.TotalDebit.Amount);
    }

    [Fact]
    public void Plain_sales_without_item_invoice_still_posts_and_moves_no_stock()
    {
        var k = NewKit("Plain Sales Co");
        var before = OnHand(k.Vm.Company!, k.ItemId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        Assert.False(entry.IsItemInvoice);

        entry.Lines[0].SelectedLedger = k.Vm.Company!.FindLedger(k.CustomerId);
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "1200";
        entry.Lines[1].SelectedLedger = k.Vm.Company!.FindLedger(k.SalesLedgerId);
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "1200";

        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(before, OnHand(reloaded, k.ItemId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.False(rPosted.HasInventoryLines);
    }

    // ---------------------------------------------------------------- (4) mode is Purchase/Sales-only

    [Theory]
    [InlineData(VoucherBaseType.Payment)]
    [InlineData(VoucherBaseType.Receipt)]
    [InlineData(VoucherBaseType.Journal)]
    [InlineData(VoucherBaseType.Contra)]
    public void Non_purchase_sales_vouchers_cannot_be_item_invoices(VoucherBaseType baseType)
    {
        var k = NewKit($"No Invoice {baseType} Co");
        k.Vm.OpenVoucher(baseType);
        var entry = k.Vm.VoucherEntry!;

        Assert.False(entry.CanBeItemInvoice);
        k.Vm.ToggleItemInvoice();                           // no-op on a non-invoice type
        Assert.False(entry.IsItemInvoice);
    }

    // ---------------------------------------------------------------- (5) pre-validation

    [Fact]
    public void Zero_or_negative_rate_keeps_the_item_line_incomplete_and_blocks_accept()
    {
        var k = NewKit("Zero Rate Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.SupplierId);

        // Zero rate → the line is not "complete" (rate must be > 0), so Accept is disabled.
        FillItemLine(entry, k, 10m, "0.00");
        Assert.False(entry.CanAccept);

        // A negative rate is likewise rejected (the underlying line treats < 0 as invalid).
        entry.InventoryLines[0].RateText = "-5.00";
        entry.RecalculateItemInvoice();
        Assert.False(entry.CanAccept);

        // Force an accept attempt with an all-blank grid + no rate → friendly Message, no crash, nothing posts.
        entry.InventoryLines[0].RateText = "0";
        entry.RecalculateItemInvoice();
        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        Assert.DoesNotContain(reloaded.Vouchers, v => v.TypeId == rType.Id);
    }

    [Fact]
    public void No_party_selected_blocks_accept_with_a_message()
    {
        var k = NewKit("No Party Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        // Explicitly clear the party (default is the "(none)" entry).
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger is null);
        FillItemLine(entry, k, 5m, "20.00");

        Assert.False(entry.CanAccept);                      // no party ⇒ Accept disabled
        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
    }

    [Fact]
    public void A_blank_item_grid_never_enables_accept()
    {
        var k = NewKit("Blank Grid Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.SupplierId);

        // No item lines filled → Accept disabled and a no-op.
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
    }

    // ---------------------------------------------------------------- (6) toggle round-trips

    [Fact]
    public void Toggling_item_invoice_off_restores_the_plain_dr_cr_accept_gate()
    {
        var k = NewKit("Toggle Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;

        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        k.Vm.ToggleItemInvoice();
        Assert.False(entry.IsItemInvoice);

        // Back in plain mode: a balanced Dr/Cr pair enables Accept as before.
        entry.Lines[0].SelectedLedger = k.Vm.Company!.FindLedger(k.PurchasesLedgerId);
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "100";
        entry.Lines[1].SelectedLedger = k.Vm.Company!.FindLedger(k.SupplierId);
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "100";
        Assert.True(entry.CanAccept);
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
