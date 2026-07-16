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
/// no RNG</b>, money as <b>integer paisa</b> at the boundary (<see cref="MoneyCodec.ToPaisa"/>, ER-10), UTF-8 no BOM,
/// de-branded (ER-11). It builds the <b>request</b> payload the IRP turns into an IRN — it carries <b>no IRN and no
/// signed QR</b> (those only ever arrive inbound, ER-5). Values are read off the <b>posted</b> tax lines
/// (<see cref="GstLineTax"/>), never recomputed (ER-9).
/// <para>
/// <b>R7 (A14 to confirm):</b> the exact NIC INV-01 v1.1 JSON key names / nesting were not fully verifiable at build; the
/// schema version is pinned (<see cref="SchemaVersion"/>) and the emission flagged via <c>schemaStatus</c>. This is a
/// faithful structured emission; the field names may need a rename pass, but the values (paisa, uppercased doc-no,
/// GstRt-accepts-40, trending-to-0 cess) are correct.
/// </para>
/// </summary>
public static class EInvoiceJson
{
    private const string SchemaVersion = "1.1"; // NIC INV-01 v1.1
    private const string SchemaStatusFlag =
        "faithful-structured; NIC INV-01 v1.1 JSON keys pending A14 confirmation (R7)";

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
    /// Money is integer paisa; <c>DocDtls.No</c> is the <b>uppercased</b> document number.</summary>
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

        var items = BuildItems(company, voucher, groups, cessTotalPaisa);

