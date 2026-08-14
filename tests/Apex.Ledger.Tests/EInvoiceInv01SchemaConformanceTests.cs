using System.Globalization;
using System.Text;
using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>The official NIC INV-01 e-invoice schema, transcribed field-by-field.</b>
///
/// <para><b>Source (primary):</b> <c>https://einvoice1.gst.gov.in/Documents/EInvoice_Schema.xlsx</c> — the e-invoice
/// schema workbook published by NIC on the national e-invoice portal, <b>retrieved 2026-08-14 by direct HTTPS GET
/// (curl, HTTP 200, 198,376 bytes)</b>. It carries five sheets; three are transcribed here: <b>"Schema"</b> (the
/// normative JSON-Schema draft-07 document — types, lengths, enums, <c>required</c> lists), <b>"E-Invoice
/// Attributes"</b> (titled "E-Invoice JSON ATTRIBUTES - Version (1.01)" — data type, width, sample, cardinality) and
/// <b>"Validations"</b> (the arithmetic and rate rules, "V 1.3.1").</para>
///
/// <para><b>Source (corroborating — and its limits are stated, because it does NOT agree throughout).</b>
/// <c>https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf</c> — retrieved the same way (curl, HTTP 200,
/// 148,829 bytes) and read with <c>pdftotext -layout</c>, then searched whitespace-insensitively (the tabulation
/// line-wraps field names, so a naive grep under-reports). It is the <b>superseded v1.00</b> tabulation. It
/// corroborates the names <c>AssVal</c>, <c>TotInvVal</c>, <c>TotAmt</c>, <c>AssAmt</c>, <c>UnitPrice</c>,
/// <c>Qty</c>, <c>HsnCd</c>, <c>CesRt</c> and <c>BuyerDtls.Stcd</c> — and <b>nothing else transcribed here</b>.
/// <b>Measured occurrence counts over the whitespace-stripped text: <c>Pos</c> 0, <c>GstRt</c> 0, <c>IsServc</c> 0,
/// <c>LglNm</c> 0, <c>CesAmt</c> 0, <c>CesNonAdvlAmt</c> 0.</b> It also spells the seller's state code
/// <c>SellerDtls.StCd</c> (capital C) where the workbook and this writer emit <c>Stcd</c>; it nests items as
/// <c>ItemList.Item.*</c> rather than <c>ItemList[]</c>; and it names per-head rates <c>CgstRt</c>/<c>SgstRt</c>/
/// <c>IgstRt</c> where the current schema has the single combined <c>GstRt</c>. <b>For those six names the xlsx is
/// the only source, and it is cited alone.</b> An earlier revision of this file claimed the PDF "agrees on every
/// field name transcribed below" — it does not, and a manufactured corroboration is worse than none (this project
/// already carries a recorded defect from citing a weak source for shipped TDS rates).</para>
///
/// <para><b>Why this class exists.</b> <see cref="EInvoiceJson"/> used to emit invented snake-case keys —
/// <c>ass_val_paisa</c>, <c>cgst_val_paisa</c>, <c>ces_amt_paisa</c>, <c>qty_millis</c>, <c>unit_price_paisa</c> and
/// nine more — where NIC names <c>AssVal</c>, <c>CgstVal</c>, <c>CesAmt</c>, <c>Qty</c>, <c>UnitPrice</c>. The file
/// said so itself in a standing "R7 (A14 to confirm)" note, and
/// <c>tests/Apex.Ledger.Io.Tests/EInvoiceConnectorJsonTests.cs</c> named the real field <c>CesAmt</c> in a comment
/// four lines above the assertion that pinned the invented one. The correct answer was known and written down; the
/// suite pinned the wrong one. Transcribing the official table is what stops that recurring.</para>
///
/// <para><b>The units were wrong too, and that is the larger half of the defect.</b> Every NIC money field is
/// <c>type: "number"</c> with a rupee scale (Number(12,2) at item level, Number(14,2) at document level); we emitted
/// integer <b>paisa</b> — every declared amount 100× overstated. <c>Qty</c> is Number(10,3); we emitted <b>millis</b>
/// — 1000× overstated. <c>GstRt</c> is documented "The GST rate, represented as <b>percentage</b>" with
/// <c>maximum: 999.999</c>; we emitted <b>basis points</b> — 100× overstated. A rate in the wrong unit is the same
/// class of defect as a wrong key, so all three are pinned here.</para>
/// </summary>
internal static class NicInv01Schema
{
    internal enum Req { Mandatory, Optional }

    /// <summary>One transcribed schema row. <paramref name="Scale"/> is the maximum number of decimal places the
    /// schema's <c>maximum</c> permits for a numeric field (0 for strings).</summary>
    internal readonly record struct Field(string Path, string Type, Req Requirement, int Scale, string Desc);

    private const Req M = Req.Mandatory;
    private const Req O = Req.Optional;

