using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-8 — the NIC e-Way Part-A master codes.</b> The Part-A <c>supplyType</c> / <c>subSupplyType</c> / <c>docType</c>
/// triple is a <b>statutory filing</b> field set: it goes verbatim onto the EWB-01 request
/// (<c>EWayBillJson.BuildEwb01</c>), so an out-of-domain value is a malformed government filing, not a cosmetic defect.
/// Before this suite the engine emitted human-readable DESCRIPTIONS ("Outward", "Supply", "Job Work") and two codes
/// that do not exist in the e-Way domain at all (CRN / DBN, which belong to the <i>e-invoice</i> INV-01 schema), plus
/// one invented value ("Handicraft") that appears nowhere in NIC's list.
///
/// <para><b>Every value asserted here is read from an official source, live (R7):</b></para>
/// <list type="bullet">
/// <item><b>Master codes</b> — <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c> (published by the
/// "Eway Bill Team, National Informatics Centre, Karnataka, Govt. of India"). <c>Supply Type</c>: <c>I</c> Inward,
/// <c>O</c> Outward. <c>Sub Supply Type</c>: 1 Supply, 2 Import, 3 Export, 4 Job Work, 5 For Own Use, 6 Job work
/// Returns, 7 Sales Return, 8 Others, 9 SKD/CKD/Lots, 10 Line Sales, 11 Recipient Not Known, 12 Exhibition or Fairs —
/// <b>twelve values, and "Handicraft" is not one of them</b>. <c>Document Type</c>: <c>INV</c> Tax Invoice,
/// <c>BIL</c> Bill of Supply, <c>BOE</c> Bill of Entry, <c>CHL</c> Delivery Challan, <c>OTH</c> Others —
/// <b>five values, and CRN / DBN are not among them</b>.</item>
/// <item><b>Supply Type – Document Type mapping</b> — <c>https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html</c>,
/// the table of <i>permitted combinations</i>. It is what settles the return-note direction: <b>"Sales Return"
/// appears only under Inward</b> (row: Inward | Sales Return | Delivery Challan | From = Other GSTIN/URP | To = Self),
/// and there is no Sales-Return row under Outward at all. It equally settles that <c>Outward | Supply</c> permits
/// only Tax Invoice / Bill of Supply — never a Delivery Challan — and that <c>Outward | Others</c> permits only
/// Delivery Challan / Others — never a Tax Invoice.</item>
/// <item><b>CGST Rule 138</b> —
/// <c>https://taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter16/rule138_v1.00.html</c>.
/// 138(1) covers movement "in relation to a supply" (not "taxable supply") and Explanation 2 defines the consignment
/// value as the value "declared in an invoice, <b>a bill of supply</b> or a delivery challan" — so a bill of supply
/// carries an e-Way Bill, and therefore needs a docType of its own. Explanation 1 defines "handicraft goods" by
/// reference to Notification 56/2018-Central Tax: handicraft is a Rule-138 <i>threshold</i> concept, not a NIC
/// sub-supply type.</item>
/// <item><b>State codes</b> — <c>https://einvoice1.gst.gov.in/Others/MasterCodes</c> (State Codes table, read from the
/// page DOM): <b>96 = OTHER COUNTRIES, 97 = Other Territory, 99 = OTHER COUNTRIES</b>. 97 is a DOMESTIC GST
/// territory; 99 is a genuine overseas code.</item>
/// </list>
///
/// <para>Money fixtures are odd-paisa throughout: a round figure would pass under a rounding defect.</para>
/// </summary>
public sealed class EWayPartACodeTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly MoveDate = new(2025, 4, 10);

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required EWayBillService Service { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid WidgetId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid DomesticPartyId { get; init; }
        public required Guid OtherTerritoryPartyId { get; init; }   // state 97 — DOMESTIC
        public required Guid OverseasPartyId { get; init; }          // state 99 — export

        public Guid TypeId(VoucherBaseType baseType) =>
            Company.VoucherTypes.First(t => t.BaseType == baseType).Id;
    }

    private static Fx Build(GstRegistrationType registration = GstRegistrationType.Regular)
    {
        var c = CompanyFactory.CreateSeeded("e-Way Codes Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, EWayIntraStateApplicable = true,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var local = Add(c, "Local Debtor", "Sundry Debtors", true);
        local.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        // State 97 "Other Territory" — DOMESTIC per the official state-code master, despite the name.
        var territory = Add(c, "Other Territory Debtor", "Sundry Debtors", true);
        territory.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "97" };
        // State 99 "OTHER COUNTRIES" — a genuine overseas place of supply.
        var overseas = Add(c, "Overseas Debtor", "Sundry Debtors", true);
        overseas.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "99" };

        return new Fx
        {
            Company = c, Service = new EWayBillService(c), GodownId = c.MainLocation!.Id, WidgetId = widget.Id,
            SalesLedgerId = sales.Id, DomesticPartyId = local.Id,
            OtherTerritoryPartyId = territory.Id, OverseasPartyId = overseas.Id,
        };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A balanced goods movement of <paramref name="baseType"/> worth ₹2,04,317.63 — comfortably over the
    /// ₹50,000 Rule-138 threshold, and odd to the paisa so no rounding shortcut can pass.</summary>
    private static Voucher Movement(Fx f, VoucherBaseType baseType, Guid partyId, decimal value = 2_04_317.63m)
    {
        var lines = new List<EntryLine>
        {
            new(partyId, new Money(value), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
        };
        return new Voucher(Guid.NewGuid(), f.TypeId(baseType), MoveDate, lines, partyId: partyId,
            inventoryLines: new[] { new VoucherInventoryLine(f.WidgetId, f.GodownId, 1m, new Money(value)) });
    }

    // ================================================================ 1 — the reported defect: docType BIL

    /// <summary>
    /// <b>The reported defect.</b> A composition dealer's outward goods movement is titled BILL OF SUPPLY by the app's
    /// own print router, yet the Part-A stamped <c>docType = "INV"</c> — Tax Invoice — for it. The NIC document-type
    /// master carries a code for exactly this document (<c>BIL</c> = Bill of Supply), and the Supply-Type/Document-Type
    /// mapping explicitly permits <c>Outward | Supply | Bill of Supply</c>, so there was never any need to misdeclare
    /// it. Routed through the SHARED predicate <see cref="GstReportSupport.IsBillOfSupply"/> — not a sixth private copy
    /// of the document-kind rule.
    /// </summary>
    [Fact]
    public void A_composition_bill_of_supply_movement_emits_the_docType_BIL_not_INV()
    {
        var f = Build(GstRegistrationType.Composition);
        var v = Movement(f, VoucherBaseType.Sales, f.DomesticPartyId);

        Assert.True(GstReportSupport.IsBillOfSupply(f.Company, v)); // the app's own document-kind predicate
        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(v));

        var codes = f.Service.PartACodesFor(v);
        Assert.Equal("O", codes.SupplyType);
        Assert.Equal("1", codes.SubSupplyType);
        Assert.Equal("BIL", codes.DocType);

        // …and it is what the audited record carries, so it is what goes on the wire.
        Assert.Equal("BIL", f.Service.PrepareRecord(v, MoveDate).DocType);
    }

    /// <summary>A Regular dealer's taxable sale is a Rule-46 tax invoice — <c>Outward | Supply | Tax Invoice</c>.</summary>
    [Fact]
    public void A_regular_taxable_sale_emits_O_1_INV()
    {
        var f = Build();
        var codes = f.Service.PartACodesFor(Movement(f, VoucherBaseType.Sales, f.DomesticPartyId));
        Assert.Equal(("O", "1", "INV"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
    }

    // ================================================================ 2 — supplyType / subSupplyType are CODES

    /// <summary>
    /// <c>supplyType</c> is a one-letter CODE (<c>I</c> / <c>O</c>) and <c>subSupplyType</c> is NUMERIC 1–12, per the
    /// NIC master-codes list. The engine used to emit the DESCRIPTIONS ("Outward", "Supply", "Job Work") — values that
    /// are nowhere in the enumerated domain, so the request was malformed on three fields at once.
    /// </summary>
    [Fact]
    public void No_Part_A_code_is_ever_a_human_readable_description()
    {
        var f = Build();
        var descriptions = new[]
        {
            "Inward", "Outward", "Supply", "Import", "Export", "Job Work", "Job work Returns", "Sales Return",
            "Others", "Handicraft", "Tax Invoice", "Bill of Supply", "Delivery Challan",
        };
        foreach (var baseType in GoodsMovementBaseTypes)
            foreach (var txn in AllTxnTypes)
            {
                var codes = f.Service.PartACodesFor(Movement(f, baseType, f.DomesticPartyId), txn);
                Assert.DoesNotContain(codes.SupplyType, descriptions);
                Assert.DoesNotContain(codes.SubSupplyType, descriptions);
                Assert.DoesNotContain(codes.DocType, descriptions);
            }
    }

    // ================================================================ 3 — CRN / DBN are not e-Way codes

    /// <summary>
    /// <b>CRN and DBN are e-INVOICE codes, and the e-Way domain does not contain them.</b> The NIC document-type master
    /// enumerates exactly five values — INV, BIL, BOE, CHL, OTH — while the INV-01 schema's <c>DocDtls.Typ</c> is a
    /// separate String(3) domain of three ("INV-Invoice, CRN-Credit Note, DBN-Debit Note", official schema PDF
    /// <c>https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf</c> field 9). The engine used to emit the
    /// e-invoice codes into the e-Way request, so a return note filed a value the portal cannot accept.
    ///
    /// <para>Per the Supply-Type/Document-Type mapping, a return travels on a <b>Delivery Challan</b>, and its direction
    /// is INWARD: the only "Sales Return" row in the whole table is <c>Inward | Sales Return | Delivery Challan</c>,
    /// with From = Other GSTIN/URP and To = Self — goods coming back to the seller. The engine used to file a credit
    /// note as OUTWARD.</para>
    /// </summary>
    [Fact]
    public void A_sales_return_credit_note_files_INWARD_as_a_delivery_challan_never_CRN()
    {
        var f = Build();
        var codes = f.Service.PartACodesFor(Movement(f, VoucherBaseType.CreditNote, f.DomesticPartyId));
        Assert.Equal(("I", "7", "CHL"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
        Assert.NotEqual("CRN", codes.DocType);
    }

    /// <summary>
    /// A debit note likewise never emits DBN, and <b>W0-8 settles its DIRECTION as Outward</b>. The earlier note left it
    /// Inward as "UNVERIFIED" because the mapping has no purchase-return SUB-TYPE — true, but that is the wrong column.
    /// The From/To columns settle it: <c>Outward | Others | Delivery Challan</c> is From = Self, To = Self/Other/URP,
    /// while <c>Inward | Others | Delivery Challan</c> fixes <b>To = Self</b>. A purchase return moves goods away from
    /// Self to the supplier, so the consignee is not Self and only the Outward row can carry it. This is the same
    /// reasoning that flipped the Credit Note — applying it to one return note and not the other was the asymmetry.
    /// </summary>
    [Fact]
    public void A_debit_note_never_emits_DBN_and_files_OUTWARD_because_the_goods_leave_Self()
    {
        var f = Build();
        var codes = f.Service.PartACodesFor(Movement(f, VoucherBaseType.DebitNote, f.DomesticPartyId));
        Assert.Equal(("O", "8", "CHL"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
        Assert.NotEqual("DBN", codes.DocType);

        // The job-work leg of the same document takes the Outward job-work row.
        var jw = f.Service.PartACodesFor(
            Movement(f, VoucherBaseType.DebitNote, f.DomesticPartyId), EWayTransactionType.JobWork);
        Assert.Equal(("O", "4", "CHL"), (jw.SupplyType, jw.SubSupplyType, jw.DocType));
    }

    // ================================================================ 4 — job work, and where Handicraft lands

    /// <summary>A principal→job-worker movement is <c>Outward | Job Work (4) | Delivery Challan</c>; the return leg is
    /// <c>Inward | Job work Returns (6) | Delivery Challan</c>. Both rows are read straight off the mapping table.</summary>
    [Fact]
    public void Job_work_out_is_O_4_CHL_and_the_return_leg_is_I_6_CHL()
    {
        var f = Build();
        var outLeg = f.Service.PartACodesFor(
            Movement(f, VoucherBaseType.DeliveryNote, f.DomesticPartyId), EWayTransactionType.JobWork);
        Assert.Equal(("O", "4", "CHL"), (outLeg.SupplyType, outLeg.SubSupplyType, outLeg.DocType));

        var backLeg = f.Service.PartACodesFor(
            Movement(f, VoucherBaseType.ReceiptNote, f.DomesticPartyId), EWayTransactionType.JobWork);
        Assert.Equal(("I", "6", "CHL"), (backLeg.SupplyType, backLeg.SubSupplyType, backLeg.DocType));
    }

    /// <summary>
    /// An ordinary (non-job-work) delivery note is <c>Outward | Others (8) | Delivery Challan</c> — <b>not</b> sub-type
    /// 1 Supply. The mapping permits <c>Outward | Supply</c> only with a Tax Invoice or a Bill of Supply; a delivery
    /// challan under sub-type 1 is not a row in the table, so the obvious-looking "1" would be rejected.
    /// </summary>
    [Fact]
    public void A_plain_delivery_note_is_Outward_Others_8_because_Outward_Supply_forbids_a_challan()
    {
        var f = Build();
        var codes = f.Service.PartACodesFor(Movement(f, VoucherBaseType.DeliveryNote, f.DomesticPartyId));
        Assert.Equal(("O", "8", "CHL"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
    }

    /// <summary>
    /// <b>"Handicraft" is not a NIC sub-supply type</b> — the master list has twelve values and none of them is
    /// handicraft. CGST Rule 138 Explanation 1 defines "handicraft goods" by reference to Notification 56/2018-Central
    /// Tax purely to drive the "irrespective of the value of the consignment" relaxation, i.e. it is a THRESHOLD
    /// concept, not a document classification. So a handicraft sale declares what it actually is — a supply on a tax
    /// invoice — and the handicraft-ness survives only where it belongs, in <see cref="EWayCoverage"/>.
    ///
    /// <para>"8 Others" would be the fallback if nothing fitted, but here it would be <b>invalid</b>: the mapping
    /// permits <c>Outward | Others</c> only with a Delivery Challan or Others, never with the Tax Invoice this
    /// movement actually travels on.</para>
    /// </summary>
    [Fact]
    public void Handicraft_has_no_NIC_code_so_a_handicraft_sale_declares_the_supply_it_is()
    {
        var f = Build();
        var v = Movement(f, VoucherBaseType.Sales, f.DomesticPartyId);
        var codes = f.Service.PartACodesFor(v, EWayTransactionType.Handicraft);
        Assert.Equal(("O", "1", "INV"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));

        // The handicraft flag still does the one job it is for — the threshold carve-out (inter-state).
        var inter = Movement(f, VoucherBaseType.Sales, f.OtherTerritoryPartyId, 1_234.57m);
        Assert.Equal(EWayCoverage.MandatoryIrrespectiveOfValue,
            f.Service.CoverageOf(inter, EWayTransactionType.Handicraft));
    }

    // ================================================================ 5 — export routing (3), and the 97 correction

    /// <summary>
    /// Sub-supply 3 Export was <b>never emitted</b>: an export sale declared "Supply". A sale whose place of supply is
    /// an overseas state code now files <c>Outward | Export (3) | Tax Invoice</c>, a permitted row.
    ///
    /// <para><b>🔴 Read this together with the unreachability pin below.</b> The fixture assigns
    /// <c>StateCode = "99"</c> by direct field assignment, which is state the master editor itself refuses — so this
    /// proves the branch is CORRECT, not that it can be reached in the shipped app.</para>
    /// </summary>
    [Fact]
    public void An_export_sale_emits_subSupplyType_3()
    {
        var f = Build();
        var codes = f.Service.PartACodesFor(Movement(f, VoucherBaseType.Sales, f.OverseasPartyId));
        Assert.Equal(("O", "3", "INV"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
    }

    /// <summary>
    /// <b>🔴 PINNED GAP (review findings #4 / #7) — the export limb is UNREACHABLE through a validated master edit.</b>
    /// <see cref="IndianState.All"/> carries 97 but neither 96 nor 99, and <c>PartyGstDetails.EnsureValid</c> rejects
    /// anything outside that list, so a user cannot record an overseas place of supply at all: the only non-mainland
    /// option the party master offers is 97, which is domestic. Narrowing the predicate to 96/99 is statutorily right
    /// (official master: 96 = OTHER COUNTRIES, 97 = Other Territory, 99 = OTHER COUNTRIES) but it means
    /// <see cref="EInvoiceSupplyCategory.Export"/>, INV-01 <c>SupTyp="EXPWP"</c>, e-Way sub-type <c>3</c> and the B2C-QR
    /// overseas suppression are all now reachable only in memory and through import.
    ///
    /// <para><b>Why it is pinned rather than fixed here.</b> <c>Gstin.Validate</c> checks a GSTIN's leading two digits
    /// against the SAME list (Gstin.cs:32), so adding 96/99 to it would start accepting GSTINs beginning "96"/"99",
    /// which do not exist. Closing this properly means splitting the place-of-supply domain from the GSTIN-prefix
    /// domain, or adding a party-level export/SEZ flag — a slice of its own. This test fails the day either happens.</para>
    /// </summary>
    [Fact]
    public void PINNED_GAP_an_overseas_place_of_supply_cannot_be_recorded_through_a_validated_master_edit()
    {
        foreach (var overseas in new[] { "96", "99" })
        {
            Assert.True(GstReportSupport.IsOverseasStateCode(overseas));
            Assert.False(IndianState.IsValidCode(overseas));
            var ex = Assert.Throws<ArgumentException>(() =>
                new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = overseas }
                    .EnsureValid());
            Assert.Contains("is not a valid Indian State/UT code", ex.Message);
        }

        // 97 — the only non-mainland code the master accepts — is DOMESTIC, so no validated party can be an export.
        Assert.True(IndianState.IsValidCode("97"));
        Assert.False(GstReportSupport.IsOverseasStateCode("97"));
    }

    /// <summary>
    /// <b>State code 97 is DOMESTIC.</b> The official state-code master reads 96 = OTHER COUNTRIES, <b>97 = Other
    /// Territory</b>, 99 = OTHER COUNTRIES. "Other Territory" is the GST territory covering India's continental shelf
    /// and EEZ — a domestic place of supply, not an overseas one. Three separate call sites tested
    /// <c>StateCode is "96" or "97"</c> and so mis-classified every 97 supply as an export while missing 99 entirely.
    /// </summary>
    [Fact]
    public void State_97_is_other_territory_and_is_not_an_export_while_99_is()
    {
        var f = Build();

        Assert.False(GstReportSupport.IsOverseasStateCode("97"));
        Assert.True(GstReportSupport.IsOverseasStateCode("96"));
        Assert.True(GstReportSupport.IsOverseasStateCode("99"));
        Assert.False(GstReportSupport.IsOverseasStateCode(null));

        // e-Way: a 97 supply is an ordinary domestic supply (1), not an export (3).
        var toTerritory = f.Service.PartACodesFor(Movement(f, VoucherBaseType.Sales, f.OtherTerritoryPartyId));
        Assert.Equal("1", toTerritory.SubSupplyType);

        // e-Invoice: the same correction, through the same shared predicate. W0-8 (review finding #17) — asserted as a
        // POSITIVE outcome, not `NotEqual(Export, …)`: that form also passes for null, and null means "excluded from
        // e-invoicing entirely", so a reordering that silently dropped the party out of coverage would have stayed
        // green. The 97 party here is a Consumer, so the correct answer is the B2C fall-through, which is null.
        var einv = new EInvoiceService(f.Company);
        Assert.Null(einv.ResolveSupplyCategory(Movement(f, VoucherBaseType.Sales, f.OtherTerritoryPartyId)));
        Assert.Equal(EInvoiceSupplyCategory.Export,
            einv.ResolveSupplyCategory(Movement(f, VoucherBaseType.Sales, f.OverseasPartyId)));

        // …and a REGISTERED party at 97 is an ordinary domestic B2B supply — the shape that would silently vanish from
        // e-invoicing if the resolver ever answered null for it.
        var registered97 = new Domain.Ledger(Guid.NewGuid(), "Other Territory B2B",
            f.Company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
        {
            PartyGst = new PartyGstDetails
            { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "97" },
        };
        f.Company.AddLedger(registered97);
        Assert.Equal(EInvoiceSupplyCategory.Regular,
            einv.ResolveSupplyCategory(Movement(f, VoucherBaseType.Sales, registered97.Id)));
    }

    // ================================================================ 6 — the whole triple is a row of the table

    private static readonly VoucherBaseType[] GoodsMovementBaseTypes =
    {
        VoucherBaseType.Sales, VoucherBaseType.Purchase, VoucherBaseType.CreditNote,
        VoucherBaseType.DebitNote, VoucherBaseType.DeliveryNote, VoucherBaseType.ReceiptNote,
    };

    private static readonly EWayTransactionType[] AllTxnTypes =
    {
        EWayTransactionType.Regular, EWayTransactionType.JobWork, EWayTransactionType.Handicraft,
    };

    /// <summary>
    /// The official <b>Supply Type – Document Type mapping</b>. <b>W0-8:</b> this used to be a second, hand-typed copy
    /// of the table living in this file; it is now the single transcription in <see cref="NicSupplyDocTypeMapping"/>,
    /// which carries all FIVE of the table's columns and its retrieval date. A triple absent from this set is a
    /// combination the portal does not accept, whatever the individual codes are — and the From/To columns the
    /// three-column projection drops are asserted separately, in <see cref="EWayPartAOrientationTests"/>.
    /// </summary>
    private static HashSet<(string Supply, string Sub, string Doc)> OfficialCombinations =>
        NicSupplyDocTypeMapping.Triples;

    /// <summary>
    /// The exhaustive guard: for every goods-movement base type × transaction type × document kind (tax invoice vs
    /// bill of supply) × place of supply (domestic vs overseas) the engine can face, the emitted triple must be a row
    /// of the official mapping. This is what stops a future "obvious" one-field edit — say pairing a delivery challan
    /// with sub-type 1 Supply — from silently producing a request the portal rejects.
    ///
    /// <para><b>It proves the CODES only.</b> Three individually-legal codes can still form a filing the portal
    /// refuses, because the mapping also constrains the consignor/consignee. That half is
    /// <c>EWayPartAOrientationTests.Every_payload_the_app_can_post_is_a_full_five_column_row_of_the_official_mapping</c>,
    /// which asserts on the EWB-01 payload rather than on the code triple.</para>
    /// </summary>
    [Fact]
    public void Every_triple_the_engine_can_emit_is_a_row_of_the_official_NIC_mapping()
    {
        foreach (var registration in new[] { GstRegistrationType.Regular, GstRegistrationType.Composition })
        {
            var f = Build(registration);
            foreach (var partyId in new[] { f.DomesticPartyId, f.OtherTerritoryPartyId, f.OverseasPartyId })
                foreach (var baseType in GoodsMovementBaseTypes)
                    foreach (var txn in AllTxnTypes)
                    {
                        var codes = f.Service.PartACodesFor(Movement(f, baseType, partyId), txn);
                        var triple = (codes.SupplyType, codes.SubSupplyType, codes.DocType);
                        Assert.True(OfficialCombinations.Contains(triple),
                            $"{registration}/{baseType}/{txn} emitted {triple}, which is not a row of the official " +
                            "NIC Supply Type – Document Type mapping.");
                    }
        }
    }
}
