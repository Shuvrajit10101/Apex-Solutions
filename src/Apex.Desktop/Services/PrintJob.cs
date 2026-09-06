using System;
using System.Threading.Tasks;

namespace Apex.Desktop.Services;

/// <summary>
/// A finished print job: exactly the bytes to spool, the queue to spool them to, how many times the spooler
/// should repeat them, and what the queue should call the job. Pure data — no Avalonia type, no OS handle — so
/// every decision that goes into a job can be asserted without a printer anywhere near the test.
/// </summary>
/// <param name="PrinterName">The queue name, verbatim as the OS gave it.</param>
/// <param name="Pdf">
/// The document bytes. These are the <b>exact</b> bytes the operator previewed and would have saved: what is
/// printed is what was previewed, the same discipline <c>PrintPreviewViewModel.SavePdf</c> already keeps.
/// </param>
/// <param name="SpoolerCopies">
/// 🔴 <b>Always 1, and that is a decision, not an oversight.</b> The copy count is already collated INTO
/// <paramref name="Pdf"/>: every renderer ends with <c>writer.RepeatAllPages(page.EffectiveCopies)</c>, so a
/// three-copy preview is a PDF that physically contains the document three times. Asking the spooler for three
/// copies of that file as well would put nine document sets on real paper. The count lives in exactly one
/// place — the preview's own <c>Copies</c> knob — and <see cref="PrintJobBuilder"/> is where that is enforced.
/// </param>
/// <param name="JobName">The job title the queue displays. Sanitised by <see cref="PrintJobBuilder"/>.</param>
public sealed record PrintJob(string PrinterName, byte[] Pdf, int SpoolerCopies, string JobName);

/// <summary>What the spooler said when it was handed a job.</summary>
/// <param name="Submitted">True only when the platform accepted the job for printing.</param>
/// <param name="Message">
/// The operator-facing sentence. On success it names the queue and the byte count; on failure it says why, in
/// the platform's own words, because "printing failed" tells an operator nothing they can act on.
/// </param>
public sealed record PrintJobResult(bool Submitted, string Message);

/// <summary>
/// Hands a built <see cref="PrintJob"/> to the operating system's spooler — the second half of census row 12.5,
/// and <b>the one call in this track that no CI runner can execute</b>.
///
/// <para>The seam is drawn here on purpose, as tightly as it can be drawn: everything upstream of this single
/// method — which printers are offered, which is selected, how many copies, which bytes, what the job is
/// called, what happens when there are no printers at all, and what the operator sees when submission fails —
/// is ordinary testable code and is tested against a recording fake. Only the hand-off to <c>winspool.drv</c> /
/// <c>lp</c> sits outside, mirroring <c>StorageProviderFilePathPicker</c>, whose own header states the rule:
/// translate the request, call the platform, hand back the answer.</para>
/// </summary>
public interface IPrintJobSubmitter
{
    /// <summary>
    /// Submits <paramref name="job"/> and reports what happened. <b>Never throws</b> — a missing spooler, a
    /// deleted queue or a refused job all come back as <see cref="PrintJobResult.Submitted"/> false with the
    /// reason, because an unhandled exception from a print button would take the shell down with it.
    /// </summary>
    Task<PrintJobResult> SubmitAsync(PrintJob job);
}

/// <summary>
/// The submitter used when no platform submitter has been injected. It prints nothing and says so plainly.
///
/// <para>It exists so the view model never holds a null submitter and never has to branch on one, and so a
/// headless test that forgets to substitute gets a truthful refusal instead of a <c>NullReferenceException</c>
/// or — far worse — a silent success that would let a broken build claim the row.</para>
/// </summary>
public sealed class UnavailablePrintJobSubmitter : IPrintJobSubmitter
{
    /// <summary>The single refusal wording, exposed so a test can pin it rather than re-typing the sentence.</summary>
    public const string Reason = "Printing is not available on this build. Use Save PDF instead.";

    /// <inheritdoc/>
    public Task<PrintJobResult> SubmitAsync(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Task.FromResult(new PrintJobResult(false, Reason));
    }
}
