using System.Globalization;
using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders an item-invoice (Sales) as a built-in GST <b>tax invoice</b> (RQ-11; CGST Rule 46). Lays out the
/// mandatory Rule-46 particulars: the document title + copy-marking label, the seller (supplier) and buyer
/// (recipient) name/address/GSTIN blocks, invoice number + date + place of supply, the item table
/// (Sr / Description / HSN / Qty / Rate / Amount), the GST breakup (CGST+SGST per rate for an intra-state
/// supply, or IGST per rate for an inter-state supply), the taxable value + tax + grand total, the total in
/// words (Indian numbering, paisa-accurate), and a declaration + signature block. De-branded, deterministic
/// (no clock/RNG, invariant formatting) — the same invoice renders byte-identically. Reuses
/// <see cref="PdfWriter"/>.
///
/// <para>Paginates like <see cref="ReportPdf"/>: a long invoice whose item rows overflow the page starts a
/// continuation page (repeating the item-table column header), and the closing block (totals + GST breakup +
/// amount-in-words + declaration/signature) is kept together — moved to a fresh page if it would not fit under
/// the last item row. The footer shows "Page N of M".</para>
/// </summary>
public static class InvoicePdf
{
    /// <summary>Renders the tax invoice to PDF bytes.</summary>
    public static byte[] Render(InvoicePrintData data, PrintConfig config, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(page);

        string title = string.IsNullOrWhiteSpace(config.TitleOverride)
            ? "TAX INVOICE"
            : Debrand.Text(config.TitleOverride!.Trim());
        if (string.IsNullOrWhiteSpace(title)) title = "TAX INVOICE"; // guard: don't let de-brand blank the title

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double bottom = page.MarginBottom + page.FooterFontSize + 6;
        var geo = new Geometry(left, right, page);

        // ---- Pre-build the closing block's lines so we can measure it and keep it together. ----
        var closing = BuildClosing(data, config, page);

        // ---- Paginate the item rows, reserving room for the closing block on whichever page it lands. ----
        var pages = new List<List<(int Sr, InvoiceItemRow Row)>>();
        var current = new List<(int, InvoiceItemRow)>();
        double y = page.PageHeight - page.MarginTop - FirstHeaderHeight(data, page);
        int sr = 1;
        foreach (var item in data.Items)
        {
            if (y - page.RowHeight < bottom && current.Count > 0)
            {
                pages.Add(current);
                current = new List<(int, InvoiceItemRow)>();
                y = page.PageHeight - page.MarginTop - ContinuationHeaderHeight(page);
            }
            current.Add((sr, item));
            y -= page.RowHeight;
            sr++;
        }
        pages.Add(current);

        bool closingOnNewPage = y - closing.Height < bottom;
        int total = pages.Count + (closingOnNewPage ? 1 : 0);

        var writer = new PdfWriter { DocumentTitle = SafeTitle(title) };

        for (int p = 0; p < pages.Count; p++)
        {
            writer.BeginPage(page.PageWidth, page.PageHeight);
            bool isFirst = p == 0;
            double yy = isFirst
                ? DrawFirstHeader(writer, data, config, page, title, geo, left, right)
                : DrawContinuationHeader(writer, page, title, left, right);
            yy = DrawItemTableHeader(writer, page, geo, left, right, yy);
            foreach (var (rowSr, row) in pages[p])
                yy = DrawItemRow(writer, page, geo, left, right, rowSr, row, yy);
            writer.Line(left, yy + page.RowHeight - 3, right, yy + page.RowHeight - 3, 0.5);

            if (p == pages.Count - 1 && !closingOnNewPage)
                DrawClosingBlock(writer, data, config, page, geo, left, right, closing, yy - 2);

            DrawFooter(writer, page, left, right, p + 1, total);
        }

        if (closingOnNewPage)
        {
            writer.BeginPage(page.PageWidth, page.PageHeight);
            double yy = DrawContinuationHeader(writer, page, title, left, right);
            DrawClosingBlock(writer, data, config, page, geo, left, right, closing, yy);
            DrawFooter(writer, page, left, right, total, total);
        }

        return writer.Build();
    }

    // ================================================================ geometry

    private sealed class Geometry
    {
        public readonly double SrX, DescX, HsnLeft, HsnRight, QtyRight, RateRight, AmtRight, MidX;
        public Geometry(double left, double right, PageConfig page)
        {
            SrX = left;
            DescX = left + 26;
            HsnLeft = left + page.ContentWidth * 0.48;
            HsnRight = left + page.ContentWidth * 0.60;
            QtyRight = left + page.ContentWidth * 0.72;
            RateRight = left + page.ContentWidth * 0.86;
            AmtRight = right;
            MidX = left + page.ContentWidth / 2.0;
        }
    }

    // ================================================================ header heights (kept in sync w/ drawing)

