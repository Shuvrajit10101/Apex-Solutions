using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io;

/// <summary>
/// Deterministic <b>offline-JSON</b> writer for the NIC <b>INV-01</b> e-invoice request (Phase 9 slice 4a; RQ-5; ER-5,
/// ER-9, ER-10, ER-11). A pure, framework-agnostic emitter following the determinism of <see cref="GstReturnJson"/> /
/// <see cref="FvuWriter"/>: <see cref="System.Text.Json"/> only, culture-invariant, fixed property order, <b>no clock /
/// no RNG</b>, money carried internally as integer paisa (<see cref="MoneyCodec.ToPaisa"/>, ER-10) and emitted in the
/// <b>schema's rupee decimal</b> — see the units note below — UTF-8 no BOM,
/// de-branded (ER-11). It builds the <b>request</b> payload the IRP turns into an IRN — it carries <b>no IRN and no
/// signed QR</b> (those only ever arrive inbound, ER-5). Values are read off the <b>posted</b> tax lines
/// (<see cref="GstLineTax"/>), never recomputed (ER-9).
/// <para>
/// <b>R7 — the field names, types and units are the official NIC ones.</b> Source:
/// <c>https://einvoice1.gst.gov.in/Documents/EInvoice_Schema.xlsx</c>, the e-invoice schema workbook published on the
/// national e-invoice portal (retrieved 2026-08-14 by direct HTTPS GET, HTTP 200, 198,376 bytes). Its <b>"Schema"</b>
/// sheet is the normative JSON-Schema draft-07 document (types, lengths, enums, <c>required</c>), its <b>"E-Invoice
/// Attributes"</b> sheet the field table, its <b>"Validations"</b> sheet the arithmetic and rate rules.
/// <see cref="Apex.Ledger.Tests"/>' <c>NicInv01Schema</c> transcribes that table and asserts every emitted key against
/// it, so an invented name cannot reappear silently.
/// <para>
/// <b>The second source corroborates PART of this, and the part is stated.</b>
/// <c>https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf</c> (same method, HTTP 200, 148,829 bytes) is the
/// <b>superseded v1.00</b> tabulation. Read with <c>pdftotext -layout</c> and searched whitespace-insensitively it
/// corroborates <c>AssVal</c>, <c>TotInvVal</c>, <c>TotAmt</c>, <c>AssAmt</c>, <c>UnitPrice</c>, <c>Qty</c>,
/// <c>HsnCd</c>, <c>CesRt</c> and <c>BuyerDtls.Stcd</c> — and <b>contains ZERO occurrences of <c>Pos</c>,
/// <c>GstRt</c>, <c>IsServc</c>, <c>LglNm</c>, <c>CesAmt</c> or <c>CesNonAdvlAmt</c></b>, spells the seller's state
/// code <c>StCd</c>, and names per-head rates <c>CgstRt</c>/<c>SgstRt</c>/<c>IgstRt</c> where the current schema has
/// the single combined <c>GstRt</c>. For those six names the xlsx is cited <b>alone</b>. An earlier revision of this
/// comment claimed unqualified corroboration; a manufactured agreement is worse than none, because it manufactures
/// confidence (this project already carries a recorded defect from citing a weak source for shipped TDS rates).
/// </para>
/// </para>
/// <para>
/// <b>Units are NIC's, not ours.</b> Money is stored internally as integer paisa but is emitted as the schema's rupee
/// <c>Number(_,2)</c>; <c>Qty</c> is a quantity (<c>Number(10,3)</c>), not millis; <c>GstRt</c> / <c>CesRt</c> are
/// decimal <b>percents</b> (<c>maximum: 999.999</c>), not basis points. <c>DocDtls.Dt</c> is <c>DD/MM/YYYY</c> per the
/// schema's own pattern <c>[0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9]</c>. Conversion happens only at this boundary, so
/// the posted figures (ER-9) are still read, never recomputed.
/// </para>
/// <para>
/// <b>Not yet conformant (pinned, not invented).</b> Each of these is covered by a <c>PINNED_…</c> test so it stays
/// visible instead of reading as conformant:
/// <list type="bullet">
/// <item>the schema makes <c>SellerDtls.LglNm/Addr1/Loc/Pin</c> and <c>BuyerDtls.LglNm/Addr1/Loc</c> mandatory; this
/// domain model carries no legal name and no address for either party, so they are <b>omitted rather than
/// fabricated</b>;</item>
/// <item><c>HsnCd</c> may still be <c>""</c> where an income ledger declares no HSN/SAC, against
/// <c>minLength: 4</c>;</item>
/// <item><c>Qty</c>/<c>Unit</c> are omitted on every ledger-only line. Both are <b>Optional</b> in the schema, so
/// omission is legal — but "Validations on Items" rule 5 makes them mandatory <i>for goods</i>, and an accounts-only
/// sale of goods has no quantity dimension in the domain model at all. Omitting beats the previous <c>"Qty": 0</c>
/// beside a non-zero <c>TotAmt</c>, which was a declaration the IRP can read and disprove;</item>
/// <item><c>DocDtls.No</c> is emitted <b>verbatim</b> and is NOT guarded against the schema's
/// <c>^([A-Z1-9]{1}[A-Z0-9/-]{0,15})$</c>. Four ordinary numbering configurations violate it — a zero-padded width
/// ("0001"), a space-padded width ("&#160;&#160;&#160;1"), a lowercase prefix, and <c>NumberingMethod.None</c> (which
/// falls back to a 32-char lowercase GUID, over <c>maxLength: 16</c>). Guarding it would change the "paper == IRP ==
/// e-Way == GSTR-1 == Day Book, one identical string" contract, which is a user decision, not this writer's;</item>
/// <item>a zero-rated (LUT/bond) export posts no tax line, so it projects <b>no item lines at all</b>, against
/// "Validations on Items" rule 11 ("a minimum of 1 item").</item>
/// </list>
/// </para>
/// </summary>
public static class EInvoiceJson
{
    private const string SchemaVersion = "1.1"; // NIC INV-01; the workbook's Sample JSON emits Version "1.1".

