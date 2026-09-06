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
        if (data.IsRecipientRecord)
        {
            // T0-11 slice S2 — a document we did NOT issue. It is tested FIRST, ahead of the bill-of-supply branch,
            // because both outward titles are refused here and letting that branch win would title an inward supply
            // BILL OF SUPPLY — the document CGST Rule 49 opens by putting on "the SUPPLIER". Structural, exactly as
            // FIX-W1h/FIX-W2b made the bill-of-supply title structural: derived from the flag, never trusted from the
            // DTO, and the refusal is case-insensitive and trims, because "Tax Invoice" is the spelling this app's
            // own badge and prose use and an ordinal compare let it straight through once already.
            title = data.DocumentTitle;
            if (string.IsNullOrWhiteSpace(title)
                || title.Trim().Equals(GstReportSupport.TaxInvoiceTitle, StringComparison.OrdinalIgnoreCase)
                || title.Trim().Equals(GstReportSupport.BillOfSupplyTitle, StringComparison.OrdinalIgnoreCase))
                title = GstReportSupport.PurchaseRecordTitle;
            // The F12 title override does not reach here, for the same reason it does not reach a bill of supply:
            // the document kind follows the transaction, not a print preference, and a knob that could re-title a
            // record into a tax invoice would issue through the print dialog the document §31(1) denies us.
        }
        else if (data.StatesSection34Note)
        {
            // T0-11 slice S4 — a §34 note we ISSUE. The NATURE OF THE DOCUMENT is a mandatory Rule-53 particular, so
            // the F12 title override does not reach it, for exactly the reason it does not reach a bill of supply or
            // a record: the document kind follows the transaction, not a print preference, and a knob that could
            // re-title a credit note "TAX INVOICE" would state on paper that we supplied something we did not.
            // (A note we merely RECORD took the IsRecipientRecord branch above, which already refuses the override.)
            title = data.DocumentTitle?.Trim() ?? string.Empty;
            // Structural, like the two branches around it: a note may bear a NOTE title and nothing else. But unlike
            // the record branch — whose class has exactly one title — there are TWO here, and the DTO does not say
            // which, so a caller that supplied an outward title (or none) has named a kind this flag contradicts and
            // there is no correct substitution to make. The page is left UNTITLED rather than titled with a guess:
            // an untitled page states nothing false, and refusing is this codebase's settled direction wherever a
            // document cannot be identified. Unreachable from the projector, which always sets one of the two.
            if (!title.Equals(GstReportSupport.CreditNoteTitle, StringComparison.OrdinalIgnoreCase)
                && !title.Equals(GstReportSupport.DebitNoteTitle, StringComparison.OrdinalIgnoreCase))
                title = string.Empty;
        }
        else if (data.IsBillOfSupply)
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
                y = page.PageHeight - page.MarginTop - ContinuationHeaderHeight(data, page);
            }
            current.Add((sr, item));
            y -= page.RowHeight;
            sr++;
        }
        pages.Add(current);

        // T0-11 review C4/L1-04, third measurement. `y` here is the paginator's position for the LAST item row; the
        // closing block is drawn from `yy - 2`, and `yy` is one ROW below that row's baseline (DrawItemRow returns
        // `y - RowHeight`). Measured without the row, this check believed the closing started a whole row higher than
        // it does and kept it on a page it did not fit: on a Letter page of 30 rows "Authorised Signatory" was drawn
        // at y = 44.00 against a footer occupying [36, 44] — the closing block's own version of the last-item-row
        // defect, and invisible to any assertion about the header alone.
        bool closingOnNewPage = y - page.RowHeight - closing.Height < bottom;
        int total = pages.Count + (closingOnNewPage ? 1 : 0);

        var writer = new PdfWriter { DocumentTitle = SafeTitle(title) };

        // W2-31 (census 12.4) F10 — the SAME rule ReportPdf and VoucherPdf apply. StartPageNumber RENUMBERS sheet
        // one; the range SELECTS which sheets are drawn and never renumbers them. Defaults reproduce the shipped
        // bytes exactly (ER-13).
        int firstNumber = page.StartPageNumber < 1 ? 1 : page.StartPageNumber;
        int lastNumber = firstNumber + total - 1;
        int drawn = 0;

        for (int p = 0; p < pages.Count; p++)
        {
            if (!page.IncludesPage(p + 1)) continue;   // outside the F10 range — not drawn at all
            writer.BeginPage(page.PageWidth, page.PageHeight);
            // 🔴 "First" is the sheet's place in the DOCUMENT, not in the selection. DrawFirstHeader is what carries
            // the Rule 46(a) party blocks, the Rule 48(1) copy marking and the Rule 5(1)(f) declaration; deriving it
            // from the drawn count would move that whole statutory header onto sheet 3 whenever sheet 3 is what the
            // operator reprints, producing a page the document never had.
            bool isFirst = p == 0;
            double yy = isFirst
                ? DrawFirstHeader(writer, data, config, page, title, geo, left, right)
                : DrawContinuationHeader(writer, data, page, title, left, right);
            yy = DrawItemTableHeader(writer, page, geo, left, right, yy);
            foreach (var (rowSr, row) in pages[p])
                yy = DrawItemRow(writer, page, geo, left, right, rowSr, row, yy);
            writer.Line(left, yy + page.RowHeight - 3, right, yy + page.RowHeight - 3, 0.5);

            if (p == pages.Count - 1 && !closingOnNewPage)
                DrawClosingBlock(writer, data, config, page, geo, left, right, closing, yy - 2);

            DrawFooter(writer, page, left, right, firstNumber + p, lastNumber);
            drawn++;
        }

        if (closingOnNewPage && page.IncludesPage(total))
        {
            writer.BeginPage(page.PageWidth, page.PageHeight);
            double yy = DrawContinuationHeader(writer, data, page, title, left, right);
            DrawClosingBlock(writer, data, config, page, geo, left, right, closing, yy);
            DrawFooter(writer, page, left, right, lastNumber, lastNumber);
            drawn++;
        }

        // A PDF must carry at least one page. An out-of-bounds range yields ONE BLANK SHEET rather than the whole
        // invoice — falling back to "print everything" would hand the operator a full tax invoice he did not ask
        // to reprint.
        if (drawn == 0)
            writer.BeginPage(page.PageWidth, page.PageHeight);

        // W2-31 (census 12.4) F5: collated copies of the whole invoice — the case the knob exists for, since
        // CGST Rule 48(1) prepares a goods invoice in triplicate. The COPY MARKING is a separate knob
        // (<see cref="PrintConfig.CopyMarking"/>): this repeats the document, it does not re-label each set.
        // One copy repeats nothing, so the shipped byte stream is untouched (ER-13).
        writer.RepeatAllPages(page.EffectiveCopies);
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

    /// <summary>
    /// The gap a bold column-heading row opens between itself and the rule drawn under it (<c>y -= 3</c>). It is
    /// drawn twice — under the item table's headings (<see cref="DrawItemTableHeader"/>) and under the per-rate
    /// breakup's headings (<see cref="DrawClosingBlock"/>) — and it is REAL drawn height, so every measurement that
    /// spans one of those rules has to carry it.
    /// <para><b>T0-11 review C4/L1-04.</b> Neither measurement did. It is the "latent 3 pt" the verifier separated
    /// out from the 11 pt reference row: on a fixture whose residual is under 3 pt the last item row breached the
    /// bottom guard with NO reference number at all, and a fix that corrected only the reference row would have left
    /// the arithmetic 3 pt out of true on the first page and on every continuation sheet.</para>
    /// </summary>
    private const double HeadingRuleGap = 3;

    /// <summary>
    /// Does the header state the counterparty's own document number (v48 numbering §8 — their PO on an outward
    /// invoice; on a purchase record RQ-11a makes this pair the carrier of the SUPPLIER's invoice number, so it is
    /// present on essentially every real one)?
    /// <para><b>THE SINGLE SOURCE for both the reserved height and the drawn row</b>, in the same idiom as
    /// <see cref="TopDeclarationLines"/> and <see cref="HeadRows"/>. <b>T0-11 review C4/L1-04:</b> this row was drawn
    /// by <see cref="DrawFirstHeader"/> and reserved by nobody, so the paginator sized page 1 from a header one row
    /// shorter than the one the renderer drew — and page 1's last six-cell money row, Amount included, was drawn on
    /// top of the "Page 1 of N" footer.</para>
    /// </summary>
    private static bool StatesReferenceRow(InvoicePrintData data) => !string.IsNullOrWhiteSpace(data.ReferenceNo);

    private static double FirstHeaderHeight(InvoicePrintData data, PageConfig page)
    {
        double h = page.TitleFontSize + 8 + 4;   // title band + rule
        // Phase 10.11 S3 — the CANCELLED over-print sits between the title and its rule and therefore costs a
        // band row. Reserved here so the paginator does not push a cancelled invoice's last item row off the page.
        if (data.IsCancelled) h += page.TitleFontSize + 2;
        // CGST Rule 5(1)(f): the composition declaration sits "at the top of the bill of supply" — immediately under
        // the title band, above the party blocks. Absent on every other document ⇒ zero height ⇒ byte-identical.
        int declLines = TopDeclarationLines(data, page).Count;
        if (declLines > 0) h += declLines * (page.BodyFontSize + 2) + 4;
        h += PartyBlockHeight(data.Seller, data.Buyer, page) + 4;
        // The number/date row, the optional reference row and the place-of-supply row — counted off the SAME
        // predicate the drawing branches on, so the two cannot disagree about how many rows there are.
        //
        // The rule between the party blocks and these rows used to be reserved as `h += 0.5`. It is a STROKE: it is
        // drawn AT the current y and moves the pen by nothing, so it costs no height, and the 0.5 left measured and
        // drawn 0.5 apart on every document ever rendered. Removed rather than kept as a cushion — the whole defect
        // was two numbers that were supposed to be one.
        h += (page.BodyFontSize + 2) * (StatesReferenceRow(data) ? 3 : 2);
        h += 6;                                   // rule spacer
        // census T0-9: the e-invoice band (signed QR + IRN + Ack). Zero on every document that is not an e-invoice,
        // so the paginator's arithmetic is unchanged for them (ER-13).
        h += EInvoiceBandHeight(data, page);
        h += page.BodyFontSize + 2;               // the item table's own column-heading row
        h += HeadingRuleGap;                      // …and the gap DrawItemTableHeader opens under those headings
        return h;
    }

    private static double ContinuationHeaderHeight(InvoicePrintData data, PageConfig page) =>
        page.TitleFontSize + 8 + 4 + page.BodyFontSize + 2 + HeadingRuleGap
        + (data.IsCancelled ? page.TitleFontSize + 2 : 0);   // + the S3 CANCELLED over-print

    /// <summary>
    /// The word over-printed under a cancelled document's title (Phase 10.11 S3), on EVERY page — a continuation
    /// sheet separated from page 1 must still say what it is.
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN — ours, corpus silent.</b> The source corpus describes no printed treatment
    /// of a cancelled document; the over-print, its wording and its placement are our decision (R7).</para>
    /// </summary>
    private const string CancelledBanner = "CANCELLED";

    // ================================================================ e-Invoice band (census T0-9)

    /// <summary>
    /// The printed side of the QR square, in points (about 34 mm). <b>OURS, and deliberately generous.</b>
    ///
    /// <para>No official source mandates a size. The GST Network's e-invoice FAQ (v1.4, 30-03-2021, Q71) says the QR
    /// "shall be extracted and printed on the invoice", that "printing of QR code on separate paper is not allowed",
    /// and that "while the printed QR code shall be clear enough to be readable by a QR Code reader, the size and its
    /// placing on invoice is upto the preference of the businesses" - the NIC procedure note
    /// (<c>https://einvoice1.gst.gov.in/Documents/Qrcode_procedure.pdf</c>) says the same. Two figures circulate in
    /// secondary commentary (2x2 inches, 3x3 inches) which contradict each other and trace to no primary document, so
    /// neither is treated as a rule here.</para>
    ///
    /// <para><b>What the number is actually chosen against: module size at print resolution.</b> An IRP signed QR is a
    /// long JWS. Measured on an 804-character one, error-correction level L lands on symbol version 20 - 97 modules,
    /// plus the 4-module quiet zone on each side, 105 across. At 96 pt that is 0.914 pt per module, i.e. 3.81 dots at
    /// 300 dpi. Halving the box to 48 pt would halve that to 1.9 dots, which is below what a printed QR survives.</para>
    /// </summary>
    private const double EInvoiceQrSize = 96;

    /// <summary>
    /// The error-correction level the e-invoice QR is encoded at. <b>OURS; no source specifies one.</b>
    ///
    /// <para><b>L, and the reason is the opposite of the intuitive one.</b> A higher level recovers more damaged
    /// modules, but it does so by adding codewords, which pushes the symbol to a higher version, which - inside a box
    /// of FIXED printed size - makes every module smaller. Measured on an 804-character signed QR the choice is
    /// version 20 at L against version 23 at M: 0.914 pt per module against 0.821, i.e. 3.81 dots at 300 dpi against
    /// 3.42. On a document that is laser-printed once and scanned at close range from a flat page, module size
    /// dominates damage tolerance, so the level that keeps the modules largest is the level most likely to scan.</para>
    /// </summary>
    private const QrErrorCorrection EInvoiceQrEcc = QrErrorCorrection.Low;

    /// <summary>The text lines printed beside the QR. <b>The single source</b> for the band's measured height and its
    /// drawing, in the same spirit as <see cref="HeadRows"/> - the two used to be independent expressions elsewhere in
    /// this file and drifted.</summary>
    private static List<string> EInvoiceLines(InvoicePrintData data)
    {
        var lines = new List<string>();
        if (!data.StatesEInvoice) return lines;
        lines.Add("e-Invoice (IRP registered)");
        if (!string.IsNullOrWhiteSpace(data.EInvoiceIrn)) lines.Add("IRN: " + data.EInvoiceIrn.Trim());
        if (!string.IsNullOrWhiteSpace(data.EInvoiceAckNo)) lines.Add("Ack No: " + data.EInvoiceAckNo.Trim());
        if (!string.IsNullOrWhiteSpace(data.EInvoiceAckDateText))
            lines.Add("Ack Date: " + data.EInvoiceAckDateText.Trim());
        return lines;
    }

    /// <summary>The band's height, or zero when the document carries no e-invoice particulars - so a document that is
    /// not an e-invoice reserves nothing, draws nothing and renders byte-identically (ER-13).</summary>
    private static double EInvoiceBandHeight(InvoicePrintData data, PageConfig page)
    {
        if (!data.StatesEInvoice) return 0;
        double textHeight = EInvoiceLines(data).Count * (page.FooterFontSize + 2);
        return Math.Max(EInvoiceQrSize, textHeight) + 6;
    }

    /// <summary>
    /// Draws the e-invoice band: the signed QR on the right, the IRN and IRP acknowledgement to its left. Returns the
    /// new baseline. Placement is <b>OURS</b> - the GSTN FAQ leaves it "upto the preference of the businesses" and the
    /// source corpus describes no placement at all - chosen here so the QR sits inside the header block, above the
    /// item table, where it cannot be cut off by a page break or crossed by a table rule.
    ///
    /// <para><b>Page 1 only.</b> Rule 46(r) is a particular of the invoice, not of every sheet, and one symbol per
    /// document is what a scanner expects. This differs from the CANCELLED over-print, which repeats precisely because
    /// a loose continuation sheet must still say it is void; a continuation sheet carrying a second QR would instead
    /// invite a second scan of the same IRN. <b>OURS</b>; no source addresses it.</para>
    /// </summary>
    private static double DrawEInvoiceBand(
        PdfWriter writer, InvoicePrintData data, PageConfig page, double left, double right, double y)
    {
        if (!data.StatesEInvoice) return y;

        double top = y;
        double qrLeft = right - EInvoiceQrSize;

        if (!string.IsNullOrWhiteSpace(data.EInvoiceSignedQr))
        {
            // VERBATIM (ER-5): the IRP's own signed string, encoded as-is. Nothing here parses, reformats, trims to a
            // field or de-brands it - any of which would invalidate the signature the QR exists to carry.
            var symbol = QrCode.Encode(data.EInvoiceSignedQr, EInvoiceQrEcc);
            var bitmap = PdfBitmap.FromQr(symbol);
            writer.Image(qrLeft, top - EInvoiceQrSize, EInvoiceQrSize, EInvoiceQrSize, bitmap);
        }

        double ty = top - page.FooterFontSize;
        bool first = true;
        foreach (var line in EInvoiceLines(data))
        {
            // The IRN is 64 characters; fit it to the space left of the QR rather than letting it run under the
            // symbol, where it would be unreadable and would look like part of the picture.
            var text = PdfWriter.FitToWidth(line, qrLeft - left - 8, page.FooterFontSize);
            writer.Text(left, ty, text, page.FooterFontSize, bold: first);
            ty -= page.FooterFontSize + 2;
            first = false;
        }

        return top - EInvoiceBandHeight(data, page);
    }

    /// <summary>
    /// <b>This measurement deliberately OVER-reserves, and that is left alone.</b> It prices every line at
    /// <c>BodyFontSize + 1</c>, while <see cref="DrawPartyBlock"/> drops <c>BodyFontSize + 1</c> for the caption and
    /// the name and <c>FooterFontSize + 1</c> for the address, State and GSTIN lines — so on the ordinary
    /// two-address-line block it reserves 60 pt against 56 pt drawn.
    /// <para>Measured while closing T0-11 review C4/L1-04, and recorded rather than changed. It errs in the SAFE
    /// direction (the paginator believes rows sit lower than they do, so it admits fewer, never more), it is not the
    /// defect L1-04 names, and the only thing it costs is density: on that block page 1 holds 47 item rows where the
    /// exact arithmetic would hold 48, the 48th landing at y = 51.89 against a 50 guard. Making it exact would
    /// re-paginate every multi-page invoice the product has ever produced for a cosmetic gain — a change that wants
    /// its own slice, not a ride on a footer-overlap fix.</para>
    /// </summary>
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

    /// <summary>
    /// The vertical space the per-rate breakup table occupies, term for term as <see cref="DrawClosingBlock"/>
    /// draws it: the rule down to the caption baseline, the caption down to the column-head baseline, the heads
    /// down to the first rate row (<see cref="HeadingRuleGap"/> plus the row the rule opens), one row per rate,
    /// and the 2 pt tail. Zero when <see cref="StatesTaxBreakup"/> refuses the table, so a bill of supply and an
    /// unrouted document measure exactly as they draw.
    ///
    /// <para><b>T0-11 review C4/L1-04, second measurement.</b> Written out inline, this block reserved
    /// <c>BodyFontSize + 2</c> (11 pt) for a gap the drawing spends <c>RowHeight + HeadingRuleGap</c> (16 pt) on —
    /// so every taxed invoice's closing block was measured 5 pt shorter than it is drawn, and the paginator kept it
    /// on a page it did not fit. Measured: "Authorised Signatory" drawn at y = 44.00 against a footer occupying
    /// [36, 44] on a Letter page. Same class as the header row, different measurement, and only an invariant over
    /// the WHOLE page could see it — the header fix alone leaves it live.</para>
    /// </summary>
    private static double TaxBreakupHeight(InvoicePrintData data, PageConfig page) =>
        !StatesTaxBreakup(data)
            ? 0
            : (page.BodyFontSize + 2)                     // rule -> caption baseline
              + page.RowHeight                            // caption -> column-head baseline
              + HeadingRuleGap + page.RowHeight           // column heads -> rule -> first rate row
              + data.TaxRows.Count * page.RowHeight
              + 2;

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
        // T0-11 slice S2 — and a RECORD is not a declaration of ours at all. "We declare that this invoice shows the
        // actual price…" is OUR attestation about a document WE issued; on a page headed by the supplier it would be
        // an attestation about someone else's. The record says what it is instead, and the signature block below is
        // dropped with it (Rule 46(q) puts the signature on the ISSUER).
        //
        // 🔴 T0-11 review C24/L3-10 — TWO QUESTIONS, TWO AXES, read separately. The legend is a ROLE-and-ORIENTATION
        // statement: it says this page records "a document issued by the SUPPLIER NAMED ABOVE", which is true only
        // when the party in the head block is somebody else. Whether OUR declaration belongs here is the Rule 46(q)
        // question and is `StatesOurDeclarationAndSignature`'s. Off one flag, a Recorded document headed by US printed
        // the legend over our own name; off the two axes it states neither the legend (nobody else issued it) nor a
        // suppressed declaration (whatever the classification said). Byte-identical on every shipped shape, because
        // both axes default to the coherent pairing.
        string declaration =
            data.IsRecipientRecord && data.Heads == PartyOrientation.WeAreRecipient
            ? GstReportSupport.RecipientRecordLegend
            : !data.StatesOurDeclarationAndSignature
            ? string.Empty
            : data.IsBillOfSupply
            ? "Declaration: We declare that this bill of supply shows the actual price of the goods described and " +
              "that all particulars are true and correct."
            : "Declaration: We declare that this invoice shows the actual price of the goods described and that " +
              "all particulars are true and correct.";
        var declLines = declaration.Length == 0
            ? new List<string>()
            : VoucherPdf.WrapText(declaration, page.ContentWidth * 0.62, page.FooterFontSize);

        // Totals rows: Taxable + (IGST | CGST+SGST) + optional Cess + optional Round Off + Grand Total.
        // The Cess row appears only on a cess-bearing invoice, so a cess-free one measures (and renders) as before.
        // A BILL OF SUPPLY has no tax head to state (Rule 49 prescribes none), so it is value + optional round-off +
        // total — kept in lockstep with DrawClosingBlock below.
        // W0-15: `IsInterState` is three-valued — null (nothing established a routing) states NO named head row.
        // The count comes from HeadRows, the SAME list DrawClosingBlock draws, so measure and draw cannot drift.
        // T0-11 review C1: `OtherCharges` draws one row each, on EVERY document kind (the loop in DrawClosingBlock
        // sits outside the bill-of-supply branch for the reason stated there), so both limbs count it.
        int totalRows = data.IsBillOfSupply
            ? 1 + data.OtherCharges.Count + (data.RoundOff.Amount != 0m ? 1 : 0) + 1
            : 1 + HeadRows(data).Count
                + (data.TotalCess.Amount != 0m ? 1 : 0)
                + data.OtherCharges.Count
                + (data.RoundOff.Amount != 0m ? 1 : 0) + 1;
        double h = 2 + totalRows * page.RowHeight + 2;

        h += TaxBreakupHeight(data, page);

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
        // T0-11 review C3/L1-03 — the copy marking is an ISSUER particular and must not print on a document we do
        // not issue. CGST Rule 48(1) prescribes the three markings for the invoice prepared by the SUPPLIER under
        // §31(1) / Rule 46; a page recording a supply made TO us is none of his three copies, so stamping one on it
        // makes the page assert it IS one — the same false self-description slice S2 removed from the title, the
        // number caption, the place of supply, the declaration and the signature. It was the one issuer particular
        // S2 left ungated.
        // The gate is `StatesOurDeclarationAndSignature`, NOT `IsRecipientRecord`: Rule 48(1)'s markings and Rule
        // 46(q)'s signature answer one question — "is this a copy of a document WE issued?" — and answering it off
        // two different flags is how this leaked in the first place. It is also the answer the shapes ahead need:
        // on slice S5's §31(3)(f) self-invoice the role is Recorded and WE are the issuer, so the markings belong on
        // it, which a role-axis gate would wrongly suppress. Byte-identical on every outward document (the axis
        // defaults to `!IsRecipientRecord`).
        if (config.CopyMarking != CopyMarking.None && data.StatesOurDeclarationAndSignature)
        {
            string label = config.CopyMarkingLabel;
            double w = PdfWriter.MeasureHelvetica(label, page.FooterFontSize);
            writer.Text(right - w, y, label, page.FooterFontSize, bold: true);
        }
        // Phase 10.11 S3 — over-print CANCELLED directly under the statutory title, ABOVE the rule, so it reads as
        // part of the document's name and cannot be taken for a line item. The title itself is untouched: a
        // cancelled tax invoice is still structurally a tax invoice. Nothing draws and no space is consumed when
        // the flag is false, so every existing invoice PDF is byte-identical (ER-13). FirstHeaderHeight reserves
        // the matching space for the paginator.
        if (data.IsCancelled)
        {
            y -= page.TitleFontSize + 2;
            Center(writer, CancelledBanner, left, right, y, page.TitleFontSize, bold: true);
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
        // The captions name the ROLES, and the projector decides which party fills which. On a recipient-side record
        // the left block is the real supplier and the right is us — so "Bill to" would be wrong there (nobody is
        // billing us on a page we produced) and the plain "Recipient:" is what is true.
        // T0-11 review C24/L3-10: that is the ORIENTATION question — whose identity heads the page (Rule 46(a)) —
        // so it reads `Heads`, not the role flag. On a Recorded document that WE head, "Bill to" is true again.
        double sellerY = DrawPartyBlock(writer, page, "Supplier:", data.Seller, left, blockTop);
        double buyerY = DrawPartyBlock(writer, page,
            data.Heads == PartyOrientation.WeAreRecipient ? "Recipient:" : "Recipient (Bill to):",
            data.Buyer, geo.MidX + 6, blockTop);
        y = Math.Min(sellerY, buyerY) - 4;

        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 2;

        // FIX-W1f: the document-number caption must name the document this IS. A bill of supply captioned "Invoice No:"
        // calls its own serial an invoice number on a page whose title band reads BILL OF SUPPLY and whose closing
        // declaration (already fixed by W0-1) says "this bill of supply shows the actual price" — the same
        // self-description error, one screenful apart. It also disagreed with the on-screen preview mirror, which W0-1
        // DID change to "Bill of Supply No.", so the operator approved one caption and issued another.
        // T0-11 slice S2 (RQ-11a) — on a recipient-side record the number under the caption is OURS, printed under
        // the SUPPLIER's identity. "Invoice No:" there would call our internal reference the serial of a document we
        // did not issue: a false statement of the same class FIX-W1f caught, not a cosmetic label.
        writer.Text(left, y,
            (data.IsRecipientRecord ? GstReportSupport.RecordNumberCaption + ": "
                : data.IsBillOfSupply ? "Bill of Supply No: " : "Invoice No: ") + data.InvoiceNumber,
            page.BodyFontSize, bold: false);
        writer.Text(geo.MidX + 6, y, "Date: " + data.InvoiceDateText, page.BodyFontSize, bold: false);
        y -= page.BodyFontSize + 2;
        // v48 (numbering §8): the buyer's reference (e.g. their PO / "Reference No."). Printed only when captured,
        // so an invoice without one is byte-identical to before (ER-13).
        // T0-11 review C4/L1-04 — the presence question is `StatesReferenceRow`, the SAME expression
        // FirstHeaderHeight reserves against. It used to be asked here and nowhere else.
        if (StatesReferenceRow(data))
        {
            var refLine = data.ReferenceCaption + ": " + data.ReferenceNo;
            if (!string.IsNullOrWhiteSpace(data.ReferenceDateText)) refLine += "   Dated: " + data.ReferenceDateText;
            writer.Text(left, y, refLine, page.BodyFontSize, bold: false);
            y -= page.BodyFontSize + 2;
        }
        // CGST Rule 46(n) is a SUPPLIER particular, so a recipient-side record states no place of supply — and states
        // no empty caption for one either. The ROW is still consumed, because FirstHeaderHeight reserves it and a
        // renderer whose measured height and drawn height disagree pushes the last item row off the page.
        if (!data.IsRecipientRecord)
            writer.Text(left, y, "Place of Supply: " + data.PlaceOfSupply, page.BodyFontSize, bold: false);
        // The intra/inter caption names a TAX HEAD ("CGST + SGST" / "IGST"). CGST Rule 49 prescribes no rate and no
        // tax-amount particular, and a composition supplier may collect none (§10(4), §32(2)), so a bill of supply
        // states no head at all. Occupies the same line as Place of Supply ⇒ dropping it changes no height.
        // W0-15: null routing states no head, so no caption — naming one would assert a routing nothing established.
        if (!data.IsBillOfSupply && data.IsInterState is { } interState)
        {
            string supply = interState ? "Inter-State (IGST)" : "Intra-State (CGST + SGST)";
            writer.Text(geo.MidX + 6, y, supply, page.BodyFontSize, bold: false);
        }
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        // census T0-9 - the e-invoice band sits between the document's own particulars and the item table: below the
        // rule that closes the Rule-46 header block, above the goods. Draws and consumes nothing when the document is
        // not an e-invoice, in lockstep with FirstHeaderHeight's reservation.
        y = DrawEInvoiceBand(writer, data, page, left, right, y);
        y -= page.BodyFontSize + 2;
        return y;
    }

    /// <summary>
    /// The TAX rows the totals band states, in order: <b>IGST</b> for an inter-state supply, <b>CGST + SGST</b> for an
    /// intra-state one, and — when the routing is unknown (W0-15) — <b>no NAMED head at all</b>, because naming one
    /// would assert a routing nothing established.
    ///
    /// <para><b>THE SINGLE SOURCE for both the measured height and the drawn rows.</b> <see cref="BuildClosing"/>
    /// counts this list and <see cref="DrawClosingBlock"/> draws exactly it. <b>Review correction:</b> this used to be
    /// an <c>int HeadRowCount</c> consumed only by the measurement, while the drawing re-decided with its own switch
    /// over the same property. The doc comment claimed they could not drift, but they were two independent
    /// expressions — and, measured, collapsing the count's <c>null</c> limb to 2 changed nothing on the page and
    /// turned no test red, because the count reaches only <c>h</c> (geometry) and every assertion in the suite is
    /// about content. Returning the rows themselves removes the possibility rather than testing for it.</para>
    ///
    /// <para><b>The unknown-routing case still states the tax AMOUNT when there is one</b>, under the head-free label
    /// "Tax". The alternative is a Grand Total that silently exceeds "Taxable Value" by an amount the document never
    /// mentions — <see cref="InvoicePrintData.GrandTotal"/> adds <see cref="InvoicePrintData.TotalTax"/> regardless.
    /// Stating an amount asserts no routing; stating "CGST" would. Unreachable from
    /// <c>VoucherPrintProjector</c> (a null routing there means no forward tax leg was posted, so the tax is zero and
    /// no row is emitted); this is the belt-and-braces limb for any other caller, in the same spirit as the bill-of-
    /// supply suppressions below.</para>
    /// </summary>
    private static IReadOnlyList<(string Label, Money Amount)> HeadRows(InvoicePrintData data) => data.IsInterState switch
    {
        true => new[] { ("IGST", data.TotalIgst) },
        false => new[] { ("CGST", data.TotalCgst), ("SGST", data.TotalSgst) },
        null => data.TotalTax.Amount != 0m
            ? new[] { ("Tax", data.TotalTax) }
            : Array.Empty<(string, Money)>(),
    };

    /// <summary>
    /// Does this document state the per-rate GST breakup table? Never on a bill of supply (Rule 49 prescribes no rate
    /// particular), never without rate rows — and <b>never when the routing is unknown</b> (W0-15 review): the
    /// breakup's column headers NAME the heads ("CGST"/"SGST" or "IGST"), so drawing it under a null routing asserts
    /// exactly the intra-state supply the totals band above refuses to assert. Before this gate a caller passing rate
    /// rows with <see cref="InvoicePrintData.IsInterState"/> unset — which now defaults to <c>null</c>, where it used
    /// to default to <c>false</c> — got a document whose breakup showed CGST and SGST columns and whose totals band
    /// showed no tax at all.
    /// <para><b>The single source for the breakup's measured height and its drawing</b>, for the same reason
    /// <see cref="HeadRows"/> is.</para>
    /// </summary>
    private static bool StatesTaxBreakup(InvoicePrintData data) =>
        !data.IsBillOfSupply && data.TaxRows.Count > 0 && data.IsInterState is not null;

    private static double DrawContinuationHeader(
        PdfWriter writer, InvoicePrintData data, PageConfig page, string title, double left, double right)
    {
        double y = page.PageHeight - page.MarginTop;
        y -= page.TitleFontSize;
        Center(writer, title + " (continued)", left, right, y, page.TitleFontSize, bold: true);
        // S3 — the over-print repeats here: a continuation sheet is a loose page once it leaves the printer.
        if (data.IsCancelled)
        {
            y -= page.TitleFontSize + 2;
            Center(writer, CancelledBanner, left, right, y, page.TitleFontSize, bold: true);
        }
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
        y -= HeadingRuleGap;
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
            // W0-15 — three-valued. These rows ARE `HeadRows`, the same list BuildClosing counted, so the measured
            // height and the drawn rows are one expression: a document nothing routed may not state
            // "CGST 0.00 / SGST 0.00", which asserts an intra-state supply.
            foreach (var (label, amount) in HeadRows(data))
                TotalLine(label, Fmt(amount), false);
            // Compensation Cess gets its OWN line — it is ring-fenced from the GST heads (never folded into
            // CGST/SGST/IGST) but it IS charged to the recipient, so it must appear on the bill and reach the Grand
            // Total. Printed only when non-zero, so a cess-free invoice renders exactly as before (ER-13).
            if (data.TotalCess.Amount != 0m)
                TotalLine("Compensation Cess", Fmt(data.TotalCess), false);
        }
        // T0-11 review C1 — the posted party-side charges that are neither goods nor GST/cess: an additional cost of
        // purchase (Freight, Packing, …) on a record, §206C TCS on an outward invoice. Each is captioned with the
        // ledger the operator posted it to and each reaches the Grand Total, so the demand is the posted debt.
        // NOT inside the `!IsBillOfSupply` block: Rule 49 withholds the RATE and TAX particulars from a bill of
        // supply, and a freight charge is neither — suppressing it would put the Grand Total back out of true.
        // Drawn AFTER the tax heads so no reader can infer the tax above was charged on these amounts. Empty on
        // every document that bears none ⇒ byte-identical (ER-13).
        foreach (var charge in data.OtherCharges)
            TotalLine(charge.Caption, Fmt(charge.Amount), false);
        if (data.RoundOff.Amount != 0m)
            TotalLine("Round Off", FmtSigned(data.RoundOff), false);
        writer.Line(geo.QtyRight, y + page.RowHeight - 2, right, y + page.RowHeight - 2, 0.7);
        TotalLine(data.IsBillOfSupply ? "Total" : "Grand Total", Fmt(data.GrandTotal), true);
        y -= 2;

        // --- Per-rate tax breakup table --- (never on a bill of supply: Rule 49 prescribes no rate particular, and
        // showing one would assert a collection §10(4) / §32(2) forbid; never under an unknown routing, whose column
        // headers would name a head nothing established. Belt-and-braces — the projector already suppresses the rows;
        // this makes the renderer safe against any future caller that does not. The gate is `StatesTaxBreakup`, the
        // SAME predicate BuildClosing measured with.)
        if (StatesTaxBreakup(data))
        {
            writer.Line(left, y, right, y, 0.5);
            y -= page.BodyFontSize + 2;
            // T0-11 slice S2 — WHOSE tax this is, said out loud. The record must state the tax (it is what
            // substantiates the input tax credit we claim), and the figures come off the posted Input legs, so the
            // caption is the only thing keeping the page from asserting that WE charged it.
            // T0-11 review C24/L3-10: "whose tax" is the ORIENTATION question, so it reads `Heads`. On a document WE
            // head, captioning the breakup as somebody else's charge would be the same false statement in reverse.
            // (The wording itself is untouched — it is under an open R12 question, plan.md Phase 10.13.)
            writer.Text(left, y,
                data.Heads == PartyOrientation.WeAreRecipient ? GstReportSupport.SupplierTaxCaption : "GST Breakup",
                page.BodyFontSize, bold: true);
            y -= page.RowHeight;

            double rTaxableR = left + page.ContentWidth * 0.30;
            double rC1R = left + page.ContentWidth * 0.53;
            double rC2R = left + page.ContentWidth * 0.76;

            writer.Text(left, y, "Rate", page.BodyFontSize, bold: true);
            RightText(writer, "Taxable", left + page.ContentWidth * 0.10, rTaxableR, y, page.BodyFontSize, bold: true);
            // W0-15: `is true` vs `is false` on a three-valued routing. The `else` here is `is false` and ONLY that,
            // because `StatesTaxBreakup` already refused the null.
            //
            // ▶ REVIEW CORRECTION. This said "the null case cannot reach here — a rate row exists only where a forward
            // leg was posted, and a posted leg is what makes the routing non-null". That is true of
            // `VoucherPrintProjector` and FALSE of this renderer, which the comment above deliberately keeps "safe
            // against any future caller". Widening `InvoicePrintData.IsInterState` to `bool?` changed its DEFAULT from
            // false to null, so a DTO that merely omits the property reached this `else` and was read as intra-state
            // here while the totals band read the same null as "no head" — one document, two answers. The gate, not an
            // unreachability argument, is what makes the `else` safe.
            if (data.IsInterState is true)
            {
                RightText(writer, "IGST", rTaxableR, rC1R, y, page.BodyFontSize, bold: true);
            }
            else
            {
                RightText(writer, "CGST", rTaxableR, rC1R, y, page.BodyFontSize, bold: true);
                RightText(writer, "SGST", rC1R, rC2R, y, page.BodyFontSize, bold: true);
            }
            y -= HeadingRuleGap;
            writer.Line(left, y, right, y, 0.4);
            y -= page.RowHeight;

            foreach (var tr in data.TaxRows)
            {
                writer.Text(left, y, tr.RateLabel, page.BodyFontSize, bold: false);
                RightText(writer, Fmt(tr.TaxableValue), left + page.ContentWidth * 0.10, rTaxableR, y, page.BodyFontSize, bold: false);
                if (data.IsInterState is true)
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
        //
        // 🔴 T0-11 slice S2 — DROPPED ENTIRELY on a recipient-side record, and this is the sharpest of the slice's
        // suppressions. `data.Seller` is the party who HEADS the document, and on a record that is the SUPPLIER: the
        // block below, drawn unchanged, would print "For {the supplier}" over "Authorised Signatory" on a page WE
        // produced — not a mislabelled caption but an attestation in someone else's name. CGST Rule 46(q) puts the
        // signature on the ISSUER, and on this document that is not us. Costs no measured height: the block draws in
        // the right column beside the declaration BuildClosing already sized.
        // T0-11 review C22/L3-08 + C24/L3-10 — read off the axis that ANSWERS the Rule 46(q) question rather than off
        // a proxy from the role axis. `StatesOurDeclarationAndSignature` was write-only until this change: the
        // classifier set it on both branches and no production code read it, so the field claimed to govern a
        // suppression that was in fact governed by a different flag. Byte-identical on every shipped shape (it
        // defaults to `!IsRecipientRecord`), and now the classification's own answer is the one obeyed.
        if (!data.StatesOurDeclarationAndSignature) return;

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
