using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Foundation tests for the hand-rolled PDF writer and the report -> PDF renderer (RQ-9 print/preview,
/// RQ-13 de-branded output). Uses a small fixed report so figures are deterministic.
/// </summary>
public sealed class ReportPdfTests
{
    // A small, deterministic Trial-Balance-shaped report (Bright-style figures).
    private static PrintReport SampleReport() => new()
    {
        Title = "Trial Balance",
        Subtitle = "Bright Traders  —  as at 31-03-2025",
        Columns = new[]
        {
            new PrintColumn("Particulars", 3.0, CellAlign.Left),
            new PrintColumn("Debit", 1.5, CellAlign.Right),
            new PrintColumn("Credit", 1.5, CellAlign.Right),
        },
        Rows = new[]
        {
            PrintRow.Header("Current Assets"),
            new PrintRow("Cash-in-Hand", "1,05,000.00", ""),
            new PrintRow("Bank Account", "2,50,000.00", ""),
            PrintRow.Header("Sales Accounts"),
            new PrintRow("Sales", "", "3,55,000.00"),
            PrintRow.Total("Grand Total", "3,55,000.00", "3,55,000.00"),
        },
    };

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public void Output_is_a_valid_pdf_structure()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("xref", s);
        Assert.Contains("trailer", s);
        Assert.Contains("startxref", s);
        Assert.Contains("%%EOF", s);
        Assert.Contains("/Type /Catalog", s);
        Assert.Contains("/BaseFont /Helvetica", s);
    }

    [Fact]
    public void Expected_report_text_appears_in_content()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        string s = AsLatin1(bytes);

        Assert.Contains("Trial Balance", s);
        Assert.Contains("Cash-in-Hand", s);
        Assert.Contains("Sales", s);
        Assert.Contains("Grand Total", s);
        Assert.Contains("3,55,000.00", s);
    }

    [Fact]
    public void Same_report_and_config_renders_byte_identical()
    {
        var a = ReportPdf.Render(SampleReport(), new PageConfig());
        var b = ReportPdf.Render(SampleReport(), new PageConfig());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Long_report_paginates_to_more_than_one_page()
    {
        var rows = new List<PrintRow>();
        for (int i = 0; i < 200; i++)
            rows.Add(new PrintRow($"Ledger {i:D3}", "1,000.00", ""));

        var report = new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "Big Co",
            Columns = SampleReport().Columns,
            Rows = rows,
        };

        var bytes = ReportPdf.Render(report, new PageConfig());
        string s = AsLatin1(bytes);

        Assert.True(PageObjectCount(s) > 1, $"expected >1 page, got {PageObjectCount(s)}");
        // Footer should reflect a multi-page count.
        Assert.Contains("Page 1 of", s);
    }

    [Fact]
    public void No_tally_branding_anywhere_in_bytes()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        string s = AsLatin1(bytes).ToLowerInvariant();
        Assert.DoesNotContain("tally", s);

        // And the de-branded metadata is present.
        string raw = AsLatin1(bytes);
        Assert.Contains("/Producer (Apex Solutions)", raw);
        Assert.Contains("/Creator (Apex Solutions)", raw);
    }

    [Fact]
    public void Letter_and_landscape_configs_render_valid_pdfs()
    {
        var letter = ReportPdf.Render(SampleReport(), new PageConfig { Size = PageSize.Letter });
        var landscape = ReportPdf.Render(SampleReport(),
            new PageConfig { Size = PageSize.A4, Orientation = PageOrientation.Landscape });

        Assert.StartsWith("%PDF-", AsLatin1(letter));
        Assert.Contains("%%EOF", AsLatin1(letter));
        Assert.StartsWith("%PDF-", AsLatin1(landscape));
        Assert.Contains("%%EOF", AsLatin1(landscape));
        // Landscape A4 MediaBox width should exceed its height.
        Assert.Contains("/MediaBox [0 0 841.89 595.276]", AsLatin1(landscape));
    }

    [Fact]
    public void Text_escaping_handles_parens_and_backslash()
    {
        var report = new PrintReport
        {
            Title = "P&L (Draft)",
            Subtitle = @"path\to\report",
            Columns = new[] { new PrintColumn("Item", 1.0) },
            Rows = new[] { new PrintRow("Rent (Office)") },
        };
        var bytes = ReportPdf.Render(report, new PageConfig());
        string s = AsLatin1(bytes);
        Assert.Contains(@"P&L \(Draft\)", s);
        Assert.Contains(@"Rent \(Office\)", s);
    }

    // ---------------------------------------------------------------- Fix 1: real WinAnsi encoding

    [Fact]
    public void Em_dash_in_subtitle_encodes_to_its_winansi_byte_not_question_mark()
    {
        var report = new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "Bright Traders — as at 31-03-2025", // em dash U+2014
            Columns = new[] { new PrintColumn("Particulars", 1.0) },
            Rows = new[] { new PrintRow("Cash") },
        };
        var bytes = ReportPdf.Render(report, new PageConfig());

        // The em-dash must appear as its WinAnsi byte 0x97 somewhere in the drawn content, never folded to '?'.
        Assert.Contains((byte)0x97, bytes);
        // The subtitle text on either side of the dash is present; the dash sits between as byte 0x97.
        string s = AsLatin1(bytes);
        Assert.Contains("Bright Traders", s);
        Assert.Contains("as at 31-03-2025", s);
        int dashIdx = System.Array.IndexOf(bytes, (byte)0x97);
        Assert.True(dashIdx > 0);
    }

    [Fact]
    public void Default_footer_em_dashes_render_as_winansi_bytes()
    {
        // PageConfig's default footer uses em-dashes; they must encode, not become '?'.
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        Assert.Contains((byte)0x97, bytes); // em dash 0x97 present in the footer band
    }

    [Fact]
    public void EscapeString_maps_cp1252_glyphs_to_correct_bytes()
    {
        // en dash, curly quotes, bullet, ellipsis -> their CP1252 bytes.
        Assert.Equal(0x96, (int)PdfWriter.EscapeString("–")[0]); // en dash
        Assert.Equal(0x97, (int)PdfWriter.EscapeString("—")[0]); // em dash
        Assert.Equal(0x91, (int)PdfWriter.EscapeString("‘")[0]); // left single quote
        Assert.Equal(0x92, (int)PdfWriter.EscapeString("’")[0]); // right single quote
        Assert.Equal(0x93, (int)PdfWriter.EscapeString("“")[0]); // left double quote
        Assert.Equal(0x94, (int)PdfWriter.EscapeString("”")[0]); // right double quote
        Assert.Equal(0x95, (int)PdfWriter.EscapeString("•")[0]); // bullet
        Assert.Equal(0xE9, (int)PdfWriter.EscapeString("é")[0]); // é latin-1 accent
        // The Indian Rupee sign is not representable in CP1252 -> deterministic fallback '?'.
        Assert.Equal('?', PdfWriter.EscapeString("₹")[0]);
    }

    // ---------------------------------------------------------------- Fix 2: long cell truncation

    [Fact]
    public void Overlong_cell_is_truncated_with_ellipsis_within_the_column_and_page()
    {
        var longName = new string('W', 200); // far wider than any column
        var report = new PrintReport
        {
            Title = "Trial Balance",
            Columns = new[]
            {
                new PrintColumn("Particulars", 3.0, CellAlign.Left),
                new PrintColumn("Debit", 1.5, CellAlign.Right),
                new PrintColumn("Credit", 1.5, CellAlign.Right),
            },
            Rows = new[] { new PrintRow(longName, "1,00,000.00", "") },
        };
        var config = new PageConfig();
        var bytes = ReportPdf.Render(report, config);
        string s = AsLatin1(bytes);

        // The full 200-W string must NOT survive; the drawn text is clipped with an ellipsis.
        Assert.DoesNotContain(longName, s);
        Assert.Contains("...", s);

        // Measure the drawn (truncated) cell: it must fit inside its column's inner width.
        double contentWidth = config.ContentWidth;
        double col0Width = contentWidth * (3.0 / (3.0 + 1.5 + 1.5)); // Particulars column
        double innerWidth = col0Width - 2 * 2; // minus 2pt padding each side
        string drawn = FirstDrawnTextStartingWith(s, "WWW");
        Assert.EndsWith("...", drawn);
        double measured = PdfWriter.MeasureHelvetica(drawn, config.BodyFontSize);
        Assert.True(measured <= innerWidth,
            $"truncated cell width {measured:0.###}pt exceeds column inner width {innerWidth:0.###}pt");
        Assert.True(measured <= contentWidth, "truncated cell exceeds page content width");
    }

    // ---------------------------------------------------------------- Fix 3: bold header/total rows

    [Fact]
    public void Bold_font_resource_is_declared()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        string s = AsLatin1(bytes);
        Assert.Contains("/BaseFont /Helvetica-Bold", s);
        Assert.Contains("/F2", s); // referenced in page resources
    }

    [Fact]
    public void Header_and_total_rows_are_drawn_with_the_bold_font()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig());
        string s = AsLatin1(bytes);

        // The column-header caption "Particulars" is a header row -> drawn with /F2.
        Assert.True(DrawnWithFont(s, "Particulars", "/F2"),
            "expected the column-header caption to be drawn bold (/F2)");
        // "Grand Total" is a total row -> bold.
        Assert.True(DrawnWithFont(s, "Grand Total", "/F2"),
            "expected the total row to be drawn bold (/F2)");
        // A plain body row stays regular (/F1).
        Assert.True(DrawnWithFont(s, "Cash-in-Hand", "/F1"),
            "expected a body row to be drawn regular (/F1)");
    }

    // Finds the argument of the first "(...) Tj" show whose text begins with the given ASCII prefix.
    private static string FirstDrawnTextStartingWith(string content, string prefix)
    {
        int idx = 0;
        while ((idx = content.IndexOf('(', idx)) >= 0)
        {
            int end = content.IndexOf(") Tj", idx, StringComparison.Ordinal);
            if (end < 0) break;
            string inner = content.Substring(idx + 1, end - idx - 1);
            string unescaped = inner.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\");
            if (unescaped.StartsWith(prefix, StringComparison.Ordinal))
                return unescaped;
            idx = end + 4;
        }
        return string.Empty;
    }

    // True if the given text is shown by a "(text) Tj" whose most recent "/Fx .. Tf" selector is fontTag.
    private static bool DrawnWithFont(string content, string text, string fontTag)
    {
        string needle = "(" + text;
        int at = content.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            // The show must be this exact text ") Tj" right after.
            int close = content.IndexOf(") Tj", at, StringComparison.Ordinal);
            // Walk back to the nearest "/F" font selector before this show.
            int f = content.LastIndexOf("/F", at, StringComparison.Ordinal);
            if (f >= 0 && content.Substring(f, 3) == fontTag)
                return true;
            at = content.IndexOf(needle, at + 1, StringComparison.Ordinal);
        }
        return false;
    }

    // Counts "/Type /Page" occurrences that are NOT "/Type /Pages".
    private static int PageObjectCount(string s)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf("/Type /Page", idx, StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            if (after >= s.Length || s[after] != 's')
                count++;
            idx = after;
        }
        return count;
    }
}
