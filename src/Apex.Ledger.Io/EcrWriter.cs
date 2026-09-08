using System.Globalization;
using System.Text;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="PfEcrReturn"/> to the EPFO <b>ECR 2.0</b> offline flat-file (Phase 8 slice 4; catalog
/// §14). The layout is the <c>#~#</c> (hash-tilde-hash)-delimited, one-member-per-line text format the Unified
/// EPFO portal consumes: <b>member detail lines ONLY</b>, each carrying the 11 ECR fields in order and keyed by a
/// 12-digit UAN. The establishment challan (A/c 1 / 2 / 10 / 21 / 22) is <b>not</b> embedded in the file — the EPFO
/// portal auto-generates it from the uploaded member lines, and a non-member trailer line would fail the portal's
/// per-line ECR validation; the challan totals are surfaced separately on the report (<see cref="PfEcrReturn.Totals"/>).
/// This mirrors the deterministic, byte-stable discipline of <see cref="FvuWriter"/>/<see cref="CsvWriter"/>:
/// integers only (the ECR carries no paisa), invariant-culture formatting, no clock, no RNG, and every free-text
/// field de-branded (ER-11) so the file can never leak a third-party accounting brand.
/// <para>
/// This produces the upload file offline; it does not perform any online EPFO portal upload (project decision D4).
/// The member lines are emitted in the return's already-deterministic order; the file has no trailing empty line.
/// </para>
/// </summary>
public static class EcrWriter
{
    /// <summary>The ECR 2.0 field delimiter (<c>#~#</c>) and the record separator (LF).</summary>
    private const string Delimiter = "#~#";
    private const string RecordSeparator = "\n";

    /// <summary>Serializes <paramref name="ecr"/> to ECR 2.0 flat-file bytes (UTF-8, no BOM) — <b>member detail lines
    /// only</b>, one per member (the establishment challan totals are the portal's job, never a file line). Pure,
    /// deterministic and byte-stable for a fixed return.</summary>
    public static byte[] Write(PfEcrReturn ecr)
    {
        ArgumentNullException.ThrowIfNull(ecr);

        var sb = new StringBuilder();

        // ---- Member detail lines: the 11 ECR fields in order (integers only). No challan trailer — the EPFO portal
        //      derives the challan (A/c 1/2/10/21/22) from these lines on upload; an embedded non-member trailer
        //      would be rejected by the per-line ECR validation.
        foreach (var m in ecr.Members)
            WriteRecord(sb,
                // 🔴 RULING 18: the UAN and the member's legal name are the EPF member's OWN identity, matched
                // against each other by EPFO. Both go through Name() — delimiter-safed, never de-branded.
                Name(m.Uan), Name(m.Name),
                Int(m.GrossWages), Int(m.EpfWages), Int(m.EpsWages), Int(m.EdliWages),
                Int(m.EmployeeShareEpf), Int(m.EpsContribution), Int(m.EmployerShareEpf),
                Int(m.NcpDays), Int(m.RefundOfAdvances));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- field encoders (invariant, de-branded, delimiter-safe, formula-guarded) ----

    /// <summary>
    /// De-brands (ER-11), then strips the delimiter/record separators so a stray token in a user field can never
    /// corrupt the record framing, then applies <see cref="SpreadsheetFormulaGuard"/>. Deterministic; no culture
    /// leak.
    ///
    /// <para><b>Why the formula guard is here even though this file is <c>.txt</c> and not <c>.csv</c>.</b> The ECR
    /// is <c>#~#</c>-delimited, so a spreadsheet will not auto-split it on a double-click — the risk is lower than
    /// the product's real CSVs and it is ranked as such. But an operator checking an upload file routinely opens it
    /// through Excel's Text Import Wizard, and a cell whose content begins <c>= + - @</c> is evaluated on import
    /// exactly as it would be from a <c>.csv</c>. The guard costs one call and cannot fire on real data (no legal
    /// name or UAN begins with a formula trigger), so the honest trade is to apply it rather than to rank the risk
    /// and leave it open.</para>
    ///
    /// <para>🔴 <b>It runs LAST, after the replacement, and that ordering is load-bearing</b> — the replacement can
    /// expose a trigger that was not first before (<c>"#~#=cmd…"</c> becomes <c>" =cmd…"</c>). Numbers never come
    /// through here: <see cref="Int"/> is a separate encoder, so a negative figure cannot collect an apostrophe and
    /// stop being a number.</para>
    /// </summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = Debrand.Text(value);
        return SpreadsheetFormulaGuard.Neutralize(
            cleaned.Replace(Delimiter, " ").Replace('\r', ' ').Replace('\n', ' '));
    }

    /// <summary>
    /// 🔴 <b>RULING 18 — an EPF MEMBER'S own identity (name or UAN), delimiter-safed but NEVER de-branded.</b> The
    /// member is the person this return is filed about, and EPFO matches the name against the 12-digit UAN.
    /// <see cref="Text"/> strips a case-insensitive vendor token, so a real member whose legal name carries that
    /// token was filed under a name that is not theirs — a name-vs-UAN mismatch in a statutory return, not a
    /// cosmetic edit. The record-framing guard STAYS: it is a property of the FILE FORMAT, and a stray
    /// <c>#~#</c> or newline in a name would break the per-line ECR validation and the portal would reject the
    /// upload. Pinned by <c>EcrWriterTests</c> with a fixture name that actually contains the delimiter.
    ///
    /// <para>The formula guard runs here too, and last, for the reason given on <see cref="Text"/>. It PREFIXES and
    /// never rewrites, so the member's own name still reaches EPFO verbatim after the apostrophe.</para>
    /// </summary>
    private static string Name(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return SpreadsheetFormulaGuard.Neutralize(
            value.Replace(Delimiter, " ").Replace('\r', ' ').Replace('\n', ' '));
    }

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void WriteRecord(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(Delimiter);
            sb.Append(fields[i]);
        }
        sb.Append(RecordSeparator);
    }
}
