using System.Globalization;
using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Best-effort CSV import for <b>flat</b> masters and vouchers (§DP-5). Unlike the JSON/XML canonical formats
/// (which round-trip the full aggregate losslessly), CSV is the human-authored/interchange path: a section is
/// selected by a <c>#section</c> directive line, a header row names the columns, and each subsequent row is one
/// record referencing masters <b>by name</b> (not by id). It is RFC-4180-parsed (quoted fields, doubled quotes,
/// CRLF or LF records, optional UTF-8 BOM). It never throws on bad data — a malformed row appends a per-row
/// message to <see cref="CsvImportResult.Errors"/> and is skipped, so the engine stage can reject-batch (RQ-21).
/// <para>
/// The supported sections and their columns (case-insensitive header names):
/// <list type="bullet">
///   <item><c>#groups</c>: Name, Nature, Under(optional)</item>
///   <item><c>#ledgers</c>: Name, Under, OpeningBalance(rupees, optional), OpeningSide(Debit/Credit, optional)</item>
///   <item><c>#vouchers</c>: one row per <b>line</b> keyed by VoucherRef — Date, Type, VoucherRef, Ledger, Amount(rupees), DrCr, Narration(optional)</item>
/// </list>
/// A voucher is assembled by grouping consecutive rows sharing a <c>VoucherRef</c>; its date/type/narration come
/// from the group's first row. Money is authored in <b>rupees</b> here (the human column) and converted to the
/// exact paisa the engine stage posts. Anything richer (GST splits, item-invoice lines, bill allocations) is
/// out of CSV scope and belongs in the JSON/XML canonical path.
/// </para>
/// </summary>
public static class CsvImport
{
    public static CsvImportResult Parse(byte[] bytes)
    {
        var result = new CsvImportResult();
        if (bytes is null || bytes.Length == 0)
        {
            result.Errors.Add("CSV import document is empty.");
            return result;
        }

        string text = DecodeUtf8(bytes);
        var records = CsvReader.ReadAll(text);

        string? section = null;
        string[]? header = null;
        int lineNo = 0;
        // Buffer voucher line-rows by VoucherRef so multi-line vouchers assemble in order.
        var voucherOrder = new List<string>();
        var voucherRows = new Dictionary<string, List<(int Line, IReadOnlyList<string> Fields)>>(StringComparer.Ordinal);

        foreach (var fields in records)
        {
            lineNo++;
            if (fields.Count == 0 || (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0])))
                continue; // blank line

            string first = fields[0].Trim();
            if (first.StartsWith('#'))
            {
                section = first.TrimStart('#').Trim().ToLowerInvariant();
                header = null;
                continue;
            }

            if (section is null)
            {
                result.Errors.Add($"Line {lineNo}: data before any #section directive.");
                continue;
            }

            if (header is null)
            {
                header = fields.Select(f => f.Trim()).ToArray();
                continue;
            }

            switch (section)
            {
                case "groups": ParseGroup(result, header, fields, lineNo); break;
                case "ledgers": ParseLedger(result, header, fields, lineNo); break;
                case "vouchers":
                    var refName = Get(header, fields, "VoucherRef") ?? Get(header, fields, "Ref") ?? $"__auto{lineNo}";
                    if (!voucherRows.TryGetValue(refName, out var list))
                    {
                        list = new List<(int, IReadOnlyList<string>)>();
                        voucherRows[refName] = list;
                        voucherOrder.Add(refName);
                    }
                    list.Add((lineNo, fields));
                    break;
                default:
                    result.Errors.Add($"Line {lineNo}: unknown #section '{section}'.");
                    break;
            }