    private static double FirstHeaderHeight(InvoicePrintData data, PageConfig page)
    {
        double h = page.TitleFontSize + 8 + 4;   // title band + rule
        h += PartyBlockHeight(data.Seller, data.Buyer, page) + 4;
        h += 0.5;                                 // rule
        h += (page.BodyFontSize + 2) * 2;         // invoice-no/date + place-of-supply rows
        h += 6;                                   // rule spacer
        h += page.BodyFontSize + 2;               // item-table header offset
        return h;
    }

    private static double ContinuationHeaderHeight(PageConfig page) =>
        page.TitleFontSize + 8 + 4 + page.BodyFontSize + 2;

    private static double PartyBlockHeight(InvoicePartyBlock seller, InvoicePartyBlock buyer, PageConfig page)
    {
        int Lines(InvoicePartyBlock b)
        {
            int n = 2;                                       // caption + name
            foreach (var l in b.AddressLines) if (!string.IsNullOrWhiteSpace(l)) n++;
            if (!string.IsNullOrWhiteSpace(b.StateText)) n++;
            n++;                                             // GSTIN line (always shown)
            return n;
        }
        int maxLines = Math.Max(Lines(seller), Lines(buyer));
        return maxLines * (page.BodyFontSize + 1);
    }

    // ================================================================ closing block model

    private sealed class Closing
    {
        public required List<string> WordLines { get; init; }
        public required List<string> NarrationLines { get; init; }
        public required List<string> DeclarationLines { get; init; }
        public required double Height { get; init; }
    }

    private static Closing BuildClosing(InvoicePrintData data, PrintConfig config, PageConfig page)
    {
        string words = "Amount Chargeable (in words): " + IndianAmountInWords.Convert(data.GrandTotal.Amount);
        var wordLines = VoucherPdf.WrapText(words, page.ContentWidth, page.BodyFontSize);

        var narrationLines = (config.ShowNarration && !string.IsNullOrWhiteSpace(data.Narration))
            ? VoucherPdf.WrapText("Remarks: " + Debrand.Text(data.Narration), page.ContentWidth, page.BodyFontSize)
            : new List<string>();

        const string declaration =
            "Declaration: We declare that this invoice shows the actual price of the goods described and that " +
            "all particulars are true and correct.";
        var declLines = VoucherPdf.WrapText(declaration, page.ContentWidth * 0.62, page.FooterFontSize);

        // Totals rows: Taxable + (IGST | CGST+SGST) + optional Round Off + Grand Total.
        int totalRows = 1 + (data.IsInterState ? 1 : 2) + (data.RoundOff.Amount != 0m ? 1 : 0) + 1;
        double h = 2 + totalRows * page.RowHeight + 2;

        if (data.TaxRows.Count > 0)
        {
            h += page.BodyFontSize + 2                 // "GST Breakup" caption + rule
               + page.RowHeight                        // caption row
               + page.BodyFontSize + 2                 // head row + rule
               + data.TaxRows.Count * page.RowHeight
               + 2;
        }

        h += page.BodyFontSize + 2 + wordLines.Count * (page.BodyFontSize + 3) + 4;   // amount-in-words + rule
        if (narrationLines.Count > 0) h += narrationLines.Count * (page.BodyFontSize + 3) + 4;
        h += page.FooterFontSize + 2 + declLines.Count * (page.FooterFontSize + 2);   // declaration + rule + signature

        return new Closing { WordLines = wordLines, NarrationLines = narrationLines, DeclarationLines = declLines, Height = h };
    }

    // ================================================================ header drawing

    private static double DrawFirstHeader(
        PdfWriter writer, InvoicePrintData data, PrintConfig config, PageConfig page,
        string title, Geometry geo, double left, double right)
    {
        double y = page.PageHeight - page.MarginTop;

        y -= page.TitleFontSize;
        Center(writer, title, left, right, y, page.TitleFontSize, bold: true);
        if (config.CopyMarking != CopyMarking.None)
        {
            string label = config.CopyMarkingLabel;
            double w = PdfWriter.MeasureHelvetica(label, page.FooterFontSize);
            writer.Text(right - w, y, label, page.FooterFontSize, bold: true);
        }
        y -= 8;
        writer.Line(left, y, right, y, 0.8);
        y -= 4;

        double blockTop = y;
        double sellerY = DrawPartyBlock(writer, page, "Supplier:", data.Seller, left, blockTop);
        double buyerY = DrawPartyBlock(writer, page, "Recipient (Bill to):", data.Buyer, geo.MidX + 6, blockTop);
        y = Math.Min(sellerY, buyerY) - 4;

        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;

        writer.Text(left, y, "Invoice No: " + data.InvoiceNumber, page.BodyFontSize, bold: false);
        writer.Text(geo.MidX + 6, y, "Date: " + data.InvoiceDateText, page.BodyFontSize, bold: false);
        y -= page.BodyFontSize + 2;
        writer.Text(left, y, "Place of Supply: " + data.PlaceOfSupply, page.BodyFontSize, bold: false);
        string supply = data.IsInterState ? "Inter-State (IGST)" : "Intra-State (CGST + SGST)";
        writer.Text(geo.MidX + 6, y, supply, page.BodyFontSize, bold: false);
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 2;
        return y;
    }

