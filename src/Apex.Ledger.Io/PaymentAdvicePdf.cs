using System.Globalization;
using Apex.Ledger.Reports;
using static Apex.Ledger.Io.CertificatePdfSupport;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders the <b>supplier payment advice</b> letter (catalog §8 Banking; census row 8.7) — one letter per
/// payment, deterministic and de-branded, through the same <see cref="PdfWriter"/> pipeline as the payslip and
/// the TDS certificates.
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/payment-advice/</c> — the printed advice carries
/// "Invoice-wise details, such as: Invoice numbers, Amounts paid, Deductions (if any), TDS details, Payment mode
/// (NEFT, RTGS, cheque, etc.)", the party's contact details and address, and bank transfer information; and the
/// report offers "Print each transaction on a fresh page", which is why <see cref="Render"/> takes a list and a
/// flag rather than a single advice.</para>
///
/// <para><b>🔴 A CAPTION WITH NOTHING BEHIND IT IS NOT PRINTED.</b> The company's PAN and the bank's
/// account number / IFSC have no source in this product's Company and (before the cheque-books migration) Ledger
/// masters. The letter therefore <b>omits</b> those lines rather than printing an empty caption — an empty
/// "PAN:" on a letter to a supplier reads as a company with no PAN, which is a different and worse statement
/// than saying nothing.</para>
///
/// <para><b>🔴 THE DE-BRANDING GUARD STOPS AT THE COUNTERPARTY.</b> <see cref="Debrand.Text"/> strips a
/// third-party accounting brand out of text this product owns — our letterhead, our footer, the /Title metadata
/// (ER-11). It must never be pointed at the <b>supplier's own legal name</b>, its address, its bank or its bill
/// references: those are the counterparty's identity, quoted back to it on a letter addressed to it, and a
/// case-insensitive substring strip silently rewrites any of them that happens to contain the token. A letter
/// beginning "To Metally Traders" is not a de-branded letter — it is a letter to somebody else. So every field
/// on this page that belongs to the SUPPLIER is drawn verbatim, and the guard stays on the fields that are
/// ours.</para>
///
/// <para><b>🔴 A LETTER THAT RUNS OFF THE SHEET IS NOT A LAYOUT BLEMISH — THE MONEY IS SIMPLY MISSING.</b>
/// <see cref="DrawOne"/> breaks the page <i>inside</i> one letter, not only between letters: an advice with
/// enough bills fills a sheet on its own, and before this the cursor just kept decrementing until the bill
/// table, the totals and the signature were being drawn at negative coordinates. The PDF stayed structurally
/// valid and every substring test still passed, while the supplier received a first sheet that stops mid-table.
/// Every break goes through <see cref="Sheet.NewPage"/>, and a continuation sheet re-states which letter it
/// belongs to and repeats the bill-table caption band.</para>
///
/// <para><b>Determinism.</b> No clock, no RNG, invariant date and money formatting throughout; the same advices
/// render byte-identically on every host, which is what the repository's PDF tests assert.</para>
/// </summary>
public static class PaymentAdvicePdf
{
    private const string Title = "PAYMENT ADVICE";

    /// <summary>
    /// Renders the advices. <paramref name="freshPageEach"/> is the vendor's "Print each transaction on a fresh
    /// page": true ⇒ one advice per page, false ⇒ they flow one after another, separated by a rule.
    /// </summary>
    /// <param name="advices">The advices to print, in report order.</param>
    /// <param name="companyName">The drawer company, printed as the letterhead and the signatory.</param>
    /// <param name="companyAddress">The company's address block, or blank to omit it.</param>
    /// <param name="page">The page geometry (A4/Letter, margins, type sizes).</param>
    /// <param name="freshPageEach">One advice per page.</param>
    public static byte[] Render(
        IReadOnlyList<SupplierPaymentAdviceRow> advices,
        string companyName,
        string? companyAddress,
        PageConfig page,
        bool freshPageEach = true)
    {
        ArgumentNullException.ThrowIfNull(advices);
        ArgumentNullException.ThrowIfNull(page);

        // 🔴 TWO PASSES, BECAUSE "Page 2 of 5" CANNOT BE WRITTEN BEFORE THE 5 IS KNOWN. The run's page count is
        // only settled once the last letter has been laid out, and a footer is drawn as its page is finished —
        // so pass 1 is laid out purely to be counted and thrown away, and pass 2 is the document. Nothing in the
        // layout depends on the footer text (it is centred at the bottom margin and never moves the cursor), so
        // the two passes break in exactly the same places and the count is the one the reader gets.
        int pages = Lay(advices, companyName, companyAddress, page, freshPageEach, pageCount: 0).PageCount;
        var writer = Lay(advices, companyName, companyAddress, page, freshPageEach, pageCount: pages);
        writer.RepeatAllPages(page.EffectiveCopies);
        return writer.Build();
    }

