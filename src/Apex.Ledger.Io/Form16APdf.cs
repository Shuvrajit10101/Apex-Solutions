using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Reports;
using static Apex.Ledger.Io.CertificatePdfSupport;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a <see cref="Form16A"/> <b>TDS certificate</b> (catalog §13; s.203 r.31) as a deterministic, de-branded
/// PDF through the same pipeline as the GST tax invoice (<see cref="PdfWriter"/> + <see cref="IndianAmountInWords"/>):
/// the deductor block (TAN / PAN / name / responsible person), the deductee block (PAN / name), the quarter's TDS
/// summary table (section / date / amount paid / rate / TDS), the challan/deposit table (serial / BSR / date /
/// TDS deposited), the totals, and the total TDS in words. Every figure is read verbatim off the <see cref="Form16A"/>
/// projection (so it matches Form 26Q to the paisa); the same certificate renders byte-identically.
/// </summary>
public static class Form16APdf
{
    private const string Title = "FORM 16A";
    private const string Subtitle =
        "Certificate under section 203 of the Income-tax Act, 1961 for tax deducted at source";

    /// <summary>Renders the TDS certificate to PDF bytes.</summary>
    public static byte[] Render(Form16A cert, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(cert);
        ArgumentNullException.ThrowIfNull(page);

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double mid = left + page.ContentWidth / 2.0;

        var writer = new PdfWriter { DocumentTitle = SafeTitle(Title) };
        writer.BeginPage(page.PageWidth, page.PageHeight);
        double y = page.PageHeight - page.MarginTop;

        // ---- Title band ----
        y -= page.TitleFontSize;
        Center(writer, Title, left, right, y, page.TitleFontSize, bold: true);
        y -= page.SubtitleFontSize + 2;
        Center(writer, Subtitle, left, right, y, page.SubtitleFontSize, bold: false);
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 4;

        // ---- FY / Quarter meta ----
        writer.Text(left, y, "Financial Year: " + cert.FinancialYearLabel, page.BodyFontSize, bold: true);
        writer.Text(mid + 6, y, "Quarter: " + cert.QuarterLabel + "  (" + Date(cert.From) + " to " + Date(cert.To) + ")",
            page.BodyFontSize, bold: true);
        y -= page.BodyFontSize + 4;
        writer.Line(left, y, right, y, 0.4);
        y -= page.BodyFontSize + 2;

        // ---- Deductor (left) + Deductee (right) blocks ----
        double blockTop = y;
        double dy = y;
        writer.Text(left, dy, "Deductor (Person responsible for deduction)", page.FooterFontSize, bold: true);
        dy -= page.BodyFontSize + 2;
        dy = KeyVal(writer, left, dy, "Name:", cert.Deductor.Name, page);
        dy = KeyVal(writer, left, dy, "TAN:", cert.Deductor.Tan, page);
        dy = KeyVal(writer, left, dy, "PAN:", cert.Deductor.Pan, page);
        dy = KeyVal(writer, left, dy, "Responsible:", cert.Deductor.ResponsiblePersonName, page);

        double ey = blockTop;
        writer.Text(mid + 6, ey, "Deductee (Certificate issued to)", page.FooterFontSize, bold: true);
        ey -= page.BodyFontSize + 2;
        ey = KeyVal(writer, mid + 6, ey, "Name:", cert.Deductee.Name, page);
        ey = KeyVal(writer, mid + 6, ey, "PAN:", cert.Deductee.Pan, page);

        y = Math.Min(dy, ey) - 2;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 2;

        // ---- Deduction summary table ----
        var geo = new Cols(left, right, page);
        writer.Text(left, y, "Summary of tax deducted", page.BodyFontSize, bold: true);
        y -= page.RowHeight;
        y = DrawDeductionHeader(writer, page, geo, left, right, y);
        int sr = 1;
        foreach (var d in cert.Deductions)
        {
            writer.Text(geo.SrX, y, sr.ToString(CultureInfo.InvariantCulture), page.BodyFontSize, false);
            writer.Text(geo.SecX, y, d.SectionCode + " (" + d.FvuSectionCode + ")", page.BodyFontSize, false);
            writer.Text(geo.DateX, y, Date(d.Date), page.BodyFontSize, false);
            RightText(writer, Rupees(d.AmountPaid), geo.DateX, geo.PaidR, y, page.BodyFontSize, false);
            RightText(writer, d.RatePercent.ToString("0.##", CultureInfo.InvariantCulture) + "%", geo.PaidR, geo.RateR, y, page.BodyFontSize, false);
            RightText(writer, Rupees(d.TdsAmount), geo.RateR, geo.TaxR, y, page.BodyFontSize, false);
            y -= page.RowHeight;
            sr++;
        }
        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.5);
        RightText(writer, "Total", geo.DateX, geo.PaidR, y, page.BodyFontSize, true);
        RightText(writer, Rupees(cert.TotalAmountPaid), geo.DateX, geo.PaidR, y - page.RowHeight, page.BodyFontSize, true);
        RightText(writer, Rupees(cert.TotalTdsDeducted), geo.RateR, geo.TaxR, y - page.RowHeight, page.BodyFontSize, true);
        y -= page.RowHeight * 2 + 4;