    /// <summary>
    /// The INV-01 fields, verbatim from the workbook's "Schema" sheet. Only the objects this application actually
    /// emits are transcribed in full; the <c>required</c> lists are reproduced exactly as published so a missing
    /// mandatory field is a test failure rather than an omission nobody notices.
    /// </summary>
    internal static readonly IReadOnlyList<Field> Fields = new[]
    {
        // ---- root. required: [Version, TranDtls, DocDtls, SellerDtls, BuyerDtls, ItemList, ValDtls]
        new Field("Version", "string", M, 0, "Version of the schema; maxLength 6. Sample JSON emits \"1.1\"."),

        // ---- TranDtls. required: [TaxSch, SupTyp]
        new Field("TranDtls.TaxSch", "string", M, 0, "enum [GST]"),
        new Field("TranDtls.SupTyp", "string", M, 0, "enum [B2B, SEZWP, SEZWOP, EXPWP, EXPWOP, DEXP]"),
        new Field("TranDtls.RegRev", "string", O, 0, "enum [Y, N] — tax payable under reverse charge"),
        new Field("TranDtls.EcmGstin", "string", O, 0, "GSTIN of e-Commerce operator"),
        new Field("TranDtls.IgstOnIntra", "string", O, 0, "enum [Y, N]"),

        // ---- DocDtls. required: [Typ, No, Dt]
        new Field("DocDtls.Typ", "string", M, 0, "enum [INV, CRN, DBN] — minLength 3, maxLength 3"),
        new Field("DocDtls.No", "string", M, 0, "maxLength 16, pattern ^([A-Z1-9]{1}[A-Z0-9/-]{0,15})$"),
        new Field("DocDtls.Dt", "string", M, 0, "pattern [0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9] — DD/MM/YYYY"),

        // ---- SellerDtls. required: [Gstin, LglNm, Addr1, Loc, Pin, Stcd]
        new Field("SellerDtls.Gstin", "string", M, 0, "pattern ([0-9]{2}[0-9A-Z]{13})"),
        new Field("SellerDtls.LglNm", "string", M, 0, "Legal Name"),
        new Field("SellerDtls.TrdNm", "string", O, 0, "Tradename"),
        new Field("SellerDtls.Addr1", "string", M, 0, "Building/Flat no, Road/Street"),
        new Field("SellerDtls.Addr2", "string", O, 0, "Address 2"),
        new Field("SellerDtls.Loc", "string", M, 0, "Location"),
        new Field("SellerDtls.Pin", "number", M, 0, "PIN"),
        new Field("SellerDtls.Stcd", "string", M, 0, "State Code of the supplier. Refer the master"),
        new Field("SellerDtls.Ph", "string", O, 0, "Phone or Mobile No."),
        new Field("SellerDtls.Em", "string", O, 0, "Email-Id"),

        // ---- BuyerDtls. required: [Gstin, LglNm, Pos, Addr1, Loc, Stcd]
        new Field("BuyerDtls.Gstin", "string", M, 0,
            "minLength 3, maxLength 15, pattern ^(([0-9]{2}[0-9A-Z]{13})|URP)$ — \"GSTIN of buyer , URP if exporting\""),
        new Field("BuyerDtls.LglNm", "string", M, 0, "Legal Name"),
        new Field("BuyerDtls.TrdNm", "string", O, 0, "Tradename"),
        new Field("BuyerDtls.Pos", "string", M, 0,
            "State code of Place of supply. If POS lies outside the country, the code shall be 96."),
        new Field("BuyerDtls.Addr1", "string", M, 0, "Building/Flat no, Road/Street"),
        new Field("BuyerDtls.Addr2", "string", O, 0, "Address 2"),
        new Field("BuyerDtls.Loc", "string", M, 0, "Location"),
        new Field("BuyerDtls.Pin", "number", O, 0, "PIN"),
        new Field("BuyerDtls.Stcd", "string", M, 0, "State Code of the buyer. Refer the master"),
        new Field("BuyerDtls.Ph", "string", O, 0, "Phone or Mobile No."),
        new Field("BuyerDtls.Em", "string", O, 0, "Email-Id"),

        // ---- ItemList[]. required: [SlNo, IsServc, HsnCd, UnitPrice, TotAmt, AssAmt, GstRt, TotItemVal]
        new Field("ItemList[].SlNo", "string", M, 0, "Serial No. of Item — type STRING, minLength 1, maxLength 6"),
        new Field("ItemList[].PrdDesc", "string", O, 0, "Product Description"),
        new Field("ItemList[].IsServc", "string", M, 0,
            "enum [Y, N] — \"Specify whether the supply is service or not. Specify Y-for Service\""),
        new Field("ItemList[].HsnCd", "string", M, 0, "HSN Code. minLength 4, maxLength 8"),
        new Field("ItemList[].Barcde", "string", O, 0, "Bar Code"),
        new Field("ItemList[].Qty", "number", O, 3, "Quantity. maximum 9999999999.999"),
        new Field("ItemList[].FreeQty", "number", O, 3, "Free Quantity"),
        new Field("ItemList[].Unit", "string", O, 0, "Unit. Refer the master"),
        new Field("ItemList[].UnitPrice", "number", M, 3, "Unit Price - Rate. maximum 999999999999.999"),
        new Field("ItemList[].TotAmt", "number", M, 2, "Gross Amount (Unit Price * Quantity)"),
        new Field("ItemList[].Discount", "number", O, 2, "Discount"),
        new Field("ItemList[].PreTaxVal", "number", O, 2, "Pre tax value"),
        new Field("ItemList[].AssAmt", "number", M, 2, "Taxable Value (Total Amount - Discount)"),
        new Field("ItemList[].GstRt", "number", M, 3, "The GST rate, represented as PERCENTAGE. maximum 999.999"),
        new Field("ItemList[].IgstAmt", "number", O, 2, "Amount of IGST payable"),
        new Field("ItemList[].CgstAmt", "number", O, 2, "Amount of CGST payable"),
        new Field("ItemList[].SgstAmt", "number", O, 2, "Amount of SGST payable"),
        new Field("ItemList[].CesRt", "number", O, 3, "Cess Rate. maximum 999.999"),
        new Field("ItemList[].CesAmt", "number", O, 2,
            "\"Cess Amount(Advalorem) on basis of rate and quantity of item\""),
        new Field("ItemList[].CesNonAdvlAmt", "number", O, 2, "Cess Non-Advol Amount"),
        new Field("ItemList[].StateCesRt", "number", O, 3, "State CESS Rate"),
        new Field("ItemList[].StateCesAmt", "number", O, 2, "State CESS Amount"),
        new Field("ItemList[].StateCesNonAdvlAmt", "number", O, 2, "State CESS Non Adval Amount"),
        new Field("ItemList[].OthChrg", "number", O, 2, "Other Charges"),
        new Field("ItemList[].TotItemVal", "number", M, 2,
            "AssAmt + CGST + SGST + Cess + CesNonAdvl + StateCes + StateCesNonAdvl + OthChrg"),
        new Field("ItemList[].OrdLineRef", "string", O, 0, "Order line reference"),
        new Field("ItemList[].OrgCntry", "string", O, 0, "Origin Country"),
        new Field("ItemList[].PrdSlNo", "string", O, 0, "Serial number"),

        // ---- ValDtls. required: [AssVal, TotInvVal]
        new Field("ValDtls.AssVal", "number", M, 2, "Total Assessable value of all items"),
        new Field("ValDtls.CgstVal", "number", O, 2, "Total CGST value of all items"),
        new Field("ValDtls.SgstVal", "number", O, 2, "Total SGST value of all items"),
        new Field("ValDtls.IgstVal", "number", O, 2, "Total IGST value of all items"),
        new Field("ValDtls.CesVal", "number", O, 2, "Total CESS value of all items"),
        new Field("ValDtls.StCesVal", "number", O, 2, "Total State CESS value of all items"),
        new Field("ValDtls.Discount", "number", O, 2, "Discount"),
        new Field("ValDtls.OthChrg", "number", O, 2, "Other Charges"),
        new Field("ValDtls.RndOffAmt", "number", O, 2, "Rounded off amount. maximum 99.99"),
        new Field("ValDtls.TotInvVal", "number", M, 2, "Final Invoice value"),
        new Field("ValDtls.TotInvValFc", "number", O, 2, "Final Invoice value in Additional Currency"),
    };

    /// <summary>Every legal dotted path, for the "no invented key" guard.</summary>
    internal static readonly HashSet<string> Paths = Fields.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);

    internal static Field? Find(string path) =>
        Fields.Where(f => f.Path == path).Cast<Field?>().FirstOrDefault();

    /// <summary>The document-level objects the root <c>required</c> list names.</summary>
    internal static readonly IReadOnlyList<string> RootRequired = new[]
    { "Version", "TranDtls", "DocDtls", "SellerDtls", "BuyerDtls", "ItemList", "ValDtls" };

    /// <summary>
    /// Every scalar path the schema's own <c>required</c> lists name, verbatim from the "Schema" sheet:
    /// <c>TranDtls: [TaxSch, SupTyp]</c>, <c>DocDtls: [Typ, No, Dt]</c>,
    /// <c>SellerDtls: [Gstin, LglNm, Addr1, Loc, Pin, Stcd]</c>, <c>BuyerDtls: [Gstin, LglNm, Pos, Addr1, Loc, Stcd]</c>,
    /// <c>ItemList[]: [SlNo, IsServc, HsnCd, UnitPrice, TotAmt, AssAmt, GstRt, TotItemVal]</c>,
    /// <c>ValDtls: [AssVal, TotInvVal]</c>.
    /// </summary>
    internal static readonly IReadOnlyList<string> RequiredScalars = new[]
    {
        "Version",
        "TranDtls.TaxSch", "TranDtls.SupTyp",
        "DocDtls.Typ", "DocDtls.No", "DocDtls.Dt",
        "SellerDtls.Gstin", "SellerDtls.LglNm", "SellerDtls.Addr1", "SellerDtls.Loc", "SellerDtls.Pin",
        "SellerDtls.Stcd",
        "BuyerDtls.Gstin", "BuyerDtls.LglNm", "BuyerDtls.Pos", "BuyerDtls.Addr1", "BuyerDtls.Loc", "BuyerDtls.Stcd",
        "ItemList[].SlNo", "ItemList[].IsServc", "ItemList[].HsnCd", "ItemList[].UnitPrice", "ItemList[].TotAmt",
        "ItemList[].AssAmt", "ItemList[].GstRt", "ItemList[].TotItemVal",
        "ValDtls.AssVal", "ValDtls.TotInvVal",
    };

    /// <summary>
    /// The mandatory paths this application <b>knowingly</b> does not emit, because its domain model carries no
    /// source for them (see the PINNED tests). Anything mandatory OUTSIDE this set must be present and
    /// <b>non-null</b> — a JSON <c>null</c> is not a string, and the request is rejected on schema validation before
    /// a single figure is read.
    /// </summary>
    internal static readonly HashSet<string> KnownOmittedMandatory = new(StringComparer.Ordinal)
    {
        "SellerDtls.LglNm", "SellerDtls.Addr1", "SellerDtls.Loc", "SellerDtls.Pin",
        "BuyerDtls.LglNm", "BuyerDtls.Addr1", "BuyerDtls.Loc",
    };

    /// <summary>Valid UQCs, from the workbook's "Master Codes" sheet (Unit column, 53 entries). Only the ones this
    /// application can emit here are transcribed.</summary>
    internal static readonly HashSet<string> UqcSample = new(StringComparer.Ordinal) { "OTH", "NOS", "DOZ", "BAG" };
}

/// <summary>
/// <b>F14 — the INV-01 payload files NIC field names, in NIC units.</b> The e-invoice counterpart of
/// <see cref="EWayPartAOrientationTests"/>, and it asserts on the <b>emitted PAYLOAD</b>
/// (<see cref="EInvoiceJson.BuildInv01"/>) rather than on any intermediate, because the payload is what the IRP reads.
///
/// <para>Money fixtures are odd-paisa throughout: ₹1,23,456.79 taxable. A round figure would pass under a
/// paisa/rupee confusion — ₹50,000 and 5,000,000 paisa are both "clean" numbers — which is precisely how the
/// 100× unit defect survived a green suite.</para>
/// </summary>
public sealed class EInvoiceInv01SchemaConformanceTests
{
    private const string GstinHome = "27AAPFU0939F1ZV";      // Maharashtra (27) — the filer
    private const string GstinGujarat = "24AAPFU0939F1ZV";   // Gujarat (24) — an inter-state counterparty
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    /// <summary>₹1,23,456.79 — odd to the paisa.</summary>
    private const decimal Taxable = 1_23_456.79m;

