using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Desktop.Services;

/// <summary>
/// Chooses the printing shim for the machine the app is actually running on (census row 12.5).
///
/// <para><b>Why a shim at all.</b> Measured over every ref assembly shipped in Avalonia 12.0.5, the complete
/// set of <c>Print</c> identifiers is <c>Key.Print</c>, <c>Key.PrintScreen</c>, the C# compiler's record
/// <c>PrintMembers</c> helper, <c>PrettyPrintStringAttribute</c>, and a <c>[System.Printing, PublicKey=…]</c>
/// friend-assembly blob sitting in the metadata heap. There is no dialog, no queue, no device enumeration and
/// no provider interface. Avalonia offers this project nothing for printing, so there is nothing to wrap — only
/// a platform to shim.</para>
///
/// <para><b>What each platform actually does, stated rather than implied.</b>
/// <list type="bullet">
///   <item><b>Linux / macOS</b> — CUPS. Queues come from <c>lpstat -a</c> and the default from <c>lpstat -d</c>;
///     jobs go to <c>lp</c> on standard input. PDF is CUPS's own native print format, so the scheduler converts
///     it for whatever the device speaks. This arm is fully honest on any printer CUPS can drive.</item>
///   <item><b>Windows</b> — the spooler, through <c>winspool.drv</c>. Queues come from <c>EnumPrinters</c> and
///     the default from <c>GetDefaultPrinter</c>; jobs are spooled with datatype <c>RAW</c>, which hands the
///     PDF to the device unchanged. <b>That works only where the device itself reads PDF or PostScript.</b>
///     There is no dependency-free way to render a PDF onto a GDI device, and we will not render the job a
///     second way to get one: the operator approved specific bytes in the preview, and printing different bytes
///     than were previewed would break the discipline <c>PrintPreviewViewModel.SavePdf</c> already keeps. The
///     panel therefore states the constraint on screen and Save PDF remains available on every device.</item>
///   <item><b>Anything else</b> — no queues and an honest refusal, never a silent success.</item>
/// </list></para>
/// </summary>
public static class PlatformPrinting
{
    /// <summary>The enumerator for this OS. Never null; on an unknown platform it lists nothing.</summary>
    public static IPrinterDevices Devices()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPrinterDevices();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new CupsPrinterDevices();
        return new NoPrinterDevices();
    }

    /// <summary>The submitter for this OS. Never null; on an unknown platform it refuses and says so.</summary>
    public static IPrintJobSubmitter Submitter()
    {
        if (OperatingSystem.IsWindows()) return new WindowsRawPrintJobSubmitter();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new CupsPrintJobSubmitter();
        return new UnavailablePrintJobSubmitter();
    }
}

/// <summary>A platform with no printing support: no queues, ever. Distinct from "the machine has none".</summary>
public sealed class NoPrinterDevices : IPrinterDevices
{
    /// <inheritdoc/>
    public IReadOnlyList<PrinterDevice> List() => Array.Empty<PrinterDevice>();
}

// ===================================================================================== CUPS (Linux / macOS)

/// <summary>
/// Enumerates CUPS queues via <c>lpstat</c>. Kept deliberately thin — run the tool, parse the names, hand them
/// back — mirroring <c>StorageProviderFilePathPicker</c>'s own statement of that rule.
/// </summary>
public sealed class CupsPrinterDevices : IPrinterDevices
{
    /// <summary>How long <c>lpstat</c> is given before we conclude the machine has no usable CUPS.</summary>
    internal const int TimeoutMs = 4000;

