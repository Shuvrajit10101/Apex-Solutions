using System.Globalization;
using Apex.Ledger.Reports;
using static Apex.Ledger.Io.CertificatePdfSupport;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a <see cref="Payslip"/> as a deterministic, de-branded PDF through the same pipeline as the GST tax
/// invoice / TDS certificates (<see cref="PdfWriter"/> + <see cref="IndianAmountInWords"/>): the employer band, the
/// employee identity block, side-by-side earnings and deductions tables, the gross / total-deductions / net-pay
/// summary, the net pay in Indian amount-in-words, the employer contributions (informational) and the attendance
/// summary. Every figure is read verbatim off the <see cref="Payslip"/> projection (so it matches the payroll
/// voucher to the paisa); the same payslip renders byte-identically and carries no third-party brand.
/// </summary>
public static class PayslipPdf
{
    private const string Title = "PAYSLIP";

    /// <summary>Renders the payslip to PDF bytes.</summary>
    public static byte[] Render(Payslip slip, PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(page);

        double left = page.MarginLeft;
        double right = page.PageWidth - page.MarginRight;
        double mid = left + page.ContentWidth / 2.0;

        // Two side-by-side tables: earnings (left half) + deductions (right half).
        double earnAmtR = mid - 8;
        double dedLabelX = mid + 8;
        double dedAmtR = right;

        var writer = new PdfWriter { DocumentTitle = SafeTitle(Title) };
        writer.BeginPage(page.PageWidth, page.PageHeight);
        double y = page.PageHeight - page.MarginTop;

        // ---- Employer band ----
        y -= page.TitleFontSize;
        Center(writer, Debrand.Text(slip.EmployerName), left, right, y, page.TitleFontSize, bold: true);
        if (!string.IsNullOrWhiteSpace(slip.EmployerAddress))
        {
            y -= page.SubtitleFontSize + 2;
            Center(writer, Debrand.Text(slip.EmployerAddress), left, right, y, page.SubtitleFontSize, bold: false);
        }
        y -= page.SubtitleFontSize + 4;
        Center(writer, Title + " for " + PeriodLabel(slip.PeriodFrom, slip.PeriodTo), left, right, y, page.SubtitleFontSize, bold: true);
        y -= 6;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 4;

        // ---- Employee identity (left + right blocks) ----
        double blockTop = y;
        double dy = y;
        dy = KeyVal(writer, left, dy, "Employee:", slip.EmployeeName, page);
        dy = KeyVal(writer, left, dy, "Emp No:", slip.EmployeeNumber ?? "-", page);
        dy = KeyVal(writer, left, dy, "Designation:", slip.Designation ?? "-", page);
        dy = KeyVal(writer, left, dy, "Department:", slip.Department ?? "-", page);
        dy = KeyVal(writer, left, dy, "Date of Joining:", slip.DateOfJoining is { } doj ? Date(doj) : "-", page);

        double ey = blockTop;
        ey = KeyVal(writer, dedLabelX, ey, "PAN:", slip.Pan ?? "-", page);
        ey = KeyVal(writer, dedLabelX, ey, "UAN:", slip.Uan ?? "-", page);
        ey = KeyVal(writer, dedLabelX, ey, "ESI No:", slip.EsiNumber ?? "-", page);
        ey = KeyVal(writer, dedLabelX, ey, "Bank:", slip.BankName ?? "-", page);
        ey = KeyVal(writer, dedLabelX, ey, "A/c No:", slip.BankAccountNumber ?? "-", page);
        ey = KeyVal(writer, dedLabelX, ey, "IFSC:", slip.BankIfsc ?? "-", page);

        y = Math.Min(dy, ey) - 2;
        writer.Line(left, y, right, y, 0.8);
        y -= page.BodyFontSize + 2;

        // ---- Earnings + Deductions headers ----
        writer.Text(left, y, "Earnings", page.BodyFontSize, bold: true);
        RightText(writer, "Amount", left, earnAmtR, y, page.BodyFontSize, true);
        writer.Text(dedLabelX, y, "Deductions", page.BodyFontSize, bold: true);
        RightText(writer, "Amount", dedLabelX, dedAmtR, y, page.BodyFontSize, true);
        y -= 3;
        writer.Line(left, y, right, y, 0.5);
        y -= page.RowHeight;

        int rowCount = Math.Max(slip.Earnings.Count, slip.Deductions.Count);
        for (int i = 0; i < rowCount; i++)
        {
            if (i < slip.Earnings.Count)
            {
                var e = slip.Earnings[i];
                writer.Text(left, y, Debrand.Text(FitLabel(e.Name, earnAmtR - left - 60, page)), page.BodyFontSize, false);
                RightText(writer, Rupees(e.Amount), left, earnAmtR, y, page.BodyFontSize, false);
            }
            if (i < slip.Deductions.Count)
            {
                var d = slip.Deductions[i];
                writer.Text(dedLabelX, y, Debrand.Text(FitLabel(d.Name, dedAmtR - dedLabelX - 60, page)), page.BodyFontSize, false);
                RightText(writer, Rupees(d.Amount), dedLabelX, dedAmtR, y, page.BodyFontSize, false);
            }
            y -= page.RowHeight;
        }

        writer.Line(left, y + page.RowHeight - 3, right, y + page.RowHeight - 3, 0.5);
        writer.Text(left, y, "Gross Earnings", page.BodyFontSize, bold: true);
        RightText(writer, Rupees(slip.GrossEarnings), left, earnAmtR, y, page.BodyFontSize, true);
        writer.Text(dedLabelX, y, "Total Deductions", page.BodyFontSize, bold: true);
        RightText(writer, Rupees(slip.TotalDeductions), dedLabelX, dedAmtR, y, page.BodyFontSize, true);
        y -= page.RowHeight + 4;

        // ---- Net pay ----
        writer.Line(left, y + 4, right, y + 4, 0.8);
        writer.Text(left, y - page.BodyFontSize, "Net Pay", page.BodyFontSize + 1, bold: true);
        RightText(writer, Rupees(slip.NetPayable), left, right, y - page.BodyFontSize, page.BodyFontSize + 1, true);
        y -= page.RowHeight + page.BodyFontSize;
        writer.Line(left, y + 6, right, y + 6, 0.8);
        y -= 4;

        string words = "Net Pay (in words): " + IndianAmountInWords.Convert(slip.NetPayable.Amount);
        foreach (var wl in VoucherPdf.WrapText(words, page.ContentWidth, page.BodyFontSize))
        {
            writer.Text(left, y, wl, page.BodyFontSize, false);
            y -= page.BodyFontSize + 3;
        }
        y -= 4;

        // ---- Employer contributions (informational) ----
        if (slip.EmployerContributions.Count > 0)
        {
            writer.Text(left, y, "Employer Contributions (not part of net pay)", page.FooterFontSize, bold: true);
            y -= page.RowHeight;
            foreach (var ec in slip.EmployerContributions)
            {
                writer.Text(left, y, Debrand.Text(FitLabel(ec.Name, earnAmtR - left - 60, page)), page.BodyFontSize, false);
                RightText(writer, Rupees(ec.Amount), left, earnAmtR, y, page.BodyFontSize, false);
                y -= page.RowHeight;
            }
            y -= 2;
        }

        // ---- Attendance summary ----
        string attendance = "Days Paid: " + Num(slip.DaysPaid) + "     Loss of Pay (LOP): " + Num(slip.DaysLop);
        writer.Text(left, y, attendance, page.FooterFontSize, bold: false);

        DrawFooter(writer, page, left, right);
        return writer.Build();
    }

    /// <summary>Fits a line label into its column width (ellipsised when too long) so it never overruns the amount.</summary>
    private static string FitLabel(string label, double maxWidth, PageConfig page) =>
        PdfWriter.FitToWidth(label, Math.Max(maxWidth, 20), page.BodyFontSize);

    /// <summary>A period label — "01-04-2025 to 30-04-2025" (invariant, no clock).</summary>
    private static string PeriodLabel(DateOnly from, DateOnly to) => Date(from) + " to " + Date(to);

    /// <summary>Formats an attendance day/hour count invariantly, trimming trailing zeros.</summary>
    private static string Num(decimal v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void DrawFooter(PdfWriter writer, PageConfig page, double left, double right)
    {
        string footer = (page.FooterText ?? string.Empty).Replace("{page}", "1").Replace("{pages}", "1");
        if (footer.Length > 0)
            Center(writer, Debrand.Text(footer), left, right, page.MarginBottom, page.FooterFontSize, false);
    }
}
