using System;
using System.Text;

namespace Apex.Desktop.Services;

/// <summary>The outcome of asking for a job to be built: either a job, or the reason there is none.</summary>
/// <param name="Job">The job to spool, or <c>null</c> when one could not be built.</param>
/// <param name="Message">
/// Empty when a job was built; otherwise the operator-facing reason, which the panel shows verbatim.
/// </param>
public sealed record PrintJobBuildResult(PrintJob? Job, string Message)
{
    /// <summary>True when there is a job to hand to the spooler.</summary>
    public bool CanPrint => Job is not null;
}

/// <summary>
/// Turns "this document, on that printer" into a <see cref="PrintJob"/>. Pure and static: it takes bytes and a
/// device, touches no OS, no disk and no clock, and is therefore fully testable on a runner with no printer.
///
/// <para><b>This is the half of census row 12.5 that carries the decisions that reach paper</b> — which bytes
/// get spooled, how many times, and under what name — so it is deliberately separated from the platform shim
/// that merely delivers them.</para>
/// </summary>
public static class PrintJobBuilder
{
    /// <summary>
    /// 🔴 <b>The copy count asked of the spooler is ALWAYS one.</b>
    ///
    /// <para>Copies are collated into the document itself: each of the five renderers a preview can use
    /// (<c>ReportPdf</c>, <c>VoucherPdf</c>, <c>InvoicePdf</c>, <c>PosReceiptPdf</c>, <c>PayslipPdf</c>) ends with
    /// <c>writer.RepeatAllPages(page.EffectiveCopies)</c> — the certificate renderers
    /// (<c>Form16APdf</c>/<c>Form27APdf</c>/<c>Form27DPdf</c>) honour no copy count and are not previewable, so
    /// they never reach here — so a preview set to three copies IS a PDF that
    /// contains the document three times. Passing that same three to the spooler as well would print nine
    /// document sets — N², on real paper, discovered only after it had been printed. The count therefore has
    /// exactly one home, the preview's own <c>Copies</c> knob, and this constant is the statement of that.
    /// <c>PrintJobBuilderTests.Copies_are_collated_once_never_twice</c> holds it.</para>
    /// </summary>
    public const int SpoolerCopies = 1;

    /// <summary>The job name used when the document has no title of its own.</summary>
    public const string FallbackJobName = "Apex Solutions document";

    /// <summary>Shown when the machine offers no printer at all, or none has been selected.</summary>
    public const string NoPrinterMessage =
        "No printer is selected. If no printers are listed, this machine has none installed — use Save PDF.";

    /// <summary>Shown when there is a printer but the document rendered to nothing.</summary>
    public const string NothingToPrintMessage = "There is nothing to print — the document rendered no pages.";

    /// <summary>The longest job name handed to a queue; longer titles are truncated to this many characters.</summary>
    public const int MaxJobNameLength = 120;

    /// <summary>
    /// Builds the job for <paramref name="device"/> from the previewed <paramref name="pdf"/> bytes.
    /// </summary>
    /// <param name="device">The selected queue, or <c>null</c> when none is selected / none exists.</param>
    /// <param name="pdf">
    /// The rendered document. These bytes are carried into the job <b>unchanged and uncopied in spirit</b>: what
    /// prints is what was previewed, so the builder never re-renders and never re-collates.
    /// </param>
    /// <param name="documentTitle">The preview's own heading, used as the queue's job name.</param>
    public static PrintJobBuildResult Build(PrinterDevice? device, byte[]? pdf, string? documentTitle)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Name))
            return new PrintJobBuildResult(null, NoPrinterMessage);

        if (pdf is null || pdf.Length == 0)
            return new PrintJobBuildResult(null, NothingToPrintMessage);

        return new PrintJobBuildResult(
            new PrintJob(device.Name, pdf, SpoolerCopies, JobNameFrom(documentTitle)),
            string.Empty);
    }

    /// <summary>
    /// The queue's job name, derived from the document heading.
    ///
    /// <para>Control characters are stripped and the result is capped, because a job name travels into
    /// <c>lp -t</c> and into a Windows <c>DOCINFO</c> — a stray newline or NUL in a document title is a
    /// malformed job, not a malformed label. Arguments are passed to the platform through
    /// <c>ProcessStartInfo.ArgumentList</c> (never a shell command line), so this is hygiene, not the only
    /// defence.</para>
    /// </summary>
    public static string JobNameFrom(string? documentTitle)
    {
        if (string.IsNullOrWhiteSpace(documentTitle)) return FallbackJobName;

        var clean = new StringBuilder(documentTitle!.Length);
        foreach (char c in documentTitle!)
            clean.Append(char.IsControl(c) ? ' ' : c);

        string name = clean.ToString().Trim();
        if (name.Length == 0) return FallbackJobName;
        return name.Length <= MaxJobNameLength ? name : name[..MaxJobNameLength].TrimEnd();
    }
}
