using System.Collections.Generic;

namespace Apex.Desktop.Services;

/// <summary>
/// How a print job's PDF bytes reach a given device — <b>the mechanism, not a promise</b>.
///
/// <para>🔴 This is deliberately NOT a <c>bool AcceptsPdf</c>. On Windows there is no dependency-free way to
/// ask a queue whether its device understands PDF, and a boolean would have forced us to invent an answer.
/// Naming the mechanism instead lets the panel tell the operator exactly what will happen to the bytes and
/// what to do when it is not enough, without claiming knowledge we do not have.</para>
/// </summary>
public enum PdfSubmissionMode
{
    /// <summary>
    /// The queue is a CUPS queue and PDF is CUPS's own native print format: the scheduler converts the document
    /// to whatever the device speaks. Submitting a PDF here works on any printer CUPS can drive.
    /// </summary>
    CupsNative,

    /// <summary>
    /// The bytes are spooled to a Windows print queue as a RAW job — handed to the device unchanged, with no
    /// rendering step in between. <b>That works only where the device itself understands PDF or PostScript</b>
    /// (most networked office printers do; most cheap USB host-based printers do not). We cannot tell which
    /// from here, so the panel says so and the Save-PDF fallback stays available on every device.
    /// </summary>
    WindowsRaw,
}

/// <summary>
/// One physical print queue the operating system is offering, named exactly as the OS names it.
/// </summary>
/// <param name="Name">The queue name to submit against. This is what the spooler is asked for, verbatim.</param>
/// <param name="IsDefault">Whether the OS reports this as the default queue. At most one device says true.</param>
/// <param name="Submission">How this platform will hand the PDF over — see <see cref="PdfSubmissionMode"/>.</param>
public sealed record PrinterDevice(string Name, bool IsDefault, PdfSubmissionMode Submission)
{
    /// <summary>
    /// The one-line constraint statement shown beside the selected printer. It is the operator's only warning
    /// that a RAW submission can reach a device that cannot read it, so it must never be dropped to tidy the
    /// panel — <c>PrinterSelectionViewModelTests.Both_submission_notices_are_pinned_word_for_word</c> holds both
    /// wordings verbatim, and specifically the clause of each that carries the warning.
    ///
    /// <para>🔴 That sentence used to name no test and claim only that "PrinterSelectionViewModelTests pins both
    /// wordings". Nothing pinned them: the one test that read this property merely asserted the two notices were
    /// DIFFERENT from each other, which stays green if both are replaced with "OK" and "Fine". A comment that
    /// names a guard nobody wrote is worse than an unguarded property, because the next reader stops looking.</para>
    /// </summary>
    public string SubmissionNotice => Submission switch
    {
        PdfSubmissionMode.CupsNative =>
            "This printer is driven by CUPS, which accepts PDF directly — the job prints as previewed.",
        _ =>
            "The PDF is spooled to this printer unchanged. Printers that read PDF or PostScript print it as "
          + "previewed; others will not. If nothing comes out, use Save PDF and print from your PDF reader.",
    };
}

/// <summary>
/// Enumeration of the physical print queues on this machine — the first half of census row 12.5.
///
/// <para><b>Why this is an interface.</b> Avalonia 12.0.5 has no printing API of any kind (measured over every
/// shipped ref assembly: the only <c>Print</c> identifiers are <c>Key.Print</c>/<c>Key.PrintScreen</c>, the
/// compiler's record <c>PrintMembers</c> helper, <c>PrettyPrintStringAttribute</c>, and a
/// <c>[System.Printing, PublicKey=…]</c> friend-assembly blob in the metadata heap — not an API). So there is
/// nothing to wrap, only a platform to shim; and <b>no CI runner has a printer</b>, so the shim must be
/// substitutable or every test above it would be untestable. This is the same seam shape as
/// <see cref="IFilePathPicker"/>, for the same reason its header gives: a capability no test can reach is a
/// capability no one can prove is reachable.</para>
/// </summary>
public interface IPrinterDevices
{
    /// <summary>
    /// The queues this machine offers, in the order the OS reported them. <b>Never throws</b> — a machine with
    /// no printers, no spooler service, or no <c>lpstat</c> at all returns an empty list, because "there is no
    /// printer" is an answer, not a failure. Callers must handle empty.
    ///
    /// <para>⚠️ <b>Synchronous and BLOCKING, and the panel calls it on the UI thread.</b> Stated here rather
    /// than discovered later: the CUPS shim runs <c>lpstat</c> twice and the Windows shim calls
    /// <c>EnumPrinters</c>, which is known to stall while it contacts unreachable network queues.</para>
    ///
    /// <para><b>The CUPS worst case, spelled out because "bounded" was previously asserted and not true.</b> Each
    /// <c>lpstat</c> costs up to <c>CupsPrinterDevices.TimeoutMs</c> waiting for the process to exit plus a
    /// second such window draining its pipes, and <c>List()</c> runs two of them — so four windows, ~16 s at the
    /// current 4 s setting, is the ceiling. It really is a ceiling now: the earlier shape read stdout to end
    /// BEFORE waiting, which a child that filled the stderr buffer could block forever, and the timeout was
    /// never reached at all. On a healthy machine this is milliseconds; on a machine with a broken spooler or a
    /// dead print server, opening the Printer column can visibly hang for those seconds. It is bounded and it
    /// cannot throw, so it degrades rather than breaks — but it is NOT free,
    /// and that is why <c>PrinterSelectionViewModel</c> enumerates exactly once when the column opens instead of
    /// re-listing as the operator arrows through the list. The submission half, which can block for very much
    /// longer, is genuinely off-thread — see <c>WindowsRawPrintJobSubmitter.SubmitAsync</c>.</para>
    /// </summary>
    IReadOnlyList<PrinterDevice> List();
}