    /// <summary>The only value the schema's <c>TranDtls.TaxSch</c> enum admits.</summary>
    private const string TaxScheme = "GST";

    /// <summary>Integer paisa ⇒ the schema's rupee decimal. Exact: no rounding happens here.</summary>
    private static decimal Rupees(long paisa) => paisa / 100m;

    /// <summary>Basis points ⇒ the schema's decimal percent (1800 ⇒ 18). Exact.</summary>
    private static decimal Percent(int basisPoints) => basisPoints / 100m;

    /// <summary>A conservative byte budget for a bulk part (NIC bulk limit ≈ 2 MB). The packer models each object's TRUE
    /// contribution to an indented JSON array (its standalone length + the extra 2-space indent every line gains when
    /// nested one level deeper + the element separator), so a full part stays strictly below 2,000,000 bytes.</summary>
    private const int PartByteBudget = 1_970_000;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds the deterministic INV-01 request bytes (UTF-8, no BOM) for a single covered outward voucher.
    /// Money is emitted as the schema's rupee decimal; <c>DocDtls.No</c> is the <b>as-typed</b> rendered document number
    /// (the same string print / e-Way / GSTR-1 / Day Book emit).</summary>
    public static byte[] BuildInv01(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return Serialize(BuildDto(company, voucher));
    }

    /// <summary>
    /// Builds a deterministic <b>batch</b> of INV-01 parts for the covered outward vouchers, auto-splitting so each part
    /// stays under the NIC ~2 MB bulk limit. Partitioning is deterministic: vouchers are ordered by (document date,
    /// number, id), then greedily packed, starting a new part when the next object would push the part over the budget.
    /// A single covered voucher ⇒ exactly one part; each part is independently byte-stable.
    /// </summary>
    public static IReadOnlyList<byte[]> BuildBatch(Company company, IEnumerable<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(vouchers);

        var ordered = vouchers
            .OrderBy(v => v.Date)
            .ThenBy(v => v.Number)
            .ThenBy(v => v.Id)
            .Select(v => BuildDto(company, v))
            .ToList();

        var parts = new List<byte[]>();
        var current = new List<Inv01Dto>();
        var currentBytes = 0;

        foreach (var dto in ordered)
        {
            var std = Serialize(dto);
            // When nested one level deeper inside the array, every line of the object gains a 2-space indent; add the
            // element separator (",\n") too. This makes currentBytes track the true array size (± the small wrapper).
            var lineCount = std.Count(b => b == (byte)'\n') + 1;
            var contribution = std.Length + lineCount * 2 + 2;
            if (current.Count > 0 && currentBytes + contribution > PartByteBudget)
            {
                parts.Add(SerializeArray(current));
                current = new List<Inv01Dto>();
                currentBytes = 0;
            }
            current.Add(dto);
            currentBytes += contribution;
        }
        if (current.Count > 0) parts.Add(SerializeArray(current));
        return parts;
    }

    // ------------------------------------------------------------------ assembly

    private static Inv01Dto BuildDto(Company company, Voucher voucher)
    {
        var gst = company.Gst ?? throw new InvalidOperationException("e-Invoice requires an enabled GST configuration.");
        var service = new EInvoiceService(company);
        var category = service.ResolveSupplyCategory(voucher)
            ?? throw new InvalidOperationException("A B2C / excluded supply cannot be emitted as an INV-01 (ER-15).");

        // INV-01 DocDtls.Typ — String(3), and its domain is exactly three values: "INV-Invoice, CRN-Credit Note,
        // DBN-Debit Note" (official schema, https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf field 9).
        // This is CORRECT and must NOT be merged with EWayBillService.PartACodesFor (W0-8): the e-Way docType domain
        // is a DIFFERENT five-value set — INV / BIL / BOE / CHL / OTH — that contains no CRN or DBN at all, and the
        // e-Way engine emitting these e-invoice codes is the very defect W0-8 fixed. They overlap only on "INV";
        // sharing one switch between two statutory domains would guarantee the wrong value on one of them. Note too
        // that BIL has no counterpart here by design: a bill of supply is outside e-invoicing (Rule 48(4) reaches a
        // tax invoice), and EInvoiceService.CoverageOf already refuses to mint an IRN request for one.
        var type = company.FindVoucherType(voucher.TypeId);
        var docType = type?.BaseType switch
        {
            VoucherBaseType.CreditNote => "CRN",
            VoucherBaseType.DebitNote => "DBN",
            _ => "INV",
        };

        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var partyGst = party?.PartyGst;

        // Posted per-rate-group heads + the ring-fenced cess total, read off the tax lines (ER-9).
        var groups = ReadRateGroups(voucher);
        var cessTotalPaisa = ReadCessTotalPaisa(voucher);
        var assessablePaisa = MoneyCodec.ToPaisa(GstReportSupport.InvoiceTaxableValue(voucher));
        var cgstPaisa = groups.Sum(g => g.CgstPaisa);
        var sgstPaisa = groups.Sum(g => g.SgstPaisa);
        var igstPaisa = groups.Sum(g => g.IgstPaisa);

        var items = BuildItems(company, voucher, groups);

        return new Inv01Dto
        {
            Version = SchemaVersion,
            TranDtls = new TranDtlsDto
            {
                TaxSch = TaxScheme,
                SupTyp = SupplyTypeCode(category, igstPaisa),
                RegRev = category == EInvoiceSupplyCategory.RcmSupplierLiable ? "Y" : "N",
            },
            DocDtls = new DocDtlsDto
            {
                Typ = docType,
                No = EInvoiceService.DocumentNumberOf(company, voucher),
                // Schema pattern [0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9]; the Sample JSON emits "11/08/2020".
                Dt = voucher.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            },
            SellerDtls = new SellerDtlsDto { Gstin = gst.Gstin, Stcd = gst.HomeStateCode },
            BuyerDtls = BuyerBlock(category, partyGst, gst.HomeStateCode),
            ItemList = items,
            ValDtls = new ValDtlsDto
            {
                AssVal = Rupees(assessablePaisa),
                CgstVal = Rupees(cgstPaisa),
                SgstVal = Rupees(sgstPaisa),
                IgstVal = Rupees(igstPaisa),
                CesVal = Rupees(cessTotalPaisa),
                TotInvVal = Rupees(assessablePaisa + cgstPaisa + sgstPaisa + igstPaisa + cessTotalPaisa),
            },
        };
    }

