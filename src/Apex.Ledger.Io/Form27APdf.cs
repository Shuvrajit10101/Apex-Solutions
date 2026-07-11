using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Reports;
using static Apex.Ledger.Io.CertificatePdfSupport;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a <see cref="Form27A"/> <b>return control chart</b> (catalog §13) as a deterministic, de-branded PDF: the
/// verification cover a filer cross-checks before FVU validation. It lays out the return identity (form / FY / quarter
/// / TAN), the control totals (deductee/collectee record count, challan record count, total amount, total tax, total
/// deposited), the total tax in words, and the <b>tally status</b> — "Control totals tally" when the return is clear,
/// or the FVU-style mismatch messages when not. All figures come verbatim from the <see cref="Form27A"/> projection so
/// they tally with the underlying Form 26Q / 27EQ by construction; the same chart renders byte-identically.
/// </summary>
public static class Form27APdf
{
    private const string Title = "FORM 27A";
    private const string Subtitle = "Control chart / control totals for the quarterly return (pre-FVU verification)";

    /// <summary>Renders the control chart to PDF bytes.</summary>
    public static byte[] Render(Form27A chart, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(page);

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double mid = left + page.ContentWidth / 2.0;
        bool tcs = chart.ReturnFormName == "27EQ";
        string party = tcs ? "collectee" : "deductee";
        string tax = tcs ? "TCS collected" : "TDS deducted";
        string amount = tcs ? "amount received" : "amount paid";

        var writer = new PdfWriter { DocumentTitle = SafeTitle(Title) };
        writer.BeginPage(page.PageWidth, page.PageHeight);
        double y = page.PageHeight - page.MarginTop;

        y -= page.TitleFontSize;
        Center(writer, Title, left, right, y, page.TitleFontSize, bold: true);
        y -= page.SubtitleFontSize + 2;
        Center(writer, Subtitle, left, right, y, page.SubtitleFontSize, bold: false);
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 4;

        writer.Text(left, y, "Return: Form " + chart.ReturnFormName, page.BodyFontSize, bold: true);
        writer.Text(mid + 6, y, "TAN: " + chart.Tan, page.BodyFontSize, bold: true);
        y -= page.BodyFontSize + 3;
        writer.Text(left, y, "Financial Year: " + chart.FinancialYearLabel, page.BodyFontSize, bold: false);
        writer.Text(mid + 6, y, "Quarter: " + chart.QuarterLabel + "  (" + Date(chart.From) + " to " + Date(chart.To) + ")",
            page.BodyFontSize, bold: false);
        y -= page.BodyFontSize + 4;
        writer.Line(left, y, right, y, 0.4);
        y -= page.BodyFontSize + 4;

        // ---- Control totals table ----
        double valueR = right;
        writer.Text(left, y, "Control totals", page.BodyFontSize, bold: true);
        y -= page.RowHeight;

        void Row(string label, string value, bool bold = false)
        {
            writer.Text(left, y, label, page.BodyFontSize, bold);
            RightText(writer, value, mid, valueR, y, page.BodyFontSize, bold);
            y -= page.RowHeight;
        }

        Row("Count of " + party + " records", chart.DeducteeRecordCount.ToString(CultureInfo.InvariantCulture));
        Row("Count of challan records", chart.ChallanRecordCount.ToString(CultureInfo.InvariantCulture));
        Row("Total " + amount, Rupees(chart.TotalAmount));
        Row("Total " + tax, Rupees(chart.TotalTax), bold: true);
        Row("Total tax deposited (as per challans)", Rupees(chart.TotalDeposited));
        Row("Tax deposited against this quarter's " + party + " records", Rupees(chart.TotalTaxDepositedForQuarter));
        y -= 2;
        writer.Line(left, y, right, y, 0.5);
        y -= page.BodyFontSize + 4;

        // ---- Total tax in words ----
        string words = "Total " + tax + " (in words): " + IndianAmountInWords.Convert(chart.TotalTax.Amount);
        foreach (var wl in VoucherPdf.WrapText(words, page.ContentWidth, page.BodyFontSize))
        {
            writer.Text(left, y, wl, page.BodyFontSize, false);
            y -= page.BodyFontSize + 3;
        }
        y -= page.RowHeight;

        // ---- Tally status / cross-check ----
        if (chart.Tallies)
        {
            writer.Text(left, y, "Status: Control totals AGREE — the return cross-checks and is clear for FVU validation.",
                page.BodyFontSize, bold: true);
            y -= page.RowHeight;
        }
        else
        {
            writer.Text(left, y, "Status: Control totals DO NOT agree — resolve before FVU validation:", page.BodyFontSize, bold: true);
            y -= page.RowHeight;
            foreach (var msg in chart.ControlValidationMessages)
                foreach (var ml in VoucherPdf.WrapText("- " + msg, page.ContentWidth, page.BodyFontSize))
                {
                    writer.Text(left + 6, y, ml, page.BodyFontSize, false);
                    y -= page.BodyFontSize + 3;
                }
        }

        y -= page.RowHeight;
        const string sig = "Signature of person responsible";
        writer.Text(right - PdfWriter.MeasureHelvetica(sig, page.FooterFontSize), y, sig, page.FooterFontSize, false);

        DrawFooter(writer, page, left, right);
        return writer.Build();
    }

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right)
    {
        string footer = (page.FooterText ?? string.Empty).Replace("{page}", "1").Replace("{pages}", "1");
        if (footer.Length > 0)
            Center(writer, footer, left, right, page.MarginBottom, page.FooterFontSize, false);
    }
}
