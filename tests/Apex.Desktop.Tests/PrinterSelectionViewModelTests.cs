using System;
using System.Linq;
using System.Threading.Tasks;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 12.5, slice F1-S2 — the selection half.</b> Which printers are offered, which one is chosen,
/// what the copy count means, what is handed to the spooler, and what an operator with no printer at all is
/// told. Every one of these runs on a machine with no printer, which is the only kind of machine the gate has.
/// </summary>
public sealed class PrinterSelectionViewModelTests
{
    private static PrintReport SmallReport() => new()
    {
        Title = "Trial Balance",
        Subtitle = "as at 31-Mar-2026",
        Columns = new[] { new PrintColumn("Particulars", 3), new PrintColumn("Debit", 1, CellAlign.Right) },
        Rows = new[] { new PrintRow("Cash", "1,000.00"), new PrintRow("Bank", "2,000.00") },
    };

    private static PrintPreviewViewModel Preview() => new(SmallReport(), "Trial Balance");

    private static PrinterSelectionViewModel Panel(
        IPrinterDevices devices, IPrintJobSubmitter submitter, out PrintPreviewViewModel preview)
    {
        preview = Preview();
        return new PrinterSelectionViewModel(preview, devices, submitter);
    }

    /// <summary>
    /// The OS's own default queue opens pre-selected, so the ordinary case is Ctrl+P then Ctrl+A. Note the fake
    /// puts the default SECOND on purpose: selecting index 0 would satisfy a weaker test.
    /// </summary>
    [Fact]
    public void Default_printer_is_preselected()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out _);

        Assert.Equal(2, panel.Printers.Count);
        Assert.NotNull(panel.Selected);
        Assert.Equal("Accounts Laser", panel.Selected!.Name);
        Assert.True(panel.Selected.IsDefault);
    }

    /// <summary>When the OS names no default we still open with a usable selection rather than none.</summary>
    [Fact]
    public void The_first_printer_is_selected_when_the_os_names_no_default()
    {
        var devices = new FakePrinterDevices(
            new PrinterDevice("Alpha", IsDefault: false, PdfSubmissionMode.CupsNative),
            new PrinterDevice("Beta", IsDefault: false, PdfSubmissionMode.CupsNative));

        var panel = Panel(devices, new RecordingPrintJobSubmitter(), out _);

        Assert.Equal("Alpha", panel.Selected?.Name);
    }

    /// <summary>
    /// A machine with no printers is a normal machine — every CI runner is one — and the panel must say what to
    /// do instead rather than presenting an empty list with a live Print button.
    /// </summary>
    [Fact]
    public void Zero_printers_shows_the_no_printer_message_and_offers_Save_PDF()
    {
        var panel = Panel(FakePrinterDevices.None(), new RecordingPrintJobSubmitter(), out _);

        Assert.Empty(panel.Printers);
        Assert.Null(panel.Selected);
        Assert.False(panel.HasPrinters);
        Assert.True(panel.HasNoPrinters);
        Assert.Equal(PrinterSelectionViewModel.NoPrinterNotice, panel.Status);
        Assert.Contains("Save PDF", panel.SubmissionNotice, StringComparison.Ordinal);
    }

    /// <summary>Pressing Print with no printer spools nothing and explains why. Silence would be the defect.</summary>
    [Fact]
    public async Task Printing_with_no_printer_submits_nothing()
    {
        var submitter = new RecordingPrintJobSubmitter();
        var panel = Panel(FakePrinterDevices.None(), submitter, out _);

        await panel.PrintAsync();

        Assert.Empty(submitter.Submitted);
        Assert.Equal(PrintJobBuilder.NoPrinterMessage, panel.Status);
        Assert.Null(panel.LastResult);
    }

    /// <summary>
    /// The whole point of the row: the previewed bytes reach the spooler, on the chosen queue, under the
    /// document's own name, exactly once.
    /// </summary>
    [Fact]
    public async Task Printing_hands_the_previewed_bytes_to_the_spooler_once()
    {
        var submitter = new RecordingPrintJobSubmitter();
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), submitter, out var preview);

        await panel.PrintAsync();

        var job = Assert.Single(submitter.Submitted);
        Assert.Equal("Accounts Laser", job.PrinterName);
        Assert.Equal("Trial Balance", job.JobName);
        Assert.Equal(1, job.SpoolerCopies);
        Assert.True(job.Pdf.SequenceEqual(preview.PdfBytes));
        Assert.True(panel.LastResult?.Submitted);
        Assert.False(panel.IsSubmitting);
    }

    /// <summary>The operator can pick a queue other than the default, and that is the one that gets the job.</summary>
    [Fact]
    public async Task The_selected_queue_is_the_one_that_receives_the_job()
    {
        var submitter = new RecordingPrintJobSubmitter();
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), submitter, out _);

        panel.Selected = panel.Printers.First(p => p.Name == "Front Office HP");
        await panel.PrintAsync();

        Assert.Equal("Front Office HP", Assert.Single(submitter.Submitted).PrinterName);
    }

    /// <summary>
    /// 🔴 <b>One copy count, not two.</b> The panel's box writes the preview's own knob, which re-renders the
    /// document with that many collated sets — and the spooler is still asked for one. A panel-local count
    /// pushed on apply would be a second truth, and the two multiplying is how three copies become nine.
    /// </summary>
    [Fact]
    public async Task The_copy_count_is_the_previews_own_and_the_spooler_still_gets_one()
    {
        var submitter = new RecordingPrintJobSubmitter();
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), submitter, out var preview);

        byte[] singleCopy = preview.PdfBytes;
        panel.Copies = 3;

        Assert.Equal(3, preview.Copies);
        Assert.True(preview.PdfBytes.Length > singleCopy.Length,
            "raising the copy count did not re-render the document — the copies are not in the bytes, so the "
          + "spooler count below would be wrong.");

        await panel.PrintAsync();

        var job = Assert.Single(submitter.Submitted);
        Assert.Equal(1, job.SpoolerCopies);
        Assert.True(job.Pdf.SequenceEqual(preview.PdfBytes));
    }

    /// <summary>Zero or negative copies clamp to one, matching <c>PageConfig.EffectiveCopies</c>.</summary>
    [Fact]
    public void A_copy_count_below_one_clamps_to_one()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);

        panel.Copies = 0;
        Assert.Equal(1, panel.Copies);
        Assert.Equal(1, preview.Copies);
    }

    /// <summary>A refusal is reported in the platform's own words, not as a generic failure.</summary>
    [Fact]
    public async Task A_refused_job_reports_the_platforms_reason()
    {
        var submitter = new RecordingPrintJobSubmitter(succeed: false, failureMessage: "Accounts Laser is offline");
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), submitter, out _);

        await panel.PrintAsync();

        Assert.False(panel.LastResult?.Submitted);
        Assert.Equal("Accounts Laser is offline", panel.Status);
        Assert.False(panel.IsSubmitting);
    }

    /// <summary>
    /// Opening the panel and walking away changes nothing: the preview's bytes are untouched and nothing was
    /// spooled. Cancel that mutates is the shape <c>IFilePathPicker</c>'s own contract warns about.
    /// </summary>
    [Fact]
    public void Cancel_leaves_the_preview_untouched()
    {
        var preview = Preview();
        byte[] before = preview.PdfBytes;
        var submitter = new RecordingPrintJobSubmitter();

        _ = new PrinterSelectionViewModel(preview, FakePrinterDevices.TwoWithDefaultSecond(), submitter);

        Assert.True(preview.PdfBytes.SequenceEqual(before));
        Assert.Equal(1, preview.Copies);
        Assert.Empty(submitter.Submitted);
    }

    /// <summary>
    /// The queue list is read once, when the column opens. On CUPS every enumeration spawns <c>lpstat</c>, so
    /// re-listing on selection changes would spawn a process per arrow key.
    /// </summary>
    [Fact]
    public void The_printer_list_is_enumerated_once_not_per_selection_change()
    {
        var devices = FakePrinterDevices.TwoWithDefaultSecond();
        var panel = Panel(devices, new RecordingPrintJobSubmitter(), out _);

        panel.Selected = panel.Printers[0];
        panel.Selected = panel.Printers[1];
        _ = panel.SubmissionNotice;

        Assert.Equal(1, devices.ListCallCount);
    }

    /// <summary>Changing the selection changes the notice — the RAW warning follows the queue, not the panel.</summary>
    [Fact]
    public void The_submission_notice_follows_the_selected_queue()
    {
        var devices = new FakePrinterDevices(
            new PrinterDevice("Cups One", IsDefault: true, PdfSubmissionMode.CupsNative),
            new PrinterDevice("Raw Two", IsDefault: false, PdfSubmissionMode.WindowsRaw));
        var panel = Panel(devices, new RecordingPrintJobSubmitter(), out _);

        string cupsNotice = panel.SubmissionNotice;
        panel.Selected = panel.Printers[1];

        Assert.NotEqual(cupsNotice, panel.SubmissionNotice);
        Assert.Contains("Save PDF", panel.SubmissionNotice, StringComparison.Ordinal);
    }
}