    /// <summary>
    /// The schema's <c>TranDtls.SupTyp</c> — enum [B2B, SEZWP, SEZWOP, EXPWP, EXPWOP, DEXP], described "B2B-Business
    /// to Business, SEZWP - SEZ with payment, SEZWOP - SEZ without payment, EXPWP - Export with Payment, EXPWOP -
    /// Export without payment, DEXP - Deemed Export".
    /// <para><b>An export's WP/WOP limb is decided by the POSTED IGST, not by the category alone.</b> The writer used
    /// to map <see cref="EInvoiceSupplyCategory.Export"/> unconditionally to <c>EXPWP</c>, so a zero-rated LUT/bond
    /// export declared that tax had been paid on it — the branch that decides refund eligibility, and a valid enum
    /// member, so no type guard could catch it. "Validations on Items" rule 9 ("In case of export transaction, IGST
    /// tax rate and value has to be passed") is what makes the pairing <c>EXPWP ⟺ IgstVal &gt; 0</c> checkable, and it
    /// is pinned as an invariant across every fixture.</para>
    /// <para>SEZ keeps its two explicitly-modelled categories: unlike Export, the with/without-payment distinction is
    /// already carried on the enum rather than inferred.</para>
    /// </summary>
    private static string SupplyTypeCode(EInvoiceSupplyCategory category, long igstPaisa) => category switch
    {
        EInvoiceSupplyCategory.Export => igstPaisa > 0 ? "EXPWP" : "EXPWOP",
        EInvoiceSupplyCategory.SezWithPayment => "SEZWP",
        EInvoiceSupplyCategory.SezWithoutPayment => "SEZWOP",
        EInvoiceSupplyCategory.DeemedExport => "DEXP",
        _ => "B2B", // Regular + RcmSupplierLiable (the RCM flag rides RegRev = Y)
    };

    /// <summary>
    /// The <c>BuyerDtls</c> block. Two of its three members are statutorily overridden for a supply that leaves the
    /// domestic tax territory, and BOTH overrides are the schema's own literals rather than derived data:
    /// <list type="bullet">
    /// <item><b><c>Gstin</c> = <c>"URP"</c> on a direct export.</b> The schema types it
    /// <c>string, minLength: 3, maxLength: 15, pattern: "^(([0-9]{2}[0-9A-Z]{13})|URP)$"</c> and describes it "GSTIN
    /// of buyer , URP if exporting"; "Validations" rule 15 states it outright — "In case of transaction of direct
    /// export, recipient GSTIN has to be URP and state code has to be 96, POS should be 96". An overseas recipient
    /// carries no GSTIN, so this used to emit JSON <c>null</c> (the writer's
    /// <see cref="JsonIgnoreCondition.Never"/> writes nulls out). <c>null</c> is neither a string nor <c>URP</c>, and
    /// <c>Gstin</c> is in <c>BuyerDtls</c>'s <c>required</c> list — so <b>every export was rejected on schema
    /// validation before a single figure was read</b>. An SEZ recipient IS registered, so its real GSTIN stands.</item>
    /// <item><b><c>Pos</c> = <c>Stcd</c> = <c>"96"</c> for an export or an SEZ supply.</b> Rule 15 (export), rule 16
    /// ("In case, Recipient is SEZ unit or SEZ developer, the Bill to State code should be 96 and also POS should be
    /// 96") and rule 17 ("… except if supply type is SEZ or exports wherein Recipient state code will be 96"). This
    /// normalises the OTHER overseas code the domestic state master admits —
    /// <see cref="GstReportSupport.IsOverseasStateCode"/> accepts both 96 and 99 — down to the single value the INV-01
    /// rules name.</item>
    /// </list>
    /// <para>Otherwise <c>Pos</c> is the recipient's own state: it is what decides intra vs inter-state at the IRP
    /// ("Validations" rule 24), so it tracks the same state code the buyer block declares.</para>
    ///
    /// <para><b>🔴 W0-15 OPEN GAP — this domestic limb was NOT reconciled, and the reason first given for that was
    /// wrong. Recorded here rather than left to be re-derived.</b> The domestic ladder below is
    /// <c>partyGst?.StateCode ?? homeStateCode</c> — the RAW derivation W0-15 replaced on the print path and in
    /// GSTR-1 with <c>GstReportSupport.IssuedPlaceOfSupply</c>, which reconciles the live party master against the tax
    /// the voucher actually POSTED. W0-15's plan row justified leaving it alone on the ground that "reconciling could
    /// emit a triple the IRP rejects". <b>That is backwards on the shape that matters:</b> clear an IGST-bearing
    /// invoice's party State and this block emits <c>Pos = Stcd = </c> the SUPPLIER's own State beside a recipient
    /// GSTIN whose first two digits are a different State — breaching validation 17 (GSTIN prefix vs state code) and
    /// validation 24 (supplier state == POS on an inter-state supply) at once. Not reconciling does not avoid an
    /// IRP-rejected payload; it produces one, and now also disagrees with the GSTR-1 row for the same voucher.
    /// <br/><b>Why it is still not changed here.</b> Minting an INV-01 payload is a WRITE path — the same class as
    /// <c>EWayBillService.PrepareRecord</c>, which W0-15 flagged and pinned rather than changed — and the reconciled
    /// answer for this shape is <c>null</c>, which <c>Pos</c>/<c>Stcd</c> may not be (both are <c>required</c> in the
    /// schema). Choosing what an unreconstructable POS emits into a statutory payload needs its own R7 grounding and
    /// its own slice; guessing it inside a routing clean-up would be the invention this campaign exists to stop.
    /// <b>Today's output is therefore PINNED, not blessed</b>, by
    /// <c>EInvoiceInv01SchemaConformanceTests.PINNED_GAP_the_inv01_buyer_block_still_derives_its_pos_from_the_raw_ladder</c>,
    /// so it cannot change silently.</para>
    /// </summary>
    private static BuyerDtlsDto BuyerBlock(
        EInvoiceSupplyCategory category, PartyGstDetails? partyGst, string? homeStateCode)
    {
        var isExport = category == EInvoiceSupplyCategory.Export;
        var isSez = category is EInvoiceSupplyCategory.SezWithPayment or EInvoiceSupplyCategory.SezWithoutPayment;
        var stateCode = isExport || isSez ? OverseasPlaceOfSupply : partyGst?.StateCode ?? homeStateCode;

        return new BuyerDtlsDto
        {
            Gstin = partyGst?.Gstin ?? (isExport ? UnregisteredPerson : null),
            Pos = stateCode,
            Stcd = stateCode,
        };
    }

