using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-31 / census row 12.4 — the F8 <b>Print Format</b> selector, the F9 <b>paper</b> toggle, the F5
/// <b>number of copies</b>, and the F10 <b>starting page number</b> + <b>page range</b>.
///
/// <para><b>What is sourced and what is ours.</b> The row's corrected target names the three F8 values
/// (<i>Dot Matrix Format</i> · <i>Neat Mode</i> · <i>Quick/Draft Format</i>), the F9 axis
/// (<i>Plain Paper</i> ↔ <i>Pre-Printed Paper</i>) and the F5/F10 knobs — that vocabulary is the census's
/// quoted vendor list. <b>Every metric and every rendering consequence below is OURS</b> (ruling 9): no
/// admissible source states what row height a dot-matrix print uses, which rules a draft print drops, or which
/// bands pre-printed stationery suppresses. They are a documented divergence and can never join the compared
/// set.</para>
///
/// <para><b>Byte stability (ER-13).</b> The defaults — Neat, Plain, one copy, the whole range, starting at page
/// one — must render EXACTLY the bytes the shipped renderer produced, or every golden in this suite silently
/// moves. That is asserted first.</para>
/// </summary>
public sealed class PrintFormatCopiesRangeTests
{
    private static PrintReport SampleReport() => new()
    {
        Title = "Trial Balance",
        Subtitle = "Bright Traders",
        Columns = new[]
        {
            new PrintColumn("Particulars", 3.0, CellAlign.Left),
            new PrintColumn("Debit", 1.5, CellAlign.Right),
            new PrintColumn("Credit", 1.5, CellAlign.Right),
        },
        Rows = new[]
        {
            new PrintRow("Cash-in-Hand", "1,05,000.00", ""),
            PrintRow.Total("Grand Total", "3,55,000.00", "3,55,000.00"),
        },
    };