    /// <summary>Lays the whole run out onto one writer. <paramref name="pageCount"/> is 0 on the counting pass,
    /// where the footer falls back to naming the current page as the last one.</summary>
    private static PdfWriter Lay(
        IReadOnlyList<SupplierPaymentAdviceRow> advices,
        string companyName,
        string? companyAddress,
        PageConfig page,
        bool freshPageEach,
        int pageCount)
    {
        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;

        var sheet = new Sheet(
            new PdfWriter { DocumentTitle = SafeTitle(Title) }, page, left, right, pageCount, breaks: true);
        sheet.Writer.BeginPage(page.PageWidth, page.PageHeight);
        double y = sheet.Top;

        if (advices.Count == 0)
        {
            // An advice run with nothing in it must READ as "no payments", never as a blank sheet the operator
            // has to interpret. (The same rule the empty registers keep.)
            y -= page.TitleFontSize;
            Center(sheet.Writer, Debrand.Text(companyName), left, right, y, page.TitleFontSize, bold: true);
            y -= page.BodyFontSize + 8;
            Center(sheet.Writer, "No supplier payments in this period.", left, right, y, page.BodyFontSize, false);
            sheet.Footer();
            return sheet.Writer;
        }

        for (int i = 0; i < advices.Count; i++)
        {
            if (i > 0)
            {
                if (freshPageEach)
                {
                    y = sheet.NewPage();
                }
                else
                {
                    // 🔴 FLOWING MODE MUST STILL BREAK THE PAGE. Before this check `y` decremented monotonically
                    // for the whole run, so with the vendor's "print each transaction on a fresh page" switched
                    // OFF the second and third letters were laid down BELOW the sheet — measured at y = -820 on
                    // A4, most of a page past the bottom edge. The PDF stayed structurally valid and the page
                    // count stayed a tidy 1, so a page-count or substring test could not see it at all.
                    //
                    // It is a START-of-letter check only: it keeps a letter that WOULD fit from being split for
                    // nothing. A letter too tall for any sheet is handled inside DrawOne, which breaks mid-letter.
                    if (y - Measure(advices[i], companyName, companyAddress, page, left, right) < sheet.Floor)
                    {
                        y = sheet.NewPage();
                    }
                    else
                    {
                        y -= 6;
                        sheet.Writer.Line(left, y, right, y, 0.8);
                        y -= page.BodyFontSize + 6;
                    }
                }
            }

            y = DrawOne(sheet, advices[i], companyName, companyAddress, y);
        }

        sheet.Footer();
        return sheet.Writer;
    }