    /// <summary>The schema's own literal for an unregistered/overseas recipient — <c>BuyerDtls.Gstin</c>'s pattern
    /// admits exactly <c>^(([0-9]{2}[0-9A-Z]{13})|URP)$</c>.</summary>
    private const string UnregisteredPerson = "URP";

    /// <summary>The place-of-supply state code the INV-01 rules require when the supply leaves the domestic tax
    /// territory: "State code of Place of supply. If POS lies outside the country, the code shall be 96."</summary>
    private const string OverseasPlaceOfSupply = "96";

    /// <summary>
    /// The single place an INV-01 item line is shaped, so the schema's <c>TotItemVal</c> identity — "Assessable
    /// Amount + Igst Amount + Cgst Amount + Sgst Amount + Cess Amount + Cess Nonadvol + State cess amount + State
    /// Cess Non advol + Other charges" — is computed once and cannot drift between the three call sites. Every
    /// amount arrives as integer <b>paisa</b> and leaves as the schema's rupee decimal; the rate arrives as basis
    /// points and leaves as a percent.
    /// <para><b><c>IsServc</c> declares the NATURE OF THE SUPPLY, not which table the line came out of.</b> It used
    /// to be hard-coded <c>true</c> on both ledger-only branches — i.e. it answered "does this voucher carry stock
    /// lines?" — so every accounts-only sale of GOODS declared itself a service, beside a goods HSN, which
    /// "Validations on Items" rule 3 rejects ("If Is_Service is selected, then the HSN codes must belong to
    /// services"). The callers now read the declared <see cref="StockItemGstDetails.SupplyType"/> of the leg's own
    /// item/ledger — the same block whose <c>HsnSac</c> becomes <c>HsnCd</c>, so the code and the flag cannot
    /// disagree.</para>
    ///
    /// <para><b><c>Qty</c>/<c>Unit</c> are OPTIONAL and are omitted where the line has no quantity.</b> Both are
    /// absent from the schema's <c>ItemList[]</c> <c>required</c> list, and rule 5 makes them "optional for
    /// Services". Emitting <c>0</c> instead made the schema's own identity — "Gross Amount of Item = Quantity X
    /// Selling Unit Price" — computable and FALSE by the whole line value (rule 3 of Calculation of Values permits a
    /// deviation only up to rounding up to the next rupee). An absent optional field is merely incomplete; a zero is
    /// a false declaration.</para>
    ///
    /// <para><b>Cess is split by its posted VALUATION MODE.</b> <c>CesAmt</c> is described "Cess Amount(Advalorem) on
    /// basis of rate and quantity of item" and <c>CesNonAdvlAmt</c> is its own field, so a per-unit (Specific) or
    /// RSP-factor cess belongs in the latter. <c>CesRt</c> is the ad-valorem rate read straight off the posted cess
    /// line (ER-9), never a literal 0: with <c>CesAmt</c> carrying the full cess against a zero rate the payload
    /// contradicted "Cess Value of Item = Taxable Value of Item X Cess Rate" and the IRP's arithmetic check rejected
    /// it.</para>
    /// </summary>
    private static ItemDto Item(
        int slNo, bool isService, string hsnCd, decimal? qty, string? unit,
        long unitPricePaisa, long taxablePaisa, int rateBasisPoints,
        long cgstPaisa, long sgstPaisa, long igstPaisa,
        int cessRateBasisPoints, long cessAdValoremPaisa, long cessNonAdValoremPaisa) => new()
        {
            SlNo = slNo.ToString(CultureInfo.InvariantCulture),
            IsServc = isService ? "Y" : "N",
            HsnCd = hsnCd,
            Qty = qty,
            Unit = unit,
            UnitPrice = Rupees(unitPricePaisa),
            TotAmt = Rupees(taxablePaisa),
            AssAmt = Rupees(taxablePaisa),
            GstRt = Percent(rateBasisPoints),
            IgstAmt = Rupees(igstPaisa),
            CgstAmt = Rupees(cgstPaisa),
            SgstAmt = Rupees(sgstPaisa),
            CesRt = Percent(cessRateBasisPoints),
            CesAmt = Rupees(cessAdValoremPaisa),
            CesNonAdvlAmt = Rupees(cessNonAdValoremPaisa),
            TotItemVal = Rupees(
                taxablePaisa + igstPaisa + cgstPaisa + sgstPaisa + cessAdValoremPaisa + cessNonAdValoremPaisa),
        };

