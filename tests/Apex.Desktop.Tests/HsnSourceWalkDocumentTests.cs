using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Path = System.IO.Path;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 6.4 (ii) — THE HSN/SAC EVERY DOCUMENT FILES COMES FROM ONE HIERARCHY WALK, IN THE ORDER
/// <see cref="GstConfig.SourceOfHsnSacDetails"/> NAMES.</b>
///
/// <para><b>The defect, on <c>origin/main</c> 0fbf8e2.</b> <c>SourceOfHsnSacDetails</c> had no reader outside
/// persistence, and the one shared resolver every consumer called — <c>GstReportSupport.HsnSacOf(item)</c> — read
/// the STOCK ITEM alone. So an item that declared no HSN filed GSTR-1 Table 12 under <c>(none)</c>, printed a blank
/// HSN column on its tax invoice and sent <c>HsnCd: ""</c> in INV-01, even when its sales ledger, that ledger's group
/// or the company declared the code; and flipping the order moved nothing.</para>
///
/// <para><b>VENDOR (R7, opened by content 2026-10-29,
/// <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>):</b> the product "enables you to
/// specify where to look for the details by setting up the tax rate hierarchy and HSN/SAC hierarchy in the Company";
/// the two orders are <c>Ledger → Group → Stock Item → Stock Group → Company</c> and
/// <c>Stock Item → Stock Group → Ledger → Group → Company</c>; "The same explanation is true for even HSN/SAC
/// details."</para>
///
/// <para>Every figure is read off a book saved and re-loaded through a <b>fresh</b> <see cref="SqliteCompanyStore"/>
/// handle, and asserted on the emitted artefacts: the GSTR-1 and GSTR-9 JSON bytes, the INV-01 JSON bytes and the
/// tax-invoice PDF bytes (its <c>Tj</c> text operands).</para>
/// </summary>
public sealed class HsnSourceWalkDocumentTests
{
    private const string GstinHome = "27AAPFU0939F1ZV";
    private const string GstinBuyer = "27AAACR5055K1Z5";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private const string ItemHsn = "84713010";
    private const string LedgerHsn = "847130";
    private const string GroupHsn = "847141";

    /// <summary>Which master carries the HSN on the fixture.</summary>
    public enum Carrier { Ledger, Group }

    /// <summary>
    /// An item with NO HSN whose sales ledger (or that ledger's accounting group) declares one files THAT code on every
    /// document, under BOTH published orders: ledger-first reaches the ledger rung at once; item-first falls through
    /// the empty Stock Item and Stock Group rungs to it. On main every assertion below reads <c>(none)</c> / blank.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst, Carrier.Ledger, LedgerHsn)]
    [InlineData(GstDetailSource.StockItemFirst, Carrier.Ledger, LedgerHsn)]
    [InlineData(GstDetailSource.LedgerFirst, Carrier.Group, GroupHsn)]
    [InlineData(GstDetailSource.StockItemFirst, Carrier.Group, GroupHsn)]
    public void An_item_with_no_hsn_files_its_ledger_or_group_hsn_on_every_document(
        GstDetailSource order, Carrier carrier, string expected)
    {
        var (c, sale) = Reload(Build(order, itemHsn: null, carrier));
        Assert.Equal(order, c.Gst!.SourceOfHsnSacDetails);   // the option itself survived the round trip

        AssertEveryDocumentFiles(c, sale, expected);
    }

    /// <summary>
    /// 🔴 <b>THE ORDER IS LOAD-BEARING.</b> The item declares <c>84713010</c> and the sales ledger <c>847130</c>:
    /// ledger-first files the LEDGER's code, item-first the ITEM's. On main both orders filed the item's code, so the
    /// ledger-first row is red there; a walk that ignored the option would redden one of the two rows here.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst, LedgerHsn)]
    [InlineData(GstDetailSource.StockItemFirst, ItemHsn)]
    public void When_both_the_item_and_the_ledger_declare_an_hsn_the_source_order_decides(
        GstDetailSource order, string expected)
    {
        var (c, sale) = Reload(Build(order, itemHsn: ItemHsn, Carrier.Ledger));
        AssertEveryDocumentFiles(c, sale, expected);
    }

