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
}