    /// <inheritdoc/>
    public IReadOnlyList<PrinterDevice> List()
    {
        // "there is no printer" is an answer, not a failure: no lpstat, no cupsd, no queues — all empty list.
        string listing = RunTool("lpstat", "-a");
        if (listing.Length == 0) return Array.Empty<PrinterDevice>();

        string defaultQueue = DefaultQueueName();

        var devices = new List<PrinterDevice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in listing.Split('\n'))
        {
            // `lpstat -a` prints "<queue> accepting requests since <date>"; the queue name is the first token.
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            int space = trimmed.IndexOf(' ');
            string name = space < 0 ? trimmed : trimmed[..space];
            if (name.Length == 0 || !seen.Add(name)) continue;
            devices.Add(new PrinterDevice(
                name,
                string.Equals(name, defaultQueue, StringComparison.Ordinal),
                PdfSubmissionMode.CupsNative));
        }
        return devices;
    }

    private static string DefaultQueueName()
    {
        // "system default destination: <queue>", or "no system default destination".
        string text = RunTool("lpstat", "-d").Trim();
        int colon = text.IndexOf(':');
        return colon < 0 ? string.Empty : text[(colon + 1)..].Trim();
    }

    /// <summary>
    /// Runs a tool and returns its stdout, or an empty string for ANY failure at all.
    ///
    /// <para>🔴 <b>BOTH pipes are drained CONCURRENTLY, and that is the whole point of this shape.</b> The
    /// previous version redirected stderr and then did a blocking <c>StandardOutput.ReadToEnd()</c> BEFORE
    /// <c>WaitForExit</c>. A child that fills the stderr pipe buffer (4 KB on Windows, 64 KB on Linux) blocks
    /// writing to it, so it never exits and never closes stdout, so <c>ReadToEnd</c> never returns — and the
    /// timeout on the next line is never reached, because control never gets there. <c>IPrinterDevices.List()</c>
    /// promises a bounded enumeration and the panel calls it ON THE UI THREAD, so that shape could freeze the
    /// whole shell forever on a chatty or broken <c>lpstat</c>. A frozen shell on a printer enumeration is worse
    /// than no printer list.</para>
    ///
    /// <para>The async readers keep both buffers empty so the child can always make progress; the timed
    /// <c>WaitForExit(int)</c> overload deliberately does NOT wait on those readers, so it is a real bound.
    /// <c>PlatformPrintingTests.Enumeration_survives_a_tool_that_floods_stderr</c> runs a child that does
    /// exactly this and fails rather than hanging.</para>
    /// </summary>
    internal static string RunTool(string fileName, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in arguments) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null) return string.Empty;

            // Start draining BEFORE waiting. stderr is read solely to keep its buffer empty — nothing reads the
            // text — but it must be read, or it is the pipe that deadlocks us.
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Observe(stdout);
            Observe(stderr);

            if (!process.WaitForExit(CupsPrinterDevices.TimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return string.Empty;
            }

            // The child has exited, so both pipes are at EOF and these complete at once. The bound is kept
            // anyway: a grandchild that inherited the write handle can hold a pipe open after its parent dies,
            // and that is the same unbounded wait wearing a different hat.
            if (!Task.WaitAll(new Task[] { stdout, stderr }, CupsPrinterDevices.TimeoutMs))
                return string.Empty;

            return process.ExitCode == 0 ? stdout.Result : string.Empty;
        }
        catch
        {
            // No such executable, no permission, no /bin at all — every one of them means "no queues".
            return string.Empty;
        }
    }

    /// <summary>
    /// Marks a drain task's exception as observed. The drains outlive us on every abandonment path (a killed
    /// child, a blown timeout), and an unobserved faulted <see cref="Task"/> is a finalizer-thread surprise we
    /// do not need in a panel that is merely listing printers.
    /// </summary>
    private static void Observe(Task task) =>
        task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}

/// <summary>Hands a PDF to <c>lp</c> on standard input. CUPS takes PDF as its native print format.</summary>
public sealed class CupsPrintJobSubmitter : IPrintJobSubmitter
{
    /// <summary>How long <c>lp</c> is given to accept the job before we report that it did not.</summary>
    internal const int SubmitTimeoutMs = 15000;