            // Voucher assembly needs the header captured for the voucher rows, so remember it.
            if (section == "vouchers") result.VoucherHeader ??= header;
        }

        AssembleVouchers(result, voucherOrder, voucherRows);
        return result;
    }

    private static void ParseGroup(CsvImportResult r, string[] h, IReadOnlyList<string> f, int line)
    {
        var name = Get(h, f, "Name");
        if (string.IsNullOrWhiteSpace(name)) { r.Errors.Add($"Line {line}: group Name is required."); return; }
        r.Groups.Add(new CsvGroup(name.Trim(), Get(h, f, "Nature")?.Trim(), Get(h, f, "Under")?.Trim()));
    }

    private static void ParseLedger(CsvImportResult r, string[] h, IReadOnlyList<string> f, int line)
    {
        var name = Get(h, f, "Name");
        if (string.IsNullOrWhiteSpace(name)) { r.Errors.Add($"Line {line}: ledger Name is required."); return; }
        var under = Get(h, f, "Under")?.Trim();
        if (string.IsNullOrWhiteSpace(under)) { r.Errors.Add($"Line {line}: ledger '{name}' needs an Under group."); return; }

        long? openingPaisa = null;
        var openingText = Get(h, f, "OpeningBalance");
        if (!string.IsNullOrWhiteSpace(openingText))
        {
            if (!TryRupeesToPaisa(openingText, out var p))
            { r.Errors.Add($"Line {line}: ledger '{name}' OpeningBalance '{openingText}' is not a valid amount."); return; }
            openingPaisa = p;
        }
        var side = Get(h, f, "OpeningSide")?.Trim();
        bool openingIsDebit = !string.Equals(side, "Credit", StringComparison.OrdinalIgnoreCase);
        r.Ledgers.Add(new CsvLedger(name.Trim(), under, openingPaisa ?? 0, openingIsDebit));
    }

    private static void AssembleVouchers(
        CsvImportResult r, List<string> order,
        Dictionary<string, List<(int Line, IReadOnlyList<string> Fields)>> rows)
    {
        var h = r.VoucherHeader;
        if (h is null) return;

        foreach (var refName in order)
        {
            var group = rows[refName];
            var first = group[0];
            var date = Get(h, first.Fields, "Date")?.Trim();
            var type = Get(h, first.Fields, "Type")?.Trim();
            var narration = Get(h, first.Fields, "Narration")?.Trim();

            bool bad = false;
            if (string.IsNullOrWhiteSpace(date) ||
                !DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            { r.Errors.Add($"Voucher '{refName}' (line {first.Line}): Date '{date}' is not a valid ISO date."); bad = true; }
            if (string.IsNullOrWhiteSpace(type))
            { r.Errors.Add($"Voucher '{refName}' (line {first.Line}): Type is required."); bad = true; }

            var lines = new List<CsvVoucherLine>();
            foreach (var (lineNo, fields) in group)
            {
                var ledger = Get(h, fields, "Ledger")?.Trim();
                var amount = Get(h, fields, "Amount");
                var drcr = Get(h, fields, "DrCr")?.Trim();
                if (string.IsNullOrWhiteSpace(ledger))
                { r.Errors.Add($"Voucher '{refName}' (line {lineNo}): Ledger is required."); bad = true; continue; }
                if (!TryRupeesToPaisa(amount, out var paisa))
                { r.Errors.Add($"Voucher '{refName}' (line {lineNo}): Amount '{amount}' is not a valid amount."); bad = true; continue; }
                bool isDebit = string.Equals(drcr, "Debit", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(drcr, "Dr", StringComparison.OrdinalIgnoreCase);
                if (!isDebit && !(string.Equals(drcr, "Credit", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(drcr, "Cr", StringComparison.OrdinalIgnoreCase)))
                { r.Errors.Add($"Voucher '{refName}' (line {lineNo}): DrCr '{drcr}' must be Debit/Credit."); bad = true; continue; }
                lines.Add(new CsvVoucherLine(ledger, paisa, isDebit));
            }

            if (bad) continue;
            r.Vouchers.Add(new CsvVoucher(refName, date!, type!, narration, lines));
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string? Get(string[] header, IReadOnlyList<string> fields, string column)
    {
        for (int i = 0; i < header.Length; i++)
            if (string.Equals(header[i], column, StringComparison.OrdinalIgnoreCase))
                return i < fields.Count ? fields[i] : null;
        return null;
    }

    /// <summary>Parses a rupees string to exact integer paisa (rounding to the paisa away-from-zero).</summary>
    private static bool TryRupeesToPaisa(string? rupees, out long paisa)
    {
        paisa = 0;
        if (string.IsNullOrWhiteSpace(rupees)) return false;
        var cleaned = rupees.Trim().Replace(",", "");
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return false;
        paisa = (long)decimal.Round(d * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        // Strip a leading UTF-8 BOM if present (our own CsvWriter emits one).
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        return new UTF8Encoding(false).GetString(bytes);
    }
}

// ------------------------------------------------------------------ flat CSV model + result

public sealed record CsvGroup(string Name, string? Nature, string? Under);
public sealed record CsvLedger(string Name, string Under, long OpeningBalancePaisa, bool OpeningIsDebit);
public sealed record CsvVoucherLine(string Ledger, long AmountPaisa, bool IsDebit);
public sealed record CsvVoucher(string Ref, string Date, string Type, string? Narration, IReadOnlyList<CsvVoucherLine> Lines);

/// <summary>The parsed flat CSV import + any per-row errors. A non-empty <see cref="Errors"/> means reject-batch.</summary>
public sealed class CsvImportResult
{
    public List<CsvGroup> Groups { get; } = new();
    public List<CsvLedger> Ledgers { get; } = new();
    public List<CsvVoucher> Vouchers { get; } = new();
    public List<string> Errors { get; } = new();

    /// <summary>Captured header of the #vouchers section (internal to assembly).</summary>
    internal string[]? VoucherHeader { get; set; }

    public bool HasErrors => Errors.Count > 0;
}
