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

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-32 — pressing Print twice must not leave two Print Preview columns.</b>
///
/// <para><b>🔴 THE DEFECT, as measured.</b> <c>MainWindowViewModel.PrintMultiAccountJob</c> carried a doc comment
/// stating "the existing preview is replaced rather than stacked — the same one-page-column rule
/// <c>OpenPageColumn</c> enforces". <b>It did neither.</b> It bypassed <c>OpenPageColumn</c> entirely and did a
/// bare <c>Columns.Add(...)</c> with no <c>TrimColumnsAfter</c> and no <c>ClearSubScreens</c>, and it did not copy
/// <c>OpenPrintPreview</c>'s <c>if (PrintPreview is not null) return;</c> guard either. Clicking the panel's Print
/// button three times therefore produced <b>three</b> stacked Print Preview columns, each needing its own Escape
/// to get back to the selection.</para>
///
/// <para><b>Why the KEY path hid it.</b> <c>MainWindow.axaml.cs</c>'s Ctrl+A arm tests
/// <c>Screen.PrintPreview</c> BEFORE <c>Screen.MultiAccountPrint</c>, so the second Ctrl+A saved the PDF instead
/// of reprinting. The button and the accelerator did different things under one caption — which is exactly why
/// this file drives BOTH: the view-model route, and a realised click on the shipped Button.</para>
///
/// <para><b>What the fix may NOT do.</b> It may not route through <c>OpenPageColumn</c>, because that trims back
/// to the last MENU column and would take the Multi-Account Printing panel away with the old preview. The
/// operator's selection has to survive so a second Print reprints it. So the panel column must still be standing
/// afterwards, and every test below asserts that as well as the column count.</para>
/// </summary>
public sealed class MultiAccountPreviewStackingTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ApexMultiStack_" + Guid.NewGuid().ToString("N"));

    private static MainWindowViewModel Shell(string tempDir)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.LoadRobertDemo();
        vm.OpenMultiAccountPrint();
        vm.MultiAccountPrint!.Accounts[0].IsSelected = true;
        return vm;
    }

    private static int PreviewColumns(MainWindowViewModel vm)
        => vm.Columns.Count(c => c.Page is PrintPreviewViewModel);

    private static int PanelColumns(MainWindowViewModel vm)
        => vm.Columns.Count(c => c.Page is MultiAccountPrintViewModel);

    /// <summary>
    /// <b>THE COLUMN-COUNT ASSERTION.</b> Three prints, one preview column — and the panel still there beneath
    /// it. Bite: remove the trim and this reads 3.
    /// </summary>
    [Fact]
    public void Printing_three_times_leaves_exactly_one_preview_column_and_the_panel_beneath_it()
    {
        string tempDir = TempDir();
        try
        {
            var vm = Shell(tempDir);
            int columnsBefore = vm.Columns.Count;

            vm.PrintMultiAccountJob();
            vm.PrintMultiAccountJob();
            vm.PrintMultiAccountJob();

            Assert.Equal(1, PreviewColumns(vm));
            Assert.Equal(1, PanelColumns(vm));
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            // The preview is the rightmost column and the panel is immediately to its left.
            Assert.IsType<PrintPreviewViewModel>(vm.Columns[^1].Page);
            Assert.IsType<MultiAccountPrintViewModel>(vm.Columns[^2].Page);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// <b>REPLACED, not merely deduplicated.</b> Changing the selection and printing again must show the NEW
    /// job. A guard that simply returned early when a preview was already open would satisfy the count assertion
    /// above while leaving the operator staring at the previous job's document.
    /// </summary>
    [Fact]
    public void Re_printing_after_changing_the_selection_shows_the_new_job_not_the_old_one()
    {
        string tempDir = TempDir();
        try
        {
            var vm = Shell(tempDir);
            var panel = vm.MultiAccountPrint!;

            vm.PrintMultiAccountJob();
            var firstPreview = vm.PrintPreview;
            int onePage = firstPreview!.PageCount;

            panel.Accounts[1].IsSelected = true;      // now two accounts
            panel.Accounts[2].IsSelected = true;      // now three
            vm.PrintMultiAccountJob();

            Assert.Equal(1, PreviewColumns(vm));
            Assert.NotSame(firstPreview, vm.PrintPreview);
            Assert.True(vm.PrintPreview!.PageCount > onePage,
                $"the reprint previewed {vm.PrintPreview.PageCount} sheet(s), the same as the one-account job "
              + $"({onePage}) — the old preview is still on screen.");
            // And the shell member points at the column that is actually there.
            Assert.Same(vm.PrintPreview, vm.Columns[^1].Page);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// <b>ONE Escape returns to the selection.</b> This is the operator-visible symptom the stacking produced:
    /// three previews meant three Escapes. Driven through the same Back the Escape key reaches.
    /// </summary>
    [Fact]
    public void One_escape_after_three_prints_returns_to_the_panel()
    {
        string tempDir = TempDir();
        try
        {
            var vm = Shell(tempDir);

            vm.PrintMultiAccountJob();
            vm.PrintMultiAccountJob();
            vm.PrintMultiAccountJob();
            vm.Back();                                // the public route Key.Escape reaches

            Assert.Equal(0, PreviewColumns(vm));
            Assert.Equal(Screen.MultiAccountPrint, vm.CurrentScreen);
            Assert.NotNull(vm.MultiAccountPrint);
            Assert.Equal(1, vm.MultiAccountPrint!.SelectedCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// <b>THE REALISED BUTTON.</b> The defect was reachable from the Print BUTTON and not from Ctrl+A, so a
    /// view-model-only test could be written that never touched the broken path. This clicks the shipped
    /// <see cref="Button"/> the operator clicks, three times, on a realised window.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_the_realised_print_button_three_times_leaves_one_preview_column()
    {
        string tempDir = TempDir();
        MainWindow? window = null;
        try
        {
            var vm = Shell(tempDir);
            window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1280, 800));
            window.Arrange(new Rect(0, 0, 1280, 800));
            Dispatcher.UIThread.RunJobs();

            var print = window.GetVisualDescendants().OfType<Button>()
                .Single(b => (b.Content as string) == "Print (Ctrl+A)");

            // The Button carries a XAML Click handler, not a Command — so raising ClickEvent is the route the
            // operator's click takes. (Verified by mutation: removing the trim turns this into 3.)
            for (int i = 0; i < 3; i++)
            {
                print.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(1, PreviewColumns(vm));
            Assert.Equal(1, PanelColumns(vm));
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        }
        finally
        {
            window?.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
