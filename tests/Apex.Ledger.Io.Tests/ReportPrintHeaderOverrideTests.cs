using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census 12.3 — the F12 <b>title and sub-title override on a REPORT print</b>. Until this slice
/// <see cref="PrintConfig"/> reached <see cref="VoucherPdf"/> and <see cref="InvoicePdf"/> only, so a report
/// print had no configuration beyond the page-size and orientation toggles.
///
/// <para><b>Vendor grounding (R7 source 1), both pages opened BY CONTENT on 2026-10-13.</b>
/// <c>help.tallysolutions.com/configure-printing-for-invoices-reports/</c> opens
/// <i>"Before printing, exporting, or sharing documents, like your invoices, <b>financial reports</b>, payment
/// reminder letters, dashboard tiles, and multi-account reports, you can configure them, as per your business
/// needs."</i> and its <i>Titles/Subtitles</i> section reads <i>"In the sub-form, press <b>F7</b> (Title). In
/// the Header Information sub-screen, provide the required <b>Title</b> and <b>Subtitle</b> for the document and
/// save."</i> <c>help.tallysolutions.com/working-with-reports/</c> attests the same from the other side:
/// <i>"Activities like changing printer, setting up format or paper type for printing, or updating the
/// title/sub-title of the report will not require you to open the F12 Configuration list anymore"</i> — which
/// states in passing that a report's title and sub-title ARE F12 print configuration.</para>
///
/// <para>🔴 Every assertion here is on the <b>emitted bytes</b>. A knob that sets a property is not a knob; the
/// discipline this repository already holds for the W2-31 knobs is that a configuration must MOVE A DOCUMENT'S
/// BYTES, and the same bar applies to this one.</para>
/// </summary>
public sealed class ReportPrintHeaderOverrideTests
{
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
            new PrintRow("Cash-in-Hand", "1,05,000.00", ""),
            PrintRow.Total("Grand Total", "1,05,000.00", "1,05,000.00"),
        },
    };

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ------------------------------------------------------------------ ER-13: the unconfigured path is unmoved

    [Fact]
    public void A_null_or_empty_print_config_renders_byte_identically_to_the_old_overload()
    {
        var page = new PageConfig();
        var baseline = ReportPdf.Render(SampleReport(), page);

        Assert.Equal(baseline, ReportPdf.Render(SampleReport(), page, null));
        Assert.Equal(baseline, ReportPdf.Render(SampleReport(), page, new PrintConfig()));
        // Whitespace is not a title. An operator who tabs through the box leaves the output alone.
        Assert.Equal(baseline, ReportPdf.Render(SampleReport(), page,
            new PrintConfig { TitleOverride = "   ", SubtitleOverride = "  " }));
        // And the narration / copy-marking knobs are not read by the report renderer at all, so setting them
        // cannot move a report's bytes — which is why they stay off this slice's F12 group.
        Assert.Equal(baseline, ReportPdf.Render(SampleReport(), page,
            new PrintConfig { ShowNarration = false, CopyMarking = CopyMarking.Triplicate }));
    }

    // ------------------------------------------------------------------ the override reaches the paper

    [Fact]
    public void A_title_override_replaces_the_heading_on_the_emitted_bytes()
    {
        var page = new PageConfig();
        var baseline = ReportPdf.Render(SampleReport(), page);
        var bytes = ReportPdf.Render(SampleReport(), page, new PrintConfig { TitleOverride = "List of Balances" });

        Assert.NotEqual(baseline, bytes);                       // the knob MOVED the bytes

        var s = AsLatin1(bytes);
        Assert.Contains("List of Balances", s);
        // 🔴 REPLACES, not adds. A print carrying both headings would be worse than either.
        Assert.DoesNotContain("Trial Balance", s);
        // The figures and the sub-title are untouched — this is a heading knob, not a re-projection.
        Assert.Contains("1,05,000.00", s);
        Assert.Contains("Bright Traders", s);
    }

    [Fact]
    public void A_subtitle_override_replaces_the_subtitle_band_and_leaves_the_title_alone()
    {
        var page = new PageConfig();
        var bytes = ReportPdf.Render(SampleReport(), page,
            new PrintConfig { SubtitleOverride = "Prepared for the statutory auditor" });

        var s = AsLatin1(bytes);
        Assert.Contains("Prepared for the statutory auditor", s);
        Assert.DoesNotContain("as at 31-03-2025", s);
        Assert.Contains("Trial Balance", s);                    // the title is its own knob
        Assert.NotEqual(ReportPdf.Render(SampleReport(), page), bytes);
    }

    [Fact]
    public void Both_overrides_together_replace_both_bands()
    {
        var bytes = ReportPdf.Render(SampleReport(), new PageConfig(),
            new PrintConfig { TitleOverride = "List of Balances", SubtitleOverride = "Audit copy" });

        var s = AsLatin1(bytes);
        Assert.Contains("List of Balances", s);
        Assert.Contains("Audit copy", s);
        Assert.DoesNotContain("Trial Balance", s);
        Assert.DoesNotContain("as at 31-03-2025", s);
    }

    [Fact]
    public void The_override_is_trimmed_and_the_render_stays_deterministic()
    {
        var page = new PageConfig();
        var trimmed = ReportPdf.Render(SampleReport(), page, new PrintConfig { TitleOverride = "Audit Extract" });
        var padded = ReportPdf.Render(SampleReport(), page, new PrintConfig { TitleOverride = "  Audit Extract  " });

        Assert.Equal(trimmed, padded);
        Assert.Equal(trimmed, ReportPdf.Render(SampleReport(), page, new PrintConfig { TitleOverride = "Audit Extract" }));
    }

    // ------------------------------------------------------------------ the multi-document job (census 12.6)

    [Fact]
    public void The_override_applies_to_every_document_of_a_job()
    {
        var page = new PageConfig();
        var one = SampleReport();
        var two = new PrintReport
        {
            Title = "Ledger Account",
            Subtitle = "Acme Traders  —  April 2025",
            Columns = one.Columns,
            Rows = one.Rows,
        };

        var job = new[] { one, two };
        Assert.Equal(ReportPdf.Render(job, page), ReportPdf.Render(job, page, null));

        var s = AsLatin1(ReportPdf.Render(job, page, new PrintConfig { TitleOverride = "Audit Pack" }));
        // ONE operator title across the whole job — the same scope rule the W2-31 knobs take. A job whose
        // sheets carried two different operator titles would not be one document an operator can collate.
        Assert.DoesNotContain("Trial Balance", s);
        Assert.DoesNotContain("Ledger Account", s);
        Assert.Contains("Audit Pack", s);
    }

    // ------------------------------------------------------------------ the ruling-18 seam

    [Fact]
    public void A_title_override_clears_the_master_name_flag_but_a_subtitle_override_does_not()
    {
        var withMasterName = new PrintReport
        {
            Title = "Ledger Monthly Summary — Acme Traders Pvt Ltd",
            Subtitle = "Bright Traders  —  April 2025",
            TitleCarriesMasterName = true,
            Columns = SampleReport().Columns,
            Rows = SampleReport().Rows,
        };

        // 🔴 An override is text the OPERATOR typed into the print sub-form for OUR document — not a master name
        // a user typed into a master. Carrying the flag through would exempt an arbitrary typed heading from the
        // ER-11 guard on every report, which is the opposite of what ruling 18 asks for.
        var titled = withMasterName.WithHeaderOverride(new PrintConfig { TitleOverride = "Audit Extract" });
        Assert.Equal("Audit Extract", titled.Title);
        Assert.False(titled.TitleCarriesMasterName);

        // Overriding only the SUB-title leaves the real counterparty name in the title, so its protection stays.
        var subtitled = withMasterName.WithHeaderOverride(new PrintConfig { SubtitleOverride = "Audit copy" });
        Assert.Equal("Ledger Monthly Summary — Acme Traders Pvt Ltd", subtitled.Title);
        Assert.True(subtitled.TitleCarriesMasterName);
        Assert.Equal("Audit copy", subtitled.Subtitle);
    }

    [Fact]
    public void With_no_override_the_same_instance_is_returned()
    {
        var report = SampleReport();
        // Reference equality, deliberately: it is what makes the ER-13 byte-identity above structural rather
        // than coincidental — nothing is rebuilt, so nothing can drift.
        Assert.Same(report, report.WithHeaderOverride(null));
        Assert.Same(report, report.WithHeaderOverride(new PrintConfig()));
        Assert.Same(report, report.WithHeaderOverride(new PrintConfig { TitleOverride = " " }));
    }

    [Fact]
    public void The_overridden_report_keeps_its_columns_and_rows_by_reference()
    {
        var report = SampleReport();
        var overridden = report.WithHeaderOverride(new PrintConfig { TitleOverride = "Audit Extract" });

        // A heading knob must not be able to lose, reorder or re-format a single figure.
        Assert.Same(report.Columns, overridden.Columns);
        Assert.Same(report.Rows, overridden.Rows);
    }
}