    /// <summary>
    /// GSTR-9 Table 18 (inward HSN) reads the same walk off the PURCHASE ledger: an item with no HSN bought through a
    /// purchase ledger that declares one files that code, under both orders, on the reloaded book's JSON bytes.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void Table_18_files_the_purchase_ledgers_hsn_for_an_item_with_none(GstDetailSource order)
    {
        var (book, _) = Build(order, itemHsn: null, Carrier.Ledger);
        var c = book;
        var gst = new GstService(c);
        var widget = c.StockItems.Single(i => i.Name == "Widget");
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        purchases.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = LedgerHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var supplier = Add(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinBuyer, StateCode = "27" };
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(7_777.77m), 1800, null) },
            interState: false, GstTaxDirection.Input);
        var legs = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(7_777.77m), DrCr.Debit),
            new(supplier.Id, new Money(7_777.77m + tax.TotalTax.Amount), DrCr.Credit),
        };
        legs.AddRange(tax.TaxLines);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var purchase = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), purchaseType, SaleDate, legs, partyId: supplier.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 1m, Money.FromRupees(7_777.77m)),
            }));

        var (reloaded, _) = Reload((c, purchase));
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(reloaded, FyStart, FyEnd));
        var row = Assert.Single(g9.RootElement.GetProperty("tbl18_hsn").EnumerateArray());
        Assert.Equal(LedgerHsn, row.GetProperty("hsn_sac").GetString());   // main: "(none)"
    }

    // ================================================================ the four artefacts

    private static void AssertEveryDocumentFiles(Company c, Voucher sale, string expected)
    {
        // GSTR-1 Table 12, the JSON bytes.
        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());
            Assert.Equal(expected, row.GetProperty("hsn_sac").GetString());
        }

        // GSTR-9 Table 17 is folded from the same summary; the JSON bytes.
        using (var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g9.RootElement.GetProperty("tbl17_hsn").EnumerateArray());
            Assert.Equal(expected, row.GetProperty("hsn_sac").GetString());
        }

        // INV-01, the JSON bytes.
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))
        {
            var item = Assert.Single(inv.RootElement.GetProperty("ItemList").EnumerateArray());
            Assert.Equal(expected, item.GetProperty("HsnCd").GetString());
        }

        // EWB-01, the JSON bytes (the schema types the code as a number).
        var ewb = new EWayBillService(c);
        var record = ewb.PrepareRecord(sale, sale.Date);
        ewb.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250);
        using (var e = JsonDocument.Parse(Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(c, sale, record))))
        {
            var item = Assert.Single(e.RootElement.GetProperty("itemList").EnumerateArray());
            Assert.Equal(long.Parse(expected), item.GetProperty("hsnCode").GetInt64());
        }

        // The tax invoice, the PDF bytes.
        var pdf = Encoding.Latin1.GetString(
            InvoicePdf.Render(VoucherPrintProjector.ProjectInvoice(c, sale), new PrintConfig(), new PageConfig()));
        Assert.Contains("TAX INVOICE", pdf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"({expected}) Tj", pdf, StringComparison.Ordinal);
        foreach (var other in new[] { ItemHsn, LedgerHsn, GroupHsn }.Where(h => h != expected))
            Assert.DoesNotContain($"({other}) Tj", pdf, StringComparison.Ordinal);
    }

    // ================================================================ fixture

    private static (Company Company, Voucher Sale) Build(GstDetailSource order, string? itemHsn, Carrier carrier)
    {
        var c = CompanyFactory.CreateSeeded("HSN Walk Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, EWayThreshold = new Money(1m),
        });
        c.Gst!.SourceOfHsnSacDetails = order;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = itemHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        Assert.Equal(itemHsn, GstReportSupport.ItemRungHsnSacOf(widget));   // the fixture's premise
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(500m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        if (carrier == Carrier.Ledger)
            sales.SalesPurchaseGst = new StockItemGstDetails
            { HsnSac = LedgerHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        else
            c.FindGroupByName("Sales Accounts")!.Gst = new MasterGstDetails
            { HsnSac = GroupHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var party = Add(c, "Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinBuyer, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1_23_456.78m), 1800, null) },
            interState: false, GstTaxDirection.Output);
        var legs = new List<EntryLine>
        {
            new(party.Id, new Money(1_23_456.78m + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1_23_456.78m), DrCr.Credit),
        };
        legs.AddRange(tax.TaxLines);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate, legs, partyId: party.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 1m, Money.FromRupees(1_23_456.78m)),
            }));
        return (c, sale);
    }

    private static (Company Company, Voucher Sale) Reload((Company Company, Voucher Sale) original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-hsn-walk-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original.Company);
            using var read = new SqliteCompanyStore(dbPath);
            var c = read.Load(original.Company.Id)!;
            return (c, c.FindVoucher(original.Sale.Id)!);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