        return new Inv01Dto
        {
            Version = SchemaVersion,
            TranDtls = new TranDtlsDto
            {
                SupTyp = SupplyTypeCode(category),
                RegRev = category == EInvoiceSupplyCategory.RcmSupplierLiable ? "Y" : "N",
            },
            DocDtls = new DocDtlsDto
            {
                Typ = docType,
                No = EInvoiceService.DocumentNumberOf(voucher),
                Dt = $"{voucher.Date:yyyy-MM-dd}",
            },
            SellerDtls = new PartyDtlsDto { Gstin = gst.Gstin, StateCode = gst.HomeStateCode },
            BuyerDtls = new PartyDtlsDto { Gstin = partyGst?.Gstin, StateCode = partyGst?.StateCode ?? gst.HomeStateCode },
            ItemList = items,
            ValDtls = new ValDtlsDto
            {
                AssValPaisa = assessablePaisa,
                CgstValPaisa = cgstPaisa,
                SgstValPaisa = sgstPaisa,
                IgstValPaisa = igstPaisa,
                CesValPaisa = cessTotalPaisa,
                TotInvValPaisa = assessablePaisa + cgstPaisa + sgstPaisa + igstPaisa + cessTotalPaisa,
            },
            SchemaStatus = SchemaStatusFlag,
        };
    }

    private static string SupplyTypeCode(EInvoiceSupplyCategory category) => category switch
    {
        EInvoiceSupplyCategory.Export => "EXPWP",
        EInvoiceSupplyCategory.SezWithPayment => "SEZWP",
        EInvoiceSupplyCategory.SezWithoutPayment => "SEZWOP",
        EInvoiceSupplyCategory.DeemedExport => "DEXP",
        _ => "B2B", // Regular + RcmSupplierLiable (the RCM flag rides RegRev = Y)
    };

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
                    UnitPricePaisa = g.TaxablePaisa,
                    TotAmtPaisa = g.TaxablePaisa,
                    AssAmtPaisa = g.TaxablePaisa,
                    GstRt = g.Rate,
                    CgstAmtPaisa = g.CgstPaisa,
                    SgstAmtPaisa = g.SgstPaisa,
                    IgstAmtPaisa = g.IgstPaisa,
                    CesRt = 0,
                    CesAmtPaisa = slNo == 1 ? cessTotalPaisa : 0,
                    CesNonAdvlAmtPaisa = 0,
                });
                slNo++;
            }
            return list;
        }

        // Item-invoice: attribute each rate group's per-head tax to its stock lines by value share (last line in the
        // group absorbs the remainder so Σ line tax == the group's posted tax exactly — mirrors Gstr1's HSN attribution).
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

        // Cess is ring-fenced (its own column) — apportion the invoice cess total across ALL lines by value share.
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
            var valuePaisa = MoneyCodec.ToPaisa(il.Value);
            items.Add(new ItemDto
            {
                SlNo = sl++,
                HsnCd = item?.Gst?.HsnSac ?? item?.HsnSacCode ?? "",
                QtyMillis = (long)Math.Round(il.BilledQuantity * 1000m, MidpointRounding.AwayFromZero),
                Unit = company.FindUnit(item?.BaseUnitId ?? Guid.Empty)?.UnitQuantityCode ?? "OTH",
                UnitPricePaisa = MoneyCodec.ToPaisa(il.Rate),
                TotAmtPaisa = valuePaisa,
                AssAmtPaisa = valuePaisa,
                GstRt = singleRate ?? LineIntegratedRate(company, il),
                CgstAmtPaisa = c,
                SgstAmtPaisa = s,
                IgstAmtPaisa = ig,
                CesRt = 0,
                CesAmtPaisa = cessByLine.TryGetValue(il, out var ces) ? ces : 0,
                CesNonAdvlAmtPaisa = 0,
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

    private readonly record struct RateGroup(int Rate, long CgstPaisa, long SgstPaisa, long IgstPaisa, long TaxablePaisa);

    // ------------------------------------------------------------------ INV-01 DTOs (fixed property order)

    private sealed record Inv01Dto
    {
        [JsonPropertyName("Version")] public required string Version { get; init; }
        [JsonPropertyName("TranDtls")] public required TranDtlsDto TranDtls { get; init; }
        [JsonPropertyName("DocDtls")] public required DocDtlsDto DocDtls { get; init; }
        [JsonPropertyName("SellerDtls")] public required PartyDtlsDto SellerDtls { get; init; }
        [JsonPropertyName("BuyerDtls")] public required PartyDtlsDto BuyerDtls { get; init; }
        [JsonPropertyName("ItemList")] public required IReadOnlyList<ItemDto> ItemList { get; init; }
        [JsonPropertyName("ValDtls")] public required ValDtlsDto ValDtls { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record TranDtlsDto
    {
        [JsonPropertyName("SupTyp")] public required string SupTyp { get; init; }
        [JsonPropertyName("RegRev")] public required string RegRev { get; init; }
    }

    private sealed record DocDtlsDto
    {
        [JsonPropertyName("Typ")] public required string Typ { get; init; }
        [JsonPropertyName("No")] public required string No { get; init; }
        [JsonPropertyName("Dt")] public required string Dt { get; init; }
    }

    private sealed record PartyDtlsDto
    {
        [JsonPropertyName("Gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("StateCode")] public string? StateCode { get; init; }
    }

    private sealed record ItemDto
    {
        [JsonPropertyName("SlNo")] public required int SlNo { get; init; }
        [JsonPropertyName("HsnCd")] public required string HsnCd { get; init; }
        [JsonPropertyName("qty_millis")] public long QtyMillis { get; init; }
        [JsonPropertyName("Unit")] public required string Unit { get; init; }
        [JsonPropertyName("unit_price_paisa")] public long UnitPricePaisa { get; init; }
        [JsonPropertyName("tot_amt_paisa")] public long TotAmtPaisa { get; init; }
        [JsonPropertyName("ass_amt_paisa")] public long AssAmtPaisa { get; init; }
        [JsonPropertyName("GstRt")] public int GstRt { get; init; }
        [JsonPropertyName("cgst_amt_paisa")] public long CgstAmtPaisa { get; init; }
        [JsonPropertyName("sgst_amt_paisa")] public long SgstAmtPaisa { get; init; }
        [JsonPropertyName("igst_amt_paisa")] public long IgstAmtPaisa { get; init; }
        [JsonPropertyName("CesRt")] public int CesRt { get; init; }
        [JsonPropertyName("ces_amt_paisa")] public long CesAmtPaisa { get; init; }
        [JsonPropertyName("ces_nonadvl_amt_paisa")] public long CesNonAdvlAmtPaisa { get; init; }
    }

    private sealed record ValDtlsDto
    {
        [JsonPropertyName("ass_val_paisa")] public long AssValPaisa { get; init; }
        [JsonPropertyName("cgst_val_paisa")] public long CgstValPaisa { get; init; }
        [JsonPropertyName("sgst_val_paisa")] public long SgstValPaisa { get; init; }
        [JsonPropertyName("igst_val_paisa")] public long IgstValPaisa { get; init; }
        [JsonPropertyName("ces_val_paisa")] public long CesValPaisa { get; init; }
        [JsonPropertyName("tot_inv_val_paisa")] public long TotInvValPaisa { get; init; }
    }
}