        // ---- Challan / deposit table ----
        writer.Text(left, y, "Details of tax deposited (challan-wise)", page.BodyFontSize, bold: true);
        y -= page.RowHeight;
        y = DrawChallanHeader(writer, page, geo, left, right, y);
        sr = 1;
        foreach (var ch in cert.Challans)
        {
            writer.Text(geo.SrX, y, sr.ToString(CultureInfo.InvariantCulture), page.BodyFontSize, false);
            writer.Text(geo.SecX, y, ch.ChallanNo, page.BodyFontSize, false);
            writer.Text(geo.DateX, y, ch.BsrCode, page.BodyFontSize, false);
            RightText(writer, Date(ch.DepositDate), geo.DateX, geo.RateR, y, page.BodyFontSize, false);
            RightText(writer, Rupees(ch.TdsDeposited), geo.RateR, geo.TaxR, y, page.BodyFontSize, false);
            y -= page.RowHeight;
            sr++;
        }
        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.5);
        RightText(writer, "Total deposited", geo.DateX, geo.RateR, y, page.BodyFontSize, true);
        RightText(writer, Rupees(cert.TotalTdsDeposited), geo.RateR, geo.TaxR, y, page.BodyFontSize, true);
        y -= page.RowHeight + 4;

        // ---- Amount in words + signature ----
        string words = "Total tax deducted (in words): " + IndianAmountInWords.Convert(cert.TotalTdsDeducted.Amount);
        foreach (var wl in VoucherPdf.WrapText(words, page.ContentWidth, page.BodyFontSize))
        {
            writer.Text(left, y, wl, page.BodyFontSize, false);
            y -= page.BodyFontSize + 3;
        }
        y -= page.RowHeight;
        string forDeductor = "For " + Debrand.Text(cert.Deductor.Name);
        writer.Text(right - PdfWriter.MeasureHelvetica(forDeductor, page.BodyFontSize), y, forDeductor, page.BodyFontSize, true);
        y -= page.RowHeight + 4;
        const string sig = "Authorised Signatory";
        writer.Text(right - PdfWriter.MeasureHelvetica(sig, page.FooterFontSize), y, sig, page.FooterFontSize, false);

        DrawFooter(writer, page, left, right);
        return writer.Build();
    }

    private sealed class Cols
    {
        public readonly double SrX, SecX, DateX, PaidR, RateR, TaxR;
        public Cols(double left, double right, PageConfig page)
        {
            SrX = left;
            SecX = left + 24;
            DateX = left + page.ContentWidth * 0.34;
            PaidR = left + page.ContentWidth * 0.66;
            RateR = left + page.ContentWidth * 0.80;
            TaxR = right;
        }
    }

    private static double DrawDeductionHeader(PdfWriter w, PageConfig page, Cols c, double left, double right, double y)
    {
        w.Text(c.SrX, y, "Sr", page.BodyFontSize, true);
        w.Text(c.SecX, y, "Section", page.BodyFontSize, true);
        w.Text(c.DateX, y, "Date", page.BodyFontSize, true);
        RightText(w, "Amount Paid", c.DateX, c.PaidR, y, page.BodyFontSize, true);
        RightText(w, "Rate", c.PaidR, c.RateR, y, page.BodyFontSize, true);
        RightText(w, "TDS", c.RateR, c.TaxR, y, page.BodyFontSize, true);
        y -= 3;
        w.Line(left, y, right, y, 0.5);
        return y - page.RowHeight;
    }

    private static double DrawChallanHeader(PdfWriter w, PageConfig page, Cols c, double left, double right, double y)
    {
        w.Text(c.SrX, y, "Sr", page.BodyFontSize, true);
        w.Text(c.SecX, y, "Challan No", page.BodyFontSize, true);
        w.Text(c.DateX, y, "BSR Code", page.BodyFontSize, true);
        RightText(w, "Deposit Date", c.DateX, c.RateR, y, page.BodyFontSize, true);
        RightText(w, "TDS Deposited", c.RateR, c.TaxR, y, page.BodyFontSize, true);
        y -= 3;
        w.Line(left, y, right, y, 0.5);
        return y - page.RowHeight;
    }

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right)
    {
        string footer = (page.FooterText ?? string.Empty).Replace("{page}", "1").Replace("{pages}", "1");
        if (footer.Length > 0)
            Center(writer, footer, left, right, page.MarginBottom, page.FooterFontSize, false);
    }
}