    /// <summary>
    /// How tall one letter is, in points — measured by drawing it onto a THROWAWAY writer and reading how far
    /// <see cref="DrawOne"/> moved the cursor.
    ///
    /// <para><b>Why a probe rather than an arithmetic estimate.</b> A letter's height depends on its address
    /// lines, on which of the six optional header captions apply, on its bill count, on whether a deduction
    /// prints, and on how many lines the amount-in-words wraps to. Re-deriving that sum here would be a second
    /// statement of the layout obliged to agree with the first, and the two would drift the first time anyone
    /// added a caption — the same reason <c>ChequeLayout</c> derives its line gap instead of storing it. Running
    /// the real renderer and discarding the bytes cannot drift, because it IS the renderer.</para>
    ///
    /// <para>The probe's sheet has breaking <b>off</b>: what is wanted here is the letter's NATURAL height, so
    /// that a letter taller than a sheet reports a height taller than a sheet and the caller starts it on a fresh
    /// page. A probe that broke pages would report the height of its last fragment instead.</para>
    /// </summary>
    private static double Measure(
        SupplierPaymentAdviceRow advice,
        string companyName,
        string? companyAddress,
        PageConfig page,
        double left,
        double right)
    {
        var probe = new Sheet(new PdfWriter(), page, left, right, pageCount: 0, breaks: false);
        probe.Writer.BeginPage(page.PageWidth, page.PageHeight);
        return probe.Top - DrawOne(probe, advice, companyName, companyAddress, probe.Top);
    }

