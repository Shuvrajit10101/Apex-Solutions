namespace Apex.Ledger.Io;

/// <summary>
/// The file format a report/master-list is exported to (RQ-14/16). PDF export is rendered by
/// <see cref="ReportPdf"/>; CSV/XLSX by <see cref="CsvWriter"/>/<see cref="XlsxWriter"/>; HTML, XML, JSON and
/// ASCII by <see cref="HtmlReportWriter"/>, <see cref="XmlReportWriter"/>, <see cref="JsonReportWriter"/> and
/// <see cref="AsciiReportWriter"/> (W2-25 / census 13.6).
///
/// <para><b>Members are APPENDED, never renumbered.</b> The ordinal is not persisted today, but the enum is
/// public API and a saved export config could carry one; the four W2-25 members therefore sit after the three
/// that shipped.</para>
///
/// <para><b>What is still missing, stated rather than hidden.</b> The vendor's File Format list has seven
/// entries; <b>JPEG (Image) <c>.jpg</c> is not among ours</b> — it needs a rasteriser (a dependency or a
/// hand-rolled encoder) and is carved out by design ruling R14, so census row 13.6 does <b>not</b> close on
/// this enum. Our <see cref="Csv"/> (<c>.csv</c>) and <see cref="Ascii"/> (<c>.txt</c>) are the SAME
/// comma-delimited records; see <see cref="AsciiReportWriter"/> for why both exist.</para>
/// </summary>
public enum ExportFormat
{
    Csv,
    Xlsx,
    Pdf,

    /// <summary>HTML (Web-Publishing), <c>.html</c> — a self-contained single-table document.</summary>
    Html,

    /// <summary>XML (Data Interchange), <c>.xml</c> — a report document, NOT the whole-company canonical XML.</summary>
    Xml,

    /// <summary>JSON (Data Exchange), <c>.json</c> — a report document, NOT the whole-company canonical JSON.</summary>
    Json,

    /// <summary>ASCII (Comma Delimited), <c>.txt</c> — the delimited records with no byte-order mark.</summary>
    Ascii,
}

/// <summary>
/// The export configuration the thin Avalonia layer builds from the export dialog and hands to the IO layer
/// (RQ-16): the chosen <see cref="Format"/>, the target <see cref="Folder"/> and base <see cref="FileName"/>,
/// and an optional <see cref="TimestampSuffix"/>.
///
/// <para>The IO layer has <b>no clock</b> (ER-8): the timestamp <i>value</i> is passed in by the UI (already
/// formatted, e.g. <c>"20260706-1200"</c>) so this class stays deterministic. When set, it is appended to the
/// base name as <c>Name_Suffix.ext</c>; the extension is derived from <see cref="Format"/>.</para>
/// </summary>
public sealed class ExportConfig
{
    public ExportFormat Format { get; init; } = ExportFormat.Csv;

    /// <summary>The target folder (chosen in the UI's folder/save dialog).</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>The base file name without extension (defaults to the report title in the UI).</summary>
    public string FileName { get; init; } = "Export";

    /// <summary>An already-formatted timestamp the UI supplies (the IO layer has no clock); blank ⇒ none.</summary>
    public string? TimestampSuffix { get; init; }

    /// <summary>The file extension for the chosen format (no dot).</summary>
    public string Extension => Format switch
    {
        ExportFormat.Csv => "csv",
        ExportFormat.Xlsx => "xlsx",
        ExportFormat.Pdf => "pdf",
        // W2-25: the four extensions are the vendor File Format list's own (census 13.6), including ASCII's
        // .txt — which is why Ascii is a member of its own rather than a rename of Csv.
        ExportFormat.Html => "html",
        ExportFormat.Xml => "xml",
        ExportFormat.Json => "json",
        ExportFormat.Ascii => "txt",
        _ => "dat",
    };

    /// <summary>The final file name: <c>Name[_Suffix].ext</c>.</summary>
    public string ResolvedFileName
    {
        get
        {
            string baseName = string.IsNullOrWhiteSpace(FileName) ? "Export" : FileName;
            string suffix = string.IsNullOrWhiteSpace(TimestampSuffix) ? string.Empty : "_" + TimestampSuffix;
            return baseName + suffix + "." + Extension;
        }
    }

    /// <summary>The full path (folder + resolved file name). The UI writes the writer's bytes here.</summary>
    public string FullPath =>
        string.IsNullOrEmpty(Folder) ? ResolvedFileName : System.IO.Path.Combine(Folder, ResolvedFileName);
}
