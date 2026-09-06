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

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-32 / census 12.6 — the LAST mile: realised controls the operator can actually reach.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> <c>MultiAccountPrintReachabilityTests</c> pins the view-model half: the
/// menu entry, the opener, the selection, the print. It never opens a window. Deleting the
/// <c>MultiAccountPrintViewModel</c> <c>DataTemplate</c> from <c>MainWindow.axaml</c> would leave every one of
/// those tests green while making the account selection <b>unreachable by any user</b> — the panel would open as
/// an empty column. <c>IsSelected</c> is not a feature; a <c>CheckBox</c> bound to it is.</para>
///
/// <para>So this file realises the <b>real</b> <see cref="MainWindow"/> with the <b>real</b> panel open on the
/// Robert fixture, and asks the realised controls what they offer — the idiom
/// <c>ExportFormatRealisedReachabilityTests</c> established. It reads the realised control rather than the
/// markup, so it bites through a refactor, including the refactor's own failure mode of a control that renders
/// but is bound to nothing.</para>
/// </summary>
public sealed class MultiAccountPrintRealisedReachabilityTests
{
    /// <summary>Flushes bindings and forces a layout pass so the panel's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The shipped window with the Multi-Account Printing panel genuinely open, reached by menu.</summary>
    private static MainWindow OpenPanel(out MainWindowViewModel vm, string tempDir)
    {
        vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.LoadRobertDemo();
        vm.OpenMultiAccountPrint();
        Assert.True(vm.MultiAccountPrint is not null,
            "OpenMultiAccountPrint() produced no panel — the route itself is broken, so nothing below is meaningful.");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

    private static CheckBox[] RealisedAccountCheckBoxes(Window window)
        => window.GetVisualDescendants()
                 .OfType<CheckBox>()
                 .Where(c => c.DataContext is MultiAccountRowViewModel)
                 .ToArray();

    private static string[] RealisedDocumentKindCaptions(Window window)
        => window.GetVisualDescendants()
                 .OfType<RadioButton>()
                 .Where(r => r.GroupName == "MultiAccountDocument")
                 .Select(r => r.Content as string ?? string.Empty)
                 .ToArray();

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ApexMultiPrintRealised_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> Every account the panel lists is realised as a real, toggleable
    /// <see cref="CheckBox"/> — and toggling the realised control moves the view model, which is the half a
    /// markup scan cannot see.
    /// </summary>
    [AvaloniaFact]
    public void Every_account_is_realised_as_a_checkbox_that_moves_the_view_model()
    {
        string tempDir = TempDir();
        MainWindow? window = null;
        try
        {
            window = OpenPanel(out var vm, tempDir);
            var boxes = RealisedAccountCheckBoxes(window);

            Assert.Equal(vm.MultiAccountPrint!.Accounts.Count, boxes.Length);
            Assert.NotEmpty(boxes);
            Assert.Equal(0, vm.MultiAccountPrint.SelectedCount);

            // Toggle the realised control the way an operator's Space press does.
            boxes[0].IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, vm.MultiAccountPrint.SelectedCount);
            Assert.True(((MultiAccountRowViewModel)boxes[0].DataContext!).IsSelected,
                "the realised CheckBox is not two-way bound to IsSelected — the operator can tick a box that "
              + "selects nothing.");
        }
        finally
        {
            window?.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// <b>THE KEYSTROKE ASSERTION — the operator's own route, end to end, with no view-model call in it.</b>
    ///
    /// <para>Everything above still calls a method somewhere. This one ticks a realised <see cref="CheckBox"/>
    /// and presses <c>Ctrl+A</c> on the real window, and asserts a print preview came back over the whole
    /// selection. If the code-behind's <c>Ctrl+A</c> arm were missing, the panel's Print caption would be a
    /// promise the keyboard does not keep — the same class of defect as a panel advertising a function key it
    /// does not bind, which this area has already been caught doing once.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_realised_panel_prints_the_selected_accounts()
    {
        string tempDir = TempDir();
        MainWindow? window = null;
        try
        {
            window = OpenPanel(out var vm, tempDir);
            var boxes = RealisedAccountCheckBoxes(window);
            Assert.True(boxes.Length >= 2, "the Robert fixture must offer at least two accounts to print");

            boxes[0].IsChecked = true;
            boxes[1].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, vm.MultiAccountPrint!.SelectedCount);
            Assert.Null(vm.PrintPreview);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
            Assert.NotEmpty(vm.PrintPreview!.PdfBytes);
            Assert.True(vm.PrintPreview.PageCount >= 2,
                $"Ctrl+A printed {vm.PrintPreview.PageCount} sheet(s) for a two-account job; each selected "
              + "account must start a fresh sheet.");
        }
        finally
        {
            window?.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// <b>THE DOCUMENT-KIND ASSERTION (census 12.7).</b> All three document kinds are realised as clickable
    /// choices. The reminder letter and the confirmation of accounts are reached ONLY from here — they are
    /// multi-account outputs, not standalone documents — so a missing radio makes both unreachable.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_panel_offers_all_three_document_kinds()
    {
        string tempDir = TempDir();
        MainWindow? window = null;
        try
        {
            window = OpenPanel(out var vm, tempDir);
            var captions = RealisedDocumentKindCaptions(window);

            Assert.Contains(captions, c => c.Contains("Ledger Account", StringComparison.Ordinal));
            Assert.Contains(captions, c => c.Contains("Reminder Letter", StringComparison.Ordinal));
            Assert.Contains(captions, c => c.Contains("Confirmation of Accounts", StringComparison.Ordinal));

            // And the choice really drives the projection, not just the radio's own visual state.
            var reminder = window.GetVisualDescendants().OfType<RadioButton>()
                .Single(r => r.GroupName == "MultiAccountDocument"
                          && (r.Content as string) == "Reminder Letter");
            reminder.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(MultiAccountDocumentKind.ReminderLetter, vm.MultiAccountPrint!.DocumentKind);
        }
        finally
        {
            window?.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