    private static double DrawContinuationHeader(PdfWriter writer, PageConfig page, string title, double left, double right)
    {
        double y = page.PageHeight - page.MarginTop;
        y -= page.TitleFontSize;
        Center(writer, title + " (continued)", left, right, y, page.TitleFontSize, bold: true);
        y -= 8;
        writer.Line(left, y, right, y, 0.8);
        y -= 4;
        y -= page.BodyFontSize + 2;
        return y;
    }

    private static double DrawItemTableHeader(PdfWriter writer, PageConfig page, Geometry geo, double left, double right, double y)
    {
        writer.Text(geo.SrX, y, "Sr", page.BodyFontSize, bold: true);
        writer.Text(geo.DescX, y, "Description", page.BodyFontSize, bold: true);
        RightText(writer, "HSN/SAC", geo.HsnLeft, geo.HsnRight, y, page.BodyFontSize, bold: true);
        RightText(writer, "Qty", geo.HsnRight, geo.QtyRight, y, page.BodyFontSize, bold: true);
        RightText(writer, "Rate", geo.QtyRight, geo.RateRight, y, page.BodyFontSize, bold: true);
        RightText(writer, "Amount", geo.RateRight, geo.AmtRight, y, page.BodyFontSize, bold: true);
        y -= 3;
        writer.Line(left, y, right, y, 0.5);
        y -= page.RowHeight;
        return y;
    }

    private static double DrawItemRow(PdfWriter writer, PageConfig page, Geometry geo, double left, double right, int sr, InvoiceItemRow item, double y)
    {
        writer.Text(geo.SrX, y, sr.ToString(CultureInfo.InvariantCulture), page.BodyFontSize, bold: false);
        string desc = PdfWriter.FitToWidth(item.Description, geo.HsnLeft - geo.DescX - 4, page.BodyFontSize);
        writer.Text(geo.DescX, y, desc, page.BodyFontSize, bold: false);
        RightText(writer, item.HsnSac, geo.HsnLeft, geo.HsnRight, y, page.BodyFontSize, bold: false);
        RightText(writer, item.QuantityText, geo.HsnRight, geo.QtyRight, y, page.BodyFontSize, bold: false);
        RightText(writer, item.RateText, geo.QtyRight, geo.RateRight, y, page.BodyFontSize, bold: false);
        RightText(writer, Fmt(item.TaxableValue), geo.RateRight, geo.AmtRight, y, page.BodyFontSize, bold: false);
        return y - page.RowHeight;
    }

    // ================================================================ closing block drawing

