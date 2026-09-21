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
/// Deterministic <b>offline-JSON</b> writer for the NIC <b>EWB-01</b> (Part A + Part B) and consolidated <b>EWB-02</b>
/// requests (Phase 9 slice 5; RQ-6; ER-5, ER-9, ER-10, ER-11) — the outbound-artefact twin of <see cref="EInvoiceJson"/>.
/// A pure, framework-agnostic emitter: <see cref="System.Text.Json"/> only, culture-invariant, <b>fixed property
/// order</b> via <c>[JsonPropertyName]</c> DTOs, <b>no clock / no RNG</b>, UTF-8 no BOM, <b>de-branded</b> (ER-11).
/// The item/tax values are read off the <b>posted</b> tax lines (ER-9), never recomputed; the consignment value is
/// the record's audited <see cref="EWayBillRecord.ConsignmentValuePaisa"/> (computed once by <c>EWayBillService</c>).
/// The 12-digit EWB number and validity are NEVER emitted — they only ever arrive inbound (ER-5 twin).
///
/// <para><b>🔴 T1-29 — THE EWB-01 PAYLOAD IS NOW THE NIC PAYLOAD, NOT OURS.</b> Until T1-29 this writer emitted a
/// structure of its own design: money in <b>integer paisa</b> under invented keys (<c>totInvValue_paisa</c>,
/// <c>taxable_amt_paisa</c>, <c>cgst_amt_paisa</c>…), an <c>itemList</c> that shared <b>zero</b> key names with the
/// schema, <c>docDate</c> in <c>yyyy-MM-dd</c>, and <b>six of the schema's seventeen mandatory keys missing</b>
/// (<c>fromPincode</c>, <c>toPincode</c>, <c>actFromStateCode</c>, <c>actToStateCode</c>, <c>totInvValue</c>,
/// <c>transactionType</c>). The portal would have rejected every one of those files, and a goods vehicle moves on an
/// accepted e-Way Bill — so the emission was a consignment that could not legally move, not a cosmetic naming gap.
/// The payload is now built against the <b>published NIC EWB-01 v1.03 JSON Schema</b>
/// (<c>https://docs.ewaybillgst.gov.in/apidocs/version1.03/generate-eway-bill.html</c>), a verbatim copy of which is
/// the ORACLE for <c>Ewb01SchemaConformanceTests</c>: the test validates the emitted bytes against the schema
/// document itself, so this writer cannot drift back towards a shape of our own.</para>
///
/// <para><b>Money and rates follow the schema, not our internal representation.</b> Every monetary member is the
/// schema's <c>Decimal(18,2)</c> in <b>rupees</b> (<c>multipleOf 0.01</c>); paisa remains the internal unit and is
/// divided exactly on the way out (<see cref="MoneyCodec"/>, drift lock D3). The item block carries <b>rates</b>
/// (<c>cgstRate</c>/<c>sgstRate</c>/<c>igstRate</c>/<c>cessRate</c>, <c>Decimal(6,3)</c> percents) while the main
/// object carries the <b>values</b> (<c>cgstValue</c>…<c>totInvValue</c>) — NIC's own split, and the reason the old
/// per-item amount keys had no schema home at all. <c>docDate</c> is <b>DD/MM/YYYY</b>, which is what the schema's
/// own <c>pattern</c> demands.</para>
///
/// <para><b>Nothing is fabricated to satisfy a required key.</b> Where the domain model genuinely has no source for
/// a mandatory member (an unset company PIN, a party with no mailing PIN) the member is emitted as <c>null</c>
/// rather than invented — visible to the conformance guard and to <see cref="MissingMandatory"/>, which the
/// Generate e-Way Bill screen shows to the operator <b>before</b> they upload. An unregistered counterparty is the
/// one exception and it is NIC's own rule, not an invention: "In case of unregistered person involved in the
/// transaction, pass URP in these fields."</para>
/// </summary>
public static class EWayBillJson
{
    /// <summary>NIC EWB-01 / EWB-02 schema generation this writer targets.</summary>
    private const string SchemaVersion = "1.03";

