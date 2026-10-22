using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b><see cref="ReportPdf.SplitWideColumns"/> — COLUMN PAGINATION, THE THING THIS RENDERER HAD NONE OF.</b>
///
/// <para><b>Why it exists, measured rather than predicted.</b> <c>ComputeColumnX</c> abbreviates prose to free
/// width for the figures and <c>FitOrientation</c> turns the sheet, and between them they cured every column
/// band this product shipped — but neither can create page, and both documented that residual while nothing in
/// the repertoire reached it. A comparative report reaches it immediately: Alt+N monthly over a financial year
/// is fourteen print columns of which thirteen must each hold a rupee figure, and no allocation of A4
/// landscape's 769.89pt fits that. What came out of the renderer's own "not even the floors fit, nothing can be
/// printed whole" branch was a page of ellipses: every label drawn as <c>"..."</c> and every figure as
/// <c>"37,000...."</c> with its In/Out suffix clipped, so an outflow printed identically to an inflow.</para>
///
/// <para><b>The invariants held here.</b> A document that fits comes back as THE SAME INSTANCE, which is an
/// exact ER-13 test rather than an approximation. A document that does not is dealt into groups that lose no
/// column, duplicate no column, keep the comparison columns in order, repeat the stub on every sheet, carry
/// every row's cells across unchanged, and say in the sub-title which slice they are. And the two
/// configurations that are deliberately NOT split stay unsplit.</para>
/// </summary>
public sealed class ReportPdfColumnSplitTests
{
    private const string WidestMoney = "99,99,99,999.00";

    /// <summary>A comparative Cash Flow's print shape: the Particulars stub at the weight the comparative
    /// projector emits, then <paramref name="columns"/> right-aligned money columns carrying a signed cell —
    /// which is what makes them unsplittably wide in the aggregate.</summary>
    private static PrintReport Comparative(int columns, int rows = 3)
    {
        var cols = new List<PrintColumn> { new("Particulars", 6, CellAlign.Left) };
        for (int i = 0; i < columns; i++)
            cols.Add(new PrintColumn($"Period {i + 1}", 1.6, CellAlign.Right));

        var body = new List<PrintRow>(rows);
        for (int r = 0; r < rows; r++)
        {
            var cells = new string[cols.Count];
            cells[0] = $"Brightline Industrial Supplies Private Ltd {r + 1}";
            for (int i = 0; i < columns; i++) cells[i + 1] = $"{WidestMoney} Out";
            body.Add(new PrintRow { Cells = cells, IsTotal = r == 0 });
        }

        return new PrintReport
        {
            Title = "Cash Flow",
            Subtitle = "Apex Transport  -  1-Apr-2020 to 31-Mar-2021",
            Columns = cols,
            Rows = body,
        };
    }

    private static PageConfig ShippedDefault() => new() { Size = PageSize.A4, Orientation = PageOrientation.Portrait };

    // ---------------------------------------------------------------- ER-13: nothing that fits moves

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_document_that_fits_is_returned_as_the_SAME_INSTANCE(int columns)
    {
        var report = Comparative(columns);
        var sheets = ReportPdf.SplitWideColumns(report, ShippedDefault());

        // Reference equality, not structural: the renderer must be able to prove its bytes are unmoved.
        Assert.Same(report, Assert.Single(sheets));
    }

    [Fact]
    public void A_single_column_document_is_never_split_however_wide_its_one_column()
    {
        var report = new PrintReport
        {
            Title = "One column",
            Columns = new[] { new PrintColumn("Amount", 1.5, CellAlign.Right) },
            Rows = new[] { new PrintRow { Cells = new[] { WidestMoney } } },
        };

        Assert.Same(report, Assert.Single(ReportPdf.SplitWideColumns(report, ShippedDefault())));
    }

