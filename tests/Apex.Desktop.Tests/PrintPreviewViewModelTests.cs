using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-9 (RQ-9 print/preview, RQ-13 de-branded output): the P / Ctrl+P
/// Print action projects the CURRENT report into a de-branded PDF via <c>Apex.Ledger.Io</c>, the preview VM
/// exposes the paginated pages + the rendered bytes, and Save writes those bytes.
///
/// <para>Every test drives the wiring over the embedded "Robert" demo (last voucher 2020-04-30 → default
/// as-of). The renderer itself is trusted (covered by <c>Apex.Ledger.Io.Tests</c>); these tests pin the
/// thin Avalonia layer: the shell opens a preview column, the bytes are non-empty + contain the report
/// title + carry no "tally" (case-insensitive) anywhere, the preview paginates, and Save persists.</para>
/// </summary>
public sealed class PrintPreviewViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PrintPreviewViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPrintPreviewTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel ShellWithReport(ReportKind kind)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(kind);
        return vm;
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- print produces de-branded PDF bytes

    [Fact]
    public void Print_produces_nonempty_pdf_bytes_with_title_and_no_tally()
    {
        var vm = ShellWithReport(ReportKind.TrialBalance);

        vm.OpenPrintPreview();

        Assert.NotNull(vm.PrintPreview);
        var bytes = vm.PrintPreview!.PdfBytes;
        Assert.NotEmpty(bytes);

        var text = AsLatin1(bytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("Trial Balance", text);           // the report title survives into the PDF content
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase); // RQ-13 de-brand
        Assert.Contains("Apex Solutions", text);          // brand-safe metadata / footer
    }

    [Fact]
    public void Print_bytes_are_byte_identical_on_re_render()
    {
        var report = new PrintReport
        {
            Title = "Trial Balance",
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = new[]
            {
                new PrintRow("Cash", "1,00,000.00"),
                PrintRow.Total("Grand Total", "1,00,000.00"),
            },
        };

        var a = new PrintPreviewViewModel(report, "Trial Balance").PdfBytes;
        var b = new PrintPreviewViewModel(report, "Trial Balance").PdfBytes;
        Assert.Equal(a, b); // determinism (no clock / RNG in the render path)
    }

    // ---------------------------------------------------------------- preview exposes paginated pages

    [Fact]
    public void Preview_exposes_pages_reflecting_the_report()
    {
        var vm = ShellWithReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();
        var preview = vm.PrintPreview!;

        Assert.True(preview.PageCount >= 1);
        Assert.NotEmpty(preview.Pages);
        Assert.Equal("Trial Balance", preview.Pages[0].Title);

        // The Grand Total line the grid shows must appear as a preview line on some page.
        bool hasTotal = preview.Pages
            .SelectMany(p => p.Lines)
            .Any(l => l.IsTotal && l.Cells.Any(c => c.Contains("Grand Total")));
        Assert.True(hasTotal);

        // Every page's footer is brand-safe and never mentions a third-party brand.
        foreach (var page in preview.Pages)
        {
            Assert.Contains("Apex Solutions", page.Footer);
            Assert.DoesNotContain("tally", page.Footer, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Long_report_paginates_to_more_than_one_preview_page()
    {
        // A tall synthetic report forces overflow well past one page.
        var rows = Enumerable.Range(1, 400)
            .Select(i => new PrintRow($"Ledger {i}", $"{i:#,0}.00"))
            .ToArray();
        var report = new PrintReport
        {
            Title = "Big Report",
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };

        var preview = new PrintPreviewViewModel(report, "Big Report");
        Assert.True(preview.PageCount > 1, $"expected >1 page, got {preview.PageCount}");
        // The rendered PDF paginates too (footer shows "Page 1 of N", N>1).
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("Page 1 of", text);
    }

    // ---------------------------------------------------------------- Save writes the bytes

    [Fact]
    public void Save_writes_the_rendered_pdf_bytes_to_disk()
    {
        var vm = ShellWithReport(ReportKind.BalanceSheet);
        vm.OpenPrintPreview();
        var preview = vm.PrintPreview!;
        var path = Path.Combine(_tempDir, "bs.pdf");

        Assert.True(vm.SavePrintPreview(path));
        Assert.True(File.Exists(path));

        var onDisk = File.ReadAllBytes(path);
        Assert.Equal(preview.PdfBytes, onDisk);       // what is saved is exactly what was previewed
        Assert.StartsWith("%PDF-", AsLatin1(onDisk));
        Assert.DoesNotContain("tally", AsLatin1(onDisk), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_with_blank_path_is_rejected_and_reports_status()
    {
        var vm = ShellWithReport(ReportKind.ProfitAndLoss);
        vm.OpenPrintPreview();

        Assert.False(vm.SavePrintPreview("   "));
        Assert.False(string.IsNullOrEmpty(vm.PrintPreview!.Status));
    }

    // ---------------------------------------------------------------- shell wiring

    [Fact]
    public void OpenPrintPreview_is_a_noop_without_a_report_and_does_not_stack()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.OpenPrintPreview();                 // no report open
        Assert.Null(vm.PrintPreview);

        var vm2 = ShellWithReport(ReportKind.DayBook);
        vm2.OpenPrintPreview();
        var first = vm2.PrintPreview;
        vm2.OpenPrintPreview();                // re-press: must not stack a second preview
        Assert.Same(first, vm2.PrintPreview);
        Assert.Equal(Screen.PrintPreview, vm2.CurrentScreen);
    }

    [Fact]
    public void Landscape_and_letter_toggles_re_render_valid_pdf()
    {
        var vm = ShellWithReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();
        var preview = vm.PrintPreview!;
        var portraitA4 = preview.PdfBytes;

        preview.Landscape = true;
        preview.UseLetter = true;
        var landscapeLetter = preview.PdfBytes;

        Assert.NotEqual(portraitA4, landscapeLetter);            // the page geometry changed
        Assert.StartsWith("%PDF-", AsLatin1(landscapeLetter));
        Assert.DoesNotContain("tally", AsLatin1(landscapeLetter), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
