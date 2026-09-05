using System.Globalization;
using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a posted voucher (<see cref="VoucherPrintData"/>) to a PDF (RQ-10): a company + voucher-type
/// header, the number/date/party, the Dr/Cr posting lines in a two-amount-column table, the Dr/Cr totals, an
/// "amount in words" line and the narration (honouring the <see cref="PrintConfig.ShowNarration"/> toggle).
/// De-branded and deterministic — no clock, no RNG, invariant formatting — so the same voucher renders
/// byte-identically. Reuses the hand-rolled <see cref="PdfWriter"/>.
///
/// <para>Paginates like <see cref="ReportPdf"/>: a long voucher whose posting lines overflow the page starts a
/// continuation page (repeating the Particulars/Debit/Credit column header), and the closing block (totals +
/// amount-in-words + narration) is kept together — moved to a fresh page if it would not fit under the last
/// row. The footer shows "Page N of M".</para>
/// </summary>
public static class VoucherPdf
{
    /// <summary>Renders the voucher to PDF bytes.</summary>
    public static byte[] Render(VoucherPrintData data, PrintConfig config, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(page);

        string title = string.IsNullOrWhiteSpace(config.TitleOverride)
            ? DefaultTitle(data.VoucherTypeName)
            : Debrand.Text(config.TitleOverride!.Trim());

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double bottom = page.MarginBottom + page.FooterFontSize + 6;

        // ---- Pre-compute the closing block's height so we can keep it together on the final page. ----
        var value = data.TotalDebit >= data.TotalCredit ? data.TotalDebit : data.TotalCredit;
        string words = "Amount (in words): " + IndianAmountInWords.Convert(value.Amount);
        var wordLines = WrapText(words, page.ContentWidth, page.BodyFontSize);
        var narrationLines = (config.ShowNarration && !string.IsNullOrWhiteSpace(data.Narration))
            ? WrapText(Debrand.Text(data.Narration), page.ContentWidth, page.BodyFontSize)
            : new List<string>();

        double closingHeight =
            2 + page.RowHeight + 4                                    // totals rule + Total row
            + wordLines.Count * (page.BodyFontSize + 3) + 4;          // amount-in-words
        if (narrationLines.Count > 0)
            closingHeight += (page.BodyFontSize + 3)                  // "Narration:" caption
                + narrationLines.Count * (page.BodyFontSize + 3);

        // ---- Paginate the posting lines: reserve room for the closing block on whichever page it lands. ----
        var pages = new List<List<VoucherPrintLine>>();
        var current = new List<VoucherPrintLine>();
        double headerHeight = HeaderBandHeight(data, page);
        double y = page.PageHeight - page.MarginTop - headerHeight;
        foreach (var line in data.Lines)
        {
            if (y - page.RowHeight < bottom && current.Count > 0)
            {
                pages.Add(current);
                current = new List<VoucherPrintLine>();
                y = page.PageHeight - page.MarginTop - ContinuationBandHeight(data, page);
            }
            current.Add(line);
            y -= page.RowHeight;
        }
        pages.Add(current); // always at least one page (may be empty for a line-less voucher)

        // If the closing block will not fit below the last row on the last page, it spills to a fresh page.
        bool closingOnNewPage = y - closingHeight < bottom;
        int total = pages.Count + (closingOnNewPage ? 1 : 0);

        var writer = new PdfWriter { DocumentTitle = SafeTitle(title) };
        for (int p = 0; p < pages.Count; p++)
        {
            writer.BeginPage(page.PageWidth, page.PageHeight);
            bool isFirst = p == 0;
            double yy = DrawHeaderBand(writer, data, config, page, title, left, right, isFirst);
            yy = DrawPostingHeader(writer, page, left, right, yy);
            foreach (var line in pages[p])
                yy = DrawPostingLine(writer, page, left, right, line, yy);

            if (p == pages.Count - 1 && !closingOnNewPage)
                DrawClosingBlock(writer, data, page, left, right, wordLines, narrationLines, yy);

            DrawFooter(writer, page, left, right, p + 1, total);
        }

        if (closingOnNewPage)
        {
            writer.BeginPage(page.PageWidth, page.PageHeight);
            double yy = DrawHeaderBand(writer, data, config, page, title, left, right, isFirstPage: false);
            // A continuation of the table: repeat the column header, then draw the closing (totals) block.
            yy = DrawPostingHeader(writer, page, left, right, yy);
            DrawClosingBlock(writer, data, page, left, right, wordLines, narrationLines, yy);
            DrawFooter(writer, page, left, right, total, total);
        }

        // W2-31 (census 12.4) F5: collated copies of the whole voucher. One copy repeats nothing, so the shipped
        // byte stream is untouched (ER-13).
        writer.RepeatAllPages(page.EffectiveCopies);
        return writer.Build();
    }

    // ---- band-height estimates (kept in sync with the drawing methods) ----

