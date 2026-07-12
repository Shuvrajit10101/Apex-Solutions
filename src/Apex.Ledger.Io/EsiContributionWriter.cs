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
/// a stray token in a user field can never corrupt the record framing. Rows are emitted in the return's
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
                Text(r.IpNumber), Text(r.IpName), Int(r.NoOfDays), Int(r.TotalMonthlyWages),
                Text(r.ReasonForZeroWages), Text(r.LastWorkingDay));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- field encoders (invariant, de-branded, delimiter-safe) ----

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = Debrand.Text(value);
        return cleaned.Replace(Delimiter, ' ').Replace('\r', ' ').Replace('\n', ' ');
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
