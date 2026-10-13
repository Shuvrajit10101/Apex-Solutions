using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 12.3 — <b>the report title / sub-title override has to be REACHABLE, and it has to MOVE THE BYTES.</b>
///
/// <para>The census gap read: the print config is "voucher/invoice only, so a <b>report</b> print has no
/// configuration beyond the page-size and orientation toggles". The engine half of the fix is locked by
/// <c>Apex.Ledger.Io.Tests.ReportPrintHeaderOverrideTests</c> on the emitted PDF; this file locks the half that
/// decides whether an operator can get at it — the F12 panel over a report preview, the group's visibility, the
/// Apply push, and the on-screen band agreeing with the paper.</para>
///
/// <para><b>Vendor grounding</b> is quoted in full on <c>PrintConfig.TitleOverride</c>: the
/// <i>Titles/Subtitles</i> sub-form (<b>F7</b> → Header Information, Title and Subtitle) on
/// <c>help.tallysolutions.com/configure-printing-for-invoices-reports/</c>, whose opening sentence names
/// <i>financial reports</i>, plus <i>"updating the title/sub-title of the report"</i> on
/// <c>help.tallysolutions.com/working-with-reports/</c>. Both opened by content 2026-10-13.</para>
///
/// <para>🔴 This is the <c>PrintConfigKnobsMoveTheBytesTests</c> discipline applied to a new knob: a knob the
/// panel offers must do something, and the proof is a byte comparison rather than a property read. Alt+P was
/// once inert and three reports printed pages of blank cells — offering a control and ignoring it is the same
/// class of defect.</para>
/// </summary>
public sealed class ReportPrintHeaderKnobReachTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReportPrintHeaderKnobReachTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexHdrKnob_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static PrintReport SampleReport() => new()
    {
        Title = "Trial Balance",
        Subtitle = "Bright Traders  —  as at 31-03-2025",
        Columns = new[]
        {
            new PrintColumn("Particulars", 3.0),
            new PrintColumn("Debit", 1.0, CellAlign.Right),
        },
        Rows = new[]
        {
            new PrintRow("Cash-in-Hand", "1,05,000.00"),
            PrintRow.Total("Grand Total", "1,05,000.00"),
        },
    };

    private static InvoicePrintData MinimalInvoice() => new()
    {
        DocumentTitle = "Tax Invoice",
        Seller = new InvoicePartyBlock { Name = "Bright Traders" },
        Buyer = new InvoicePartyBlock { Name = "Acme Traders" },
        InvoiceNumber = "INV-1",
        InvoiceDateText = "10-04-2025",
        Items = Array.Empty<InvoiceItemRow>(),
    };

    // ================================================================ the panel offers it, on the right kinds

    [Fact]
    public void The_f12_panel_over_a_report_preview_offers_the_header_knobs_and_not_the_document_knobs()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var panel = new PrintConfigViewModel(preview);

        Assert.True(panel.SupportsReportHeaderKnobs);
        // 🔴 And NOT the document knobs. Widening SupportsPrintConfig instead of adding a flag would have put a
        // narration checkbox and three CGST Rule 48(1) copy radios on a Trial Balance's F12 panel — three
        // controls ReportPdf cannot honour, i.e. three dead knobs.
        Assert.False(panel.SupportsDocumentKnobs);
    }

    [Fact]
    public void The_f12_panel_over_a_document_preview_does_not_offer_the_report_header_knobs()
    {
        var panel = new PrintConfigViewModel(new PrintPreviewViewModel(MinimalInvoice()));

        Assert.True(panel.SupportsDocumentKnobs);          // the invoice keeps its own title override
        Assert.False(panel.SupportsReportHeaderKnobs);     // no sub-title band exists to override
    }

    [Fact]
    public void A_multi_document_job_preview_offers_the_header_knobs_too()
    {
        var job = new[] { SampleReport(), SampleReport() };
        var panel = new PrintConfigViewModel(new PrintPreviewViewModel(job, "Audit Pack"));

        Assert.True(panel.SupportsReportHeaderKnobs);
        Assert.False(panel.SupportsDocumentKnobs);
    }

    // ================================================================ applying it moves the bytes

    [Fact]
    public void Applying_a_title_override_through_the_panel_moves_the_report_previews_bytes()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var before = preview.PdfBytes.ToArray();
        Assert.NotEmpty(before);

        var panel = new PrintConfigViewModel(preview);
        panel.TitleOverride = "List of Balances";
        panel.Apply();

        Assert.False(before.SequenceEqual(preview.PdfBytes),
            "the panel offers a report title override, but applying it left the PDF byte-identical — either "
          + "ReportPdf is not reading PrintConfig.TitleOverride or the preview is not passing it.");
        Assert.Equal("List of Balances", preview.TitleOverride);
    }

    [Fact]
    public void Applying_a_subtitle_override_through_the_panel_moves_the_report_previews_bytes()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var before = preview.PdfBytes.ToArray();

        var panel = new PrintConfigViewModel(preview);
        panel.SubtitleOverride = "Prepared for the statutory auditor";
        panel.Apply();

        Assert.False(before.SequenceEqual(preview.PdfBytes),
            "the panel offers a report sub-title override, but applying it left the PDF byte-identical.");
        Assert.Equal("Prepared for the statutory auditor", preview.SubtitleOverride);
    }

    [Fact]
    public void Opening_the_panel_and_applying_with_no_edits_leaves_the_bytes_untouched()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var before = preview.PdfBytes.ToArray();

        new PrintConfigViewModel(preview).Apply();

        // ER-13: a preview the operator never configured prints the bytes it always did.
        Assert.True(before.SequenceEqual(preview.PdfBytes));
    }

    [Fact]
    public void Reopening_the_panel_shows_the_override_already_applied()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");

        var first = new PrintConfigViewModel(preview);
        first.TitleOverride = "List of Balances";
        first.SubtitleOverride = "Audit copy";
        first.Apply();

        // The seeding half. A panel that reopened blank would silently discard the operator's heading the next
        // time they adjusted the paper size — the same defect as not applying it at all.
        var second = new PrintConfigViewModel(preview);
        Assert.Equal("List of Balances", second.TitleOverride);
        Assert.Equal("Audit copy", second.SubtitleOverride);
    }

    // ================================================================ the pane cannot lie about the paper

    [Fact]
    public void The_on_screen_preview_band_shows_the_same_heading_the_pdf_carries()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        Assert.Contains("Trial Balance", preview.Pages[0].Title, StringComparison.Ordinal);

        var panel = new PrintConfigViewModel(preview);
        panel.TitleOverride = "List of Balances";
        panel.SubtitleOverride = "Audit copy";
        panel.Apply();

        // 🔴 The band and the bytes read ONE document. A preview that kept the old heading while the paper
        // carried the new one is the preview-lies-about-the-paper defect; both halves are asserted here.
        var page = preview.Pages[0];
        Assert.Contains("List of Balances", page.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Trial Balance", page.Title, StringComparison.Ordinal);
        Assert.Contains("Audit copy", page.Subtitle, StringComparison.Ordinal);

        var pdf = System.Text.Encoding.Latin1.GetString(preview.PdfBytes);
        Assert.Contains("List of Balances", pdf);
        Assert.DoesNotContain("Trial Balance", pdf);
    }

    [Fact]
    public void Typing_in_the_preview_box_re_renders_a_report_without_waiting_for_Apply()
    {
        // The preview VM's own properties are what the panel pushes to, and the W2-31 page knobs re-render on
        // change. The header overrides must behave the same way on a REPORT, or the first draft's bug returns:
        // the override reached the renderer and typing re-rendered nothing, so the knob worked and looked broken.
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var before = preview.PdfBytes.ToArray();

        preview.TitleOverride = "List of Balances";
        Assert.False(before.SequenceEqual(preview.PdfBytes));

        var afterTitle = preview.PdfBytes.ToArray();
        preview.SubtitleOverride = "Audit copy";
        Assert.False(afterTitle.SequenceEqual(preview.PdfBytes));
    }

    [Fact]
    public void The_narration_and_copy_knobs_still_do_nothing_to_a_report_and_are_not_offered_there()
    {
        var preview = new PrintPreviewViewModel(SampleReport(), "Trial Balance");
        var before = preview.PdfBytes.ToArray();

        preview.ShowNarration = false;
        preview.CopyMarking = CopyMarking.Triplicate;

        // Unchanged bytes — ReportPdf has no narration line and no copy band. This is WHY those two stay behind
        // SupportsDocumentKnobs: the honest move for a knob the renderer ignores is not to offer it.
        Assert.True(before.SequenceEqual(preview.PdfBytes));
        Assert.False(new PrintConfigViewModel(preview).SupportsDocumentKnobs);
    }

    // ================================================================ the shell route (F12 over Alt+P)

    [Fact]
    public void F12_over_a_real_reports_print_preview_opens_a_panel_that_offers_the_header_knobs()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Header Knob Co " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();

        vm.OpenReport(ReportKind.TrialBalance);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        vm.OpenPrintPreview();                       // Alt+P
        Assert.NotNull(vm.PrintPreview);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Report, vm.PrintPreview!.Kind);

        vm.OpenPrintConfig();                        // F12
        Assert.NotNull(vm.PrintConfigPanel);
        Assert.True(vm.PrintConfigPanel!.SupportsReportHeaderKnobs);

        var before = vm.PrintPreview!.PdfBytes.ToArray();
        vm.PrintConfigPanel!.TitleOverride = "List of Balances";
        vm.ApplyPrintConfig();                       // Ctrl+A

        Assert.False(before.SequenceEqual(vm.PrintPreview!.PdfBytes));
        Assert.Contains("List of Balances",
            System.Text.Encoding.Latin1.GetString(vm.PrintPreview!.PdfBytes));
    }
}