    private static void DrawClosingBlock(
        PdfWriter writer, InvoicePrintData data, PrintConfig config, PageConfig page,
        Geometry geo, double left, double right, Closing closing, double y)
    {
        // --- Totals block (right-aligned labels + amounts) ---
        double labelRight = geo.RateRight - 4;
        void TotalLine(string label, string amount, bool bold)
        {
            RightText(writer, label, geo.QtyRight, labelRight, y, page.BodyFontSize, bold);
            RightText(writer, amount, geo.RateRight, geo.AmtRight, y, page.BodyFontSize, bold);
            y -= page.RowHeight;
        }

        TotalLine("Taxable Value", Fmt(data.TotalTaxable), false);
        if (data.IsInterState)
        {
            TotalLine("IGST", Fmt(data.TotalIgst), false);
        }
        else
        {
            TotalLine("CGST", Fmt(data.TotalCgst), false);
            TotalLine("SGST", Fmt(data.TotalSgst), false);
        }
        if (data.RoundOff.Amount != 0m)
            TotalLine("Round Off", FmtSigned(data.RoundOff), false);
        writer.Line(geo.QtyRight, y + page.RowHeight - 2, right, y + page.RowHeight - 2, 0.7);
        TotalLine("Grand Total", Fmt(data.GrandTotal), true);
        y -= 2;

        // --- Per-rate tax breakup table ---
        if (data.TaxRows.Count > 0)
        {
            writer.Line(left, y, right, y, 0.5);
            y -= page.BodyFontSize + 2;
            writer.Text(left, y, "GST Breakup", page.BodyFontSize, bold: true);
            y -= page.RowHeight;

            double rTaxableR = left + page.ContentWidth * 0.30;
            double rC1R = left + page.ContentWidth * 0.53;
            double rC2R = left + page.ContentWidth * 0.76;

            writer.Text(left, y, "Rate", page.BodyFontSize, bold: true);
            RightText(writer, "Taxable", left + page.ContentWidth * 0.10, rTaxableR, y, page.BodyFontSize, bold: true);
            if (data.IsInterState)
            {
                RightText(writer, "IGST", rTaxableR, rC1R, y, page.BodyFontSize, bold: true);
            }
            else
            {
                RightText(writer, "CGST", rTaxableR, rC1R, y, page.BodyFontSize, bold: true);
                RightText(writer, "SGST", rC1R, rC2R, y, page.BodyFontSize, bold: true);
            }
            y -= 3;
            writer.Line(left, y, right, y, 0.4);
            y -= page.RowHeight;

            foreach (var tr in data.TaxRows)
            {
                writer.Text(left, y, tr.RateLabel, page.BodyFontSize, bold: false);
                RightText(writer, Fmt(tr.TaxableValue), left + page.ContentWidth * 0.10, rTaxableR, y, page.BodyFontSize, bold: false);
                if (data.IsInterState)
                {
                    RightText(writer, Fmt(tr.Igst), rTaxableR, rC1R, y, page.BodyFontSize, bold: false);
                }
                else
                {
                    RightText(writer, Fmt(tr.Cgst), rTaxableR, rC1R, y, page.BodyFontSize, bold: false);
                    RightText(writer, Fmt(tr.Sgst), rC1R, rC2R, y, page.BodyFontSize, bold: false);
                }
                y -= page.RowHeight;
            }
            y -= 2;
        }

        // --- Amount in words ---
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;
        foreach (var wl in closing.WordLines)
        {
            writer.Text(left, y, wl, page.BodyFontSize, bold: false);
            y -= page.BodyFontSize + 3;
        }
        y -= 4;

        // --- Narration (F12 toggle) ---
        if (closing.NarrationLines.Count > 0)
        {
            foreach (var nl in closing.NarrationLines)
            {
                writer.Text(left, y, nl, page.BodyFontSize, bold: false);
                y -= page.BodyFontSize + 3;
            }
            y -= 4;
        }

        // --- Declaration + signature block ---
        writer.Line(left, y, right, y, 0.5);
        y -= page.FooterFontSize + 2;
        double declTop = y;
        foreach (var dl in closing.DeclarationLines)
        {
            writer.Text(left, y, dl, page.FooterFontSize, bold: false);
            y -= page.FooterFontSize + 2;
        }

        // Signature (right column), aligned to the top of the declaration.
        double sigY = declTop + (page.FooterFontSize + 2);
        string forCompany = "For " + data.Seller.Name;
        writer.Text(right - PdfWriter.MeasureHelvetica(forCompany, page.BodyFontSize), sigY, forCompany, page.BodyFontSize, bold: true);
        const string authSig = "Authorised Signatory";
        writer.Text(right - PdfWriter.MeasureHelvetica(authSig, page.FooterFontSize), y, authSig, page.FooterFontSize, bold: false);
    }

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right, int pageNo, int pageCount)
    {
        string footer = (page.FooterText ?? string.Empty)
            .Replace("{page}", pageNo.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", pageCount.ToString(CultureInfo.InvariantCulture));
        if (footer.Length > 0)
            Center(writer, footer, left, right, page.MarginBottom, page.FooterFontSize, bold: false);
    }

    // ================================================================ helpers

    private static double DrawPartyBlock(PdfWriter writer, PageConfig page, string caption, InvoicePartyBlock party, double x, double y)
    {
        writer.Text(x, y, caption, page.FooterFontSize, bold: true);
        y -= page.BodyFontSize + 1;
        writer.Text(x, y, party.Name, page.BodyFontSize, bold: true);
        y -= page.BodyFontSize + 1;
        foreach (var line in party.AddressLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            writer.Text(x, y, line, page.FooterFontSize, bold: false);
            y -= page.FooterFontSize + 1;
        }
        if (!string.IsNullOrWhiteSpace(party.StateText))
        {
            writer.Text(x, y, "State: " + party.StateText, page.FooterFontSize, bold: false);
            y -= page.FooterFontSize + 1;
        }
        string gstin = string.IsNullOrWhiteSpace(party.Gstin) ? "GSTIN: Unregistered" : "GSTIN: " + party.Gstin;
        writer.Text(x, y, gstin, page.FooterFontSize, bold: true);
        y -= page.FooterFontSize + 1;
        return y;
    }

    private static string Fmt(Money m) => m.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string FmtSigned(Money m)
    {
        string s = Math.Abs(m.Amount).ToString("#,##0.00", CultureInfo.InvariantCulture);
        return m.Amount < 0m ? "-" + s : s;
    }

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

    private static string SafeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Apex Solutions Tax Invoice" : Debrand.Text(title) + " — Apex Solutions";
}
