using System;
using System.Diagnostics;

namespace Apex.Desktop.Services;

/// <summary>
/// The ONE seam through which this application hands a URI (or a file path) to the operating system's own
/// default handler — a <c>mailto:</c> to the mail client, an <c>https://</c> to the browser.
///
/// <para>🔴 <b>Why this interface exists at all.</b> Before it, <c>EmailComposeViewModel.MailtoUri</c> was a
/// fully-computed, change-notified, twice-tested property with <b>zero consumers anywhere in
/// <c>src/</c></b> — no XAML binding, no command, and <c>Process.Start</c> appeared nowhere in the
/// repository. The class documented that the mailto "opens the OS mail client"; nothing in the shipped
/// application opened anything. That is the dead-capability shape this project has already filed twice
/// (<c>CostReports.BuildLedgerBreakup</c>; <c>MultiAccountPrintViewModel</c>, census T2-40): a capability
/// no user can reach is not a feature, and its tests are green for a thing that does not happen.</para>
///
/// <para><b>Why an interface and not a bare static call.</b> Every CI runner this gate crosses (ubuntu,
/// windows, macos) is headless and has no registered mail client, no browser and — on ubuntu — frequently
/// no <c>xdg-open</c> at all. A test that exercised the real launcher would either hang, spawn a stray
/// process, or fail for the environment rather than for the code. The seam lets the shell wire the real
/// implementation while every test asserts against a recording double, so the ASSERTION is "the URI the
/// application would hand out", which is the part that can actually be wrong.</para>
/// </summary>
public interface IExternalLauncher
{
    /// <summary>
    /// Hands <paramref name="target"/> to the OS default handler. Returns true when the hand-off was
    /// accepted, false when it was refused (blank target, no handler registered, launch threw). It NEVER
    /// throws: a missing mail client is an ordinary condition on a workstation, not an exception.
    /// </summary>
    bool Open(string target);
}

/// <summary>
/// The real launcher: <see cref="Process.Start(ProcessStartInfo)"/> with <c>UseShellExecute = true</c>, which
/// is what routes a <c>mailto:</c> through the OS handler rather than trying to execute it as a program.
///
/// <para><b>Cross-platform.</b> On Windows this goes to ShellExecute; on macOS .NET maps it to <c>open</c>;
/// on Linux to <c>xdg-open</c>. Any of the three can be absent or refuse, so every failure mode is caught
/// and reported as <c>false</c> — the panel then says so in its status line instead of the window dying.</para>
/// </summary>
public sealed class ShellExternalLauncher : IExternalLauncher
{
    /// <summary>The shared instance the shell wires by default.</summary>
    public static readonly ShellExternalLauncher Default = new();

    public bool Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;

        try
        {
            var started = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            // A null return is legal and means "the shell handled it without giving us a process handle"
            // (the common case for a mailto on Windows when the client is already running). That is a
            // SUCCESS, not a failure — treating it as false would make the panel lie about what happened.
            started?.Dispose();
            return true;
        }
        catch (Exception)
        {
            // No handler registered, xdg-open absent, sandbox refusal, malformed URI. All ordinary.
            return false;
        }
    }
}
