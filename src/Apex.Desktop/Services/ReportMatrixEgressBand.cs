using System.Collections.Generic;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Services;

/// <summary>
/// The ONE egress column band for a dynamic-matrix report that carries a SECOND section
/// (<see cref="ReportsViewModel.PayrollColumns2"/> / <see cref="ReportsViewModel.PayrollRows2"/>), read by
/// <see cref="ReportTabularProjector"/> and <see cref="ReportPrintProjector"/> alike.
///
/// <para>🔴 <b>WHY THIS TYPE EXISTS — A FILED ANNUAL RETURN PRINTED AND EXPORTED WITH ITS MONEY GONE.</b>
/// <c>PrintReport</c> and <c>TabularExport</c> each carry exactly ONE column band, and every writer loops to
/// <c>Columns.Count</c> (<c>DelimitedText.RowFields</c>, <c>XlsxWriter</c>, <c>HtmlReportWriter</c>,
/// <c>XmlReportWriter</c>, <c>JsonReportWriter</c>) or clamps with <c>Math.Min(Columns.Count, row.Cells.Count)</c>
/// (<c>ReportPdf</c>). Both projectors used to build that band from band 1 alone and then append section 2's rows
/// under it. That is harmless while section 2 is the NARROWER of the two — which was true of the only report that
/// had a section 2 when the feature was written (PF Form 6A: 9 columns on page 1, 8 on page 2) — and it silently
/// DELETES section 2's right-hand cells the moment section 2 is wider.
/// <list type="bullet">
///   <item><b>GSTR-9 Part VI Table 17</b> (band 1 = 2, band 2 = 6) printed and exported its outward HSN summary
///   with <c>Quantity</c>, <c>Taxable Value</c> and <c>Total Tax</c> GONE, and its statutory total line reduced to
///   the bare string <c>"17  Total"</c> with no figure beside it. A filed annual return rendering blank money.</item>
///   <item><b>Income Tax Computation</b> (2, 3) dropped the <c>Amount</c> of every Component-details line.</item>
///   <item><b>Payroll Statutory Summary</b> (4, 6) dropped <c>Paid</c> and <c>Balance</c> off every
///   Statutory-Pay-Head-Details line.</item>
/// </list>
/// Three reports, one shape. The on-screen grid was right in all three cases — it renders the two bands as two
/// independent grids — so a green suite that asserted on the view model could not see any of it. That is why
/// <c>MatrixSecondBandEgressTests</c> asserts on the EMITTED CSV and the EMITTED PDF bytes.</para>
///
/// <para>🔴 <b>WHY THE SURPLUS COLUMNS ARE CAPTIONED FROM BAND 2 AND NOT LEFT BLANK.</b> The obvious widening —
/// pad band 1 out to the wider count with <c>new PrintColumn(string.Empty, …)</c> — re-opens census 12.1(b), the
/// blank-printed-caption defect this project has already shipped once. Index <c>i &gt;= band1.Count</c> is a
/// position NO band-1 row ever occupies, so the only cells that can ever land there are band 2's; captioning
/// those positions from band 2 is therefore the only caption that is TRUE of every cell underneath it, and it is
/// never blank. Positions below <c>band1.Count</c> keep band 1's caption, alignment and numeric flag unchanged —
/// band 1's own rows must not move under a heading that is not theirs, which is the "believable heading over the
/// wrong figures" failure that is worse than a blank one.</para>
///
/// <para>Section 2's rows still carry their own in-body heading row immediately above them (both projectors emit
/// it), so a reader of the spreadsheet or the printed page sees band 2's real captions over band 2's real figures.
/// The merged band is what stops the writers from truncating those figures away before anyone can read them.</para>
///
/// <para>A report with no second section, or whose second section is no wider than its first, gets band 1 back
/// unchanged — reference-identical where possible — so nothing about PF Form 6A's two pages moves.</para>
/// </summary>
public static class ReportMatrixEgressBand
{
    /// <summary>
    /// The column band both egress projectors must use for a dynamic-matrix report: band 1 in full, followed by
    /// band 2's surplus columns when band 2 is wider. Never shorter than either band's widest row-bearing band.
    /// </summary>
    public static IReadOnlyList<PayrollMatrixColumnVm> For(ReportsViewModel vm)
    {
        var band1 = vm.PayrollColumns;
        var band2 = vm.PayrollColumns2;

        // Nothing in section 2, or section 2 fits inside section 1's band: band 1 is already the widest thing any
        // writer has to carry. Hand it back untouched.
        if (vm.PayrollRows2.Count == 0 || band2.Count <= band1.Count)
            return band1;

        var merged = new List<PayrollMatrixColumnVm>(band2.Count);
        foreach (var c in band1) merged.Add(c);
        for (int i = band1.Count; i < band2.Count; i++) merged.Add(band2[i]);
        return merged;
    }
}
