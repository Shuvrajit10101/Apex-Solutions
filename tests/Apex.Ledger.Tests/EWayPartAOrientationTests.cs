using System.Text;
using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>The official NIC "Validating the Supply Type with Document Type" table, transcribed in FULL.</b>
/// Source: <c>https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html</c> (Eway Bill Team, National
/// Informatics Centre, Karnataka, Govt. of India), <b>retrieved 2026-08-13</b> from the page DOM in a real browser
/// (the host answers HTTP 403 to automated fetchers — a bot-block, not a missing document). Sub-supply descriptions
/// are replaced by their numeric code from the companion master-codes list,
/// <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>, retrieved in the same session.
///
/// <para><b>The table has FIVE columns, not three.</b> Transaction Type | Transaction Sub-Type | Document Type |
/// <b>From GSTIN (Supplier)</b> | <b>To GSTIN (Buyer)</b>. Dropping the last two is what let an <c>Inward</c> row be
/// certified "conformant" while the request named the filer itself as the consignor — three individually-legal codes
/// forming a combination the portal refuses. Every row below carries all five columns.</para>
/// </summary>
internal static class NicSupplyDocTypeMapping
{
    /// <summary>Which party each end of the movement may be. <see cref="Self"/> = the filing taxpayer's own GSTIN;
    /// <see cref="Other"/> = "Other GSTIN/URP" (a counterparty, registered or unregistered); <see cref="Either"/> =
    /// the table's "Self/Other/URP", which admits both.</summary>
    internal enum PartyRole { Self, Other, Either }

    internal readonly record struct Row(string Supply, string Sub, string Doc, PartyRole From, PartyRole To);

    private const PartyRole S = PartyRole.Self;
    private const PartyRole O = PartyRole.Other;
    private const PartyRole E = PartyRole.Either;

    /// <summary>All 28 rows, verbatim. Outward first, then Inward, in the page's own order.</summary>
    internal static readonly IReadOnlyList<Row> Rows = new[]
    {
        // ---- Outward | Sub-Type | Document | From (Supplier) | To (Buyer)
        new Row("O", "1",  "INV", S, O),   // Supply              | Tax Invoice     | Self | Other GSTIN/URP
        new Row("O", "1",  "BIL", S, O),   // Supply              | Bill of Supply  | Self | Other GSTIN/URP
        new Row("O", "3",  "INV", S, O),   // Export              | Tax Invoice     | Self | Other GSTIN/URP
        new Row("O", "3",  "BIL", S, O),   // Export              | Bill of Supply  | Self | Other GSTIN/URP
        new Row("O", "4",  "CHL", S, O),   // Job Work            | Delivery Challan| Self | Other GSTIN/URP
        new Row("O", "9",  "INV", S, O),   // SKD/CKD/Lots        | Tax Invoice     | Self | Other GSTIN/URP
        new Row("O", "9",  "BIL", S, O),   // SKD/CKD/Lots        | Bill of Supply  | Self | Other GSTIN/URP
        new Row("O", "9",  "CHL", S, O),   // SKD/CKD/Lots        | Delivery Challan| Self | Other GSTIN/URP
        new Row("O", "11", "CHL", S, S),   // Recipient not known | Delivery Challan| Self | Self
        new Row("O", "11", "OTH", S, S),   // Recipient not known | Others          | Self | Self
        new Row("O", "5",  "CHL", S, S),   // For own Use         | Delivery Challan| Self | Self
        new Row("O", "12", "CHL", S, S),   // Exhibition or fairs | Delivery Challan| Self | Self
        new Row("O", "10", "CHL", S, S),   // Line Sales          | Delivery Challan| Self | Self
        new Row("O", "8",  "CHL", S, E),   // Others              | Delivery Challan| Self | Self/Other/URP
        new Row("O", "8",  "OTH", S, E),   // Others              | Others          | Self | Self/Other/URP
        // ---- Inward
        new Row("I", "1",  "INV", O, S),   // Supply              | Tax Invoice     | Other GSTIN/URP | Self
        new Row("I", "1",  "BIL", O, S),   // Supply              | Bill of Supply  | Other GSTIN/URP | Self
        new Row("I", "2",  "BOE", O, S),   // Import              | Bill of Entry   | Other GSTIN/URP | Self
        new Row("I", "9",  "BOE", O, S),   // SKD/CKD/Lots        | Bill of Entry   | URP             | Self
        new Row("I", "9",  "INV", O, S),   // SKD/CKD/Lots        | Tax Invoice     | Other GSTIN/URP | Self
        new Row("I", "9",  "BIL", O, S),   // SKD/CKD/Lots        | Bill of Supply  | Other GSTIN/URP | Self
        new Row("I", "9",  "CHL", O, S),   // SKD/CKD/Lots        | Delivery Challan| Other GSTIN/URP | Self
        new Row("I", "6",  "CHL", O, S),   // Job Work Returns    | Delivery Challan| Other GSTIN/URP | Self
        new Row("I", "7",  "CHL", O, S),   // Sales Return        | Delivery Challan| Other GSTIN/URP | Self
        new Row("I", "12", "CHL", S, S),   // Exhibition or fairs | Delivery Challan| Self | Self
        new Row("I", "5",  "CHL", S, S),   // For own Use         | Delivery Challan| Self | Self
        new Row("I", "8",  "CHL", E, S),   // Others              | Delivery Challan| Self/Other/URP | Self
        new Row("I", "8",  "OTH", E, S),   // Others              | Others          | Self/Other/URP | Self
    };

