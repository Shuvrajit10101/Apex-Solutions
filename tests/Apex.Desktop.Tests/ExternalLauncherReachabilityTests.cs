using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE mailto: THAT NOTHING OPENED.</b>
///
/// <para><b>The defect this file locks shut.</b> <c>EmailComposeViewModel.MailtoUri</c> shipped as a fully
/// computed property, change-notified on four separate fields, documented as the thing that "opens the OS
/// mail client", and covered by two passing tests. Measured across the whole repository, it had
/// <b>zero consumers in <c>src/</c></b>: no XAML binding, no command, and <c>Process.Start</c> appeared
/// <b>nowhere in the repository at all</b>. Every green test in
/// <see cref="EmailComposeViewModelTests"/> was green for a string the operator could never cause to be
/// used. That is the same shape as <c>CostReports.BuildLedgerBreakup</c> (fully tested, zero <c>src/</c>
/// callers) and <c>MultiAccountPrintViewModel</c> (~432 lines, zero references, filed as census T2-40).</para>
///
/// <para><b>Why these tests look at the realised visual tree and at the real key tunnel.</b> A test that
/// asserted <c>vm.MailtoUri != ""</c> — or even that <c>OpenInMailClient()</c> calls the launcher — passes
/// perfectly against a build in which no button exists and no key is bound, which is precisely the build
/// that shipped. So the reachability assertions here walk <see cref="MainWindow"/>'s realised tree for the
/// button, and drive the real <c>Alt+O</c> through the window's own handler.</para>
///
/// <para><b>No real launch, ever.</b> <see cref="RecordingLauncher"/> stands in for the shell. No CI runner
/// (ubuntu, windows or macos) has a registered mail client, and ubuntu images frequently have no
/// <c>xdg-open</c> either — a test that exercised <see cref="ShellExternalLauncher"/> would fail for the
/// environment rather than for the code, or worse, leave a stray process behind.</para>
/// </summary>
public sealed class ExternalLauncherReachabilityTests
{
    /// <summary>Records what the application would have handed the OS, and answers with a scripted outcome.</summary>
    private sealed class RecordingLauncher : IExternalLauncher
    {
        public List<string> Opened { get; } = new();
        public bool Result { get; set; } = true;

        public bool Open(string target)
        {
            Opened.Add(target);
            return Result;
        }
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewCompany()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexLauncher_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();

        vm.NewCompanyName = "Launcher Co";
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>The realised "Open in Mail Client" button, or null when nothing on screen offers that verb.</summary>
    private static Button? MailClientButton(MainWindow w) =>
        Descendants(w).OfType<Button>().FirstOrDefault(b => b.Name == "OpenMailClientButton");

    // ------------------------------------------------------------------ the door exists at all

    /// <summary>
    /// 🔴 <b>The load-bearing test.</b> On the shipped build this fails: no control anywhere in the realised
    /// tree offers the verb, because <c>MailtoUri</c> had no consumer. Asserting on the URI string instead
    /// would pass on that same build — which is why this walks the visual tree.
    /// </summary>
    [AvaloniaFact]
    public void The_email_panel_realises_a_control_that_opens_the_mailto_uri()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenEmailCompose();
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            Pump(window);

            var button = MailClientButton(window);
            Assert.NotNull(button);
            Assert.True(button!.IsVisible, "the mail-client hand-off must be ON SCREEN, not merely declared");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The button is DIM until there is a recipient, because the URI is empty until then. An enabled control
    /// that fires nothing is the register's own IV-31 defect, and this panel already carries the honest
    /// alternative for its Save path.
    /// </summary>
    [AvaloniaFact]
    public void The_mail_client_button_is_dim_until_a_recipient_is_entered()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenEmailCompose();
            Pump(window);

            var button = MailClientButton(window);
            Assert.NotNull(button);
            Assert.False(button!.IsEnabled, "no recipient yet — the mailto would be empty");

            vm.EmailCompose!.To = "buyer@example.com";
            Pump(window);
            Assert.True(MailClientButton(window)!.IsEnabled);
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ the chord

    /// <summary>
    /// Alt+O through the REAL window key tunnel hands the composed URI to the launcher. Driving the chord
    /// rather than the method is the half that catches an unbound key.
    /// </summary>
    [AvaloniaFact]
    public void AltO_on_the_compose_panel_hands_the_mailto_uri_to_the_launcher()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            var launcher = new RecordingLauncher();
            vm.Launcher = launcher;

            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenEmailCompose();
            vm.EmailCompose!.To = "buyer@example.com";
            vm.EmailCompose.Subject = "Trial Balance";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            Pump(window);

            Assert.Single(launcher.Opened);
            Assert.StartsWith("mailto:buyer@example.com", launcher.Opened[0]);
            Assert.Contains("subject=Trial%20Balance", launcher.Opened[0]);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The button and the chord must be the SAME door. WI-1 records what happens when they are not: a badge
    /// and a key that advertise one shortcut and do two different things.
    /// </summary>
    [AvaloniaFact]
    public void The_button_and_AltO_run_the_same_door()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            var launcher = new RecordingLauncher();
            vm.Launcher = launcher;

            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenEmailCompose();
            vm.EmailCompose!.To = "buyer@example.com";
            Pump(window);

            MailClientButton(window)!.Command?.Execute(null);
            var viaClick = launcher.Opened.Count;
            if (viaClick == 0)
            {
                // The button uses a Click handler, not a Command — raise the click the way the user would.
                MailClientButton(window)!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(window);
            }
            Assert.Single(launcher.Opened);
            var fromButton = launcher.Opened[0];

            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(2, launcher.Opened.Count);

            Assert.Equal(fromButton, launcher.Opened[1]);
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ honesty of the status line

    [Fact]
    public void Nothing_is_handed_over_without_a_recipient_and_the_panel_says_why()
    {
        var launcher = new RecordingLauncher();
        var vm = new EmailComposeViewModel("Trial Balance", attachment: null,
            now: new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc), writeBytes: null)
        { Launcher = launcher };

        Assert.False(vm.OpenInMailClient());
        Assert.Empty(launcher.Opened);
        Assert.Contains("recipient", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A refused launch (no mail client registered — the ordinary state of a fresh workstation and of every
    /// CI runner) must be REPORTED, not swallowed into a success message. A panel that says "Opened a draft"
    /// when nothing opened is the failure mode this whole file exists to prevent, one layer down.
    /// </summary>
    [Fact]
    public void A_refused_launch_is_reported_and_points_at_the_eml_instead()
    {
        var launcher = new RecordingLauncher { Result = false };
        var vm = new EmailComposeViewModel("Trial Balance", attachment: null,
            now: new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc), writeBytes: null)
        { Launcher = launcher, To = "buyer@example.com" };

        Assert.False(vm.OpenInMailClient());
        Assert.Single(launcher.Opened);
        Assert.Contains("Could not open", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".eml", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Opened a draft", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The success line must NOT imply a send, and must say the attachment did not travel — a mailto cannot
    /// carry one (RFC 6068) and the panel's whole contract is that nothing is sent.
    /// </summary>
    [Fact]
    public void The_success_line_never_claims_a_send()
    {
        var launcher = new RecordingLauncher();
        var vm = new EmailComposeViewModel("Trial Balance", attachment: null,
            now: new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc), writeBytes: null)
        { Launcher = launcher, To = "buyer@example.com" };

        Assert.True(vm.OpenInMailClient());
        Assert.Contains("Nothing was sent", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