    private static IReadOnlyList<ItemDto> BuildItems(
        Company company, Voucher voucher, IReadOnlyList<RateGroup> groups)
    {
        var inventory = voucher.InventoryLines;
        if (inventory.Count == 0)
        {
            // Ledger-only voucher. An ACCOUNTING (service) invoice's income legs carry a SAC, so each rate group is
            // expanded into one item PER SERVICE LEG bearing its own HsnCd. A plain As-Voucher sale has no SAC-bearing
            // leg and keeps the original single synthetic item per rate group (HsnCd "") — byte-identical, ER-13.
            var serviceLegs = ServiceLegsByRate(company, voucher, groups);
            // "Is this voucher a SERVICE invoice?" is a VOUCHER-level question, and the app already has exactly one
            // answer to it. It is the fallback only: where a leg declares its own supply type, that wins (below).
            var voucherIsService = GstReportSupport.IsServiceAccountingInvoice(company, voucher);
            var list = new List<ItemDto>();
            var slNo = 1;
            foreach (var g in groups)
            {
                if (serviceLegs.TryGetValue(g.Rate, out var legs) && legs.Count > 0)
                {
                    // Split the group's posted taxable + per-head tax + ITS OWN cess across ITS legs by value share,
                    // last leg absorbing the remainder — the same identity the item path and Gstr1's SAC attribution
                    // keep, so Σ AssAmt still foots to AssVal exactly and no line is invented or lost.
                    var groupValue = legs.Sum(l => l.Paisa);
                    long runV = 0, runC = 0, runS = 0, runI = 0, runCa = 0, runCn = 0;
                    for (var i = 0; i < legs.Count; i++)
                    {
                        var last = i == legs.Count - 1;
                        var v = last ? g.TaxablePaisa - runV
                            : (groupValue > 0 ? Apportion(g.TaxablePaisa, legs[i].Paisa, groupValue) : 0);
                        var c = last ? g.CgstPaisa - runC
                            : (groupValue > 0 ? Apportion(g.CgstPaisa, legs[i].Paisa, groupValue) : 0);
                        var s = last ? g.SgstPaisa - runS
                            : (groupValue > 0 ? Apportion(g.SgstPaisa, legs[i].Paisa, groupValue) : 0);
                        var ig = last ? g.IgstPaisa - runI
                            : (groupValue > 0 ? Apportion(g.IgstPaisa, legs[i].Paisa, groupValue) : 0);
                        var ca = last ? g.CessAdValoremPaisa - runCa
                            : (groupValue > 0 ? Apportion(g.CessAdValoremPaisa, legs[i].Paisa, groupValue) : 0);
                        var cn = last ? g.CessNonAdValoremPaisa - runCn
                            : (groupValue > 0 ? Apportion(g.CessNonAdValoremPaisa, legs[i].Paisa, groupValue) : 0);
                        if (!last) { runV += v; runC += c; runS += s; runI += ig; runCa += ca; runCn += cn; }

                        list.Add(Item(
                            slNo,
                            // The leg's OWN declared nature — the same GST block whose HsnSac becomes HsnCd below.
                            isService: legs[i].Ledger.SalesPurchaseGst?.SupplyType == GstSupplyType.Services,
                            // The SAME resolver GSTR-1's Table-12 row uses; a ledger with no declared code still
                            // emits "" (unchanged) rather than a fabricated one.
                            hsnCd: Gstr1.ServiceSacOf(legs[i].Ledger) ?? "",
                            // A ledger leg carries VALUE, never a quantity — so the optional pair is omitted rather
                            // than declared 0 (which would make the gross-amount identity computable and false).
                            qty: null, unit: null,
                            unitPricePaisa: v, taxablePaisa: v, rateBasisPoints: g.Rate,
                            cgstPaisa: c, sgstPaisa: s, igstPaisa: ig,
                            cessRateBasisPoints: g.CessRateBasisPoints,
                            cessAdValoremPaisa: ca, cessNonAdValoremPaisa: cn));
                        slNo++;
                    }
                    continue;
                }

                // No leg declares a GST block for this rate group — a plain As-Voucher sale. Nothing states the
                // nature of the supply at line level, so the voucher-level predicate decides it.
                list.Add(Item(
                    slNo, isService: voucherIsService, hsnCd: "", qty: null, unit: null,
                    unitPricePaisa: g.TaxablePaisa, taxablePaisa: g.TaxablePaisa, rateBasisPoints: g.Rate,
                    cgstPaisa: g.CgstPaisa, sgstPaisa: g.SgstPaisa, igstPaisa: g.IgstPaisa,
                    cessRateBasisPoints: g.CessRateBasisPoints,
                    cessAdValoremPaisa: g.CessAdValoremPaisa, cessNonAdValoremPaisa: g.CessNonAdValoremPaisa));
                slNo++;
            }
            return list;
        }

        // Item-invoice: attribute each rate group's per-head tax to its stock lines by value share (last line in the
        // group absorbs the remainder so Σ line tax == the group's posted tax exactly — mirrors Gstr1's HSN attribution).
        var singleRate = groups.Count == 1 ? groups[0].Rate : (int?)null;
        // Per-VOUCHER, so it is resolved once rather than per line (see GstReportSupport.BucketingValueLedger).
        var valueLedger = singleRate is null ? GstReportSupport.BucketingValueLedger(company, voucher) : null;
        var linesByRate = new Dictionary<int, List<VoucherInventoryLine>>();
        foreach (var il in inventory)
        {
            var rate = singleRate ?? LineIntegratedRate(company, voucher, valueLedger, il);
            if (!linesByRate.TryGetValue(rate, out var bucket)) linesByRate[rate] = bucket = new List<VoucherInventoryLine>();
            bucket.Add(il);
        }

        // Per-line tax AND per-line cess, both attributed WITHIN the line's own rate group. Cess used to be spread
        // across every line of the invoice from a single invoice-wide total (and, on the ledger-only path, dumped
        // entirely onto item #1). That was inert while `ces_amt_paisa` was an invented key the IRP could not read;
        // now that CesAmt/CesRt are the real NIC fields, the attribution has to track the group that actually bore
        // the cess, or "Cess Value of Item = Taxable Value of Item X Cess Rate" fails on every line.
        var tax = new Dictionary<VoucherInventoryLine, LineTax>();
        foreach (var g in groups)
        {
            if (!linesByRate.TryGetValue(g.Rate, out var groupLines) || groupLines.Count == 0) continue;
            var groupValue = groupLines.Sum(l => MoneyCodec.ToPaisa(l.Value));
            long runC = 0, runS = 0, runI = 0, runCa = 0, runCn = 0;
            for (var i = 0; i < groupLines.Count; i++)
            {
                var value = MoneyCodec.ToPaisa(groupLines[i].Value);
                long c, s, ig, ca, cn;
                if (i == groupLines.Count - 1)
                {
                    c = g.CgstPaisa - runC; s = g.SgstPaisa - runS; ig = g.IgstPaisa - runI;
                    ca = g.CessAdValoremPaisa - runCa; cn = g.CessNonAdValoremPaisa - runCn;
                }
                else
                {
                    c = Apportion(g.CgstPaisa, value, groupValue);
                    s = Apportion(g.SgstPaisa, value, groupValue);
                    ig = Apportion(g.IgstPaisa, value, groupValue);
                    ca = Apportion(g.CessAdValoremPaisa, value, groupValue);
                    cn = Apportion(g.CessNonAdValoremPaisa, value, groupValue);
                    runC += c; runS += s; runI += ig; runCa += ca; runCn += cn;
                }
                tax[groupLines[i]] = new LineTax(c, s, ig, g.CessRateBasisPoints, ca, cn);
            }
        }

        var items = new List<ItemDto>();
        var sl = 1;
        foreach (var il in inventory)
        {
            var item = company.FindStockItem(il.StockItemId);
            var t = tax.TryGetValue(il, out var found) ? found : default;
            var valuePaisa = MoneyCodec.ToPaisa(il.Value);
            // WI-10 Gap 2 follow-on: Qty and Unit must describe the SAME physical quantity the printed invoice,
            // the e-way bill and GSTR-1 state. Emitting the line quantity beside the item's BASE UQC declared
            // "2 NOS" for a line on which 24 Nos move. Declare the line's own unit when it maps to a valid UQC;
            // otherwise the resolver converts the quantity to base AND the rate with it — never one alone, which
            // is precisely the 12× money defect the unit contract warns about, here inside the NIC payload.
            var decl = UqcResolver.Declare(company, il, il.BilledQuantity);
            items.Add(Item(
                sl++,
                // A stock line's nature comes from the item's own GST block, which defaults to Goods — so this is
                // "N" for every existing item (ER-13) and reads the declaration rather than assuming it.
                isService: item?.Gst?.SupplyType == GstSupplyType.Services,
                // Resolution order is the ONE rule (drift lock D7); "" is the NIC schema's own "not declared".
                hsnCd: GstReportSupport.HsnSacOf(item) ?? "",
                // Schema: Qty is Number(10,3) — a quantity, not millis.
                qty: Math.Round(decl.Quantity, 3, MidpointRounding.AwayFromZero),
                unit: decl.Code ?? "OTH",
                // The resolver only ever converts a rate into the base unit when the result lands paisa-exact —
                // where it would not (₹10/Crate of 12 = ₹0.8333…/Nos) it declares the line's own unit under "OTH"
                // with the entered rate instead. So decl.Rate is paisa-exact on EVERY path, and MoneyCodec's
                // throw-on-inexact guard is the right boundary: no rounding happens here, and the footing identity
                // Qty x UnitPrice == AssAmt holds exactly rather than within a tolerance.
                unitPricePaisa: MoneyCodec.ToPaisa(new Money(decl.Rate)),
                taxablePaisa: valuePaisa,
                // NIC types GstRt as "the GST rate … that applies to the invoiced item", validated by
                // CGST Value = Taxable Value x GstRt / 2 and IGST Value = Taxable Value x GstRt
                // (einv-apisandbox.nic.in/version1.01/generate-irn.html). It is therefore the rate that must
                // reproduce THIS item's own attributed tax from THIS item's own AssAmt — the same figure the line
                // was bucketed by, never a fresh single-rung master read that could contradict it.
                rateBasisPoints: singleRate ?? LineIntegratedRate(company, voucher, valueLedger, il),
                cgstPaisa: t.Cgst, sgstPaisa: t.Sgst, igstPaisa: t.Igst,
                cessRateBasisPoints: t.CessRateBasisPoints,
                cessAdValoremPaisa: t.CessAdValorem, cessNonAdValoremPaisa: t.CessNonAdValorem));
        }
        return items;
    }

