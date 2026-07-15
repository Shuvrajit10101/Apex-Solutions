using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io;

/// <summary>
/// A <b>pure, deterministic</b> parser for a portal-downloaded GSTR-2B/2A JSON (Phase 9 slice 6; RQ-12; DP-13). It maps
/// the GSTN <c>docdata</c> sections (B2B / B2BA / CDNR / CDNRA / IMPG / IMPGSEZ / ISD) into a neutral
/// <see cref="Gstr2bStatementDto"/> — culture-invariant, ordinal, <b>no clock / no RNG</b>, money as <b>integer paisa</b>
/// at the boundary. The same bytes always yield byte-identical DTOs (a fixed golden), so a malformed or
/// wrong-recipient-GSTIN file <b>fails fast</b> (Parsed = false) rather than importing a partial statement.
/// <para>
/// <b>R7 (A14 to confirm):</b> the exact GSTN 2B JSON key names were not fully verifiable at build; this is a faithful
/// structured read of the documented section shapes (<c>ctin</c>/<c>inum</c>/<c>itcavl</c>/<c>rev</c>/<c>iamt</c>…). The
/// values (paisa, ITC-available bifurcation, supplier-RCM flag, cess) are correct; a later key-rename pass may be needed.
/// </para>
/// </summary>
public static class Gstr2bJsonParser
{
    /// <summary>Parses portal JSON bytes into a neutral statement DTO. Returns <c>Parsed = false</c> (+ an error code /
    /// message, no partial statement) on malformed JSON, a wrong root, or a recipient-GSTIN mismatch.</summary>
    public static Gstr2bImportResult Parse(byte[] json, GstStatementType statementType, string recipientGstin)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(recipientGstin);

        var hash = Convert.ToHexString(SHA256.HashData(json));

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new Gstr2bImportResult(false, null, "MALFORMED_JSON", ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new Gstr2bImportResult(false, null, "BAD_ROOT", "The statement root must be a JSON object.");

            var gstin = Str(root, "gstin");
            if (string.IsNullOrWhiteSpace(gstin))
                return new Gstr2bImportResult(false, null, "NO_GSTIN", "The statement carries no recipient GSTIN.");
            if (!string.Equals(gstin.Trim(), recipientGstin.Trim(), StringComparison.OrdinalIgnoreCase))
                return new Gstr2bImportResult(false, null, "GSTIN_MISMATCH",
                    $"The statement GSTIN '{gstin}' does not match the recipient GSTIN '{recipientGstin}'.");

            string returnPeriod;
            try
            {
                returnPeriod = ParseReturnPeriod(Str(root, "rtnprd"));
            }
            catch (FormatException ex)
            {
                return new Gstr2bImportResult(false, null, "BAD_PERIOD", ex.Message);
            }

            DateOnly? generatedOn;
            try
            {
                generatedOn = ParseDateOpt(Str(root, "gendt"));
            }
            catch (FormatException ex)
            {
                return new Gstr2bImportResult(false, null, "BAD_GENDT", ex.Message);
            }

            long sumIgst = 0, sumCgst = 0, sumSgst = 0, sumCess = 0;
            if (root.TryGetProperty("itcsumm", out var summ) && summ.ValueKind == JsonValueKind.Object)
            {
                // A malformed summary money value must fail fast (Parsed = false) like docdata/gendt/rtnprd — not throw an
                // opaque exception out of Parse (finding #3).
                try
                {
                    sumIgst = Paisa(summ, "igst");
                    sumCgst = Paisa(summ, "cgst");
                    sumSgst = Paisa(summ, "sgst");
                    sumCess = Paisa(summ, "cess");
                }
                catch (FormatException ex)
                {
                    return new Gstr2bImportResult(false, null, "BAD_SUMMARY", ex.Message);
                }
            }

            var lines = new List<Gstr2bStatementLineDto>();
            if (root.TryGetProperty("docdata", out var docdata) && docdata.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    ReadCounterpartySection(docdata, "b2b", "inv", Gstr2bDocType.B2b, lines);
                    ReadCounterpartySection(docdata, "b2ba", "inv", Gstr2bDocType.B2bAmendment, lines);
                    ReadNoteSection(docdata, "cdnr", isAmendment: false, lines);
                    ReadNoteSection(docdata, "cdnra", isAmendment: true, lines);
                    ReadImportSection(docdata, "impg", Gstr2bDocType.ImportOfGoods, lines);
                    ReadImportSection(docdata, "impgsez", Gstr2bDocType.ImportOfGoodsSez, lines);
                    ReadCounterpartySection(docdata, "isd", "doclist", Gstr2bDocType.Isd, lines);
                }
                catch (FormatException ex)
                {
                    return new Gstr2bImportResult(false, null, "BAD_LINE", ex.Message);
                }
            }