    private static double DrawOne(
        Sheet s,
        SupplierPaymentAdviceRow a,
        string companyName,
        string? companyAddress,
        double y)
    {
        var page = s.Page;
        var writer = s.Writer;
        double left = s.Left;
        double right = s.Right;
        double amtRight = right;
        double dueRight = right - 90;
        bool inBillTable = false;

        // The bill table's caption band — drawn where the table starts and AGAIN at the top of every continuation
        // sheet, because a column of bare figures under no headings is not a bill table. Leaves the cursor on the
        // first data row's baseline.
        void BillHead()
        {
            writer.Text(left, y, "Bill / Reference", page.BodyFontSize, bold: true);
            RightText(writer, "Due Date", left, dueRight, y, page.BodyFontSize, true);
            RightText(writer, "Amount", dueRight, amtRight, y, page.BodyFontSize, true);
            y -= 3;
            writer.Line(left, y, right, y, 0.5);
            y -= page.RowHeight;
        }

        // 🔴 THE MID-LETTER PAGE BREAK. Guarantees `need` points of room below the cursor, moving to a
        // continuation sheet when there is not — every caller that is about to draw asks for its own room first,
        // so no line can land below the floor. On return the cursor is always a baseline that may be drawn on.
        void Room(double need)
        {
            if (!s.Breaks || y - need >= s.Floor) return;

            y = s.NewPage();
            // A continuation sheet says which letter it belongs to. A loose second sheet carrying a supplier's
            // money and no addressee is worse than a page break.
            y -= page.SubtitleFontSize;
            writer.Text(left, y, Debrand.Text(companyName) + "  -  " + Title + "  (continued)",
                page.SubtitleFontSize, bold: true);
            y -= page.FooterFontSize + 3;
            writer.Text(left, y,
                "To " + a.AddresseeName + "   .   Voucher No. " + a.FormattedNumber + "   .   " + Date(a.Date),
                page.FooterFontSize, bold: false);
            y -= 6;
            writer.Line(left, y, right, y, 0.5);
            y -= page.BodyFontSize + 6;
            if (inBillTable) BillHead();
        }

        // ---- Letterhead ----
        y -= page.TitleFontSize;
        Center(writer, Debrand.Text(companyName), left, right, y, page.TitleFontSize, bold: true);
        if (!string.IsNullOrWhiteSpace(companyAddress))
        {
            foreach (var line in SplitLines(companyAddress))
            {
                y -= page.SubtitleFontSize + 2;
                Center(writer, Debrand.Text(line), left, right, y, page.SubtitleFontSize, bold: false);
            }
        }
        y -= page.SubtitleFontSize + 4;
        Center(writer, Title, left, right, y, page.SubtitleFontSize, bold: true);
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 6;

        // ---- Addressee ----
        // 🔴 VERBATIM. This is the supplier's own legal name and its own address, on a letter addressed to it.
        // De-branding them would rewrite a counterparty's identity — see the class remarks.
        Room(page.RowHeight * 2);
        writer.Text(left, y, "To", page.BodyFontSize, bold: false);
        y -= page.RowHeight;
        writer.Text(left, y, a.AddresseeName, page.BodyFontSize, bold: true);
        foreach (var line in a.AddressLines)
        {
            Room(page.RowHeight);
            y -= page.RowHeight;
            writer.Text(left, y, line, page.BodyFontSize, bold: false);
        }
        y -= page.RowHeight + 4;

        // ---- Payment header ----
        double keyStep = page.BodyFontSize + 3;
        Room(keyStep);
        y = KeyVal(writer, left, y, "Voucher No:", a.FormattedNumber, page);
        Room(keyStep);
        y = KeyVal(writer, left, y, "Date:", Date(a.Date), page);
        // A payment made out of cash carries no mode; the caption is omitted rather than left blank.
        if (a.PaymentMode is { } mode)
        {
            Room(keyStep);
            y = KeyVal(writer, left, y, "Payment Mode:", SupplierPaymentAdvice.PaymentModeText(mode), page);
        }
        if (!string.IsNullOrWhiteSpace(a.InstrumentNumber))
        {
            Room(keyStep);
            y = KeyVal(writer, left, y, "Instrument No:", a.InstrumentNumber, page);
        }
        if (a.InstrumentDate is { } idt)
        {
            Room(keyStep);
            y = KeyVal(writer, left, y, "Instrument Date:", Date(idt), page);
        }
        if (!string.IsNullOrWhiteSpace(a.BankName))
        {
            // 🔴 VERBATIM, for the same reason as the addressee: this names a real bank, and a bank whose name
            // contains the token is a bank, not a branding leak.
            Room(keyStep);
            y = KeyVal(writer, left, y, "Bank:", a.BankName, page, debrandValue: false);
        }
        // "matched (reconciled) or not" is the report's own headline fact, so the letter states it too.
        Room(keyStep);
        y = KeyVal(writer, left, y, "Status:",
            a.BankDate is { } bd ? "Cleared on " + Date(bd) : "Not yet cleared", page);

        y -= 4;
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 4;

        // ---- Bill-wise detail ----
        // A caption band with no room for a row under it is not a table, so the head asks for three rows.
        Room(page.RowHeight * 3);
        inBillTable = true;
        BillHead();

        if (a.Bills.Count == 0)
        {
            // A payment with no bill-wise allocation is a legitimate posting (bill-by-bill off, or an
            // on-account settlement); say so rather than leaving an empty table.
            writer.Text(left, y, "(no bill-wise detail recorded for this payment)", page.BodyFontSize, false);
            y -= page.RowHeight;
        }
        else
        {
            foreach (var b in a.Bills)
            {
                // The row is drawn AT the cursor, so it is the cursor itself that has to clear the floor.
                Room(0);
                writer.Text(left, y,
                    // 🔴 VERBATIM: a bill reference is the SUPPLIER'S invoice number, quoted back to it. It is the
                    // one field on this letter the supplier looks up in its own ledger.
                    PdfWriter.FitToWidth(b.BillReference, dueRight - left - 12, page.BodyFontSize),
                    page.BodyFontSize, false);
                RightText(writer, b.DueDate is { } d ? Date(d) : "-", left, dueRight, y, page.BodyFontSize, false);
                RightText(writer, Rupees(b.Amount), dueRight, amtRight, y, page.BodyFontSize, false);
                y -= page.RowHeight;
            }
        }

        // The totals sit in the table's own Amount column, so `inBillTable` stays on: a break here has to repeat
        // the caption band, or a bare figure lands under nothing.
        Room(page.RowHeight * 2);
        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.5);
        writer.Text(left, y, "Gross Amount", page.BodyFontSize, bold: true);
        RightText(writer, Rupees(a.GrossAmount), dueRight, amtRight, y, page.BodyFontSize, true);
        y -= page.RowHeight;

        // "Deductions (if any), TDS details" — printed only when there IS a deduction, so an advice for a
        // payment carrying no withholding does not imply one of zero.
        if (a.TdsDeducted.Amount != 0m)
        {
            Room(page.RowHeight);
            writer.Text(left, y, "Less: Tax Deducted at Source", page.BodyFontSize, bold: false);
            RightText(writer, Rupees(a.TdsDeducted), dueRight, amtRight, y, page.BodyFontSize, false);
            y -= page.RowHeight;
        }

