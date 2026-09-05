namespace Apex.Ledger.Io;

/// <summary>Standard page sizes, in PDF points (1 pt = 1/72 inch).</summary>
public enum PageSize
{
    /// <summary>ISO A4: 595.276 × 841.890 pt (210 × 297 mm).</summary>
    A4,

    /// <summary>US Letter: 612 × 792 pt (8.5 × 11 in).</summary>
    Letter,
}

/// <summary>Portrait or landscape. Landscape swaps the page's width and height.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// The <b>F8 Print Format</b> selector (W2-31 / census 12.4). The three values are the vendor's own, quoted by
/// the census: <i>"Dot Matrix Format"</i>, <i>"Neat Mode"</i> and <i>"Quick/Draft Format"</i>. (<i>Condensed</i>
/// is a Tally.ERP-9-era term that is not in that list, and <i>Pre-Printed</i> is not a format at all — it is the
/// separate <see cref="PaperKind"/> axis.)
///
/// <para><b>What each one DOES to our output is OURS</b> (ruling 9). No admissible source states the metrics, so
/// the mapping in <see cref="PageConfig"/> — condensed rows for dot matrix, no ruling ink for a draft — is a
/// documented divergence and can never join the compared set. <see cref="Neat"/> is the default and its metrics
/// are exactly the ones the renderer shipped with, so nothing moves unless the operator asks.</para>
/// </summary>
public enum PrintFormat
{
    /// <summary>"Neat Mode" — the presentation layout. The default; identical to the shipped output.</summary>
    Neat,

    /// <summary>"Dot Matrix Format" — condensed rows for continuous stationery on an impact printer.</summary>
    DotMatrix,

    /// <summary>"Quick/Draft Format" — the fastest, plainest pass: no ruling ink.</summary>
    QuickDraft,
}

/// <summary>
/// The <b>F9 paper</b> toggle (W2-31 / census 12.4): plain paper, or paper that already carries the letterhead
/// and the column captions. This is a DIFFERENT axis from <see cref="PrintFormat"/> — the census records that
/// listing "Pre-Printed" as a print format would put a paper setting in a format dropdown.
///
/// <para><b>Ours</b> (ruling 9): which bands <see cref="PrePrinted"/> suppresses is our choice — the title
/// block and the column-header band, i.e. exactly what stationery is pre-printed with and what would otherwise
/// double-strike. The figures always print.</para>
/// </summary>
public enum PaperKind
{
    /// <summary>Plain paper — the document prints its own title block and column captions.</summary>
    Plain,

    /// <summary>Pre-printed stationery — the title block and column captions are suppressed.</summary>
    PrePrinted,
}

/// <summary>
/// Everything the (deterministic, culture-invariant) PDF renderer needs about the page: size,
/// orientation, margins, per-page header/footer text and font sizing. No clock: the page footer's
/// page-number is derived from pagination, and any date shown must be baked into the report model or
/// passed as <see cref="FooterText"/> by the caller — the renderer never reads <c>DateTime.Now</c>.
/// </summary>
public sealed class PageConfig
{
    public PageSize Size { get; init; } = PageSize.A4;
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>Page margins in points. Default ~0.5in (36 pt) all round.</summary>
    public double MarginLeft { get; init; } = 36;
    public double MarginRight { get; init; } = 36;
    public double MarginTop { get; init; } = 36;
    public double MarginBottom { get; init; } = 36;

    /// <summary>Optional running header line printed at the top of every page (e.g. company name).</summary>
    public string HeaderText { get; init; } = string.Empty;

    /// <summary>
    /// Optional running footer line printed at the bottom of every page. A brand-safe default is used
    /// when empty. The literal token <c>{page}</c> is replaced with the 1-based page number and
    /// <c>{pages}</c> with the total page count.
    /// </summary>
    public string FooterText { get; init; } = "Apex Solutions  —  Page {page} of {pages}";

    // ---- font sizing (points) ----
    public double TitleFontSize { get; init; } = 16;
    public double SubtitleFontSize { get; init; } = 10;
    public double HeaderFontSize { get; init; } = 9;
    public double BodyFontSize { get; init; } = 9;
    public double FooterFontSize { get; init; } = 8;

    /// <summary>Vertical distance between body row baselines, in points.</summary>
    public double RowHeight { get; init; } = 13;

