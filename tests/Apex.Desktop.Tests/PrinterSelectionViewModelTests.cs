using System;
using System.Collections.Generic;
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

    /// <summary>
    /// 🔴 <b>A clamp that does not announce itself leaves the box showing a number the paper will not match.</b>
    ///
    /// <para>The copies box is a two-way binding. Typing 0 clamps to 1, but on a preview that is ALREADY at 1
    /// that is no change to the stored value — so a setter that only notifies when the stored value moves stays
    /// silent, Avalonia never pushes the corrected value back, and the panel sits there reading "0 copies" while
    /// the job it is about to spool will print 1. The screen would be lying about what is being printed.</para>
    ///
    /// <para>This asserts the notification itself rather than the resulting value, deliberately: <see
    /// cref="A_copy_count_below_one_clamps_to_one"/> above already passes on the defective build, because the
    /// value was never wrong — only the display was.</para>
    /// </summary>
    [Fact]
    public void A_clamped_copy_count_announces_itself_so_the_box_cannot_show_a_number_the_job_will_not_print()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);
        Assert.Equal(1, preview.Copies);   // the clamp target and the current value coincide — the silent case

        var announced = new List<string?>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        panel.Copies = 0;

        Assert.Contains(nameof(PrinterSelectionViewModel.Copies), announced);
        Assert.Equal(1, panel.Copies);
    }

    /// <summary>A rejected NEGATIVE count announces itself for the same reason, and still prints one.</summary>
    [Fact]
    public void A_negative_copy_count_announces_itself_and_still_spools_one()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out _);

        var announced = new List<string?>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        panel.Copies = -5;

        Assert.Contains(nameof(PrinterSelectionViewModel.Copies), announced);
        Assert.Equal(1, panel.Copies);
    }

    /// <summary>
    /// A count that genuinely moves still announces itself exactly as before — the fix above must not have been
    /// bought by making the ordinary path noisier or quieter.
    /// </summary>
    [Fact]
    public void A_real_copy_count_change_still_announces_itself()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);

        var announced = new List<string?>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        panel.Copies = 4;

        Assert.Contains(nameof(PrinterSelectionViewModel.Copies), announced);
        Assert.Equal(4, panel.Copies);
        Assert.Equal(4, preview.Copies);
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

    /// <summary>
    /// 🔴 <b>The copies box is BOUNDED above, because writing it re-renders the whole PDF synchronously on the
    /// UI thread.</b>
    ///
    /// <para><c>Copies</c> writes <c>PrintPreviewViewModel.Copies</c>, whose <c>OnCopiesChanged</c> calls
    /// <c>Render()</c>, which ends in <c>writer.RepeatAllPages(EffectiveCopies)</c> — the document is physically
    /// repeated that many times. The count arrived from a free-text box with no upper bound at all, so a
    /// mistyped or pasted five-figure number asked the shell to build a document of that many document-sets, in
    /// one blocking call, from a keystroke: a frozen window and then an <c>OutOfMemoryException</c>.</para>
    ///
    /// <para>5,000 is chosen deliberately over something like 100,000: it is unambiguously past the bound, so it
    /// proves the clamp, while staying a size the DEFECTIVE build can actually finish rendering. A mutation
    /// check on this line must fail the test, not exhaust the runner's memory and take the gate with it.</para>
    /// </summary>
    [Fact]
    public void A_copy_count_above_the_bound_is_clamped_instead_of_rendered()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);

        panel.Copies = 5000;

        Assert.Equal(PrinterSelectionViewModel.MaxCopies, panel.Copies);
        Assert.Equal(PrinterSelectionViewModel.MaxCopies, preview.Copies);
    }

    /// <summary>
    /// The upper clamp announces itself for exactly the reason the lower one does. On a preview ALREADY at the
    /// bound, clamping 5,000 down to it moves no stored value — so a setter that notified only on movement would
    /// leave the two-way binding sitting there reading "5000" while the job prints 99.
    /// </summary>
    [Fact]
    public void A_clamped_high_copy_count_announces_itself()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);
        panel.Copies = PrinterSelectionViewModel.MaxCopies;
        Assert.Equal(PrinterSelectionViewModel.MaxCopies, preview.Copies);   // the silent case is now set up

        var announced = new List<string?>();
        panel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        panel.Copies = 5000;

        Assert.Contains(nameof(PrinterSelectionViewModel.Copies), announced);
        Assert.Equal(PrinterSelectionViewModel.MaxCopies, panel.Copies);
    }

    /// <summary>
    /// The bound is an upper bound and nothing more: a count comfortably inside it is still honoured exactly,
    /// and still reaches the paper by re-rendering the document. A clamp that quietly capped everything would
    /// pass the two tests above.
    /// </summary>
    [Fact]
    public void A_count_inside_the_bound_is_untouched_and_still_re_renders()
    {
        var panel = Panel(FakePrinterDevices.TwoWithDefaultSecond(), new RecordingPrintJobSubmitter(), out var preview);
        byte[] singleCopy = preview.PdfBytes;

        panel.Copies = PrinterSelectionViewModel.MaxCopies;

        Assert.Equal(PrinterSelectionViewModel.MaxCopies, panel.Copies);
        Assert.Equal(PrinterSelectionViewModel.MaxCopies, preview.Copies);
        Assert.True(preview.PdfBytes.Length > singleCopy.Length,
            "the bound was applied but the document never re-rendered, so the copies are not in the bytes.");
    }

    /// <summary>
    /// 🔴 <b>Both submission notices, pinned word for word — the pin <c>PrinterDevice.SubmissionNotice</c>'s own
    /// comment claimed existed and did not.</b>
    ///
    /// <para>The comment said "<c>PrinterSelectionViewModelTests</c> pins both wordings". Nothing did: the only
    /// test that read the property asserted the two notices were DIFFERENT from each other, which stays green if
    /// both are replaced with "OK" and "Fine". These are not decoration. The Windows one is the operator's ONLY
    /// warning that the spooler can accept a RAW job that the device then prints as nothing, and the sentence
    /// that tells them what to do instead is the second half of it; the CUPS one is the counter-statement that
    /// makes the first meaningful rather than a blanket disclaimer on every printer.</para>
    /// </summary>
    [Fact]
    public void Both_submission_notices_are_pinned_word_for_word()
    {
        var cups = new PrinterDevice("Cups One", IsDefault: true, PdfSubmissionMode.CupsNative);
        var raw = new PrinterDevice("Raw Two", IsDefault: false, PdfSubmissionMode.WindowsRaw);

        Assert.Equal(
            "This printer is driven by CUPS, which accepts PDF directly — the job prints as previewed.",
            cups.SubmissionNotice);

        Assert.Equal(
            "The PDF is spooled to this printer unchanged. Printers that read PDF or PostScript print it as "
          + "previewed; others will not. If nothing comes out, use Save PDF and print from your PDF reader.",
            raw.SubmissionNotice);

        // The clauses that carry the warning, asserted separately so a reworded-but-still-honest notice fails
        // loudly on the sentence above while these say WHY the wording mattered.
        Assert.Contains("others will not", raw.SubmissionNotice, StringComparison.Ordinal);
        Assert.Contains("Save PDF", raw.SubmissionNotice, StringComparison.Ordinal);

        // And the panel shows the SELECTED queue's notice, not a constant.
        var panel = Panel(new FakePrinterDevices(cups, raw), new RecordingPrintJobSubmitter(), out _);
        Assert.Equal(cups.SubmissionNotice, panel.SubmissionNotice);
        panel.Selected = panel.Printers[1];
        Assert.Equal(raw.SubmissionNotice, panel.SubmissionNotice);
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
