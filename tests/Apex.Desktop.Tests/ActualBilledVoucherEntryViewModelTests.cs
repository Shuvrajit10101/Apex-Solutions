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
/// Desktop VM coverage for Phase 6 slice 4 — <b>separate Actual/Billed quantity columns</b> (company/F11 flag,
/// Book pp.145–147; RQ-22..RQ-25) and <b>zero-valued transactions</b> (voucher-type flag, Book pp.142–143;
/// RQ-21) on the Purchase (F9) / Sales (F8) item-invoice screen, driven through the real shell + entry VM
/// against a throwaway <c>.db</c> (no UI toolkit). Proves: the Billed column shows only when the company flag
/// is on (Sales/Purchase only); Billed drives the value/GST while Actual drives the displayed/posted stock; a
/// ₹0 free-goods line is accepted only when the voucher type allows zero-valued transactions (else blocked);
/// and every new header/label is brand-neutral (no "Tally").
/// </summary>
public sealed class ActualBilledVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ActualBilledVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexActualBilledTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid RiceId { get; init; }
        public required Guid EarphoneId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    /// <summary>
    /// A seeded company with two items (Basmati Rice + Samsung Earphone, both starting at
    /// <paramref name="openingQty"/> units in Main Location), plus Purchases/Sales ledgers, a supplier and a
    /// customer. When <paramref name="gst"/> is true the company is GST-enabled (home Maharashtra 27), Rice is
    /// taxable @ 18%, and both parties carry a Maharashtra GSTIN (intra-state).
    /// </summary>
    private Kit NewKit(string companyName, decimal openingQty = 100m, bool gst = false)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        if (gst)
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var kg = masters.CreateSimpleUnit("kg", "Kilograms");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var rice = masters.CreateStockItem("Basmati Rice", grp.Id, kg.Id);
        var earphone = masters.CreateStockItem("Samsung Earphone", grp.Id, nos.Id);
        if (gst)
            rice.Gst = new StockItemGstDetails { HsnSac = "100630", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        if (openingQty > 0m)
        {
            masters.AddOpeningBalance(rice.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(70m));
            masters.AddOpeningBalance(earphone.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(500m));
        }

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");
        if (gst)
        {
            supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
            customer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        }

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            RiceId = rice.Id,
            EarphoneId = earphone.Id,
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

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    /// <summary>Fills item line <paramref name="index"/> (adding rows as needed) with (item, godown, actual, billed, rate).</summary>
    private static InventoryVoucherLineViewModel FillItemLine(
        VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal actual, string rate,
        string? billed = null, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = actual.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (billed is not null) line.BilledQuantityText = billed;
        line.RateText = rate;
        return line;
    }

    // ================================================================ (1) column gating

    [Fact]
    public void Billed_column_hidden_when_company_flag_off_shown_when_on()
    {
        var k = NewKit("AB Gating Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        // Flag off (default): no Billed column, plain "Quantity" header, and each line hides its Billed field.
        Assert.False(entry.UseSeparateActualBilledQuantity);
        Assert.False(entry.ShowActualBilledColumns);
        Assert.Equal("Quantity", entry.QuantityHeader);
        Assert.All(entry.InventoryLines, l => Assert.False(l.ShowActualBilled));

        // Turn the company flag on → Billed column appears, header relabels, existing + new lines show Billed.
        entry.UseSeparateActualBilledQuantity = true;
        Assert.True(entry.ShowActualBilledColumns);
        Assert.Equal("Qty (Actual)", entry.QuantityHeader);
        Assert.All(entry.InventoryLines, l => Assert.True(l.ShowActualBilled));
        var added = entry.AddInventoryLine();
        Assert.True(added.ShowActualBilled);

        // The flag persists on the company.
        Assert.True(Reload(k.CompanyName).UseSeparateActualBilledQuantity);
    }

    [Fact]
    public void Ab_checkbox_is_available_on_sales_and_purchase_but_not_other_types()
    {
        var k = NewKit("AB Availability Co");

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        Assert.True(k.Vm.VoucherEntry!.CanUseSeparateActualBilled);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        Assert.True(k.Vm.VoucherEntry!.CanUseSeparateActualBilled);

        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        Assert.False(k.Vm.VoucherEntry!.CanUseSeparateActualBilled);
    }

    // ================================================================ (2) Billed drives value; Actual drives stock

    [Fact]
    public void Billed_drives_value_while_actual_drives_stock_60_actual_50_billed()
    {
        var k = NewKit("AB Value Co", openingQty: 0m);   // start empty so closing stock is the single lot
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.SupplierId);

        // Receive 60 kg, billed for 50 kg @ ₹70 (Book pp.145–147 worked example).
        FillItemLine(entry, k.RiceId, k.MainGodownId, actual: 60m, rate: "70.00", billed: "50");

        // Value (and thus the derived legs) = Billed × Rate = 50 × 70 = ₹3,500 — NOT 60 × 70.
        Assert.Equal("3,500.00", entry.ItemsTotalText);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // POSTED + PERSISTED: on-hand up by the ACTUAL 60 kg; accounting/value leg = ₹3,500 (billed).
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(60m, OnHand(reloaded, k.RiceId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.Equal(3500m, rPosted.InventoryLinesValue.Amount);
        Assert.Equal(3500m, rPosted.TotalDebit.Amount);
        Assert.Equal(3500m, rPosted.TotalCredit.Amount);

        // The persisted stock line carries the Actual/Billed split (Actual 60 stock, Billed 50 value).
        var line = rPosted.InventoryLines.Single();
        Assert.Equal(60m, line.Quantity);
        Assert.Equal(50m, line.BilledQuantity);
        Assert.Equal(3500m, line.Value.Amount);
    }

    [Fact]
    public void Billed_greater_than_actual_is_accepted_rq25()
    {
        var k = NewKit("AB Overbill Co", openingQty: 0m);
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.SupplierId);

        // Rare quality shortfall billed in full: receive 50, billed 60 @ ₹70 → value ₹4,200, stock +50.
        FillItemLine(entry, k.RiceId, k.MainGodownId, actual: 50m, rate: "70.00", billed: "60");
        Assert.Equal("4,200.00", entry.ItemsTotalText);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        Assert.Equal(50m, OnHand(reloaded, k.RiceId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        Assert.Equal(4200m, reloaded.Vouchers.Single(v => v.TypeId == rType.Id).InventoryLinesValue.Amount);
    }

    [Fact]
    public void With_flag_off_billed_equals_actual_and_value_is_unchanged()
    {
        var k = NewKit("AB Off Co", openingQty: 0m);
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.SupplierId);

        // Flag off: the line VM ignores any stray Billed text and value = Actual × Rate = 60 × 70 = ₹4,200.
        var line = FillItemLine(entry, k.RiceId, k.MainGodownId, actual: 60m, rate: "70.00", billed: "50");
        Assert.False(line.ShowActualBilled);
        Assert.Equal(60m, line.ParsedBilledQuantity);       // Billed ≡ Actual when the feature is off
        Assert.Equal("4,200.00", entry.ItemsTotalText);
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var rLine = reloaded.Vouchers.Single(v => v.TypeId == rType.Id).InventoryLines.Single();
        Assert.Equal(60m, rLine.Quantity);
        Assert.Equal(60m, rLine.BilledQuantity);            // billed defaults to actual
    }

    // ================================================================ (3) zero-valued acceptance

    [Fact]
    public void Zero_valued_line_blocked_without_the_flag()
    {
        var k = NewKit("ZV Off Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.SupplierId);

        Assert.False(entry.AllowZeroValued);                // default off
        FillItemLine(entry, k.EarphoneId, k.MainGodownId, actual: 3m, rate: "0");

        // A ₹0 line on a normal Purchase type is rejected (fat-finger guard stays).
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));

        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        Assert.DoesNotContain(reloaded.Vouchers, v => v.TypeId == rType.Id);
    }

    [Fact]
    public void Zero_valued_free_goods_accepted_when_flag_on_moves_stock_posts_zero()
    {
        var k = NewKit("ZV On Co", openingQty: 0m);
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.AllowZeroValued = true;                       // enable zero-valued on this Purchase type
        SelectParty(entry, k.SupplierId);

        // Buy 3 earphones @ ₹500 and get 3 rice-bags free (Book p.143 worked shape).
        FillItemLine(entry, k.EarphoneId, k.MainGodownId, actual: 3m, rate: "500.00", index: 0);
        FillItemLine(entry, k.RiceId, k.MainGodownId, actual: 3m, rate: "0", index: 1);

        // Total = the paid line only (₹1,500); the free line contributes ₹0.
        Assert.Equal("1,500.00", entry.ItemsTotalText);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // Both items moved stock (+3 each); the free line posts ₹0 value.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(3m, OnHand(reloaded, k.EarphoneId, k.MainGodownId));
        Assert.Equal(3m, OnHand(reloaded, k.RiceId, k.MainGodownId));
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.Equal(1500m, rPosted.InventoryLinesValue.Amount);
        var freeLine = rPosted.InventoryLines.Single(l => l.StockItemId == k.RiceId);
        Assert.Equal(0m, freeLine.Value.Amount);
        Assert.Equal(3m, freeLine.Quantity);

        // The voucher-type flag persisted.
        Assert.True(reloaded.VoucherTypes.Single(t => t.Id == rType.Id).AllowZeroValuedTransactions);
    }

    [Fact]
    public void Zero_valued_flag_only_offered_on_sales_and_purchase()
    {
        var k = NewKit("ZV Availability Co");

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        Assert.True(k.Vm.VoucherEntry!.CanAllowZeroValued);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        Assert.True(k.Vm.VoucherEntry!.CanAllowZeroValued);

        k.Vm.OpenVoucher(VoucherBaseType.Journal);
        Assert.False(k.Vm.VoucherEntry!.CanAllowZeroValued);
    }

    // ================================================================ (4) GST computes off Billed

    [Fact]
    public void Gst_taxable_and_tax_compute_off_billed_not_actual()
    {
        var k = NewKit("AB Gst Co", openingQty: 0m, gst: true);
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsGstInvoice);
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.SupplierId);

        // Receive 60 kg Rice, billed 50 kg @ ₹100 → taxable = 50 × 100 = ₹5,000 (NOT 60 × 100 = ₹6,000).
        FillItemLine(entry, k.RiceId, k.MainGodownId, actual: 60m, rate: "100.00", billed: "50");

        // Intra-state 18% on ₹5,000 → CGST 450 + SGST 450; party total = 5,000 + 900 = ₹5,900.
        Assert.Equal("5,000.00", entry.ItemsTotalText);
        Assert.Equal("450.00", entry.GstCgstText);
        Assert.Equal("450.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("5,900.00", entry.PartyTotalText);

        Assert.True(entry.Accept());

        // Stock still moved by the ACTUAL 60 kg; the tax posted off the billed value.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(60m, OnHand(reloaded, k.RiceId, k.MainGodownId));
    }

    // ================================================================ (5) de-brand — no "Tally" in new UI

    [Fact]
    public void New_headers_and_labels_are_brand_neutral()
    {
        var k = NewKit("Debrand Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        Assert.DoesNotContain("Tally", entry.QuantityHeader, StringComparison.OrdinalIgnoreCase);
        entry.UseSeparateActualBilledQuantity = true;
        Assert.DoesNotContain("Tally", entry.QuantityHeader, StringComparison.OrdinalIgnoreCase);

        // The new axaml flag labels + Billed column header must never render the "Tally" brand (ER-8).
        var axaml = File.ReadAllText(FindRepoFile("src/Apex.Desktop/Views/MainWindow.axaml"));
        var region = SliceAround(axaml, "Use separate Actual", "Allow zero-valued transactions", "Qty (Billed)");
        Assert.DoesNotContain("Tally", region, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Concatenates the axaml lines that contain any of the given markers (the new slice-4 UI region).</summary>
    private static string SliceAround(string text, params string[] markers)
    {
        var lines = text.Split('\n');
        return string.Join("\n", lines.Where(l => markers.Any(m => l.Contains(m, StringComparison.Ordinal))));
    }

    /// <summary>Walks up from the test bin dir to the repo root and resolves a repo-relative path.</summary>
    private static string FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) throw new FileNotFoundException(relative);
        return Path.Combine(dir, relative);
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
