using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-32 / census row 12.6 — <b>multi-account / multi-voucher (range) printing</b>: one print job built from a
/// SET of documents rather than exactly one. The census's evidence for 12.6 is that <i>"the opener builds
/// exactly one preview from exactly one report or one drilled voucher"</i>; this is the job-set iterator that
/// makes a set possible.
///
/// <para><b>Ours (ruling 9):</b> that each document starts on a fresh sheet and that page numbering runs across
/// the whole job ("Page 3 of 12", not "Page 1 of 2" three times) are our choices; no admissible source states
/// them. They are what makes a printed stack of ledger accounts usable as one document.</para>
/// </summary>
public sealed class MultiDocumentPrintTests
{
    private static PrintReport Doc(string title, params string[] rowLabels)
    {
        var rows = new List<PrintRow>();
        foreach (var label in rowLabels) rows.Add(new PrintRow(label, "1,000.00", ""));
        return new PrintReport
        {
            Title = title,
            Subtitle = "Bright Traders",
            Columns = new[]
            {
                new PrintColumn("Particulars", 3.0, CellAlign.Left),
                new PrintColumn("Debit", 1.5, CellAlign.Right),
                new PrintColumn("Credit", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    private static PrintReport LongDoc(string title, int rows)
    {
        var labels = new string[rows];
        for (int i = 0; i < rows; i++) labels[i] = $"{title} row {i:D3}";
        return Doc(title, labels);
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

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

    [Fact]
    public void Three_one_page_accounts_render_as_a_three_page_job()
    {
        var docs = new[] { Doc("Acme Ltd", "Opening"), Doc("Bravo Co", "Opening"), Doc("Delta Inc", "Opening") };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        Assert.Equal(3, PageObjectCount(s));
        Assert.Contains("(Acme Ltd) Tj", s);
        Assert.Contains("(Bravo Co) Tj", s);
        Assert.Contains("(Delta Inc) Tj", s);
    }

    [Fact]
    public void Each_account_starts_on_its_own_sheet_even_when_short()
    {
        // Two one-row accounts would fit on one A4 sheet if the job merely concatenated rows. They must not:
        // the operator hands one sheet per account to one party.
        var docs = new[] { Doc("Acme Ltd", "Opening"), Doc("Bravo Co", "Opening") };

        Assert.Equal(2, PageObjectCount(AsLatin1(ReportPdf.Render(docs, new PageConfig()))));
    }

    [Fact]
    public void Page_numbering_runs_across_the_whole_job_not_per_document()
    {
        // 60 rows overflow the 53-row A4 neat page, so this account is 2 sheets; the one-row account after it is
        // sheet 3 of 3 — not "Page 1 of 1" restarting.
        var docs = new[] { LongDoc("Acme Ltd", 60), Doc("Bravo Co", "Opening") };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        Assert.Equal(3, PageObjectCount(s));
        Assert.Contains("Page 1 of 3", s);
        Assert.Contains("Page 2 of 3", s);
        Assert.Contains("Page 3 of 3", s);
    }

    [Fact]
    public void An_empty_job_still_produces_a_one_page_pdf()
    {
        string s = AsLatin1(ReportPdf.Render(System.Array.Empty<PrintReport>(), new PageConfig()));

        Assert.StartsWith("%PDF-", s);
        Assert.Equal(1, PageObjectCount(s));
    }

    [Fact]
    public void A_single_document_job_is_byte_identical_to_printing_that_document_alone()
    {
        // The job-set path must not quietly re-lay-out a lone document, or a multi-account print of one account
        // would differ from that account printed on its own.
        var doc = Doc("Acme Ltd", "Opening", "Sales");

        Assert.Equal(
            ReportPdf.Render(doc, new PageConfig()),
            ReportPdf.Render(new[] { doc }, new PageConfig()));
    }

    [Fact]
    public void A_page_range_selects_sheets_of_the_whole_job()
    {
        var docs = new[] { Doc("Acme Ltd", "Opening"), Doc("Bravo Co", "Opening"), Doc("Delta Inc", "Opening") };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig { FirstPage = 2, LastPage = 2 }));

        Assert.Equal(1, PageObjectCount(s));
        Assert.DoesNotContain("(Acme Ltd) Tj", s);
        Assert.Contains("(Bravo Co) Tj", s);
        Assert.DoesNotContain("(Delta Inc) Tj", s);
    }

    [Fact]
    public void Copies_repeat_the_whole_job_collated()
    {
        var docs = new[] { Doc("Acme Ltd", "Opening"), Doc("Bravo Co", "Opening") };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig { Copies = 2 }));

        Assert.Equal(4, PageObjectCount(s));
    }

    [Fact]
    public void The_job_honours_the_print_format()
    {
        var docs = new[] { LongDoc("Acme Ltd", 60), LongDoc("Bravo Co", 60) };

        // 60 rows are 2 neat sheets each (53/page) but 1 dot-matrix sheet each (64/page).
        Assert.Equal(4, PageObjectCount(AsLatin1(ReportPdf.Render(docs, new PageConfig()))));
        Assert.Equal(2, PageObjectCount(AsLatin1(ReportPdf.Render(docs,
            new PageConfig { Format = PrintFormat.DotMatrix }))));
    }

    [Fact]
    public void The_job_is_deterministic()
    {
        var docs = new[] { Doc("Acme Ltd", "Opening"), Doc("Bravo Co", "Opening") };

        Assert.Equal(ReportPdf.Render(docs, new PageConfig()), ReportPdf.Render(docs, new PageConfig()));
    }

    /// <summary>
    /// 🔴 <b>A PRE-EXISTING DEFECT this slice found and fixed, not a new requirement.</b> <c>ReportPdf</c>
    /// declares its output de-branded (ER-11) and its own suite asserted it — but only over a report whose title
    /// was already clean. <c>SafeTitle</c> ran no scrub at all, and the drawn title/subtitle ran none either, so
    /// a document whose heading carried the forbidden brand leaked it into both the <c>/Title</c> metadata and
    /// the printed page. It went unseen because every report title in the app is app-authored.
    ///
    /// <para>W2-32 is what makes it reachable: a multi-account job titles each sheet with a LEDGER NAME, which is
    /// typed by the user. A party ledger named after the brand would have printed it on the paper.</para>
    ///
    /// <para>Both paths are asserted — the job set AND the single-document overload, since the hole was in the
    /// shared code and fixing only the new path would leave the shipped one open.</para>
    /// </summary>
    [Fact]
    public void The_job_never_emits_the_forbidden_brand()
    {
        var docs = new[] { Doc("Tally Ltd", "Tally Opening") };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig())).ToLowerInvariant();

