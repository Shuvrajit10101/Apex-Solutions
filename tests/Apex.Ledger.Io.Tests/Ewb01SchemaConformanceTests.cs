using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>T1-29 — the EWB-01 is validated against NIC's own schema, not against our shape.</b>
///
/// <para><b>What was wrong.</b> The EWB-01 this application wrote would have been <b>rejected by the portal</b>.
/// Measured against the <c>JSON Schema</c> block published at
/// <c>https://docs.ewaybillgst.gov.in/apidocs/version1.03/generate-eway-bill.html</c> it was missing <b>six of the
/// seventeen</b> mandatory keys (<c>fromPincode</c>, <c>toPincode</c>, <c>actFromStateCode</c>,
/// <c>actToStateCode</c>, <c>totInvValue</c>, <c>transactionType</c>), shared <b>zero</b> <c>itemList</c> key names
/// with the schema (<c>SlNo</c>/<c>HsnCd</c>/<c>taxable_amt_paisa</c>… against
/// <c>productName</c>/<c>hsnCode</c>/<c>taxableAmount</c>…), omitted all seven main-object value members, and failed
/// the schema's own <c>docDate</c> pattern. A goods vehicle moves on an accepted e-Way Bill, so that file was a
/// consignment that could not legally move.</para>
///
/// <para><b>🔴 NOTE THE DIRECTION OF THE DATE DEFECT, WHICH WAS REPORTED BACKWARDS.</b> The schema's published
/// pattern is <c>[0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9]</c> and its Data Structure table says "dd/mm/yyyy format" —
/// so NIC wants <b>DD/MM/YYYY</b> and we were emitting <b>yyyy-MM-dd</b>, not the other way round. Retrieving the
/// schema rather than trusting the description is the whole point of this file.</para>
///
/// <para><b>Why the oracle is the schema file.</b> See <see cref="JsonSchemaSubsetValidator"/>: every previous EWB-01
/// test asserted on our own key names, which is why a green suite coexisted with a rejectable filing for the whole
/// life of the payload. <c>Fixtures/ewb01-v1.03.schema.json</c> is a verbatim copy of NIC's published schema and is
/// what these tests assert against.</para>
/// </summary>
public sealed class Ewb01SchemaConformanceTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinKarnataka = "29AAPFU0939F1Z2";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    /// <summary>NIC's published EWB-01 v1.03 JSON Schema, read from the copy shipped beside the tests.</summary>
    private static JsonDocument Schema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ewb01-v1.03.schema.json");
        Assert.True(File.Exists(path), $"The NIC EWB-01 schema fixture is missing at {path}.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    // ================================================================ the conformance gate

    /// <summary>
    /// 🔴 <b>THE GATE.</b> A complete, ordinary intra-State goods movement must validate against NIC's schema with
    /// <b>zero</b> departures. On today's <c>main</c> this fails with more than a dozen of them.
    /// </summary>
    [Fact]
    public void An_ordinary_movement_conforms_to_the_published_NIC_EWB01_schema()
    {
        var (company, sale, record) = Movement();

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();

        var errors = JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement);
        Assert.True(errors.Count == 0,
            "The EWB-01 does not conform to NIC's published schema:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors));
    }

    /// <summary>An inter-State movement states IGST, and still conforms (NIC validates the head against the
    /// State pair: "In case of inter-state transaction ... the IGST tax rate and value has to be passed").</summary>
    [Fact]
    public void An_inter_state_movement_conforms_and_states_the_IGST_rate()
    {
        var (company, sale, record) = Movement(interState: true);

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();
        Assert.Empty(JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement));

        var item = payload.RootElement.GetProperty("itemList")[0];
        Assert.Equal(18m, item.GetProperty("igstRate").GetDecimal());
        Assert.Equal(0m, item.GetProperty("cgstRate").GetDecimal());
        Assert.Equal(0m, item.GetProperty("sgstRate").GetDecimal());
        Assert.Equal(9000m, payload.RootElement.GetProperty("igstValue").GetDecimal());
        Assert.Equal(0m, payload.RootElement.GetProperty("cgstValue").GetDecimal());
    }

    /// <summary>A cess-bearing consignment conforms, and the cess reaches BOTH the item rate and the main-object
    /// value — the pair NIC validates together.</summary>
    [Fact]
    public void A_cess_bearing_movement_conforms_and_states_cess_as_a_rate_and_a_value()
    {
        var (company, sale, record) = Movement(cessBasisPoints: 1200);

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();
        Assert.Empty(JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement));

        Assert.Equal(12m, payload.RootElement.GetProperty("itemList")[0].GetProperty("cessRate").GetDecimal());
        Assert.Equal(6000m, payload.RootElement.GetProperty("cessValue").GetDecimal());   // 12% of ₹50,000
        Assert.Equal(0m, payload.RootElement.GetProperty("cessNonAdvolValue").GetDecimal());
    }

    // ================================================================ the specific T1-29 departures

    /// <summary>
    /// All seventeen of the schema's mandatory keys are present — read off the schema's own <c>required</c> array,
    /// not from a list re-typed here. Six were absent before T1-29.
    /// </summary>
    [Fact]
    public void Every_mandatory_key_the_schema_names_is_present()
    {
        var (company, sale, record) = Movement();
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();

        var required = schema.RootElement.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Equal(17, required.Count);

        var absent = required.Where(k => !payload.RootElement.TryGetProperty(k, out _)).ToList();
        Assert.True(absent.Count == 0, "Mandatory EWB-01 keys absent: " + string.Join(", ", absent));
    }

    /// <summary>
    /// The payload names nothing the schema does not declare. This is the assertion that would have caught the old
    /// emission: <c>totInvValue_paisa</c>, <c>taxable_amt_paisa</c>, <c>schemaStatus</c> and the rest are not NIC
    /// keys, and an invented key is how a whole payload silently stops being the document it claims to be.
    /// </summary>
    [Fact]
    public void The_payload_invents_no_key_the_schema_does_not_declare()
    {
        var (company, sale, record) = Movement();
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();

        var declared = schema.RootElement.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var invented = payload.RootElement.EnumerateObject().Select(p => p.Name)
            .Where(n => !declared.Contains(n)).ToList();
        Assert.True(invented.Count == 0, "EWB-01 keys NIC never declared: " + string.Join(", ", invented));

        var itemKeys = schema.RootElement.GetProperty("properties").GetProperty("itemList")
            .GetProperty("items")[0].GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var inventedItem = payload.RootElement.GetProperty("itemList")[0].EnumerateObject()
            .Select(p => p.Name).Where(n => !itemKeys.Contains(n)).ToList();
        Assert.True(inventedItem.Count == 0, "itemList keys NIC never declared: " + string.Join(", ", inventedItem));
    }

    /// <summary>
    /// <c>docDate</c> is DD/MM/YYYY, which is what the schema's <c>pattern</c> demands — and what the brief for this
    /// defect had backwards. Pinned literally as well as through the pattern so the direction cannot drift again.
    /// </summary>
    [Fact]
    public void DocDate_is_the_schema_pattern_DD_MM_YYYY_not_ISO()
    {
        var (company, sale, record) = Movement();
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal("10/04/2025", payload.RootElement.GetProperty("docDate").GetString());
    }

    /// <summary>
    /// Money is the schema's rupee <c>Decimal(18,2)</c>, and the invented <c>_paisa</c> suffix is gone everywhere.
    /// NIC also requires Σ(values) ≤ <c>totInvValue</c> + ₹2.00; deriving <c>otherValue</c> as the residual makes it
    /// hold <b>exactly</b>, which is what this asserts rather than the ±₹2 grace.
    /// </summary>
    [Fact]
    public void Money_is_rupees_and_the_value_block_reconciles_to_the_consignment_value_exactly()
    {
        var (company, sale, record) = Movement();
        var bytes = EWayBillJson.BuildEwb01(company, sale, record);
        Assert.DoesNotContain("paisa", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);

        using var payload = JsonDocument.Parse(bytes);
        var root = payload.RootElement;
        Assert.Equal(50000m, root.GetProperty("totalValue").GetDecimal());
        Assert.Equal(4500m, root.GetProperty("cgstValue").GetDecimal());
        Assert.Equal(4500m, root.GetProperty("sgstValue").GetDecimal());
        Assert.Equal(59000m, root.GetProperty("totInvValue").GetDecimal());

        var sum = root.GetProperty("totalValue").GetDecimal()
                + root.GetProperty("cgstValue").GetDecimal()
                + root.GetProperty("sgstValue").GetDecimal()
                + root.GetProperty("igstValue").GetDecimal()
                + root.GetProperty("cessValue").GetDecimal()
                + root.GetProperty("cessNonAdvolValue").GetDecimal()
                + root.GetProperty("otherValue").GetDecimal();
        Assert.Equal(root.GetProperty("totInvValue").GetDecimal(), sum);
    }

    /// <summary>
    /// The State-code pair describes the GSTIN ends and the <c>act*</c> pair the physical ends, per NIC's Bill-To /
    /// Ship-To rule. Before T1-29 the record's Ship-To code overwrote <c>toStateCode</c>, stating the Ship-To State
    /// against the Bill-To GSTIN — a contradiction the portal checks.
    /// </summary>
    [Fact]
    public void The_ship_to_state_lands_on_actToStateCode_and_not_on_the_bill_to_state()
    {
        var (company, sale, record) = Movement(shipToGstin: GstinKarnataka);

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        var root = payload.RootElement;
        // Both ends are States the schema types as integers, and the pair is filled from DIFFERENT sources: the
        // Bill-To end from the party's GSTIN, the physical end from the record's Ship-To State.
        Assert.Equal(27, root.GetProperty("toStateCode").GetInt32());
        Assert.Equal(StateOf(record.ShipToStateCode), root.GetProperty("actToStateCode").GetInt32());
        Assert.Equal(StateOf(record.ShipFromStateCode), root.GetProperty("actFromStateCode").GetInt32());
        Assert.Equal(27, root.GetProperty("fromStateCode").GetInt32());
        // Master code 2 = Bill To - Ship To; NIC requires a Ship-To GSTIN for transaction types 2 and 4.
        Assert.Equal(2, root.GetProperty("transactionType").GetInt32());

        using var schema = Schema();
        Assert.Empty(JsonSchemaSubsetValidator.Validate(root, schema.RootElement));
    }

    /// <summary>Without a Ship-To GSTIN the movement is master code 1, Regular.</summary>
    [Fact]
    public void A_plain_movement_is_transaction_type_1_regular()
    {
        var (company, sale, record) = Movement();
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal(1, payload.RootElement.GetProperty("transactionType").GetInt32());
    }

    private static int StateOf(string? code) => int.Parse(code!, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An unregistered counterparty gets NIC's own placeholder — "In case of unregistered person involved
    /// in the transaction, pass URP in these fields" — rather than a null in a mandatory member.</summary>
    [Fact]
    public void An_unregistered_buyer_is_URP_and_the_payload_still_conforms()
    {
        var (company, sale, record) = Movement(unregisteredBuyer: true);

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal("URP", payload.RootElement.GetProperty("toGstin").GetString());
    }

    // ================================================================ the guard is not vacuous

    /// <summary>
    /// 🔴 <b>THE ORACLE IS MUTATION-CHECKED.</b> A validator that returns "no errors" for everything would make every
    /// test above pass while proving nothing — the exact failure mode this defect was. Each mutation below is a real
    /// departure that the old payload actually had, and each must be REPORTED.
    /// </summary>
    [Theory]
    // the old ISO date — fails the schema's docDate pattern
    [InlineData("\"docDate\": \"10/04/2025\"", "\"docDate\": \"2025-04-10\"", "docDate")]
    // a State code as the string we store internally, where the schema says integer
    [InlineData("\"toStateCode\": 27", "\"toStateCode\": \"27\"", "toStateCode")]
    // the old integer-paisa money, which is not a multiple of 0.01 rupees... it is a hundred times the value
    [InlineData("\"totInvValue\": 59000", "\"totInvValue\": \"59000\"", "totInvValue")]
    // an out-of-domain supply type
    [InlineData("\"supplyType\": \"O\"", "\"supplyType\": \"X\"", "supplyType")]
    // distance as a number, where the schema says string
    [InlineData("\"transDistance\": \"250\"", "\"transDistance\": 250", "transDistance")]
    // an unsourced mandatory member left null
    [InlineData("\"fromPincode\": 400001", "\"fromPincode\": null", "fromPincode")]
    // an item key renamed back to ours
    [InlineData("\"hsnCode\": 847130", "\"hsnCode\": \"847130\"", "hsnCode")]
    public void The_validator_reports_each_departure_the_old_payload_actually_had(
        string sound, string mutated, string expectedPath)
    {
        var (company, sale, record) = Movement();
        var json = Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Contains(sound, json);   // the sound payload really does say this

        using var broken = JsonDocument.Parse(json.Replace(sound, mutated));
        using var schema = Schema();
        var errors = JsonSchemaSubsetValidator.Validate(broken.RootElement, schema.RootElement);
        Assert.True(errors.Any(e => e.Contains(expectedPath, StringComparison.Ordinal)),
            $"The schema guard did not report the mutated '{expectedPath}'. Reported: " +
            (errors.Count == 0 ? "(nothing)" : string.Join(" | ", errors)));
    }

    /// <summary>A removed mandatory key is reported — the six-key hole, reproduced deliberately.</summary>
    [Fact]
    public void The_validator_reports_a_missing_mandatory_key()
    {
        var (company, sale, record) = Movement();
        var json = Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(company, sale, record));

        using var broken = JsonDocument.Parse(RemoveMember(json, "transactionType"));
        using var schema = Schema();
        var errors = JsonSchemaSubsetValidator.Validate(broken.RootElement, schema.RootElement);
        Assert.Contains(errors, e => e.Contains("transactionType", StringComparison.Ordinal));
    }

    // ================================================================ the operator can see the gap

    /// <summary>
    /// A book with no PIN codes cannot produce an acceptable EWB-01, and nothing is invented to hide that. The
    /// Generate e-Way Bill screen shows <see cref="EWayBillJson.MissingMandatory"/> beside the written file, so the
    /// operator learns it at the keyboard rather than from a portal rejection.
    /// </summary>
    [Fact]
    public void A_book_with_no_pin_codes_names_the_mandatory_members_it_cannot_source()
    {
        var (company, sale, record) = Movement(withPinCodes: false);

        var missing = EWayBillJson.MissingMandatory(company, sale, record);
        Assert.Contains("fromPincode", missing);
        Assert.Contains("toPincode", missing);

        // …and the payload is genuinely non-conformant, rather than quietly carrying a fabricated PIN.
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = Schema();
        Assert.NotEmpty(JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement));
    }

    /// <summary>A complete book reports nothing missing.</summary>
    [Fact]
    public void A_complete_book_reports_no_missing_mandatory_members()
    {
        var (company, sale, record) = Movement();
        Assert.Empty(EWayBillJson.MissingMandatory(company, sale, record));
    }

    /// <summary>The writer stays deterministic and de-branded (ER-11), and still never leaks the EWB number.</summary>
    [Fact]
    public void The_conforming_payload_is_still_byte_stable_debranded_and_free_of_the_EWB_number()
    {
        var (company, sale, record) = Movement();
        var first = EWayBillJson.BuildEwb01(company, sale, record);
        Assert.Equal(first, EWayBillJson.BuildEwb01(company, sale, record));

        var text = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("231000000123", text);
    }

    // ================================================================ fixture

    /// <summary>
    /// A posted goods movement with <b>every</b> mandatory source populated — PIN codes on both ends, a GSTIN party,
    /// Part B set. ₹50,000 @18%: intra ⇒ CGST ₹4,500 + SGST ₹4,500, consignment ₹59,000.
    /// </summary>
    private static (Company Company, Voucher Sale, EWayBillRecord Record) Movement(
        bool interState = false, bool unregisteredBuyer = false, bool withPinCodes = true,
        int cessBasisPoints = 0, string? shipToGstin = null)
    {
        var c = CompanyFactory.CreateSeeded("e-Way Conformance Co", FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        if (withPinCodes) c.Pin = "400001";

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "847130",
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            CessApplicable = cessBasisPoints > 0,
            CessValuationMode = cessBasisPoints > 0 ? CessValuationMode.AdValorem : null,
            CessRateBasisPoints = cessBasisPoints > 0 ? cessBasisPoints : null,
        };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(40000m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        if (!unregisteredBuyer)
            buyer.PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = interState ? GstinKarnataka : GstinMaharashtra,
                StateCode = interState ? "29" : "27",
            };
        else
            buyer.MailingStateCode = "27";

        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co",
            Address = "12 Residency Road\nShivajinagar",
            Country = "India",
            Pincode = withPinCodes ? (interState ? "560025" : "400012") : null,
        };

        const decimal saleValue = 50000m;
        var cess = cessBasisPoints > 0 ? gst.ResolveCess(item, sales, SaleDate, quantity: 1m) : null;
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(saleValue), 1800, cess) },
            interState, GstTaxDirection.Output);

        // Σ over the posted tax LINES, not TotalTax: the ring-fenced cess leg is not part of TotalTax, so a
        // cess-bearing fixture built off TotalTax posts an unbalanced voucher.
        var taxTotal = tax.TaxLines.Sum(l => l.Amount.Amount);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(saleValue + taxTotal), DrCr.Debit),
            new(sales.Id, new Money(saleValue), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines, partyId: buyer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 1m, new Money(saleValue)) }));

        var service = new EWayBillService(c);
        var record = service.PrepareRecord(sale, SaleDate, shipToGstin: shipToGstin);
        service.SetPartB(record, "27AAPFU0939F1ZV", EWayTransportMode.Road, "MH12AB1234", 250);
        return (c, sale, record);
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Drops one top-level member from an indented payload, for the missing-mandatory mutation.</summary>
    private static string RemoveMember(string json, string name)
    {
        using var source = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var member in source.RootElement.EnumerateObject())
                if (!string.Equals(member.Name, name, StringComparison.Ordinal))
                    member.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