    /// <summary>The three CODE columns only — the projection the exhaustive code guard uses.</summary>
    internal static readonly HashSet<(string Supply, string Sub, string Doc)> Triples =
        Rows.Select(r => (r.Supply, r.Sub, r.Doc)).ToHashSet();

    /// <summary>True iff a filed triple, together with the observed consignor/consignee identity, is a row of the
    /// table. <paramref name="fromIsSelf"/> / <paramref name="toIsSelf"/> report whether each end of the emitted
    /// EWB-01 carries the filer's OWN GSTIN.</summary>
    internal static bool Permits(string supply, string sub, string doc, bool fromIsSelf, bool toIsSelf) =>
        Rows.Any(r => r.Supply == supply && r.Sub == sub && r.Doc == doc
                      && Admits(r.From, fromIsSelf) && Admits(r.To, toIsSelf));

    private static bool Admits(PartyRole role, bool isSelf) => role switch
    {
        PartyRole.Self => isSelf,
        PartyRole.Other => !isSelf,
        _ => true,
    };
}

/// <summary>
/// <b>W0-8 — the e-Way Part-A ORIENTATION, reachability, and the legacy rows already on disk.</b> The companion to
/// <see cref="EWayPartACodeTests"/>, which proves the three CODE columns. This suite proves the two columns that one
/// drops — <b>From GSTIN (Supplier)</b> and <b>To GSTIN (Buyer)</b> — and it asserts on the <b>EWB-01 PAYLOAD</b>
/// (<see cref="EWayBillJson.BuildEwb01"/>), not on the record, because the payload is what the portal reads.
///
/// <para>Money fixtures are odd-paisa throughout: a round figure would pass under a rounding defect.</para>
/// </summary>
public sealed class EWayPartAOrientationTests
{
    private const string GstinHome = "27AAPFU0939F1ZV";      // Maharashtra (27) — the filer
    private const string GstinSupplier = "24AAPFU0939F1ZV";  // Gujarat (24) — the counterparty
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly MoveDate = new(2025, 4, 10);