    /// <summary>One inventory line's attributed share of its rate group's posted tax and cess (all paisa).</summary>
    private readonly record struct LineTax(
        long Cgst, long Sgst, long Igst, int CessRateBasisPoints, long CessAdValorem, long CessNonAdValorem);

    /// <summary>
    /// The integrated rate (basis points) an item-invoice stock line is bucketed by — and, on a multi-rate invoice,
    /// STATED as that item's <c>GstRt</c>.
    /// <para><b>T0-17: this used to read the stock item's GST block directly</b>, hard-wired to one rung of the
    /// five-rung hierarchy, so on a <c>LedgerFirst</c> book — the shipped default since T0-4 S2b — or under an
    /// HSN-dated rate window it could declare to the IRP a rate that contradicts the item's own stated tax, and a
    /// different rate than the invoice in the customer's hand. It now delegates to the ONE rule,
    /// <see cref="GstReportSupport.BucketingRateOf"/>, which resolves exactly as the posting did.</para>
    /// </summary>
    private static int LineIntegratedRate(
        Company company, Voucher voucher, Domain.Ledger? valueLedger, VoucherInventoryLine il) =>
        GstReportSupport.BucketingRateOf(company, voucher, company.FindStockItem(il.StockItemId), valueLedger);

    /// <summary>
    /// The TAXABLE service-income ledger legs of a ledger-only voucher, bucketed into the posted rate group each
    /// belongs to — the e-invoice mirror of <c>Gstr1.AccumulateServiceHsn</c>'s bucketing, reading the SAME
    /// <see cref="Gstr1.ServiceLegs"/> definition so the payload's <c>HsnCd</c> cannot drift from the SAC the return
    /// files.
    /// <para>A NON-taxable (exempt/nil/non-GST) leg is deliberately excluded: it contributed nothing to the posted tax
    /// and nothing to <c>ass_val_paisa</c>, so bucketing it into a posted rate group would both tax an exempt supply
    /// and break the payload's footing identity. It is simply not an INV-01 line (an e-invoice is projected off the
    /// posted tax lines; the exempt value is reported through GSTR-1's exempt bucket, which now carries it — FIX-1/2).</para>
    /// <para>Empty for a plain As-Voucher sale, which is what keeps that path byte-identical.</para>
    /// </summary>
    private static Dictionary<int, List<(Domain.Ledger Ledger, long Paisa)>> ServiceLegsByRate(
        Company company, Voucher voucher, IReadOnlyList<RateGroup> groups)
    {
        var byRate = new Dictionary<int, List<(Domain.Ledger, long)>>();
        // Single-rate collapse, scoped to TAXABLE legs only (a non-taxable leg is never a group member).
        var singleRate = groups.Count == 1 ? groups[0].Rate : (int?)null;
        foreach (var (ledger, value) in Gstr1.ServiceLegs(company, voucher))
        {
            if (Gstr1.IsNonTaxableServiceLedger(ledger)) continue;
            // T0-17: the ONE bucketing rule, not a direct read of the ledger's SAC block. A leg whose declared rate
            // and whose RESOLVED rate differ (an HSN/SAC-dated window) used to leave its posted group unmatched, and
            // that group then fell through to the synthetic HsnCd "" item below — an INV-01 line declaring a supply
            // with no SAC at all, beside another line whose stated tax contradicted its own assessable amount.
            var rate = singleRate ?? GstReportSupport.BucketingRateOf(company, voucher, item: null, ledger);
            if (!byRate.TryGetValue(rate, out var list)) byRate[rate] = list = new List<(Domain.Ledger, long)>();
            list.Add((ledger, MoneyCodec.ToPaisa(new Money(value))));
        }
        return byRate;
    }

