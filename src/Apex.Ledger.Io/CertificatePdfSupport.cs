using System.Globalization;
using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// Shared, deterministic drawing + formatting primitives for the Phase-7 TDS/TCS statutory certificate PDFs
/// (<see cref="Form16APdf"/>, <see cref="Form27DPdf"/>, <see cref="Form27APdf"/>). Mirrors the helpers baked into
/// <see cref="InvoicePdf"/> — Indian-grouped money (e.g. 100000 → "1,00,000.00"), invariant date text, and
/// left/right/centre text placement — factored out so the three certificate renderers stay identical in look and
/// byte-stable. Culture-invariant: no clock, no RNG, no host-locale dependence.
/// </summary>
internal static class CertificatePdfSupport
{
    private static readonly CultureInfo Indian = CreateIndianCulture();

    private static CultureInfo CreateIndianCulture()
    {
        var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        ci.NumberFormat.NumberGroupSizes = new[] { 3, 2 };
        ci.NumberFormat.NumberGroupSeparator = ",";
        ci.NumberFormat.NumberDecimalSeparator = ".";
        return ci;
    }

    /// <summary>Formats a rupee amount with Indian (3;2;2) digit grouping and 2 decimals, e.g. "1,00,000.00".</summary>
    internal static string Rupees(Money m) => Rupees(m.Amount);

    /// <summary>Formats a rupee amount with Indian grouping and 2 decimals.</summary>
    internal static string Rupees(decimal v) => v.ToString("#,##0.00", Indian);

    /// <summary>Formats a date as "dd-MM-yyyy" (invariant), the certificate/return date convention.</summary>
    internal static string Date(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>Draws a horizontally-centred line of text within [left, right] at baseline <paramref name="y"/>.</summary>
    internal static void Center(PdfWriter w, string text, double left, double right, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = (left + right) / 2.0 - tw / 2.0;
        if (x < left) x = left;
        w.Text(x, y, text, size, bold);
    }

    /// <summary>Draws <paramref name="text"/> right-aligned so its right edge sits at <paramref name="cellRight"/>.</summary>
    internal static void RightText(PdfWriter w, string text, double cellLeft, double cellRight, double y, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        double tw = PdfWriter.MeasureHelvetica(text, size);
        double x = cellRight - tw;
        if (x < cellLeft) x = cellLeft;
        w.Text(x, y, text, size, bold);
    }

    /// <summary>Draws a "Label: value" pair (bold label, regular value) at (x, y), returning the next baseline.</summary>
    internal static double KeyVal(PdfWriter w, double x, double y, string label, string? value, PageConfig page)
    {
        w.Text(x, y, label, page.FooterFontSize, bold: true);
        double lw = PdfWriter.MeasureHelvetica(label + " ", page.FooterFontSize);
        w.Text(x + lw, y, Debrand.Text(value ?? "-"), page.BodyFontSize, bold: false);
        return y - (page.BodyFontSize + 3);
    }

    /// <summary>The de-branded document /Title suffix used on every certificate's metadata.</summary>
    internal static string SafeTitle(string title) => Debrand.Text(title) + " — Apex Solutions";
}
