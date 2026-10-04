using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE PRINT PREVIEW MUST NOT TELL THE OPERATOR SOMETHING THE EMITTED PDF CONTRADICTS — ASSERTED ON THE
/// EMITTED BYTES AND ON THE BOUND PROPERTY THE CHECKBOX RENDERS, NEVER ON A CONFIG FLAG.</b>
///
/// <para><b>The two defects these lock, and both were INTRODUCED by the crore-scale truncation fix rather than
/// found on it.</b> That fix gave <c>ReportPdf</c> the power to turn a too-wide report onto its side
/// (<c>FitOrientation</c> / <see cref="PageConfig.AutoFitOrientation"/>, true by default). Two visible things in
/// the pane were left behind by it:</para>
///
/// <para><b>T2-81 — the Landscape checkbox.</b> <c>AutoFitOrientation</c> was passed <c>false</c> by nothing in
/// <c>src/</c>, so the box could neither refuse the turn nor report it: a GSTR-1 with real party names came out
/// landscape with the box reading unchecked, and unchecking could not produce portrait while checking changed
/// nothing. A bound, visible control that displays a state it does not cause is worse than no control, so both
/// directions are held here — the box READS the orientation of the bytes, and WRITING it is absolute.</para>
///
/// <para><b>T2-82 — the sheet count.</b> <c>PaginateForPreview</c> re-derived its own rows-per-page from the
/// config, which agreed with the PDF only while the PDF honoured that config verbatim: 53 rows to a portrait
/// sheet in the pane against 34 to a landscape sheet in the file. A 40-row return previewed as ONE sheet and
/// showed <i>Pages: 1</i> while two sheets came out of the printer, so an operator counting sheets or setting a
/// page range worked from a wrong number.</para>
///
/// <para><b>Why none of this reddened the suite that shipped it.</b> Every existing assertion read either the
/// PDF alone or the pane alone. Neither defect exists inside one of them — each is a DISAGREEMENT between the
/// two, and only an assertion that holds the pane against the bytes can see it. Every test below renders through
/// the production view-model and reads the emitted <c>/MediaBox</c> and <c>/Type /Page</c> objects.</para>
///
/// <para><b>Honest provenance:</b> these pass against today's <c>origin/main</c>, where <c>ReportPdf</c> never
/// turns a page and so the pane and the file trivially agree. They are regression guards on a regression
/// introduced on this branch, and they fail on this branch without the fix beside them.</para>
/// </summary>
public sealed class PrintPreviewOrientationAndSheetCountTests
{
    /// <summary>The widest figure an Indian money column must hold below a hundred crore, in the grouped form the
    /// report rows carry.</summary>
    private const string WidestMoney = "99,99,99,999.00";

    /// <summary>A realistic 42-character party name. The prose columns hold no slack with one of these in them,
    /// which is the condition under which the renderer turns the sheet.</summary>
    private const string LongLabel = "Brightline Industrial Supplies Private Ltd";

    /// <summary>
    /// A GSTR-1-shaped return: four label columns and five figure columns, the band the truncation fix measured
    /// as the worst of the thirty-seven. Portrait A4 cannot hold it with the figures whole, so the renderer turns
    /// it; that is exactly the case in which the pane used to disagree with the file.
    /// </summary>
    private static PrintReport WideReturn(int rowCount)
    {
        var columns = new List<PrintColumn>
        {
            new("Particulars", 3),
            new("GSTIN/UIN", 2.2),
            new("Invoice No.", 2.2),
            new("Place of Supply", 2.2),
            new("Taxable Value", 1.5, CellAlign.Right),
            new("IGST", 1.5, CellAlign.Right),
            new("CGST", 1.5, CellAlign.Right),
            new("SGST", 1.5, CellAlign.Right),
            new("Cess", 1.5, CellAlign.Right),
        };

        var rows = new List<PrintRow>();
        for (int i = 0; i < rowCount; i++)
            rows.Add(new PrintRow(LongLabel, LongLabel, LongLabel, LongLabel,
                WidestMoney, WidestMoney, WidestMoney, WidestMoney, WidestMoney));

        return new PrintReport { Title = "Outward Supplies", Columns = columns, Rows = rows };
    }

    /// <summary>A narrow report that fits portrait with room to spare — the common case, which must be untouched
    /// by everything below.</summary>
    private static PrintReport NarrowStatement(int rowCount)
    {
        var columns = new List<PrintColumn>
        {
            new("Particulars", 3),
            new("Amount", 1.5, CellAlign.Right),
        };

        var rows = new List<PrintRow>();
        for (int i = 0; i < rowCount; i++)
            rows.Add(new PrintRow("Sundry Debtors", "1,250.00"));

        return new PrintReport { Title = "Trial Balance", Columns = columns, Rows = rows };
    }