    /// <summary>Delegates to <see cref="ProRata.Paisa"/> — the ONE apportionment rule (drift lock D1).</summary>
    private static long Apportion(long total, long value, long totalValue) =>
        ProRata.Paisa(total, value, totalValue);

    /// <summary>
    /// Per-(integrated rate) posted head totals + taxable + <b>that group's own compensation cess</b>, read off the
    /// tax lines (ER-9). Reverse-charge lines are excluded (consistent with
    /// <see cref="GstReportSupport.InvoiceTaxableValue"/>).
    ///
    /// <para><b>How a Cess line is matched to its GST rate group, and why it is not by rate.</b> A cess line's own
    /// <see cref="GstLineTax.RateBasisPoints"/> is the CESS rate, not the GST rate, so
    /// <see cref="GstReportSupport.IntegratedRateOf"/> cannot key it. <c>GstService.ComputeInvoiceTax</c> posts, for
    /// each rate group in order, that group's heads and then <b>immediately</b> its cess ("one entry line per rate
    /// group, on the same side as the GST heads"), and <c>entry_lines</c> is loaded
    /// <c>ORDER BY line_order, id</c> — so a cess line belongs to the most recent preceding group, and that holds
    /// across a database round-trip. A cess line seen before any head (a hand-keyed voucher) is buffered and
    /// attributed to the first group that appears, so it can never be silently dropped from the item lines while
    /// still counting in <c>CesVal</c>.</para>
    ///
    /// <para><b>Ad-valorem vs not is also a pure read.</b> The engine stamps the cess line's rate with the group's
    /// "representative ad-valorem bp (0 for specific/RSP)" — its own words — so a positive rate means the cess is
    /// ad-valorem (<c>CesAmt</c> + <c>CesRt</c>) and zero means it is not (<c>CesNonAdvlAmt</c>). Nothing is
    /// recomputed from the item masters.</para>
    /// </summary>
    private static IReadOnlyList<RateGroup> ReadRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, Acc>();
        int? lastRate = null;
        var pendingCess = new List<GstLineTax>();
        var pendingCessPaisa = new List<long>();

        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            var amount = MoneyCodec.ToPaisa(line.Amount);

            if (g.TaxHead == GstTaxHead.Cess)
            {
                if (lastRate is int key) AddCess(key, g, amount);
                else { pendingCess.Add(g); pendingCessPaisa.Add(amount); }
                continue;
            }

