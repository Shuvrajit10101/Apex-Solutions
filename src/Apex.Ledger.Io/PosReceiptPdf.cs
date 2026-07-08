using System.Globalization;
using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a POS bill as a retail <b>receipt</b> (catalog §11; Phase 6 slice 7 RQ-44; Study Guide pp.240–242;
/// DP-6). Lays out the receipt top-down: title + store name, bill number/date/customer, the item table
/// (Description / Qty / Rate / Amount), the per-rate GST breakup (CGST+SGST for an intra-state supply, IGST for
/// an inter-state one), the taxable + tax + grand total, the tender lines with their references and the
/// informational change, then the two thank-you messages and the declaration. De-branded (never the word
/// "Tally", ER-11), deterministic (no clock/RNG, invariant formatting — the same bill renders byte-identically).
/// Reuses <see cref="PdfWriter"/>. A retail receipt is short, so it lays out on a single page.
/// </summary>
public static class PosReceiptPdf
{
    /// <summary>Renders the retail receipt to PDF bytes.</summary>
    public static byte[] Render(PosReceiptData data, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(page);

        string title = string.IsNullOrWhiteSpace(data.Title) ? "RETAIL INVOICE" : Debrand.Text(data.Title.Trim());
        if (string.IsNullOrWhiteSpace(title)) title = "RETAIL INVOICE";

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double width = right - left;

        var writer = new PdfWriter { DocumentTitle = SafeTitle(title) };
        writer.BeginPage(page.PageWidth, page.PageHeight);

        double y = page.PageHeight - page.MarginTop;

        // ---- Title + store ----
        y -= page.TitleFontSize;
        Center(writer, title, left, right, y, page.TitleFontSize, bold: true);
        y -= 6;
        if (!string.IsNullOrWhiteSpace(data.StoreName))
        {
            y -= page.BodyFontSize + 2;
            Center(writer, Debrand.Text(data.StoreName), left, right, y, page.BodyFontSize, bold: true);
        }
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 2;

        // ---- Bill meta ----
        writer.Text(left, y, "Bill No: " + data.BillNumber, page.BodyFontSize);
        RightText(writer, "Date: " + data.DateText, left, right, y, page.BodyFontSize, bold: false);
        y -= page.BodyFontSize + 2;
        writer.Text(left, y, "Customer: " + (string.IsNullOrWhiteSpace(data.Party) ? "(cash)" : Debrand.Text(data.Party)), page.BodyFontSize);
        y -= 6;
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;

        // ---- Item table ----
        double qtyR = left + width * 0.62;
        double rateR = left + width * 0.80;
        double amtR = right;
        writer.Text(left, y, "Item", page.BodyFontSize, bold: true);
        RightText2(writer, "Qty", qtyR - 60, qtyR, y, page.BodyFontSize, bold: true);
        RightText2(writer, "Rate", qtyR, rateR, y, page.BodyFontSize, bold: true);
        RightText2(writer, "Amount", rateR, amtR, y, page.BodyFontSize, bold: true);
        y -= 3;
        writer.Line(left, y, right, y, 0.4);
        y -= page.RowHeight;

        foreach (var it in data.Items)
        {
            var desc = PdfWriter.FitToWidth(Debrand.Text(it.Description), qtyR - 60 - left - 4, page.BodyFontSize);
            writer.Text(left, y, desc, page.BodyFontSize);
            RightText2(writer, it.QuantityText, qtyR - 60, qtyR, y, page.BodyFontSize, bold: false);
            RightText2(writer, it.RateText, qtyR, rateR, y, page.BodyFontSize, bold: false);
            RightText2(writer, Fmt(it.Value), rateR, amtR, y, page.BodyFontSize, bold: false);
            y -= page.RowHeight;
        }
        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.4);

        // ---- Totals ----
        void Total(string label, string amount, bool bold)
        {
            RightText2(writer, label, qtyR, rateR - 4, y, page.BodyFontSize, bold);
            RightText2(writer, amount, rateR, amtR, y, page.BodyFontSize, bold);
            y -= page.RowHeight;
        }
        Total("Taxable", Fmt(data.TotalTaxable), false);
        if (data.IsInterState)
        {
            Total("IGST", Fmt(data.TotalIgst), false);
        }
        else
        {
            Total("CGST", Fmt(data.TotalCgst), false);
            Total("SGST", Fmt(data.TotalSgst), false);
        }
        writer.Line(qtyR, y + page.RowHeight - 2, right, y + page.RowHeight - 2, 0.6);
        Total("Grand Total", Fmt(data.GrandTotal), true);
        y -= 2;

        // ---- Per-rate GST breakup ----
        if (data.TaxRows.Count > 0)
        {
            writer.Line(left, y, right, y, 0.5);
            y -= page.BodyFontSize + 2;
            writer.Text(left, y, "GST Breakup", page.BodyFontSize, bold: true);
            y -= page.RowHeight;
            double c0 = left + width * 0.30, c1 = left + width * 0.55, c2 = left + width * 0.80;
            writer.Text(left, y, "Rate", page.BodyFontSize, bold: true);
            RightText2(writer, "Taxable", left + width * 0.10, c0, y, page.BodyFontSize, bold: true);
            if (data.IsInterState) RightText2(writer, "IGST", c0, c1, y, page.BodyFontSize, bold: true);
            else
            {
                RightText2(writer, "CGST", c0, c1, y, page.BodyFontSize, bold: true);
                RightText2(writer, "SGST", c1, c2, y, page.BodyFontSize, bold: true);
            }
            y -= 3;
            writer.Line(left, y, right, y, 0.3);
            y -= page.RowHeight;
            foreach (var tr in data.TaxRows)
            {
                writer.Text(left, y, tr.RateLabel, page.BodyFontSize);
                RightText2(writer, Fmt(tr.TaxableValue), left + width * 0.10, c0, y, page.BodyFontSize, bold: false);
                if (data.IsInterState) RightText2(writer, Fmt(tr.Igst), c0, c1, y, page.BodyFontSize, bold: false);
                else
                {
                    RightText2(writer, Fmt(tr.Cgst), c0, c1, y, page.BodyFontSize, bold: false);
                    RightText2(writer, Fmt(tr.Sgst), c1, c2, y, page.BodyFontSize, bold: false);
                }
                y -= page.RowHeight;
            }
            y -= 2;
        }

        // ---- Tender lines ----
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;
        writer.Text(left, y, "Payment", page.BodyFontSize, bold: true);
        y -= page.RowHeight;
        foreach (var t in data.Tenders)
        {
            writer.Text(left + 6, y, Debrand.Text(t.Label), page.BodyFontSize);
            RightText2(writer, Fmt(t.Amount), rateR, amtR, y, page.BodyFontSize, bold: false);
            y -= page.RowHeight;
            if (!string.IsNullOrWhiteSpace(t.Reference))
            {
                writer.Text(left + 12, y, Debrand.Text(t.Reference), page.FooterFontSize);
                y -= page.RowHeight;
            }
        }
        if (data.CashTendered.Amount > 0m)
        {
            writer.Text(left + 6, y, "Cash Tendered", page.BodyFontSize);
            RightText2(writer, Fmt(data.CashTendered), rateR, amtR, y, page.BodyFontSize, bold: false);
            y -= page.RowHeight;
            writer.Text(left + 6, y, "Change", page.BodyFontSize);
            RightText2(writer, Fmt(data.Change), rateR, amtR, y, page.BodyFontSize, bold: false);
            y -= page.RowHeight;
        }
        y -= 2;

        // ---- Messages + declaration ----
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 4;
        void Note(string? s, double size, bool bold)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            foreach (var ln in VoucherPdf.WrapText(Debrand.Text(s), width, size))
            {
                Center(writer, ln, left, right, y, size, bold);
                y -= size + 3;
            }
        }
        Note(data.Message1, page.BodyFontSize, true);
        Note(data.Message2, page.BodyFontSize, false);
        if (!string.IsNullOrWhiteSpace(data.Declaration))
        {
            y -= 3;
            Note(data.Declaration, page.FooterFontSize, false);
        }

        // Footer.
        string footer = (page.FooterText ?? string.Empty).Replace("{page}", "1").Replace("{pages}", "1");
        if (footer.Length > 0)
            Center(writer, footer, left, right, page.MarginBottom, page.FooterFontSize, bold: false);

        return writer.Build();
    }

    private static string Fmt(Money m) => m.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static void Center(PdfWriter w, string text, double left, double right, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = (left + right) / 2.0 - tw / 2.0;
        if (x < left) x = left;
        w.Text(x, y, text, size, bold);
    }

    // Right-aligns text within [cellLeft, cellRight].
    private static void RightText2(PdfWriter w, string text, double cellLeft, double cellRight, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = cellRight - tw;
        if (x < cellLeft) x = cellLeft;
        w.Text(x, y, text, size, bold);
    }

    // Right-aligns to the page right edge from a hint left.
    private static void RightText(PdfWriter w, string text, double left, double right, double y, double size, bool bold)
        => RightText2(w, text, left, right, y, size, bold);

    private static string SafeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Apex Solutions Retail Receipt" : Debrand.Text(title) + " — Apex Solutions";
}
