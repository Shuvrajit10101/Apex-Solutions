using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// TDD for the canonical JSON + XML export/parse (RQ-19, DP-4, PR-4 round-trip gate) and the CSV import parser
/// (§DP-5). Uses the rich <see cref="CanonicalFixture"/> Bright company (masters + item-invoice + GST) as the
/// subject. Asserts: every field survives the projection (money exact via integer paisa), the serialisation is
/// byte-stable, malformed input yields structured errors rather than exceptions, the envelope carries
/// formatVersion + schemaVersion, and zero "tally" bytes leak into the output.
/// </summary>
public class CanonicalRoundTripTests
{
    // ------------------------------------------------------------------ JSON

    [Fact]
    public void Json_export_carries_versioned_envelope()
    {
        var company = CanonicalFixture.BuildBright();
        var bytes = CanonicalJson.Export(company);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"formatVersion\": 1", text);
        Assert.Contains($"\"schemaVersion\": {CanonicalMapper.SchemaVersion}", text);
        Assert.Contains("\"company\"", text);
        Assert.Contains("\"payload\"", text);
    }

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var company = CanonicalFixture.BuildBright();
        var first = CanonicalJson.Export(company);

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);

        var second = CanonicalJson.Export(model!);
        Assert.Equal(first, second); // byte-for-byte
    }

    [Fact]
    public void Json_preserves_every_field_via_integer_paisa()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);
        AssertModelMatchesCompany(model!, company);
    }

    [Fact]
    public void Json_malformed_yields_errors_not_exception()
    {
        // Not JSON at all.
        var (m1, e1) = CanonicalJson.Parse(Encoding.UTF8.GetBytes("{ this is not json "));
        Assert.Null(m1);
        Assert.NotEmpty(e1);

        // Valid JSON, unknown formatVersion.
        var badVersion = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":999,\"schemaVersion\":14,\"company\":{\"id\":\"" + Guid.NewGuid() +
            "\",\"name\":\"X\",\"financialYearStart\":\"2021-04-01\",\"booksBeginFrom\":\"2021-04-01\",\"decimalPlaces\":2},\"payload\":{}}");
        var (m2, e2) = CanonicalJson.Parse(badVersion);
        Assert.Null(m2);
        Assert.Contains(e2, msg => msg.Contains("formatVersion"));

        // Valid JSON, non-integer money (float where a long paisa is required).
        var badMoney = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":1,\"schemaVersion\":14,\"company\":{\"id\":\"" + Guid.NewGuid() +
            "\",\"name\":\"X\",\"financialYearStart\":\"2021-04-01\",\"booksBeginFrom\":\"2021-04-01\",\"decimalPlaces\":2}," +
            "\"payload\":{\"ledgers\":[{\"id\":\"" + Guid.NewGuid() +
            "\",\"name\":\"L\",\"groupId\":\"" + Guid.NewGuid() + "\",\"openingBalancePaisa\":12.5,\"openingIsDebit\":true}]}}");
        var (m3, e3) = CanonicalJson.Parse(badMoney);
        Assert.Null(m3);
        Assert.NotEmpty(e3);

        // Empty document.
        var (m4, e4) = CanonicalJson.Parse(Array.Empty<byte>());
        Assert.Null(m4);
        Assert.NotEmpty(e4);
    }

    [Fact]
    public void Json_missing_required_field_yields_error()
    {
        // company.name missing (required) → JsonException surfaced as an error, no throw.
        var missingName = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":1,\"schemaVersion\":14,\"company\":{\"id\":\"" + Guid.NewGuid() +
            "\",\"financialYearStart\":\"2021-04-01\",\"booksBeginFrom\":\"2021-04-01\",\"decimalPlaces\":2},\"payload\":{}}");
        var (model, errors) = CanonicalJson.Parse(missingName);
        Assert.Null(model);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Json_output_has_zero_tally_bytes()
    {
        var company = CanonicalFixture.BuildBright();
        var bytes = CanonicalJson.Export(company);
        AssertNoTally(bytes);
    }

    // ------------------------------------------------------------------ XML

    [Fact]
    public void Xml_export_carries_versioned_envelope()
    {
        var company = CanonicalFixture.BuildBright();
        var text = Encoding.UTF8.GetString(CanonicalXml.Export(company));

        Assert.Contains("formatVersion=\"1\"", text);
        Assert.Contains($"schemaVersion=\"{CanonicalMapper.SchemaVersion}\"", text);
        Assert.Contains("<company", text);
        Assert.Contains("<vouchers", text);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var company = CanonicalFixture.BuildBright();
        var first = CanonicalXml.Export(company);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);

        var second = CanonicalXml.Export(model!);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Xml_preserves_every_field_via_integer_paisa()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(errors);
        AssertModelMatchesCompany(model!, company);
    }

    [Fact]
    public void Xml_malformed_yields_errors_not_exception()
    {
        var (m1, e1) = CanonicalXml.Parse(Encoding.UTF8.GetBytes("<not><closed>"));
        Assert.Null(m1);
        Assert.NotEmpty(e1);

        // Wrong root element.
        var (m2, e2) = CanonicalXml.Parse(Encoding.UTF8.GetBytes("<other formatVersion=\"1\"/>"));
        Assert.Null(m2);
        Assert.Contains(e2, msg => msg.Contains("root"));

        // Unknown formatVersion.
        var badVersion = Encoding.UTF8.GetBytes(
            "<apexExport formatVersion=\"999\" schemaVersion=\"14\">" +
            "<company id=\"" + Guid.NewGuid() + "\" name=\"X\" financialYearStart=\"2021-04-01\" booksBeginFrom=\"2021-04-01\" decimalPlaces=\"2\"/>" +
            "</apexExport>");
        var (m3, e3) = CanonicalXml.Parse(badVersion);
        Assert.Null(m3);
        Assert.Contains(e3, msg => msg.Contains("formatVersion"));

        var (m4, e4) = CanonicalXml.Parse(Array.Empty<byte>());
        Assert.Null(m4);
        Assert.NotEmpty(e4);
    }

    [Fact]
    public void Xml_output_has_zero_tally_bytes()
    {
        var company = CanonicalFixture.BuildBright();
        AssertNoTally(CanonicalXml.Export(company));
    }

    // ------------------------------------------------------------------ JSON vs XML carry identical payload

    [Fact]
    public void Json_and_xml_parse_to_equal_models()
    {
        var company = CanonicalFixture.BuildBright();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(company));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(je);
        Assert.Empty(xe);

        // The two formats must carry the identical payload → re-serialising each to JSON yields equal bytes.
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    // ------------------------------------------------------------------ CSV import (flat, best-effort)

    [Fact]
    public void Csv_import_parses_flat_masters_and_vouchers()
    {
        string csv =
            "#ledgers\n" +
            "Name,Under,OpeningBalance,OpeningSide\n" +
            "Bright's Capital,Capital Account,150000.00,Credit\n" +
            "Cash,Cash-in-Hand,20000,Debit\n" +
            "#vouchers\n" +
            "Date,Type,VoucherRef,Ledger,Amount,DrCr,Narration\n" +
            "2021-04-02,Receipt,V1,Cash,5000.00,Debit,Capital in\n" +
            "2021-04-02,Receipt,V1,Bright's Capital,5000.00,Credit,\n";
        var result = CsvImport.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Ledgers.Count);
        Assert.Equal(15000000L, result.Ledgers[0].OpeningBalancePaisa); // 150000.00 rupees → paisa
        Assert.False(result.Ledgers[0].OpeningIsDebit);
        Assert.Single(result.Vouchers);
        Assert.Equal(2, result.Vouchers[0].Lines.Count);
        Assert.Equal(500000L, result.Vouchers[0].Lines[0].AmountPaisa);
        Assert.True(result.Vouchers[0].Lines[0].IsDebit);
    }

    [Fact]
    public void Csv_import_collects_errors_for_bad_rows_without_throwing()
    {
        string csv =
            "#ledgers\n" +
            "Name,Under,OpeningBalance,OpeningSide\n" +
            ",Capital Account,100,Credit\n" +          // missing name
            "Good,Capital Account,not-a-number,Debit\n"; // bad amount
        var result = CsvImport.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.True(result.HasErrors);
        Assert.Equal(2, result.Errors.Count);
        Assert.Empty(result.Ledgers);
    }

    [Fact]
    public void Csv_reader_handles_quoted_fields_with_commas()
    {
        string csv =
            "#ledgers\n" +
            "Name,Under,OpeningBalance,OpeningSide\n" +
            "\"Smith, Jones & Co\",Sundry Debtors,1000.00,Debit\n";
        var result = CsvImport.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.False(result.HasErrors);
        Assert.Single(result.Ledgers);
        Assert.Equal("Smith, Jones & Co", result.Ledgers[0].Name);
    }

    // ------------------------------------------------------------------ assertions

    private static void AssertModelMatchesCompany(CanonicalModel model, Company company)
    {
        Assert.Equal(CanonicalMapper.FormatVersion, model.FormatVersion);
        Assert.Equal(CanonicalMapper.SchemaVersion, model.SchemaVersion);

        // Company header.
        Assert.Equal(company.Id, model.Company.Id);
        Assert.Equal(company.Name, model.Company.Name);
        Assert.Equal(company.MailingName, model.Company.MailingName);
        Assert.Equal(company.State, model.Company.State);
        Assert.Equal("2021-04-01", model.Company.FinancialYearStart);
        Assert.Equal("2021-04-01", model.Company.BooksBeginFrom);

        // GST config.
        Assert.NotNull(model.Company.Gst);
        Assert.True(model.Company.Gst!.Enabled);
        Assert.Equal(company.Gst!.Gstin, model.Company.Gst.Gstin);
        Assert.Equal("27", model.Company.Gst.HomeStateCode);
        Assert.Equal(company.Gst.RateSlabs.Count, model.Company.Gst.RateSlabs.Count);

        // Every ledger reconciles by id, opening paisa, side, and group ref.
        foreach (var l in company.Ledgers)
        {
            var dto = model.Payload.Ledgers.Single(x => x.Id == l.Id);
            Assert.Equal(l.Name, dto.Name);
            Assert.Equal(l.GroupId, dto.GroupId);
            Assert.Equal(MoneyCodec.ToPaisa(l.OpeningBalance), dto.OpeningBalancePaisa);
            Assert.Equal(l.OpeningIsDebit, dto.OpeningIsDebit);
        }

        // The GST party ledger round-trips its PartyGst.
        var party = company.FindLedgerByName("Ram & Co")!;
        var partyDto = model.Payload.Ledgers.Single(x => x.Id == party.Id);
        Assert.NotNull(partyDto.PartyGst);
        Assert.Equal(party.PartyGst!.Gstin, partyDto.PartyGst!.Gstin);
        Assert.Equal("Regular", partyDto.PartyGst.RegistrationType);

        // A tax ledger round-trips its classification.
        var taxLedger = company.Ledgers.First(x => x.GstClassification is not null);
        var taxDto = model.Payload.Ledgers.Single(x => x.Id == taxLedger.Id);
        Assert.NotNull(taxDto.GstClassification);

        // Stock item + opening balance + item-invoice voucher.
        var item = company.FindStockItemByName("Widget")!;
        var itemDto = model.Payload.StockItems.Single(x => x.Id == item.Id);
        Assert.Equal("84713010", itemDto.Gst!.HsnSac);
        Assert.Equal(5m, itemDto.ReorderLevel);

        var ob = company.StockOpeningBalances.Single(b => b.StockItemId == item.Id);
        var obDto = model.Payload.StockOpeningBalances.Single(x => x.Id == ob.Id);
        Assert.Equal(100m, obDto.Quantity);
        Assert.Equal(MoneyCodec.ToPaisa(ob.Rate), obDto.RatePaisa);
        Assert.Equal("B1", obDto.BatchLabel);

        // Vouchers: same count, and the item-invoice voucher preserves its lines, GST tax line, and inventory line.
        Assert.Equal(company.Vouchers.Count, model.Payload.Vouchers.Count);
        // The GST'd item-invoice Purchase (the fixture now also carries non-GST Sales/POS item-invoices).
        var sale = company.Vouchers.Single(v => v.Narration == "Bought 10 widgets from Ram & Co");
        var saleDto = model.Payload.Vouchers.Single(x => x.Id == sale.Id);
        Assert.Equal(sale.Lines.Count, saleDto.Lines.Count);
        Assert.Equal(sale.PartyId, saleDto.PartyId);

        // Total debit/credit reconcile to the paisa through the DTO.
        long dtoDr = saleDto.Lines.Where(x => x.Side == "Debit").Sum(x => x.AmountPaisa);
        long dtoCr = saleDto.Lines.Where(x => x.Side == "Credit").Sum(x => x.AmountPaisa);
        Assert.Equal(dtoDr, dtoCr);
        Assert.Equal(MoneyCodec.ToPaisa(sale.TotalDebit), dtoDr);

        var gstLine = saleDto.Lines.First(x => x.Gst is not null);
        Assert.Equal(900, gstLine.Gst!.RateBasisPoints);
        Assert.Equal(MoneyCodec.ToPaisa(Money.FromRupees(2000m)), gstLine.Gst.TaxableValuePaisa);

        var invLine = Assert.Single(saleDto.InventoryLines);
        Assert.Equal(10m, invLine.Quantity);
        Assert.Equal(MoneyCodec.ToPaisa(Money.FromRupees(200m)), invLine.RatePaisa);
        Assert.Equal("Inward", invLine.Direction); // stamped by posting (Purchase ⇒ Inward)
    }

    // ------------------------------------------------------------------ newly-added data types round-trip

    [Fact]
    public void Json_round_trips_cost_bank_forex_budget_scenario_currency_inventory()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);
        AssertNewDataTypes(model!, company);
    }

    [Fact]
    public void Xml_round_trips_cost_bank_forex_budget_scenario_currency_inventory()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(errors);
        AssertNewDataTypes(model!, company);
    }

    // ------------------------------------------------------------------ batch masters round-trip (Phase 6)

    [Fact]
    public void Json_round_trips_batch_masters_and_item_switches()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);
        AssertBatches(model!, company);
    }

    [Fact]
    public void Xml_round_trips_batch_masters_and_item_switches()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(errors);
        AssertBatches(model!, company);
    }

    [Fact]
    public void Batch_tracked_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        // PR-4 gate extended to batches: export a batch-tracked company to JSON AND XML, import EACH into a fresh
        // (differently-Guid'd) company through the engine-routed CompanyImportService, and assert every batch
        // master + count is EQUAL source == target and every stock figure reconciles to the paisa.
        var source = CanonicalFixture.BuildBright();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertBatchesReconcileAcrossCompanies(source, fresh);
        }
    }

    [Fact]
    public void Corrupted_batch_import_is_rejected_and_leaves_pre_existing_data_unchanged()
    {
        // A batch-tracked export corrupted at the wire (a batch master references a stock item that is neither
        // imported nor present) must be rejected (Applied=false) with the target byte-for-byte unchanged.
        var source = CanonicalFixture.BuildBright();
        var (parsed, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        // Corrupt exactly one batch master's stock-item reference to a random, absent id (a genuine dangling ref
        // — not a wholesale rename, which would stay internally consistent). The document is still well-formed.
        var badBatches = parsed!.Payload.BatchMasters
            .Select((b, ix) => ix == 0 ? b with { StockItemId = Guid.NewGuid() } : b).ToList();
        var model = parsed with { Payload = parsed.Payload with { BatchMasters = badBatches } };

        var target = FreshTarget();
        // Pre-seed the target with a batch so we can prove it survives an aborted import.
        var inv = new Apex.Ledger.Services.InventoryService(target);
        var sg = inv.CreateStockGroup("Existing");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var existingItem = inv.CreateStockItem("Existing Item", sg.Id, nos.Id);
        new Apex.Ledger.Services.BatchService(target).CreateBatch(existingItem.Id, "PRE-1",
            inwardQuantity: 7m, inwardRate: Money.FromRupees(3m));
        var before = Encoding.UTF8.GetString(CanonicalJson.Export(target));

        var result = new CompanyImportService(target).Apply(model);
        Assert.False(result.Applied);

        var after = Encoding.UTF8.GetString(CanonicalJson.Export(target));
        Assert.Equal(before, after); // byte-for-byte unchanged: the pre-existing batch is intact
        Assert.Single(target.BatchMasters);
        Assert.Equal("PRE-1", target.BatchMasters[0].BatchNumber);
    }

    // ------------------------------------------------------------------ BOM + Manufacturing Journal (Phase 6 slice 2)

    [Fact]
    public void Json_round_trips_bom_masters_and_manufacturing_journal()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);
        AssertBom(model!, company);
    }

    [Fact]
    public void Xml_round_trips_bom_masters_and_manufacturing_journal()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(errors);
        AssertBom(model!, company);
    }

    [Fact]
    public void Bom_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        // PR-4 gate extended to BOMs + Manufacturing Journals: export a company carrying a multi-line BOM (with a
        // by-product/scrap carve-out line) and a posted Manufacturing Journal to JSON AND XML, import EACH into a
        // fresh (differently-Guid'd) company through the engine-routed CompanyImportService, and assert every BOM +
        // line count is EQUAL source == target and every stock/valuation figure reconciles to the paisa.
        var source = CanonicalFixture.BuildBright();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertBomReconcilesAcrossCompanies(source, fresh);
        }
    }

    [Fact]
    public void Batch_tracked_manufacture_round_trips_lossless_json_and_xml()
    {
        // A10 gap: no existing round-trip exercised a manufacture that CONSUMES a batch-tracked component (the old
        // aggregate label-less consumption path). Build a small company where the component is maintained in
        // batches, manufacture drawing FIFO across two lots (each valued at its own rate, DP-8), export to JSON AND
        // XML, import EACH into a fresh company, and assert the batch on-hand + FG value reconcile to the paisa.
        var source = BuildBatchManufactureCompany();
        var asOf = new DateOnly(2021, 4, 30);

        // Baseline (source): 150 A consumed spans B1 (100 @ ₹10) + B2 (50 @ ₹20) = ₹2000 FG value.
        var srcFg = source.FindStockItemByName("Batched Gadget")!;
        var srcComp = source.FindStockItemByName("Batched Part")!;
        var srcGodown = source.MainLocation!.Id;
        Assert.Equal(Money.FromRupees(2000m),
            new Apex.Ledger.Services.StockValuationService(source).ClosingValue(srcFg.Id, asOf).Value);
        Assert.Equal(0m, new Apex.Ledger.Services.InventoryLedger(source).OnHand(srcComp.Id, srcGodown, "B1", asOf));
        Assert.Equal(50m, new Apex.Ledger.Services.InventoryLedger(source).OnHand(srcComp.Id, srcGodown, "B2", asOf));

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            var tgtFg = fresh.FindStockItemByName("Batched Gadget")!;
            var tgtComp = fresh.FindStockItemByName("Batched Part")!;
            var tgtGodown = fresh.MainLocation!.Id;
            var onHand = new Apex.Ledger.Services.InventoryLedger(fresh);

            // The per-batch draw-down survived: B1 fully consumed, B2 half consumed (the outward carried its label).
            Assert.Equal(0m, onHand.OnHand(tgtComp.Id, tgtGodown, "B1", asOf));
            Assert.Equal(50m, onHand.OnHand(tgtComp.Id, tgtGodown, "B2", asOf));
            // The finished-good value reconciles to the paisa, valuing each lot at its own inward rate (DP-8).
            Assert.Equal(Money.FromRupees(2000m),
                new Apex.Ledger.Services.StockValuationService(fresh).ClosingValue(tgtFg.Id, asOf).Value);
        }
    }

    /// <summary>A minimal company whose manufacture consumes a batch-tracked component across two FIFO lots.</summary>
    private static Company BuildBatchManufactureCompany()
    {
        var company = CompanyFactory.CreateSeeded("Batch Mfg Co", new DateOnly(2021, 4, 1), new DateOnly(2021, 4, 1));
        var inv = new Apex.Ledger.Services.InventoryService(company);
        var sg = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var godown = company.MainLocation!.Id;

        var fg = inv.CreateStockItem("Batched Gadget", sg.Id, nos.Id);
        var comp = inv.CreateStockItem("Batched Part", sg.Id, nos.Id);
        comp.MaintainInBatches = true; // batch-tracked, expiry off ⇒ FIFO-by-inward (DP-1)

        var posting = new Apex.Ledger.Services.InventoryPostingService(company);
        var receiptType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id;
        posting.Post(new InventoryVoucher(Guid.NewGuid(), receiptType, new DateOnly(2021, 4, 5),
            new[] { new InventoryAllocation(comp.Id, godown, 100m, StockDirection.Inward, Money.FromRupees(10m), "B1") }));
        posting.Post(new InventoryVoucher(Guid.NewGuid(), receiptType, new DateOnly(2021, 4, 8),
            new[] { new InventoryAllocation(comp.Id, godown, 100m, StockDirection.Inward, Money.FromRupees(20m), "B2") }));

        var bom = new BomService(company).CreateBom(fg.Id, "Std", unitOfManufacture: 1m,
            new[] { new BomLine(BomLineType.Component, comp.Id, quantityPerBlock: 150m) });
        var mfg = new ManufacturingJournalService(company);
        var mfgType = mfg.CreateManufacturingJournalType("Manufacturing Journal");
        mfg.Manufacture(mfgType.Id, bom.Id, quantity: 1m, date: new DateOnly(2021, 4, 15),
            consumptionGodownId: godown, productionGodownId: godown);
        return company;
    }

    [Fact]
    public void Corrupted_bom_import_is_rejected_and_leaves_pre_existing_data_unchanged()
    {
        // A BOM export corrupted at the wire (a BOM references a finished-good stock item that is neither imported
        // nor present) must be rejected (Applied=false) with the target byte-for-byte unchanged.
        var source = CanonicalFixture.BuildBright();
        var (parsed, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        // Corrupt exactly one BOM's finished-good stock-item reference to a random, absent id (a genuine dangling
        // ref — not a wholesale rename, which would stay internally consistent). The document is still well-formed.
        var badBoms = parsed!.Payload.BillsOfMaterials
            .Select((b, ix) => ix == 0 ? b with { StockItemId = Guid.NewGuid() } : b).ToList();
        var model = parsed with { Payload = parsed.Payload with { BillsOfMaterials = badBoms } };

        var target = FreshTarget();
        // Pre-seed the target with a BOM so we can prove it survives an aborted import.
        var inv = new Apex.Ledger.Services.InventoryService(target);
        var sg = inv.CreateStockGroup("Existing");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var existingFg = inv.CreateStockItem("Existing Gadget", sg.Id, nos.Id);
        var existingComp = inv.CreateStockItem("Existing Comp", sg.Id, nos.Id);
        new Apex.Ledger.Services.BomService(target).CreateBom(existingFg.Id, "PRE", unitOfManufacture: 1m,
            new[] { new BomLine(BomLineType.Component, existingComp.Id, quantityPerBlock: 2m) });
        var before = Encoding.UTF8.GetString(CanonicalJson.Export(target));

        var result = new CompanyImportService(target).Apply(model);
        Assert.False(result.Applied);

        var after = Encoding.UTF8.GetString(CanonicalJson.Export(target));
        Assert.Equal(before, after); // byte-for-byte unchanged: the pre-existing BOM is intact
        Assert.Single(target.BillsOfMaterials);
        Assert.Equal("PRE", target.BillsOfMaterials[0].Name);
    }

    // ------------------------------------------------------------------ advanced inventory (Phase 6 slices 3–8)

    [Fact]
    public void Json_round_trips_advanced_inventory_price_reorder_pos_jobwork()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(company));
        Assert.Empty(errors);
        AssertAdvancedInventoryProjection(model!, company);
    }

    [Fact]
    public void Xml_round_trips_advanced_inventory_price_reorder_pos_jobwork()
    {
        var company = CanonicalFixture.BuildBright();
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(company));
        Assert.Empty(errors);
        AssertAdvancedInventoryProjection(model!, company);
    }

    [Fact]
    public void Advanced_inventory_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        // PR-4 gate extended to Phase-6 slices 3–8 (the previously-DROPPED entities): export a company carrying
        // price levels/lists, reorder definitions, an additional-cost ledger + loaded Stock-Journal transfer, an
        // Actual-vs-Billed item invoice, a POS Sales type + POS sale, and a Job Work Out Order + linked Material Out
        // to JSON AND XML, import EACH into a fresh (differently-Guid'd) company through the engine-routed
        // CompanyImportService, and assert every master/sub-object COUNT is EQUAL source == target and every figure
        // reconciles to the paisa. Before this task these entities were silently dropped on export+import.
        var source = CanonicalFixture.BuildBright();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertAdvancedInventoryReconcilesAcrossCompanies(source, fresh);
        }
    }

    /// <summary>The Phase-6 slice 3–8 entities survive the projection into the canonical model with every field.</summary>
    private static void AssertAdvancedInventoryProjection(CanonicalModel model, Company company)
    {
        // Company F11 toggles.
        Assert.True(model.Company.UseSeparateActualBilledQuantity);
        Assert.True(model.Company.EnableMultiplePriceLevels);
        Assert.True(model.Company.EnableJobOrderProcessing);

        // Slice 5: price level + list + party default level.
        var wholesale = company.FindPriceLevelByName("Wholesale")!;
        Assert.Contains(model.Payload.PriceLevels, x => x.Id == wholesale.Id && x.Name == "Wholesale");
        var gizmo = company.FindStockItemByName("Gizmo")!;
        var listDto = model.Payload.PriceLists.Single(x => x.PriceLevelId == wholesale.Id && x.StockItemId == gizmo.Id);
        Assert.Equal(2, listDto.Slabs.Count);
        Assert.Null(listDto.Slabs[1].ToQty);                       // open-ended top
        Assert.Equal(5m, listDto.Slabs[1].DiscountPercent);
        Assert.Equal(11000L, listDto.Slabs[1].RatePaisa);          // ₹110
        var partyDto = model.Payload.Ledgers.Single(x => x.Name == "Ram & Co");
        Assert.Equal(wholesale.Id, partyDto.DefaultPriceLevelId);

        // Slice 6: two reorder definitions (Simple item + Advanced group).
        Assert.Equal(company.ReorderDefinitions.Count, model.Payload.ReorderDefinitions.Count);
        var itemDef = model.Payload.ReorderDefinitions.Single(x => x.Scope == "Item" && x.TargetId == gizmo.Id);
        Assert.Equal(25m, itemDef.ReorderQuantity);
        Assert.Equal(50m, itemDef.MinOrderQuantity);
        var advGrp = company.FindStockGroupByName("Advanced Goods")!;
        var grpDef = model.Payload.ReorderDefinitions.Single(x => x.Scope == "Group" && x.TargetId == advGrp.Id);
        Assert.True(grpDef.ReorderAdvanced);
        Assert.Equal("Months", grpDef.PeriodUnit);
        Assert.Equal("Higher", grpDef.Criteria);
        Assert.Equal(3, grpDef.PeriodCount);

        // Slice 3: additional-cost ledger + Track-Additional-Costs type + a loaded Stock-Journal transfer.
        var freightDto = model.Payload.Ledgers.Single(x => x.Name == "Freight Inward");
        Assert.Equal("ByValue", freightDto.MethodOfAppropriation);
        Assert.True(model.Payload.VoucherTypes.Single(x => x.Name == "Import Purchase").TrackAdditionalCosts);
        var transferDto = model.Payload.InventoryVouchers.Single(x => x.AdditionalCostLines.Count > 0);
        var acl = Assert.Single(transferDto.AdditionalCostLines);
        Assert.Equal(5000L, acl.AmountPaisa);                      // ₹50 freight

        // Slice 4: Actual (10) vs Billed (8) on the sale item line.
        var abDto = model.Payload.Vouchers.Single(x => x.Narration == "Sale 10 Gizmo, billed 8 (2 free)");
        var abLine = Assert.Single(abDto.InventoryLines);
        Assert.Equal(10m, abLine.Quantity);
        Assert.Equal(8m, abLine.BilledQuantity);

        // Slice 7: POS type + config + a POS sale settled by a Cash tender.
        var posTypeDto = model.Payload.VoucherTypes.Single(x => x.Name == "POS Sale");
        Assert.True(posTypeDto.UseForPos);
        Assert.NotNull(posTypeDto.PosConfig);
        Assert.Equal("Retail Invoice", posTypeDto.PosConfig!.DefaultTitle);
        Assert.Equal(company.MainLocation!.Id, posTypeDto.PosConfig.DefaultGodownId);
        var tld = Assert.Single(posTypeDto.PosConfig.TenderLedgerDefaults);
        Assert.Equal("Cash", tld.TenderType);
        var posDto = model.Payload.Vouchers.Single(x => x.Narration == "POS retail sale");
        var tender = Assert.Single(posDto.PosTenders);
        Assert.Equal("Cash", tender.TenderType);
        Assert.Equal(45000L, tender.AmountPaisa);                  // ₹450 payable (residual)
        Assert.Equal(50000L, tender.TenderedPaisa);                // ₹500 tendered
        Assert.Equal(5000L, tender.ChangePaisa);                   // ₹50 change

        // Slice 8: Job Work Out Order payload + a Material Out linking it.
        var orderDto = model.Payload.InventoryVouchers.Single(x => x.JobWorkOrder is not null);
        Assert.Equal("Out", orderDto.JobWorkOrder!.Direction);
        Assert.Equal("JW/001", orderDto.JobWorkOrder.OrderNo);
        Assert.Equal(100m, orderDto.JobWorkOrder.FinishedGoodQuantity);
        Assert.Equal(3000L, orderDto.JobWorkOrder.FinishedGoodRatePaisa);
        var jwLine = Assert.Single(orderDto.JobWorkOrder.Lines);
        Assert.Equal("PendingToIssue", jwLine.Track);
        Assert.Equal(200m, jwLine.Quantity);
        var matOutDto = model.Payload.InventoryVouchers.Single(x => x.OrderLinks.Count > 0);
        Assert.Equal(orderDto.Id, Assert.Single(matOutDto.OrderLinks));
    }

    /// <summary>
    /// After an export → import into a fresh company (by name, differently-Guid'd), every Phase-6 slice 3–8 entity
    /// reconciles count-for-count and figure-for-figure to the paisa across the two companies.
    /// </summary>
    private static void AssertAdvancedInventoryReconcilesAcrossCompanies(Company source, Company target)
    {
        // F11 company toggles survived.
        Assert.Equal(source.UseSeparateActualBilledQuantity, target.UseSeparateActualBilledQuantity);
        Assert.Equal(source.EnableMultiplePriceLevels, target.EnableMultiplePriceLevels);
        Assert.Equal(source.EnableJobOrderProcessing, target.EnableJobOrderProcessing);

        // ---- slice 5: price levels + lists + party default level (all resolve by name across companies) ----
        Assert.Equal(source.PriceLevels.Count, target.PriceLevels.Count);
        Assert.Equal(source.PriceLists.Count, target.PriceLists.Count);
        var srcGizmo = source.FindStockItemByName("Gizmo")!;
        var tgtGizmo = target.FindStockItemByName("Gizmo")!;
        var srcWholesale = source.FindPriceLevelByName("Wholesale")!;
        var tgtWholesale = target.FindPriceLevelByName("Wholesale")!;
        var srcList = source.PriceListsFor(srcWholesale.Id, srcGizmo.Id).Single();
        var tgtList = target.PriceListsFor(tgtWholesale.Id, tgtGizmo.Id).Single();
        Assert.Equal(srcList.ApplicableFrom, tgtList.ApplicableFrom);
        Assert.Equal(srcList.Slabs.Count, tgtList.Slabs.Count);
        for (var i = 0; i < srcList.Slabs.Count; i++)
        {
            Assert.Equal(srcList.Slabs[i].FromQty, tgtList.Slabs[i].FromQty);
            Assert.Equal(srcList.Slabs[i].ToQty, tgtList.Slabs[i].ToQty);
            Assert.Equal(srcList.Slabs[i].Rate, tgtList.Slabs[i].Rate);                 // paisa-exact
            Assert.Equal(srcList.Slabs[i].DiscountPercent, tgtList.Slabs[i].DiscountPercent);
        }
        // The party's default price level resolves to the TARGET's own Wholesale level (RQ-30).
        Assert.Equal(tgtWholesale.Id, target.FindLedgerByName("Ram & Co")!.DefaultPriceLevelId);

        // ---- slice 6: reorder definitions reconcile per (scope, resolved-target) ----
        Assert.Equal(source.ReorderDefinitions.Count, target.ReorderDefinitions.Count);
        var tgtItemDef = target.FindReorderDefinition(ReorderScope.Item, tgtGizmo.Id)!;
        Assert.Equal(25m, tgtItemDef.ReorderQuantity);
        Assert.Equal(50m, tgtItemDef.MinOrderQuantity);
        var tgtAdvGrp = target.FindStockGroupByName("Advanced Goods")!;
        var tgtGrpDef = target.FindReorderDefinition(ReorderScope.Group, tgtAdvGrp.Id)!;
        Assert.True(tgtGrpDef.ReorderAdvanced);
        Assert.True(tgtGrpDef.MinQtyAdvanced);
        Assert.Equal(3, tgtGrpDef.PeriodCount);
        Assert.Equal(ExpiryPeriodUnit.Months, tgtGrpDef.PeriodUnit);
        Assert.Equal(ReorderCriteria.Higher, tgtGrpDef.Criteria);

        // ---- slice 3: additional-cost ledger method + Track-Additional-Costs type + loaded transfer line ----
        Assert.Equal(MethodOfAppropriation.ByValue, target.FindLedgerByName("Freight Inward")!.MethodOfAppropriation);
        Assert.True(target.FindVoucherTypeByName("Import Purchase")!.TrackAdditionalCosts);
        var tgtTransfer = target.InventoryVouchers.Single(v => v.AdditionalCostLines.Count > 0);
        var tgtAcl = Assert.Single(tgtTransfer.AdditionalCostLines);
        Assert.Equal(Money.FromRupees(50m), tgtAcl.Amount);
        Assert.Equal(target.FindLedgerByName("Freight Inward")!.Id, tgtAcl.LedgerId);

        // ---- slice 4: Actual vs Billed survived on the sale item line ----
        var tgtAbSale = target.Vouchers.Single(v => v.Narration == "Sale 10 Gizmo, billed 8 (2 free)");
        var tgtAbLine = Assert.Single(tgtAbSale.InventoryLines);
        Assert.Equal(10m, tgtAbLine.Quantity);
        Assert.Equal(8m, tgtAbLine.BilledQuantity);
        Assert.Equal(Money.FromRupees(1200m), tgtAbLine.Value); // billed × rate

        // ---- slice 7: POS type + config + the POS sale's Cash tender split ----
        var tgtPosType = target.FindVoucherTypeByName("POS Sale")!;
        Assert.True(tgtPosType.IsPosSales);
        Assert.NotNull(tgtPosType.PosConfig);
        Assert.Equal(target.MainLocation!.Id, tgtPosType.PosConfig!.DefaultGodownId);
        Assert.True(tgtPosType.PosConfig.PrintAfterSave);
        Assert.Equal("Retail Invoice", tgtPosType.PosConfig.DefaultTitle);
        Assert.Equal(target.FindLedgerByName("Cash")!.Id, tgtPosType.PosConfig.TenderLedgerDefault(PosTenderType.Cash));
        var tgtPosSale = target.Vouchers.Single(v => v.Narration == "POS retail sale");
        var tgtTender = Assert.Single(tgtPosSale.PosTenders);
        Assert.Equal(PosTenderType.Cash, tgtTender.Type);
        Assert.Equal(Money.FromRupees(450m), tgtTender.Amount);
        Assert.Equal(Money.FromRupees(500m), tgtTender.Tendered);
        Assert.Equal(Money.FromRupees(50m), tgtTender.Change);
        Assert.Equal(target.FindLedgerByName("Cash")!.Id, tgtTender.LedgerId);

        // ---- slice 8: Job Work Out Order + the linked Material Out issue reconcile, incl. third-party on-hand ----
        Assert.Equal(source.InventoryVouchers.Count, target.InventoryVouchers.Count);
        var tgtOrder = target.InventoryVouchers.Single(v => v.JobWorkOrder is not null);
        Assert.Equal(JobWorkDirection.Out, tgtOrder.JobWorkOrder!.Direction);
        Assert.Equal("JW/001", tgtOrder.JobWorkOrder.OrderNo);
        Assert.Equal(Money.FromRupees(30m), tgtOrder.JobWorkOrder.FinishedGoodRate);
        Assert.Equal(target.FindStockItemByName("JW Assembly")!.Id, tgtOrder.JobWorkOrder.FinishedGoodStockItemId);
        var tgtJwLine = Assert.Single(tgtOrder.JobWorkOrder.Lines);
        Assert.Equal(JobWorkComponentTrack.PendingToIssue, tgtJwLine.Track);
        Assert.Equal(target.FindStockItemByName("JW Raw")!.Id, tgtJwLine.ComponentStockItemId);
        Assert.Equal(200m, tgtJwLine.Quantity);

        var tgtMatOut = target.InventoryVouchers.Single(v => v.OrderLinks.Count > 0);
        Assert.Equal(tgtOrder.Id, Assert.Single(tgtMatOut.OrderLinks)); // link resolved to the target order voucher

        // The Material Out moved 200 JW Raw to the third-party 'Worker Site' godown (balanced transfer) — on-hand
        // reconciles to the paisa/qty under the target's own inventory ledger.
        var asOf = new DateOnly(2021, 4, 30);
        var tgtRaw = target.FindStockItemByName("JW Raw")!;
        var tgtWorker = target.FindGodownByName("Worker Site")!;
        var tgtMain = target.MainLocation!.Id;
        var onHand = new Apex.Ledger.Services.InventoryLedger(target);
        Assert.Equal(200m, onHand.OnHand(tgtRaw.Id, tgtWorker.Id, asOf));
        Assert.Equal(300m, onHand.OnHand(tgtRaw.Id, tgtMain, asOf)); // 500 opening − 200 issued

        // Gizmo on-hand reconciles across both companies (200 opening − 20 transfer − 10 AB sale − 3 POS = 167).
        var srcGizmoOnHand = new Apex.Ledger.Services.InventoryLedger(source).OnHand(srcGizmo.Id, source.MainLocation!.Id, asOf);
        var tgtGizmoOnHand = onHand.OnHand(tgtGizmo.Id, tgtMain, asOf);
        Assert.Equal(srcGizmoOnHand, tgtGizmoOnHand);
        Assert.Equal(167m, tgtGizmoOnHand);
    }

    // ------------------------------------------------------------------ XXE safety (XML only)

    [Fact]
    public void Xml_with_doctype_and_internal_entity_is_rejected_not_expanded()
    {
        // A classic XXE payload: a DOCTYPE with an internal entity referenced in a value. The parser MUST reject it
        // as a structured error (DtdProcessing.Prohibit), never expand the entity, and never throw to the caller.
        var xxe =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE apexExport [ <!ENTITY xxe \"INJECTED\"> ]>" +
            "<apexExport formatVersion=\"1\" schemaVersion=\"14\">" +
            "<company id=\"" + Guid.NewGuid() + "\" name=\"&xxe;\" financialYearStart=\"2021-04-01\" " +
            "booksBeginFrom=\"2021-04-01\" decimalPlaces=\"2\"/></apexExport>";
        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(xxe));

        Assert.Null(model);
        Assert.NotEmpty(errors);
        // The entity text must NOT have been expanded into any error string.
        Assert.DoesNotContain(errors, e => e.Contains("INJECTED", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ strict ISO date parsing

    [Fact]
    public void Locale_ambiguous_dates_are_rejected_json_and_xml()
    {
        // "01/04/2021" (dd/MM/yyyy or MM/dd/yyyy) is ambiguous → rejected; only yyyy-MM-dd is accepted.
        var badJson = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":1,\"schemaVersion\":14,\"company\":{\"id\":\"" + Guid.NewGuid() +
            "\",\"name\":\"X\",\"financialYearStart\":\"01/04/2021\",\"booksBeginFrom\":\"2021-04-01\",\"decimalPlaces\":2},\"payload\":{}}");
        var (jm, je) = CanonicalJson.Parse(badJson);
        Assert.Null(jm);
        Assert.Contains(je, e => e.Contains("financialYearStart"));

        var badXml = Encoding.UTF8.GetBytes(
            "<apexExport formatVersion=\"1\" schemaVersion=\"14\">" +
            "<company id=\"" + Guid.NewGuid() + "\" name=\"X\" financialYearStart=\"01/04/2021\" " +
            "booksBeginFrom=\"2021-04-01\" decimalPlaces=\"2\"/></apexExport>");
        var (xm, xe) = CanonicalXml.Parse(badXml);
        Assert.Null(xm);
        Assert.Contains(xe, e => e.Contains("financialYearStart"));
    }

    // ------------------------------------------------------------------ assertions

    private static void AssertNewDataTypes(CanonicalModel model, Company company)
    {
        // Cost category + centre.
        var category = company.FindCostCategoryByName("Departments")!;
        var catDto = model.Payload.CostCategories.Single(x => x.Id == category.Id);
        Assert.True(catDto.AllocateRevenueItems);
        Assert.True(catDto.AllocateNonRevenueItems);
        var centre = company.FindCostCentreByName("Sales Dept")!;
        var centreDto = model.Payload.CostCentres.Single(x => x.Id == centre.Id);
        Assert.Equal(category.Id, centreDto.CategoryId);

        // Cost allocation on the Salary line.
        var salary = company.FindLedgerByName("Salary")!;
        var salaryVoucher = company.Vouchers.Single(v => v.Lines.Any(l => l.LedgerId == salary.Id));
        var salaryDto = model.Payload.Vouchers.Single(x => x.Id == salaryVoucher.Id);
        var costLine = salaryDto.Lines.Single(l => l.LedgerId == salary.Id);
        var alloc = Assert.Single(costLine.CostAllocations);
        Assert.Equal(category.Id, alloc.CategoryId);
        Assert.Equal(centre.Id, alloc.CentreId);
        Assert.Equal(300000L, alloc.AmountPaisa); // ₹3000

        // Bank ledger + bank allocation.
        var bank = company.FindLedgerByName("HDFC Bank")!;
        var bankDto = model.Payload.Ledgers.Single(x => x.Id == bank.Id);
        Assert.True(bankDto.EnableChequePrinting);
        Assert.Equal("HDFC Bank", bankDto.ChequePrintingBankName);
        var bankLineDto = salaryDto.Lines.Single(l => l.LedgerId == bank.Id);
        Assert.NotNull(bankLineDto.BankAllocation);
        Assert.Equal("ChequeOrDD", bankLineDto.BankAllocation!.TransactionType);
        Assert.Equal("000123", bankLineDto.BankAllocation.InstrumentNumber);
        Assert.Equal("2021-04-08", bankLineDto.BankAllocation.BankDate);

        // Currency + exchange rate.
        var usd = company.FindCurrencyByName("USD")!;
        var usdDto = model.Payload.Currencies.Single(x => x.Id == usd.Id);
        Assert.Equal("$", usdDto.Symbol);
        Assert.False(usdDto.IsBaseCurrency);
        var rate = company.ExchangeRates.Single(r => r.CurrencyId == usd.Id);
        var rateDto = model.Payload.ExchangeRates.Single(x => x.Id == rate.Id);
        Assert.Equal(75_000_000L, rateDto.StandardRateMicro);   // 75 × 1,000,000
        Assert.Equal(75_500_000L, rateDto.SellingRateMicro);
        Assert.Equal(74_500_000L, rateDto.BuyingRateMicro);

        // Forex line on the foreign-currency ledger.
        var foreignExp = company.FindLedgerByName("Foreign Consulting")!;
        Assert.Equal(usd.Id, model.Payload.Ledgers.Single(x => x.Id == foreignExp.Id).CurrencyId);
        var forexVoucher = company.Vouchers.Single(v => v.Lines.Any(l => l.LedgerId == foreignExp.Id));
        var forexLineDto = model.Payload.Vouchers.Single(x => x.Id == forexVoucher.Id)
            .Lines.Single(l => l.LedgerId == foreignExp.Id);
        Assert.NotNull(forexLineDto.Forex);
        Assert.Equal(usd.Id, forexLineDto.Forex!.CurrencyId);
        Assert.Equal(10000L, forexLineDto.Forex.ForexAmountPaisa);  // US$100 → 10000 paisa
        Assert.Equal(75_000_000L, forexLineDto.Forex.RateMicro);

        // Budget.
        var budget = company.FindBudgetByName("FY Budget")!;
        var budgetDto = model.Payload.Budgets.Single(x => x.Id == budget.Id);
        var bLine = Assert.Single(budgetDto.Lines);
        Assert.Equal(salary.Id, bLine.LedgerId);
        Assert.Equal(3_600_000L, bLine.AmountPaisa); // ₹36000

        // Scenario.
        var scenario = company.FindScenarioByName("Provisional")!;
        var scenarioDto = model.Payload.Scenarios.Single(x => x.Id == scenario.Id);
        Assert.True(scenarioDto.IncludeActuals);
        Assert.Single(scenarioDto.IncludedTypeIds);

        // Inventory voucher (the "GRN for 5 widgets" Receipt Note; the fixture also adds a batch GRN separately).
        var invVoucher = company.InventoryVouchers.Single(v => v.Narration == "GRN for 5 widgets");
        var invDto = model.Payload.InventoryVouchers.Single(x => x.Id == invVoucher.Id);
        var invAlloc = Assert.Single(invDto.Allocations);
        Assert.Equal(5m, invAlloc.Quantity);
        Assert.Equal("Inward", invAlloc.Direction);
        Assert.Equal(20000L, invAlloc.RatePaisa); // ₹200
        Assert.Equal("B2", invAlloc.BatchLabel);
    }

    private static void AssertNoTally(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ batch assertions

    /// <summary>The batch-tracked item + its two batch masters survive the projection with every field intact.</summary>
    private static void AssertBatches(CanonicalModel model, Company company)
    {
        var med = company.FindStockItemByName("Paracetamol")!;
        var medDto = model.Payload.StockItems.Single(x => x.Id == med.Id);
        Assert.True(medDto.MaintainInBatches);
        Assert.True(medDto.TrackManufacturingDate);
        Assert.True(medDto.UseExpiryDates);

        // Exactly the two batch masters we created, matched by number.
        var srcBatches = company.BatchesFor(med.Id).ToList();
        Assert.Equal(2, srcBatches.Count);
        Assert.Equal(company.BatchMasters.Count, model.Payload.BatchMasters.Count);

        var srcA = company.FindBatchByNumber(med.Id, "LOT-A")!;
        var aDto = model.Payload.BatchMasters.Single(x => x.Id == srcA.Id);
        Assert.Equal(med.Id, aDto.StockItemId);
        Assert.Equal("LOT-A", aDto.BatchNumber);
        Assert.Equal("2021-01-10", aDto.ManufacturingDate);
        Assert.Equal("2023-01-10", aDto.ExpiryDate);
        Assert.Null(aDto.ExpiryPeriod);
        Assert.Equal(med.Id == srcA.StockItemId ? srcA.GodownId : null, aDto.GodownId);
        Assert.Equal(250m, aDto.InwardQuantity);
        Assert.Equal(MoneyCodec.ToPaisa(Money.FromRupees(12.34m)), aDto.InwardRatePaisa);

        var srcB = company.FindBatchByNumber(med.Id, "LOT-B")!;
        var bDto = model.Payload.BatchMasters.Single(x => x.Id == srcB.Id);
        Assert.Equal("2021-03-01", bDto.ManufacturingDate);
        Assert.Null(bDto.ExpiryDate);
        Assert.Equal("18 Months", bDto.ExpiryPeriod);
        Assert.Null(bDto.GodownId);
        Assert.Null(bDto.InwardQuantity);
        Assert.Null(bDto.InwardRatePaisa);
    }

    /// <summary>
    /// After an export → import into a fresh company (by name, differently-Guid'd), the batch masters reconcile
    /// count-for-count and figure-for-figure to the paisa — matched by (item name, batch number).
    /// </summary>
    private static void AssertBatchesReconcileAcrossCompanies(Company source, Company target)
    {
        Assert.Equal(source.BatchMasters.Count, target.BatchMasters.Count);

        foreach (var srcItem in source.StockItems)
        {
            var tgtItem = target.FindStockItemByName(srcItem.Name)!;
            Assert.Equal(srcItem.MaintainInBatches, tgtItem.MaintainInBatches);
            Assert.Equal(srcItem.TrackManufacturingDate, tgtItem.TrackManufacturingDate);
            Assert.Equal(srcItem.UseExpiryDates, tgtItem.UseExpiryDates);

            var srcBatches = source.BatchesFor(srcItem.Id).ToList();
            var tgtBatches = target.BatchesFor(tgtItem.Id).ToList();
            Assert.Equal(srcBatches.Count, tgtBatches.Count);

            foreach (var sb in srcBatches)
            {
                var tb = target.FindBatchByNumber(tgtItem.Id, sb.BatchNumber)!;
                Assert.Equal(sb.ManufacturingDate, tb.ManufacturingDate);
                Assert.Equal(sb.ExpiryDate, tb.ExpiryDate);
                Assert.Equal(sb.ExpiryPeriod, tb.ExpiryPeriod);
                Assert.Equal(sb.ResolvedExpiryDate, tb.ResolvedExpiryDate);
                Assert.Equal(sb.InwardQuantity, tb.InwardQuantity);
                Assert.Equal(sb.InwardRate, tb.InwardRate);            // paisa-exact
                // The inward-layer godown resolves by name across companies.
                if (sb.GodownId is { } sgId)
                {
                    var srcGodown = source.FindGodown(sgId)!;
                    Assert.Equal(target.FindGodownByName(srcGodown.Name)!.Id, tb.GodownId);
                }
                else
                {
                    Assert.Null(tb.GodownId);
                }
            }
        }
    }

    // ------------------------------------------------------------------ BOM assertions

    /// <summary>The multi-line BOM master + its item's Set-Components flag + the Manufacturing-Journal voucher type
    /// survive the projection with every field intact (RQ-9..RQ-13, DP-3).</summary>
    private static void AssertBom(CanonicalModel model, Company company)
    {
        var fg = company.FindStockItemByName("Assembled Gadget")!;
        var fgDto = model.Payload.StockItems.Single(x => x.Id == fg.Id);
        Assert.True(fgDto.SetComponents); // RQ-10: an item with a BOM is a manufactured item

        // Exactly the one BOM we created, matched by id, with all four lines and their types/values.
        Assert.Equal(company.BillsOfMaterials.Count, model.Payload.BillsOfMaterials.Count);
        var srcBom = company.BomsFor(fg.Id).Single();
        var bomDto = model.Payload.BillsOfMaterials.Single(x => x.Id == srcBom.Id);
        Assert.Equal(fg.Id, bomDto.StockItemId);
        Assert.Equal("Standard", bomDto.Name);
        Assert.Equal(10m, bomDto.UnitOfManufacture);
        Assert.Equal(4, bomDto.Lines.Count);

        var compA = company.FindStockItemByName("Raw Part A")!;
        var aLine = bomDto.Lines.Single(l => l.ComponentStockItemId == compA.Id);
        Assert.Equal("Component", aLine.LineType);
        Assert.Equal(2m, aLine.QuantityPerBlock);
        Assert.Equal(company.MainLocation!.Id, aLine.GodownId);
        Assert.Null(aLine.RatePaisa);
        Assert.Null(aLine.PercentOfFinishedGoodCost);

        var compB = company.FindStockItemByName("Raw Part B")!;
        var bLine = bomDto.Lines.Single(l => l.ComponentStockItemId == compB.Id);
        Assert.Equal("Component", bLine.LineType);
        Assert.Equal(3m, bLine.QuantityPerBlock);
        Assert.Null(bLine.GodownId); // BOM line godown = resolve-at-posting

        var scrap = company.FindStockItemByName("Metal Scrap")!;
        var scrapLine = bomDto.Lines.Single(l => l.ComponentStockItemId == scrap.Id);
        Assert.Equal("Scrap", scrapLine.LineType);
        Assert.Equal(1m, scrapLine.QuantityPerBlock);
        Assert.Equal(MoneyCodec.ToPaisa(Money.FromRupees(2m)), scrapLine.RatePaisa); // DP-3 explicit rate basis
        Assert.Null(scrapLine.PercentOfFinishedGoodCost);

        var co = company.FindStockItemByName("Side Product")!;
        var coLine = bomDto.Lines.Single(l => l.ComponentStockItemId == co.Id);
        Assert.Equal("CoProduct", coLine.LineType);
        Assert.Null(coLine.RatePaisa);
        Assert.Equal(5m, coLine.PercentOfFinishedGoodCost); // DP-3 percent basis

        // The Manufacturing-Journal voucher type round-trips its flag (RQ-11).
        var mfgType = company.VoucherTypes.Single(t => t.IsManufacturingJournal);
        var mfgTypeDto = model.Payload.VoucherTypes.Single(x => x.Id == mfgType.Id);
        Assert.True(mfgTypeDto.UseAsManufacturingJournal);
        Assert.Equal("StockJournal", mfgTypeDto.BaseType);

        // The posted Manufacturing Journal is a Stock-Journal inventory voucher (source + destination allocations).
        var mfgVoucher = company.InventoryVouchers.Single(v => v.TypeId == mfgType.Id);
        var mfgDto = model.Payload.InventoryVouchers.Single(x => x.Id == mfgVoucher.Id);
        Assert.NotEmpty(mfgDto.Allocations);            // components consumed (source, outward)
        Assert.NotEmpty(mfgDto.DestinationAllocations); // FG + carve-outs produced (destination, inward)
    }

    /// <summary>
    /// After an export → import into a fresh company (by name, differently-Guid'd), the BOM masters reconcile
    /// count-for-count and line-for-line, the finished-good Set-Components flag and Manufacturing-Journal type flag
    /// survive, and the manufactured finished-good stock value reconciles to the paisa — matched by names.
    /// </summary>
    private static void AssertBomReconcilesAcrossCompanies(Company source, Company target)
    {
        Assert.Equal(source.BillsOfMaterials.Count, target.BillsOfMaterials.Count);

        foreach (var srcBom in source.BillsOfMaterials)
        {
            var srcFg = source.FindStockItem(srcBom.StockItemId)!;
            var tgtFg = target.FindStockItemByName(srcFg.Name)!;
            Assert.True(tgtFg.SetComponents); // RQ-10 survived

            var tgtBom = target.FindBomByName(tgtFg.Id, srcBom.Name)!;
            Assert.Equal(srcBom.UnitOfManufacture, tgtBom.UnitOfManufacture);
            Assert.Equal(srcBom.Lines.Count, tgtBom.Lines.Count);

            foreach (var sl in srcBom.Lines)
            {
                var srcComp = source.FindStockItem(sl.ComponentStockItemId)!;
                var tgtComp = target.FindStockItemByName(srcComp.Name)!;
                var tl = tgtBom.Lines.Single(l => l.ComponentStockItemId == tgtComp.Id);
                Assert.Equal(sl.LineType, tl.LineType);
                Assert.Equal(sl.QuantityPerBlock, tl.QuantityPerBlock);
                Assert.Equal(sl.Rate, tl.Rate);                              // paisa-exact carve-out rate
                Assert.Equal(sl.PercentOfFinishedGoodCost, tl.PercentOfFinishedGoodCost);
                // A pinned BOM-line godown resolves by name across companies.
                if (sl.GodownId is { } sgId)
                    Assert.Equal(target.FindGodownByName(source.FindGodown(sgId)!.Name)!.Id, tl.GodownId);
                else
                    Assert.Null(tl.GodownId);
            }
        }

        // The Manufacturing-Journal voucher type + its posted manufacture survived, and the finished-good closing
        // stock value reconciles to the paisa (PR-4) — same value under both companies' valuation engines.
        var srcMfgType = source.VoucherTypes.Single(t => t.IsManufacturingJournal);
        var tgtMfgType = target.FindVoucherTypeByName(srcMfgType.Name)!;
        Assert.True(tgtMfgType.IsManufacturingJournal);

        foreach (var srcFg2 in source.StockItems.Where(i => source.BomsFor(i.Id).Any()))
        {
            var tgtFg2 = target.FindStockItemByName(srcFg2.Name)!;
            var srcVal = new Apex.Ledger.Services.StockValuationService(source)
                .ClosingValue(srcFg2.Id, new DateOnly(2021, 4, 30));
            var tgtVal = new Apex.Ledger.Services.StockValuationService(target)
                .ClosingValue(tgtFg2.Id, new DateOnly(2021, 4, 30));
            Assert.Equal(srcVal.Quantity, tgtVal.Quantity);
            Assert.Equal(srcVal.Value, tgtVal.Value); // paisa-exact finished-good stock value
        }

        // Conservation to the paisa (A10 finding #2): the fixture manufactures a finished good whose value ₹157.50
        // does NOT divide evenly across 20 units (₹7.875/unit) — the exact case the old single-rounded-rate line
        // shorted by 10 paisa. Pin the conserved value so the round-trip proves a NON-dividing manufacture survives
        // to the paisa in BOTH companies (not merely source == target). Components ₹70 + labour ₹100 − carve-outs
        // ₹12.50 (scrap ₹4 + co-product 5% of ₹170 = ₹8.50) = ₹157.50.
        var srcGadget = source.FindStockItemByName("Assembled Gadget")!;
        var tgtGadget = target.FindStockItemByName("Assembled Gadget")!;
        var asOf = new DateOnly(2021, 4, 30);
        Assert.Equal(Money.FromRupees(157.50m),
            new Apex.Ledger.Services.StockValuationService(source).ClosingValue(srcGadget.Id, asOf).Value);
        Assert.Equal(Money.FromRupees(157.50m),
            new Apex.Ledger.Services.StockValuationService(target).ClosingValue(tgtGadget.Id, asOf).Value);
    }

    // ------------------------------------------------------------------ Phase-6 EXIT GATE: full-set reconciliation

    /// <summary>
    /// The Phase-6 <b>exit-gate reconciliation</b> (PR-2, extended): the rich Bright fixture — which exercises the
    /// FULL advanced-inventory set (batch movement, BOM/Manufacturing Journal, additional-cost purchase transfer,
    /// Actual-vs-Billed invoice, price-list sale, reorder definitions, POS multi-tender sale, and a Job Work
    /// Material In/Out to a third-party godown) — reconciles into the <b>Stock Summary</b> and the <b>Balance
    /// Sheet</b> to the paisa. Proves (a) the per-item Stock Summary identity opening + inward − outward = closing
    /// holds for every row; (b) the closing stock is CONSISTENT across three independent engines — the Stock
    /// Summary total, the <see cref="StockValuationService"/> aggregate, and the derived Balance-Sheet
    /// Stock-in-Hand asset line — all equal to the paisa; and (c) that the ONLY Balance-Sheet imbalance is the
    /// fixture's deliberate ₹55,000 opening-balance gap (Capital ₹1,50,000 Cr vs ₹95,000 of opening asset
    /// debits) — i.e. every advanced-inventory voucher is self-balancing and leaks nothing into the statements.
    /// Concrete advanced-inventory closings are pinned (Gizmo 167 after the transfer/AB-sale/POS draw-downs; JW
    /// Raw split 300 main + 200 third-party). (The balanced-books Dr = Cr paisa gate — TotalAssets ==
    /// TotalLiabilities == ₹1,84,000 — is the sibling <c>BrightReVerificationTests.BR4</c> on the bright.json set.)
    /// </summary>
    [Fact]
    public void Bright_full_advanced_inventory_set_reconciles_into_stock_summary_and_balance_sheet_to_the_paisa()
    {
        var company = CanonicalFixture.BuildBright();
        var asOf = new DateOnly(2021, 4, 30);

        // ---- (a) Stock Summary per-row identity: opening + inward − outward == closing, and Σ rows == total ----
        var summary = StockSummary.Build(company, asOf);
        Assert.NotEmpty(summary.Rows);
        foreach (var row in summary.Rows)
            Assert.Equal(row.ClosingQuantity, row.OpeningQuantity + row.InwardQuantity - row.OutwardQuantity);

        var rowValueSum = summary.Rows.Aggregate(Money.Zero, (acc, r) => acc + r.ClosingValue);
        Assert.Equal(summary.TotalClosingValue, rowValueSum); // grand total foots to the paisa

        // ---- (b) closing stock CONSISTENT across three independent engines, to the paisa ----
        var valuationTotal = new StockValuationService(company).TotalClosingStockValue(asOf);
        Assert.Equal(valuationTotal, summary.TotalClosingValue);

        var bs = BalanceSheet.Build(company, asOf, ClosingStockMode.InventoryDerived);
        var stockInHand = bs.Assets.Single(a => a.Name == "Stock-in-Hand");
        Assert.Equal(summary.TotalClosingValue, stockInHand.Amount); // BS Stock-in-Hand == Stock Summary total

        // ---- (c) the ONLY imbalance is the fixture's deliberate ₹55,000 opening-balance gap, to the paisa ----
        // Every Phase-6 advanced-inventory voucher (transfer, AB sale, POS multi-tender, Material Out) is
        // self-balancing, so it contributes exactly ZERO to the Balance-Sheet imbalance: the gap stays pinned at
        // the base fixture's opening-balance difference (Capital ₹1,50,000 Cr − ₹95,000 opening asset Dr).
        Assert.Equal(Money.FromRupees(55000m), bs.TotalLiabilities - bs.TotalAssets);

        // ---- concrete advanced-inventory closings prove the FULL set moved stock (not merely round-tripped) ----
        var onHand = new InventoryLedger(company);
        var main = company.MainLocation!.Id;

        // Slice 3/4/7: Gizmo 200 opening − 20 additional-cost transfer − 10 Actual sale − 3 POS = 167 on-hand.
        var gizmo = company.FindStockItemByName("Gizmo")!;
        Assert.Equal(167m, onHand.OnHand(gizmo.Id, main, asOf));

        // Slice 8: Job Work Material Out issued 200 JW Raw to the third-party 'Worker Site' godown.
        var jwRaw = company.FindStockItemByName("JW Raw")!;
        var worker = company.FindGodownByName("Worker Site")!;
        Assert.Equal(300m, onHand.OnHand(jwRaw.Id, main, asOf));     // 500 opening − 200 issued
        Assert.Equal(200m, onHand.OnHand(jwRaw.Id, worker.Id, asOf)); // third-party stock held at the worker

        // Slice 2: the manufactured finished good carries its conserved BOM value (₹157.50) into the closing stock.
        var gadget = company.FindStockItemByName("Assembled Gadget")!;
        Assert.Equal(Money.FromRupees(157.50m), new StockValuationService(company).ClosingValue(gadget.Id, asOf).Value);
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2021, 4, 1), new DateOnly(2021, 4, 1));
}
