using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Reports;

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
/// <para><b>Also renders a BILL OF SUPPLY</b> (W0-1 / census T0-7), when <see cref="InvoicePrintData.IsBillOfSupply"/>
/// is set — the document CGST Act §31(3)(c) requires "instead of a tax invoice" from a registered person supplying
/// exempted goods or services, or paying tax under §10 (composition). CGST Rule 49 prescribes eight particulars and
/// <b>none of them is a rate or an amount of tax</b>, so that document drops the per-head totals, the per-rate breakup
/// table and the intra/inter (CGST+SGST / IGST) caption, states Rule 49(g)'s "Value of Supply" rather than a "Taxable
/// Value", and — for a composition supplier — carries the Rule 5(1)(f) declaration at the TOP, immediately under the
/// title. The F12 title override does not apply to it: the document kind follows the supply, not a print preference.
/// </para>
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

        // W0-1 (T0-7): the title states which document this IS in law, and on a bill of supply that is not negotiable.
        // The F12 title override exists so an operator can print e.g. "PROFORMA INVOICE"; letting it apply to a bill of
        // supply would reissue, through a print knob, exactly the tax invoice CGST §31(3)(c) forbids a composition or
        // exempt supplier to issue. Copy marking (Original/Duplicate) is unaffected and still available.
        // FIX-W1g: the two titles are read from the single GstReportSupport constants rather than re-spelled here.
        // They were previously literal in four places (the two consts, the DTO default and two fallbacks below), so
        // the consts' own doc claim to be "the single source both the print projector and the renderer read" was
        // false as written and the literals could drift. Apex.Ledger.Io already project-references Apex.Ledger.
        string title;
        if (data.IsBillOfSupply)
        {
            // FIX-W1h: derive STRUCTURALLY, never by trusting the DTO. `InvoicePrintData.DocumentTitle` defaulted to
            // the non-blank string "TAX INVOICE", so this branch's blank-only guard could not catch a caller that set
            // IsBillOfSupply without a title: it suppressed every tax head, the breakup and the intra/inter caption —
            // and then TITLED the page TAX INVOICE and stamped that into the PDF metadata, i.e. produced precisely the
            // document §31(3)(c) forbids, from the renderer whose comment advertises it is safe for any future caller.
            // The DTO default is now empty as well, so this is belt-and-braces on both sides.
            // FIX-W2b: the rejection is CASE-INSENSITIVE (and trims). It used to be ordinal equality against the
            // upper-case constant, so a DTO carrying "Tax Invoice" — the exact spelling the drilled-voucher badge and
            // this project's own prose use — sailed through and printed a bill of supply headed "Tax Invoice", with
            // that string stamped into the PDF metadata too. Every tax head was still correctly suppressed, so only
            // the self-description lied, which is the hardest kind to notice.
            title = data.DocumentTitle;
            if (string.IsNullOrWhiteSpace(title) ||
                title.Trim().Equals(GstReportSupport.TaxInvoiceTitle, StringComparison.OrdinalIgnoreCase))
                title = GstReportSupport.BillOfSupplyTitle;
        }
        else
        {
            title = string.IsNullOrWhiteSpace(config.TitleOverride)
                ? (string.IsNullOrWhiteSpace(data.DocumentTitle) ? GstReportSupport.TaxInvoiceTitle : data.DocumentTitle)
                : Debrand.Text(config.TitleOverride!.Trim());
            // guard: don't let de-brand blank the title
            if (string.IsNullOrWhiteSpace(title)) title = GstReportSupport.TaxInvoiceTitle;
        }

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

    /// <summary>The Rule 5(1)(f) declaration wrapped to the content width, or empty when the document bears none.
    /// Measured and drawn from ONE place so <see cref="FirstHeaderHeight"/> and <see cref="DrawFirstHeader"/> cannot
    /// drift apart.
    /// <para><b>W0-1 follow-up (review finding #6) — gated on the STRUCTURAL flag, not merely on the string being
    /// non-blank.</b> FIX-W1h/FIX-W2b made the TITLE renderer-derived precisely so this layer would be "safe against
    /// any future caller that does not" gate it; the declaration was left caller-trusted in exactly the way the title
    /// no longer is. A caller writing <c>{ TopDeclaration = …, IsBillOfSupply = false, TotalCgst = … }</c> — the
    /// mirror of the mistake FIX-W1h fixed — centred "composition taxable person, not eligible to collect tax on
    /// supplies" over a page that goes on to print CGST and SGST head lines: the badge/declaration contradiction
    /// FIX-W1e removed from the drilled-voucher pane, reborn in the renderer. Rule 5(1)(f) binds the wording to the
    /// bill of supply he issues, so it can only ever appear on one.</para></summary>
    private static List<string> TopDeclarationLines(InvoicePrintData data, PageConfig page) =>
        !data.IsBillOfSupply || string.IsNullOrWhiteSpace(data.TopDeclaration)
            ? new List<string>()
            : VoucherPdf.WrapText(Debrand.Text(data.TopDeclaration.Trim()), page.ContentWidth, page.BodyFontSize);

    private static double FirstHeaderHeight(InvoicePrintData data, PageConfig page)
    {
        double h = page.TitleFontSize + 8 + 4;   // title band + rule
        // CGST Rule 5(1)(f): the composition declaration sits "at the top of the bill of supply" — immediately under
        // the title band, above the party blocks. Absent on every other document ⇒ zero height ⇒ byte-identical.
        int declLines = TopDeclarationLines(data, page).Count;
        if (declLines > 0) h += declLines * (page.BodyFontSize + 2) + 4;
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

        // The document must not describe itself as something it is not: a bill of supply is not an invoice.
        string declaration = data.IsBillOfSupply
            ? "Declaration: We declare that this bill of supply shows the actual price of the goods described and " +
              "that all particulars are true and correct."
            : "Declaration: We declare that this invoice shows the actual price of the goods described and that " +
              "all particulars are true and correct.";
        var declLines = VoucherPdf.WrapText(declaration, page.ContentWidth * 0.62, page.FooterFontSize);

        // Totals rows: Taxable + (IGST | CGST+SGST) + optional Cess + optional Round Off + Grand Total.
        // The Cess row appears only on a cess-bearing invoice, so a cess-free one measures (and renders) as before.
        // A BILL OF SUPPLY has no tax head to state (Rule 49 prescribes none), so it is value + optional round-off +
        // total — kept in lockstep with DrawClosingBlock below.
        int totalRows = data.IsBillOfSupply
            ? 1 + (data.RoundOff.Amount != 0m ? 1 : 0) + 1
            : 1 + (data.IsInterState ? 1 : 2)
                + (data.TotalCess.Amount != 0m ? 1 : 0)
                + (data.RoundOff.Amount != 0m ? 1 : 0) + 1;
        double h = 2 + totalRows * page.RowHeight + 2;

        if (!data.IsBillOfSupply && data.TaxRows.Count > 0)
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

        // CGST Rule 5(1)(f) — "at the top of the bill of supply issued by him". Drawn here, directly beneath the title
        // rule and above the supplier/recipient blocks, so it is the first thing read after the document's name.
        var declLines = TopDeclarationLines(data, page);
        if (declLines.Count > 0)
        {
            foreach (var dl in declLines)
            {
                y -= page.BodyFontSize + 2;
                Center(writer, dl, left, right, y, page.BodyFontSize, bold: true);
            }
            y -= 4;
        }

        double blockTop = y;
        double sellerY = DrawPartyBlock(writer, page, "Supplier:", data.Seller, left, blockTop);
        double buyerY = DrawPartyBlock(writer, page, "Recipient (Bill to):", data.Buyer, geo.MidX + 6, blockTop);
        y = Math.Min(sellerY, buyerY) - 4;

        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;

        // FIX-W1f: the document-number caption must name the document this IS. A bill of supply captioned "Invoice No:"
        // calls its own serial an invoice number on a page whose title band reads BILL OF SUPPLY and whose closing
        // declaration (already fixed by W0-1) says "this bill of supply shows the actual price" — the same
        // self-description error, one screenful apart. It also disagreed with the on-screen preview mirror, which W0-1
        // DID change to "Bill of Supply No.", so the operator approved one caption and issued another.
        writer.Text(left, y, (data.IsBillOfSupply ? "Bill of Supply No: " : "Invoice No: ") + data.InvoiceNumber,
            page.BodyFontSize, bold: false);
        writer.Text(geo.MidX + 6, y, "Date: " + data.InvoiceDateText, page.BodyFontSize, bold: false);
        y -= page.BodyFontSize + 2;
        // v48 (numbering §8): the buyer's reference (e.g. their PO / "Reference No."). Printed only when captured,
        // so an invoice without one is byte-identical to before (ER-13).
        if (!string.IsNullOrWhiteSpace(data.ReferenceNo))
        {
            var refLine = data.ReferenceCaption + ": " + data.ReferenceNo;
            if (!string.IsNullOrWhiteSpace(data.ReferenceDateText)) refLine += "   Dated: " + data.ReferenceDateText;
            writer.Text(left, y, refLine, page.BodyFontSize, bold: false);
            y -= page.BodyFontSize + 2;
        }
        writer.Text(left, y, "Place of Supply: " + data.PlaceOfSupply, page.BodyFontSize, bold: false);
        // The intra/inter caption names a TAX HEAD ("CGST + SGST" / "IGST"). CGST Rule 49 prescribes no rate and no
        // tax-amount particular, and a composition supplier may collect none (§10(4), §32(2)), so a bill of supply
        // states no head at all. Occupies the same line as Place of Supply ⇒ dropping it changes no height.
        if (!data.IsBillOfSupply)
        {
            string supply = data.IsInterState ? "Inter-State (IGST)" : "Intra-State (CGST + SGST)";
            writer.Text(geo.MidX + 6, y, supply, page.BodyFontSize, bold: false);
        }
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

        // CGST Rule 49(g) — "value of supply of goods or services or both taking into account discount or abatement".
        // "Taxable Value" is a tax concept and would contradict the document: this supply is precisely NOT taxable.
        TotalLine(data.IsBillOfSupply ? "Value of Supply" : "Taxable Value", Fmt(data.TotalTaxable), false);
        if (!data.IsBillOfSupply)
        {
            if (data.IsInterState)
            {
                TotalLine("IGST", Fmt(data.TotalIgst), false);
            }
            else
            {
                TotalLine("CGST", Fmt(data.TotalCgst), false);
                TotalLine("SGST", Fmt(data.TotalSgst), false);
            }
            // Compensation Cess gets its OWN line — it is ring-fenced from the GST heads (never folded into
            // CGST/SGST/IGST) but it IS charged to the recipient, so it must appear on the bill and reach the Grand
            // Total. Printed only when non-zero, so a cess-free invoice renders exactly as before (ER-13).
            if (data.TotalCess.Amount != 0m)
                TotalLine("Compensation Cess", Fmt(data.TotalCess), false);
        }
        if (data.RoundOff.Amount != 0m)
            TotalLine("Round Off", FmtSigned(data.RoundOff), false);
        writer.Line(geo.QtyRight, y + page.RowHeight - 2, right, y + page.RowHeight - 2, 0.7);
        TotalLine(data.IsBillOfSupply ? "Total" : "Grand Total", Fmt(data.GrandTotal), true);
        y -= 2;

        // --- Per-rate tax breakup table --- (never on a bill of supply: Rule 49 prescribes no rate particular, and
        // showing one would assert a collection §10(4) / §32(2) forbid. Belt-and-braces — the projector already
        // suppresses the rows; this makes the renderer safe against any future caller that does not.)
        if (!data.IsBillOfSupply && data.TaxRows.Count > 0)
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

    /// <summary>Money on the tax invoice, Indian-grouped via <see cref="IndianMoneyFormat"/> — the ONE grouping
    /// rule (drift lock D2). This previously formatted against <see cref="CultureInfo.InvariantCulture"/>, whose
    /// flat group size of 3 printed ₹1,00,000 as "100,000.00" on the invoice while the very same assembly printed
    /// "1,00,000.00" on a Form-16A/27D certificate.</summary>
    private static string Fmt(Money m) => IndianMoneyFormat.Amount(m.Amount);

    private static string FmtSigned(Money m)
    {
        string s = IndianMoneyFormat.Amount(Math.Abs(m.Amount));
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

    /// <summary>The PDF metadata title. Follows the document's own title, so a bill of supply's file properties do not
    /// announce a tax invoice either.</summary>
    private static string SafeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Apex Solutions Tax Invoice" : Debrand.Text(title) + " — Apex Solutions";
}