    /// <summary>
    /// <b>EWB-02 only.</b> The consolidated request is a different NIC API with its own payload, and <b>it has not
    /// been re-grounded</b>: T1-29 covered EWB-01. The EWB-02 keys below remain a structured emission of our own
    /// design, and this flag says so on the artefact rather than letting a reader assume the EWB-01 work covered it.
    /// </summary>
    private const string Ewb02SchemaStatusFlag =
        "faithful-structured; NIC EWB-02 (consolidated) JSON keys not yet verified against the published schema";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>NIC's placeholder GSTIN for an unregistered counterparty ("pass URP in these fields").</summary>
    private const string UnregisteredPerson = "URP";

    /// <summary>Builds the deterministic EWB-01 request bytes (UTF-8, no BOM) for one goods-movement voucher + its
    /// <see cref="EWayBillRecord"/> (Part A from the posted lines, Part B from the record), conforming to the
    /// published NIC EWB-01 v1.03 JSON Schema. Money is the schema's rupee <c>Decimal(18,2)</c>; <c>docNo</c> is the
    /// <b>uppercased</b> document number and <c>docDate</c> is DD/MM/YYYY.</summary>
    public static byte[] BuildEwb01(Company company, Voucher voucher, EWayBillRecord record)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(record);
        return Serialize(BuildEwb01Dto(company, voucher, record));
    }

    /// <summary>
    /// The schema-mandatory members this book cannot source for <paramref name="voucher"/>, in schema order — empty
    /// when the payload is complete. The portal rejects the file outright if any of these is absent, so the Generate
    /// e-Way Bill screen surfaces the list at the moment the JSON is written rather than leaving the operator to
    /// discover it on upload.
    /// <para>Only the members with a real domain source are checked: the remaining mandatory keys
    /// (<c>supplyType</c>, <c>subSupplyType</c>, <c>docType</c>, <c>docNo</c>, <c>docDate</c>, <c>transDistance</c>,
    /// <c>itemList</c>, <c>totInvValue</c>, <c>transactionType</c>) are non-null by construction — the record cannot
    /// exist without them — and the conformance test, not a runtime probe, is what holds that.</para>
    /// </summary>
    public static IReadOnlyList<string> MissingMandatory(Company company, Voucher voucher, EWayBillRecord record)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(record);

        var dto = BuildEwb01Dto(company, voucher, record);
        var missing = new List<string>();
        if (string.IsNullOrEmpty(dto.FromGstin)) missing.Add("fromGstin");
        if (dto.FromPincode is null) missing.Add("fromPincode");
        if (dto.FromStateCode is null) missing.Add("fromStateCode");
        if (string.IsNullOrEmpty(dto.ToGstin)) missing.Add("toGstin");
        if (dto.ToPincode is null) missing.Add("toPincode");
        if (dto.ToStateCode is null) missing.Add("toStateCode");
        if (dto.ActFromStateCode is null) missing.Add("actFromStateCode");
        if (dto.ActToStateCode is null) missing.Add("actToStateCode");
        if (dto.ItemList.Any(i => i.HsnCode is null)) missing.Add("itemList[].hsnCode");
        return missing;
    }

    /// <summary>Builds the deterministic consolidated EWB-02 request bytes (UTF-8, no BOM) for a set of already-generated
    /// children travelling in one conveyance. No monetary recomputation — a consolidation is a header over the child EWB
    /// numbers (ER-9). <b>Not</b> re-grounded by T1-29; see <see cref="Ewb02SchemaStatusFlag"/>.</summary>
    public static byte[] BuildEwb02(Company company, ConsolidatedEWayBill consolidated)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(consolidated);
        var dto = new Ewb02Dto
        {
            Version = SchemaVersion,
            FromStateCode = consolidated.FromStateCode,
            VehicleNo = consolidated.VehicleNumber,
            TransMode = (int)consolidated.Mode,
            EwbList = consolidated.ChildEwbNumbers.ToList(),
            SchemaStatus = Ewb02SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    // ------------------------------------------------------------------ assembly

    private static Ewb01Dto BuildEwb01Dto(Company company, Voucher voucher, EWayBillRecord record)
    {
        var gst = company.Gst ?? throw new InvalidOperationException("e-Way Bill requires an enabled GST configuration.");
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var partyGst = party?.PartyGst;

        var groups = ReadRateGroups(voucher);
        var items = BuildItems(company, voucher, groups);

        // W0-8 — the consignor/consignee ends follow the record's supplyType. NIC's Supply-Type/Document-Type mapping
        // (https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html) constrains the From/To party on every row:
        // an Inward row is From = Other GSTIN/URP, To = Self. Writing fromGstin = the filer's own GSTIN unconditionally
        // — as this did — inverted every inward movement the engine can now legally reach.
        var inward = string.Equals(record.SupplyType, "I", StringComparison.Ordinal);

        var self = new Party(company.MailingName, AddressLinesOf(company.Address), company.Pin, gst.Gstin, gst.HomeStateCode);
        var other = new Party(
            party?.Mailing?.MailingName ?? party?.Name,
            party?.Mailing?.AddressLines ?? Array.Empty<string>(),
            party?.Mailing?.Pincode,
            // NIC: "In case of unregistered person involved in the transaction, pass URP in these fields."
            partyGst?.Gstin ?? UnregisteredPerson,
            partyGst?.StateCode);

        var from = inward ? other : self;
        var to = inward ? self : other;

        // NIC's Bill-To/Ship-To rule decides which end each State code describes: "toGstin, toTrdName and toStateCode
        // should be passed with 'Bill To' party and toAddr1, toAddr2, toPlace, toPincode and actToStateCode values
        // should be passed with 'Ship To' party" (and the mirror for Bill-From/Dispatch-From). So the *StateCode pair
        // is the GSTIN's State and the act*StateCode pair is the State goods physically move between — which is what
        // the record's Ship-From / Ship-To hold. Before T1-29 the record's ship codes overwrote fromStateCode /
        // toStateCode, which states the Ship-To State against the Bill-To GSTIN — a contradiction the portal checks.
        var fromStateCode = StateCode(from.StateCode);
        var toStateCode = StateCode(to.StateCode);

        var totals = Totals(groups, record.ConsignmentValuePaisa);

        return new Ewb01Dto
        {
            SupplyType = record.SupplyType ?? "",
            SubSupplyType = record.SubSupplyType ?? "",
            DocType = record.DocType ?? "",
            DocNo = EInvoiceService.DocumentNumberOf(company, voucher),
            // The schema's own pattern is [0-3][0-9]/[0-1][0-9]/[2][0][1-2][0-9] and the Data Structure table says
            // "dd/mm/yyyy format". We emitted yyyy-MM-dd, which fails that pattern outright.
            DocDate = voucher.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            FromGstin = from.Gstin,
            FromTrdName = Trim(from.TradeName),
            FromAddr1 = AddressLine(from.AddressLines, 0),
            FromAddr2 = AddressLine(from.AddressLines, 1),
            ActFromStateCode = StateCode(record.ShipFromStateCode) ?? fromStateCode,
            FromPincode = Pincode(from.Pincode),
            FromStateCode = fromStateCode,
            ToGstin = to.Gstin,
            ToTrdName = Trim(to.TradeName),
            ToAddr1 = AddressLine(to.AddressLines, 0),
            ToAddr2 = AddressLine(to.AddressLines, 1),
            ToPincode = Pincode(to.Pincode),
            ActToStateCode = StateCode(record.ShipToStateCode) ?? toStateCode,
            ToStateCode = toStateCode,
            // Master codes: 1 Regular, 2 Bill To - Ship To, 3 Bill From - Dispatch From, 4 Combination of 2 and 3
            // (https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html). A recorded Ship-To GSTIN is exactly a
            // Bill-To/Ship-To movement, and NIC requires it for types 2 and 4. Bill-From/Dispatch-From (3, and so 4)
            // needs a dispatch-from PARTY, which this domain model does not carry, so it is never emitted.
            TransactionType = string.IsNullOrWhiteSpace(record.ShipToGstin) ? 1 : 2,
            TotalValue = totals.Taxable,
            CgstValue = totals.Cgst,
            SgstValue = totals.Sgst,
            IgstValue = totals.Igst,
            CessValue = totals.CessAdValorem,
            CessNonAdvolValue = totals.CessNonAdValorem,
            // NIC: "Sum of totalValue, cgstValue, sgstValue, igstValue, cessValue, otherValue and cessNonAdvolValue
            // should be equal to or less than totInvValue with a grace value of Rs. 2.00." The consignment value is
            // audited independently of the tax lines, so the residual IS the invoice's other charges — deriving it
            // makes the document satisfy that rule exactly rather than within the grace, on every voucher.
            OtherValue = totals.Other,
            TotInvValue = totals.TotInvValue,
            TransMode = record.Mode is { } m ? ((int)m).ToString(CultureInfo.InvariantCulture) : null,
            // Schema type is string, not integer — "transDistance": { "type": "string" }.
            TransDistance = record.DistanceKm.ToString(CultureInfo.InvariantCulture),
            TransporterId = Trim(record.TransporterId),
            TransDocNo = Trim(record.TransportDocNo),
            VehicleNo = Trim(record.VehicleNumber),
            // Master codes: R Regular, O ODC (Over Dimensional Cargo). Only meaningful beside a vehicle number.
            VehicleType = Trim(record.VehicleNumber) is null ? null : record.IsOverDimensionalCargo ? "O" : "R",
            ItemList = items,
        };
    }

    /// <summary>The main object's rupee value block. Every member is read off the posted lines or off the record's
    /// audited consignment value — nothing is recomputed from a rate.</summary>
    private static (decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst,
                    decimal CessAdValorem, decimal CessNonAdValorem, decimal Other, decimal TotInvValue)
        Totals(IReadOnlyList<RateGroup> groups, long consignmentValuePaisa)
    {
        long taxable = 0, cgst = 0, sgst = 0, igst = 0, cessAd = 0, cessNon = 0;
        foreach (var g in groups)
        {
            taxable += g.TaxablePaisa;
            cgst += g.CgstPaisa;
            sgst += g.SgstPaisa;
            igst += g.IgstPaisa;
            cessAd += g.CessAdValoremPaisa;
            cessNon += g.CessNonAdValoremPaisa;
        }
        var other = consignmentValuePaisa - (taxable + cgst + sgst + igst + cessAd + cessNon);
        return (Rupees(taxable), Rupees(cgst), Rupees(sgst), Rupees(igst),
                Rupees(cessAd), Rupees(cessNon), Rupees(other), Rupees(consignmentValuePaisa));
    }

    private static IReadOnlyList<ItemDto> BuildItems(
        Company company, Voucher voucher, IReadOnlyList<RateGroup> groups)
    {
        var inventory = voucher.InventoryLines;
        if (inventory.Count == 0)
        {
            // As-voucher (no stock lines): one synthetic item per posted rate group.
            var list = new List<ItemDto>();
            foreach (var g in groups)
                list.Add(Item(null, null, g, MoneyCodec.FromPaisa(g.TaxablePaisa).Amount, quantity: 0m, unit: "OTH"));
            return list;
        }

        // Item-invoice: attribute each rate group's per-head tax to its stock lines by value share (last line in the
        // group absorbs the remainder so Σ line tax == the group's posted tax exactly — mirrors the INV-01 attribution).
        // The EWB-01 item block states RATES, not amounts, so only the line's rate group is needed per line; the
        // apportionment survives because the main object's *Value members are what carry the money.
        var singleRate = groups.Count == 1 ? groups[0].Rate : (int?)null;
        // Per-VOUCHER, so it is resolved once rather than per line (see GstReportSupport.BucketingValueLedger).
        var valueLedger = singleRate is null ? GstReportSupport.BucketingValueLedger(company, voucher) : null;

        var byRate = groups.ToDictionary(g => g.Rate);
        var items = new List<ItemDto>();
        foreach (var il in inventory)
        {
            var item = company.FindStockItem(il.StockItemId);
            // NIC maps the e-Way item rate onto the SAME quantity the e-invoice states — igstRate = Item.GstRt,
            // cgstRate = sgstRate = Item.GstRt/2, taxableAmount = Item.AssAmt
            // (einv-apisandbox.nic.in/Mapping_of_ewaybill_schema.html) — so the consignment and the invoice must
            // never name two different rates for one line.
            var rate = singleRate ?? LineIntegratedRate(company, voucher, valueLedger, il);
            var group = byRate.TryGetValue(rate, out var g)
                ? g
                : new RateGroup(rate, 0, 0, 0, 0, 0, 0, 0);
            // WI-10 Gap 2 follow-on: the quantity an e-way bill declares is the quantity a checkpoint physically
            // verifies. Emitting the line quantity beside the item's BASE UQC declared "2 NOS" on a consignment
            // in which 24 Nos travel — a verification exposure with no money symptom to catch it. Declare the
            // line's own unit when it maps to a valid UQC, else the base unit with the quantity converted.
            var decl = UqcResolver.Declare(company, il, il.BilledQuantity);
            items.Add(Item(item, GstReportSupport.HsnSacOf(item), group, il.Value.Amount, decl.Quantity,
                decl.Code ?? "OTH"));
        }
        return items;
    }

    private static ItemDto Item(StockItem? item, string? hsnSac, RateGroup group, decimal taxableAmount,
        decimal quantity, string unit)
    {
        // The schema types hsnCode as a NUMBER (Number(8) in the Data Structure table), not a string. A book that
        // declares no HSN has nothing to put here: "" was our own sentinel and is not a schema value, so the member
        // is null — visible to MissingMandatory and to the conformance guard — rather than a fabricated code.
        long? hsn = long.TryParse(hsnSac, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        // NIC validation: "In case of intra-state transaction ... the SGST and CGST tax rate and value has to be
        // passed"; inter-state (and any SEZ leg) passes IGST. The posted heads already answer which this is — read
        // them rather than re-deciding place of supply here.
        var inter = group.IgstPaisa != 0;
        var half = group.Rate / 2m / 100m;   // basis points ⇒ percent, halved for the intra pair
        return new ItemDto
        {
            ProductName = Trim(item?.Name),
            ProductDesc = Trim(item?.Alias) ?? Trim(item?.Name),
            HsnCode = hsn,
            Quantity = quantity,
            QtyUnit = unit,
            TaxableAmount = taxableAmount,
            SgstRate = inter ? 0m : half,
            CgstRate = inter ? 0m : half,
            IgstRate = inter ? Percent(group.Rate) : 0m,
            CessRate = Percent(group.CessRateBasisPoints),
            // The schema's cessNonadvol is a Decimal(6,3) RATE beside cessRate. This domain values non-ad-valorem
            // cess as an AMOUNT (specific per-unit / RSP-factor) and holds no per-unit RATE to state here, so the
            // rate is 0 and the money rides the main object's cessNonAdvolValue, where it is a value. Nothing is
            // invented to fill it.
            CessNonAdvol = 0m,
        };
    }

    /// <summary>
    /// The integrated rate (basis points) a consignment line is bucketed by — and, on a multi-rate document, STATED
    /// as that line's rate on the EWB-01.
    /// <para><b>T0-17: this used to read the stock item's GST block directly</b>, one hard-wired rung of five, so a
    /// checkpoint reading the e-way bill and a customer reading the invoice could see two different rates for the
    /// same goods. It now delegates to the ONE rule, <see cref="GstReportSupport.BucketingRateOf"/>.</para>
    /// </summary>
    private static int LineIntegratedRate(
        Company company, Voucher voucher, Domain.Ledger? valueLedger, VoucherInventoryLine il) =>
        GstReportSupport.BucketingRateOf(company, voucher, company.FindStockItem(il.StockItemId), valueLedger);

    /// <summary>
    /// Per-(integrated rate) posted head totals + taxable, read off the tax lines (ER-9). Reverse-charge lines are
    /// excluded (consistent with <see cref="GstReportSupport.InvoiceTaxableValue"/>).
    /// <para><b>Cess is attributed to its GST rate group by POSTING ORDER, never by rate</b> — a cess line's own
    /// <see cref="GstLineTax.RateBasisPoints"/> is the CESS rate, so keying it by
    /// <see cref="GstReportSupport.IntegratedRateOf"/> would invent a phantom rate group. <c>ComputeInvoiceTax</c>
    /// posts each group's heads and then immediately that group's cess, and entry lines load in that order, so a
    /// cess line belongs to the most recent preceding group; one seen before any head is buffered onto the first
    /// group that appears so it can never be silently dropped. This mirrors <see cref="EInvoiceJson"/> exactly,
    /// which is the point — the two outbound artefacts must not disagree about one invoice's cess.</para>
    /// <para>A positive cess-line rate means the engine valued it ad valorem (<c>cessValue</c> + <c>cessRate</c>);
    /// zero means specific / RSP-factor (<c>cessNonAdvolValue</c>). Nothing is recomputed from the item masters.</para>
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
            // The group taxable is the same on every line of the group; take the max so the intra CGST+SGST legs
            // (equal taxable) are not double-counted.
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

    // ------------------------------------------------------------------ small conversions

    /// <summary>Integer paisa ⇒ the schema's rupee <c>Decimal(18,2)</c> (<c>multipleOf 0.01</c>). Exact.</summary>
    private static decimal Rupees(long paisa) => paisa / 100m;

    /// <summary>Basis points ⇒ the schema's decimal percent (1800 ⇒ 18). Exact.</summary>
    private static decimal Percent(int basisPoints) => basisPoints / 100m;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? AddressLine(IReadOnlyList<string> lines, int index) =>
        index < lines.Count ? Trim(lines[index]) : null;

    private static IReadOnlyList<string> AddressLinesOf(string? address) =>
        string.IsNullOrWhiteSpace(address)
            ? Array.Empty<string>()
            : address.Replace("\r\n", "\n").Replace('\r', '\n')
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The schema types every State code as <c>integer</c> (maximum 99); this domain stores the same code as
    /// a two-character string ("27"). A non-numeric or absent code yields <c>null</c> rather than a guess.</summary>
    private static int? StateCode(string? code) =>
        int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= 99
            ? parsed
            : null;

    /// <summary>The schema types both PIN codes as <c>integer</c> (100000–999999).</summary>
    private static int? Pincode(string? pin) =>
        int.TryParse(pin, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 100_000 and <= 999_999
            ? parsed
            : null;

    private static byte[] Serialize(object dto)
    {
        var json = JsonSerializer.Serialize(dto, dto.GetType(), Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private readonly record struct RateGroup(
        int Rate, long CgstPaisa, long SgstPaisa, long IgstPaisa, long TaxablePaisa,
        int CessRateBasisPoints, long CessAdValoremPaisa, long CessNonAdValoremPaisa);

    private struct Acc
    {
        public long Cgst, Sgst, Igst, Taxable, CessAdValorem, CessNonAdValorem;
        public int CessRateBasisPoints;
    }

    /// <summary>One end of the movement — whichever of the filer and the counterparty the supply type puts there.</summary>
    private readonly record struct Party(
        string? TradeName, IReadOnlyList<string> AddressLines, string? Pincode, string? Gstin, string? StateCode);

    // ------------------------------------------------------------------ EWB-01 DTO (NIC v1.03 key names and order)

    /// <summary>
    /// The NIC EWB-01 v1.03 request object. Property names and order follow the published schema's own
    /// <c>properties</c> block; the seventeen mandatory members are never omitted (a null is visible, an absent key
    /// is not), while genuinely optional members we cannot source are dropped with
    /// <see cref="JsonIgnoreCondition.WhenWritingNull"/> rather than written as null — the same discipline
    /// <see cref="EInvoiceJson"/> applies to INV-01.
    /// </summary>
    private sealed record Ewb01Dto
    {
        [JsonPropertyName("supplyType")] public required string SupplyType { get; init; }
        [JsonPropertyName("subSupplyType")] public required string SubSupplyType { get; init; }
        [JsonPropertyName("docType")] public required string DocType { get; init; }
        [JsonPropertyName("docNo")] public required string DocNo { get; init; }
        [JsonPropertyName("docDate")] public required string DocDate { get; init; }
        [JsonPropertyName("fromGstin")] public string? FromGstin { get; init; }

        [JsonPropertyName("fromTrdName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FromTrdName { get; init; }

        [JsonPropertyName("fromAddr1")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FromAddr1 { get; init; }

        [JsonPropertyName("fromAddr2")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FromAddr2 { get; init; }

        [JsonPropertyName("actFromStateCode")] public int? ActFromStateCode { get; init; }
        [JsonPropertyName("fromPincode")] public int? FromPincode { get; init; }
        [JsonPropertyName("fromStateCode")] public int? FromStateCode { get; init; }
        [JsonPropertyName("toGstin")] public string? ToGstin { get; init; }

        [JsonPropertyName("toTrdName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToTrdName { get; init; }

        [JsonPropertyName("toAddr1")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToAddr1 { get; init; }

        [JsonPropertyName("toAddr2")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToAddr2 { get; init; }

        [JsonPropertyName("toPincode")] public int? ToPincode { get; init; }
        [JsonPropertyName("actToStateCode")] public int? ActToStateCode { get; init; }
        [JsonPropertyName("toStateCode")] public int? ToStateCode { get; init; }
        [JsonPropertyName("transactionType")] public int TransactionType { get; init; }
        [JsonPropertyName("totalValue")] public decimal TotalValue { get; init; }
        [JsonPropertyName("cgstValue")] public decimal CgstValue { get; init; }
        [JsonPropertyName("sgstValue")] public decimal SgstValue { get; init; }
        [JsonPropertyName("igstValue")] public decimal IgstValue { get; init; }
        [JsonPropertyName("cessValue")] public decimal CessValue { get; init; }
        [JsonPropertyName("cessNonAdvolValue")] public decimal CessNonAdvolValue { get; init; }
        [JsonPropertyName("otherValue")] public decimal OtherValue { get; init; }
        [JsonPropertyName("totInvValue")] public decimal TotInvValue { get; init; }

        [JsonPropertyName("transMode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TransMode { get; init; }

        [JsonPropertyName("transDistance")] public required string TransDistance { get; init; }

        [JsonPropertyName("transporterId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TransporterId { get; init; }

        [JsonPropertyName("transDocNo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TransDocNo { get; init; }

        [JsonPropertyName("vehicleNo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? VehicleNo { get; init; }

        [JsonPropertyName("vehicleType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? VehicleType { get; init; }

        [JsonPropertyName("itemList")] public required IReadOnlyList<ItemDto> ItemList { get; init; }
    }

    /// <summary>One <c>itemList</c> entry. The schema's required pair is [hsnCode, taxableAmount]; the tax members are
    /// <b>rates</b> (<c>Decimal(6,3)</c> percents), not amounts.</summary>
    private sealed record ItemDto
    {
        [JsonPropertyName("productName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProductName { get; init; }

        [JsonPropertyName("productDesc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProductDesc { get; init; }

        [JsonPropertyName("hsnCode")] public long? HsnCode { get; init; }
        [JsonPropertyName("quantity")] public decimal Quantity { get; init; }
        [JsonPropertyName("qtyUnit")] public required string QtyUnit { get; init; }
        [JsonPropertyName("taxableAmount")] public decimal TaxableAmount { get; init; }
        [JsonPropertyName("sgstRate")] public decimal SgstRate { get; init; }
        [JsonPropertyName("cgstRate")] public decimal CgstRate { get; init; }
        [JsonPropertyName("igstRate")] public decimal IgstRate { get; init; }
        [JsonPropertyName("cessRate")] public decimal CessRate { get; init; }
        [JsonPropertyName("cessNonadvol")] public decimal CessNonAdvol { get; init; }
    }

    // ------------------------------------------------------------------ EWB-02 DTO (NOT re-grounded — see the flag)

    private sealed record Ewb02Dto
    {
        [JsonPropertyName("Version")] public required string Version { get; init; }
        [JsonPropertyName("fromStateCode")] public required string FromStateCode { get; init; }
        [JsonPropertyName("vehicleNo")] public required string VehicleNo { get; init; }
        [JsonPropertyName("transMode")] public int TransMode { get; init; }
        [JsonPropertyName("ewbList")] public required IReadOnlyList<string> EwbList { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }
}
