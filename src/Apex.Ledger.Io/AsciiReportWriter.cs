using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="TabularExport"/> to the vendor File Format list's <i>ASCII (Comma Delimited)</i>
/// output (W2-25 / census 13.6): the same RFC-4180 comma-delimited records <see cref="CsvWriter"/> produces,
/// written as a plain <c>.txt</c> file with <b>no</b> byte-order mark.
///
/// <para><b>Why it is not simply CSV under another name.</b> The census records our pre-existing <c>Csv</c>
/// member as the source's ASCII format <i>"renamed, not missing"</i>. Rather than rename a shipped member (and
/// break every saved export config), <c>Ascii</c> ships beside it under the source's own <c>.txt</c> extension.
/// The BOM is the one real difference: <see cref="CsvWriter"/> leads with it so Excel opens the file as Unicode,
/// and a plain-text consumer that does not know about BOMs would read those three bytes as glyphs. Both writers
/// share <see cref="DelimitedText"/>, so the quoting, de-branding and formula-injection guard are identical by
/// construction.</para>
///
/// <para><b>Divergence, ours (ruling 9).</b> No admissible source states the vendor's exact byte encoding for
/// this format. UTF-8-without-BOM is our choice; it is a superset of ASCII, so a pure-ASCII report is
/// byte-identical to a true ASCII file while a rupee sign or a non-Latin party name still survives rather than
/// being silently mangled. Deterministic and byte-stable.</para>
/// </summary>
public static class AsciiReportWriter
{
    /// <summary>Serializes the model to comma-delimited UTF-8 bytes with no BOM.</summary>
    public static byte[] Write(TabularExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(DelimitedText.Compose(export));
    }
}
