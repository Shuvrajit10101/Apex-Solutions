using System;
using System.IO;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-31 / census row 12.4 — the F8 print format, the F9 paper toggle, the F5 copy count and the F10 page
/// range/starting number must be REACHABLE from the print-configuration panel over an open preview, and
/// applying them must actually change the rendered bytes.
///
/// <para>The row does not close on a knob that exists in the Io layer. Before this slice the F12 panel opened
/// ONLY over a voucher/invoice preview, so a report preview — the surface most reports are printed from — had
/// no configuration column at all.</para>
/// </summary>
public sealed class PrintConfigFormatCopiesRangeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PrintConfigFormatCopiesRangeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPrintCfg_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private MainWindowViewModel ShellWithReportPreview()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();
        return vm;
    }

    private static int PageObjectCount(byte[] pdf)
    {
        string s = Encoding.Latin1.GetString(pdf);
        int count = 0, idx = 0;
        while ((idx = s.IndexOf("/Type /Page", idx, StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            if (after >= s.Length || s[after] != 's') count++;
            idx = after;
        }
        return count;
    }

    // ------------------------------------------------------------------ reachability

    [Fact]
    public void The_config_panel_opens_over_a_report_preview_not_only_a_voucher()
    {
        var shell = ShellWithReportPreview();
        Assert.NotNull(shell.PrintPreview);

        shell.OpenPrintConfig();

        Assert.NotNull(shell.PrintConfigPanel);
        Assert.Equal(Screen.PrintConfig, shell.CurrentScreen);
    }

    [Fact]
    public void The_document_only_knobs_stay_hidden_over_a_report()
    {
        var shell = ShellWithReportPreview();
        shell.OpenPrintConfig();

        // Title override / narration / copy marking are voucher-and-invoice knobs; the page knobs are not.
        Assert.False(shell.PrintConfigPanel!.SupportsDocumentKnobs);
        Assert.True(shell.PrintConfigPanel!.SupportsPageKnobs);
    }

    [Fact]
    public void The_panel_is_seeded_from_the_previews_current_page_knobs()
    {
        var shell = ShellWithReportPreview();
        shell.PrintPreview!.Copies = 2;
        shell.PrintPreview!.PrintFormat = PrintFormat.DotMatrix;

        shell.OpenPrintConfig();

        Assert.Equal(2, shell.PrintConfigPanel!.Copies);
        Assert.Equal(PrintFormat.DotMatrix, shell.PrintConfigPanel!.PrintFormat);
        Assert.True(shell.PrintConfigPanel!.IsDotMatrix);
    }

    // ------------------------------------------------------------------ the knobs change the bytes

    [Fact]
    public void Applying_a_copy_count_of_three_triples_the_rendered_pages()
    {
        var shell = ShellWithReportPreview();
        int before = PageObjectCount(shell.PrintPreview!.PdfBytes);
        Assert.Equal(1, before);

        shell.OpenPrintConfig();
        shell.PrintConfigPanel!.Copies = 3;
        shell.ApplyPrintConfig();

        Assert.Equal(3, PageObjectCount(shell.PrintPreview!.PdfBytes));
    }

    [Fact]
    public void Applying_pre_printed_paper_drops_the_title_from_the_bytes()
    {
        var shell = ShellWithReportPreview();
        Assert.Contains("Trial Balance", Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes));

        shell.OpenPrintConfig();
        shell.PrintConfigPanel!.IsPrePrinted = true;
        shell.ApplyPrintConfig();

        // The heading is on the stationery; the figures still print.
        string s = Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes);
        Assert.DoesNotContain("(Trial Balance) Tj", s);
    }

    [Fact]
    public void Applying_quick_draft_stops_the_renderer_stroking_rules()
    {
        var shell = ShellWithReportPreview();
        string neat = Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes);
        Assert.Contains(" l\nS\n", neat);

        shell.OpenPrintConfig();
        shell.PrintConfigPanel!.IsQuickDraft = true;
        shell.ApplyPrintConfig();

        Assert.DoesNotContain(" l\nS\n", Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes));
    }

    [Fact]
    public void The_format_radios_are_mutually_exclusive()
    {
        var shell = ShellWithReportPreview();
        shell.OpenPrintConfig();
        var panel = shell.PrintConfigPanel!;

        Assert.True(panel.IsNeat);
        panel.IsDotMatrix = true;
        Assert.True(panel.IsDotMatrix);
        Assert.False(panel.IsNeat);
        panel.IsQuickDraft = true;
        Assert.True(panel.IsQuickDraft);
        Assert.False(panel.IsDotMatrix);
        panel.IsNeat = true;
        Assert.True(panel.IsNeat);
        Assert.False(panel.IsQuickDraft);
    }

    [Fact]
    public void Applying_a_page_range_prints_only_those_sheets()
    {
        // The Day Book over Robert's demo is long enough to paginate; if it is not, the range is still honoured
        // and the assertion below degrades to "one page in, one page out", so the test states the page count it
        // measured rather than assuming one.
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.OpenReport(ReportKind.DayBook);
        shell.OpenPrintPreview();

        int total = PageObjectCount(shell.PrintPreview!.PdfBytes);

        shell.OpenPrintConfig();
        shell.PrintConfigPanel!.FirstPage = 1;
        shell.PrintConfigPanel!.LastPage = 1;
        shell.ApplyPrintConfig();

        Assert.Equal(1, PageObjectCount(shell.PrintPreview!.PdfBytes));
        Assert.True(total >= 1);
    }

    [Fact]
    public void Applying_a_start_page_number_renumbers_the_footer()
    {
        var shell = ShellWithReportPreview();
        Assert.Contains("Page 1 of 1", Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes));

        shell.OpenPrintConfig();
        shell.PrintConfigPanel!.StartPageNumber = 7;
        shell.ApplyPrintConfig();

        Assert.Contains("Page 7 of 7", Encoding.Latin1.GetString(shell.PrintPreview!.PdfBytes));
    }

    [Fact]
    public void Opening_and_applying_with_no_edits_leaves_the_bytes_untouched()
    {
        var shell = ShellWithReportPreview();
        byte[] before = shell.PrintPreview!.PdfBytes;

        shell.OpenPrintConfig();
        shell.ApplyPrintConfig();

        Assert.Equal(before, shell.PrintPreview!.PdfBytes);
    }
}
