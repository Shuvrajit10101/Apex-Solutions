using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// A substitutable printer list for every test above the platform shim. No CI runner has a printer, so the
/// enumeration half of census row 12.5 is only assertable against a fake — exactly the reason
/// <c>IFilePathPicker</c> has <c>FakeFilePathPicker</c> beside it.
/// </summary>
internal sealed class FakePrinterDevices : IPrinterDevices
{
    private readonly IReadOnlyList<PrinterDevice> _devices;

    /// <summary>How many times the panel asked for the list — a re-enumeration on every keystroke is a defect.</summary>
    public int ListCallCount { get; private set; }

    public FakePrinterDevices(params PrinterDevice[] devices) => _devices = devices ?? Array.Empty<PrinterDevice>();

    public IReadOnlyList<PrinterDevice> List()
    {
        ListCallCount++;
        return _devices;
    }

    /// <summary>Two queues, the second of which the OS calls the default — the ordinary office machine.</summary>
    public static FakePrinterDevices TwoWithDefaultSecond() => new(
        new PrinterDevice("Front Office HP", IsDefault: false, PdfSubmissionMode.WindowsRaw),
        new PrinterDevice("Accounts Laser", IsDefault: true, PdfSubmissionMode.WindowsRaw));

    /// <summary>A machine with no printers installed at all.</summary>
    public static FakePrinterDevices None() => new();
}

/// <summary>
/// A submitter that records what it was handed and never touches the OS. It is the only way to assert what
/// would have reached paper.
/// </summary>
internal sealed class RecordingPrintJobSubmitter : IPrintJobSubmitter
{
    private readonly bool _succeed;
    private readonly string _failureMessage;

    public List<PrintJob> Submitted { get; } = new();

    public RecordingPrintJobSubmitter(bool succeed = true, string failureMessage = "the queue refused the job")
    {
        _succeed = succeed;
        _failureMessage = failureMessage;
    }

    public Task<PrintJobResult> SubmitAsync(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Submitted.Add(job);
        return Task.FromResult(_succeed
            ? new PrintJobResult(true, $"Sent \"{job.JobName}\" to {job.PrinterName}.")
            : new PrintJobResult(false, _failureMessage));
    }
}

/// <summary>
/// <b>Census row 12.5, slice F1-S1 — the job-building half, which is where the decisions that reach paper are
/// made.</b>
///
/// <para>Avalonia 12.0.5 ships no printing API whatsoever, so 12.5 is a platform shim behind an interface; and
/// no CI runner has a printer, so the submission call itself cannot be executed anywhere the gate runs. This
/// file exists because everything <i>except</i> that one call is ordinary deterministic code — which bytes are
/// spooled, how many times, under what name, and what happens when there is no printer — and a capability whose
/// only test is "it compiles" is not shipped.</para>
/// </summary>
public sealed class PrintJobBuilderTests
{
    private static PrintReport ThreePageish() => new()
    {
        Title = "Trial Balance",
        Subtitle = "as at 31-Mar-2026",
        Columns = new[] { new PrintColumn("Particulars", 3), new PrintColumn("Debit", 1, CellAlign.Right) },
        Rows = Enumerable.Range(1, 40)
                         .Select(i => new PrintRow($"Ledger {i}", $"{i * 100:0.00}"))
                         .ToArray(),
    };

    /// <summary>Counts the page objects the PDF actually carries — the writer emits one "/Type /Page /Parent" each.</summary>
    private static int PdfPageCount(byte[] pdf)
    {
        string text = Encoding.ASCII.GetString(pdf);
        int count = 0, at = 0;
        const string marker = "/Type /Page /Parent";
        while ((at = text.IndexOf(marker, at, StringComparison.Ordinal)) >= 0) { count++; at += marker.Length; }
        return count;
    }

    private static PrinterDevice AnyDevice() =>
        new("Accounts Laser", IsDefault: true, PdfSubmissionMode.WindowsRaw);

    /// <summary>
    /// 🔴 <b>The N² guard, and the single likeliest silent defect in this whole track.</b>
    ///
    /// <para>The copy count is already collated into the PDF — every renderer ends with
    /// <c>RepeatAllPages(EffectiveCopies)</c> — so a three-copy preview is physically a three-times-longer
    /// document. If the job ALSO asked the spooler for three copies, nine document sets would come out of a real
    /// printer, and nothing in a headless suite would ever notice. This test proves both halves at once: the
    /// bytes carry the copies, and the spooler is asked for exactly one.</para>
    /// </summary>
    [Fact]
    public void Copies_are_collated_once_never_twice()
    {
        var one = new PrintPreviewViewModel(ThreePageish(), "Trial Balance");
        int onePages = PdfPageCount(one.PdfBytes);
        Assert.True(onePages >= 1, "the single-copy preview rendered no pages — nothing below would be meaningful.");

        var three = new PrintPreviewViewModel(ThreePageish(), "Trial Balance") { Copies = 3 };
        int threePages = PdfPageCount(three.PdfBytes);

        Assert.True(threePages == onePages * 3,
            $"the three-copy preview carries {threePages} pages, not {onePages * 3}. The copy count is supposed "
          + "to be collated into the document by the renderer; if it is not, the spooler count below is wrong.");

        var built = PrintJobBuilder.Build(AnyDevice(), three.PdfBytes, three.ReportTitle);

        Assert.True(built.CanPrint, $"no job was built: {built.Message}");
        Assert.Equal(1, built.Job!.SpoolerCopies);
        Assert.Equal(PrintJobBuilder.SpoolerCopies, built.Job.SpoolerCopies);
        Assert.Equal(threePages, PdfPageCount(built.Job.Pdf));
    }