    /// <summary>The stock line: 2.5 Nos @ ₹9,876.54 = ₹24,691.35. The quantity is deliberately fractional (it is what
    /// catches a millis encoding) while the rate stays paisa-exact, which the item-invoice pairing invariant
    /// (Σ qty × rate == the accounting leg) and <c>MoneyCodec</c>'s throw-on-inexact guard both require.</summary>
    private const decimal ItemQty = 2.5m;
    private const decimal ItemRate = 9_876.54m;
    private const decimal ItemTaxable = 24_691.35m;

    /// <summary>The second stock line of <see cref="Fx.TwoItemSale"/>: 1.5 Nos @ ₹1,234.57 = ₹1,851.855 ⇒ NOT
    /// paisa-exact, so 3 Nos @ ₹1,234.57 = ₹3,703.71 is used instead. Σ = ₹28,395.06.</summary>
    private const decimal Item2Qty = 3m;
    private const decimal Item2Rate = 1_234.57m;
    private const decimal Item2Taxable = 3_703.71m;

    /// <summary>The per-unit cess fixture: 100 units at ₹7.77 each ⇒ ₹777.00 of NON-ad-valorem cess.</summary>
    private const decimal SpecificCessPerUnit = 7.77m;
    private const decimal SpecificCessQuantity = 100m;

    /// <summary>The ad-valorem cess fixture rate: 12% (1200 bp) on the odd-paisa taxable value.</summary>
    private const int CessBasisPoints = 1200;

    private sealed class Fx
    {
        public required Company Company { get; init; }

        /// <summary>A plain As-Voucher sale: the income ledger declares NO <c>SalesPurchaseGst</c> at all, so the
        /// writer takes the synthetic-item fallback branch (HsnCd "").</summary>
        public required Voucher LedgerOnlySale { get; init; }

        /// <summary>One inventory line — the item-invoice path.</summary>
        public required Voucher ItemSale { get; init; }

        /// <summary>Two inventory lines, so per-item fields (SlNo, the cess attribution) are exercised on more
        /// than one line.</summary>
        public required Voucher TwoItemSale { get; init; }

        /// <summary>A genuine accounting (service) invoice: <c>isAccountingInvoice: true</c> through an income
        /// ledger declaring <c>SupplyType = Services</c> and the SAC 998311.</summary>
        public required Voucher ServiceInvoice { get; init; }

        /// <summary>A ledger-only sale of GOODS — the accounts-only trading shape. The income ledger declares
        /// <c>SupplyType = Goods</c> and the 8-digit HSN 84713010.</summary>
        public required Voucher GoodsLedgerSale { get; init; }

        /// <summary>An INTER-state sale to a Gujarat (24) recipient ⇒ IGST only.</summary>
        public required Voucher InterStateSale { get; init; }

        /// <summary>A direct export under LUT/bond: overseas recipient (96), no GSTIN, no tax charged.</summary>
        public required Voucher ExportWithoutPayment { get; init; }

        /// <summary>A direct export ON payment of IGST: overseas recipient (96), no GSTIN, IGST posted.</summary>
        public required Voucher ExportWithPayment { get; init; }

        /// <summary>An intra-state sale bearing 12% AD-VALOREM compensation cess.</summary>
        public required Voucher CessSale { get; init; }

        /// <summary>An intra-state sale bearing a per-unit (SPECIFIC) compensation cess — non-ad-valorem, and the
        /// posted line carries no ad-valorem rate at all.</summary>
        public required Voucher SpecificCessSale { get; init; }
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A GST company with ten real POSTED outward vouchers, every one through <see cref="LedgerService.Post"/>,
    /// so a shape the application cannot create fails this fixture instead of passing it.</summary>
    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("INV-01 Conformance Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(500m));
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails
        { HsnSac = "847160", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(200m));

        var sales = Add(c, "Sales", "Sales Accounts", false);

        // A SAC-bearing SERVICE income ledger (the accounting-invoice shape) …
        var serviceIncome = Add(c, "Consultancy Income", "Sales Accounts", false);
        serviceIncome.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        // … and an HSN-bearing GOODS income ledger. This is the accounts-only trading shape: the ledger declares
        // an 8-digit GOODS HSN, so the payload's IsServc must read "N" even though the voucher carries no stock.
        var goodsIncome = Add(c, "Goods Sales (accounts only)", "Sales Accounts", false);
        goodsIncome.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "84713010", SupplyType = GstSupplyType.Goods,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var b2b = Add(c, "Local Debtor", "Sundry Debtors", true);
        b2b.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinHome, StateCode = "27" };

