using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to RFC-4180 CSV bytes (RQ-18). A field that contains a comma,
/// a double-quote, a CR or an LF is wrapped in double-quotes with any embedded quote doubled; records are
/// separated by CRLF. Output is UTF-8 encoded <b>with a BOM</b> so Excel opens it in Unicode (the rupee sign
/// and any non-ASCII label survive). Number cells format invariant at their OWN natural decimal scale (money at
/// 2dp, a quantity/rate at its real precision), so a spreadsheet reads them back as real numbers. A text field
/// that begins with a spreadsheet formula trigger is neutralized (a leading <c>'</c>) so it renders as literal
/// text (OWASP CSV-injection). Deterministic and byte-stable: no clock, no culture leak.
///
/// <para>The record composition itself lives in <see cref="DelimitedText"/> (W2-25), shared with the plain-text
/// <see cref="AsciiReportWriter"/> so the two delimited exports can never drift apart. The ONLY difference
/// between them is this writer's leading UTF-8 BOM.</para>
/// </summary>
public static class CsvWriter
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Serializes the model to RFC-4180 UTF-8-with-BOM CSV bytes.</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        // Prepend the UTF-8 BOM.
        byte[] body = Encoding.UTF8.GetBytes(DelimitedText.Compose(export));
        var result = new byte[Utf8Bom.Length + body.Length];
        System.Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
        System.Buffer.BlockCopy(body, 0, result, Utf8Bom.Length, body.Length);
        return result;
    }
}