        Room(page.RowHeight);
        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.8);
        writer.Text(left, y, "Net Amount Paid", page.BodyFontSize + 1, bold: true);
        RightText(writer, Rupees(a.NetPaid), dueRight, amtRight, y, page.BodyFontSize + 1, true);
        y -= page.RowHeight + 4;
        inBillTable = false;

        string words = "Net amount (in words): " + IndianAmountInWords.Convert(a.NetPaid.Amount);
        foreach (var wl in VoucherPdf.WrapText(Debrand.Text(words), page.ContentWidth, page.BodyFontSize))
        {
            Room(page.BodyFontSize + 3);
            writer.Text(left, y, wl, page.BodyFontSize, false);
            y -= page.BodyFontSize + 3;
        }

        // ---- Signature ----
        Room(page.RowHeight * 5);
        y -= page.RowHeight * 2;
        RightText(writer, "For " + Debrand.Text(companyName), left, right, y, page.BodyFontSize, true);
        y -= page.RowHeight * 2;
        RightText(writer, "Authorised Signatory", left, right, y, page.FooterFontSize, false);
        return y - page.RowHeight;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        foreach (var l in text.Replace("\r\n", "\n").Split('\n'))
            if (!string.IsNullOrWhiteSpace(l)) yield return l.Trim();
    }

    /// <summary>
    /// The sheet the letters are being laid onto: the writer, the geometry, and the page number the footer needs.
    ///
    /// <para>It owns the <b>one</b> page break in this file, so the break BETWEEN two letters and the break
    /// INSIDE a long one cannot drift apart — and the footer's "Page n of m" is produced by the same object that
    /// counts the pages, rather than by a second statement of the same fact.</para>
    /// </summary>
    private sealed class Sheet
    {
        private readonly int _pageCount;
        private int _pageNo = 1;

        internal Sheet(PdfWriter writer, PageConfig page, double left, double right, int pageCount, bool breaks)
        {
            Writer = writer;
            Page = page;
            Left = left;
            Right = right;
            _pageCount = pageCount;
            Breaks = breaks;
        }

        internal PdfWriter Writer { get; }

        internal PageConfig Page { get; }

        internal double Left { get; }

        internal double Right { get; }

        /// <summary>The first baseline band of a fresh sheet.</summary>
        internal double Top => Page.PageHeight - Page.MarginTop;

        /// <summary>The lowest y a line may be drawn at. Below this the line is off the sheet, which is not a
        /// layout blemish — the supplier simply never receives that part of the advice.</summary>
        internal double Floor => Page.MarginBottom + Page.FooterFontSize + 4;

        /// <summary>False on the measuring probe, which must report one letter's NATURAL height.</summary>
        internal bool Breaks { get; }

        /// <summary>Foots the current sheet, starts a fresh one, and returns the cursor to its top.</summary>
        internal double NewPage()
        {
            Footer();
            Writer.BeginPage(Page.PageWidth, Page.PageHeight);
            _pageNo++;
            return Top;
        }

        /// <summary>
        /// Draws the running footer for the sheet being finished. Every sheet of a multi-sheet run used to be
        /// footed "Page 1 of 1" because both replacements were hard-coded — the same footer contract
        /// <c>ReportPdf</c> and <c>VoucherPdf</c> keep is now kept here.
        /// </summary>
        internal void Footer()
        {
            string footer = (Page.FooterText ?? string.Empty)
                .Replace("{page}", _pageNo.ToString(CultureInfo.InvariantCulture))
                .Replace("{pages}", (_pageCount > 0 ? _pageCount : _pageNo).ToString(CultureInfo.InvariantCulture));
            if (footer.Length > 0)
                Center(Writer, Debrand.Text(footer), Left, Right, Page.MarginBottom, Page.FooterFontSize, false);
        }
    }
}
