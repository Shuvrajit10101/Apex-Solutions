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

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;

        var writer = new PdfWriter { DocumentTitle = SafeTitle(Title) };
        writer.BeginPage(page.PageWidth, page.PageHeight);
        double y = page.PageHeight - page.MarginTop;

        if (advices.Count == 0)
        {
            // An advice run with nothing in it must READ as "no payments", never as a blank sheet the operator
            // has to interpret. (The same rule the empty registers keep.)
            y -= page.TitleFontSize;
            Center(writer, Debrand.Text(companyName), left, right, y, page.TitleFontSize, bold: true);
            y -= page.BodyFontSize + 8;
            Center(writer, "No supplier payments in this period.", left, right, y, page.BodyFontSize, bold: false);
            DrawFooter(writer, page, left, right);
            writer.RepeatAllPages(page.EffectiveCopies);
            return writer.Build();
        }

        for (int i = 0; i < advices.Count; i++)
        {
            if (i > 0)
            {
                if (freshPageEach)
                {
                    DrawFooter(writer, page, left, right);
                    writer.BeginPage(page.PageWidth, page.PageHeight);
                    y = page.PageHeight - page.MarginTop;
                }
                else
                {
                    y -= 6;
                    writer.Line(left, y, right, y, 0.8);
                    y -= page.BodyFontSize + 6;
                }
            }

            y = DrawOne(writer, advices[i], companyName, companyAddress, page, left, right, y);
        }

        DrawFooter(writer, page, left, right);
        writer.RepeatAllPages(page.EffectiveCopies);
        return writer.Build();
    }

    private static double DrawOne(
        PdfWriter writer,
        SupplierPaymentAdviceRow a,
        string companyName,
        string? companyAddress,
        PageConfig page,
        double left,
        double right,
        double y)
    {
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
        writer.Text(left, y, "To", page.BodyFontSize, bold: false);
        y -= page.RowHeight;
        writer.Text(left, y, Debrand.Text(a.AddresseeName), page.BodyFontSize, bold: true);
        foreach (var line in a.AddressLines)
        {
            y -= page.RowHeight;
            writer.Text(left, y, Debrand.Text(line), page.BodyFontSize, bold: false);
        }
        y -= page.RowHeight + 4;

        // ---- Payment header ----
        double dy = y;
        dy = KeyVal(writer, left, dy, "Voucher No:", a.FormattedNumber, page);
        dy = KeyVal(writer, left, dy, "Date:", Date(a.Date), page);
        // A payment made out of cash carries no mode; the caption is omitted rather than left blank.
        if (a.PaymentMode is { } mode)
            dy = KeyVal(writer, left, dy, "Payment Mode:", SupplierPaymentAdvice.PaymentModeText(mode), page);
        if (!string.IsNullOrWhiteSpace(a.InstrumentNumber))
            dy = KeyVal(writer, left, dy, "Instrument No:", a.InstrumentNumber, page);
        if (a.InstrumentDate is { } idt)
            dy = KeyVal(writer, left, dy, "Instrument Date:", Date(idt), page);
        if (!string.IsNullOrWhiteSpace(a.BankName))
            dy = KeyVal(writer, left, dy, "Bank:", Debrand.Text(a.BankName), page);
        // "matched (reconciled) or not" is the report's own headline fact, so the letter states it too.
        dy = KeyVal(writer, left, dy, "Status:",
            a.BankDate is { } bd ? "Cleared on " + Date(bd) : "Not yet cleared", page);

        y = dy - 4;
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 4;

        // ---- Bill-wise detail ----
        double amtRight = right;
        double dueRight = right - 90;
        writer.Text(left, y, "Bill / Reference", page.BodyFontSize, bold: true);
        RightText(writer, "Due Date", left, dueRight, y, page.BodyFontSize, true);
        RightText(writer, "Amount", dueRight, amtRight, y, page.BodyFontSize, true);
        y -= 3;
        writer.Line(left, y, right, y, 0.5);
        y -= page.RowHeight;

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
                writer.Text(left, y,
                    PdfWriter.FitToWidth(Debrand.Text(b.BillReference), dueRight - left - 12, page.BodyFontSize),
                    page.BodyFontSize, false);
                RightText(writer, b.DueDate is { } d ? Date(d) : "-", left, dueRight, y, page.BodyFontSize, false);
                RightText(writer, Rupees(b.Amount), dueRight, amtRight, y, page.BodyFontSize, false);
                y -= page.RowHeight;
            }
        }

        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.5);
        writer.Text(left, y, "Gross Amount", page.BodyFontSize, bold: true);
        RightText(writer, Rupees(a.GrossAmount), dueRight, amtRight, y, page.BodyFontSize, true);
        y -= page.RowHeight;

        // "Deductions (if any), TDS details" — printed only when there IS a deduction, so an advice for a
        // payment carrying no withholding does not imply one of zero.
        if (a.TdsDeducted.Amount != 0m)
        {
            writer.Text(left, y, "Less: Tax Deducted at Source", page.BodyFontSize, bold: false);
            RightText(writer, Rupees(a.TdsDeducted), dueRight, amtRight, y, page.BodyFontSize, false);
            y -= page.RowHeight;
        }

        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.8);
        writer.Text(left, y, "Net Amount Paid", page.BodyFontSize + 1, bold: true);
        RightText(writer, Rupees(a.NetPaid), dueRight, amtRight, y, page.BodyFontSize + 1, true);
        y -= page.RowHeight + 4;

        string words = "Net amount (in words): " + IndianAmountInWords.Convert(a.NetPaid.Amount);
        foreach (var wl in VoucherPdf.WrapText(Debrand.Text(words), page.ContentWidth, page.BodyFontSize))
        {
            writer.Text(left, y, wl, page.BodyFontSize, false);
            y -= page.BodyFontSize + 3;
        }

        // ---- Signature ----
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

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right)
    {
        string footer = (page.FooterText ?? string.Empty).Replace("{page}", "1").Replace("{pages}", "1");
        if (footer.Length > 0)
            Center(writer, Debrand.Text(footer), left, right, page.MarginBottom, page.FooterFontSize, false);
    }
}
