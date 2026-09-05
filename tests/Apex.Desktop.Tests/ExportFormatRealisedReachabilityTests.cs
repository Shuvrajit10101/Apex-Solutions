using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Io;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-25 / census row 13.6 — the LAST mile: a realised control the operator can actually click.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> <c>ExportFormatReachabilityTests</c> pins the view-model half —
/// <c>IsHtml</c>/<c>IsXml</c>/<c>IsJson</c>/<c>IsAscii</c> flip <c>Format</c>, and <c>Apply()</c> writes the right
/// bytes. It does not touch <c>MainWindow.axaml</c>. Deleting the four <c>RadioButton</c>s from the Export panel
/// would leave all eight of those tests green while making the four formats <b>unreachable by any user</b> — the
/// precise shape this project keeps re-finding (<c>CompanyStorage.Rename()</c>,
/// <c>CostReports.BuildLedgerBreakup</c>: written, correct, tested, and called by nobody). A view-model property
/// is not a feature; a control bound to it is.</para>
///
/// <para>So this file realises the <b>real</b> <see cref="MainWindow"/> with the <b>real</b> Export panel open on
/// a real report, and asks the realised radios what they offer — the idiom
/// <c>CopyMarkingCaptionLockTests</c> established after a mutation escaped a markup-only scan. It reads the
/// realised control rather than the markup, so it bites whether a caption is a literal today or a binding after a
/// refactor, including the refactor's own failure mode of a radio that renders but is bound to nothing.</para>
/// </summary>
public sealed class ExportFormatRealisedReachabilityTests
{
    /// <summary>Flushes bindings and forces a layout pass so the Export panel's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The shipped window, on a real report, with the Export panel genuinely open.</summary>
    private static MainWindow OpenExportPanel(out MainWindowViewModel vm, string tempDir)
    {
        vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenExport();
        Assert.True(vm.ExportPanel is not null,
            "OpenExport() produced no panel — the export route itself is broken, so nothing below is meaningful.");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

    private static string[] RealisedFormatCaptions(Window window)
        => window.GetVisualDescendants()
                 .OfType<RadioButton>()
                 .Where(r => r.GroupName == "ExportFormat")
                 .Select(r => r.Content as string ?? string.Empty)
                 .ToArray();

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> The realised Export panel offers a clickable choice for each of the
    /// four W2-25 formats. Captions are matched on their format word only, so re-wording the parenthetical is
    /// free while deleting a choice is not.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_export_panel_offers_the_four_new_formats()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApexExportReach_" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        try
        {
            window = OpenExportPanel(out _, tempDir);
            var captions = RealisedFormatCaptions(window);

            // Non-vacuity first: if the scan found nothing, every Contains below would pass on an empty premise.
            Assert.True(captions.Length >= 7,
                $"the realised Export panel offers only {captions.Length} format radio(s) "
              + $"({string.Join(" | ", captions)}) — expected the three shipped plus the four from W2-25.");

            foreach (var expected in new[] { "HTML", "XML", "JSON", "ASCII" })
                Assert.True(captions.Any(c => c.Contains(expected, StringComparison.OrdinalIgnoreCase)),
                    $"no realised radio offers {expected}. The writer may exist in Apex.Ledger.Io, but with no "
                  + $"control bound to it the format is unreachable and census row 13.6 has not moved. "
                  + $"Realised captions were: {string.Join(" | ", captions)}");
        }
        finally
        {
            // Closing the realised window is NOT optional here, and omitting it is what made this file crash the
            // whole Apex.Desktop suite (a process-level "Catastrophic failure", at a DIFFERENT test each run, long
            // after these two had passed). Every other AvaloniaFact in this project closes in a finally.
            window?.Close();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The realised radios are genuinely <b>bound</b> — checking one moves the view-model's <c>Format</c>, and the
    /// resolved file name picks up the new extension. A radio that renders but is bound to nothing would satisfy
    /// the caption test above; only driving it proves the wire.
    /// </summary>
    [AvaloniaFact]
    public void Checking_a_realised_radio_drives_the_format_and_the_file_extension()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApexExportDrive_" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        try
        {
            window = OpenExportPanel(out var vm, tempDir);

            var radios = window.GetVisualDescendants()
                               .OfType<RadioButton>()
                               .Where(r => r.GroupName == "ExportFormat")
                               .ToList();

            // Expected extensions are the vendor File Format list's own, transcribed here and never read back
            // out of ExportConfig — a test that asks the code what it does can only ever agree with it.
            foreach (var (word, format, extension) in new[]
                     {
                         ("HTML", ExportFormat.Html, "html"),
                         ("XML", ExportFormat.Xml, "xml"),
                         ("JSON", ExportFormat.Json, "json"),
                         ("ASCII", ExportFormat.Ascii, "txt"),
                     })
            {
                var radio = radios.First(r =>
                    (r.Content as string ?? string.Empty).Contains(word, StringComparison.OrdinalIgnoreCase));

                radio.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(format, vm.ExportPanel!.Format);
                Assert.EndsWith("." + extension, vm.ExportPanel!.ResolvedFileName, StringComparison.Ordinal);
            }
        }
        finally
        {
            window?.Close();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