    // ---- T2-81: the checkbox reports, and causes, the orientation of the bytes -------------------------------

    /// <summary>
    /// 🔴 The box must not read PORTRAIT over a sideways sheet. The operator opens a wide return, touches
    /// nothing, and the renderer turns the page: the bound property the checkbox renders must say so.
    /// </summary>
    [Fact]
    public void Landscape_box_reads_the_orientation_the_pdf_was_actually_emitted_at()
    {
        var preview = new PrintPreviewViewModel(WideReturn(5), "Outward Supplies");

        Assert.True(EmittedIsLandscape(preview.PdfBytes),
            "fixture no longer exercises the defect: this report was expected to be too wide for portrait and "
            + "to be turned by ReportPdf, but the emitted MediaBox is portrait.");

        Assert.True(preview.Landscape,
            "the Landscape checkbox read UNCHECKED while the emitted PDF is landscape. A bound, visible control "
            + "that displays a state it does not cause is worse than no control.");
    }

    /// <summary>
    /// 🔴 The operator's choice is ABSOLUTE. Unticking Landscape on a report that does not fit portrait must
    /// produce a portrait document — auto-fit decides only the case nobody has decided. The figures may clip on
    /// that page; that is the operator's call, and it is a call they must be able to make.
    /// </summary>
    [Fact]
    public void Unticking_landscape_pins_portrait_even_on_a_report_that_does_not_fit()
    {
        var preview = new PrintPreviewViewModel(WideReturn(5), "Outward Supplies");
        Assert.True(EmittedIsLandscape(preview.PdfBytes));   // auto-fit turned it, as the fixture intends

        preview.Landscape = false;                            // the operator refuses the turn

        Assert.False(EmittedIsLandscape(preview.PdfBytes),
            "the operator unticked Landscape and the sheet still came out sideways: the control cannot refuse "
            + "the auto-fit, so no operator can pin an orientation.");
        Assert.False(preview.Landscape);
    }

    /// <summary>The other direction, so a fix cannot satisfy the test above by simply disabling auto-fit:
    /// ticking the box must still turn the page, including on a narrow report that had no need to turn.</summary>
    [Fact]
    public void Ticking_landscape_emits_landscape_even_when_portrait_would_have_fitted()
    {
        var preview = new PrintPreviewViewModel(NarrowStatement(5), "Trial Balance");
        Assert.False(EmittedIsLandscape(preview.PdfBytes));

        preview.Landscape = true;

        Assert.True(EmittedIsLandscape(preview.PdfBytes));
        Assert.True(preview.Landscape);
    }

    /// <summary>The common case stays exactly as it was: a report that fits portrait is not turned, and the box
    /// correctly reads unchecked.</summary>
    [Fact]
    public void A_report_that_fits_portrait_is_not_turned_and_the_box_reads_unchecked()
    {
        var preview = new PrintPreviewViewModel(NarrowStatement(5), "Trial Balance");

        Assert.False(EmittedIsLandscape(preview.PdfBytes));
        Assert.False(preview.Landscape);
    }

    // ---- T2-82: the sheet count shown is the sheet count emitted ---------------------------------------------

    /// <summary>
    /// 🔴 THE READOUT AND THE PAPER. <c>Pages: N</c> binds <c>PageCount</c>, which counts the pane's own sheets;
    /// it must equal the number of page objects in the emitted PDF. Forty rows is the measured case: the pane
    /// counted one portrait sheet of 53 rows while the file carried two landscape sheets of 34.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(1)]
    [InlineData(34)]
    [InlineData(35)]
    [InlineData(80)]
    public void Sheet_count_shown_equals_the_sheet_count_emitted_for_a_turned_report(int rowCount)
    {
        var preview = new PrintPreviewViewModel(WideReturn(rowCount), "Outward Supplies");

        Assert.True(EmittedIsLandscape(preview.PdfBytes));
        Assert.Equal(EmittedPageCount(preview.PdfBytes), preview.PageCount);
    }

    /// <summary>The same agreement on the untouched common case, so the fix cannot buy the test above by
    /// breaking the pagination everything else uses.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(120)]
    public void Sheet_count_shown_equals_the_sheet_count_emitted_for_a_portrait_report(int rowCount)
    {
        var preview = new PrintPreviewViewModel(NarrowStatement(rowCount), "Trial Balance");

        Assert.False(EmittedIsLandscape(preview.PdfBytes));
        Assert.Equal(EmittedPageCount(preview.PdfBytes), preview.PageCount);
    }

