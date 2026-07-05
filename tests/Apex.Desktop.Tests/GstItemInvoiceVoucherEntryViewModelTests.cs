using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for slice 4e — <b>GST on the item-invoice (Ctrl+I) Purchase/Sales screen</b>. When
/// the company has GST enabled, a Purchase/Sales item invoice created through <see cref="VoucherEntryViewModel"/>
/// must compute, display and POST CGST/SGST (intra) or IGST (inter) additively: the stock/value leg stays at
/// Σ taxable (tax excluded — the pairing invariant), the tax lines post to the correct Output/Input tax
/// ledgers carrying <see cref="GstLineTax"/> metadata (so the UI-created invoice flows into GSTR-1/3B/Tax
/// Analysis), and the party leg carries taxable + tax. Proves intra (CGST+SGST), inter (IGST), multi-rate
/// (per-rate split), B2C (no GSTIN, in-state ⇒ CGST+SGST), exempt (no tax), the derived-summary display, and
/// that a GST-off company is completely unchanged (Phase-3 behavior). Drives the real shell + entry VM over a
/// throwaway <c>.db</c> — no UI toolkit. All amounts worked by hand and reconciled to the paisa.
/// </summary>
public sealed class GstItemInvoiceVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstItemInvoiceVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstItemInvoiceTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid WidgetId { get; init; }       // taxable @ 18%
        public required Guid GadgetId { get; init; }       // taxable @ 5%
        public required Guid BookId { get; init; }         // exempt
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid LocalSupplierId { get; init; }  // in-state (27), registered
        public required Guid LocalCustomerId { get; init; }  // in-state (27), registered (B2B)
        public required Guid InterCustomerId { get; init; }  // Gujarat (24), registered (inter-state)
        public required Guid ConsumerId { get; init; }       // no GSTIN, in-state (B2C)
    }

    /// <summary>
    /// A seeded, GST-enabled (home Maharashtra 27) company with three items — Widget (18%), Gadget (5%),
    /// Book (exempt) — plus opening stock, Purchases/Sales ledgers, and four parties: an in-state supplier
    /// and in-state B2B customer (27), an inter-state customer (Gujarat 24) and an unregistered B2C consumer.
    /// </summary>
    private Kit NewGstKit(string companyName, decimal openingQty = 200m)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        // Back-date the FY so the default entry date (books-begin) lands in FY 2024-25.
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };

        if (openingQty > 0m)
        {
            inv.AddOpeningBalance(widget.Id, main, openingQty, Money.FromRupees(100m));
            inv.AddOpeningBalance(gadget.Id, main, openingQty, Money.FromRupees(20m));
            inv.AddOpeningBalance(book.Id, main, openingQty, Money.FromRupees(150m));
        }

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var sales = AddLedger(c, "Sales", "Sales Accounts");

        var supplier = AddLedger(c, "Local Supplier", "Sundry Creditors");
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var localCustomer = AddLedger(c, "Local Customer", "Sundry Debtors");
        localCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var interCustomer = AddLedger(c, "Gujarat Customer", "Sundry Debtors");
        interCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var consumer = AddLedger(c, "Walk-in Consumer", "Sundry Debtors");
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            WidgetId = widget.Id,
            GadgetId = gadget.Id,
            BookId = book.Id,
            MainGodownId = main,
            PurchasesLedgerId = purchases.Id,
            SalesLedgerId = sales.Id,
            LocalSupplierId = supplier.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
            ConsumerId = consumer.Id,
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

    /// <summary>Fills item line <paramref name="index"/> with (item, godown, qty, rate), adding rows as needed.</summary>
    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private static decimal Signed(Company c, Guid ledgerId, DateOnly asOf) =>
        LedgerBalances.SignedClosing(c, c.FindLedger(ledgerId)!, asOf);

    private static Guid TaxLedgerId(Company c, GstTaxHead head, GstTaxDirection direction) =>
        new GstService(c).FindTaxLedger(head, direction)!.Id;

    // ================================================================ (1) intra-state purchase (ITC CGST+SGST)

    [Fact]
    public void Gst_purchase_item_invoice_posts_input_cgst_sgst_and_flows_to_gstr3b()
    {
        var k = NewGstKit("GST Purchase Co");
        var c0 = k.Vm.Company!;
        var before = OnHand(c0, k.WidgetId, k.MainGodownId);

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.IsGstInvoice);                       // GST is on ⇒ GST-aware invoice

        SelectParty(entry, k.LocalSupplierId);
        Assert.Equal(k.PurchasesLedgerId, entry.SelectedStockLedger!.Id);

        // 10 Widget @ ₹100 = ₹1000 taxable @ 18% intra ⇒ Input CGST 90 + SGST 90; supplier = 1180.
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 10m, "100.00");
        Assert.Equal("1,000.00", entry.ItemsTotalText);
        Assert.Equal("90.00", entry.GstCgstText);
        Assert.Equal("90.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("1,180.00", entry.PartyTotalText);        // taxable + tax
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        // Stock up 10; pairing invariant: stock leg == Σ taxable (₹1000, tax excluded).
        Assert.Equal(before + 10m, OnHand(k.Vm.Company!, k.WidgetId, k.MainGodownId));
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(1000m, posted.InventoryLinesValue.Amount);
        Assert.Equal(1000m, Signed(k.Vm.Company!, k.PurchasesLedgerId, asOf));      // Dr Purchases (taxable only)
        Assert.Equal(90m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Input), asOf));  // Input CGST debit
        Assert.Equal(90m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.State, GstTaxDirection.Input), asOf));
        Assert.Equal(-1180m, Signed(k.Vm.Company!, k.LocalSupplierId, asOf));       // Cr Supplier (taxable + tax)

        // The UI-created invoice flows into GSTR-3B: ITC by head reconciles to the Input tax ledgers.
        var g3b = Report.BuildGstr3b(k.Vm.Company!, FyStart, asOf);
        Assert.Equal(Money.FromRupees(90m), g3b.ItcCgst);
        Assert.Equal(Money.FromRupees(90m), g3b.ItcSgst);

        // Persisted end-to-end.
        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var rPosted = reloaded.Vouchers.Single(v => v.TypeId == rType.Id);
        Assert.Equal(1000m, rPosted.InventoryLinesValue.Amount);
        Assert.Equal(90m, Signed(reloaded, TaxLedgerId(reloaded, GstTaxHead.Central, GstTaxDirection.Input), AsOf(reloaded)));
    }

    // ================================================================ (2) intra-state B2B sale (Output CGST+SGST) → GSTR-1

    [Fact]
    public void Gst_sale_item_invoice_posts_output_cgst_sgst_and_appears_in_gstr1_b2b()
    {
        var k = NewGstKit("GST Sale Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsGstInvoice);

        SelectParty(entry, k.LocalCustomerId);
        Assert.Equal(k.SalesLedgerId, entry.SelectedStockLedger!.Id);

        // 10 Widget @ ₹100 = ₹1000 @ 18% intra ⇒ CGST 90 + SGST 90; customer = 1180.
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 10m, "100.00");
        Assert.Equal("90.00", entry.GstCgstText);
        Assert.Equal("90.00", entry.GstSgstText);
        Assert.Equal("1,180.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(1000m, posted.InventoryLinesValue.Amount);                    // pairing holds
        Assert.Equal(-1000m, Signed(k.Vm.Company!, k.SalesLedgerId, asOf));          // Cr Sales (taxable only)
        Assert.Equal(-90m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-90m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(1180m, Signed(k.Vm.Company!, k.LocalCustomerId, asOf));         // Dr Customer (taxable + tax)

        // The UI-created sale appears in GSTR-1 as a B2B row with the party's GSTIN and head amounts.
        var g1 = Report.BuildGstr1(k.Vm.Company!, FyStart, asOf);
        var b2b = g1.B2B.Single(b => b.PartyGstin == GstinMaharashtra);
        Assert.Equal(Money.FromRupees(1000m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), b2b.Cgst);
        Assert.Equal(Money.FromRupees(90m), b2b.Sgst);
        Assert.Equal(Money.Zero, b2b.Igst);
    }

    // ================================================================ (3) inter-state sale → IGST

    [Fact]
    public void Inter_state_gst_sale_item_invoice_posts_output_igst()
    {
        var k = NewGstKit("GST Inter Sale Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.InterCustomerId);                 // Gujarat (24) ⇒ inter-state

        // 20 Widget @ ₹100 = ₹2000 @ 18% inter ⇒ IGST 360; customer = 2360.
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 20m, "100.00");
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstSgstText);
        Assert.Equal("360.00", entry.GstIgstText);
        Assert.Equal("2,360.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        Assert.Equal(-360m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Integrated, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(2360m, Signed(k.Vm.Company!, k.InterCustomerId, asOf));

        var g1 = Report.BuildGstr1(k.Vm.Company!, FyStart, asOf);
        var inter = g1.B2B.Single(b => b.PartyGstin == GstinGujarat);
        Assert.Equal("24", inter.PlaceOfSupplyStateCode);
        Assert.Equal(Money.FromRupees(360m), inter.Igst);
        Assert.Equal(Money.Zero, inter.Cgst);
    }

    // ================================================================ (4) multi-rate invoice → per-rate split

    [Fact]
    public void Multi_rate_item_invoice_splits_tax_per_rate()
    {
        var k = NewGstKit("GST Multi Rate Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.LocalCustomerId);

        // Line A: 10 Widget @ ₹100 = ₹1000 @ 18% ⇒ CGST 90 + SGST 90.
        // Line B: 10 Gadget @ ₹100 = ₹1000 @ 5%  ⇒ CGST 25 + SGST 25.
        // Totals: CGST 115, SGST 115, taxable 2000, party 2230.
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 10m, "100.00", index: 0);
        FillItemLine(entry, k.GadgetId, k.MainGodownId, 10m, "100.00", index: 1);

        Assert.Equal("2,000.00", entry.ItemsTotalText);
        Assert.Equal("115.00", entry.GstCgstText);
        Assert.Equal("115.00", entry.GstSgstText);
        Assert.Equal("2,230.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(2000m, posted.InventoryLinesValue.Amount);
        Assert.Equal(-115m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-115m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.State, GstTaxDirection.Output), asOf));

        // GSTR-1 rate-wise summary keeps per-rate identity (18% ₹1000/tax180, 5% ₹1000/tax50).
        var g1 = Report.BuildGstr1(k.Vm.Company!, FyStart, asOf);
        Assert.Equal(Money.FromRupees(180m), g1.RateSummary.Single(x => x.RateBasisPoints == 1800).TotalTax);
        Assert.Equal(Money.FromRupees(50m), g1.RateSummary.Single(x => x.RateBasisPoints == 500).TotalTax);
    }

    // ================================================================ (5) B2C (no GSTIN, in-state) ⇒ CGST+SGST

    [Fact]
    public void B2c_in_state_item_invoice_uses_cgst_sgst_not_igst()
    {
        var k = NewGstKit("GST B2C Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.ConsumerId);                      // no GSTIN, in-state ⇒ intra

        // 10 Gadget @ ₹100 = ₹1000 @ 5% intra ⇒ CGST 25 + SGST 25; consumer = 1050.
        FillItemLine(entry, k.GadgetId, k.MainGodownId, 10m, "100.00");
        Assert.Equal("25.00", entry.GstCgstText);
        Assert.Equal("25.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("1,050.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        Assert.Equal(-25m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-25m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Integrated, GstTaxDirection.Output), asOf));

        // GSTR-1 consolidates the unregistered supply into the B2C section.
        var g1 = Report.BuildGstr1(k.Vm.Company!, FyStart, asOf);
        var b2c = Assert.Single(g1.B2C);
        Assert.Equal(500, b2c.RateBasisPoints);
        Assert.Equal(Money.FromRupees(25m), b2c.Cgst);
    }

    // ================================================================ (6) exempt item ⇒ no tax

    [Fact]
    public void Exempt_item_invoice_posts_no_tax_and_party_equals_taxable()
    {
        var k = NewGstKit("GST Exempt Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.LocalCustomerId);

        // 5 Book @ ₹200 = ₹1000 exempt ⇒ zero tax; customer = 1000.
        FillItemLine(entry, k.BookId, k.MainGodownId, 5m, "200.00");
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("1,000.00", entry.PartyTotalText);        // no additive tax
        Assert.True(entry.Accept());

        var asOf = AsOf(k.Vm.Company!);
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        // No tax lines: exactly the two accounting legs.
        Assert.Equal(2, posted.Lines.Count);
        Assert.Equal(1000m, Signed(k.Vm.Company!, k.LocalCustomerId, asOf));
        Assert.Equal(0m, Signed(k.Vm.Company!, TaxLedgerId(k.Vm.Company!, GstTaxHead.Central, GstTaxDirection.Output), asOf));
    }

    // ================================================================ (7) taxable item, unresolvable rate ⇒ friendly message

    [Fact]
    public void Taxable_item_with_no_resolvable_rate_surfaces_a_message_not_a_crash()
    {
        var k = NewGstKit("GST Unresolved Co");
        // Make Widget taxable but with NO rate anywhere (item rate null; Sales ledger rate null; no company default).
        var widget = k.Vm.Company!.StockItems.Single(i => i.Id == k.WidgetId);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = null };
        _storage.Save(k.Vm.Company!);

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.LocalCustomerId);
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 10m, "100.00");

        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);

        // Nothing persisted.
        var reloaded = Reload(k.CompanyName);
        var rType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        Assert.DoesNotContain(reloaded.Vouchers, v => v.TypeId == rType.Id);
    }

    // ================================================================ (8) GST-off company ⇒ unchanged Phase-3 behavior

    [Fact]
    public void Gst_off_item_invoice_posts_two_legs_with_no_tax()
    {
        // A plain (no-GST) company: same shape as the Phase-3 item-invoice tests.
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Plain Item Invoice Co";
        vm.CreateCompany();
        var c = vm.Company!;
        Assert.False(c.GstEnabled);

        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.False(entry.IsGstInvoice);                      // GST off ⇒ no GST wiring

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == supplier.Id);
        FillItemLine(entry, item.Id, c.MainLocation!.Id, 10m, "50.00");
        Assert.Equal("500.00", entry.ItemsTotalText);
        Assert.Equal("500.00", entry.PartyTotalText);          // party == taxable (no tax added)
        Assert.True(entry.Accept());

        var type = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(2, posted.Lines.Count);                   // exactly the two accounting legs, no tax
        Assert.Equal(500m, posted.TotalDebit.Amount);
        Assert.Equal(500m, posted.InventoryLinesValue.Amount);
    }

    // ================================================================ (9) party required for a GST invoice

    [Fact]
    public void Gst_invoice_requires_a_party_and_surfaces_a_message()
    {
        var k = NewGstKit("GST No Party Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        // Clear the party (default is "(none)").
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger is null);
        FillItemLine(entry, k.WidgetId, k.MainGodownId, 10m, "100.00");

        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
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
