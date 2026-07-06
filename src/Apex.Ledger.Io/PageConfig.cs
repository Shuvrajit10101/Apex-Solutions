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

    /// <summary>The page's physical width in points, accounting for orientation.</summary>
    public double PageWidth => Orientation == PageOrientation.Portrait ? BaseWidth : BaseHeight;

    /// <summary>The page's physical height in points, accounting for orientation.</summary>
    public double PageHeight => Orientation == PageOrientation.Portrait ? BaseHeight : BaseWidth;

    private double BaseWidth => Size == PageSize.A4 ? 595.276 : 612.0;
    private double BaseHeight => Size == PageSize.A4 ? 841.890 : 792.0;

    /// <summary>Width available for content between the left and right margins.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;
}