    /// <summary>A SET (W2-32) is orientation-per-document, so the count must still agree when one member is
    /// turned and the other is not.</summary>
    [Fact]
    public void Sheet_count_shown_equals_the_sheet_count_emitted_for_a_mixed_set()
    {
        var job = new[] { NarrowStatement(60), WideReturn(40) };
        var preview = new PrintPreviewViewModel(job, "Print Job");

        Assert.Equal(EmittedPageCount(preview.PdfBytes), preview.PageCount);
        Assert.True(preview.Landscape,
            "one member of the job was turned onto its side and the box read unchecked. Unchecked over a turned "
            + "sheet is the direction that misleads.");
    }

    /// <summary>
    /// The layout the preview reads and the layout the renderer draws come from one method, so they cannot drift
    /// apart again. Held directly against the bytes for the turned case.
    /// </summary>
    [Fact]
    public void ReportPdf_LayOut_reports_the_page_and_the_sheet_count_the_render_emits()
    {
        var report = WideReturn(40);
        var config = new PageConfig
        {
            Size = PageSize.A4,
            Orientation = PageOrientation.Portrait,
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        };

        var (page, sheets) = ReportPdf.LayOut(report, config);
        byte[] pdf = ReportPdf.Render(report, config);

        Assert.Equal(PageOrientation.Landscape, page.Orientation);
        Assert.True(EmittedIsLandscape(pdf));
        Assert.Equal(EmittedPageCount(pdf), sheets.Count);
        Assert.Equal(report.Rows.Count, sheets.Sum(s => s.Count));

        // 🔴 ABSOLUTE, NOT MERELY CONSISTENT. Agreement alone is satisfiable by making BOTH sides wrong the same
        // way — paginating the landscape sheet at the portrait pitch keeps the two counts equal while the rows
        // run off the bottom of the paper. The measured landscape capacity is pinned here: A4 turned gives
        // 559.276pt from the top margin down to the 50pt footer reserve, less a 55pt banner, at a 13pt pitch.
        Assert.Equal(LandscapeRowsPerSheet, sheets[0].Count);
        Assert.Equal(2, sheets.Count);
    }

    /// <summary>The rows one turned A4 sheet holds at the shipped 13pt pitch, measured on the renderer.</summary>
    private const int LandscapeRowsPerSheet = 34;

    /// <summary>The rows one portrait A4 sheet holds at the same pitch — the untouched common case, pinned so a
    /// change to the pagination everything else uses cannot pass unnoticed.</summary>
    private const int PortraitRowsPerSheet = 53;

    /// <summary>The absolute capacities both ways, so neither side can be wrong consistently.</summary>
    [Fact]
    public void A_sheet_holds_the_measured_number_of_rows_each_way_up()
    {
        var portrait = new PrintPreviewViewModel(NarrowStatement(PortraitRowsPerSheet), "Trial Balance");
        Assert.Equal(1, portrait.PageCount);

        var portraitOver = new PrintPreviewViewModel(NarrowStatement(PortraitRowsPerSheet + 1), "Trial Balance");
        Assert.Equal(2, portraitOver.PageCount);

        var landscape = new PrintPreviewViewModel(WideReturn(LandscapeRowsPerSheet), "Outward Supplies");
        Assert.Equal(1, landscape.PageCount);

        var landscapeOver = new PrintPreviewViewModel(WideReturn(LandscapeRowsPerSheet + 1), "Outward Supplies");
        Assert.Equal(2, landscapeOver.PageCount);
    }

    // ---- reading the artefact --------------------------------------------------------------------------------

    /// <summary>The number of page objects in the emitted PDF. <c>PdfWriter</c> writes one
    /// <c>&lt;&lt; /Type /Page /Parent …</c> dictionary per sheet, uncompressed, so this counts real sheets —
    /// <c>/Type /Pages</c>, the tree node, carries no <c>/Parent</c> and is not matched.</summary>
    private static int EmittedPageCount(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        return Regex.Matches(text, @"/Type /Page /Parent").Count;
    }

    /// <summary>Whether the emitted sheets are wider than they are tall, read from the first <c>/MediaBox</c>.
    /// This is the orientation the paper comes out at, not the orientation anything was asked for.</summary>
    private static bool EmittedIsLandscape(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var m = Regex.Match(text, @"/MediaBox \[0 0 ([0-9.]+) ([0-9.]+)\]");
        Assert.True(m.Success, "the emitted PDF carries no /MediaBox — nothing was drawn.");
        double width = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        double height = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return width > height;
    }
}
