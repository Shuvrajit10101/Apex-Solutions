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
    /// panel — <c>PrinterSelectionViewModelTests</c> pins both wordings.
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
    /// </summary>
    IReadOnlyList<PrinterDevice> List();
}
