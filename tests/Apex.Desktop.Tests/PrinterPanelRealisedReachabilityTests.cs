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
}
