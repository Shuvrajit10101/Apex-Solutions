using System;
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
using Apex.Ledger.Reports;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 12.5 — the LAST mile, asserted through the real window and the real chord.</b>
///
/// <para><b>🔴 Why this file is not optional.</b> This project has filed the same defect three times:
/// <c>CompanyStorage.Rename()</c>, <c>CostReports.BuildLedgerBreakup</c> and <c>MultiAccountPrintViewModel</c>
/// were each written, correct and tested, and callable by nobody. A view model that lists printers and builds
/// jobs is not a printing feature; a column the operator can open with a key, standing on a real document, is.
/// So this file realises the shipped <see cref="MainWindow"/> with the shipped markup, presses the shipped
/// chord, and reads the controls that actually rendered.</para>
///
/// <para>No runner has a printer, so the enumerator and the spooler are substituted — and only those two. The
/// route, the markup, the bindings, the key handler and the job that would have reached paper are all real.</para>
/// </summary>
public sealed class PrinterPanelRealisedReachabilityTests
{
    /// <summary>
    /// 🔴 <b>Realised is not the same as SHOWN, and this project has already been bitten by the difference.</b>
    /// Avalonia keeps an <c>IsVisible="False"</c> control in the visual tree, so a scan of
    /// <c>GetVisualDescendants()</c> alone happily finds a control the operator cannot see — a mutation that set
    /// <c>IsVisible="False"</c> on the printer list left this whole file green until this predicate was added.
    /// A control counts only when it is effectively visible AND was actually given area by the layout pass.
    /// </summary>
    private static bool OnScreen(Control control) =>
        control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0;

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The shipped window on a real report with a real Print Preview open, and the printing seam faked.</summary>
    private static MainWindow OpenPreview(
        out MainWindowViewModel vm, out RecordingPrintJobSubmitter submitter, string tempDir,
        FakePrinterDevices? devices = null)
    {
        vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        submitter = new RecordingPrintJobSubmitter();
        vm.PrinterDevices = devices ?? FakePrinterDevices.TwoWithDefaultSecond();
        vm.PrintJobSubmitter = submitter;

        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();
        Assert.True(vm.PrintPreview is not null,
            "no print preview opened — the route this row hangs off is broken, so nothing below is meaningful.");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

    private static string TempDir(string tag) =>
        Path.Combine(Path.GetTempPath(), tag + "_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// <b>The route.</b> Ctrl+P, standing on a Print Preview, opens the Printer column. This is the assertion
    /// that fails on today's main: <see cref="Screen.Printer"/> does not exist there, and Ctrl+P was being
    /// swallowed by the preview handler (which returned immediately, a preview already being open).
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_on_a_preview_opens_the_printer_column()
    {
        string dir = TempDir("ApexPrinterRoute");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out _, dir);
            int columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.True(vm.PrinterPanel is not null,
                "Ctrl+P on an open Print Preview did not open the Printer column — census row 12.5 has no route in.");
            Assert.Equal(Screen.Printer, vm.CurrentScreen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.Equal("Printer", vm.Columns[^1].Title);
            // The preview stays live BENEATH the printer column — this is a cascade, not a replacement.
            Assert.True(vm.PrintPreview is not null);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// <b>The door is visible, not only chorded.</b> The Print Preview shows a "Printer… (Ctrl+P)" button, and
    /// clicking it opens the column. A capability whose only route is an undiscoverable keystroke is barely a
    /// capability, and a caption naming a key that is not bound is a defect this project has already filed.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_shows_a_printer_button_that_opens_the_column()
    {
        string dir = TempDir("ApexPrinterButton");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out _, dir);

            var printerButton = window.GetVisualDescendants().OfType<Button>()
                                      .FirstOrDefault(b => (b.Content as string)?.StartsWith("Printer", StringComparison.Ordinal) == true
                                                        && OnScreen(b));
            Assert.True(printerButton is not null,
                "the Print Preview shows no Printer button — census row 12.5 would be reachable only by a chord "
              + "nothing on screen mentions.");
            Assert.Contains("Ctrl+P", (string)printerButton!.Content!, StringComparison.Ordinal);

            ((Avalonia.Interactivity.Interactive)printerButton).RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            Assert.True(vm.PrinterPanel is not null, "the Printer button did not open the Printer column.");
            Assert.Equal(Screen.Printer, vm.CurrentScreen);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// <b>The controls.</b> The realised column offers a queue for each printer the OS reported, a copies box,
    /// the submission notice and a Print button. Asserting the view model's collections instead would pass on a
    /// build whose <c>DataTemplate</c> had been deleted.
    /// </summary>
    [AvaloniaFact]
    public void The_printer_column_realises_its_list_its_copies_box_its_notice_and_its_print_button()
    {
        string dir = TempDir("ApexPrinterControls");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out _, dir);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            var lists = window.GetVisualDescendants().OfType<ListBox>()
                              .Where(l => l.ItemsSource is System.Collections.IEnumerable src
                                       && src.Cast<object>().Any(o => o is PrinterDevice))
                              .Where(OnScreen)
                              .ToList();
            var printerList = Assert.Single(lists);
            var offered = ((System.Collections.IEnumerable)printerList.ItemsSource!)
                          .Cast<PrinterDevice>().Select(p => p.Name).ToArray();

            Assert.Equal(new[] { "Front Office HP", "Accounts Laser" }, offered);
            Assert.Equal("Accounts Laser", (printerList.SelectedItem as PrinterDevice)?.Name);

            var copiesBox = window.GetVisualDescendants().OfType<TextBox>()
                                  .FirstOrDefault(t => t.Name == "PrinterCopiesBox" && OnScreen(t));
            Assert.True(copiesBox is not null,
                "the printer column shows no copies box — the copy count is unreachable from this panel.");

            var printButton = window.GetVisualDescendants().OfType<Button>()
                                    .FirstOrDefault(b => (b.Content as string)?.Contains("Print", StringComparison.Ordinal) == true
                                                      && (b.Content as string)?.Contains("Ctrl+A", StringComparison.Ordinal) == true
                                                      && OnScreen(b));
            Assert.True(printButton is not null,
                "the printer column shows no Print button — the only way to spool would be the chord.");
            Assert.True(printButton!.IsEnabled);

            // The RAW-submission constraint is on screen, not only in a code comment. It is the operator's only
            // warning that a job can be accepted by the spooler and produce nothing at the device.
            string notice = vm.PrinterPanel!.SubmissionNotice;
            var noticeBlocks = window.GetVisualDescendants().OfType<TextBlock>()
                                     .Where(t => string.Equals(t.Text, notice, StringComparison.Ordinal) && OnScreen(t))
                                     .ToList();
            Assert.True(noticeBlocks.Count >= 1,
                $"the submission notice is not shown anywhere in the realised column. Expected: {notice}");
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// <b>The verb.</b> Ctrl+A on the Printer column spools the job the operator was looking at — the same bytes
    /// the preview holds, on the selected queue, with the spooler asked for exactly one copy.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_printer_column_spools_the_previewed_job()
    {
        string dir = TempDir("ApexPrinterSpool");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out var submitter, dir);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var job = Assert.Single(submitter.Submitted);
            Assert.Equal("Accounts Laser", job.PrinterName);
            Assert.Equal(1, job.SpoolerCopies);
            Assert.True(job.Pdf.SequenceEqual(vm.PrintPreview!.PdfBytes),
                "the spooled bytes are not the previewed bytes — the operator would print a document they never saw.");
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// <b>Cancel changes nothing.</b> Opening the column and pressing Esc pops back to the live preview, spools
    /// nothing, and leaves the document byte-identical.
    /// </summary>
    [AvaloniaFact]
    public void Esc_pops_back_to_the_live_preview_having_spooled_nothing()
    {
        string dir = TempDir("ApexPrinterEsc");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out var submitter, dir);
            byte[] before = vm.PrintPreview!.PdfBytes;

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);
            Assert.NotNull(vm.PrinterPanel);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.PrinterPanel is null, "Esc left the Printer column open.");
            Assert.True(vm.PrintPreview is not null, "Esc popped past the preview instead of back to it.");
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.True(vm.PrintPreview!.PdfBytes.SequenceEqual(before));
            Assert.Empty(submitter.Submitted);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// <b>The existing binding is untouched.</b> Ctrl+P standing on a REPORT still opens the Print Preview, as
    /// it always did. Widening a chord onto a new screen must not narrow it on the old one.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_on_a_report_still_opens_the_preview_not_the_printer()
    {
        string dir = TempDir("ApexPrinterPreviewStill");
        MainWindow? window = null;
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(dir))
            {
                PrinterDevices = FakePrinterDevices.TwoWithDefaultSecond(),
                PrintJobSubmitter = new RecordingPrintJobSubmitter(),
            };
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);