            var rate = GstReportSupport.IntegratedRateOf(g, line.Amount);
            var cur = byRate.TryGetValue(rate, out var acc) ? acc : default;
            cur.Taxable = Math.Max(cur.Taxable, MoneyCodec.ToPaisa(g.TaxableValue));
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: cur.Cgst += amount; break;
                case GstTaxHead.State: cur.Sgst += amount; break;
                case GstTaxHead.Integrated: cur.Igst += amount; break;
                default: continue;
            }
            byRate[rate] = cur;
            lastRate = rate;

            // Flush any cess that arrived before the first head onto the first group we see.
            for (var i = 0; i < pendingCess.Count; i++) AddCess(rate, pendingCess[i], pendingCessPaisa[i]);
            pendingCess.Clear();
            pendingCessPaisa.Clear();
        }

        return byRate
            .OrderBy(kv => kv.Key)
            .Select(kv => new RateGroup(
                kv.Key, kv.Value.Cgst, kv.Value.Sgst, kv.Value.Igst, kv.Value.Taxable,
                kv.Value.CessRateBasisPoints, kv.Value.CessAdValorem, kv.Value.CessNonAdValorem))
            .ToList();

        void AddCess(int rate, GstLineTax g, long amount)
        {
            var cur = byRate.TryGetValue(rate, out var acc) ? acc : default;
            if (g.RateBasisPoints > 0)
            {
                cur.CessAdValorem += amount;
                cur.CessRateBasisPoints = g.RateBasisPoints;
            }
            else cur.CessNonAdValorem += amount;
            byRate[rate] = cur;
        }
    }

    private struct Acc
    {
        public long Cgst, Sgst, Igst, Taxable, CessAdValorem, CessNonAdValorem;
        public int CessRateBasisPoints;
    }

    // Phase 9 slice 5: the ring-fenced posted-cess total is read via the ONE shared GstReportSupport.PostedCessTotal helper
    // so this writer and the e-Way consignment-value / EWayBillJson writers can never drift (risk #1). Converting the
    // summed decimal-rupee total to paisa is exact (Σ amount × 100 == Σ (amount × 100) for paisa-exact amounts), so the
    // emitted bytes are unchanged from the previous per-line paisa fold.
    private static long ReadCessTotalPaisa(Voucher voucher) =>
        MoneyCodec.ToPaisa(GstReportSupport.PostedCessTotal(voucher));

    private static byte[] Serialize(object dto)
    {
        var json = JsonSerializer.Serialize(dto, dto.GetType(), Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private static byte[] SerializeArray(IReadOnlyList<Inv01Dto> dtos)
    {
        var json = JsonSerializer.Serialize(dtos, Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private readonly record struct RateGroup(
        int Rate, long CgstPaisa, long SgstPaisa, long IgstPaisa, long TaxablePaisa,
        int CessRateBasisPoints, long CessAdValoremPaisa, long CessNonAdValoremPaisa);

    // ------------------------------------------------------------------ INV-01 DTOs (fixed property order)

    // Property order follows the workbook's published Sample JSON. It is fixed (determinism, ER-10) but carries no
    // statutory meaning — the schema is an object model, not a positional one.

    private sealed record Inv01Dto
    {
        [JsonPropertyName("Version")] public required string Version { get; init; }
        [JsonPropertyName("TranDtls")] public required TranDtlsDto TranDtls { get; init; }
        [JsonPropertyName("DocDtls")] public required DocDtlsDto DocDtls { get; init; }
        [JsonPropertyName("SellerDtls")] public required SellerDtlsDto SellerDtls { get; init; }
        [JsonPropertyName("BuyerDtls")] public required BuyerDtlsDto BuyerDtls { get; init; }
        [JsonPropertyName("ItemList")] public required IReadOnlyList<ItemDto> ItemList { get; init; }
        [JsonPropertyName("ValDtls")] public required ValDtlsDto ValDtls { get; init; }
    }

    /// <summary>required: [TaxSch, SupTyp].</summary>
    private sealed record TranDtlsDto
    {
        [JsonPropertyName("TaxSch")] public required string TaxSch { get; init; }
        [JsonPropertyName("SupTyp")] public required string SupTyp { get; init; }
        [JsonPropertyName("RegRev")] public required string RegRev { get; init; }
    }

    /// <summary>required: [Typ, No, Dt]. <c>Dt</c> is DD/MM/YYYY.</summary>
    private sealed record DocDtlsDto
    {
        [JsonPropertyName("Typ")] public required string Typ { get; init; }
        [JsonPropertyName("No")] public required string No { get; init; }
        [JsonPropertyName("Dt")] public required string Dt { get; init; }
    }

    /// <summary>The schema's required list is [Gstin, LglNm, Addr1, Loc, Pin, Stcd]; the name/address members have no
    /// source in this domain model and are omitted rather than fabricated (see the type remarks).</summary>
    private sealed record SellerDtlsDto
    {
        [JsonPropertyName("Gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("Stcd")] public string? Stcd { get; init; }
    }

    /// <summary>The schema's required list is [Gstin, LglNm, Pos, Addr1, Loc, Stcd]. <c>Pos</c> is the place-of-supply
    /// state code, which decides intra vs inter-state ("Validations", rule 24).</summary>
    private sealed record BuyerDtlsDto
    {
        [JsonPropertyName("Gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("Pos")] public string? Pos { get; init; }
        [JsonPropertyName("Stcd")] public string? Stcd { get; init; }
    }

    /// <summary>required: [SlNo, IsServc, HsnCd, UnitPrice, TotAmt, AssAmt, GstRt, TotItemVal]. <c>SlNo</c> is a
    /// <b>string</b> (maxLength 6). Amounts are rupees; <c>Qty</c> a quantity; <c>GstRt</c>/<c>CesRt</c> percents.</summary>
    private sealed record ItemDto
    {
        [JsonPropertyName("SlNo")] public required string SlNo { get; init; }
        [JsonPropertyName("IsServc")] public required string IsServc { get; init; }
        [JsonPropertyName("HsnCd")] public required string HsnCd { get; init; }
        // Qty and Unit are OPTIONAL in the schema (absent from the ItemList[] `required` list) and are OMITTED —
        // not written as null — where the line carries no quantity. A per-property WhenWritingNull overrides the
        // writer's global JsonIgnoreCondition.Never, which is what keeps the mandatory members' nulls visible to
        // the conformance guard while these two simply disappear.
        [JsonPropertyName("Qty")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? Qty { get; init; }
        [JsonPropertyName("Unit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Unit { get; init; }
        [JsonPropertyName("UnitPrice")] public decimal UnitPrice { get; init; }
        [JsonPropertyName("TotAmt")] public decimal TotAmt { get; init; }
        [JsonPropertyName("AssAmt")] public decimal AssAmt { get; init; }
        [JsonPropertyName("GstRt")] public decimal GstRt { get; init; }
        [JsonPropertyName("IgstAmt")] public decimal IgstAmt { get; init; }
        [JsonPropertyName("CgstAmt")] public decimal CgstAmt { get; init; }
        [JsonPropertyName("SgstAmt")] public decimal SgstAmt { get; init; }
        [JsonPropertyName("CesRt")] public decimal CesRt { get; init; }
        [JsonPropertyName("CesAmt")] public decimal CesAmt { get; init; }
        [JsonPropertyName("CesNonAdvlAmt")] public decimal CesNonAdvlAmt { get; init; }
        [JsonPropertyName("TotItemVal")] public decimal TotItemVal { get; init; }
    }

    /// <summary>required: [AssVal, TotInvVal]. All members are the schema's rupee <c>Number(14,2)</c>.</summary>
    private sealed record ValDtlsDto
    {
        [JsonPropertyName("AssVal")] public decimal AssVal { get; init; }
        [JsonPropertyName("CgstVal")] public decimal CgstVal { get; init; }
        [JsonPropertyName("SgstVal")] public decimal SgstVal { get; init; }
        [JsonPropertyName("IgstVal")] public decimal IgstVal { get; init; }
        [JsonPropertyName("CesVal")] public decimal CesVal { get; init; }
        [JsonPropertyName("TotInvVal")] public decimal TotInvVal { get; init; }
    }
}
