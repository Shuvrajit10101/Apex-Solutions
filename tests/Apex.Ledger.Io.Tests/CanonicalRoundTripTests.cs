using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
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
        var sale = company.Vouchers.Single(v => v.HasInventoryLines);
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

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2021, 4, 1), new DateOnly(2021, 4, 1));
}