            var statement = new Gstr2bStatementDto(
                statementType, returnPeriod, gstin.Trim(), generatedOn, hash, sumIgst, sumCgst, sumSgst, sumCess, lines);
            return new Gstr2bImportResult(true, statement, null, null);
        }
    }

    // ---- section readers ----

    /// <summary>Reads a counterparty-grouped section (b2b / b2ba / isd): each element carries a supplier <c>ctin</c> +
    /// <c>trdnm</c> and an array (<paramref name="docArray"/>) of documents.</summary>
    private static void ReadCounterpartySection(
        JsonElement docdata, string section, string docArray, Gstr2bDocType docType, List<Gstr2bStatementLineDto> into)
    {
        if (!docdata.TryGetProperty(section, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var supplier in arr.EnumerateArray())
        {
            var ctin = Str(supplier, "ctin") ?? "";
            var trdnm = Str(supplier, "trdnm");
            if (!supplier.TryGetProperty(docArray, out var docs) || docs.ValueKind != JsonValueKind.Array) continue;
            foreach (var d in docs.EnumerateArray())
                into.Add(BuildLine(ctin, trdnm, docType,
                    docNumber: Str(d, "inum") ?? Str(d, "docnum") ?? Str(d, "num") ?? "",
                    docDateRaw: Str(d, "idt") ?? Str(d, "docdt") ?? Str(d, "dt"),
                    d));
        }
    }

    /// <summary>Reads a credit/debit-note section (cdnr / cdnra): each element carries a supplier <c>ctin</c> and a
    /// <c>nt</c> array of notes; each note's <c>ntty</c> (C/D) picks CreditNote vs DebitNote.</summary>
    private static void ReadNoteSection(JsonElement docdata, string section, bool isAmendment, List<Gstr2bStatementLineDto> into)
    {
        if (!docdata.TryGetProperty(section, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var supplier in arr.EnumerateArray())
        {
            var ctin = Str(supplier, "ctin") ?? "";
            var trdnm = Str(supplier, "trdnm");
            if (!supplier.TryGetProperty("nt", out var notes) || notes.ValueKind != JsonValueKind.Array) continue;
            foreach (var n in notes.EnumerateArray())
            {
                var isCredit = string.Equals(Str(n, "ntty"), "C", StringComparison.OrdinalIgnoreCase);
                var docType = (isAmendment, isCredit) switch
                {
                    (false, true) => Gstr2bDocType.CreditNote,
                    (false, false) => Gstr2bDocType.DebitNote,
                    (true, true) => Gstr2bDocType.CreditNoteAmendment,
                    (true, false) => Gstr2bDocType.DebitNoteAmendment,
                };
                into.Add(BuildLine(ctin, trdnm, docType,
                    docNumber: Str(n, "nt_num") ?? Str(n, "ntnum") ?? "",
                    docDateRaw: Str(n, "nt_dt") ?? Str(n, "ntdt"),
                    n));
            }
        }
    }

    /// <summary>Reads an import-of-goods section (impg / impgsez): a flat BoE array. Imports have no supplier GSTIN, so a
    /// non-blank sentinel ("IMPG"/port code) is used as the key — an import line never matches a books supplier GSTIN and
    /// lands in the in-portal-only bucket (import IGST is claimed via a separate mechanism).</summary>
    private static void ReadImportSection(JsonElement docdata, string section, Gstr2bDocType docType, List<Gstr2bStatementLineDto> into)
    {
        if (!docdata.TryGetProperty(section, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var boe in arr.EnumerateArray())
        {
            var port = Str(boe, "portcode");
            var ctin = string.IsNullOrWhiteSpace(port) ? "IMPG" : $"IMPG:{port}";
            into.Add(BuildLine(ctin, Str(boe, "trdnm") ?? "Import of goods", docType,
                docNumber: Str(boe, "boenum") ?? Str(boe, "refnum") ?? "",
                docDateRaw: Str(boe, "boedt") ?? Str(boe, "dt"),
                boe));
        }
    }

    /// <summary>Builds one line, summing the per-item tax over the document's <c>itms</c> array (or the document's own
    /// flat tax fields when there is no <c>itms</c> array, as for import BoEs).</summary>
    private static Gstr2bStatementLineDto BuildLine(
        string ctin, string? trdnm, Gstr2bDocType docType, string docNumber, string? docDateRaw, JsonElement d)
    {
        long taxable = 0, igst = 0, cgst = 0, sgst = 0, cess = 0;
        if (d.TryGetProperty("itms", out var itms) && itms.ValueKind == JsonValueKind.Array)
        {
            foreach (var itm in itms.EnumerateArray())
            {
                // The tax fields sit either directly on the item or under "itm_det" (the real 2B nesting).
                var det = itm.TryGetProperty("itm_det", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : itm;
                taxable += Paisa(det, "txval");
                igst += Paisa(det, "iamt");
                cgst += Paisa(det, "camt");
                sgst += Paisa(det, "samt");
                cess += Paisa(det, "csamt");
            }
        }
        else
        {
            // Flat tax fields on the document itself (import BoE).
            taxable = Paisa(d, "txval");
            igst = Paisa(d, "iamt");
            cgst = Paisa(d, "camt");
            sgst = Paisa(d, "samt");
            cess = Paisa(d, "csamt");
        }

        var docDate = ParseDateOpt(docDateRaw) ?? default;
        var itcAvail = !string.Equals(Str(d, "itcavl"), "N", StringComparison.OrdinalIgnoreCase); // absent/Y ⇒ available
        var reason = Str(d, "rsn");
        var rev = string.Equals(Str(d, "rev"), "Y", StringComparison.OrdinalIgnoreCase);
        var pos = Str(d, "pos");

        return new Gstr2bStatementLineDto(
            ctin.Trim(), trdnm, docType, docNumber,
            Gstr2bReconciler.NormaliseDocNo(docNumber), docDate, pos,
            taxable, igst, cgst, sgst, cess, itcAvail, reason, rev);
    }

    // ---- primitives ----

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The paisa value of a decimal-rupee money field (× 100, away-from-zero to the paisa). Accepts a JSON number
    /// or a numeric string; a missing/blank field is 0.</summary>
    private static long Paisa(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        decimal rupees;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                rupees = v.GetDecimal();
                break;
            case JsonValueKind.String:
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out rupees))
                    throw new FormatException($"Money field '{name}' value '{s}' is not a valid decimal.");
                break;
            default:
                return 0;
        }
        return (long)Math.Round(rupees * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts a "MMyyyy" return period to canonical "yyyy-MM".</summary>
    private static string ParseReturnPeriod(string? rtnprd)
    {
        if (string.IsNullOrWhiteSpace(rtnprd)) throw new FormatException("The statement carries no return period (rtnprd).");
        var p = rtnprd.Trim();
        if (p.Length == 6 && int.TryParse(p.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm) &&
            int.TryParse(p.AsSpan(2, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var yyyy) &&
            mm is >= 1 and <= 12)
            return $"{yyyy:D4}-{mm:D2}";
        // Also accept an already-canonical "yyyy-MM".
        if (DateOnly.TryParseExact(p + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return $"{d.Year:D4}-{d.Month:D2}";
        throw new FormatException($"Return period '{rtnprd}' is not a valid MMyyyy value.");
    }

    /// <summary>Parses a portal "dd-MM-yyyy" (or ISO "yyyy-MM-dd") date, or <c>null</c> when blank.</summary>
    private static DateOnly? ParseDateOpt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (DateOnly.TryParseExact(s, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        throw new FormatException($"Date '{raw}' is not a valid dd-MM-yyyy or yyyy-MM-dd value.");
    }
}

/// <summary>The deterministic parse output for a GSTR-2B/2A statement (Phase 9 slice 6) — a neutral, id-free, clock-free
/// DTO the import service materialises into a domain <see cref="Gstr2bSnapshot"/>. Money is integer paisa.</summary>
public sealed record Gstr2bStatementDto(
    GstStatementType StatementType, string ReturnPeriod, string RecipientGstin, DateOnly? GeneratedOn,
    string SourceFileHash, long SummaryIgstPaisa, long SummaryCgstPaisa, long SummarySgstPaisa, long SummaryCessPaisa,
    IReadOnlyList<Gstr2bStatementLineDto> Lines);

/// <summary>One parsed inward-supply record (Phase 9 slice 6). Money is integer paisa; the doc-no is carried verbatim +
/// pre-normalised for the match key.</summary>
public sealed record Gstr2bStatementLineDto(
    string SupplierGstin, string? SupplierTradeName, Gstr2bDocType DocType, string DocNumber, string? DocNumberNorm,
    DateOnly DocDate, string? PosStateCode, long TaxableValuePaisa, long IgstPaisa, long CgstPaisa, long SgstPaisa,
    long CessPaisa, bool ItcAvailable, string? ItcUnavailableReason, bool ReverseCharge);