    private static double HeaderBandHeight(VoucherPrintData data, PageConfig page)
    {
        double h = 0;
        if (!string.IsNullOrWhiteSpace(data.CompanyName)) h += page.TitleFontSize + 6;
        h += page.SubtitleFontSize + 2 + 10;                 // title band
        // Phase 10.11 S3 — the CANCELLED over-print sits in the title band and therefore costs a band row. This
        // estimate is what the paginator reserves; leaving it out would let a cancelled voucher's last posting
        // line fall past the page foot.
        if (data.IsCancelled) h += page.SubtitleFontSize + 2;
        h += page.BodyFontSize + 4;                          // No/Date row
        if (!string.IsNullOrWhiteSpace(data.PartyName)) h += page.BodyFontSize + 4;
        if (!string.IsNullOrWhiteSpace(data.ReferenceNo)) h += page.BodyFontSize + 4; // v48 numbering §8 ref row
        return h;
    }

    private static double ContinuationBandHeight(VoucherPrintData data, PageConfig page) =>
        page.SubtitleFontSize + 2 + 10 + page.BodyFontSize + 4  // "(continued)" title band + a spacer row
        + (data.IsCancelled ? page.SubtitleFontSize + 2 : 0);   // + the S3 CANCELLED over-print

    /// <summary>
    /// The word over-printed across a cancelled voucher's title band (Phase 10.11 S3), on EVERY page — a
    /// continuation sheet that has been separated from page 1 must still say what it is.
    ///
    /// <para><b>🔴 UNVERIFIED-BY-DESIGN — ours, corpus silent.</b> The source corpus describes no printed
    /// treatment of a cancelled voucher at all; the over-print, its wording and its placement are our
    /// decision (R7).</para>
    /// </summary>
    private const string CancelledBanner = "CANCELLED";

    // ---- drawing ----

    private static double DrawHeaderBand(
        PdfWriter writer, VoucherPrintData data, PrintConfig config, PageConfig page,
        string title, double left, double right, bool isFirstPage)
    {
        double y = page.PageHeight - page.MarginTop;

        if (isFirstPage)
        {
            if (!string.IsNullOrWhiteSpace(data.CompanyName))
            {
                y -= page.TitleFontSize;
                Center(writer, data.CompanyName, left, right, y, page.TitleFontSize, bold: true);
                y -= 6;
            }
            y -= page.SubtitleFontSize + 2;
            Center(writer, title, left, right, y, page.SubtitleFontSize + 2, bold: true);
            // Phase 10.11 S3 — over-print CANCELLED under the document title. Nothing is drawn and no space is
            // consumed when the flag is false, so every voucher PDF the app has ever produced is byte-identical
            // (ER-13). See HeaderBandHeight, which reserves the matching space for the paginator.
            if (data.IsCancelled)
            {
                y -= page.SubtitleFontSize + 2;
                Center(writer, CancelledBanner, left, right, y, page.SubtitleFontSize + 2, bold: true);
            }
            y -= 10;

            if (config.CopyMarking != CopyMarking.None)
            {
                string label = config.CopyMarkingLabel;
                double w = PdfWriter.MeasureHelvetica(label, page.FooterFontSize);
                writer.Text(right - w, y, label, page.FooterFontSize, bold: true);
            }

            y -= page.BodyFontSize + 4;
            if (!string.IsNullOrWhiteSpace(data.VoucherNumber))
                writer.Text(left, y, "No: " + data.VoucherNumber, page.BodyFontSize, bold: false);
            string dateLine = "Dated: " + data.DateText;
            double dw = PdfWriter.MeasureHelvetica(dateLine, page.BodyFontSize);
            writer.Text(right - dw, y, dateLine, page.BodyFontSize, bold: false);
            y -= page.BodyFontSize + 4;
            if (!string.IsNullOrWhiteSpace(data.PartyName))
            {
                writer.Text(left, y, "Party: " + data.PartyName, page.BodyFontSize, bold: true);
                y -= page.BodyFontSize + 4;
            }
            // v48 (numbering §8): the counterparty document number ("Supplier Invoice No." / "Reference No."),
            // printed only when captured so a voucher without one is byte-identical to before (ER-13).
            if (!string.IsNullOrWhiteSpace(data.ReferenceNo))
            {
                var refLine = data.ReferenceCaption + ": " + data.ReferenceNo;
                if (!string.IsNullOrWhiteSpace(data.ReferenceDateText)) refLine += "   Dated: " + data.ReferenceDateText;
                writer.Text(left, y, refLine, page.BodyFontSize, bold: false);
                y -= page.BodyFontSize + 4;
            }
        }
        else
        {
            // Continuation page: a compact title band for context.
            y -= page.SubtitleFontSize + 2;
            Center(writer, title + " (continued)", left, right, y, page.SubtitleFontSize + 2, bold: true);
            // S3 — the over-print repeats here: a continuation sheet is a loose page once it leaves the printer.
            if (data.IsCancelled)
            {
                y -= page.SubtitleFontSize + 2;
                Center(writer, CancelledBanner, left, right, y, page.SubtitleFontSize + 2, bold: true);
            }
            y -= 10;
            y -= page.BodyFontSize + 4;
        }
        return y;
    }

