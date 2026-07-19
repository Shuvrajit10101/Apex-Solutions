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
/// order</b> via <c>[JsonPropertyName]</c> DTOs, <b>no clock / no RNG</b>, money as <b>integer paisa</b>
/// (<see cref="MoneyCodec"/>, ER-10), UTF-8 no BOM, <b>de-branded</b> (ER-11). The item/tax values are read off the
/// <b>posted</b> tax lines (ER-9), never recomputed; the consignment value is the record's audited
/// <see cref="EWayBillRecord.ConsignmentValuePaisa"/> (computed once by <c>EWayBillService</c>). The 12-digit EWB number
/// and validity are NEVER emitted — they only ever arrive inbound (ER-5 twin).
/// <para>
/// <b>R7 (A14 to confirm):</b> the exact NIC EWB-01 JSON key names were not fully verifiable at build; the schema version
/// is pinned (<see cref="SchemaVersion"/>) and flagged via <c>schemaStatus</c>. This is a faithful structured emission
/// (paisa, uppercased doc-no, GstRt-accepts-40, trending-to-0 cess); the field names may need a later rename pass.
/// </para>
/// </summary>
public static class EWayBillJson
{
    private const string SchemaVersion = "1.03"; // NIC EWB-01 v1.03
    private const string SchemaStatusFlag =
        "faithful-structured; NIC EWB-01 v1.03 JSON keys pending A14 confirmation (R7)";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds the deterministic EWB-01 request bytes (UTF-8, no BOM) for one goods-movement voucher + its
    /// <see cref="EWayBillRecord"/> (Part A from the posted lines, Part B from the record). Money is integer paisa;
    /// <c>docNo</c> is the <b>uppercased</b> document number.</summary>
    public static byte[] BuildEwb01(Company company, Voucher voucher, EWayBillRecord record)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(record);
        return Serialize(BuildEwb01Dto(company, voucher, record));
    }

    /// <summary>Builds the deterministic consolidated EWB-02 request bytes (UTF-8, no BOM) for a set of already-generated
    /// children travelling in one conveyance. No monetary recomputation — a consolidation is a header over the child EWB
    /// numbers (ER-9).</summary>
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
            SchemaStatus = SchemaStatusFlag,
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
        var cessTotalPaisa = MoneyCodec.ToPaisa(GstReportSupport.PostedCessTotal(voucher));
        var items = BuildItems(company, voucher, groups, cessTotalPaisa);

        return new Ewb01Dto
        {
            Version = SchemaVersion,
            SupplyType = record.SupplyType ?? "",
            SubSupplyType = record.SubSupplyType ?? "",
            DocType = record.DocType ?? "",
            DocNo = EInvoiceService.DocumentNumberOf(voucher),
            DocDate = $"{voucher.Date:yyyy-MM-dd}",
            FromGstin = gst.Gstin,
            FromStateCode = record.ShipFromStateCode ?? gst.HomeStateCode,
            ToGstin = partyGst?.Gstin,
            ToStateCode = record.ShipToStateCode ?? partyGst?.StateCode,
            ShipToGstin = record.ShipToGstin,
            ItemList = items,
            TotInvValuePaisa = record.ConsignmentValuePaisa,
            TransDistance = record.DistanceKm,
            TransporterId = record.TransporterId,
            TransMode = record.Mode is { } m ? (int)m : null,
            VehicleNo = record.VehicleNumber,
            TransDocNo = record.TransportDocNo,
            SchemaStatus = SchemaStatusFlag,
        };
    }

    private static IReadOnlyList<ItemDto> BuildItems(
        Company company, Voucher voucher, IReadOnlyList<RateGroup> groups, long cessTotalPaisa)
    {
        var inventory = voucher.InventoryLines;
        if (inventory.Count == 0)
        {
            // As-voucher (no stock lines): one synthetic item per posted rate group; the whole cess rides the first.
            var list = new List<ItemDto>();
            var slNo = 1;
            foreach (var g in groups)
            {
                list.Add(new ItemDto
                {
                    SlNo = slNo,
                    HsnCd = "",
                    QtyMillis = 0,
                    Unit = "OTH",
                    TaxableAmtPaisa = g.TaxablePaisa,
                    GstRt = g.Rate,
                    CgstAmtPaisa = g.CgstPaisa,
                    SgstAmtPaisa = g.SgstPaisa,
                    IgstAmtPaisa = g.IgstPaisa,
                    CesAmtPaisa = slNo == 1 ? cessTotalPaisa : 0,
                });
                slNo++;
            }
            return list;
        }

        // Item-invoice: attribute each rate group's per-head tax to its stock lines by value share (last line in the
        // group absorbs the remainder so Σ line tax == the group's posted tax exactly — mirrors the INV-01 attribution).
        var singleRate = groups.Count == 1 ? groups[0].Rate : (int?)null;
        var linesByRate = new Dictionary<int, List<VoucherInventoryLine>>();
        foreach (var il in inventory)
        {
            var rate = singleRate ?? LineIntegratedRate(company, il);
            if (!linesByRate.TryGetValue(rate, out var bucket)) linesByRate[rate] = bucket = new List<VoucherInventoryLine>();
            bucket.Add(il);
        }

        var tax = new Dictionary<VoucherInventoryLine, (long Cgst, long Sgst, long Igst)>();
        foreach (var g in groups)
        {
            if (!linesByRate.TryGetValue(g.Rate, out var groupLines) || groupLines.Count == 0) continue;
            var groupValue = groupLines.Sum(l => MoneyCodec.ToPaisa(l.Value));
            long runC = 0, runS = 0, runI = 0;
            for (var i = 0; i < groupLines.Count; i++)
            {
                var value = MoneyCodec.ToPaisa(groupLines[i].Value);
                long c, s, ig;
                if (i == groupLines.Count - 1)
                {
                    c = g.CgstPaisa - runC; s = g.SgstPaisa - runS; ig = g.IgstPaisa - runI;
                }
                else
                {
                    c = Apportion(g.CgstPaisa, value, groupValue);
                    s = Apportion(g.SgstPaisa, value, groupValue);
                    ig = Apportion(g.IgstPaisa, value, groupValue);
                    runC += c; runS += s; runI += ig;
                }
                tax[groupLines[i]] = (c, s, ig);
            }
        }

        // Cess is ring-fenced — apportion the invoice cess total across ALL lines by value share (last absorbs remainder).
        var totalValue = inventory.Sum(l => MoneyCodec.ToPaisa(l.Value));
        var cessByLine = new Dictionary<VoucherInventoryLine, long>();
        long runCess = 0;
        for (var i = 0; i < inventory.Count; i++)
        {
            var value = MoneyCodec.ToPaisa(inventory[i].Value);
            long ces = i == inventory.Count - 1
                ? cessTotalPaisa - runCess
                : (totalValue > 0 ? Apportion(cessTotalPaisa, value, totalValue) : 0);
            if (i != inventory.Count - 1) runCess += ces;
            cessByLine[inventory[i]] = ces;
        }

        var items = new List<ItemDto>();
        var sl = 1;
        foreach (var il in inventory)
        {
            var item = company.FindStockItem(il.StockItemId);
            var (c, s, ig) = tax.TryGetValue(il, out var t) ? t : (0L, 0L, 0L);
            // WI-10 Gap 2 follow-on: the quantity an e-way bill declares is the quantity a checkpoint physically
            // verifies. Emitting the line quantity beside the item's BASE UQC declared "2 NOS" on a consignment
            // in which 24 Nos travel — a verification exposure with no money symptom to catch it. Declare the
            // line's own unit when it maps to a valid UQC, else the base unit with the quantity converted.
            var decl = UqcResolver.Declare(company, il, il.BilledQuantity);
            items.Add(new ItemDto
            {
                SlNo = sl++,
                HsnCd = item?.Gst?.HsnSac ?? item?.HsnSacCode ?? "",
                QtyMillis = (long)Math.Round(decl.Quantity * 1000m, MidpointRounding.AwayFromZero),
                Unit = decl.Code ?? "OTH",
                TaxableAmtPaisa = MoneyCodec.ToPaisa(il.Value),
                GstRt = singleRate ?? LineIntegratedRate(company, il),
                CgstAmtPaisa = c,
                SgstAmtPaisa = s,
                IgstAmtPaisa = ig,
                CesAmtPaisa = cessByLine.TryGetValue(il, out var ces) ? ces : 0,
            });
        }
        return items;
    }

    private static int LineIntegratedRate(Company company, VoucherInventoryLine il) =>
        company.FindStockItem(il.StockItemId)?.Gst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0;

    private static long Apportion(long total, long value, long totalValue) =>
        totalValue == 0 ? 0 : (long)Math.Round((decimal)total * value / totalValue, MidpointRounding.AwayFromZero);

    /// <summary>Per-(integrated rate) posted head totals + taxable, read off the tax lines (ER-9). Excludes the ring-fenced
    /// Cess head and reverse-charge lines (consistent with <see cref="GstReportSupport.InvoiceTaxableValue"/>).</summary>
    private static IReadOnlyList<RateGroup> ReadRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, (long C, long S, long I, long Taxable)>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g);
            var cur = byRate.TryGetValue(rate, out var acc) ? acc : (0L, 0L, 0L, 0L);
            var amount = MoneyCodec.ToPaisa(line.Amount);
            var taxable = Math.Max(cur.Item4, MoneyCodec.ToPaisa(g.TaxableValue));
            cur = g.TaxHead switch
            {
                GstTaxHead.Central => (cur.Item1 + amount, cur.Item2, cur.Item3, taxable),
                GstTaxHead.State => (cur.Item1, cur.Item2 + amount, cur.Item3, taxable),
                GstTaxHead.Integrated => (cur.Item1, cur.Item2, cur.Item3 + amount, taxable),
                _ => cur,
            };
            byRate[rate] = cur;
        }
        return byRate
            .OrderBy(kv => kv.Key)
            .Select(kv => new RateGroup(kv.Key, kv.Value.C, kv.Value.S, kv.Value.I, kv.Value.Taxable))
            .ToList();
    }

    private static byte[] Serialize(object dto)
    {
        var json = JsonSerializer.Serialize(dto, dto.GetType(), Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private readonly record struct RateGroup(int Rate, long CgstPaisa, long SgstPaisa, long IgstPaisa, long TaxablePaisa);

    // ------------------------------------------------------------------ EWB-01 / EWB-02 DTOs (fixed property order)

    private sealed record Ewb01Dto
    {
        [JsonPropertyName("Version")] public required string Version { get; init; }
        [JsonPropertyName("supplyType")] public required string SupplyType { get; init; }
        [JsonPropertyName("subSupplyType")] public required string SubSupplyType { get; init; }
        [JsonPropertyName("docType")] public required string DocType { get; init; }
        [JsonPropertyName("docNo")] public required string DocNo { get; init; }
        [JsonPropertyName("docDate")] public required string DocDate { get; init; }
        [JsonPropertyName("fromGstin")] public string? FromGstin { get; init; }
        [JsonPropertyName("fromStateCode")] public string? FromStateCode { get; init; }
        [JsonPropertyName("toGstin")] public string? ToGstin { get; init; }
        [JsonPropertyName("toStateCode")] public string? ToStateCode { get; init; }
        [JsonPropertyName("shipToGstin")] public string? ShipToGstin { get; init; }
        [JsonPropertyName("itemList")] public required IReadOnlyList<ItemDto> ItemList { get; init; }
        [JsonPropertyName("totInvValue_paisa")] public long TotInvValuePaisa { get; init; }
        [JsonPropertyName("transDistance")] public int TransDistance { get; init; }
        [JsonPropertyName("transporterId")] public string? TransporterId { get; init; }
        [JsonPropertyName("transMode")] public int? TransMode { get; init; }
        [JsonPropertyName("vehicleNo")] public string? VehicleNo { get; init; }
        [JsonPropertyName("transDocNo")] public string? TransDocNo { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record ItemDto
    {
        [JsonPropertyName("SlNo")] public required int SlNo { get; init; }
        [JsonPropertyName("HsnCd")] public required string HsnCd { get; init; }
        [JsonPropertyName("qty_millis")] public long QtyMillis { get; init; }
        [JsonPropertyName("Unit")] public required string Unit { get; init; }
        [JsonPropertyName("taxable_amt_paisa")] public long TaxableAmtPaisa { get; init; }
        [JsonPropertyName("GstRt")] public int GstRt { get; init; }
        [JsonPropertyName("cgst_amt_paisa")] public long CgstAmtPaisa { get; init; }
        [JsonPropertyName("sgst_amt_paisa")] public long SgstAmtPaisa { get; init; }
        [JsonPropertyName("igst_amt_paisa")] public long IgstAmtPaisa { get; init; }
        [JsonPropertyName("ces_amt_paisa")] public long CesAmtPaisa { get; init; }
    }

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
