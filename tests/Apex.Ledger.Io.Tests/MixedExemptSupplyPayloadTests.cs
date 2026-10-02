using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>THE <c>singleRate</c> COLLAPSE ON THE FILED PAYLOADS — an EWB-01 declaring 18% GST on EXEMPT goods, and an
/// INV-01 putting real rupees of tax on them.</b>
///
/// <para><b>One root cause, three emitters.</b> Every item consumer skipped per-line rate resolution whenever a
/// voucher posted exactly ONE GST rate group (<c>groups.Count == 1 ? groups[0].Rate : null</c>), so every inventory
/// line — exempt, nil-rated and non-GST included — was stamped with that group's rate. The grep for the shortcut
/// returned FIVE sites: <c>Gstr1.AccumulateHsn</c> (T1-59, fixed with these), <c>EWayBillJson.BuildItems</c>,
/// <c>EInvoiceJson.BuildItems</c>, and two already-correct service paths that split non-taxable legs out first
/// (<c>Gstr1.AccumulateServiceHsn</c> and <c>EInvoiceJson.ServiceLegsByRate</c>) — which is what shows the GOODS side
/// was simply missing the discriminator the LEDGER side already had.</para>
///
/// <para><b>🔴 Why the schema cannot catch it, which is why these tests exist.</b> NIC types <c>cgstRate</c> as no
/// more than <c>{ "type": "number", "multipleOf": 0.001 }</c>, so a 9 stamped on exempt milk validates clean against
/// the published schema. The conformance suite is therefore structurally blind to it, and so was the whole gate: a
/// reviewer applied the one-line fix and re-ran both owning suites — 724 and 2,860 tests, zero failures.</para>
///
/// <para><b>Figures worked by hand.</b> One intra-State consignment: Widget ₹50,000 @ 18% (HSN 847130) and EXEMPT
/// Fresh Milk ₹20,000 (HSN 040110). The posting excludes the exempt line from the tax base, exactly as
/// <c>VoucherEntryViewModel.ComputeItemInvoiceGst</c> does, so ₹50,000 is taxed: CGST ₹4,500 + SGST ₹4,500, ONE 1800
/// rate group. Under the collapse the milk was the group's remainder-absorbing line, so the INV-01 put
/// 4,500 − round(4,500 × 50,000/70,000, 2) = 4,500 − 3,214.29 = <b>₹1,285.71</b> of CGST and the same of SGST on the
/// EXEMPT HSN, and the EWB-01 declared <c>cgstRate</c> 9 / <c>sgstRate</c> 9 on it.</para>
///
/// <para><b>Also pinned here: NIC <c>transactionType</c>.</b> It read
/// <c>record.ShipToGstin is blank ? 1 : 2</c> while <c>EWayBillService.PrepareRecord</c> REFUSES a record without a
/// Ship-To GSTIN from 01-Aug-2026 — so every bill declared type 2 (Bill To - Ship To) for an ordinary two-party
/// movement. Master codes verified by content at
/// <c>docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>: 1 Regular, 2 Bill To - Ship To, 3 Bill From -
/// Dispatch From, 4 Combination of 2 and 3.</para>
/// </summary>
public sealed class MixedExemptSupplyPayloadTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinOtherBuyer = "27AABCU9603R1ZM";
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly SaleDate = new(2026, 9, 10);  // on/after EWayShipToMandatoryFrom

    private const string TaxedHsn = "847130";
    private const string ExemptHsn = "040110";

    // ================================================================ EWB-01: the exempt-goods rate declaration

    /// <summary>
    /// 🔴 <b>THE DEFECT, ON THE EMITTED FILE.</b> A goods vehicle moves on an e-Way Bill and a roadside checkpoint
    /// reads it. The exempt line must declare a ZERO rate; it declared 9 / 9.
    /// </summary>
    [Fact]
    public void An_exempt_line_on_a_single_rate_EWB01_declares_a_zero_GST_rate()
    {
        var (company, sale, record) = Movement();

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        var milk = ItemByHsn(payload, ExemptHsn);

        Assert.Equal(0m, milk.GetProperty("cgstRate").GetDecimal());   // was 9
        Assert.Equal(0m, milk.GetProperty("sgstRate").GetDecimal());   // was 9
        Assert.Equal(0m, milk.GetProperty("igstRate").GetDecimal());
        Assert.Equal(0m, milk.GetProperty("cessRate").GetDecimal());
    }

    /// <summary>The taxable line in the SAME consignment keeps its real rate, so the fix cannot be bought by zeroing
    /// every line — which would understate the declaration instead of overstating it.</summary>
    [Fact]
    public void The_taxable_line_in_the_same_consignment_still_declares_its_own_rate()
    {
        var (company, sale, record) = Movement();

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        var widget = ItemByHsn(payload, TaxedHsn);

        Assert.Equal(9m, widget.GetProperty("cgstRate").GetDecimal());
        Assert.Equal(9m, widget.GetProperty("sgstRate").GetDecimal());
    }

    /// <summary>
    /// The consignment still conforms to NIC's published schema after the fix. The schema can never prove the rate
    /// CORRECT (see the class comment), so this is a regression guard beside the assertions above, never a substitute
    /// for them.
    /// </summary>
    [Fact]
    public void A_mixed_exempt_consignment_still_conforms_to_the_published_NIC_schema()
    {
        var (company, sale, record) = Movement();

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = SchemaFixture();
        Assert.Empty(JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement));
    }

    // ================================================================ EWB-01: transactionType

    /// <summary>
    /// 🔴 An ordinary two-party movement is NIC transaction type <b>1 (Regular)</b>. Because PrepareRecord refuses a
    /// blank Ship-To GSTIN from 01-Aug-2026 and this voucher is dated 10-Sep-2026, the old "is it present" test made
    /// this declare 2 — asserting a Bill-To/Ship-To split the payload does not describe and the product cannot
    /// represent at all (census 10.2 Multi Address is ABSENT).
    /// </summary>
    [Fact]
    public void An_ordinary_movement_declares_transactionType_1_Regular_not_2()
    {
        var (company, sale, record) = Movement();  // ship-to GSTIN == the buyer's own

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal(1, payload.RootElement.GetProperty("transactionType").GetInt32());
    }

    /// <summary>The mirror: a Ship-To GSTIN naming a DIFFERENT party is a genuine Bill To - Ship To movement and must
    /// still declare 2, so the fix narrows the claim rather than deleting it.</summary>
    [Fact]
    public void A_genuinely_different_ship_to_party_still_declares_transactionType_2()
    {
        var (company, sale, record) = Movement(shipToGstin: GstinOtherBuyer);

        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        Assert.Equal(2, payload.RootElement.GetProperty("transactionType").GetInt32());
    }

    /// <summary>Case and surrounding whitespace do not make the same party a different one — a GSTIN is canonically
    /// upper-case and the field is free text, so a lower-cased entry must not flip the declaration to 2.</summary>
    [Theory]
    [InlineData("27aapfu0939f1zv")]
    [InlineData("  27AAPFU0939F1ZV  ")]
    public void The_same_party_spelled_differently_is_still_Regular(string shipTo)
    {
        Assert.False(EWayBillJson.IsBillToShipTo(shipTo, GstinMaharashtra));
        Assert.True(EWayBillJson.IsBillToShipTo(GstinOtherBuyer, GstinMaharashtra));
    }

    // ================================================================ EWB-01: the format pre-flight

    /// <summary>
    /// 🔴 The "the portal will reject it" pre-flight checked only PRESENCE, so a malformed value that was present
    /// passed and the operator was told to upload. NIC's schema is the oracle: <c>"vehicleNo": { "minLength": 7 }</c>,
    /// verified by content on the published generate-eway-bill page.
    /// </summary>
    [Fact]
    public void A_vehicle_number_too_short_for_the_schema_is_refused_by_the_pre_flight()
    {
        var (company, sale, record) = Movement(vehicleNumber: "MH1AB1");  // 6 characters

        // The payload is PRESENT and complete, so the presence check is silent — that is the hole.
        Assert.Empty(EWayBillJson.MissingMandatory(company, sale, record));

        var problems = EWayBillJson.SchemaFormatProblems(company, sale, record);
        Assert.Contains(problems, p => p.Contains("vehicleNo", StringComparison.Ordinal));

        // And the schema agrees it would be rejected, so the pre-flight is not inventing a rule.
        using var payload = JsonDocument.Parse(EWayBillJson.BuildEwb01(company, sale, record));
        using var schema = SchemaFixture();
        Assert.Contains(
            JsonSchemaSubsetValidator.Validate(payload.RootElement, schema.RootElement),
            e => e.Contains("$.vehicleNo", StringComparison.Ordinal));
    }

    /// <summary>A distance beyond NIC's stated maximum is refused. The published page is internally inconsistent —
    /// the schema description reads "Distance (&lt;4000 km)" and the Data Structure table "Max Value = 4000" — so the
    /// check refuses only STRICTLY above 4000, and 4000 itself is accepted.</summary>
    [Fact]
    public void A_distance_beyond_the_schema_maximum_is_refused_and_the_boundary_is_accepted()
    {
        var (c1, s1, r1) = Movement(distanceKm: 4001);
        Assert.Contains(EWayBillJson.SchemaFormatProblems(c1, s1, r1),
            p => p.Contains("transDistance", StringComparison.Ordinal));

        var (c2, s2, r2) = Movement(distanceKm: 4000);
        Assert.DoesNotContain(EWayBillJson.SchemaFormatProblems(c2, s2, r2),
            p => p.Contains("transDistance", StringComparison.Ordinal));
    }

    /// <summary>A well-formed consignment raises nothing, so the pre-flight cannot cry wolf on the ordinary path.</summary>
    [Fact]
    public void A_well_formed_consignment_raises_no_format_problem()
    {
        var (company, sale, record) = Movement();
        Assert.Empty(EWayBillJson.SchemaFormatProblems(company, sale, record));
    }

    // ================================================================ INV-01: the same collapse, carrying rupees

    /// <summary>
    /// 🔴 <b>THE DEFECT THE REVIEW DID NOT FIND, AND IT IS WORSE THAN THE EWB-01 ONE.</b> The EWB-01 states rates;
    /// the INV-01 states AMOUNTS, and it is the invoice the IRP registers. The exempt line was declared with
    /// <c>GstRt</c> 1800, <c>CgstAmt</c> ₹1,285.71 and <c>SgstAmt</c> ₹1,285.71 — real tax against exempt goods — and
    /// the taxable line was short by the same amount, so NIC's own item check (CGST Value = Taxable Value × GstRt / 2)
    /// failed on BOTH lines at once.
    /// </summary>
    [Fact]
    public void An_exempt_line_on_a_single_rate_INV01_bears_no_tax_and_no_rate()
    {
        var (company, sale, _) = Movement();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var milk = Inv01ItemByHsn(payload, ExemptHsn);

        // 🔴 THE MONEY FIRST — this is the figure that makes it a misstatement and not a formatting fault.
        Assert.Equal(0m, milk.GetProperty("CgstAmt").GetDecimal());    // was 1285.71
        Assert.Equal(0m, milk.GetProperty("SgstAmt").GetDecimal());    // was 1285.71
        Assert.Equal(0m, milk.GetProperty("IgstAmt").GetDecimal());
        Assert.Equal(0m, milk.GetProperty("GstRt").GetDecimal());      // was 18

        // Its own assessable amount is untouched — the goods ARE on the invoice; only the tax was wrong.
        Assert.Equal(20_000m, milk.GetProperty("AssAmt").GetDecimal());
    }

    /// <summary>The taxable INV-01 line keeps the whole posted tax of its group, and NIC's own item identity
    /// (CGST Value = Taxable Value × GstRt / 2) now holds on it — it did not before, because the exempt line had
    /// taken ₹1,285.71 per head away from it.</summary>
    [Fact]
    public void The_taxable_INV01_line_satisfies_NICs_own_item_tax_identity()
    {
        var (company, sale, _) = Movement();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var widget = Inv01ItemByHsn(payload, TaxedHsn);

        var assAmt = widget.GetProperty("AssAmt").GetDecimal();
        var gstRt = widget.GetProperty("GstRt").GetDecimal();
        var cgst = widget.GetProperty("CgstAmt").GetDecimal();
        var sgst = widget.GetProperty("SgstAmt").GetDecimal();

        Assert.Equal(50_000m, assAmt);
        Assert.Equal(18m, gstRt);
        Assert.Equal(4_500m, cgst);     // was 3214.29
        Assert.Equal(4_500m, sgst);     // was 3214.29
        Assert.Equal(assAmt * gstRt / 200m, cgst);
        Assert.Equal(assAmt * gstRt / 200m, sgst);
    }

    /// <summary>Σ of the item tax must still equal the tax actually POSTED. Under the collapse this held while both
    /// lines were wrong, which is exactly why a footing check could not find the defect — pinned so a future change
    /// cannot buy a correct exempt line by inventing or losing tax.</summary>
    [Fact]
    public void Every_posted_rupee_of_tax_still_reaches_exactly_one_INV01_line()
    {
        var (company, sale, _) = Movement();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var items = payload.RootElement.GetProperty("ItemList");

        Assert.Equal(2, items.GetArrayLength());  // the exempt line is zero-taxed, never dropped
        Assert.Equal(9_000m, items.EnumerateArray()
            .Sum(i => i.GetProperty("CgstAmt").GetDecimal()
                    + i.GetProperty("SgstAmt").GetDecimal()
                    + i.GetProperty("IgstAmt").GetDecimal()));
    }

    // ================================================================ fixture

    private static JsonElement ItemByHsn(JsonDocument payload, string hsn) =>
        payload.RootElement.GetProperty("itemList").EnumerateArray()
            .Single(i => i.GetProperty("hsnCode").GetInt64().ToString() == hsn.TrimStart('0'));

    private static JsonElement Inv01ItemByHsn(JsonDocument payload, string hsn) =>
        payload.RootElement.GetProperty("ItemList").EnumerateArray()
            .Single(i => i.GetProperty("HsnCd").GetString() == hsn);

    private static JsonDocument SchemaFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ewb01-v1.03.schema.json");
        Assert.True(File.Exists(path), $"The NIC EWB-01 schema fixture is missing at {path}.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// One intra-State goods movement MIXING a taxable and an exempt line: Widget ₹50,000 @ 18% (HSN 847130) first,
    /// EXEMPT Fresh Milk ₹20,000 (HSN 040110) second. The milk is ordered LAST deliberately: under the collapse it
    /// was then the rate group's remainder-absorbing line, which is what produced ₹1,285.71 rather than a rounded
    /// share, and the figure the class comment measures.
    /// </summary>
    private static (Company Company, Voucher Sale, EWayBillRecord Record) Movement(
        string? shipToGstin = null, string vehicleNumber = "MH12AB1234", int distanceKm = 250)
    {
        var c = CompanyFactory.CreateSeeded("Mixed Supply Movement Co", FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";

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

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        {
            HsnSac = TaxedHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var milk = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        milk.Gst = new StockItemGstDetails { HsnSac = ExemptHsn, Taxability = GstTaxability.Exempt };

        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(20m));
        inv.AddOpeningBalance(milk.Id, main, 1000m, Money.FromRupees(10m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };
        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co",
            Address = "12 Residency Road\nShivajinagar",
            Country = "India",
            Pincode = "400012",
        };

        // 🔴 The POSTING excludes the exempt line from the tax base, exactly as ComputeItemInvoiceGst does, so the
        // invoice posts ONE 1800 rate group over ₹50,000 — the shape every emitter then has to read correctly.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) },
            interState: false, GstTaxDirection.Output);

        var taxTotal = tax.TaxLines.Sum(l => l.Amount.Amount);
        Assert.Equal(9_000m, taxTotal);   // the fixture's own premise, held at build time

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(70_000m + taxTotal), DrCr.Debit),
            new(sales.Id, new Money(70_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines, partyId: buyer.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, main, 2500m, Money.FromRupees(20m)),  // 50,000
                new VoucherInventoryLine(milk.Id, main, 2000m, Money.FromRupees(10m)),    // 20,000 — LAST
            }));

        var service = new EWayBillService(c);
        // Dated 10-Sep-2026, i.e. on/after EWayShipToMandatoryFrom, so PrepareRecord REFUSES a blank Ship-To GSTIN.
        // Defaulting it to the buyer's OWN GSTIN is the ordinary two-party case the transactionType tests turn on.
        var record = service.PrepareRecord(sale, SaleDate, shipToGstin: shipToGstin ?? GstinMaharashtra);
        service.SetPartB(record, GstinMaharashtra, EWayTransportMode.Road, vehicleNumber, distanceKm);
        return (c, sale, record);
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
