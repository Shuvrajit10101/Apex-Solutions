using System.Globalization;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a <see cref="PrintReport"/> (already-formatted title / subtitle / columns / rows) to a PDF
/// document via the hand-rolled <see cref="PdfWriter"/>. Lays out a title block and a running page
/// header/footer, draws the column-header band on every page, right-aligns amount columns, bolds/rules
/// section headers and totals, and paginates when rows overflow the content height.
///
/// <para>Deterministic and culture-invariant: no clock, no RNG, invariant number formatting. Metadata is
/// de-branded ("Apex Solutions"). Given the same report + config it produces byte-identical output.</para>
/// </summary>
public static class ReportPdf
{
    /// <summary>Renders the report to PDF bytes using the given page configuration.</summary>
    public static byte[] Render(PrintReport report, PageConfig config)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Render(new[] { report }, config);
    }

    /// <summary>
    /// Renders a SET of already-formatted documents into ONE PDF (W2-32 / census 12.6 — multi-account /
    /// multi-voucher range printing). Each document starts on a fresh sheet and the page numbering runs across
    /// the whole job, so a stack of printed ledger accounts reads as one document an operator can collate.
    ///
    /// <para>The W2-31 knobs apply to the JOB, not to each member: the F10 range selects sheets of the job, the
    /// F5 copy count repeats the whole job collated, and the F8/F9 format and paper apply throughout. Rendering
    /// a one-document job is byte-identical to rendering that document alone (ER-13), which is why the
    /// single-document overload above simply delegates here.</para>
    /// </summary>
    public static byte[] Render(IReadOnlyList<PrintReport> documents, PageConfig config)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(config);

        // First pass: paginate EVERY document so the footer can show the job-wide "Page x of N". Each document's
        // pages are kept with the document they belong to, because the column geometry is per document (a ledger
        // account and a reminder letter do not share a column layout).
        var laid = new List<(PrintReport Report, double[] ColX, List<PrintRow> Rows)>();
        foreach (var report in documents)
        {
            if (report is null) continue;
            double[] colX = ComputeColumnX(report, config);
            var pages = Paginate(report, config);
            if (pages.Count == 0) pages.Add(new List<PrintRow>());
            foreach (var rows in pages) laid.Add((report, colX, rows));
        }
        int total = laid.Count == 0 ? 1 : laid.Count;

        // W2-31 (census 12.4) F10: the job keeps its OWN numbering. StartPageNumber renumbers sheet 1 (so a
        // continuation report reads "Page 7 of 10"), and the page RANGE selects which of those sheets are drawn —
        // it never renumbers them, because the operator is holding sheet 3 of a 4-sheet job.
        int firstNumber = config.StartPageNumber < 1 ? 1 : config.StartPageNumber;
        int lastNumber = firstNumber + total - 1;

        // The PDF /Title names the job. A single-document job keeps that document's title, so the one-document
        // path is byte-identical to the single-document render it replaced.
        var writer = new PdfWriter { DocumentTitle = SafeTitle(JobTitle(documents)) };

        int drawn = 0;
        for (int p = 0; p < laid.Count; p++)
        {
            if (!config.IncludesPage(p + 1)) continue;   // outside the F10 range — not drawn at all
            var (report, colX, rows) = laid[p];
            writer.BeginPage(config.PageWidth, config.PageHeight);
            DrawPage(writer, report, config, colX, rows, firstNumber + p, lastNumber, isFirstPage: p == 0);
            drawn++;
        }

        // A PDF must carry at least one page. An out-of-bounds range (or an empty job) therefore yields ONE BLANK
        // sheet rather than the whole report — silently falling back to "print everything" is the failure this
        // guards against.
        if (drawn == 0)
            writer.BeginPage(config.PageWidth, config.PageHeight);

        // W2-31 F5: collated copies of the WHOLE job (1,2,1,2 — never 1,1,2,2). One copy repeats nothing, so the
        // shipped byte stream is untouched (ER-13).
        writer.RepeatAllPages(config.EffectiveCopies);

        return writer.Build();
    }

    /// <summary>The /Title for a job: the lone document's title, or a neutral label for a set.</summary>
    private static string JobTitle(IReadOnlyList<PrintReport> documents)
        => documents.Count == 1 && documents[0] is { } only ? only.Title : "Print Job";

    // ---- pagination ----

    private static List<List<PrintRow>> Paginate(PrintReport report, PageConfig config)
    {
        double top = config.PageHeight - config.MarginTop;
        double bottom = config.MarginBottom + config.FooterFontSize + 6;

        // Height consumed by the fixed banner (title block + column-header band) at the top of each page.
        double firstBanner = BannerHeight(config, includeTitle: true);
        double restBanner = BannerHeight(config, includeTitle: true); // title repeats on every page for context

        var pages = new List<List<PrintRow>>();
        var current = new List<PrintRow>();
        double y = top - firstBanner;

        foreach (var row in report.Rows)
        {
            double h = config.FormattedRowHeight;   // W2-31: dot matrix condenses the pitch, so more rows fit
            if (y - h < bottom && current.Count > 0)
            {
                pages.Add(current);
                current = new List<PrintRow>();
                y = top - restBanner;
            }
            current.Add(row);
            y -= h;
        }
        if (current.Count > 0 || pages.Count == 0)
            pages.Add(current);
        return pages;
    }

    private static double BannerHeight(PageConfig config, bool includeTitle)
    {
        // W2-31: the banner is measured with the FORMAT's metrics so pagination and drawing agree. It is NOT
        // measured with the PAPER's: pre-printed stationery physically occupies that space with a letterhead, so
        // the band is suppressed from the ink but its height is still reserved — otherwise the figures would
        // overprint the letterhead.
        double h = 0;
        if (includeTitle)
        {
            h += config.FormattedTitleFontSize + 6;
            h += config.SubtitleFontSize + 8;
        }
        h += config.FormattedHeaderFontSize + 6; // column header band + its rule
        return h;
    }

    // ---- column geometry ----

    private static double[] ComputeColumnX(PrintReport report, PageConfig config)
    {
        int n = report.Columns.Count;
        var xs = new double[Math.Max(n, 1) + 1];
        double left = config.MarginLeft;
        if (n == 0)
        {
            xs[0] = left;
            xs[1] = left + config.ContentWidth;
            return xs;
        }
        double totalWeight = 0;
        foreach (var c in report.Columns) totalWeight += c.Weight <= 0 ? 1 : c.Weight;
        double x = left;
        xs[0] = x;
        for (int i = 0; i < n; i++)
        {
            double w = report.Columns[i].Weight <= 0 ? 1 : report.Columns[i].Weight;
            x += config.ContentWidth * (w / totalWeight);
            xs[i + 1] = x;
        }
        return xs;
    }

    // ---- drawing ----

    private static void DrawPage(
        PdfWriter writer, PrintReport report, PageConfig config, double[] colX,
        List<PrintRow> rows, int pageNo, int pageCount, bool isFirstPage)
    {
        double left = config.MarginLeft;
        double right = config.PageWidth - config.MarginRight;
        double y = config.PageHeight - config.MarginTop;

        // Running header text (optional).
        if (!string.IsNullOrEmpty(config.HeaderText))
        {
            writer.Text(left, config.PageHeight - config.MarginTop + 4, config.HeaderText, config.FooterFontSize);
        }

        // Title block (centered title + subtitle), repeated on every page for context. W2-31 F9: on pre-printed
        // stationery the letterhead is already there, so the band is SKIPPED but its height is still consumed —
        // the figures must land where the stationery leaves room for them, not slide up over the letterhead.
        y -= config.FormattedTitleFontSize;
        if (config.DrawsTitleBand)
            DrawCentered(writer, Scrub(report.Title), left, right, y, config.FormattedTitleFontSize);
        y -= 6;
        y -= config.SubtitleFontSize;
        if (config.DrawsTitleBand && !string.IsNullOrEmpty(report.Subtitle))
            DrawCentered(writer, Scrub(report.Subtitle), left, right, y, config.SubtitleFontSize);
        y -= 8;

        // Column header band + rule (the caption row is always bold). Suppressed on pre-printed stationery for
        // the same reason; the rule is also dropped by a Quick/Draft pass, which draws no ruling ink at all.
        y -= config.FormattedHeaderFontSize;
        if (config.DrawsColumnHeaderBand)
            DrawRowCells(writer, report, config, colX, HeaderRow(report), y, config.FormattedHeaderFontSize);
        double ruleY = y - 3;
        if (config.DrawsRules && config.DrawsColumnHeaderBand)
            writer.Line(left, ruleY, right, ruleY, 0.7);
        y -= 6;

        // Body rows.
        foreach (var row in rows)
        {
            y -= config.FormattedRowHeight;
            double baseline = y + (config.FormattedRowHeight - config.FormattedBodyFontSize) / 2.0;
            if (row.IsTotal && config.DrawsRules)
                writer.Line(left, y + config.FormattedRowHeight - 2, right, y + config.FormattedRowHeight - 2, 0.5);
            DrawRowCells(writer, report, config, colX, row, baseline, config.FormattedBodyFontSize);
        }

        // Footer.
        string footer = (config.FooterText ?? string.Empty)
            .Replace("{page}", pageNo.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", pageCount.ToString(CultureInfo.InvariantCulture));
        if (footer.Length > 0)
        {
            double fy = config.MarginBottom;
            DrawCentered(writer, footer, left, right, fy, config.FooterFontSize);
        }
    }

    private static PrintRow HeaderRow(PrintReport report)
    {
        var cells = new string[report.Columns.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = report.Columns[i].Header;
        return new PrintRow { Cells = cells, IsHeader = true };
    }

    private static void DrawRowCells(
        PdfWriter writer, PrintReport report, PageConfig config, double[] colX,
        PrintRow row, double baseline, double fontSize)
    {
        int n = report.Columns.Count;
        double pad = 2;
        // Section headers and total rows render bold so they stand out from body rows (RQ-9 fidelity).
        bool bold = row.IsHeader || row.IsTotal;
        for (int i = 0; i < n; i++)
        {
            // ER-11: the cell text is scrubbed on the SAME terms the CSV / XLSX / HTML / JSON writers scrub the
            // same projection's cells (TabularDebrand.Cell). Before W2-32 this renderer scrubbed nothing at all —
            // it was the only PDF renderer in the assembly that did not — so the same report exported to CSV and
            // printed to PDF disagreed about whether the forbidden brand appeared. Conditional, so a clean cell's
            // bytes (and its measured width, and therefore its clipping) do not move (ER-13).
            string text = i < row.Cells.Count ? Scrub(row.Cells[i]) : string.Empty;
            if (i == 0 && row.Indent > 0)
                text = new string(' ', row.Indent) + text;
            if (text.Length == 0) continue;

            var align = report.Columns[i].Align;
            double cellLeft = colX[i] + pad;
            double cellRight = colX[i + 1] - pad;
            double cellWidth = cellRight - cellLeft;

            // Clip long text to the column's inner width so it never overflows into the next column or past
            // the page's right edge (viewers otherwise just draw it clipped and columns misalign).
            text = PdfWriter.FitToWidth(text, cellWidth, fontSize);
            if (text.Length == 0) continue;

            double textW = PdfWriter.MeasureHelvetica(text, fontSize);
            double x = align switch
            {
                CellAlign.Right => cellRight - textW,
                CellAlign.Center => (cellLeft + cellRight) / 2.0 - textW / 2.0,
                _ => cellLeft,
            };
            if (x < cellLeft) x = cellLeft;
            writer.Text(x, baseline, text, fontSize, bold);
        }
    }

    private static void DrawCentered(PdfWriter writer, string text, double left, double right, double y, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return;
        double w = PdfWriter.MeasureHelvetica(text, fontSize);
        double x = (left + right) / 2.0 - w / 2.0;
        if (x < left) x = left;
        writer.Text(x, y, text, fontSize);
    }

    /// <summary>Keeps the /Title metadata brand-safe (never emits a third-party brand into the PDF).</summary>
    private static string SafeTitle(string title)
        => string.IsNullOrWhiteSpace(title) ? "Apex Solutions Report" : Scrub(title) + " — Apex Solutions";

    /// <summary>
    /// 🔴 <b>ER-11 de-branding for a heading, added by W2-32 to close a REAL hole.</b> This renderer declared its
    /// output de-branded and its metadata carried "Apex Solutions", but nothing ever scrubbed the report's own
    /// title or subtitle — so a document whose heading carried the forbidden brand printed it, on the page and in
    /// the <c>/Title</c>. It stayed invisible because every report title in the app is app-authored; a
    /// multi-account job (census 12.6) titles each sheet with a LEDGER NAME the user typed, which is what makes
    /// it reachable.
    ///
    /// <para>The scrub is applied only when the brand is actually present, because
    /// <see cref="Debrand.Text"/> also collapses whitespace runs — an unconditional call would have moved the
    /// bytes of every clean document that has a double space in its subtitle, and this suite's goldens with it
    /// (ER-13).</para>
    /// </summary>
    private static string Scrub(string? text)
        => Debrand.Contains(text) ? Debrand.Text(text) : (text ?? string.Empty);
}
