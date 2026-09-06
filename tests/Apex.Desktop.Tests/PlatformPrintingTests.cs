using System;
using System.Threading.Tasks;
using Apex.Desktop.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 12.5, slice F1-S3 — the platform shim, tested for the only thing a printerless runner can
/// honestly assert: its CONTRACT.</b>
///
/// <para>🔴 <b>What this file deliberately does NOT do.</b> It never asserts that a printer exists, that one is
/// named anything, or that a job printed. The gate runs on <c>ubuntu-latest</c>, <c>windows-latest</c> and
/// <c>macos-latest</c> and none of them has a printer, so any such assertion would either fail everywhere or —
/// far worse — pass vacuously by comparing an empty result to an empty expectation, which is exactly how
/// <c>SpecialFolder.MyDocuments</c> returning "" on Linux got an assertion through this gate. What is asserted
/// instead is the promise both interfaces make and that everything above them relies on: <b>enumeration never
/// throws and submission never throws</b>, on every platform, with no printer, no spooler and no CUPS.</para>
///
/// <para>The single genuinely untestable thing in this track is the successful spool itself — one method,
/// <c>IPrintJobSubmitter.SubmitAsync</c>, against a real device. That is stated rather than papered over.</para>
/// </summary>
public sealed class PlatformPrintingTests
{
    /// <summary>
    /// The enumerator this machine gets never throws and never returns null — a printerless machine is a normal
    /// machine, and "no queues" is an answer. A throw here would take down a panel that is merely opening.
    /// </summary>
    [Fact]
    public void Listing_printers_never_throws_on_any_platform()
    {
        var devices = PlatformPrinting.Devices();
        Assert.NotNull(devices);

        var listed = devices.List();
        Assert.NotNull(listed);

        // Whatever the runner happens to have, every entry must be usable: a queue with a blank name could not
        // be submitted to, and a duplicate default would make the pre-selection non-deterministic.
        int defaults = 0;
        foreach (var device in listed)
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Name), "a queue was listed with no name.");
            Assert.False(string.IsNullOrWhiteSpace(device.SubmissionNotice));
            if (device.IsDefault) defaults++;
        }
        Assert.True(defaults <= 1, $"{defaults} queues claim to be the OS default; at most one may.");
    }

    /// <summary>Every platform's enumerator, not just this runner's, honours the never-throws contract.</summary>
    [Fact]
    public void Every_platform_enumerator_answers_rather_than_throwing()
    {
        Assert.NotNull(new NoPrinterDevices().List());
        Assert.NotNull(new CupsPrinterDevices().List());
        Assert.NotNull(new WindowsPrinterDevices().List());
    }

    /// <summary>
    /// Submission on a machine with no such queue reports a failure — it never throws, and it never claims
    /// success. A silent success is how a printing feature ships broken.
    /// </summary>
    [Fact]
    public async Task Submitting_to_a_queue_that_does_not_exist_reports_failure_without_throwing()
    {
        var job = new PrintJob(
            "Apex Nonexistent Queue " + Guid.NewGuid().ToString("N"),
            new byte[] { 0x25, 0x50, 0x44, 0x46 },   // "%PDF"
            1,
            "Contract test");

        var result = await PlatformPrinting.Submitter().SubmitAsync(job);

        Assert.NotNull(result);
        Assert.False(result.Submitted,
            "a job addressed to a queue that cannot exist was reported as printed.");
        Assert.False(string.IsNullOrWhiteSpace(result.Message),
            "a refusal with no reason tells the operator nothing they can act on.");
    }

    /// <summary>
    /// 🔴 <b>The spooler must not run on the thread that asked for it — the shell fires this from a key handler
    /// on the UI thread.</b>
    ///
    /// <para>Every call inside the Windows spool is a blocking win32 P/Invoke, and <c>OpenPrinter</c> alone can
    /// sit for tens of seconds on an offline or unreachable network queue. A <c>SubmitAsync</c> that built its
    /// result eagerly would do all of that on the caller's thread and freeze the whole shell, while
    /// <c>MainWindow.axaml.cs</c>'s Ctrl+A comment claims fire-and-forget prevents exactly that.</para>
    ///
    /// <para><b>Why this is deterministic rather than a thread race.</b> It reads the recorded thread at the
    /// instant <c>SubmitAsync</c> RETURNS, before awaiting. At that moment there are only two possibilities: the
    /// spool ran inline on this thread — in which case the recorded id is this thread's and the assertion fires —
    /// or it was queued, and <c>Task.Run</c> never executes a delegate on a thread that is still inside the
    /// caller, so the recorded id is either 0 or some other thread's. There is no interleaving that makes a
    /// defective build pass.</para>
    ///
    /// <para><b>Non-Windows is asserted, not skipped.</b> The gate demands 0 skipped on all three runners, and an
    /// unasserted branch would pass vacuously — the shape that let <c>SpecialFolder.MyDocuments</c> returning ""
    /// through this gate. Off Windows the submitter's documented contract is an immediate honest refusal that
    /// spools nothing at all, so that is what is asserted there.</para>
    /// </summary>
    [Fact]
    public async Task The_windows_spool_does_not_run_on_the_calling_thread()
    {
        var job = new PrintJob(
            "Apex Nonexistent Queue " + Guid.NewGuid().ToString("N"),
            new byte[] { 0x25, 0x50, 0x44, 0x46 },
            1,
            "Off-thread contract test");

        var submitter = new WindowsRawPrintJobSubmitter();
        WindowsRawPrintJobSubmitter.LastSpoolThreadId = 0;

        int caller = Environment.CurrentManagedThreadId;
        var pending = submitter.SubmitAsync(job);

        // Read BEFORE awaiting — this is the whole point of the test.
        int spooledOn = System.Threading.Volatile.Read(ref WindowsRawPrintJobSubmitter.LastSpoolThreadId);
        Assert.True(spooledOn != caller,
            "the Windows spooler ran synchronously on the calling thread. The shell fires this from the Ctrl+A "
          + "key handler on the UI thread, so an offline printer would freeze the entire application.");

        var result = await pending;
        Assert.NotNull(result);

        if (!OperatingSystem.IsWindows())
        {
            // The documented off-Windows contract, asserted so this test can never pass by doing nothing.
            Assert.False(result.Submitted);
            Assert.Equal(UnavailablePrintJobSubmitter.Reason, result.Message);
            Assert.Equal(0, System.Threading.Volatile.Read(ref WindowsRawPrintJobSubmitter.LastSpoolThreadId));
        }
        else
        {
            // On Windows the spool really ran — against a queue that cannot exist, so it must have refused, and
            // it must have done so on some thread that is not this one.
            Assert.False(result.Submitted);
            int ran = System.Threading.Volatile.Read(ref WindowsRawPrintJobSubmitter.LastSpoolThreadId);
            Assert.True(ran != 0, "the Windows spool never ran at all.");
            Assert.True(ran != caller, "the Windows spool ran on the calling thread.");
        }
    }

    /// <summary>
    /// The chosen shim matches the platform, and no platform is left without one. This is the wiring assertion
    /// that would catch a shim silently dropped from the factory.
    /// </summary>
    [Fact]
    public void The_shim_matches_the_platform_it_runs_on()
    {
        var devices = PlatformPrinting.Devices();
        var submitter = PlatformPrinting.Submitter();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsPrinterDevices>(devices);
            Assert.IsType<WindowsRawPrintJobSubmitter>(submitter);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.IsType<CupsPrinterDevices>(devices);
            Assert.IsType<CupsPrintJobSubmitter>(submitter);
        }
        else
        {
            Assert.IsType<NoPrinterDevices>(devices);
            Assert.IsType<UnavailablePrintJobSubmitter>(submitter);
        }
    }
}