    // ---------------------------------------------------------------- the split itself

    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(27)]
    [InlineData(54)]
    public void A_document_too_wide_for_any_sheet_is_dealt_into_groups_that_lose_nothing(int columns)
    {
        var report = Comparative(columns);
        var sheets = ReportPdf.SplitWideColumns(report, ShippedDefault());

        Assert.True(sheets.Count > 1, $"{columns} money columns came back as {sheets.Count} sheet(s)");

        // THE STUB REPEATS ON EVERY SHEET — a sheet of figures whose rows do not say what they are about is not
        // a report, and this is the whole reason the split is offered only to documents that have a stub.
        Assert.All(sheets, s => Assert.Same(report.Columns[0], s.Columns[0]));

        // NO COLUMN LOST, NONE DUPLICATED, ORDER PRESERVED. Reconstructed by concatenating each sheet's
        // non-stub columns and comparing against the original's — a dropped comparison column is a missing
        // figure, which is the defect this method exists to cure, not one it may introduce.
        var rebuilt = sheets.SelectMany(s => s.Columns.Skip(1)).ToList();
        Assert.Equal(columns, rebuilt.Count);
        for (int i = 0; i < columns; i++) Assert.Same(report.Columns[i + 1], rebuilt[i]);

        // EVERY ROW SURVIVES ON EVERY SHEET, cells and flags alike.
        foreach (var sheet in sheets)
        {
            Assert.Equal(report.Rows.Count, sheet.Rows.Count);
            for (int r = 0; r < report.Rows.Count; r++)
            {
                Assert.Equal(report.Rows[r].Cells[0], sheet.Rows[r].Cells[0]);
                Assert.Equal(report.Rows[r].IsTotal, sheet.Rows[r].IsTotal);
                Assert.Equal(report.Rows[r].IsHeader, sheet.Rows[r].IsHeader);
                Assert.Equal(sheet.Columns.Count, sheet.Rows[r].Cells.Count);
            }
        }

        // AND EVERY ROW'S CELLS ARE THE ORIGINAL'S, slice by slice, read back by walking the sheets in order.
        int taken = 0;
        foreach (var sheet in sheets)
        {
            int width = sheet.Columns.Count - 1;
            for (int r = 0; r < report.Rows.Count; r++)
                for (int i = 0; i < width; i++)
                    Assert.Equal(report.Rows[r].Cells[taken + i + 1], sheet.Rows[r].Cells[i + 1]);
            taken += width;
        }
        Assert.Equal(columns, taken);
    }

    /// <summary>
    /// 🔴 THE SHEETS ARE NEAR-EQUAL, AND THAT IS LOAD-BEARING RATHER THAN TIDY. Greedy packing fills each sheet
    /// to the brim and leaves the remainder on the last one — thirteen monthly columns come out 8 + 5. Because
    /// <c>FitOrientation</c> is asked per document, a group narrow enough to fit PORTRAIT is not turned while its
    /// wide siblings are, so the operator collating a three-sheet comparison is handed two landscape sheets and
    /// one portrait. Equal counts keep the job one way up, which is asserted directly below.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(27)]
    [InlineData(54)]
    public void The_groups_are_near_equal_and_the_whole_job_is_one_way_up(int columns)
    {
        var config = ShippedDefault();
        var sheets = ReportPdf.SplitWideColumns(Comparative(columns), config);

        var widths = sheets.Select(s => s.Columns.Count - 1).ToList();
        Assert.True(
            widths.Max() - widths.Min() <= 1,
            $"{columns} columns were dealt as {string.Join(" + ", widths)} — the groups must differ by at most "
            + "one column, or a short last sheet ends up a different shape from its siblings");

        var orientations = sheets.Select(s => ReportPdf.LayOut(s, config).Page.Orientation).Distinct().ToList();
        Assert.Single(orientations);
    }

    [Fact]
    public void Each_sheet_says_which_slice_of_the_columns_it_carries()
    {
        var report = Comparative(13);
        var sheets = ReportPdf.SplitWideColumns(report, ShippedDefault());

        // 1-based over the WHOLE document (14 columns including the stub), so an operator holding sheet 2 can
        // tell both that sheet 1 exists and which periods it held.
        int at = 1;
        foreach (var sheet in sheets)
        {
            int width = sheet.Columns.Count - 1;
            string expected = $"columns {at + 1}-{at + width} of {report.Columns.Count}";
            Assert.Contains(expected, sheet.Subtitle);
            Assert.StartsWith(report.Subtitle, sheet.Subtitle, StringComparison.Ordinal);
            at += width;
        }
    }

    // ---------------------------------------------------------------- what is deliberately not split

    [Fact]
    public void Pre_printed_stationery_is_never_split()
    {
        // Same reason FitOrientation never turns it: the sheet already carries a letterhead and a ruled column
        // grid the operator aligned the tray to, and dealing the columns onto two of those sheets puts every
        // figure under the wrong printed rule.
        var report = Comparative(27);
        var config = new PageConfig { Size = PageSize.A4, Paper = PaperKind.PrePrinted };

        Assert.Same(report, Assert.Single(ReportPdf.SplitWideColumns(report, config)));
    }

    [Fact]
    public void A_caller_that_pinned_the_page_is_never_split()
    {
        // PageConfigWithOrientationTests asserts the clip under AutoFitOrientation = false as "the honest
        // consequence of pinning the page". Adding sheets behind that caller's back would be this method
        // quietly re-answering a question another slice already answered.
        var report = Comparative(27);
        var config = new PageConfig { Size = PageSize.A4, AutoFitOrientation = false };

        Assert.Same(report, Assert.Single(ReportPdf.SplitWideColumns(report, config)));
    }

    // ---------------------------------------------------------------- and the bytes that come out

    /// <summary>
    /// 🔴 THE POINT OF ALL OF IT, ON THE EMITTED BYTES: at thirteen money columns every figure is drawn WHOLE
    /// and no cell is drawn as a bare ellipsis. Before the split this document's PDF carried neither.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(27)]
    public void Every_figure_of_a_split_document_is_drawn_whole(int columns)
    {
        var report = Comparative(columns);
        var pdf = ReportPdf.Render(ReportPdf.SplitWideColumns(report, ShippedDefault()), ShippedDefault());
        var drawn = DrawnStrings(pdf);

        Assert.DoesNotContain("...", drawn);

        // Every row of every sheet carries the same signed figure, so the whole document's figure cells are
        // accounted for by one count: rows x columns.
        int expected = report.Rows.Count * columns;
        Assert.Equal(expected, drawn.Count(s => s == $"{WidestMoney} Out"));
    }

    /// <summary>
    /// 🔴 AND IT GOES THROUGH <see cref="ReportPdf.Render"/> WITHOUT THE CALLER ASKING. The split is inside the
    /// renderer's own first pass, so the four production call sites — Export→PDF, Ctrl+P, the e-mailed report
    /// and the Alt+M share — are each fixed by construction rather than by remembering to call a new seam.
    /// </summary>
    [Fact]
    public void Render_splits_a_wide_document_without_the_caller_asking()
    {
        var report = Comparative(13);
        var drawn = DrawnStrings(ReportPdf.Render(report, ShippedDefault()));

        Assert.DoesNotContain("...", drawn);
        Assert.Equal(report.Rows.Count * 13, drawn.Count(s => s == $"{WidestMoney} Out"));
    }

    /// <summary>The strings a PDF actually DREW, in order — each <c>Tj</c> operand, already through
    /// <c>PdfWriter.FitToWidth</c>. The parse walks forward from an unescaped <c>'('</c> and consumes <c>\x</c>
    /// pairs, because <c>EscapeString</c> emits <c>\(</c> and <c>\)</c> inside strings.</summary>
    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            if (i > 0 && text[i - 1] == '\\') continue;

            var sb = new StringBuilder();
            int j = i + 1;
            bool closed = false;
            for (; j < text.Length; j++)
            {
                char ch = text[j];
                if (ch == '\\')
                {
                    if (j + 1 < text.Length) sb.Append(text[++j]);
                    continue;
                }
                if (ch == ')') { closed = true; break; }
                sb.Append(ch);
            }

            if (!closed || j + 3 >= text.Length || text.Substring(j + 1, 3) != " Tj") continue;
            drawn.Add(sb.ToString());
            i = j + 3;
        }
        return drawn;
    }
}