    /// <inheritdoc/>
    public async Task<PrintJobResult> SubmitAsync(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        try
        {
            var psi = new ProcessStartInfo("lp")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // ArgumentList, never a command line: the job name comes from a document title, and no title should
            // ever be able to become an argument of its own.
            psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(job.PrinterName);
            psi.ArgumentList.Add("-n"); psi.ArgumentList.Add(job.SpoolerCopies.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(job.JobName);
            psi.ArgumentList.Add("--"); psi.ArgumentList.Add("-");   // read the document from stdin

            using var process = Process.Start(psi);
            if (process is null) return new PrintJobResult(false, "Could not start lp — is CUPS installed?");

            // 🔴 Bounded, not open-ended — and the bound covers the READS as well as the wait. An `lp` that
            // cannot reach a scheduler can sit there, and an unbounded wait inside a UI action — or inside the
            // CI contract test, on a runner with no CUPS at all — would hang rather than fail.
            using var timeout = new CancellationTokenSource(SubmitTimeoutMs);

            // 🔴 BOTH pipes are drained, and the drain STARTS BEFORE the document is written to stdin. This is
            // the same defect the enumerator's RunTool carried, with the roles swapped: stdout was redirected
            // and NEVER read while stderr was read to end. `lp` writes "request id is …" to stdout, and on a
            // multi-megabyte PDF it can say it while we are still feeding stdin — a full, unread stdout pipe
            // blocks the child, our WriteAsync blocks behind it, and neither side ever moves. Reading stderr to
            // end BEFORE WaitForExit had the identical hole: nothing could close stderr until the child exited,
            // and the child could not exit while it was blocked on stdout.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await using (var stdin = process.StandardInput.BaseStream)
                    await stdin.WriteAsync(job.Pdf, timeout.Token).ConfigureAwait(false);

                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new PrintJobResult(false, $"{job.PrinterName} did not accept the job in time.");
            }

            // The child has exited, so both readers are at EOF. A reader that faulted tells us nothing the exit
            // code does not, so it costs us only the wording of a refusal we are reporting anyway.
            string error = string.Empty;
            try { error = await stderrTask.ConfigureAwait(false); } catch { /* exit code decides */ }
            try { _ = await stdoutTask.ConfigureAwait(false); } catch { /* drained, not read */ }

            return process.ExitCode == 0
                ? new PrintJobResult(true,
                    $"Sent \"{job.JobName}\" ({job.Pdf.Length:#,0} bytes) to {job.PrinterName}.")
                : new PrintJobResult(false,
                    $"{job.PrinterName} refused the job: {(error.Trim().Length > 0 ? error.Trim() : "lp exited " + process.ExitCode)}");
        }
        catch (Exception ex)
        {
            return new PrintJobResult(false, "Could not print: " + ex.Message);
        }
    }
}

// ============================================================================================ Windows spooler

