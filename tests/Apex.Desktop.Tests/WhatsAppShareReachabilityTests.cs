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
/// 🔴 <b>CENSUS ROW 14.10 — "Share via WhatsApp" — AND THE REASON THIS FILE WALKS THE VISUAL TREE.</b>
///
/// <para><b>The state this file was written against.</b> The panel, its view model, its <c>Screen</c> member,
/// its <c>GatewayColumn</c> accessor and <c>MainWindowViewModel.OpenWhatsAppShare()</c> all existed and all
/// compiled — and <c>OpenWhatsAppShare</c> had <b>zero callers anywhere in <c>src/</c></b>: no key arm, no
/// button-bar entry, and no <c>DataTemplate</c> in <c>MainWindow.axaml</c>, so even had a caller existed the
/// column would have realised EMPTY. That is the fourth instance of the shape this project has already filed
/// three times — <c>CostReports.BuildLedgerBreakup</c> (fully tested, zero <c>src/</c> callers),
/// <c>MultiAccountPrintViewModel</c> (~432 lines, zero references, census T2-40), and
/// <c>EmailComposeViewModel.MailtoUri</c> (see <see cref="ExternalLauncherReachabilityTests"/>). A capability
/// no user can reach is not a feature, and a view-model test is green for it either way.</para>
///
/// <para><b>So every reachability assertion here drives the REAL key tunnel and reads the REALISED tree.</b>
/// A test that called <c>vm.OpenWhatsAppShare()</c> directly and asserted <c>CurrentScreen</c> passes against
/// the build described above — the one in which no operator could open the panel and the column would have
/// been blank if they had.</para>
///
/// <para><b>What this row deliberately does NOT do, and the tests that hold it to that.</b> The reference
/// product's WhatsApp feature is a WhatsApp Business API integration through a commercial Business Solution
/// Provider: a WABA is mandatory, a personal number cannot be used, and there is no account-free path. We have
/// no WABA and no credentials, and an outbound call to a third-party commercial API would break this
/// application's offline-by-construction posture. This panel therefore SAVES the document and hands the OS a
/// prepared <c>wa.me</c> link — it sends nothing and attaches nothing.
/// <see cref="No_control_on_the_panel_offers_to_send_anything"/> and
/// <see cref="The_realised_notice_says_nothing_is_sent_and_the_file_is_not_attached"/> are the locks that stop
/// a later slice quietly growing a "Send" button that cannot work.</para>
///
/// <para><b>No real launch, ever.</b> <see cref="RecordingLauncher"/> stands in for the shell on every runner.
/// No CI runner (ubuntu, windows, macos) has WhatsApp, a browser, or reliably an <c>xdg-open</c>; exercising
/// <c>ShellExternalLauncher</c> would fail for the environment or leave a stray process behind. Likewise every
/// save goes to a per-test temp directory, never to <c>SpecialFolder.MyDocuments</c> — which returns an EMPTY
/// STRING on Linux and has already made one assertion in this repository pass vacuously.</para>
/// </summary>
public sealed class WhatsAppShareReachabilityTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexWa_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();

        vm.NewCompanyName = "Share Co";
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

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    /// <summary>Opens a report and reaches the share panel THE WAY AN OPERATOR DOES — the bare W chord through
    /// the window's own key tunnel, never by calling the view-model method.</summary>
    private static void ReachPanelByKeyboard(MainWindow window, MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.TrialBalance);
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
        Pump(window);
    }

    // ------------------------------------------------------------------ the route in

    /// <summary>
    /// 🔴 <b>THE LOAD-BEARING TEST.</b> Against the state this file was written for, this fails: <c>W</c> was
    /// bound to nothing anywhere in the key tunnel, so <c>OpenWhatsAppShare</c> had no caller and the screen
    /// stayed on the report. Calling <c>vm.OpenWhatsAppShare()</c> instead would have passed on that build,
    /// which is exactly why this presses the key.
    /// </summary>
    [AvaloniaFact]
    public void W_on_a_report_opens_the_share_column_through_the_real_key_tunnel()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            ReachPanelByKeyboard(window, vm);

            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Assert.NotNull(vm.WhatsAppShare);
            // A cascading Miller column to the RIGHT of the report, never a stacked overlay: the report column
            // it was opened from must still be there underneath it.
            Assert.Equal("Share via WhatsApp", vm.Columns[^1].Title);
            Assert.True(vm.Columns.Count >= 2, "the report column must persist beside the share column");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The column must actually RENDER. A <c>Screen</c> member and a non-null panel property are satisfied by a
    /// build with no <c>DataTemplate</c> at all — which is the build this file was written against, and which
    /// would have shown the operator a blank column.
    /// </summary>
    [AvaloniaFact]
    public void The_share_column_realises_its_controls_and_is_not_a_blank_panel()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            ReachPanelByKeyboard(window, vm);

            Assert.NotNull(Named<TextBlock>(window, "WhatsAppNoticeText"));
            Assert.NotNull(Named<TextBox>(window, "WhatsAppNumberBox"));
            Assert.NotNull(Named<Button>(window, "SaveWhatsAppDocumentButton"));

            var open = Named<Button>(window, "OpenWhatsAppButton");
            Assert.NotNull(open);
            Assert.True(open!.IsVisible, "the hand-off must be ON SCREEN, not merely declared");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The button bar carries the row, so the capability is discoverable by a mouse too — and its badge
    /// letter is the key that actually triggers it (the RQ-28 rule).</summary>
    [AvaloniaFact]
    public void The_button_bar_offers_WhatsApp_on_a_printable_page()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);

            var item = vm.ButtonBar.FirstOrDefault(b => b.Caption == "WhatsApp");
            Assert.NotNull(item);
            Assert.Equal("W", item!.Key);
            Assert.True(item.Enabled, "a printable page is exactly where sharing applies");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Off a printable page the chord must do NOTHING — the same guard the e-mail channel carries. A share panel
    /// opened over the bare Gateway would have no document to share, and the panel's whole contract is that the
    /// message names a real saved file.
    /// </summary>
    [AvaloniaFact]
    public void W_on_the_bare_gateway_opens_nothing()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);

            Assert.Null(vm.WhatsAppShare);
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    // ------------------------------------------------------------------ save-then-share ordering

    /// <summary>
    /// 🔴 <b>The ordering lock.</b> The message text NAMES the file the operator is asked to attach, so the file
    /// must exist before the link is handed out. Alt+O before a save must hand the launcher NOTHING and say why.
    /// </summary>
    [AvaloniaFact]
    public void AltO_before_a_save_hands_over_nothing_and_the_panel_says_why()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            var launcher = new RecordingLauncher();
            vm.Launcher = launcher;

            ReachPanelByKeyboard(window, vm);
            vm.WhatsAppShare!.PhoneNumber = "9876543210";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            Pump(window);

            Assert.Empty(launcher.Opened);
            Assert.Contains("Save the document first", vm.WhatsAppShare.Status, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The whole two-step flow driven the way an operator drives it: W to open, type the number, save the
    /// artefact, Alt+O to hand over the link. The assertion is that the artefact is ON DISK <b>before</b> the
    /// launcher sees anything, and that the link names that same file.
    /// </summary>
    [AvaloniaFact]
    public void Sharing_writes_the_artefact_before_the_launcher_is_invoked()
    {
        var (window, vm, dir) = NewCompany();
        var saveDir = Path.Combine(Path.GetTempPath(), "ApexWaOut_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saveDir);
        try
        {
            var launcher = new RecordingLauncher();
            vm.Launcher = launcher;

            ReachPanelByKeyboard(window, vm);
            var panel = vm.WhatsAppShare!;
            panel.PhoneNumber = "98765 43210";
            Pump(window);

            var target = Path.Combine(saveDir, panel.SuggestedFileName);
            Assert.True(vm.SaveWhatsAppDocument(target));
            Assert.True(File.Exists(target), "step ONE must put the artefact on disk");
            Assert.Empty(launcher.Opened);      // nothing handed over yet

            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            Pump(window);

            Assert.Single(launcher.Opened);
            var uri = launcher.Opened[0];
            Assert.StartsWith("https://wa.me/919876543210?text=", uri, StringComparison.Ordinal);
            // The message must name the file the operator has to attach, and the file must be the one written.
            Assert.Contains(Uri.EscapeDataString(Path.GetFileName(target)).Replace("%20", "%20"),
                            uri.Replace("%2E", "."), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Close(window, dir);
            try { Directory.Delete(saveDir, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>The "Open in WhatsApp" control is DIM until both conditions hold, so the operator is never
    /// offered a control that refuses. Read off the REALISED button, not off the flag it binds to.</summary>
    [AvaloniaFact]
    public void The_open_control_is_dim_until_there_is_a_number_and_a_saved_file()
    {
        var (window, vm, dir) = NewCompany();
        var saveDir = Path.Combine(Path.GetTempPath(), "ApexWaDim_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saveDir);
        try
        {
            ReachPanelByKeyboard(window, vm);
            var panel = vm.WhatsAppShare!;

            Assert.False(Named<Button>(window, "OpenWhatsAppButton")!.IsEnabled);

            panel.PhoneNumber = "9876543210";
            Pump(window);
            Assert.False(Named<Button>(window, "OpenWhatsAppButton")!.IsEnabled);   // number, but no file yet

            vm.SaveWhatsAppDocument(Path.Combine(saveDir, panel.SuggestedFileName));
            Pump(window);
            Assert.True(Named<Button>(window, "OpenWhatsAppButton")!.IsEnabled);
        }
        finally
        {
            Close(window, dir);
            try { Directory.Delete(saveDir, recursive: true); } catch { /* temp */ }
        }
    }

    // ------------------------------------------------------------------ honesty

    /// <summary>
    /// 🔴 <b>The honesty lock, read off the screen the operator actually sees.</b> Every clause of the contract
    /// must be on the panel: nothing is sent, the file is not attached, the operator attaches it.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_notice_says_nothing_is_sent_and_the_file_is_not_attached()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            ReachPanelByKeyboard(window, vm);

            var notice = Named<TextBlock>(window, "WhatsAppNoticeText");
            Assert.NotNull(notice);
            var text = notice!.Text ?? string.Empty;

            Assert.Contains("does not send", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cannot attach", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("yourself", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>No control on this panel may offer to SEND.</b> We have no WABA, no BSP account and no credentials;
    /// a "Send" button here could only ever be a button that silently does nothing — the precise defect the row
    /// was scoped to avoid. This test exists so a later slice cannot quietly add one.
    /// </summary>
    [AvaloniaFact]
    public void No_control_on_the_panel_offers_to_send_anything()
    {
        var (window, vm, dir) = NewCompany();
        try
        {
            ReachPanelByKeyboard(window, vm);

            // Only the buttons realised INSIDE the share column, found via its own named controls' parentage.
            var open = Named<Button>(window, "OpenWhatsAppButton");
            Assert.NotNull(open);
            var panelRoot = open!.FindAncestorOfType<StackPanel>();
            Assert.NotNull(panelRoot);

            var verbs = Descendants(panelRoot!).OfType<Button>()
                                               .Select(b => b.Content?.ToString() ?? string.Empty)
                                               .ToList();
            Assert.NotEmpty(verbs);
            Assert.All(verbs, v =>
                Assert.False(v.Contains("Send", StringComparison.OrdinalIgnoreCase),
                             $"a 'Send' control cannot work without a WABA — found: {v}"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>A refused launch must be REPORTED, never swallowed into a success line — and the panel must
    /// still tell the operator where the saved document is, because that is the half that DID happen.</summary>
    [Fact]
    public void A_refused_launch_is_reported_and_still_names_the_saved_file()
    {
        var launcher = new RecordingLauncher { Result = false };
        var written = new List<string>();
        var vm = new WhatsAppShareViewModel("Trial Balance", new byte[] { 1, 2, 3 },
                                            (p, _) => written.Add(p))
        { Launcher = launcher, PhoneNumber = "9876543210" };

        // Path.Combine keeps the separator native, so this is correct on windows, ubuntu and macos alike.
        var target = Path.Combine("out", "Trial Balance.pdf");
        Assert.True(vm.SaveDocument(target));
        Assert.Single(written);

        Assert.False(vm.Share());
        Assert.Single(launcher.Opened);
        Assert.Contains("Could not open WhatsApp", vm.Status, StringComparison.Ordinal);
        Assert.Contains(target, vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Opened WhatsApp", vm.Status, StringComparison.Ordinal);
    }

    /// <summary>The success line must never imply a send, and must repeat that the file is NOT attached.</summary>
    [Fact]
    public void The_success_line_never_claims_a_send_and_repeats_the_attachment_warning()
    {
        var launcher = new RecordingLauncher();
        var vm = new WhatsAppShareViewModel("Sales Invoice", new byte[] { 9 }, (_, _) => { })
        { Launcher = launcher, PhoneNumber = "9876543210" };

        Assert.True(vm.SaveDocument(Path.Combine("out", "Sales Invoice.pdf")));
        Assert.True(vm.Share());

        Assert.Contains("Nothing was sent", vm.Status, StringComparison.Ordinal);
        Assert.Contains("NOT attached", vm.Status, StringComparison.Ordinal);
        Assert.Contains("Sales Invoice.pdf", vm.Status, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the number and the link

    /// <summary>The link is percent-encoded through the SAME encoder the mailto path uses, so this codebase
    /// does not grow a second one that is subtly wrong about a rupee sign or a newline.</summary>
    [Fact]
    public void The_wa_me_link_is_percent_encoded()
    {
        var vm = new WhatsAppShareViewModel("Sales Invoice", Array.Empty<byte>(), (_, _) => { })
        { PhoneNumber = "9876543210", Message = "Invoice ₹1,200 & more" };

        var uri = vm.WaMeUri;
        Assert.StartsWith("https://wa.me/919876543210?text=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("&more", uri, StringComparison.Ordinal);
        Assert.Contains("%26", uri, StringComparison.Ordinal);       // the ampersand, encoded
        Assert.Contains("%E2%82%B9", uri, StringComparison.Ordinal); // the rupee sign, UTF-8 first
    }

    /// <summary>Punctuation people write phone numbers with is stripped; anything else REJECTS the number
    /// outright. Silently deleting a letter out of the middle would produce a different, valid-looking number
    /// and the operator would have no way to see they were about to message a stranger.</summary>
    [Fact]
    public void Punctuation_is_stripped_but_a_letter_rejects_the_number_outright()
    {
        var vm = new WhatsAppShareViewModel("Doc", Array.Empty<byte>(), (_, _) => { })
        { PhoneNumber = "+91 (98765)-43.210" , CountryCode = "" };
        Assert.Equal(string.Empty, vm.NormalisedNumber);    // no country code at all -> no link

        vm.CountryCode = "91";
        vm.PhoneNumber = " 98765-43210 ";
        Assert.Equal("919876543210", vm.NormalisedNumber);

        vm.PhoneNumber = "98765o3210";                      // a letter, not a digit
        Assert.Equal(string.Empty, vm.NormalisedNumber);
        Assert.Equal(string.Empty, vm.WaMeUri);
    }

    /// <summary>Without a number there is no link, and <c>Share</c> refuses with the reason on the status line
    /// rather than opening something empty.</summary>
    [Fact]
    public void Without_a_number_there_is_no_link_and_share_refuses()
    {
        var launcher = new RecordingLauncher();
        var vm = new WhatsAppShareViewModel("Doc", Array.Empty<byte>(), (_, _) => { }) { Launcher = launcher };

        Assert.Equal(string.Empty, vm.WaMeUri);
        Assert.False(vm.CanShare);
        Assert.False(vm.Share());
        Assert.Empty(launcher.Opened);
        Assert.Contains("mobile number", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
