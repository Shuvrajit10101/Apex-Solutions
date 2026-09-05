using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Io;

/// <summary>
/// Deterministic <b>offline-JSON</b> writer for the composition returns CMP-08 and GSTR-4 (Phase 9 slice 3; RQ-16). A
/// pure, framework-agnostic emitter following the determinism of <see cref="CanonicalJson"/>: <see cref="System.Text.Json"/>
/// only, culture-invariant, fixed property order, no clock/RNG. Money is emitted as <b>integer paisa</b> at the boundary
/// (<see cref="MoneyCodec.ToPaisa"/>, ER-10). The government offline-tool envelope carries <c>gstin</c> + <c>fp</c> (the
/// financial period, <c>MMYYYY</c>) + the summary sections.
/// <para>
/// <b>R7 (A14 to confirm):</b> the exact GSTN CMP-08 / GSTR-4 offline-utility JSON schema (field names / nesting /
/// <c>ret_period</c> format, and whether money is rupee-decimal rather than the integer paisa used here per ER-10) was
/// not fully verifiable at build (the published utilities document the worksheet layout, not the raw JSON keys). This
/// is therefore a <b>faithful structured emission</b>, flagged via <c>schemaStatus</c>; the projection <b>records</b>
/// (Cmp08 / Gstr4) are correct regardless — only the JSON envelope is schema-sensitive and may need a field rename pass.
/// </para>
/// </summary>
public static class GstReturnJson
{
    private const string SchemaStatusFlag = "faithful-structured; GSTN offline-tool JSON keys pending A14 confirmation (R7)";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialises the CMP-08 quarterly statement for <c>[from, to]</c> to deterministic offline JSON bytes
    /// (UTF-8, no BOM). Money is integer paisa.</summary>
    public static byte[] Cmp08(Company company, DateOnly from, DateOnly to)
    {
        var r = Reports.Cmp08.Build(company, from, to);
        var dto = new Cmp08Dto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(to),
            RetPeriod = $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}",
            Applicable = r.Applicable,
            SubType = r.SubType?.ToString(),
            CompositionRateBasisPoints = r.RateBasisPoints,
            TurnoverBasePaisa = MoneyCodec.ToPaisa(r.TurnoverBase),
            OutwardTurnoverCgstPaisa = MoneyCodec.ToPaisa(r.OutwardCgst),
            OutwardTurnoverSgstPaisa = MoneyCodec.ToPaisa(r.OutwardSgst),
            InwardRcmCgstPaisa = MoneyCodec.ToPaisa(r.InwardRcmCgst),
            InwardRcmSgstPaisa = MoneyCodec.ToPaisa(r.InwardRcmSgst),
            InwardRcmIgstPaisa = MoneyCodec.ToPaisa(r.InwardRcmIgst),
            InwardRcmCessPaisa = MoneyCodec.ToPaisa(r.InwardRcmCess),
            PayableCgstPaisa = MoneyCodec.ToPaisa(r.PayableCgst),
            PayableSgstPaisa = MoneyCodec.ToPaisa(r.PayableSgst),
            PayableIgstPaisa = MoneyCodec.ToPaisa(r.PayableIgst),
            PayableCessPaisa = MoneyCodec.ToPaisa(r.PayableCess),
            InterestPaisa = MoneyCodec.ToPaisa(r.Interest),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>Serialises the GSTR-4 annual return for the FY <c>[fyFrom, fyTo]</c> to deterministic offline JSON bytes
    /// (UTF-8, no BOM). Money is integer paisa.</summary>
    public static byte[] Gstr4(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        var r = Reports.Gstr4.Build(company, fyFrom, fyTo);
        var dto = new Gstr4Dto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(fyTo),
            RetPeriod = $"{fyFrom:yyyy-MM-dd}/{fyTo:yyyy-MM-dd}",
            Applicable = r.Applicable,
            SubType = r.SubType?.ToString(),
            Table5Quarters = r.Quarters.Select(q => new Gstr4QuarterDto
            {
                FromDate = $"{q.From:yyyy-MM-dd}",
                ToDate = $"{q.To:yyyy-MM-dd}",
                OutwardTurnoverTaxPaisa = MoneyCodec.ToPaisa(q.OutwardTurnoverTax),
                InwardRcmTaxPaisa = MoneyCodec.ToPaisa(q.InwardRcmTax),
                PayableCgstPaisa = MoneyCodec.ToPaisa(q.PayableCgst),
                PayableSgstPaisa = MoneyCodec.ToPaisa(q.PayableSgst),
                PayableIgstPaisa = MoneyCodec.ToPaisa(q.PayableIgst),
                PayableCessPaisa = MoneyCodec.ToPaisa(q.PayableCess),
            }).ToList(),
            Table4RegisteredValuePaisa = MoneyCodec.ToPaisa(r.Inward.RegisteredValue),
            Table4ReverseChargeValuePaisa = MoneyCodec.ToPaisa(r.Inward.ReverseChargeValue),
            Table4ReverseChargeTaxPaisa = MoneyCodec.ToPaisa(r.Inward.ReverseChargeTax),
            Table4UnregisteredValuePaisa = MoneyCodec.ToPaisa(r.Inward.UnregisteredValue),
            Table4ImportServiceValuePaisa = MoneyCodec.ToPaisa(r.Inward.ImportServiceValue),
            Table6CompositionRateBasisPoints = r.Annual?.RateBasisPoints ?? 0,
            Table6AnnualCompositionTaxPaisa = MoneyCodec.ToPaisa(r.AnnualCompositionTax),
            Table6AnnualRcmTaxPaisa = MoneyCodec.ToPaisa(r.AnnualRcmTax),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>Serialises the GSTR-9 annual return for the FY <c>[fyFrom, fyTo]</c> to deterministic offline JSON bytes
    /// (UTF-8, no BOM). Money is integer paisa (ER-10).</summary>
    public static byte[] Gstr9(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        var r = Reports.Gstr9.Build(company, fyFrom, fyTo);
        var dto = new Gstr9Dto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(fyTo),
            RetPeriod = $"{fyFrom:yyyy-MM-dd}/{fyTo:yyyy-MM-dd}",
            Applicable = r.Applicable,
            Tbl4Cgst = MoneyCodec.ToPaisa(r.Table4Cgst),
            Tbl4Sgst = MoneyCodec.ToPaisa(r.Table4Sgst),
            Tbl4Igst = MoneyCodec.ToPaisa(r.Table4Igst),
            Tbl4Cess = MoneyCodec.ToPaisa(r.Table4Cess),
            Tbl4RcmCgst = MoneyCodec.ToPaisa(r.Table4RcmCgst),
            Tbl4RcmSgst = MoneyCodec.ToPaisa(r.Table4RcmSgst),
            Tbl4RcmIgst = MoneyCodec.ToPaisa(r.Table4RcmIgst),
            Tbl4RcmCess = MoneyCodec.ToPaisa(r.Table4RcmCess),
            Tbl4TaxableValue = MoneyCodec.ToPaisa(r.Table4TaxableValue),
            Tbl4TotalTax = MoneyCodec.ToPaisa(r.Table4TotalTax),
            Tbl5Exempt = MoneyCodec.ToPaisa(r.Table5ExemptNilNonGst),
            Tbl5NTurnover = MoneyCodec.ToPaisa(r.Table5NTurnover),
            Tbl6Cgst = MoneyCodec.ToPaisa(r.Table6Cgst),
            Tbl6Sgst = MoneyCodec.ToPaisa(r.Table6Sgst),
            Tbl6Igst = MoneyCodec.ToPaisa(r.Table6Igst),
            Tbl6Cess = MoneyCodec.ToPaisa(r.Table6Cess),
            Tbl6ItcAvailed = MoneyCodec.ToPaisa(r.Table6ItcAvailed),
            Tbl6HReclaimed = MoneyCodec.ToPaisa(r.Table6HReclaimed),
            Tbl7Rule37 = MoneyCodec.ToPaisa(r.Table7Rule37),
            Tbl7Rule42 = MoneyCodec.ToPaisa(r.Table7Rule42),
            Tbl7Rule43 = MoneyCodec.ToPaisa(r.Table7Rule43),
            Tbl7Section17_5 = MoneyCodec.ToPaisa(r.Table7Section17_5),
            Tbl7Other = MoneyCodec.ToPaisa(r.Table7Other),
            Tbl7Cess = MoneyCodec.ToPaisa(r.Table7Cess),
            Tbl7ItcReversed = MoneyCodec.ToPaisa(r.Table7ItcReversed),
            Tbl8A = MoneyCodec.ToPaisa(r.Table8A),
            Tbl8B = MoneyCodec.ToPaisa(r.Table8B),
            Tbl8D = MoneyCodec.ToPaisa(r.Table8D),
            Tbl9PaidThroughItc = MoneyCodec.ToPaisa(r.Table9PaidThroughItc),
            Tbl9PaidInCash = MoneyCodec.ToPaisa(r.Table9PaidInCash),
            Tbl17Hsn = r.Table17Hsn.Select(h => new Gstr9HsnDto
            {
                HsnSac = h.HsnSac,
                TaxableValuePaisa = MoneyCodec.ToPaisa(h.TaxableValue),
                CgstPaisa = MoneyCodec.ToPaisa(h.Cgst),
                SgstPaisa = MoneyCodec.ToPaisa(h.Sgst),
                IgstPaisa = MoneyCodec.ToPaisa(h.Igst),
            }).ToList(),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>Serialises the GSTR-9A composition annual return for the FY <c>[fyFrom, fyTo]</c> to deterministic offline
    /// JSON bytes (UTF-8, no BOM). Money is integer paisa (ER-10).</summary>
    public static byte[] Gstr9a(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        var r = Reports.Gstr9a.Build(company, fyFrom, fyTo);
        var dto = new Gstr9aDto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(fyTo),
            RetPeriod = $"{fyFrom:yyyy-MM-dd}/{fyTo:yyyy-MM-dd}",
            Applicable = r.Applicable,
            TotalTurnoverPaisa = MoneyCodec.ToPaisa(r.TotalTurnover),
            TaxableTurnoverPaisa = MoneyCodec.ToPaisa(r.TaxableTurnover),
            TaxPaidCgstPaisa = MoneyCodec.ToPaisa(r.TaxPaidCgst),
            TaxPaidSgstPaisa = MoneyCodec.ToPaisa(r.TaxPaidSgst),
            CompositionTaxPaidPaisa = MoneyCodec.ToPaisa(r.CompositionTaxPaid),
            RcmInwardTaxPaisa = MoneyCodec.ToPaisa(r.RcmInwardTax),
            LateFeePaisa = MoneyCodec.ToPaisa(r.LateFee),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>Serialises the GSTR-9C reconciliation statement for the FY <c>[fyFrom, fyTo]</c> to deterministic offline
    /// JSON bytes (UTF-8, no BOM). Money is integer paisa (ER-10). The unreconciled-difference lines are emitted verbatim
    /// (never forced to zero).</summary>
    public static byte[] Gstr9c(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        var r = Reports.Gstr9c.Build(company, fyFrom, fyTo);
        var dto = new Gstr9cDto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(fyTo),
            RetPeriod = $"{fyFrom:yyyy-MM-dd}/{fyTo:yyyy-MM-dd}",
            Applicable = r.Applicable,
            Tbl5ABooksTurnoverPaisa = MoneyCodec.ToPaisa(r.Table5ABooksTurnover),
            Tbl5QReturnTurnoverPaisa = MoneyCodec.ToPaisa(r.Table5QReturnTurnover),
            Tbl5RUnreconciledTurnoverPaisa = MoneyCodec.ToPaisa(r.Table5RUnreconciledTurnover),
            Tbl9TaxPerReturnPaisa = MoneyCodec.ToPaisa(r.Table9TaxPerReturn),
            Tbl9TaxPerBooksPaisa = MoneyCodec.ToPaisa(r.Table9TaxPerBooks),
            Tbl11UnreconciledTaxPaisa = MoneyCodec.ToPaisa(r.Table11UnreconciledTax),
            Tbl12ABooksItcPaisa = MoneyCodec.ToPaisa(r.Table12ABooksItc),
            Tbl12EReturnItcPaisa = MoneyCodec.ToPaisa(r.Table12EReturnItc),
            Tbl12FUnreconciledItcPaisa = MoneyCodec.ToPaisa(r.Table12FUnreconciledItc),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    // ==================================================================================================================
    //  GSTR-1 (outward supplies) and GSTR-3B (summary return) — W2-06 slice (a); census row 6.10 / T1-11.
    //
    //  🔴 R7 / RULING 9 — THESE TWO EMITTERS ARE A DOCUMENTED DIVERGENCE, LABELLED AS OURS.
    //  The GSTN upload-payload schema for GSTR-1 and GSTR-3B is published only behind the AUTHENTICATED GST developer
    //  portal (developer.gst.gov.in/apiportal/taxpayer/returns → "GSTR1 — Save GSTR1 data" → Request Payload). No
    //  unauthenticated CBIC/GSTN source states the key names or their types, so — exactly as for the five writers
    //  above — these follow THIS CLASS'S OWN house convention (integer-paisa money keys under ER-10, the
    //  gstin/fp/ret_period envelope) and carry the same schemaStatus flag. They are NOT claimed to be portal-accepted
    //  and may never be recorded as corpus- or source-verified. What IS locked by test is the arithmetic: every figure
    //  is read straight off the pure Gstr1 / Gstr3b projections, which read the posted GstLineTax and never recompute.
    // ==================================================================================================================

    /// <summary>Serialises the <b>GSTR-1</b> outward-supplies return for <c>[from, to]</c> to deterministic offline JSON
    /// bytes (UTF-8, no BOM). Money is integer paisa (ER-10). Sections: B2B invoices, rate-wise B2C, §34 credit/debit
    /// notes (Table 9B), advances received/adjusted (Tables 11A/11B) and the HSN summary (Table 12).</summary>
    public static byte[] Gstr1(Company company, DateOnly from, DateOnly to)
    {
        var r = Reports.Gstr1.Build(company, from, to);
        var dto = new Gstr1Dto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(to),
            RetPeriod = $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}",
            B2B = r.B2B.Select(b => new Gstr1B2BDto
            {
                Ctin = b.PartyGstin,
                PartyName = b.PartyName,
                Inum = b.InvoiceNumber,
                Idt = $"{b.InvoiceDate:yyyy-MM-dd}",
                Pos = b.PlaceOfSupplyStateCode,
                TxvalPaisa = MoneyCodec.ToPaisa(b.TaxableValue),
                CamtPaisa = MoneyCodec.ToPaisa(b.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(b.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(b.Igst),
                Irn = b.Irn,
            }).ToList(),
            B2Cs = r.B2C.Select(b => new Gstr1B2CsDto
            {
                RateBasisPoints = b.RateBasisPoints,
                TxvalPaisa = MoneyCodec.ToPaisa(b.TaxableValue),
                CamtPaisa = MoneyCodec.ToPaisa(b.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(b.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(b.Igst),
            }).ToList(),
            Cdnr = r.Table9B.Select(n => new Gstr1CdnrDto
            {
                Ntty = n.NoteType.ToString(),
                OriginalInum = n.OriginalInvoiceNumber,
                OriginalIdt = n.OriginalInvoiceDate is { } d ? $"{d:yyyy-MM-dd}" : null,
                Ndt = $"{n.NoteDate:yyyy-MM-dd}",
                Pos = n.PlaceOfSupplyStateCode,
                TxvalPaisa = MoneyCodec.ToPaisa(n.TaxableValue),
                CamtPaisa = MoneyCodec.ToPaisa(n.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(n.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(n.Igst),
                Rsn = n.ReasonCode,
            }).ToList(),
            At = r.Table11A.Select(a => new Gstr1AdvanceDto
            {
                RateBasisPoints = a.RateBasisPoints,
                InterState = a.InterState,
                AdvancePaisa = MoneyCodec.ToPaisa(a.AdvanceReceived),
                CamtPaisa = MoneyCodec.ToPaisa(a.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(a.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(a.Igst),
            }).ToList(),
            Atadj = r.Table11B.Select(a => new Gstr1AdvanceDto
            {
                RateBasisPoints = a.RateBasisPoints,
                InterState = a.InterState,
                AdvancePaisa = MoneyCodec.ToPaisa(a.AdvanceAdjusted),
                CamtPaisa = MoneyCodec.ToPaisa(a.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(a.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(a.Igst),
            }).ToList(),
            Hsn = r.HsnSummary.Select(h => new Gstr1HsnDto
            {
                HsnSac = h.HsnSac,
                Description = h.Description,
                Uqc = h.Uqc,
                Quantity = h.Quantity,
                TxvalPaisa = MoneyCodec.ToPaisa(h.TaxableValue),
                CamtPaisa = MoneyCodec.ToPaisa(h.Cgst),
                SamtPaisa = MoneyCodec.ToPaisa(h.Sgst),
                IamtPaisa = MoneyCodec.ToPaisa(h.Igst),
            }).ToList(),
            NilExemptNonGstPaisa = MoneyCodec.ToPaisa(r.ExemptNilNonGstValue),
            Rcm4BOutwardValuePaisa = MoneyCodec.ToPaisa(r.Rcm4BOutwardValue),
            TotalCgstPaisa = MoneyCodec.ToPaisa(r.TotalCgst),
            TotalSgstPaisa = MoneyCodec.ToPaisa(r.TotalSgst),
            TotalIgstPaisa = MoneyCodec.ToPaisa(r.TotalIgst),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>Serialises the <b>GSTR-3B</b> summary return for <c>[from, to]</c> to deterministic offline JSON bytes
    /// (UTF-8, no BOM). Money is integer paisa (ER-10). Every figure is emitted <b>verbatim from the engine</b> — no
    /// arithmetic is invented here, and a negative net head (a carried-forward credit, DP-9) is emitted as it stands
    /// rather than floored to zero.</summary>
    public static byte[] Gstr3b(Company company, DateOnly from, DateOnly to)
    {
        var r = Reports.Gstr3b.Build(company, from, to);
        var dto = new Gstr3bDto
        {
            Gstin = company.Gst?.Gstin,
            Fp = FinancialPeriod(to),
            RetPeriod = $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}",

            Tbl3_1aTxvalPaisa = MoneyCodec.ToPaisa(r.TaxableOutwardValue),
            Tbl3_1aCamtPaisa = MoneyCodec.ToPaisa(r.OutwardCgst),
            Tbl3_1aSamtPaisa = MoneyCodec.ToPaisa(r.OutwardSgst),
            Tbl3_1aIamtPaisa = MoneyCodec.ToPaisa(r.OutwardIgst),
            Tbl3_1cNilExemptNonGstPaisa = MoneyCodec.ToPaisa(r.ExemptNilNonGstOutward),
            Tbl3_1dRcmCamtPaisa = MoneyCodec.ToPaisa(r.RcmOutwardCgst),
            Tbl3_1dRcmSamtPaisa = MoneyCodec.ToPaisa(r.RcmOutwardSgst),
            Tbl3_1dRcmIamtPaisa = MoneyCodec.ToPaisa(r.RcmOutwardIgst),
            Tbl3_1dRcmCsamtPaisa = MoneyCodec.ToPaisa(r.RcmOutwardCess),

            Tbl4a2ImportServicesIamtPaisa = MoneyCodec.ToPaisa(r.RcmItcImportIgst),
            Tbl4a3RcmOtherCamtPaisa = MoneyCodec.ToPaisa(r.RcmItcOtherCgst),
            Tbl4a3RcmOtherSamtPaisa = MoneyCodec.ToPaisa(r.RcmItcOtherSgst),
            Tbl4a3RcmOtherIamtPaisa = MoneyCodec.ToPaisa(r.RcmItcOtherIgst),
            Tbl4a3RcmOtherCsamtPaisa = MoneyCodec.ToPaisa(r.RcmItcOtherCess),
            Tbl4a5CamtPaisa = MoneyCodec.ToPaisa(r.ItcCgst),
            Tbl4a5SamtPaisa = MoneyCodec.ToPaisa(r.ItcSgst),
            Tbl4a5IamtPaisa = MoneyCodec.ToPaisa(r.ItcIgst),

            Tbl4b1CamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B1Cgst),
            Tbl4b1SamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B1Sgst),
            Tbl4b1IamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B1Igst),
            Tbl4b1CsamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B1Cess),
            Tbl4b2CamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B2Cgst),
            Tbl4b2SamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B2Sgst),
            Tbl4b2IamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B2Igst),
            Tbl4b2CsamtPaisa = MoneyCodec.ToPaisa(r.ItcReversed4B2Cess),
            Tbl4d1CamtPaisa = MoneyCodec.ToPaisa(r.ItcReclaimed4D1Cgst),
            Tbl4d1SamtPaisa = MoneyCodec.ToPaisa(r.ItcReclaimed4D1Sgst),
            Tbl4d1IamtPaisa = MoneyCodec.ToPaisa(r.ItcReclaimed4D1Igst),
            Tbl4d1CsamtPaisa = MoneyCodec.ToPaisa(r.ItcReclaimed4D1Cess),

            Tbl6_1NetCamtPaisa = MoneyCodec.ToPaisa(r.NetCgst),
            Tbl6_1NetSamtPaisa = MoneyCodec.ToPaisa(r.NetSgst),
            Tbl6_1NetIamtPaisa = MoneyCodec.ToPaisa(r.NetIgst),
            SchemaStatus = SchemaStatusFlag,
        };
        return Serialize(dto);
    }

    /// <summary>The financial period as the government <c>MMYYYY</c> string (CMP-08 quarter's end month, GSTR-4 FY-end
    /// month), invariant-culture.</summary>
    private static string FinancialPeriod(DateOnly period) =>
        period.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
        + period.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Serialize(object dto)
    {
        var json = JsonSerializer.Serialize(dto, dto.GetType(), Options);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    private sealed record Cmp08Dto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("applicable")] public bool Applicable { get; init; }
        [JsonPropertyName("comp_sub_type")] public string? SubType { get; init; }
        [JsonPropertyName("comp_rate_bp")] public int CompositionRateBasisPoints { get; init; }
        [JsonPropertyName("turnover_base_paisa")] public long TurnoverBasePaisa { get; init; }
        [JsonPropertyName("tbl3i_out_cgst_paisa")] public long OutwardTurnoverCgstPaisa { get; init; }
        [JsonPropertyName("tbl3i_out_sgst_paisa")] public long OutwardTurnoverSgstPaisa { get; init; }
        [JsonPropertyName("tbl3ii_rcm_cgst_paisa")] public long InwardRcmCgstPaisa { get; init; }
        [JsonPropertyName("tbl3ii_rcm_sgst_paisa")] public long InwardRcmSgstPaisa { get; init; }
        [JsonPropertyName("tbl3ii_rcm_igst_paisa")] public long InwardRcmIgstPaisa { get; init; }
        [JsonPropertyName("tbl3ii_rcm_cess_paisa")] public long InwardRcmCessPaisa { get; init; }
        [JsonPropertyName("tbl3iii_pay_cgst_paisa")] public long PayableCgstPaisa { get; init; }
        [JsonPropertyName("tbl3iii_pay_sgst_paisa")] public long PayableSgstPaisa { get; init; }
        [JsonPropertyName("tbl3iii_pay_igst_paisa")] public long PayableIgstPaisa { get; init; }
        [JsonPropertyName("tbl3iii_pay_cess_paisa")] public long PayableCessPaisa { get; init; }
        [JsonPropertyName("tbl3iv_interest_paisa")] public long InterestPaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record Gstr4QuarterDto
    {
        [JsonPropertyName("from")] public required string FromDate { get; init; }
        [JsonPropertyName("to")] public required string ToDate { get; init; }
        [JsonPropertyName("out_turnover_tax_paisa")] public long OutwardTurnoverTaxPaisa { get; init; }
        [JsonPropertyName("inward_rcm_tax_paisa")] public long InwardRcmTaxPaisa { get; init; }
        [JsonPropertyName("pay_cgst_paisa")] public long PayableCgstPaisa { get; init; }
        [JsonPropertyName("pay_sgst_paisa")] public long PayableSgstPaisa { get; init; }
        [JsonPropertyName("pay_igst_paisa")] public long PayableIgstPaisa { get; init; }
        [JsonPropertyName("pay_cess_paisa")] public long PayableCessPaisa { get; init; }
    }

    private sealed record Gstr4Dto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("applicable")] public bool Applicable { get; init; }
        [JsonPropertyName("comp_sub_type")] public string? SubType { get; init; }
        [JsonPropertyName("tbl5_quarters")] public required IReadOnlyList<Gstr4QuarterDto> Table5Quarters { get; init; }
        [JsonPropertyName("tbl4a_registered_value_paisa")] public long Table4RegisteredValuePaisa { get; init; }
        [JsonPropertyName("tbl4b_rc_value_paisa")] public long Table4ReverseChargeValuePaisa { get; init; }
        [JsonPropertyName("tbl4b_rc_tax_paisa")] public long Table4ReverseChargeTaxPaisa { get; init; }
        [JsonPropertyName("tbl4c_urp_value_paisa")] public long Table4UnregisteredValuePaisa { get; init; }
        [JsonPropertyName("tbl4d_imps_value_paisa")] public long Table4ImportServiceValuePaisa { get; init; }
        [JsonPropertyName("tbl6_comp_rate_bp")] public int Table6CompositionRateBasisPoints { get; init; }
        [JsonPropertyName("tbl6_annual_comp_tax_paisa")] public long Table6AnnualCompositionTaxPaisa { get; init; }
        [JsonPropertyName("tbl6_annual_rcm_tax_paisa")] public long Table6AnnualRcmTaxPaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record Gstr9HsnDto
    {
        [JsonPropertyName("hsn_sac")] public required string HsnSac { get; init; }
        [JsonPropertyName("taxable_value_paisa")] public long TaxableValuePaisa { get; init; }
        [JsonPropertyName("cgst_paisa")] public long CgstPaisa { get; init; }
        [JsonPropertyName("sgst_paisa")] public long SgstPaisa { get; init; }
        [JsonPropertyName("igst_paisa")] public long IgstPaisa { get; init; }
    }

    private sealed record Gstr9Dto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("applicable")] public bool Applicable { get; init; }
        [JsonPropertyName("tbl4_out_cgst_paisa")] public long Tbl4Cgst { get; init; }
        [JsonPropertyName("tbl4_out_sgst_paisa")] public long Tbl4Sgst { get; init; }
        [JsonPropertyName("tbl4_out_igst_paisa")] public long Tbl4Igst { get; init; }
        [JsonPropertyName("tbl4_out_cess_paisa")] public long Tbl4Cess { get; init; }
        [JsonPropertyName("tbl4g_rcm_cgst_paisa")] public long Tbl4RcmCgst { get; init; }
        [JsonPropertyName("tbl4g_rcm_sgst_paisa")] public long Tbl4RcmSgst { get; init; }
        [JsonPropertyName("tbl4g_rcm_igst_paisa")] public long Tbl4RcmIgst { get; init; }
        [JsonPropertyName("tbl4g_rcm_cess_paisa")] public long Tbl4RcmCess { get; init; }
        [JsonPropertyName("tbl4n_taxable_value_paisa")] public long Tbl4TaxableValue { get; init; }
        [JsonPropertyName("tbl4_total_tax_paisa")] public long Tbl4TotalTax { get; init; }
        [JsonPropertyName("tbl5_exempt_paisa")] public long Tbl5Exempt { get; init; }
        [JsonPropertyName("tbl5n_turnover_paisa")] public long Tbl5NTurnover { get; init; }
        [JsonPropertyName("tbl6_itc_cgst_paisa")] public long Tbl6Cgst { get; init; }
        [JsonPropertyName("tbl6_itc_sgst_paisa")] public long Tbl6Sgst { get; init; }
        [JsonPropertyName("tbl6_itc_igst_paisa")] public long Tbl6Igst { get; init; }
        [JsonPropertyName("tbl6_itc_cess_paisa")] public long Tbl6Cess { get; init; }
        [JsonPropertyName("tbl6_itc_availed_paisa")] public long Tbl6ItcAvailed { get; init; }
        [JsonPropertyName("tbl6h_reclaimed_paisa")] public long Tbl6HReclaimed { get; init; }
        [JsonPropertyName("tbl7a_rule37_paisa")] public long Tbl7Rule37 { get; init; }
        [JsonPropertyName("tbl7c_rule42_paisa")] public long Tbl7Rule42 { get; init; }
        [JsonPropertyName("tbl7d_rule43_paisa")] public long Tbl7Rule43 { get; init; }
        [JsonPropertyName("tbl7e_section17_5_paisa")] public long Tbl7Section17_5 { get; init; }
        [JsonPropertyName("tbl7h_other_paisa")] public long Tbl7Other { get; init; }
        [JsonPropertyName("tbl7_cess_paisa")] public long Tbl7Cess { get; init; }
        [JsonPropertyName("tbl7_itc_reversed_paisa")] public long Tbl7ItcReversed { get; init; }
        [JsonPropertyName("tbl8a_itc_2b_paisa")] public long Tbl8A { get; init; }
        [JsonPropertyName("tbl8b_itc_availed_paisa")] public long Tbl8B { get; init; }
        [JsonPropertyName("tbl8d_difference_paisa")] public long Tbl8D { get; init; }
        [JsonPropertyName("tbl9_paid_through_itc_paisa")] public long Tbl9PaidThroughItc { get; init; }
        [JsonPropertyName("tbl9_paid_in_cash_paisa")] public long Tbl9PaidInCash { get; init; }
        [JsonPropertyName("tbl17_hsn")] public required IReadOnlyList<Gstr9HsnDto> Tbl17Hsn { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record Gstr9aDto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("applicable")] public bool Applicable { get; init; }
        [JsonPropertyName("total_turnover_paisa")] public long TotalTurnoverPaisa { get; init; }
        [JsonPropertyName("taxable_turnover_paisa")] public long TaxableTurnoverPaisa { get; init; }
        [JsonPropertyName("tax_paid_cgst_paisa")] public long TaxPaidCgstPaisa { get; init; }
        [JsonPropertyName("tax_paid_sgst_paisa")] public long TaxPaidSgstPaisa { get; init; }
        [JsonPropertyName("comp_tax_paid_paisa")] public long CompositionTaxPaidPaisa { get; init; }
        [JsonPropertyName("rcm_inward_tax_paisa")] public long RcmInwardTaxPaisa { get; init; }
        [JsonPropertyName("late_fee_paisa")] public long LateFeePaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    // ---- GSTR-1 (W2-06) --------------------------------------------------------------------------------------------

    private sealed record Gstr1B2BDto
    {
        [JsonPropertyName("ctin")] public string? Ctin { get; init; }
        [JsonPropertyName("party_name")] public required string PartyName { get; init; }
        [JsonPropertyName("inum")] public required string Inum { get; init; }
        [JsonPropertyName("idt")] public required string Idt { get; init; }
        [JsonPropertyName("pos")] public string? Pos { get; init; }
        [JsonPropertyName("txval_paisa")] public long TxvalPaisa { get; init; }
        [JsonPropertyName("camt_paisa")] public long CamtPaisa { get; init; }
        [JsonPropertyName("samt_paisa")] public long SamtPaisa { get; init; }
        [JsonPropertyName("iamt_paisa")] public long IamtPaisa { get; init; }
        [JsonPropertyName("irn")] public string? Irn { get; init; }
    }

    private sealed record Gstr1B2CsDto
    {
        [JsonPropertyName("rt_bp")] public int RateBasisPoints { get; init; }
        [JsonPropertyName("txval_paisa")] public long TxvalPaisa { get; init; }
        [JsonPropertyName("camt_paisa")] public long CamtPaisa { get; init; }
        [JsonPropertyName("samt_paisa")] public long SamtPaisa { get; init; }
        [JsonPropertyName("iamt_paisa")] public long IamtPaisa { get; init; }
    }

    private sealed record Gstr1CdnrDto
    {
        [JsonPropertyName("ntty")] public required string Ntty { get; init; }
        [JsonPropertyName("orig_inum")] public string? OriginalInum { get; init; }
        [JsonPropertyName("orig_idt")] public string? OriginalIdt { get; init; }
        [JsonPropertyName("ndt")] public required string Ndt { get; init; }
        [JsonPropertyName("pos")] public string? Pos { get; init; }
        [JsonPropertyName("txval_paisa")] public long TxvalPaisa { get; init; }
        [JsonPropertyName("camt_paisa")] public long CamtPaisa { get; init; }
        [JsonPropertyName("samt_paisa")] public long SamtPaisa { get; init; }
        [JsonPropertyName("iamt_paisa")] public long IamtPaisa { get; init; }
        [JsonPropertyName("rsn")] public required string Rsn { get; init; }
    }

    private sealed record Gstr1AdvanceDto
    {
        [JsonPropertyName("rt_bp")] public int RateBasisPoints { get; init; }
        [JsonPropertyName("inter_state")] public bool InterState { get; init; }
        [JsonPropertyName("advance_paisa")] public long AdvancePaisa { get; init; }
        [JsonPropertyName("camt_paisa")] public long CamtPaisa { get; init; }
        [JsonPropertyName("samt_paisa")] public long SamtPaisa { get; init; }
        [JsonPropertyName("iamt_paisa")] public long IamtPaisa { get; init; }
    }

    private sealed record Gstr1HsnDto
    {
        [JsonPropertyName("hsn_sac")] public required string HsnSac { get; init; }
        [JsonPropertyName("desc")] public required string Description { get; init; }
        [JsonPropertyName("uqc")] public string? Uqc { get; init; }
        [JsonPropertyName("qty")] public decimal Quantity { get; init; }
        [JsonPropertyName("txval_paisa")] public long TxvalPaisa { get; init; }
        [JsonPropertyName("camt_paisa")] public long CamtPaisa { get; init; }
        [JsonPropertyName("samt_paisa")] public long SamtPaisa { get; init; }
        [JsonPropertyName("iamt_paisa")] public long IamtPaisa { get; init; }
    }

    private sealed record Gstr1Dto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("b2b")] public required IReadOnlyList<Gstr1B2BDto> B2B { get; init; }
        [JsonPropertyName("b2cs")] public required IReadOnlyList<Gstr1B2CsDto> B2Cs { get; init; }
        [JsonPropertyName("cdnr")] public required IReadOnlyList<Gstr1CdnrDto> Cdnr { get; init; }
        [JsonPropertyName("at")] public required IReadOnlyList<Gstr1AdvanceDto> At { get; init; }
        [JsonPropertyName("atadj")] public required IReadOnlyList<Gstr1AdvanceDto> Atadj { get; init; }
        [JsonPropertyName("hsn")] public required IReadOnlyList<Gstr1HsnDto> Hsn { get; init; }
        [JsonPropertyName("nil_exempt_nongst_paisa")] public long NilExemptNonGstPaisa { get; init; }
        [JsonPropertyName("tbl4b_rcm_outward_value_paisa")] public long Rcm4BOutwardValuePaisa { get; init; }
        [JsonPropertyName("total_cgst_paisa")] public long TotalCgstPaisa { get; init; }
        [JsonPropertyName("total_sgst_paisa")] public long TotalSgstPaisa { get; init; }
        [JsonPropertyName("total_igst_paisa")] public long TotalIgstPaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    // ---- GSTR-3B (W2-06) -------------------------------------------------------------------------------------------

    private sealed record Gstr3bDto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }

        [JsonPropertyName("tbl3_1a_txval_paisa")] public long Tbl3_1aTxvalPaisa { get; init; }
        [JsonPropertyName("tbl3_1a_camt_paisa")] public long Tbl3_1aCamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1a_samt_paisa")] public long Tbl3_1aSamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1a_iamt_paisa")] public long Tbl3_1aIamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1c_nil_exempt_nongst_paisa")] public long Tbl3_1cNilExemptNonGstPaisa { get; init; }
        [JsonPropertyName("tbl3_1d_rcm_camt_paisa")] public long Tbl3_1dRcmCamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1d_rcm_samt_paisa")] public long Tbl3_1dRcmSamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1d_rcm_iamt_paisa")] public long Tbl3_1dRcmIamtPaisa { get; init; }
        [JsonPropertyName("tbl3_1d_rcm_csamt_paisa")] public long Tbl3_1dRcmCsamtPaisa { get; init; }

        [JsonPropertyName("tbl4a2_import_services_iamt_paisa")] public long Tbl4a2ImportServicesIamtPaisa { get; init; }
        [JsonPropertyName("tbl4a3_rcm_other_camt_paisa")] public long Tbl4a3RcmOtherCamtPaisa { get; init; }
        [JsonPropertyName("tbl4a3_rcm_other_samt_paisa")] public long Tbl4a3RcmOtherSamtPaisa { get; init; }
        [JsonPropertyName("tbl4a3_rcm_other_iamt_paisa")] public long Tbl4a3RcmOtherIamtPaisa { get; init; }
        [JsonPropertyName("tbl4a3_rcm_other_csamt_paisa")] public long Tbl4a3RcmOtherCsamtPaisa { get; init; }
        [JsonPropertyName("tbl4a5_camt_paisa")] public long Tbl4a5CamtPaisa { get; init; }
        [JsonPropertyName("tbl4a5_samt_paisa")] public long Tbl4a5SamtPaisa { get; init; }
        [JsonPropertyName("tbl4a5_iamt_paisa")] public long Tbl4a5IamtPaisa { get; init; }

        [JsonPropertyName("tbl4b1_camt_paisa")] public long Tbl4b1CamtPaisa { get; init; }
        [JsonPropertyName("tbl4b1_samt_paisa")] public long Tbl4b1SamtPaisa { get; init; }
        [JsonPropertyName("tbl4b1_iamt_paisa")] public long Tbl4b1IamtPaisa { get; init; }
        [JsonPropertyName("tbl4b1_csamt_paisa")] public long Tbl4b1CsamtPaisa { get; init; }
        [JsonPropertyName("tbl4b2_camt_paisa")] public long Tbl4b2CamtPaisa { get; init; }
        [JsonPropertyName("tbl4b2_samt_paisa")] public long Tbl4b2SamtPaisa { get; init; }
        [JsonPropertyName("tbl4b2_iamt_paisa")] public long Tbl4b2IamtPaisa { get; init; }
        [JsonPropertyName("tbl4b2_csamt_paisa")] public long Tbl4b2CsamtPaisa { get; init; }
        [JsonPropertyName("tbl4d1_camt_paisa")] public long Tbl4d1CamtPaisa { get; init; }
        [JsonPropertyName("tbl4d1_samt_paisa")] public long Tbl4d1SamtPaisa { get; init; }
        [JsonPropertyName("tbl4d1_iamt_paisa")] public long Tbl4d1IamtPaisa { get; init; }
        [JsonPropertyName("tbl4d1_csamt_paisa")] public long Tbl4d1CsamtPaisa { get; init; }

        [JsonPropertyName("tbl6_1_net_camt_paisa")] public long Tbl6_1NetCamtPaisa { get; init; }
        [JsonPropertyName("tbl6_1_net_samt_paisa")] public long Tbl6_1NetSamtPaisa { get; init; }
        [JsonPropertyName("tbl6_1_net_iamt_paisa")] public long Tbl6_1NetIamtPaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }

    private sealed record Gstr9cDto
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; init; }
        [JsonPropertyName("fp")] public required string Fp { get; init; }
        [JsonPropertyName("ret_period")] public required string RetPeriod { get; init; }
        [JsonPropertyName("applicable")] public bool Applicable { get; init; }
        [JsonPropertyName("tbl5a_books_turnover_paisa")] public long Tbl5ABooksTurnoverPaisa { get; init; }
        [JsonPropertyName("tbl5q_return_turnover_paisa")] public long Tbl5QReturnTurnoverPaisa { get; init; }
        [JsonPropertyName("tbl5r_unreconciled_turnover_paisa")] public long Tbl5RUnreconciledTurnoverPaisa { get; init; }
        [JsonPropertyName("tbl9_tax_per_return_paisa")] public long Tbl9TaxPerReturnPaisa { get; init; }
        [JsonPropertyName("tbl9_tax_per_books_paisa")] public long Tbl9TaxPerBooksPaisa { get; init; }
        [JsonPropertyName("tbl11_unreconciled_tax_paisa")] public long Tbl11UnreconciledTaxPaisa { get; init; }
        [JsonPropertyName("tbl12a_books_itc_paisa")] public long Tbl12ABooksItcPaisa { get; init; }
        [JsonPropertyName("tbl12e_return_itc_paisa")] public long Tbl12EReturnItcPaisa { get; init; }
        [JsonPropertyName("tbl12f_unreconciled_itc_paisa")] public long Tbl12FUnreconciledItcPaisa { get; init; }
        [JsonPropertyName("schemaStatus")] public required string SchemaStatus { get; init; }
    }
}