    private static double DrawPostingHeader(PdfWriter writer, PageConfig page, double left, double right, double y)
    {
        double particularsX = left;
        double drX = left + page.ContentWidth * 0.62;
        double crX = left + page.ContentWidth * 0.81;

        writer.Line(left, y, right, y, 0.7);
        y -= page.BodyFontSize + 2;
        writer.Text(particularsX + 2, y, "Particulars", page.BodyFontSize, bold: true);
        RightText(writer, "Debit", drX, crX - 4, y, page.BodyFontSize, bold: true);
        RightText(writer, "Credit", crX, right, y, page.BodyFontSize, bold: true);
        y -= 4;
        writer.Line(left, y, right, y, 0.7);
        y -= page.RowHeight;
        return y;
    }

    private static double DrawPostingLine(PdfWriter writer, PageConfig page, double left, double right, VoucherPrintLine line, double y)
    {
        double particularsX = left;
        double drX = left + page.ContentWidth * 0.62;
        double crX = left + page.ContentWidth * 0.81;

        string particulars = (line.IsDebit ? "Dr  " : "Cr  ") + line.LedgerName;
        string particularsFitted = PdfWriter.FitToWidth(particulars, drX - particularsX - 6, page.BodyFontSize);
        writer.Text(particularsX + 2, y, particularsFitted, page.BodyFontSize, bold: false);

        string amt = Fmt(line.Amount);
        if (line.IsDebit) RightText(writer, amt, drX, crX - 4, y, page.BodyFontSize, bold: false);
        else RightText(writer, amt, crX, right, y, page.BodyFontSize, bold: false);
        return y - page.RowHeight;
    }

    private static void DrawClosingBlock(
        PdfWriter writer, VoucherPrintData data, PageConfig page, double left, double right,
        List<string> wordLines, List<string> narrationLines, double y)
    {
        double particularsX = left;
        double drX = left + page.ContentWidth * 0.62;
        double crX = left + page.ContentWidth * 0.81;

        y -= 2;
        writer.Line(drX, y + page.RowHeight - 2, right, y + page.RowHeight - 2, 0.7);
        writer.Text(particularsX + 2, y, "Total", page.BodyFontSize, bold: true);
        RightText(writer, Fmt(data.TotalDebit), drX, crX - 4, y, page.BodyFontSize, bold: true);
        RightText(writer, Fmt(data.TotalCredit), crX, right, y, page.BodyFontSize, bold: true);
        y -= page.RowHeight + 4;

        foreach (var wl in wordLines)
        {
            writer.Text(left, y, wl, page.BodyFontSize, bold: false);
            y -= page.BodyFontSize + 3;
        }
        y -= 4;

        if (narrationLines.Count > 0)
        {
            writer.Text(left, y, "Narration:", page.BodyFontSize, bold: true);
            y -= page.BodyFontSize + 3;
            foreach (var nl in narrationLines)
            {
                writer.Text(left, y, nl, page.BodyFontSize, bold: false);
                y -= page.BodyFontSize + 3;
            }
        }
    }

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right, int pageNo, int pageCount)
    {
        string footer = (page.FooterText ?? string.Empty)
            .Replace("{page}", pageNo.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", pageCount.ToString(CultureInfo.InvariantCulture));
        if (footer.Length > 0)
            Center(writer, footer, left, right, page.MarginBottom, page.FooterFontSize, bold: false);
    }

    internal static string DefaultTitle(string voucherTypeName) =>
        string.IsNullOrWhiteSpace(voucherTypeName) ? "Voucher" : voucherTypeName + " Voucher";

    /// <summary>Money on the printed voucher, Indian-grouped via <see cref="IndianMoneyFormat"/> — the ONE
    /// grouping rule (drift lock D2); previously Western-grouped against the invariant culture.</summary>
    private static string Fmt(Money m) => IndianMoneyFormat.Amount(m.Amount);

    private static void Center(PdfWriter w, string text, double left, double right, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = (left + right) / 2.0 - tw / 2.0;
        if (x < left) x = left;
        w.Text(x, y, text, size, bold);
    }

    private static void RightText(PdfWriter w, string text, double cellLeft, double cellRight, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = cellRight - tw;
        if (x < cellLeft) x = cellLeft;
        w.Text(x, y, text, size, bold);
    }

    /// <summary>Greedy word-wrap of a string to lines fitting <paramref name="maxWidth"/> points (deterministic).</summary>
    internal static List<string> WrapText(string text, double maxWidth, double fontSize)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (PdfWriter.MeasureHelvetica(candidate, fontSize) > maxWidth && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static string SafeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Apex Solutions Voucher" : Debrand.Text(title) + " — Apex Solutions";
}