    /// <summary>
    /// The job carries the previewed bytes unchanged — what prints is what was previewed. A builder that
    /// re-rendered, re-collated or re-configured could produce a document the operator never approved.
    /// </summary>
    [Fact]
    public void Job_carries_the_previewed_bytes_byte_for_byte()
    {
        var preview = new PrintPreviewViewModel(ThreePageish(), "Trial Balance");
        var built = PrintJobBuilder.Build(AnyDevice(), preview.PdfBytes, preview.ReportTitle);

        Assert.True(built.CanPrint, built.Message);
        Assert.True(built.Job!.Pdf.SequenceEqual(preview.PdfBytes),
            "the job's bytes differ from the previewed bytes — the operator would print a document they never saw.");
    }

    /// <summary>The queue shows the document's own heading, not a generic label.</summary>
    [Fact]
    public void Job_name_is_the_preview_document_title()
    {
        var preview = new PrintPreviewViewModel(ThreePageish(), "Trial Balance");
        var built = PrintJobBuilder.Build(AnyDevice(), preview.PdfBytes, preview.ReportTitle);

        Assert.Equal("Trial Balance", built.Job!.JobName);
    }

    /// <summary>An untitled document still gets a usable job name rather than a blank one.</summary>
    [Fact]
    public void An_untitled_document_gets_the_fallback_job_name()
    {
        Assert.Equal(PrintJobBuilder.FallbackJobName, PrintJobBuilder.JobNameFrom(null));
        Assert.Equal(PrintJobBuilder.FallbackJobName, PrintJobBuilder.JobNameFrom("   "));
    }

    /// <summary>
    /// Control characters never reach a queue. A newline in a document title is a malformed <c>DOCINFO</c> and a
    /// malformed <c>lp -t</c> argument, not a cosmetic problem.
    /// </summary>
    [Fact]
    public void A_job_name_carries_no_control_characters_and_is_capped()
    {
        string dirty = "Tax\r\nInvoice\tNo. 42\0";
        string name = PrintJobBuilder.JobNameFrom(dirty);

        Assert.DoesNotContain(name, c => char.IsControl(c));
        Assert.Contains("Invoice", name);

        string overlong = new('X', PrintJobBuilder.MaxJobNameLength + 50);
        Assert.True(PrintJobBuilder.JobNameFrom(overlong).Length <= PrintJobBuilder.MaxJobNameLength);
    }

    /// <summary>
    /// With no printer there is no job — and the operator is told what to do instead. Returning a job with an
    /// empty printer name would send it into the void and report success.
    /// </summary>
    [Fact]
    public void No_printers_yields_no_job()
    {
        var preview = new PrintPreviewViewModel(ThreePageish(), "Trial Balance");

        var none = PrintJobBuilder.Build(null, preview.PdfBytes, preview.ReportTitle);
        Assert.False(none.CanPrint);
        Assert.Equal(PrintJobBuilder.NoPrinterMessage, none.Message);

        var blank = PrintJobBuilder.Build(
            new PrinterDevice("   ", IsDefault: false, PdfSubmissionMode.CupsNative),
            preview.PdfBytes, preview.ReportTitle);
        Assert.False(blank.CanPrint);
    }

    /// <summary>An empty document is refused rather than spooled as a zero-byte job.</summary>
    [Fact]
    public void An_empty_document_yields_no_job()
    {
        var built = PrintJobBuilder.Build(AnyDevice(), Array.Empty<byte>(), "Trial Balance");

        Assert.False(built.CanPrint);
        Assert.Equal(PrintJobBuilder.NothingToPrintMessage, built.Message);
    }

    /// <summary>
    /// The two submission modes state DIFFERENT things to the operator, and the RAW one states its limit. This
    /// notice is the operator's only warning that a RAW job can reach a device that cannot read PDF, so it is
    /// pinned rather than left to be tidied away.
    /// </summary>
    [Fact]
    public void The_submission_notice_states_the_platform_constraint()
    {
        var cups = new PrinterDevice("Office", IsDefault: true, PdfSubmissionMode.CupsNative);
        var raw = new PrinterDevice("Office", IsDefault: true, PdfSubmissionMode.WindowsRaw);

        Assert.Contains("CUPS", cups.SubmissionNotice, StringComparison.Ordinal);
        Assert.NotEqual(cups.SubmissionNotice, raw.SubmissionNotice);
        Assert.Contains("Save PDF", raw.SubmissionNotice, StringComparison.Ordinal);
    }

    /// <summary>The no-op submitter refuses honestly instead of reporting a success nothing produced.</summary>
    [Fact]
    public async Task The_unavailable_submitter_refuses_rather_than_reporting_success()
    {
        var result = await new UnavailablePrintJobSubmitter()
            .SubmitAsync(new PrintJob("Any", new byte[] { 1 }, 1, "Doc"));

        Assert.False(result.Submitted);
        Assert.Equal(UnavailablePrintJobSubmitter.Reason, result.Message);
    }
}
