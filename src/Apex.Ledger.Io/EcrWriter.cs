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
                Text(m.Uan), Text(m.Name),
                Int(m.GrossWages), Int(m.EpfWages), Int(m.EpsWages), Int(m.EdliWages),
                Int(m.EmployeeShareEpf), Int(m.EpsContribution), Int(m.EmployerShareEpf),
                Int(m.NcpDays), Int(m.RefundOfAdvances));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- field encoders (invariant, de-branded, delimiter-safe) ----

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // De-brand (ER-11) then strip the delimiter/record separators so a stray token in a user field can never
        // corrupt the record framing. Deterministic; no culture leak.
        var cleaned = Debrand.Text(value);
        return cleaned.Replace(Delimiter, " ").Replace('\r', ' ').Replace('\n', ' ');
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
