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
        ArgumentNullException.ThrowIfNull(config);

        // First pass: paginate rows into pages so the footer can show "Page x of N".
        var pages = Paginate(report, config);
        int total = pages.Count == 0 ? 1 : pages.Count;

        var writer = new PdfWriter { DocumentTitle = SafeTitle(report.Title) };
        double[] colX = ComputeColumnX(report, config);

        for (int p = 0; p < total; p++)
        {
            writer.BeginPage(config.PageWidth, config.PageHeight);
            var rows = pages.Count == 0 ? new List<PrintRow>() : pages[p];
            DrawPage(writer, report, config, colX, rows, p + 1, total, isFirstPage: p == 0);
        }

        return writer.Build();
    }

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
            double h = config.RowHeight;
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
        double h = 0;
        if (includeTitle)
        {
            h += config.TitleFontSize + 6;
            h += config.SubtitleFontSize + 8;
        }
        h += config.HeaderFontSize + 6; // column header band + its rule
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

        // Title block (centered title + subtitle), repeated on every page for context.
        y -= config.TitleFontSize;
        DrawCentered(writer, report.Title, left, right, y, config.TitleFontSize);
        y -= 6;
        y -= config.SubtitleFontSize;
        if (!string.IsNullOrEmpty(report.Subtitle))
            DrawCentered(writer, report.Subtitle, left, right, y, config.SubtitleFontSize);
        y -= 8;

        // Column header band + rule (the caption row is always bold).
        y -= config.HeaderFontSize;
        DrawRowCells(writer, report, config, colX, HeaderRow(report), y, config.HeaderFontSize);
        double ruleY = y - 3;
        writer.Line(left, ruleY, right, ruleY, 0.7);
        y -= 6;

        // Body rows.
        foreach (var row in rows)
        {
            y -= config.RowHeight;
            double baseline = y + (config.RowHeight - config.BodyFontSize) / 2.0;
            if (row.IsTotal)
                writer.Line(left, y + config.RowHeight - 2, right, y + config.RowHeight - 2, 0.5);
            DrawRowCells(writer, report, config, colX, row, baseline, config.BodyFontSize);
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
            string text = i < row.Cells.Count ? (row.Cells[i] ?? string.Empty) : string.Empty;
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
        => string.IsNullOrWhiteSpace(title) ? "Apex Solutions Report" : title + " — Apex Solutions";
}