        Assert.DoesNotContain("tally", s);
    }

    [Fact]
    public void A_single_document_never_emits_the_forbidden_brand_in_its_title_or_subtitle()
    {
        var doc = new PrintReport
        {
            Title = "Tally Ledger Account",
            Subtitle = "Tally Traders Pvt Ltd",
            Columns = new[] { new PrintColumn("Particulars", 3.0, CellAlign.Left) },
            Rows = new[] { new PrintRow("Opening") },
        };

        string s = AsLatin1(ReportPdf.Render(doc, new PageConfig())).ToLowerInvariant();

        Assert.DoesNotContain("tally", s);
        // The rest of the heading must SURVIVE the scrub — de-branding is not blanking.
        Assert.Contains("ledger account", s);
        Assert.Contains("traders pvt ltd", s);
    }

    [Fact]
    public void A_clean_title_and_subtitle_are_left_byte_identical_by_the_scrub()
    {
        // The scrub collapses runs of whitespace, which would move every shipped golden if it ran unconditionally.
        // A subtitle with a deliberate double space is the case that proves it does not.
        var doc = new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "Bright Traders  -  as at 31-03-2025",
            Columns = new[] { new PrintColumn("Particulars", 3.0, CellAlign.Left) },
            Rows = new[] { new PrintRow("Opening") },
        };

        string s = AsLatin1(ReportPdf.Render(doc, new PageConfig()));

        Assert.Contains("(Bright Traders  -  as at 31-03-2025) Tj", s);
    }
}