    // ---- W2-31 (census 12.4): the F8 / F9 / F5 / F10 print knobs ----------------------------------------
    // Every default below reproduces the shipped output exactly, so a caller that sets none of them renders
    // byte-for-byte what it always did (ER-13).

    /// <summary>F8 — the print format. <see cref="PrintFormat.Neat"/> by default (the shipped layout).</summary>
    public PrintFormat Format { get; init; } = PrintFormat.Neat;

    /// <summary>F9 — plain paper (default) or pre-printed stationery.</summary>
    public PaperKind Paper { get; init; } = PaperKind.Plain;

    /// <summary>
    /// F5 — how many collated copies of the WHOLE document the file carries. 1 by default; anything below 1 is
    /// read as 1. Copies repeat the document, not the page: a two-page report at two copies is 1,2,1,2.
    ///
    /// <para><b>R6 caveat, stated rather than hidden.</b> There is no physical printer in this application —
    /// "print" means render a PDF. A copy count is therefore a count of document sets INSIDE one PDF, which is
    /// what makes it meaningful at all here; on paper it would be the spooler's job. Census row 12.5 (printer
    /// selection / spooler) stays ABSENT and this knob does not change that.</para>
    /// </summary>
    public int Copies { get; init; } = 1;

    /// <summary>F10 — the first page of the document to print (1-based). 1 by default.</summary>
    public int FirstPage { get; init; } = 1;

    /// <summary>F10 — the last page of the document to print (1-based); <c>0</c> (default) means to the end.</summary>
    public int LastPage { get; init; }

    /// <summary>
    /// F10 — the page number the document's FIRST sheet is numbered with, for a report continuing a numbering
    /// run. 1 by default. A four-page report starting at 7 footers "Page 7 of 10" … "Page 10 of 10".
    /// </summary>
    public int StartPageNumber { get; init; } = 1;

    /// <summary>The number of collated copies actually emitted (never below one).</summary>
    public int EffectiveCopies => Copies < 1 ? 1 : Copies;

    /// <summary>Row pitch after the F8 format is applied. OURS: dot matrix condenses 13 pt to 11 pt.</summary>
    public double FormattedRowHeight => Format == PrintFormat.DotMatrix ? 11.0 : RowHeight;

    /// <summary>Body font size after the F8 format is applied. OURS: dot matrix condenses 9 pt to 8 pt.</summary>
    public double FormattedBodyFontSize => Format == PrintFormat.DotMatrix ? 8.0 : BodyFontSize;

    /// <summary>Column-caption font size after the F8 format is applied.</summary>
    public double FormattedHeaderFontSize => Format == PrintFormat.DotMatrix ? 8.0 : HeaderFontSize;

    /// <summary>Title font size after the F8 format is applied. OURS: dot matrix drops 16 pt to 12 pt.</summary>
    public double FormattedTitleFontSize => Format == PrintFormat.DotMatrix ? 12.0 : TitleFontSize;

    /// <summary>Whether ruling lines are stroked. OURS: a Quick/Draft pass draws none.</summary>
    public bool DrawsRules => Format != PrintFormat.QuickDraft;

    /// <summary>Whether the title/subtitle block prints. Suppressed on pre-printed stationery.</summary>
    public bool DrawsTitleBand => Paper != PaperKind.PrePrinted;

    /// <summary>Whether the column-caption band prints. Suppressed on pre-printed stationery.</summary>
    public bool DrawsColumnHeaderBand => Paper != PaperKind.PrePrinted;

    /// <summary>True when <paramref name="pageNumber"/> (1-based, the DOCUMENT's own numbering) falls inside the
    /// F10 range. An out-of-bounds range selects nothing — it never silently falls back to the whole report.</summary>
    public bool IncludesPage(int pageNumber)
    {
        int first = FirstPage < 1 ? 1 : FirstPage;
        if (pageNumber < first) return false;
        if (LastPage > 0 && pageNumber > LastPage) return false;
        return true;
    }

    /// <summary>The page's physical width in points, accounting for orientation.</summary>
    public double PageWidth => Orientation == PageOrientation.Portrait ? BaseWidth : BaseHeight;

    /// <summary>The page's physical height in points, accounting for orientation.</summary>
    public double PageHeight => Orientation == PageOrientation.Portrait ? BaseHeight : BaseWidth;

    private double BaseWidth => Size == PageSize.A4 ? 595.276 : 612.0;
    private double BaseHeight => Size == PageSize.A4 ? 841.890 : 792.0;

    /// <summary>Width available for content between the left and right margins.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;
}
