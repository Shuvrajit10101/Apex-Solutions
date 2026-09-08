using System.Globalization;
using System.Text;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes an <see cref="EsiContributionReturn"/> to the ESIC <b>monthly contribution</b> offline file (Phase 8
/// slice 5; catalog §14). The layout is one comma-delimited line per Insured Person, carrying the ESIC fields in
/// order: <b>IP Number · IP Name · No. of Days · Total Monthly Wages · Reason for 0 wages · Last Working Day</b>.
/// The legacy ESIC portal accepts an Excel (.xls) template; this emits the same tabular data as a clean, portable
/// delimited file offline (project decision — no online upload). It mirrors the deterministic, byte-stable
/// discipline of <see cref="EcrWriter"/>/<see cref="CsvWriter"/>: integers only (the file carries no paisa),
/// invariant-culture formatting, no clock, no RNG, and every free-text field de-branded (ER-11) + delimiter-safe so
/// a stray token in a user field can never corrupt the record framing + put through
/// <see cref="SpreadsheetFormulaGuard"/>, because this one is written with a <c>.csv</c> extension and is opened in
/// a spreadsheet before it is uploaded. Rows are emitted in the return's
/// already-deterministic order (IP number then name); the file has no trailing empty line.
/// </summary>
public static class EsiContributionWriter
{
    private const char Delimiter = ',';
    private const string RecordSeparator = "\n";

    /// <summary>Serializes <paramref name="esi"/> to monthly-contribution file bytes (UTF-8, no BOM) — one IP-detail
    /// line per row. Pure, deterministic and byte-stable for a fixed return.</summary>
    public static byte[] Write(EsiContributionReturn esi)
    {
        ArgumentNullException.ThrowIfNull(esi);

        var sb = new StringBuilder();
        foreach (var r in esi.Rows)
            WriteRecord(sb,
                // 🔴 RULING 18: the IP number and the Insured Person's legal name are that person's OWN identity,
                // matched against each other by ESIC — Name(), delimiter-safed but never de-branded. The reason
                // code and the date are OURS and keep Text().
                Name(r.IpNumber), Name(r.IpName), Int(r.NoOfDays), Int(r.TotalMonthlyWages),
                Text(r.ReasonForZeroWages), Text(r.LastWorkingDay));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- field encoders (invariant, de-branded, delimiter-safe, formula-guarded) ----

    /// <summary>
    /// 🔴 <b>THIS FILE IS WRITTEN AS <c>.csv</c> AND A CLERK OPENS IT, so the spreadsheet-formula-injection guard
    /// (OWASP "CSV injection") applies here exactly as it does to the product's other CSV exports.</b>
    /// <c>EsiContributionReportViewModel</c> writes these bytes to <c>&lt;employer code&gt;_yyyy_MM.csv</c>; the
    /// operator checks the file before uploading it to ESIC, and a double-click opens it in a spreadsheet, where a
    /// field beginning <c>= + - @</c> is EXECUTED. The IP NAME and IP NUMBER on every line are user-typed. Before
    /// this guard, an Insured Person named <c>=cmd|'/c calc'!A1</c> ran on open.
    ///
    /// <para>🔴 <b>The guard runs LAST here, and that ordering is the opposite of the quoting exporters' — for a
    /// reason.</b> Where a field is QUOTED, neutralising first is required so the <c>'</c> lands inside the quotes.
    /// This writer has no quoting: it makes the field framing-safe by REPLACING the delimiter and CR/LF with a
    /// space, and that replacement can expose a trigger that was not first before — <c>",=cmd|'/c calc'!A1"</c>
    /// becomes <c>" =cmd|'/c calc'!A1"</c>, which the guard's leading-space skip catches but only if it runs
    /// afterwards. Neutralising first would return that field untouched and ship the formula. Numbers are NOT
    /// routed through here: <see cref="Int"/> is a separate encoder, so a negative figure can never collect an
    /// apostrophe and stop being a number.</para>
    /// </summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = Debrand.Text(value);
        return SpreadsheetFormulaGuard.Neutralize(
            cleaned.Replace(Delimiter, ' ').Replace('\r', ' ').Replace('\n', ' '));
    }

    /// <summary>
    /// 🔴 <b>RULING 18 — an INSURED PERSON'S own identity (name or IP number), delimiter-safed but NEVER
    /// de-branded.</b> ESIC matches the IP name against the IP number, so stripping a case-insensitive vendor
    /// token out of a real person's legal name files them under a name that is not theirs. The record-framing
    /// guard STAYS: a stray comma or newline in a name would shift every later field on the line. Pinned by
    /// <c>EsiContributionWriterTests</c> with a fixture name that actually contains the delimiter.
    ///
    /// <para>The formula guard runs here too, and last, for the reason given on <see cref="Text"/>. It PREFIXES
    /// and never rewrites, so the person's own name still reaches ESIC verbatim after the apostrophe — and no real
    /// legal name begins with <c>= + - @</c>, so on real data this encoder is a no-op.</para>
    /// </summary>
    private static string Name(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return SpreadsheetFormulaGuard.Neutralize(
            value.Replace(Delimiter, ' ').Replace('\r', ' ').Replace('\n', ' '));
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