/// <summary>Enumerates Windows print queues through <c>winspool.drv</c>. No package dependency, no rasteriser.</summary>
public sealed class WindowsPrinterDevices : IPrinterDevices
{
    private const uint PrinterEnumLocal = 0x00000002;
    private const uint PrinterEnumConnections = 0x00000004;
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags, string? name, uint level, IntPtr buffer, uint bufferSize,
        out uint bytesNeeded, out uint countReturned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? buffer, ref int size);

    /// <inheritdoc/>
    public IReadOnlyList<PrinterDevice> List()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<PrinterDevice>();

        IntPtr buffer = IntPtr.Zero;
        try
        {
            const uint flags = PrinterEnumLocal | PrinterEnumConnections;

            // The documented two-call protocol: ask with a zero buffer to learn the size, then ask again.
            if (EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out uint needed, out _))
                return Array.Empty<PrinterDevice>();          // succeeded with nothing to report: no queues
            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || needed == 0)
                return Array.Empty<PrinterDevice>();

            buffer = Marshal.AllocHGlobal((int)needed);
            if (!EnumPrinters(flags, null, 4, buffer, needed, out _, out uint count) || count == 0)
                return Array.Empty<PrinterDevice>();

            string defaultName = DefaultPrinterName();
            int stride = Marshal.SizeOf<PrinterInfo4>();
            var devices = new List<PrinterDevice>((int)count);
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + (i * stride));
                string? name = info.pPrinterName == IntPtr.Zero ? null : Marshal.PtrToStringUni(info.pPrinterName);
                if (string.IsNullOrWhiteSpace(name)) continue;
                devices.Add(new PrinterDevice(
                    name!,
                    string.Equals(name, defaultName, StringComparison.Ordinal),
                    PdfSubmissionMode.WindowsRaw));
            }
            return devices;
        }
        catch
        {
            // A stopped spooler service, a denied call, a missing winspool: all of them mean "no queues", and
            // none of them may take the shell down from a panel that is merely opening.
            return Array.Empty<PrinterDevice>();
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DefaultPrinterName()
    {
        try
        {
            int size = 0;
            GetDefaultPrinter(null, ref size);
            if (size <= 0) return string.Empty;
            var sb = new StringBuilder(size);
            return GetDefaultPrinter(sb, ref size) ? sb.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Spools the PDF bytes to a Windows queue as a RAW job — the device receives them unchanged.
///
/// <para>🔴 <b>The limit, stated where the code is rather than only in the panel.</b> RAW means "no rendering
/// step": the printer itself must understand PDF (or PostScript). Networked office printers generally do;
/// host-based USB printers generally do not, and for those this call succeeds at the spooler and produces
/// nothing useful at the device. There is no dependency-free way to do better — rendering the PDF onto a GDI
/// <c>PrintDocument</c> needs a native rasteriser, and re-rendering the document a second way would print bytes
/// the operator never previewed. <c>PrinterSelectionViewModel</c> shows the constraint before the job is sent
/// and Save PDF stays available on every device.</para>
/// </summary>
public sealed class WindowsRawPrintJobSubmitter : IPrintJobSubmitter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDatatype;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    /// <summary>
    /// Deletes the spool file of the job currently open on this handle. This is the ONLY way to walk away from a
    /// half-written job without leaving it in the queue — see <see cref="Spool"/>'s unwind.
    /// </summary>
    [DllImport("winspool.drv", EntryPoint = "AbortPrinter", SetLastError = true)]
    private static extern bool AbortPrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr handle, int level, DocInfo1 docInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);

    /// <summary>
    /// The managed thread <see cref="Spool"/> last ran on, or 0 if it has never run. Test-visible ONLY, and it
    /// exists to hold the property below: <c>PlatformPrintingTests</c> reads it the instant
    /// <see cref="SubmitAsync"/> returns, and a spool that had happened on the caller's own thread would be
    /// caught there. Never read by shipped code.
    /// </summary>
    internal static int LastSpoolThreadId;

    /// <inheritdoc/>
    public Task<PrintJobResult> SubmitAsync(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new PrintJobResult(false, UnavailablePrintJobSubmitter.Reason));

        // 🔴 Task.Run, NOT Task.FromResult(Spool(job)). Every call below is a blocking win32 P/Invoke, and
        // OpenPrinter alone can sit for tens of seconds on an offline or unreachable network queue. Building the
        // result eagerly would run the whole spool on the CALLER's thread — which is the UI thread, because the
        // shell fires this from the Ctrl+A handler — so the shell would freeze for exactly as long as the printer
        // took, while MainWindow.axaml.cs's own comment claims fire-and-forget prevents that. It only prevents it
        // if the work is actually off-thread. This is the line that makes that claim true.
        return Task.Run(() => Spool(job));
    }

    private static PrintJobResult Spool(PrintJob job)
    {
        LastSpoolThreadId = Environment.CurrentManagedThreadId;
        IntPtr printer = IntPtr.Zero;
        IntPtr unmanaged = IntPtr.Zero;
        bool docStarted = false, pageStarted = false;

        // 🔴 THE INVARIANT: once a document is open on the handle, the unwind COMMITS it only if we reach the
        // one success return below. It is armed the instant StartDocPrinter succeeds and disarmed on exactly one
        // line, so a new early return added here later cannot silently start committing half a document — which
        // is how the short-write path came to do it in the first place.
        bool abandon = false;
        try
        {
            if (!OpenPrinter(job.PrinterName, out printer, IntPtr.Zero) || printer == IntPtr.Zero)
                return new PrintJobResult(false,
                    $"Could not open {job.PrinterName} (error {Marshal.GetLastWin32Error()}).");

            var doc = new DocInfo1 { pDocName = job.JobName, pOutputFile = null, pDatatype = "RAW" };
            if (StartDocPrinter(printer, 1, doc) == 0)
                return new PrintJobResult(false,
                    $"{job.PrinterName} refused the job (error {Marshal.GetLastWin32Error()}).");
            docStarted = true;
            abandon = true;      // armed — see the invariant above; disarmed only on the success return

            if (!StartPagePrinter(printer))
                return new PrintJobResult(false,
                    $"{job.PrinterName} refused the job (error {Marshal.GetLastWin32Error()}).");
            pageStarted = true;

            unmanaged = Marshal.AllocHGlobal(job.Pdf.Length);
            Marshal.Copy(job.Pdf, 0, unmanaged, job.Pdf.Length);
            if (!WritePrinter(printer, unmanaged, job.Pdf.Length, out int written) || written != job.Pdf.Length)
            {
                // 🔴 A SHORT WRITE MUST NOT BECOME A PRINTED DOCUMENT. Returning here used to fall straight into
                // an unwind that called EndPagePrinter + EndDocPrinter — which is precisely the sequence that
                // COMMITS the job to the queue. The operator was told "only 4,096 of 812,340 bytes reached
                // Accounts Laser" while the printer went ahead and produced a truncated document from those
                // 4,096 bytes: the paper disagreeing with both the message on screen and the PDF that was
                // previewed and posted. Half a tax invoice is worse than no tax invoice.
                return new PrintJobResult(false,
                    $"Only {written:#,0} of {job.Pdf.Length:#,0} bytes reached {job.PrinterName}. "
                  + "Nothing was printed — the job was cancelled at the queue.");
            }

            // 🔴 THE ONLY LINE THAT AUTHORISES A COMMIT. Every byte of the document reached the spooler, so the
            // unwind may now close the page and the document rather than delete them.
            abandon = false;
            return new PrintJobResult(true,
                $"Sent \"{job.JobName}\" ({job.Pdf.Length:#,0} bytes) to {job.PrinterName}.");
        }
        catch (Exception ex)
        {
            // `abandon` is left exactly as the invariant set it: true if the document was open, false if the
            // throw happened before StartDocPrinter. An incomplete document is not a job and must not become one.
            return new PrintJobResult(false, "Could not print: " + ex.Message);
        }
        finally
        {
            // Unwound in the reverse order it was built, and each step guarded: a half-started job must still be
            // closed or the queue keeps the handle.
            if (unmanaged != IntPtr.Zero) { try { Marshal.FreeHGlobal(unmanaged); } catch { } }

            if (docStarted && abandon)
            {
                // AbortPrinter deletes the spool file of the job open on this handle. EndPagePrinter and
                // EndDocPrinter are deliberately NOT called on this path: they are the commit, and there is
                // nothing here worth committing. The handle is still closed below, so the queue is not leaked.
                //
                // This arm also covers the StartPagePrinter refusal, which the previous unwind committed as a
                // document with NO page in it — an empty job left sitting in a queue the operator had just been
                // told refused the work.
                try { AbortPrinter(printer); } catch { }
            }
            else
            {
                if (pageStarted) { try { EndPagePrinter(printer); } catch { } }
                if (docStarted) { try { EndDocPrinter(printer); } catch { } }
            }

            if (printer != IntPtr.Zero) { try { ClosePrinter(printer); } catch { } }
        }
    }
}
