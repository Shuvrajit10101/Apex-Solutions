using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-4a <b>connector seam + INV-01 writer + Io</b> gate (RQ-5/RQ-30; ER-5, ER-13, ER-16). Proves: the offline
/// default connector (zero credentials), the stubbed GSP connector; a deterministic integer-paisa INV-01 request with an
/// uppercased doc-no, GstRt-accepts-40 and a trending-to-0 cess; the deterministic &lt;2 MB batch auto-split; the Io
/// lossless round-trip of e-invoice records + config; and that an e-invoicing-off company is byte-identical (ER-13). All
/// figures worked by hand: a ₹50,000 @18% intra B2B sale ⇒ CGST ₹4,500 + SGST ₹4,500, invoice value ₹59,000.
/// </summary>
public sealed class EInvoiceConnectorJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private static (Company Company, Voucher Sale, EInvoiceService Service) BuildB2BSale(int rateBasisPoints = 1800)
    {
        var c = CompanyFactory.CreateSeeded("e-Invoice Io Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = rateBasisPoints };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 20000m, Money.FromRupees(40000m)); // ample stock for the batch tests

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var b2b = Add(c, "Local Debtor", "Sundry Debtors", true);
        b2b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), rateBasisPoints) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(b2b.Id, new Money(50000m + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines, partyId: b2b.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(50000m)) }));
        return (c, sale, new EInvoiceService(c));
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ================================================================ §6.1 offline default connector

    [Fact]
    public void Offline_json_is_the_default_and_needs_no_credentials()
    {
        var config = new GstConfig();
        Assert.Equal(GstConnectorMode.OfflineJson, config.ConnectorMode);

        var connector = new OfflineJsonConnector(); // constructable with NO credentials
        Assert.Equal(GstConnectorMode.OfflineJson, connector.Mode);

        var result = connector.SubmitForIrn(new Inv01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid()));
        Assert.False(result.Accepted);
        Assert.Null(result.Irn);
    }

    // ================================================================ §6.3 stubbed GSP connector

    [Fact]
    public void Gsp_connector_every_member_throws_not_supported()
    {
        var gsp = new GspConnector();
        Assert.Equal(GstConnectorMode.Gsp, gsp.Mode);
        Assert.Throws<NotSupportedException>(() => gsp.SubmitForIrn(new Inv01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid())));
        Assert.Throws<NotSupportedException>(() => gsp.CancelIrn(new IrnCancelRequest("IRN", "1", null)));
    }

    // ================================================================ §6.4 deterministic + paisa + fixture

    [Fact]
    public void Inv01_is_deterministic_integer_paisa_with_uppercased_docno_and_no_cess()
    {
        var (company, sale, _) = BuildB2BSale();
        var first = EInvoiceJson.BuildInv01(company, sale);
        var second = EInvoiceJson.BuildInv01(company, sale);
        Assert.Equal(first, second); // byte-identical (deterministic, no clock/RNG)
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(first);
        var root = doc.RootElement;
        // DocDtls.No is the UPPERCASED document number (the bare voucher number, invariant-culture).
        Assert.Equal(sale.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            root.GetProperty("DocDtls").GetProperty("No").GetString());
        // Money is integer paisa: ass ₹50,000 = 5,000,000; CGST/SGST ₹4,500 = 450,000 each; total ₹59,000 = 5,900,000.
        var val = root.GetProperty("ValDtls");
        Assert.Equal(5_000_000L, val.GetProperty("ass_val_paisa").GetInt64());
        Assert.Equal(450_000L, val.GetProperty("cgst_val_paisa").GetInt64());
        Assert.Equal(450_000L, val.GetProperty("sgst_val_paisa").GetInt64());
        Assert.Equal(0L, val.GetProperty("igst_val_paisa").GetInt64());
        Assert.Equal(0L, val.GetProperty("ces_val_paisa").GetInt64());   // post-22-Sep cess trends to 0
        Assert.Equal(5_900_000L, val.GetProperty("tot_inv_val_paisa").GetInt64());
        // The single item line carries GstRt 1800 and CesAmt 0.
        var item0 = root.GetProperty("ItemList")[0];
        Assert.Equal(1800, item0.GetProperty("GstRt").GetInt32());
        Assert.Equal(0L, item0.GetProperty("ces_amt_paisa").GetInt64());
    }

    [Fact]
    public void Inv01_accepts_the_40_percent_slab()
    {
        var (company, sale, _) = BuildB2BSale(rateBasisPoints: 4000); // GST 2.0 de-merit 40% slab
        using var doc = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        Assert.Equal(4000, doc.RootElement.GetProperty("ItemList")[0].GetProperty("GstRt").GetInt32());
    }

    // ================================================================ §6.5 <2 MB auto-split (deterministic)

    [Fact]
    public void Batch_auto_splits_under_2mb_and_a_single_voucher_is_one_part()
    {
        var (company, sale, _) = BuildB2BSale();
        // A single covered voucher ⇒ exactly one part.
        var single = EInvoiceJson.BuildBatch(company, new[] { sale });
        Assert.Single(single);
        Assert.True(single[0].Length < 2_000_000);

        // Post enough covered sales that the batch must exceed one ~2 MB part (count derived from the object size so
        // the test is robust regardless of the exact serialized length).
        var oneLen = EInvoiceJson.BuildInv01(company, sale).Length;
        var need = (1_900_000 / oneLen) + 5;
        var sales = PostManyB2BSales(company, need);

        var parts = EInvoiceJson.BuildBatch(company, sales);
        Assert.True(parts.Count >= 2, $"expected >= 2 parts for {need} vouchers, got {parts.Count}");
        Assert.All(parts, p => Assert.True(p.Length < 2_000_000, $"part exceeded 2 MB: {p.Length}"));

        // Round-trip integrity: every object survives across the parts, and the packing is deterministic.
        var total = parts.Sum(p => JsonDocument.Parse(p).RootElement.GetArrayLength());
        Assert.Equal(sales.Count, total);
        Assert.Equal(parts.Select(p => Convert.ToBase64String(p)),
            EInvoiceJson.BuildBatch(company, sales).Select(p => Convert.ToBase64String(p)));
    }

    private static List<Voucher> PostManyB2BSales(Company company, int count)
    {
        var gst = new GstService(company);
        var ledgers = new LedgerService(company);
        var item = company.StockItems.First();
        var main = company.MainLocation!.Id;
        var sales = company.FindLedgerByName("Sales")!.Id;
        var b2b = company.FindLedgerByName("Local Debtor")!.Id;
        var salesType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);

        var list = new List<Voucher>();
        for (var i = 0; i < count; i++)
        {
            var lines = new List<EntryLine>
            {
                new(b2b, new Money(50000m + tax.TotalTax.Amount), DrCr.Debit),
                new(sales, Money.FromRupees(50000m), DrCr.Credit),
            };
            lines.AddRange(tax.TaxLines);
            list.Add(ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate.AddDays(i % 20), lines, partyId: b2b,
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 1m, Money.FromRupees(50000m)) })));
        }
        return list;
    }

    // ================================================================ §6.13 Io lossless round-trip

    [Fact]
    public void Company_with_einvoice_records_round_trips_byte_stable_json_and_xml()
    {
        var (company, sale, service) = BuildB2BSale();
        var record = service.PrepareRecord(sale);
        service.RecordIrpResponse(record, new string('A', 64), "112010036284", SaleDate, "IRP-QR", new byte[] { 4, 8, 15, 16 });

        var json = CanonicalJson.Export(company);
        var (jm, je) = CanonicalJson.Parse(json);
        Assert.Empty(je);
        Assert.Equal(json, CanonicalJson.Export(jm!));

        var xml = CanonicalXml.Export(company);
        var (xm, xe) = CanonicalXml.Parse(xml);
        Assert.Empty(xe);
        Assert.Equal(xml, CanonicalXml.Export(xm!));

        // JSON and XML carry the same payload.
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));

        // The Generated record survived with its IRP artefacts.
        var dto = Assert.Single(jm!.Payload.EInvoiceRecords);
        Assert.Equal("Generated", dto.Status);
        Assert.Equal(new string('A', 64), dto.Irn);
        Assert.NotNull(dto.SignedJsonBase64);
    }

    // ================================================================ §6.14 ER-13 byte-identical when off

    [Fact]
    public void E_invoicing_off_company_is_byte_identical_and_carries_offlinejson_defaults()
    {
        var c = CompanyFactory.CreateSeeded("Plain GST Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var first = CanonicalJson.Export(c);
        Assert.Equal(first, CanonicalJson.Export(c)); // byte-stable

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.EInvoiceRecords);
        Assert.False(model.Company.Gst!.EInvoicingEnabled);
        Assert.Equal("OfflineJson", model.Company.Gst.ConnectorMode);
        Assert.Equal("None", model.Company.Gst.EInvoiceExemptionClasses);
        Assert.Equal(first, CanonicalJson.Export(model!)); // import → re-export byte-identical

        // A non-GST company (Robert) is de-branded and carries no e-invoice records.
        var robert = CompanyFactory.CreateSeeded("Robert Transport", FyStart);
        var (rm, re) = CanonicalJson.Parse(CanonicalJson.Export(robert));
        Assert.Empty(re);
        Assert.Empty(rm!.Payload.EInvoiceRecords);
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(CanonicalJson.Export(robert)), StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ §6.15 CompanyImportService all-or-nothing

    [Fact]
    public void Import_rebuilds_a_generated_record_and_rejects_a_malformed_one_whole_batch()
    {
        var (company, sale, service) = BuildB2BSale();
        var record = service.PrepareRecord(sale);
        service.RecordIrpResponse(record, new string('E', 64), "ACK", SaleDate, "QR", new byte[] { 1, 2 });

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);

        // Good import: the covered voucher + its Generated record rebuild into a fresh company.
        var fresh = CompanyFactory.CreateSeeded("Fresh Io Co", FyStart);
        var ok = new CompanyImportService(fresh).Apply(model!);
        Assert.True(ok.Applied, string.Join("; ", ok.Errors));
        var rebuilt = Assert.Single(fresh.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Generated, rebuilt.Status);
        Assert.Equal(new string('E', 64), rebuilt.Irn);

        // Malformed: a Generated record with NO IRN rejects the WHOLE batch (Applied = false, target untouched).
        var badRecord = model!.Payload.EInvoiceRecords[0] with { Irn = null };
        var badModel = model with { Payload = model.Payload with { EInvoiceRecords = new[] { badRecord } } };
        var target = CompanyFactory.CreateSeeded("Reject Io Co", FyStart);
        var mastersBefore = target.Ledgers.Count;
        var bad = new CompanyImportService(target).Apply(badModel);
        Assert.False(bad.Applied);
        Assert.Empty(target.EInvoiceRecords);
        Assert.Equal(mastersBefore, target.Ledgers.Count); // target untouched
    }
}