        var gujarat = Add(c, "Gujarat Customer", "Sundry Debtors", true);
        gujarat.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        // The overseas recipient: no GSTIN at all, state code 96 ("OTHER COUNTRIES" in the NIC state master).
        var overseas = Add(c, "Overseas Buyer", "Sundry Debtors", true);
        overseas.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Unregistered, StateCode = "96" };

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // A taxed sale through `incomeLedger` to `party`, intra- or inter-state, with an optional cess charge.
        List<EntryLine> Legs(
            decimal taxable, Domain.Ledger income, Domain.Ledger party, bool interState,
            GstService.CessCharge? cess = null)
        {
            var tax = gst.ComputeInvoiceTax(
                new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800, cess) },
                interState, GstTaxDirection.Output);
            var l = new List<EntryLine>
            {
                new(party.Id, new Money(taxable + tax.TotalTax.Amount + tax.TotalCess.Amount), DrCr.Debit),
                new(income.Id, Money.FromRupees(taxable), DrCr.Credit),
            };
            l.AddRange(tax.TaxLines);
            return l;
        }

        var ledgerOnly = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate, Legs(Taxable, sales, b2b, interState: false), partyId: b2b.Id));

        // The stock leg must satisfy the item-invoice pairing invariant: Σ (quantity × RATE) == the accounting
        // amount. The 4th constructor argument is the RATE, not the line value (Value = Rate × BilledQuantity).
        var itemSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(1), Legs(ItemTaxable, sales, b2b, interState: false),
            partyId: b2b.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, ItemQty, Money.FromRupees(ItemRate)),
            }));

        var twoItemSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(2),
            Legs(ItemTaxable + Item2Taxable, sales, b2b, interState: false), partyId: b2b.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, ItemQty, Money.FromRupees(ItemRate)),
                new VoucherInventoryLine(gadget.Id, c.MainLocation!.Id, Item2Qty, Money.FromRupees(Item2Rate)),
            }));

        var serviceInvoice = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(3),
            Legs(Taxable, serviceIncome, b2b, interState: false), partyId: b2b.Id,
            isAccountingInvoice: true));

        var goodsLedgerSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(4),
            Legs(Taxable, goodsIncome, b2b, interState: false), partyId: b2b.Id));

        var interStateSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(5),
            Legs(Taxable, sales, gujarat, interState: true), partyId: gujarat.Id));

        // A direct export under LUT/bond: zero-rated, so NO tax line is posted at all.
        var exportWithoutPayment = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(6),
            new List<EntryLine>
            {
                new(overseas.Id, Money.FromRupees(Taxable), DrCr.Debit),
                new(sales.Id, Money.FromRupees(Taxable), DrCr.Credit),
            }, partyId: overseas.Id));

        // A direct export ON payment of IGST (an export is always inter-state — "Validations" rule 25).
        var exportWithPayment = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(7),
            Legs(Taxable, sales, overseas, interState: true), partyId: overseas.Id));

        var cessSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(8),
            Legs(Taxable, sales, b2b, interState: false,
                new GstService.CessCharge(
                    CessValuationMode.AdValorem, CessBasisPoints, Money.Zero, 0, Money.Zero, 0m)),
            partyId: b2b.Id));

        var specificCessSale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate.AddDays(9),
            Legs(Taxable, sales, b2b, interState: false,
                new GstService.CessCharge(
                    CessValuationMode.Specific, 0, Money.FromRupees(SpecificCessPerUnit), 0, Money.Zero,
                    SpecificCessQuantity)),
            partyId: b2b.Id));

        return new Fx
        {
            Company = c,
            LedgerOnlySale = ledgerOnly,
            ItemSale = itemSale,
            TwoItemSale = twoItemSale,
            ServiceInvoice = serviceInvoice,
            GoodsLedgerSale = goodsLedgerSale,
            InterStateSale = interStateSale,
            ExportWithoutPayment = exportWithoutPayment,
            ExportWithPayment = exportWithPayment,
            CessSale = cessSale,
            SpecificCessSale = specificCessSale,
        };
    }

    private static JsonElement Inv01(Company c, Voucher v) =>
        JsonDocument.Parse(EInvoiceJson.BuildInv01(c, v)).RootElement.Clone();

    /// <summary>Every voucher the fixture posts, so a whole-payload guard runs on ALL of them rather than on the
    /// one shape that happens to be conformant.</summary>
    private static IEnumerable<(string Name, Voucher Voucher)> AllVouchers(Fx f) => new[]
    {
        ("LedgerOnlySale", f.LedgerOnlySale),
        ("ItemSale", f.ItemSale),
        ("TwoItemSale", f.TwoItemSale),
        ("ServiceInvoice", f.ServiceInvoice),
        ("GoodsLedgerSale", f.GoodsLedgerSale),
        ("InterStateSale", f.InterStateSale),
        ("ExportWithoutPayment", f.ExportWithoutPayment),
        ("ExportWithPayment", f.ExportWithPayment),
        ("CessSale", f.CessSale),
        ("SpecificCessSale", f.SpecificCessSale),
    };

    /// <summary>Every dotted path present in the emitted payload, with array indices collapsed to <c>[]</c>.</summary>
    private static IEnumerable<(string Path, JsonElement Value)> Walk(JsonElement e, string prefix = "")
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}";
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        foreach (var inner in Walk(p.Value, path)) yield return inner;
                    }
                    else yield return (path, p.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var inner in Walk(item, prefix + "[]")) yield return inner;
                break;
        }
    }

    private static int ScaleOf(JsonElement n)
    {
        var s = n.GetRawText();
        var dot = s.IndexOf('.');
        return dot < 0 ? 0 : s.Length - dot - 1;
    }

    private static JsonElement Item0(Company c, Voucher v) => Inv01(c, v).GetProperty("ItemList")[0];

    // ================================================================ 1 — no invented key survives

    /// <summary>
    /// <b>Every key the payload emits must be a field of the official INV-01 schema.</b> This is the guard that the
    /// invented snake-case names could never have passed: <c>ass_val_paisa</c>, <c>cgst_val_paisa</c>,
    /// <c>sgst_val_paisa</c>, <c>igst_val_paisa</c>, <c>ces_val_paisa</c>, <c>tot_inv_val_paisa</c>,
    /// <c>qty_millis</c>, <c>unit_price_paisa</c>, <c>tot_amt_paisa</c>, <c>ass_amt_paisa</c>,
    /// <c>cgst_amt_paisa</c>, <c>sgst_amt_paisa</c>, <c>igst_amt_paisa</c>, <c>ces_amt_paisa</c>,
    /// <c>ces_nonadvl_amt_paisa</c> — fifteen of them — plus the non-schema <c>schemaStatus</c> flag the writer used
    /// to append to a statutory payload.
    /// <para>It runs over <b>every</b> fixture voucher, not one: a guard that only ever sees the conformant shape
    /// certifies nothing about the others.</para>
    /// </summary>
    [Fact]
    public void Every_emitted_key_is_an_official_INV_01_field()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            var unknown = Walk(Inv01(f.Company, v)).Select(x => x.Path)
                .Where(p => !NicInv01Schema.Paths.Contains(p))
                .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

            Assert.True(unknown.Count == 0,
                $"{name}: the INV-01 payload emits keys that are not fields of the official NIC schema " +
                "(einvoice1.gst.gov.in/Documents/EInvoice_Schema.xlsx, sheet \"Schema\"): " + string.Join(", ", unknown));
        }
    }

    /// <summary>The root objects the schema's own <c>required</c> list names must all be present.</summary>
    [Fact]
    public void The_root_carries_every_object_the_schema_marks_required()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            var root = Inv01(f.Company, v);
            foreach (var member in NicInv01Schema.RootRequired)
                Assert.True(root.TryGetProperty(member, out _), $"{name}: root is missing the mandatory member {member}");
        }
    }

    /// <summary>Each emitted field must carry the JSON <b>type</b> the schema declares. This is what pins
    /// <c>SlNo</c> as a <c>string</c> (the schema says <c>type: "string", maxLength: 6</c>; we emitted a number) and
    /// every money field as a <c>number</c>.</summary>
    [Fact]
    public void Every_emitted_field_carries_the_declared_JSON_type_and_scale()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            foreach (var (path, value) in Walk(Inv01(f.Company, v)))
            {
                if (NicInv01Schema.Find(path) is not { } spec) continue;   // the "no invented key" test owns unknowns

                var actual = value.ValueKind switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.Number => "number",
                    JsonValueKind.Null => "null",
                    _ => value.ValueKind.ToString().ToLowerInvariant(),
                };
                Assert.True(spec.Type == actual,
                    $"{name}/{path}: schema declares type \"{spec.Type}\" but the payload emitted {actual} " +
                    $"({value.GetRawText()})");

                if (actual == "number")
                    Assert.True(ScaleOf(value) <= spec.Scale,
                        $"{name}/{path}: schema permits at most {spec.Scale} decimal place(s) but the payload emitted " +
                        $"{value.GetRawText()}");
            }
        }
    }

    /// <summary>
    /// <b>Every mandatory scalar is present AND non-null</b>, except the seven the domain model provably cannot
    /// source (pinned separately). A JSON <c>null</c> is not a string: the schema declares
    /// <c>BuyerDtls.Gstin</c> <c>type: "string", minLength: 3</c>, so a null fails validation before a figure is
    /// read. An earlier revision of this guard skipped every null outright, which exempted exactly the defect it
    /// existed to catch.
    /// </summary>
    [Fact]
    public void Every_mandatory_scalar_is_present_and_non_null()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            var root = Inv01(f.Company, v);
            // A multi-item payload yields the SAME collapsed path once per item, so this is a lookup, not a
            // dictionary — and every occurrence is checked, not just the first.
            var present = Walk(root).ToLookup(x => x.Path, x => x.Value, StringComparer.Ordinal);
            var hasItems = root.GetProperty("ItemList").GetArrayLength() > 0;

            foreach (var path in NicInv01Schema.RequiredScalars)
            {
                if (NicInv01Schema.KnownOmittedMandatory.Contains(path)) continue;
                if (path.StartsWith("ItemList[]", StringComparison.Ordinal) && !hasItems) continue;

                Assert.True(present.Contains(path), $"{name}: mandatory scalar {path} is absent from the payload");
                Assert.All(present[path], value => Assert.True(value.ValueKind != JsonValueKind.Null,
                    $"{name}: mandatory scalar {path} is emitted as JSON null — the schema declares it a string/number, " +
                    "so the request is rejected on schema validation before a figure is read"));
            }
        }
    }

    // ================================================================ 2 — the units

    /// <summary>
    /// <b>Money is a rupee decimal, not integer paisa.</b> NIC types every amount <c>Number(_,2)</c>; the writer
    /// emitted integer paisa, overstating every declared value 100×. The fixture's taxable value is ₹1,23,456.79, so
    /// the correct <c>AssVal</c> is <c>123456.79</c> — the pre-fix payload said <c>12345679</c>, which the IRP would
    /// have read as ₹1.23 crore.
    /// </summary>
    [Fact]
    public void Document_totals_are_rupee_decimals_and_foot()
    {
        var f = Build();
        var val = Inv01(f.Company, f.LedgerOnlySale).GetProperty("ValDtls");

        Assert.Equal(Taxable, val.GetProperty("AssVal").GetDecimal());

        var ass = val.GetProperty("AssVal").GetDecimal();
        var cgst = val.GetProperty("CgstVal").GetDecimal();
        var sgst = val.GetProperty("SgstVal").GetDecimal();
        var igst = val.GetProperty("IgstVal").GetDecimal();
        var ces = val.GetProperty("CesVal").GetDecimal();
        var tot = val.GetProperty("TotInvVal").GetDecimal();

        // Intra-state 18% ⇒ CGST == SGST, and each is 9% of the odd-paisa taxable value.
        Assert.Equal(cgst, sgst);
        Assert.Equal(0m, igst);
        Assert.Equal(ass + cgst + sgst + igst + ces, tot);

        // Every total is a plain rupee figure, not a 100×-inflated paisa integer.
        Assert.True(tot < 2_00_000m, $"TotInvVal {tot} looks like paisa, not rupees");
    }

    /// <summary>
    /// <b><c>GstRt</c> is a decimal percent, not basis points.</b> The schema documents it "The GST rate, represented
    /// as percentage that applies to the invoiced item" with <c>maximum: 999.999</c>, and the "Validations" sheet
    /// (Validations on Items, rule 7) settles the intra-state case: <i>"In case of intra-state transaction, the sum
    /// of SGST and CGST tax rates should be entered as GST Rate."</i> So an 18% supply files <c>18</c>. The writer
    /// filed <c>1800</c> — a rate above the schema maximum, and 100× the real one.
    /// </summary>
    [Fact]
    public void GstRt_is_the_combined_rate_as_a_decimal_percent()
    {
        var f = Build();
        var item0 = Item0(f.Company, f.LedgerOnlySale);

        Assert.Equal(18m, item0.GetProperty("GstRt").GetDecimal());
        Assert.True(item0.GetProperty("GstRt").GetDecimal() <= 999.999m, "GstRt exceeds the schema maximum 999.999");
    }

    /// <summary>The GST 2.0 de-merit 40% slab files <c>40</c>, not <c>4000</c> — and stays inside the maximum.</summary>
    [Fact]
    public void The_40_percent_slab_files_as_forty()
    {
        var c = CompanyFactory.CreateSeeded("Slab Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var b2b = Add(c, "Local Debtor", "Sundry Debtors", true);
        b2b.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinHome, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(Taxable), 4000) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(b2b.Id, new Money(Taxable + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(Taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var v = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleDate, lines, partyId: b2b.Id));

        Assert.Equal(40m, Inv01(c, v).GetProperty("ItemList")[0].GetProperty("GstRt").GetDecimal());
    }

    /// <summary>
    /// <b><c>Qty</c> is a quantity, not millis, and <c>UnitPrice</c> is rupees, not paisa.</b> The schema types them
    /// <c>Number(10,3)</c> and <c>Number(12,3)</c>. 2.5 Nos must file as <c>2.5</c> — the writer filed <c>2500</c>.
    /// The footing identity Qty × UnitPrice == TotAmt is asserted in the emitted units, which is what catches a
    /// quantity converted without its rate.
    /// </summary>
    [Fact]
    public void Item_quantity_and_unit_price_are_declared_in_NIC_units()
    {
        var f = Build();
        var item0 = Item0(f.Company, f.ItemSale);

        Assert.Equal(ItemQty, item0.GetProperty("Qty").GetDecimal());          // 2.5, not 2500
        Assert.Equal(ItemRate, item0.GetProperty("UnitPrice").GetDecimal());   // 9876.54, not 987654
        Assert.Contains(item0.GetProperty("Unit").GetString()!, NicInv01Schema.UqcSample);

        Assert.Equal(ItemTaxable, item0.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(item0.GetProperty("TotAmt").GetDecimal(), item0.GetProperty("AssAmt").GetDecimal());
    }

    /// <summary>
    /// <b>The schema's gross-amount identity holds on EVERY path, because a line that cannot state it omits the
    /// terms rather than declaring a false one.</b> "Validations", Calculation of Values rule 1:
    /// <i>"Gross Amount of Item = Quantity X Selling Unit Price"</i>, with rule 3 permitting a declared figure only
    /// "between actual calculated value/amount and calculated value/amount rounded up to next rupee".
    ///
    /// <para>The writer used to emit <c>"Qty": 0</c> beside a non-zero <c>TotAmt</c> on every ledger-only line —
    /// 0 × 123456.79 = 0 ≠ 123456.79, outside the permitted band by the whole invoice. <c>Qty</c> is
    /// <b>Optional</b> in the schema (it is absent from the <c>ItemList[]</c> <c>required</c> list, which is
    /// [SlNo, IsServc, HsnCd, UnitPrice, TotAmt, AssAmt, GstRt, TotItemVal]) and "Validations on Items" rule 5 makes
    /// it "optional for Services", so <b>omitting</b> it is schema-legal where declaring 0 is arithmetically
    /// false.</para>
    /// </summary>
    [Fact]
    public void Every_item_either_states_the_gross_amount_identity_or_omits_the_quantity()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            foreach (var item in Inv01(f.Company, v).GetProperty("ItemList").EnumerateArray())
            {
                var totAmt = item.GetProperty("TotAmt").GetDecimal();
                if (!item.TryGetProperty("Qty", out var qty))
                {
                    // No quantity declared ⇒ the identity is not computable, so it cannot be violated. The Unit
                    // must be absent too: a UQC without a quantity states nothing.
                    Assert.False(item.TryGetProperty("Unit", out _),
                        $"{name}: an item omits Qty but still declares a Unit — a UQC with no quantity is not a " +
                        "declaration the IRP can read");
                    continue;
                }

                Assert.Equal(totAmt, qty.GetDecimal() * item.GetProperty("UnitPrice").GetDecimal());
            }
        }
    }

    // ================================================================ 3 — the date + document number

    /// <summary>
    /// <b><c>DocDtls.Dt</c> is DD/MM/YYYY.</b> The schema's own pattern is
    /// <c>[0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9]</c> and the published Sample JSON emits <c>"Dt":"11/08/2020"</c>.
    /// The writer emitted ISO <c>2025-04-10</c>, which fails that pattern outright.
    /// </summary>
    [Fact]
    public void Document_date_matches_the_official_pattern()
    {
        var f = Build();
        var dt = Inv01(f.Company, f.LedgerOnlySale).GetProperty("DocDtls").GetProperty("Dt").GetString()!;

        Assert.Matches(@"^[0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9]$", dt);
        Assert.Equal(SaleDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), dt);
    }

    /// <summary>The document number the DEFAULT (unconfigured) voucher type renders satisfies the schema's pattern.
    /// This is the shape the app ships with; the configurations that do NOT satisfy it are pinned below.</summary>
    [Fact]
    public void Document_number_matches_the_official_pattern()
    {
        var f = Build();
        var no = Inv01(f.Company, f.LedgerOnlySale).GetProperty("DocDtls").GetProperty("No").GetString()!;
        Assert.Matches(@"^([A-Z1-9]{1}[A-Z0-9/-]{0,15})$", no);
    }

    /// <summary>
    /// <b>PINNED — <c>DocDtls.No</c> is emitted verbatim and is NOT guarded against the schema's pattern, so four
    /// ordinary numbering configurations file a document number the IRP rejects.</b> The schema declares
    /// <c>maxLength: 16, pattern: "^([A-Z1-9]{1}[A-Z0-9/-]{0,15})$"</c> and "Validations" rule 6 is explicit:
    /// <i>"Document number should not be starting with 0, / and -. Also, alphabets in document number should not
    /// have alphabets in lower cases. If so, then request is rejected."</i>
    ///
    /// <para>The previous single-case test asserted the pattern on a fixture that renders <c>"1"</c> — a
    /// configuration that <b>cannot</b> violate it — so it certified conformance the emitter does not have. This
    /// theory drives the four configurations that do violate it and pins each, so the gap is visible and a future
    /// guard has a red test to turn green.</para>
    ///
    /// <para><b>UNVERIFIED / OPEN — and it contradicts a claim shipped in the code.</b>
    /// <c>EInvoiceService.DocumentNumberOf</c> carries an uncited assertion that "IRP has been case-insensitive
    /// since 01-Jun-2025", used to justify emitting a lowercase prefix as typed. The official "Validations" sheet
    /// retrieved 2026-08-14 says the opposite in rule 6. Whether to uppercase (breaking the "paper == IRP == e-Way
    /// == GSTR-1 == Day Book, one identical string" contract) or to reject the configuration at entry is a
    /// user-facing decision, not this slice's to take.</para>
    /// </summary>
    [Theory]
    [InlineData(4, true, "", NumberingMethod.Automatic, "a zero-padded width renders a LEADING ZERO (0001)")]
    [InlineData(4, false, "", NumberingMethod.Automatic, "a space-padded width renders SPACES, outside [A-Z0-9/-]")]
    [InlineData(0, false, "inv/", NumberingMethod.Automatic, "a lowercase prefix renders lowercase alphabets")]
    [InlineData(0, false, "", NumberingMethod.None, "an unnumbered voucher falls back to a 32-char lowercase GUID")]
    public void PINNED_the_document_number_is_not_guarded_against_the_schema_pattern(
        int width, bool prefillWithZero, string prefix, NumberingMethod numbering, string why)
    {
        var f = Build();
        var type = f.Company.FindVoucherType(f.LedgerOnlySale.TypeId)!;
        type.NumberWidth = width;
        type.PrefillWithZero = prefillWithZero;
        type.Numbering = numbering;
        if (prefix.Length > 0)
            type.SetAffixes(
                new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, prefix) },
                Array.Empty<VoucherNumberAffix>());

        var no = Inv01(f.Company, f.LedgerOnlySale).GetProperty("DocDtls").GetProperty("No").GetString()!;

        Assert.DoesNotMatch(@"^([A-Z1-9]{1}[A-Z0-9/-]{0,15})$", no);
        Assert.False(string.IsNullOrEmpty(why));   // the reason is documentation, carried into the failure message
    }

    // ================================================================ 4 — the mandatory scalars

    /// <summary>The schema's <c>required</c> lists name <c>TranDtls.TaxSch</c>, <c>BuyerDtls.Pos</c>,
    /// <c>ItemList[].IsServc</c> and <c>ItemList[].TotItemVal</c>; none of them were emitted at all.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_mandatory_scalars_the_writer_omitted_are_present(bool withInventory)
    {
        var f = Build();
        var root = Inv01(f.Company, withInventory ? f.ItemSale : f.LedgerOnlySale);

        Assert.Equal("GST", root.GetProperty("TranDtls").GetProperty("TaxSch").GetString());
        // POS drives the intra/inter determination (Validations, rule 24) — for this intra-state sale it is 27.
        Assert.Equal("27", root.GetProperty("BuyerDtls").GetProperty("Pos").GetString());
        Assert.Equal("27", root.GetProperty("SellerDtls").GetProperty("Stcd").GetString());
        Assert.Equal("27", root.GetProperty("BuyerDtls").GetProperty("Stcd").GetString());

        foreach (var item in root.GetProperty("ItemList").EnumerateArray())
        {
            Assert.Contains(item.GetProperty("IsServc").GetString(), new[] { "Y", "N" });
            _ = item.GetProperty("TotItemVal").GetDecimal();
        }
    }

    /// <summary>
    /// <b><c>SlNo</c> is the item's own 1-based index, and the values are distinct.</b> "Validations on Items"
    /// rule 1: <i>"Serial number of the item is verified for duplicate values."</i>
    ///
    /// <para>The previous assertion sat inside the per-item loop but indexed <c>ItemList[0]</c>, so it re-asserted
    /// the FIRST item N times; and no fixture emitted more than one item, so mutating the emitter to a constant
    /// <c>"1"</c> left the whole suite green. <see cref="Fx.TwoItemSale"/> exists to make that mutation red.</para>
    /// </summary>
    [Fact]
    public void Item_serial_numbers_are_the_one_based_index_and_are_distinct()
    {
        var f = Build();
        var items = Inv01(f.Company, f.TwoItemSale).GetProperty("ItemList").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var slNos = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var slNo = items[i].GetProperty("SlNo").GetString()!;
            Assert.Equal((i + 1).ToString(CultureInfo.InvariantCulture), slNo);
            slNos.Add(slNo);
        }
        Assert.Equal(slNos.Count, slNos.Distinct(StringComparer.Ordinal).Count());

        // …and across every fixture, whatever the item count.
        foreach (var (name, v) in AllVouchers(f))
        {
            var all = Inv01(f.Company, v).GetProperty("ItemList").EnumerateArray()
                .Select(i => i.GetProperty("SlNo").GetString()!).ToList();
            Assert.True(all.Count == all.Distinct(StringComparer.Ordinal).Count(),
                $"{name}: duplicate SlNo values — the IRP rejects the request (Validations on Items, rule 1)");
        }
    }

    /// <summary>
    /// <b><c>IsServc</c> declares the NATURE OF THE SUPPLY, not which table the line came out of.</b> The schema
    /// makes the flag mandatory ("Specify whether the supply is service or not. Specify Y-for Service") and
    /// "Validations on Items" rule 3 binds it to the code: <i>"If Is_Service is selected, then the HSN codes must
    /// belong to services."</i>
    ///
    /// <para>The writer used to hard-code <c>isService: true</c> on <b>both</b> ledger-only branches — i.e. it
    /// answered "does this voucher carry stock lines?", not "is this a service?". Every accounts-only sale of
    /// GOODS therefore declared itself a service. <see cref="Fx.GoodsLedgerSale"/> is exactly that shape: a
    /// ledger-only sale through an income ledger declaring <c>SupplyType = Goods</c> and the 8-digit GOODS HSN
    /// 84713010. It must file <c>"N"</c>; filing <c>"Y"</c> misdeclares the nature of supply, and does so beside a
    /// goods HSN, which rule 3 rejects.</para>
    /// </summary>
    [Fact]
    public void IsServc_is_read_from_the_declared_supply_type_not_from_the_item_source()
    {
        var f = Build();

        // A stock line is goods.
        Assert.Equal("N", Item0(f.Company, f.ItemSale).GetProperty("IsServc").GetString());

        // A genuine accounting (service) invoice through a SAC-bearing Services ledger is a service …
        var svc = Item0(f.Company, f.ServiceInvoice);
        Assert.Equal("Y", svc.GetProperty("IsServc").GetString());
        Assert.Equal("998311", svc.GetProperty("HsnCd").GetString());

        // … and a ledger-only sale through a GOODS-declaring ledger is NOT, even though it carries no stock line.
        var goods = Item0(f.Company, f.GoodsLedgerSale);
        Assert.Equal("N", goods.GetProperty("IsServc").GetString());
        Assert.Equal("84713010", goods.GetProperty("HsnCd").GetString());

        // A plain As-Voucher sale whose income ledger declares no GST block at all is not an accounting invoice,
        // so it is not a service either — the synthetic fallback branch must not assert one.
        Assert.Equal("N", Item0(f.Company, f.LedgerOnlySale).GetProperty("IsServc").GetString());
    }

    // ================================================================ 5 — place of supply drives intra vs inter

    /// <summary>
    /// <b><c>BuyerDtls.Pos</c> tracks the RECIPIENT, and the head split agrees with it.</b> "Validations" rule 24:
    /// <i>"The state code of the Supplier GSTIN and POS will decide whether the supply type is Interstate or
    /// Intrastate. That is, if the State code of Supplier and POS is same, then it is intra-state, otherwise it is
    /// inter-state."</i>
    ///
    /// <para>Every earlier assertion on <c>Pos</c> ran on a fixture whose buyer state equalled the seller's, so
    /// wiring <c>Pos</c> to <c>gst.HomeStateCode</c> instead of the party's state code was invisible to the whole
    /// repository. With the mutation shipped, a Maharashtra (27) seller invoicing a Gujarat (24) buyer would file
    /// POS 27 beside IGST 22,222.22 — an INTRA-state supply carrying only IGST, which rules 7/8 contradict.</para>
    /// </summary>
    [Fact]
    public void Pos_tracks_the_recipient_state_and_the_head_split_agrees_with_it()
    {
        var f = Build();
        var root = Inv01(f.Company, f.InterStateSale);
        var buyer = root.GetProperty("BuyerDtls");
        var val = root.GetProperty("ValDtls");

        Assert.Equal("24", buyer.GetProperty("Pos").GetString());
        Assert.Equal("24", buyer.GetProperty("Stcd").GetString());
        Assert.Equal("27", root.GetProperty("SellerDtls").GetProperty("Stcd").GetString());

        // Rule 8: "In case of inter-state transaction, the IGST tax rate and value has to be passed."
        Assert.True(val.GetProperty("IgstVal").GetDecimal() > 0m, "an inter-state supply must carry IGST");
        Assert.Equal(0m, val.GetProperty("CgstVal").GetDecimal());
        Assert.Equal(0m, val.GetProperty("SgstVal").GetDecimal());

        // …and the intra-state twin still splits CGST/SGST against POS == the seller's own state.
        var intra = Inv01(f.Company, f.LedgerOnlySale);
        Assert.Equal("27", intra.GetProperty("BuyerDtls").GetProperty("Pos").GetString());
        Assert.Equal(0m, intra.GetProperty("ValDtls").GetProperty("IgstVal").GetDecimal());
        Assert.True(intra.GetProperty("ValDtls").GetProperty("CgstVal").GetDecimal() > 0m);
    }

    // ================================================================ 6 — exports

    /// <summary>
    /// <b>A direct export files <c>BuyerDtls.Gstin = "URP"</c>, not JSON <c>null</c>.</b> The schema declares
    /// <c>Gstin</c> <c>type: "string", minLength: 3, maxLength: 15,
    /// pattern: "^(([0-9]{2}[0-9A-Z]{13})|URP)$"</c> with the description <i>"GSTIN of buyer , URP if exporting"</i>,
    /// and lists it in <c>BuyerDtls</c>'s <c>required</c> array. "Validations" rule 15 states it outright:
    /// <i>"In case of transaction of direct export, recipient GSTIN has to be URP and state code has to be 96, POS
    /// should be 96."</i>
    ///
    /// <para>The writer emitted <c>partyGst?.Gstin</c> — <c>null</c> for an overseas recipient, which carries no
    /// GSTIN — and <c>JsonIgnoreCondition.Never</c> wrote it out. <c>"Gstin": null</c> is neither a string nor
    /// <c>URP</c>, so every export this company filed was rejected on schema validation before a single figure was
    /// read. <c>URP</c> is the schema's own literal, so emitting it is transcription, not invention.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_direct_export_files_URP_and_the_overseas_place_of_supply(bool onPaymentOfTax)
    {
        var f = Build();
        var buyer = Inv01(f.Company, onPaymentOfTax ? f.ExportWithPayment : f.ExportWithoutPayment)
            .GetProperty("BuyerDtls");

        Assert.Equal("URP", buyer.GetProperty("Gstin").GetString());
        Assert.Equal("96", buyer.GetProperty("Pos").GetString());
        Assert.Equal("96", buyer.GetProperty("Stcd").GetString());
    }

    /// <summary>
    /// <b>An export declares <c>EXPWP</c> or <c>EXPWOP</c> according to whether IGST was actually charged.</b> The
    /// schema's <c>SupTyp</c> enum is [B2B, SEZWP, SEZWOP, EXPWP, EXPWOP, DEXP], described
    /// <i>"EXPWP - Export with Payment, EXPWOP - Export without payment"</i>, and "Validations on Items" rule 9 —
    /// <i>"In case of export transaction, IGST tax rate and value has to be passed"</i> — is what makes the pairing
    /// checkable.
    ///
    /// <para>The writer mapped <c>EInvoiceSupplyCategory.Export</c> unconditionally to <c>EXPWP</c>, so a
    /// zero-rated LUT/bond export declared that tax had been paid on it — the branch that decides refund
    /// eligibility. The string is a valid enum member, so no type guard fires: only the pairing catches it.</para>
    /// </summary>
    [Fact]
    public void An_export_declares_EXPWP_only_when_IGST_was_actually_charged()
    {
        var f = Build();

        var withPayment = Inv01(f.Company, f.ExportWithPayment);
        Assert.Equal("EXPWP", withPayment.GetProperty("TranDtls").GetProperty("SupTyp").GetString());
        Assert.True(withPayment.GetProperty("ValDtls").GetProperty("IgstVal").GetDecimal() > 0m);

        var underLut = Inv01(f.Company, f.ExportWithoutPayment);
        Assert.Equal("EXPWOP", underLut.GetProperty("TranDtls").GetProperty("SupTyp").GetString());
        Assert.Equal(0m, underLut.GetProperty("ValDtls").GetProperty("IgstVal").GetDecimal());

        // The pairing, stated as the invariant: SupTyp == EXPWP  <=>  IgstVal > 0.
        foreach (var (name, v) in AllVouchers(f))
        {
            var root = Inv01(f.Company, v);
            var supTyp = root.GetProperty("TranDtls").GetProperty("SupTyp").GetString();
            if (supTyp is not ("EXPWP" or "EXPWOP")) continue;
            var igst = root.GetProperty("ValDtls").GetProperty("IgstVal").GetDecimal();
            Assert.True((supTyp == "EXPWP") == (igst > 0m),
                $"{name}: declared {supTyp} with IgstVal {igst} — the two must agree");
        }

        // A domestic B2B supply is untouched by the export branch.
        Assert.Equal("B2B", Inv01(f.Company, f.LedgerOnlySale).GetProperty("TranDtls").GetProperty("SupTyp").GetString());
    }

    /// <summary>
    /// <b>PINNED — a zero-rated (LUT/bond) export emits an EMPTY <c>ItemList</c>.</b> "Validations on Items" rule 11
    /// requires "a minimum of 1 item". The writer projects items off the POSTED tax lines (ER-9), and a zero-rated
    /// export posts none, so no rate group exists to expand into an item.
    ///
    /// <para><b>UNVERIFIED / OPEN:</b> a real IRP submission would be rejected for this. Closing it means
    /// projecting the item line off the sales LEG rather than the tax line for the zero-tax export case, which
    /// changes what "read the posted figures" means on that path — a separate slice, and a decision for the
    /// user.</para>
    /// </summary>
    [Fact]
    public void PINNED_a_zero_rated_export_emits_no_item_lines_at_all()
    {
        var f = Build();
        var root = Inv01(f.Company, f.ExportWithoutPayment);
        Assert.Equal(0, root.GetProperty("ItemList").GetArrayLength());
        Assert.Equal(0m, root.GetProperty("ValDtls").GetProperty("AssVal").GetDecimal());
    }

    // ================================================================ 7 — compensation cess

    /// <summary>
    /// <b>An AD-VALOREM compensation cess files its real rate in <c>CesRt</c>, and <c>CesAmt</c> reproduces it.</b>
    /// "Validations", Calculation of Values rule 1: <i>"Cess Value of Item = Taxable Value of Item X Cess
    /// Rate"</i>.
    ///
    /// <para>The writer hard-coded <c>CesRt = 0m</c> while loading the full posted cess into <c>CesAmt</c>. Before
    /// this slice both were invented keys, so the contradiction was inert; the moment they became the real NIC
    /// fields the payload began declaring a ₹14,814.81 ad-valorem cess levied at 0%, which the IRP's own arithmetic
    /// check rejects. The rate is a pure read of the posted cess line's own
    /// <c>GstLineTax.RateBasisPoints</c> (ER-9 preserved — nothing is recomputed).</para>
    ///
    /// <para>The identity is asserted against the <b>posted</b> figure rather than the schema's raw product because
    /// the engine rounds the cess to the paisa once, at posting: 1,23,456.79 × 12% = 14,814.8148, posted as
    /// 14,814.81. That is 0.0048 BELOW the schema's band, whose floor is the exact product — a sub-paisa artefact
    /// of paisa-exact money, noted here rather than papered over.</para>
    /// </summary>
    [Fact]
    public void An_ad_valorem_cess_declares_its_rate_and_the_amount_reproduces_it()
    {
        var f = Build();
        var root = Inv01(f.Company, f.CessSale);
        var item0 = root.GetProperty("ItemList")[0];

        var cesRt = item0.GetProperty("CesRt").GetDecimal();
        var cesAmt = item0.GetProperty("CesAmt").GetDecimal();
        var ass = item0.GetProperty("AssAmt").GetDecimal();

        Assert.Equal(CessBasisPoints / 100m, cesRt);          // 12, not 0 and not 1200
        Assert.True(cesAmt > 0m, "the cess fixture must actually post cess, or the assertion is vacuous");
        Assert.Equal(Math.Round(ass * cesRt / 100m, 2, MidpointRounding.AwayFromZero), cesAmt);

        // An ad-valorem cess is not a non-advalorem one.
        Assert.Equal(0m, item0.GetProperty("CesNonAdvlAmt").GetDecimal());

        // Document total: "Total Cess Value = Cess Value of all Items + Non-Advol Cess Value of all Items".
        var items = root.GetProperty("ItemList").EnumerateArray().ToList();
        Assert.Equal(
            root.GetProperty("ValDtls").GetProperty("CesVal").GetDecimal(),
            items.Sum(i => i.GetProperty("CesAmt").GetDecimal())
                + items.Sum(i => i.GetProperty("CesNonAdvlAmt").GetDecimal()));
        Assert.True(root.GetProperty("ValDtls").GetProperty("CesVal").GetDecimal() > 0m);
    }

    /// <summary>
    /// <b>A per-unit (SPECIFIC) cess is NOT ad-valorem, so it files in <c>CesNonAdvlAmt</c>.</b> The schema
    /// describes <c>CesAmt</c> as <i>"Cess Amount(Advalorem) on basis of rate and quantity of item"</i> and gives
    /// <c>CesNonAdvlAmt</c> its own field. The writer loaded the whole posted cess into <c>CesAmt</c> and
    /// hard-coded <c>CesNonAdvlAmt = 0m</c>, so ₹777.00 of per-unit cess was declared as an ad-valorem levy — which
    /// then fails "Cess Value of Item = Taxable Value of Item X Cess Rate" for any non-zero rate.
    ///
    /// <para>The discriminant is a pure read: <c>GstService</c> stamps the posted cess line's
    /// <c>RateBasisPoints</c> with the group's ad-valorem rate, and <b>0</b> for Specific / RSP-factor (its own
    /// comment: "representative ad-valorem bp (0 for specific/RSP)"). So <c>CesRt = 0</c> here is TRUE, not a
    /// placeholder — there is no ad-valorem rate — and the identity holds trivially because <c>CesAmt</c> is 0.</para>
    /// </summary>
    [Fact]
    public void A_per_unit_cess_files_as_non_ad_valorem()
    {
        var f = Build();
        var root = Inv01(f.Company, f.SpecificCessSale);
        var item0 = root.GetProperty("ItemList")[0];

        Assert.Equal(SpecificCessPerUnit * SpecificCessQuantity, item0.GetProperty("CesNonAdvlAmt").GetDecimal());
        Assert.Equal(0m, item0.GetProperty("CesAmt").GetDecimal());
        Assert.Equal(0m, item0.GetProperty("CesRt").GetDecimal());

        Assert.Equal(
            SpecificCessPerUnit * SpecificCessQuantity,
            root.GetProperty("ValDtls").GetProperty("CesVal").GetDecimal());
    }

    /// <summary>A voucher bearing no cess declares a zero rate and zero amounts — the case where the old hard-coded
    /// <c>CesRt = 0</c> happened to be right, kept so the correction cannot regress it.</summary>
    [Fact]
    public void A_voucher_with_no_cess_declares_zero_rate_and_zero_amounts()
    {
        var f = Build();
        var item0 = Item0(f.Company, f.LedgerOnlySale);
        Assert.Equal(0m, item0.GetProperty("CesRt").GetDecimal());
        Assert.Equal(0m, item0.GetProperty("CesAmt").GetDecimal());
        Assert.Equal(0m, item0.GetProperty("CesNonAdvlAmt").GetDecimal());
    }

    // ================================================================ 8 — the footing identities

    /// <summary>
    /// <c>TotItemVal</c> is defined by the schema as the item's assessable amount plus its every tax head, and the
    /// "Validations" sheet defines the document total as the sum of those. Both identities are asserted in the
    /// emitted rupee units so a units regression breaks them — over <b>every</b> fixture, so a path that foots only
    /// on the simple shape is caught.
    /// </summary>
    [Fact]
    public void Item_values_foot_to_the_document_totals()
    {
        var f = Build();
        foreach (var (name, v) in AllVouchers(f))
        {
            var root = Inv01(f.Company, v);
            var items = root.GetProperty("ItemList").EnumerateArray().ToList();
            var val = root.GetProperty("ValDtls");

            decimal Sum(string member) => items.Sum(i => i.GetProperty(member).GetDecimal());

            Assert.Equal(val.GetProperty("AssVal").GetDecimal(), Sum("AssAmt"));
            Assert.Equal(val.GetProperty("CgstVal").GetDecimal(), Sum("CgstAmt"));
            Assert.Equal(val.GetProperty("SgstVal").GetDecimal(), Sum("SgstAmt"));
            Assert.Equal(val.GetProperty("IgstVal").GetDecimal(), Sum("IgstAmt"));
            Assert.Equal(val.GetProperty("CesVal").GetDecimal(), Sum("CesAmt") + Sum("CesNonAdvlAmt"));
            Assert.Equal(val.GetProperty("TotInvVal").GetDecimal(), Sum("TotItemVal"));

            foreach (var i in items)
            {
                var expected = i.GetProperty("AssAmt").GetDecimal()
                    + i.GetProperty("IgstAmt").GetDecimal()
                    + i.GetProperty("CgstAmt").GetDecimal()
                    + i.GetProperty("SgstAmt").GetDecimal()
                    + i.GetProperty("CesAmt").GetDecimal()
                    + i.GetProperty("CesNonAdvlAmt").GetDecimal();
                Assert.Equal(expected, i.GetProperty("TotItemVal").GetDecimal());
            }

            Assert.False(string.IsNullOrEmpty(name));
        }
    }

    // ================================================================ 9 — determinism survives the correction

    /// <summary>The writer's determinism contract (no clock, no RNG, fixed order, UTF-8 no BOM) and the de-brand
    /// guard must survive the rename.</summary>
    [Fact]
    public void The_corrected_payload_is_still_deterministic_and_de_branded()
    {
        var f = Build();
        foreach (var (_, v) in AllVouchers(f))
        {
            var first = EInvoiceJson.BuildInv01(f.Company, v);
            Assert.Equal(first, EInvoiceJson.BuildInv01(f.Company, v));
            var text = Encoding.UTF8.GetString(first);
            Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(0xEF, first[0]);   // no UTF-8 BOM
        }
    }

    // ================================================================ 10 — the two statutory domains stay separate

    /// <summary>
    /// <b>The e-Way payload is NOT touched by this correction.</b> <c>EWayBillJson</c> was fixed by W0-8 against a
    /// different official code set (the NIC e-Way master-codes list) and keeps its own field names and its own
    /// integer-paisa encoding. The two payloads overlap only on the string "INV"; merging them would re-introduce
    /// the one-rule-many-copies defect this project has repeatedly found. This test pins the separation from the
    /// e-invoice side: the INV-01 must NOT acquire e-Way keys, and vice versa.
    /// </summary>
    [Fact]
    public void The_INV_01_correction_does_not_leak_into_the_e_Way_payload()
    {
        var f = Build();
        var inv01 = Walk(Inv01(f.Company, f.ItemSale)).Select(x => x.Path).ToHashSet(StringComparer.Ordinal);

        // The e-Way writer's own key vocabulary must never appear in an INV-01.
        foreach (var ewayOnly in new[] { "supplyType", "subSupplyType", "docType", "fromGstin", "toGstin" })
            Assert.DoesNotContain(ewayOnly, inv01.Select(p => p.Split('.').Last()));

        // …and the INV-01's corrected NIC names are all schema fields.
        Assert.All(inv01, p => Assert.Contains(p, NicInv01Schema.Paths));
    }

    // ================================================================ 11 — what is NOT yet conformant, pinned

    /// <summary>
    /// <b>PINNED — the party-detail block is incomplete, and it cannot be completed without inventing data.</b>
    /// The schema's <c>required</c> lists are <c>SellerDtls: [Gstin, LglNm, Addr1, Loc, Pin, Stcd]</c> and
    /// <c>BuyerDtls: [Gstin, LglNm, Pos, Addr1, Loc, Stcd]</c>. This application's domain model carries no legal
    /// name and no address for either party — <c>GstConfig</c> holds only <c>Gstin</c> / <c>HomeStateCode</c>, and
    /// <c>PartyGstDetails</c> only <c>Gstin</c> / <c>StateCode</c> — so <c>LglNm</c>, <c>Addr1</c>, <c>Loc</c> and
    /// <c>Pin</c> have no source. They are deliberately NOT emitted rather than fabricated: an invented address is
    /// the same defect class this slice exists to remove.
    ///
    /// <para><b>UNVERIFIED / OPEN:</b> a real IRP submission would be rejected for these. Closing the gap needs
    /// company- and party-address master data, which is a separate slice.</para>
    /// </summary>
    [Fact]
    public void PINNED_the_party_address_fields_are_absent_because_the_domain_model_has_no_source_for_them()
    {
        var f = Build();
        var root = Inv01(f.Company, f.LedgerOnlySale);

        foreach (var (obj, missing) in new[]
        {
            ("SellerDtls", new[] { "LglNm", "Addr1", "Loc", "Pin" }),
            ("BuyerDtls", new[] { "LglNm", "Addr1", "Loc" }),
        })
        {
            var o = root.GetProperty(obj);
            foreach (var member in missing)
                Assert.False(o.TryGetProperty(member, out _),
                    $"{obj}.{member} is now emitted — if a real source was added, update this pin.");
        }
    }

    /// <summary>
    /// <b>PINNED — <c>HsnCd</c> can still be empty.</b> The schema sets <c>minLength: 4</c> and the "Validations"
    /// sheet requires "at least 4 digits". A ledger-only sale whose income ledger declares no SAC/HSN emits
    /// <c>""</c>. That is a real non-conformance, kept visible here rather than papered over with a fabricated
    /// code.
    /// </summary>
    [Fact]
    public void PINNED_an_income_ledger_with_no_declared_HSN_or_SAC_still_emits_an_empty_HsnCd()
    {
        var f = Build();
        Assert.Equal("", Item0(f.Company, f.LedgerOnlySale).GetProperty("HsnCd").GetString());
    }

    /// <summary>
    /// <b>PINNED — a GOODS line carrying no quantity omits <c>Qty</c>/<c>Unit</c>, which "Validations on Items"
    /// rule 5 makes mandatory for goods</b> (<i>"Quantity and Unit Quantity Code are mandatory for Goods and
    /// optional for Services"</i>). A ledger-only sale of goods has no quantity dimension anywhere in the domain
    /// model — the accounts-only shape records value, not units — so the fields are OMITTED rather than filled with
    /// a fabricated <c>1</c> or an arithmetically false <c>0</c>.
    ///
    /// <para>This is the same "omit rather than invent" doctrine the party-address pin records, and it is strictly
    /// better than the previous behaviour: <c>"Qty": 0</c> beside a non-zero <c>TotAmt</c> was a declaration the
    /// IRP can read and disprove, whereas an absent optional field is merely incomplete.</para>
    ///
    /// <para><b>UNVERIFIED / OPEN:</b> whether the IRP rejects a goods line with no <c>Qty</c> is not stated in the
    /// "Validations" sheet beyond rule 5's "mandatory".</para>
    /// </summary>
    [Fact]
    public void PINNED_a_ledger_only_goods_line_omits_the_quantity_rule_5_makes_mandatory()
    {
        var f = Build();
        var goods = Item0(f.Company, f.GoodsLedgerSale);

        Assert.Equal("N", goods.GetProperty("IsServc").GetString());     // it IS goods …
        Assert.False(goods.TryGetProperty("Qty", out _));                // … and it declares no quantity.
        Assert.False(goods.TryGetProperty("Unit", out _));
    }
}