    /// <summary>₹2,04,317.63 — comfortably over the ₹50,000 Rule-138 threshold and odd to the paisa.</summary>
    private static readonly decimal Value = 2_04_317.63m;

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required EWayBillService Service { get; init; }
        public required Voucher Sale { get; init; }
        public required Voucher Purchase { get; init; }
    }

    /// <summary>A company with a real POSTED outward sale to a Gujarat customer and a real POSTED inward purchase from
    /// a Gujarat supplier — both through <see cref="LedgerService.Post"/>, so a shape the application cannot create
    /// fails this fixture instead of passing it (review finding #2).</summary>
    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("e-Way Orientation Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, EWayIntraStateApplicable = true,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(311.17m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        var customer = Add(c, "Gujarat Customer", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "24" };
        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "24" };

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        var sale = post.Post(new Voucher(Guid.NewGuid(), salesType, MoveDate, new[]
        {
            new EntryLine(customer.Id, new Money(Value), DrCr.Debit),
            new EntryLine(sales.Id, new Money(Value), DrCr.Credit),
        }, partyId: customer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, new Money(Value)) }));

        var purchase = post.Post(new Voucher(Guid.NewGuid(), purchaseType, MoveDate, new[]
        {
            new EntryLine(purchases.Id, new Money(Value), DrCr.Debit),
            new EntryLine(supplier.Id, new Money(Value), DrCr.Credit),
        }, partyId: supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, new Money(Value)) }));

        return new Fx { Company = c, Service = new EWayBillService(c), Sale = sale, Purchase = purchase };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static JsonElement Ewb01(Fx f, Voucher v)
    {
        var record = f.Service.PrepareRecord(v, MoveDate);
        var doc = JsonDocument.Parse(EWayBillJson.BuildEwb01(f.Company, v, record));
        return doc.RootElement.Clone();
    }

    // ================================================================ 1 — the consignor / consignee orientation

    /// <summary>
    /// <b>An INWARD movement must name the COUNTERPARTY as consignor and the filer as consignee.</b> The official
    /// mapping's only Inward|Supply|Tax Invoice row is <c>From = Other GSTIN/URP, To = Self</c>. Before this fix
    /// <c>PrepareRecord</c> always stamped ShipFrom = the company's own home state and ShipTo = the party's, and
    /// <c>EWayBillJson</c> always wrote <c>fromGstin</c> = the company's own GSTIN — so a purchase filed an inward
    /// supply whose supplier was the buyer itself. Making <c>supplyType</c> a valid <c>I</c> is exactly what made the
    /// combination check reachable: the old "Inward" description died at field validation first.
    /// </summary>
    [Fact]
    public void An_inward_purchase_names_the_supplier_as_consignor_and_the_filer_as_consignee()
    {
        var f = Build();
        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(f.Purchase));

        var root = Ewb01(f, f.Purchase);
        Assert.Equal("I", root.GetProperty("supplyType").GetString());
        Assert.Equal(GstinSupplier, root.GetProperty("fromGstin").GetString());
        Assert.Equal("24", root.GetProperty("fromStateCode").GetString());
        Assert.Equal(GstinHome, root.GetProperty("toGstin").GetString());
        Assert.Equal("27", root.GetProperty("toStateCode").GetString());
    }

    /// <summary>The mirror: an OUTWARD sale is <c>From = Self, To = Other GSTIN/URP</c>. This half was already right
    /// and must stay right — a fix that merely swapped the two ends unconditionally would break it.</summary>
    [Fact]
    public void An_outward_sale_names_the_filer_as_consignor_and_the_customer_as_consignee()
    {
        var f = Build();
        var root = Ewb01(f, f.Sale);
        Assert.Equal("O", root.GetProperty("supplyType").GetString());
        Assert.Equal(GstinHome, root.GetProperty("fromGstin").GetString());
        Assert.Equal("27", root.GetProperty("fromStateCode").GetString());
        Assert.Equal(GstinSupplier, root.GetProperty("toGstin").GetString());
        Assert.Equal("24", root.GetProperty("toStateCode").GetString());
    }

    /// <summary>
    /// <b>The exhaustive guard, driven off the PAYLOAD and against all FIVE columns.</b> For every movement the
    /// application can actually post, the emitted <c>(supplyType, subSupplyType, docType, fromGstin, toGstin)</c> must
    /// be a row of <see cref="NicSupplyDocTypeMapping"/>. A code-only guard certified the inverted inward row as
    /// conformant; this one cannot.
    /// </summary>
    [Fact]
    public void Every_payload_the_app_can_post_is_a_full_five_column_row_of_the_official_mapping()
    {
        var f = Build();
        foreach (var v in new[] { f.Sale, f.Purchase })
        {
            var root = Ewb01(f, v);
            var supply = root.GetProperty("supplyType").GetString()!;
            var sub = root.GetProperty("subSupplyType").GetString()!;
            var doc = root.GetProperty("docType").GetString()!;
            var fromIsSelf = root.GetProperty("fromGstin").GetString() == GstinHome;
            var toIsSelf = root.GetProperty("toGstin").GetString() == GstinHome;

            Assert.True(NicSupplyDocTypeMapping.Permits(supply, sub, doc, fromIsSelf, toIsSelf),
                $"({supply},{sub},{doc}) with fromIsSelf={fromIsSelf}, toIsSelf={toIsSelf} is not a row of the " +
                "official NIC Supply-Type/Document-Type mapping.");
        }
    }

    /// <summary>The intra-state ≤ 50 km Part-B relaxation compares ShipFrom against ShipTo, so it must be unaffected by
    /// the orientation fix — the comparison is symmetric, and an inter-state purchase stays inter-state.</summary>
    [Fact]
    public void The_orientation_fix_does_not_disturb_the_intra_state_Part_B_relaxation()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(f.Purchase, MoveDate);
        Assert.NotEqual(record.ShipFromStateCode, record.ShipToStateCode);   // 24 -> 27, still inter-state
        Assert.True(EWayBillService.RequiresPartB(record));
    }

    // ================================================================ 2 — reachability, honestly stated

    /// <summary>
    /// <b>Four of <c>PartACodesFor</c>'s six branches are UNREACHABLE in the shipped application</b> (review finding
    /// #2), and this test is what stops them being read as live mappings. <c>CoverageOf</c> gates on
    /// <see cref="Voucher.HasInventoryLines"/>, and <c>VoucherValidator.EnsureItemInvoiceValid</c> refuses stock lines
    /// on anything but a Purchase or a Sales voucher — so a Credit/Debit Note or a Delivery/Receipt Note carrying the
    /// inventory <c>CoverageOf</c> demands cannot be POSTED at all. Delivery/Receipt notes additionally hold their
    /// stock as a separate <c>InventoryVoucher</c>, which <c>CoverageOf(Voucher)</c> never sees.
    ///
    /// <para>The branches are kept (they are the correct codes for the day the challan path is wired up) but they are
    /// pinned as unreachable here, so nobody mistakes the suite's coverage of them for production coverage.</para>
    /// </summary>
    [Fact]
    public void PINNED_the_return_and_challan_branches_cannot_be_posted_so_they_are_unreachable_today()
    {
        var f = Build();
        var c = f.Company;
        var widget = c.StockItems.First();
        var main = c.MainLocation!.Id;
        var party = c.FindLedgerByName("Gujarat Customer")!;
        var sales = c.FindLedgerByName("Sales")!;
        var post = new LedgerService(c);

        foreach (var baseType in new[]
        {
            VoucherBaseType.CreditNote, VoucherBaseType.DebitNote,
            VoucherBaseType.DeliveryNote, VoucherBaseType.ReceiptNote,
        })
        {
            var typeId = c.VoucherTypes.First(t => t.BaseType == baseType).Id;
            var v = new Voucher(Guid.NewGuid(), typeId, MoveDate, new[]
            {
                new EntryLine(party.Id, new Money(Value), DrCr.Debit),
                new EntryLine(sales.Id, new Money(Value), DrCr.Credit),
            }, partyId: party.Id,
                inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, new Money(Value)) });

            var ex = Assert.Throws<InvalidVoucherException>(() => post.Post(v));
            Assert.Contains("only valid on a Purchase or Sales voucher", ex.Message);
        }
    }

    /// <summary>
    /// <c>PartACodesFor</c> was made <b>public</b> by this slice, so it must refuse a voucher whose type the company
    /// cannot identify rather than fabricate a statutory triple for it (review finding #12). It used to default the
    /// base type to Sales, which silently produced <c>("O","1","INV")</c> — an outward taxable supply on a Tax
    /// Invoice — for a document of unknown kind.
    /// </summary>
    [Fact]
    public void PartACodesFor_refuses_a_voucher_whose_type_the_company_cannot_identify()
    {
        var f = Build();
        var orphan = new Voucher(Guid.NewGuid(), Guid.NewGuid(), MoveDate, new[]
        {
            new EntryLine(f.Company.FindLedgerByName("Sales")!.Id, new Money(Value), DrCr.Credit),
            new EntryLine(f.Company.FindLedgerByName("Gujarat Customer")!.Id, new Money(Value), DrCr.Debit),
        });
        Assert.Throws<ArgumentException>(() => f.Service.PartACodesFor(orphan));
    }

    // ================================================================ 3 — the rows already on disk

    /// <summary>
    /// <b>Records prepared BEFORE this correction are still on disk, and they are unfixable in-app</b> (review finding
    /// #6): <c>SupplyType</c>/<c>SubSupplyType</c>/<c>DocType</c> are get-only with no mutator, <c>PrepareRecord</c>
    /// refuses a second record for the same voucher, and <c>Cancel</c> requires a Generated status — while Pending is
    /// the normal resting state offline, because an EWB number only ever arrives from the portal (ER-5). So a row
    /// written as <c>('Outward','Supply','CRN')</c> would be filed verbatim, CRN and all.
    ///
    /// <para><see cref="EWayBillRecord.Rehydrate"/> — the single funnel for every persisted AND imported record
    /// (<c>SqliteCompanyStore</c> and <c>ImportPlan</c> both go through it) — now re-derives the triple from the
    /// legacy value, so no schema bump is needed and nothing on disk is rewritten behind the user's back.</para>
    /// </summary>
    [Theory]
    // legacy supply | legacy sub | legacy doc  ⇒  corrected triple
    [InlineData("Outward", "Supply", "INV", "O", "1", "INV")]        // a sale
    [InlineData("Inward", "Supply", "INV", "I", "1", "INV")]         // a purchase
    [InlineData("Outward", "Supply", "CRN", "I", "7", "CHL")]        // a sales return: CRN is not an e-Way code
    [InlineData("Inward", "Supply", "DBN", "O", "8", "CHL")]         // a purchase return: DBN is not an e-Way code
    [InlineData("Outward", "Supply", "CHL", "O", "8", "CHL")]        // a plain delivery note
    [InlineData("Inward", "Supply", "CHL", "I", "8", "CHL")]         // a plain receipt note
    [InlineData("Outward", "Job Work", "CHL", "O", "4", "CHL")]      // the job-work out leg
    [InlineData("Inward", "Job Work", "CHL", "I", "6", "CHL")]       // the job-work return leg
    [InlineData("Outward", "Handicraft", "INV", "O", "1", "INV")]    // "Handicraft" is not a NIC sub-supply type
    public void A_legacy_record_is_re_derived_into_NIC_codes_when_it_is_rehydrated(
        string oldSupply, string oldSub, string oldDoc, string supply, string sub, string doc)
    {
        var r = LegacyRecord(oldSupply, oldSub, oldDoc);
        Assert.Equal(supply, r.SupplyType);
        Assert.Equal(sub, r.SubSupplyType);
        Assert.Equal(doc, r.DocType);
        Assert.True(NicSupplyDocTypeMapping.Triples.Contains((r.SupplyType!, r.SubSupplyType!, r.DocType!)),
            $"legacy ({oldSupply},{oldSub},{oldDoc}) normalised to a combination outside the official mapping.");
    }

    /// <summary>A record already carrying NIC codes is passed through <b>untouched</b> — normalisation must never
    /// second-guess a value the corrected engine wrote.</summary>
    [Theory]
    [InlineData("O", "1", "BIL")]
    [InlineData("O", "3", "INV")]
    [InlineData("I", "7", "CHL")]
    [InlineData("I", "2", "BOE")]
    [InlineData("O", "8", "OTH")]
    public void A_record_already_in_NIC_codes_is_rehydrated_verbatim(string supply, string sub, string doc)
    {
        var r = LegacyRecord(supply, sub, doc);
        Assert.Equal((supply, sub, doc), (r.SupplyType, r.SubSupplyType, r.DocType));
    }

    /// <summary>A record with no Part-A at all (an older import that never carried the fields) stays null rather than
    /// acquiring an invented triple.</summary>
    [Fact]
    public void A_record_with_no_Part_A_codes_stays_null()
    {
        var r = LegacyRecord(null, null, null);
        Assert.Null(r.SupplyType);
        Assert.Null(r.SubSupplyType);
        Assert.Null(r.DocType);
    }

    private static EWayBillRecord LegacyRecord(string? supply, string? sub, string? doc) =>
        EWayBillRecord.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "INV/0007", EWayStatus.Pending, supply, sub, doc, 2_04_31_763,
            null, null, null, 0, null, "27", "24", false, null, false, null,
            ewbNumber: null, generatedAt: null, validUpto: null, cancelledOn: null, cancelReasonCode: null);

    // ================================================================ 4 — the two code domains stay separate

    /// <summary>
    /// <b>The INV-01 <c>DocDtls.Typ</c> domain is NOT the e-Way <c>docType</c> domain, and nothing protected that.</b>
    /// Mutating <c>EInvoiceJson</c>'s base-type⇒docType switch from CRN/DBN to CHL left all 383 Io tests green, so the
    /// e-invoice half of the very separation this slice argues for was unasserted. INV-01 field 9 is String(3) with
    /// exactly three values — INV / CRN / DBN (official schema
    /// <c>https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf</c>) — while the e-Way domain is INV / BIL / BOE
    /// / CHL / OTH. They overlap only on "INV". Merging the two switches would guarantee a wrong value on one of them.
    /// </summary>
    [Fact]
    public void A_credit_note_files_CRN_on_the_INV_01_and_never_on_the_e_Way_request()
    {
        var f = Build();
        var c = f.Company;
        var party = c.FindLedgerByName("Gujarat Customer")!;
        var sales = c.FindLedgerByName("Sales")!;
        var creditType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        // An as-voucher credit note (no stock lines — the only shape the app can post, see the reachability pin).
        var note = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), creditType, MoveDate, new[]
        {
            new EntryLine(sales.Id, new Money(Value), DrCr.Debit),
            new EntryLine(party.Id, new Money(Value), DrCr.Credit),
        }, partyId: party.Id));

        using var inv01 = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, note)));
        Assert.Equal("CRN", inv01.RootElement.GetProperty("DocDtls").GetProperty("Typ").GetString());

        // …and CRN is nowhere in the e-Way document-type domain.
        Assert.DoesNotContain("CRN", NicSupplyDocTypeMapping.Rows.Select(r => r.Doc));
    }
}