            window = new MainWindow { DataContext = vm };
            window.Show();
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.True(vm.PrintPreview is not null, "Ctrl+P on a report no longer opens the Print Preview.");
            Assert.True(vm.PrinterPanel is null, "Ctrl+P on a report opened the Printer column, skipping the preview.");
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// A machine with no printers — every CI runner — still opens the column, states the empty case, and offers
    /// no live Print button. A disabled-looking panel that silently accepts a click is the worse failure.
    /// </summary>
    [AvaloniaFact]
    public void With_no_printers_the_column_states_the_empty_case_and_the_print_button_is_disabled()
    {
        string dir = TempDir("ApexPrinterEmpty");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out var submitter, dir, FakePrinterDevices.None());
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.PrinterPanel);
            Assert.False(vm.PrinterPanel!.HasPrinters);

            var printButton = window.GetVisualDescendants().OfType<Button>()
                                    .FirstOrDefault(b => (b.Content as string)?.Contains("Print (Ctrl+A)", StringComparison.Ordinal) == true);
            Assert.True(printButton is not null, "the printer column did not realise at all with no printers.");
            Assert.False(printButton!.IsEnabled);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.Empty(submitter.Submitted);
            Assert.Equal(PrintJobBuilder.NoPrinterMessage, vm.PrinterPanel!.Status);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 🔴 <b>A column popped from OVER the Printer column must leave a Print button that still prints.</b>
    ///
    /// <para>This is the defect the F2 review found and it needs no exotic sequence. The button bar's E-Mail
    /// badge is LIVE while the Printer column is open — <c>IsPrintablePage</c> is true because the report is
    /// still bound two columns down — and <c>OpenEmailCompose</c> APPENDS its column on top. Popping that with
    /// Esc runs <c>ClearSubScreens()</c>, which nulls the shell's <c>PrinterPanel</c>; but the printer's own
    /// <c>GatewayColumn</c> is still in <c>Columns</c> holding the same view model, and the markup binds
    /// <c>GatewayColumn.PrinterPanel</c>, not the shell property. So the column keeps rendering an ENABLED
    /// "Print (Ctrl+A)" button whose click reaches <c>PrinterPanel?.PrintAsync()</c> against null and spools
    /// nothing, silently, with no message anywhere.</para>
    ///
    /// <para><b>What makes this bite rather than merely pass.</b> It presses the real badge, pops with the real
    /// key, then CLICKS THE RENDERED BUTTON and asserts a job reached the spooler. Asserting only that
    /// <c>PrinterPanel</c> is non-null would be a weaker test of the same line; asserting the button renders
    /// would pass on the defective build, because the button renders there too — that is the whole problem.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_column_popped_from_over_the_printer_leaves_a_print_button_that_still_prints()
    {
        string dir = TempDir("ApexPrinterSurvivesPop");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out var submitter, dir);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.Printer, vm.CurrentScreen);
            var panel = vm.PrinterPanel;
            Assert.NotNull(panel);
            int printerColumns = vm.Columns.Count;

            // The route is the operator's, not the test's: the shipped badge, enabled on this very screen.
            var emailBadge = vm.ButtonBar.First(b => b.Key == "M");
            Assert.True(emailBadge.Enabled,
                "the E-Mail badge is dimmed on the Printer column, so this pop route is not one an operator has "
              + "and this test would be proving nothing.");
            emailBadge.Action();
            Pump(window);

            Assert.NotNull(vm.EmailCompose);
            Assert.Equal(printerColumns + 1, vm.Columns.Count);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(printerColumns, vm.Columns.Count);
            Assert.Same(panel, vm.PrinterPanel);
            Assert.Equal(Screen.Printer, vm.CurrentScreen);

            var printButton = window.GetVisualDescendants().OfType<Button>()
                                    .FirstOrDefault(b => (b.Content as string)?.Contains("Print (Ctrl+A)", StringComparison.Ordinal) == true
                                                      && OnScreen(b));
            Assert.True(printButton is not null,
                "the surviving Printer column stopped rendering its Print button after the pop.");
            Assert.True(printButton!.IsEnabled);

            ((Avalonia.Interactivity.Interactive)printButton).RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            var job = Assert.Single(submitter.Submitted);
            Assert.Equal("Accounts Laser", job.PrinterName);

            // 🔴 The bytes are compared against the PREVIEW COLUMN's view model, not against `vm.PrintPreview`.
            // That shell property is null at this point and it is NOT a defect of this fix:
            // `RehydratePageFromRightmostColumn` re-binds only the RIGHTMOST surviving column by design, and the
            // preview sits one deeper than the printer. It self-heals — Esc from here pops the printer column
            // and re-binds the preview — but a test written against it would be asserting the wrong thing.
            var preview = vm.Columns.Select(c => c.Page).OfType<PrintPreviewViewModel>().Single();
            Assert.True(job.Pdf.SequenceEqual(preview.PdfBytes),
                "the surviving column spooled something other than the document it is standing on.");
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 🔴 <b>The double-open guard, which was shipped completely untested.</b>
    ///
    /// <para><c>OpenPrinter</c> opens with <c>if (PrinterPanel is not null) return;</c>. The reviewer mutated
    /// that to <c>if (false &amp;&amp; …)</c> and all 36 printer tests stayed green, so the line was carrying no
    /// weight at all.</para>
    ///
    /// <para><b>The route it actually protects is the BUTTON, not the chord.</b> A second Ctrl+P cannot reach
    /// <c>OpenPrinter</c> — that key arm is guarded on <c>CurrentScreen == Screen.PrintPreview</c> and the
    /// screen is now <c>Screen.Printer</c>. But the preview column is still rendered underneath (this is a
    /// Miller cascade, not a replacement), so its "Printer… (Ctrl+P)" button is still on screen and still
    /// clickable, and <c>OnOpenPrinterClick</c> calls <c>OpenPrinter()</c> unconditionally. Without the guard
    /// that click stacks a SECOND Printer column over the first, re-enumerating the machine's queues, and the
    /// operator has to press Esc twice to get out of a panel they opened once.</para>
    /// </summary>
    [AvaloniaFact]
    public void Re_opening_the_printer_column_from_the_preview_button_does_not_stack_a_second_one()
    {
        string dir = TempDir("ApexPrinterNoStack");
        MainWindow? window = null;
        try
        {
            var devices = FakePrinterDevices.TwoWithDefaultSecond();
            window = OpenPreview(out var vm, out _, dir, devices);

            Button? PrinterDoor() => window!.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (b.Content as string)?.StartsWith("Printer", StringComparison.Ordinal) == true
                                  && OnScreen(b));

            var door = PrinterDoor();
            Assert.True(door is not null, "the preview shows no Printer button to press even once.");
            ((Avalonia.Interactivity.Interactive)door!).RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            var first = vm.PrinterPanel;
            Assert.NotNull(first);
            int columns = vm.Columns.Count;
            Assert.Equal(1, devices.ListCallCount);

            // The preview is still there underneath, so its door is still pressable. Press it again.
            var again = PrinterDoor();
            Assert.True(again is not null,
                "the preview's Printer button stopped rendering once the column opened, so the guard this test "
              + "exists for could not be reached and the test would be vacuous.");
            ((Avalonia.Interactivity.Interactive)again!).RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            Assert.Same(first, vm.PrinterPanel);
            Assert.Equal(columns, vm.Columns.Count);
            Assert.Single(vm.Columns, c => c.Page is PrinterSelectionViewModel);
            Assert.Equal(1, devices.ListCallCount);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 🔴 <b>Ctrl+A with the caret in the "Number of copies" box is Select All, not "put ink on paper".</b>
    ///
    /// <para>The shell's Ctrl+A block carries no <c>IsTyping</c> guard, which is deliberate everywhere it
    /// already shipped — Ctrl+A accepts a voucher from inside the voucher's own fields. The Printer column is
    /// the one arm where it is wrong: Ctrl+A is the universal Select All of whatever text box has the caret,
    /// the copies box is the only field on the column, and the unguarded arm SPOOLS PAPER. There is no undo for
    /// a spooled job.</para>
    ///
    /// <para>The second half is as important as the first: the guard must be narrow. Ctrl+A anywhere else on the
    /// same column must still print, or the fix has quietly removed the row's own accept chord.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_inside_the_copies_box_spools_nothing_but_still_spools_from_outside_it()
    {
        string dir = TempDir("ApexPrinterCopiesChord");
        MainWindow? window = null;
        try
        {
            window = OpenPreview(out var vm, out var submitter, dir);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);

            var copiesBox = window.GetVisualDescendants().OfType<TextBox>()
                                  .FirstOrDefault(t => t.Name == "PrinterCopiesBox" && OnScreen(t));
            Assert.True(copiesBox is not null, "no copies box on the realised column — nothing to type into.");

            copiesBox!.Focus();
            Pump(window);
            Assert.True(copiesBox.IsFocused,
                "the copies box never took focus, so the keystroke below would not have come from a TextBox and "
              + "this test would pass without exercising the guard at all.");

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            // 🔴 ASSERTED BEFORE THE DISPATCHER IS PUMPED, and that ordering is load-bearing rather than tidy.
            // Measured on the mutant (guard removed): the spool happens synchronously inside the key handler —
            // `submitted=1`, `status=Sent "Trial Balance" to Accounts Laser.` — and the very next `Pump` then
            // HANGS the headless run instead of returning. Asserting after the pump would turn this test's
            // mutation signal from a red assertion into a gate that never finishes, which is not a signal.
            Assert.Empty(submitter.Submitted);

            // The other half of the same measurement: with the guard the box performs its own Select All
            // (`sel=0..1` over the text "1"), because the window returned the keystroke unhandled. Without it the
            // window consumed the key and the selection never moved (`sel=0..0`). This is the assertion that
            // proves WHY nothing was spooled — the key reached the text box — rather than merely that it wasn't.
            Assert.Equal(0, copiesBox.SelectionStart);
            Assert.Equal((copiesBox.Text ?? string.Empty).Length, copiesBox.SelectionEnd);
            Assert.Equal(string.Empty, vm.PrinterPanel!.Status);

            Pump(window);

            Assert.Equal(Screen.Printer, vm.CurrentScreen);
            Assert.NotNull(vm.PrinterPanel);

            // …and the guard is scoped to the TEXT BOX, not to the column. With the caret out of the copies box
            // — here on the Print button, one Tab away — the same chord still does what the caption promises.
            var printButton = window.GetVisualDescendants().OfType<Button>()
                                    .FirstOrDefault(b => (b.Content as string)?.Contains("Print (Ctrl+A)", StringComparison.Ordinal) == true
                                                      && OnScreen(b));
            Assert.True(printButton is not null, "no Print button to move the caret onto.");
            printButton!.Focus();
            Pump(window);
            Assert.False(copiesBox.IsFocused,
                "focus never left the copies box, so the second half of this test would be re-running the first "
              + "half and claiming it proved the opposite.");

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.Single(submitter.Submitted);
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