    /// <summary>A report long enough to paginate. 200 rows over 53 body rows per A4 page = 4 pages.</summary>
    private static PrintReport LongReport(int rows = 200)
    {
        var list = new List<PrintRow>();
        for (int i = 0; i < rows; i++) list.Add(new PrintRow($"Ledger {i:D3}", "1,000.00", ""));
        return new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "Big Co",
            Columns = SampleReport().Columns,
            Rows = list,
        };
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // Counts "/Type /Page" occurrences that are NOT "/Type /Pages".
    private static int PageObjectCount(string s)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf("/Type /Page", idx, System.StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            if (after >= s.Length || s[after] != 's') count++;
            idx = after;
        }
        return count;
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        { count++; idx += needle.Length; }
        return count;
    }

    // ------------------------------------------------------------------ ER-13: the defaults do not move

    [Fact]
    public void Default_config_is_neat_plain_one_copy_whole_range_starting_at_one()
    {
        var cfg = new PageConfig();

        Assert.Equal(PrintFormat.Neat, cfg.Format);
        Assert.Equal(PaperKind.Plain, cfg.Paper);
        Assert.Equal(1, cfg.Copies);
        Assert.Equal(1, cfg.FirstPage);
        Assert.Equal(0, cfg.LastPage);          // 0 = to the end
        Assert.Equal(1, cfg.StartPageNumber);
    }

    [Fact]
    public void Neat_plain_defaults_leave_every_layout_metric_exactly_as_it_shipped()
    {
        var cfg = new PageConfig();

        Assert.Equal(cfg.RowHeight, cfg.FormattedRowHeight);
        Assert.Equal(cfg.BodyFontSize, cfg.FormattedBodyFontSize);
        Assert.Equal(cfg.HeaderFontSize, cfg.FormattedHeaderFontSize);
        Assert.Equal(cfg.TitleFontSize, cfg.FormattedTitleFontSize);
        Assert.True(cfg.DrawsRules);
        Assert.True(cfg.DrawsTitleBand);
        Assert.True(cfg.DrawsColumnHeaderBand);
    }

    // ------------------------------------------------------------------ F8: the three print formats

    [Fact]
    public void Dot_matrix_condenses_the_rows_and_keeps_the_rules()
    {
        var cfg = new PageConfig { Format = PrintFormat.DotMatrix };

        // OURS: 13 pt -> 11 pt rows and 9 pt -> 8 pt body, the continuous-stationery density; rules survive
        // because an impact printer draws a rule as happily as a glyph.
        Assert.Equal(11.0, cfg.FormattedRowHeight);
        Assert.Equal(8.0, cfg.FormattedBodyFontSize);
        Assert.Equal(8.0, cfg.FormattedHeaderFontSize);
        Assert.Equal(12.0, cfg.FormattedTitleFontSize);
        Assert.True(cfg.DrawsRules);
    }

    [Fact]
    public void Quick_draft_drops_the_rules_and_keeps_the_shipped_metrics()
    {
        var cfg = new PageConfig { Format = PrintFormat.QuickDraft };

        // OURS: a draft is the fastest, plainest pass — same readable metrics, no ruling ink.
        Assert.Equal(13.0, cfg.FormattedRowHeight);
        Assert.Equal(9.0, cfg.FormattedBodyFontSize);
        Assert.False(cfg.DrawsRules);
    }

    [Fact]
    public void Dot_matrix_fits_more_rows_on_a_page_than_neat_does()
    {
        // Derived by hand on A4 portrait, 36 pt margins, 8 pt footer:
        //   NEAT   banner = (16+6)+(10+8)+(9+6) = 55; usable 805.890-55 = 750.890 down to 50 at 13 pt => 53 rows
        //   DOT    banner = (12+6)+(10+8)+(8+6) = 50; usable 805.890-50 = 755.890 down to 50 at 11 pt => 64 rows
        // 220 rows therefore paginate to ceil(220/53) = 5 neat sheets and ceil(220/64) = 4 dot-matrix sheets.
        var report = LongReport(220);

        int neat = PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig())));
        int dot = PageObjectCount(AsLatin1(ReportPdf.Render(report,
            new PageConfig { Format = PrintFormat.DotMatrix })));

        Assert.Equal(5, neat);
        Assert.Equal(4, dot);
    }

    [Fact]
    public void Quick_draft_emits_no_ruling_operators()
    {
        // ReportPdf rules the column-header band on every page and every total row. A draft draws none, so the
        // stroke operator count drops to zero for a report that has both.
        string neat = AsLatin1(ReportPdf.Render(SampleReport(), new PageConfig()));
        string draft = AsLatin1(ReportPdf.Render(SampleReport(),
            new PageConfig { Format = PrintFormat.QuickDraft }));

        // PdfWriter.Line emits "<w> w / <x> <y> m / <x> <y> l / S", each on its own line — so " l\nS\n" is the
        // stroke, and counting it counts rules exactly. The sample has a header-band rule plus one total rule.
        Assert.Equal(2, Occurrences(neat, " l\nS\n"));
        Assert.Equal(0, Occurrences(draft, " l\nS\n"));
    }

    // ------------------------------------------------------------------ F9: plain vs pre-printed paper

    [Fact]
    public void Pre_printed_paper_suppresses_the_bands_the_stationery_already_carries()
    {
        var cfg = new PageConfig { Paper = PaperKind.PrePrinted };

        Assert.False(cfg.DrawsTitleBand);
        Assert.False(cfg.DrawsColumnHeaderBand);
    }

    [Fact]
    public void Pre_printed_paper_prints_the_figures_but_not_the_title_or_the_captions()
    {
        string s = AsLatin1(ReportPdf.Render(SampleReport(), new PageConfig { Paper = PaperKind.PrePrinted }));

        // The data is the whole point of overprinting stationery — the figure must be there, to the paisa.
        Assert.Contains("1,05,000.00", s);
        Assert.Contains("Cash-in-Hand", s);
        // The heading and the column captions are already ON the stationery; printing them again double-strikes.
        Assert.DoesNotContain("(Trial Balance) Tj", s);
        Assert.DoesNotContain("(Particulars) Tj", s);
    }

    [Fact]
    public void Plain_paper_still_prints_the_title_and_the_captions()
    {
        string s = AsLatin1(ReportPdf.Render(SampleReport(), new PageConfig()));

        Assert.Contains("(Trial Balance) Tj", s);
        Assert.Contains("(Particulars) Tj", s);
    }

    // ------------------------------------------------------------------ F5: number of copies

    [Fact]
    public void Two_copies_emit_the_document_twice_in_one_file()
    {
        var report = SampleReport();
        int one = PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig())));
        int two = PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig { Copies = 2 })));
        int three = PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig { Copies = 3 })));

        Assert.Equal(1, one);
        Assert.Equal(2, two);
        Assert.Equal(3, three);
    }

    [Fact]
    public void Copies_repeat_a_multi_page_document_whole_not_page_by_page()
    {
        // 200 rows paginate to 4 pages; two copies is 8 page objects, and the SECOND copy must start at the
        // document's first page — a collated set, not a page-by-page duplicate.
        var report = LongReport(200);
        string one = AsLatin1(ReportPdf.Render(report, new PageConfig()));
        string two = AsLatin1(ReportPdf.Render(report, new PageConfig { Copies = 2 }));

        int pages = PageObjectCount(one);
        Assert.Equal(4, pages);
        Assert.Equal(8, PageObjectCount(two));
        // "Ledger 000" is on page 1 only; it must appear once per copy.
        Assert.Equal(1, Occurrences(one, "(Ledger 000) Tj"));
        Assert.Equal(2, Occurrences(two, "(Ledger 000) Tj"));
    }

    [Fact]
    public void A_copy_count_below_one_is_treated_as_one()
    {
        var report = SampleReport();
        Assert.Equal(1, PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig { Copies = 0 }))));
        Assert.Equal(1, PageObjectCount(AsLatin1(ReportPdf.Render(report, new PageConfig { Copies = -4 }))));
    }

    // ------------------------------------------------------------------ F10: page range + starting number

    [Fact]
    public void A_page_range_prints_only_the_pages_asked_for()
    {
        var report = LongReport(200);                              // 4 pages

        string all = AsLatin1(ReportPdf.Render(report, new PageConfig()));
        Assert.Equal(4, PageObjectCount(all));

        string two_to_three = AsLatin1(ReportPdf.Render(report,
            new PageConfig { FirstPage = 2, LastPage = 3 }));
        Assert.Equal(2, PageObjectCount(two_to_three));

        // Page 1's first row must NOT be in the file; page 2's first row must be.
        Assert.DoesNotContain("(Ledger 000) Tj", two_to_three);
        Assert.Contains("(Ledger 053) Tj", two_to_three);          // 53 body rows fit an A4 neat page
    }

    [Fact]
    public void A_page_range_keeps_the_documents_own_numbering_in_the_footer()
    {
        var report = LongReport(200);
        string s = AsLatin1(ReportPdf.Render(report, new PageConfig { FirstPage = 3, LastPage = 3 }));

        // Printing page 3 alone still says "Page 3 of 4" — the operator is holding sheet 3 of a 4-sheet report,
        // and renumbering it "Page 1 of 1" would misstate the document.
        Assert.Equal(1, PageObjectCount(s));
        Assert.Contains("Page 3 of 4", s);
        Assert.DoesNotContain("Page 1 of 1", s);
    }

    [Fact]
    public void Last_page_zero_means_to_the_end()
    {
        var report = LongReport(200);
        string s = AsLatin1(ReportPdf.Render(report, new PageConfig { FirstPage = 3, LastPage = 0 }));

        Assert.Equal(2, PageObjectCount(s));   // pages 3 and 4
    }

    [Fact]
    public void A_start_page_number_renumbers_the_footer_from_that_page()
    {
        var report = LongReport(200);          // 4 pages
        string s = AsLatin1(ReportPdf.Render(report, new PageConfig { StartPageNumber = 7 }));

        // Sheets 1..4 become 7..10, so the total shown is 10 — the continuation-numbering the F10 knob exists for.
        Assert.Contains("Page 7 of 10", s);
        Assert.Contains("Page 10 of 10", s);
        Assert.DoesNotContain("Page 1 of 4", s);
    }

    [Fact]
    public void An_out_of_bounds_range_prints_nothing_rather_than_the_whole_report()
    {
        var report = LongReport(200);          // 4 pages
        string s = AsLatin1(ReportPdf.Render(report, new PageConfig { FirstPage = 9, LastPage = 12 }));

        // A single blank page is emitted (a PDF must have one) and NONE of the report's rows are on it. Silently
        // falling back to "print everything" is the failure mode this asserts against.
        Assert.Equal(1, PageObjectCount(s));
        Assert.DoesNotContain("(Ledger 000) Tj", s);
        Assert.DoesNotContain("(Ledger 199) Tj", s);
    }

    // ------------------------------------------------------------------ combinations + determinism

    [Fact]
    public void Range_and_copies_compose_the_range_first_then_the_copies()
    {
        var report = LongReport(200);
        string s = AsLatin1(ReportPdf.Render(report,
            new PageConfig { FirstPage = 2, LastPage = 3, Copies = 3 }));

        Assert.Equal(6, PageObjectCount(s));   // 2 pages x 3 copies
    }

    [Fact]
    public void Every_new_knob_renders_byte_identically_on_a_second_run()
    {
        var cfg = new PageConfig
        {
            Format = PrintFormat.DotMatrix,
            Paper = PaperKind.PrePrinted,
            Copies = 2,
            FirstPage = 1,
            LastPage = 2,
            StartPageNumber = 5,
        };
        var report = LongReport(200);

        Assert.Equal(ReportPdf.Render(report, cfg), ReportPdf.Render(report, cfg));
    }
}
