using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-5 <b>e-Way connector seam + EWB-01/02 writer + Io</b> gate (RQ-6/RQ-30; ER-5, ER-13, ER-16). Proves: the
/// offline default connector (zero credentials) and the stubbed GSP connector both carry the new e-Way members; a
/// deterministic integer-paisa EWB-01 with an uppercased doc-no, GstRt-accepts-40 and a trending-to-0 cess; a
/// deterministic EWB-02 over generated children; the Io lossless round-trip of e-Way records + config; that an e-Way-off
/// company is byte-identical (ER-13); and that CompanyImportService rebuilds a Generated record but rejects a malformed
/// one whole-batch. Figures worked by hand: a ₹50,000 @18% intra sale ⇒ CGST ₹4,500 + SGST ₹4,500, consignment ₹59,000.
/// </summary>
public sealed class EWayConnectorJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateTimeOffset Gen = new(2025, 4, 10, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static (Company Company, Voucher Sale, EWayBillService Service) BuildMovement(int rateBasisPoints = 1800)
    {
        var c = CompanyFactory.CreateSeeded("e-Way Io Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = rateBasisPoints };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(40000m));

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
        return (c, sale, new EWayBillService(c));
    }

    private static EWayBillRecord Generate(EWayBillService service, Voucher sale, int distanceKm = 250)
    {
        var record = service.PrepareRecord(sale, SaleDate);
        service.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", distanceKm);
        service.RecordPortalResponse(record, "231000000123", Gen, EWayValidity.ValidUpto(Gen, distanceKm, false));
        return record;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ================================================================ connector seam (reuse, not fork)

    [Fact]
    public void Offline_json_connector_carries_the_eway_members_with_zero_credentials()
    {
        var connector = new OfflineJsonConnector(); // no credentials
        var submit = connector.SubmitEway(new Ewb01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid()));
        Assert.False(submit.Accepted);
        Assert.Null(submit.EwbNumber);
        Assert.Null(submit.ValidUpto);
        Assert.False(connector.CancelEway(new EwbCancelRequest("231000000123", "2", null)).Cancelled);
        Assert.False(connector.ExtendEway(new EwbExtendRequest("231000000123", 100, "trip", null)).Extended);
        Assert.Null(connector.SubmitConsolidatedEway(new Ewb02Request(new byte[] { 1 }, new[] { "231000000123" })).EwbNumber);
    }

    [Fact]
    public void Gsp_connector_every_eway_member_throws_not_supported()
    {
        var gsp = new GspConnector();
        Assert.Throws<NotSupportedException>(() => gsp.SubmitEway(new Ewb01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid())));
        Assert.Throws<NotSupportedException>(() => gsp.CancelEway(new EwbCancelRequest("E", "2", null)));
        Assert.Throws<NotSupportedException>(() => gsp.ExtendEway(new EwbExtendRequest("E", 10, "r", null)));
        Assert.Throws<NotSupportedException>(() => gsp.SubmitConsolidatedEway(new Ewb02Request(new byte[] { 1 }, new[] { "E" })));
    }

    // ================================================================ EWB-01 / EWB-02 writer

    [Fact]
    public void Ewb01_is_deterministic_integer_paisa_uppercased_docno_debranded_and_no_cess()
    {
        var (company, sale, service) = BuildMovement();
        var record = Generate(service, sale);

        var first = EWayBillJson.BuildEwb01(company, sale, record);
        var second = EWayBillJson.BuildEwb01(company, sale, record);
        Assert.Equal(first, second); // byte-identical (deterministic, no clock/RNG)
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(first);
        var root = doc.RootElement;
        Assert.Equal(sale.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            root.GetProperty("docNo").GetString());
        Assert.Equal(5_900_000L, root.GetProperty("totInvValue_paisa").GetInt64()); // ₹59,000 consignment
        Assert.Equal(250, root.GetProperty("transDistance").GetInt32());
        Assert.Equal(1, root.GetProperty("transMode").GetInt32());                   // Road
        var item0 = root.GetProperty("itemList")[0];
        Assert.Equal("847130", item0.GetProperty("HsnCd").GetString());
        Assert.Equal(1800, item0.GetProperty("GstRt").GetInt32());
        Assert.Equal(450_000L, item0.GetProperty("cgst_amt_paisa").GetInt64());
        Assert.Equal(0L, item0.GetProperty("ces_amt_paisa").GetInt64());             // post-22-Sep cess trends to 0
        // The EWB number is NEVER in the request payload (ER-5 twin).
        Assert.DoesNotContain("231000000123", Encoding.UTF8.GetString(first));
    }

    [Fact]
    public void Ewb01_accepts_the_40_percent_slab()
    {
        var (company, sale, service) = BuildMovement(rateBasisPoints: 4000);
        var record = Generate(service, sale);
        using var doc = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal(4000, doc.RootElement.GetProperty("itemList")[0].GetProperty("GstRt").GetInt32());
    }

    [Fact]
    public void Ewb02_is_deterministic_and_references_the_child_ewb_numbers()
    {
        var (company, sale, service) = BuildMovement();
        var child = Generate(service, sale);
        var cewb = service.PrepareConsolidated(new[] { child }, EWayTransportMode.Road, "MH01Z9999", "27");

        var first = EWayBillJson.BuildEwb02(company, cewb);
        Assert.Equal(first, EWayBillJson.BuildEwb02(company, cewb)); // byte-stable
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(first);
        Assert.Equal("231000000123", doc.RootElement.GetProperty("ewbList")[0].GetString());
        Assert.Equal("MH01Z9999", doc.RootElement.GetProperty("vehicleNo").GetString());
    }

    // ================================================================ Io lossless round-trip

    [Fact]
    public void Company_with_eway_records_round_trips_byte_stable_json_and_xml()
    {
        var (company, sale, service) = BuildMovement();
        Generate(service, sale);

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

        // The Generated record survived with its portal number + validity (portal receipts, exported).
        var dto = Assert.Single(jm!.Payload.EWayBillRecords);
        Assert.Equal("Generated", dto.Status);
        Assert.Equal("231000000123", dto.EwbNumber);
        Assert.NotNull(dto.ValidUpto);
        Assert.Equal("Road", dto.TransMode);
        Assert.Equal(5_900_000L, dto.ConsignmentValuePaisa);
    }

    // ================================================================ ER-13 byte-identical when off

    [Fact]
    public void E_way_off_company_is_byte_identical_and_carries_defaults()
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
        Assert.Empty(model!.Payload.EWayBillRecords);
        Assert.False(model.Company.Gst!.EWayBillEnabled);
        Assert.Equal(5_000_000L, model.Company.Gst.EWayThresholdPaisa);   // ₹50,000 default
        Assert.Equal("Rule138Default", model.Company.Gst.ConsignmentBasis);
        Assert.True(model.Company.Gst.EWayIntraStateApplicable);
        Assert.Empty(model.Company.Gst.EWayStateThresholds);
        Assert.Equal(first, CanonicalJson.Export(model!)); // import → re-export byte-identical

        // A non-GST company (Robert) is de-branded and carries no e-Way records.
        var robert = CompanyFactory.CreateSeeded("Robert Transport", FyStart);
        var (rm, re) = CanonicalJson.Parse(CanonicalJson.Export(robert));
        Assert.Empty(re);
        Assert.Empty(rm!.Payload.EWayBillRecords);
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(CanonicalJson.Export(robert)), StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ CompanyImportService all-or-nothing

    [Fact]
    public void Import_rebuilds_a_generated_record_and_rejects_a_malformed_one_whole_batch()
    {
        var (company, sale, service) = BuildMovement();
        Generate(service, sale);

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);

        // Good import: the covered movement + its Generated record rebuild into a fresh company.
        var fresh = CompanyFactory.CreateSeeded("Fresh Io Co", FyStart);
        var ok = new CompanyImportService(fresh).Apply(model!);
        Assert.True(ok.Applied, string.Join("; ", ok.Errors));
        var rebuilt = Assert.Single(fresh.EWayBillRecords);
        Assert.Equal(EWayStatus.Generated, rebuilt.Status);
        Assert.Equal("231000000123", rebuilt.EwbNumber);
        Assert.NotNull(rebuilt.ValidUpto);

        // Malformed: a Generated record with NO EWB number rejects the WHOLE batch (Applied = false, target untouched).
        var bad = model!.Payload.EWayBillRecords[0] with { EwbNumber = null };
        var badModel = model with { Payload = model.Payload with { EWayBillRecords = new[] { bad } } };
        var target = CompanyFactory.CreateSeeded("Reject Io Co", FyStart);
        var mastersBefore = target.Ledgers.Count;
        var rejected = new CompanyImportService(target).Apply(badModel);
        Assert.False(rejected.Applied);
        Assert.Empty(target.EWayBillRecords);
        Assert.Equal(mastersBefore, target.Ledgers.Count); // target untouched
    }
}
