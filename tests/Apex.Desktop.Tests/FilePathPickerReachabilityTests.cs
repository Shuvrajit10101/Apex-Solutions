using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
/// 🔴 <b>T1-20 / census row 13.10 LOCK — there is a file and folder chooser, and a user can actually reach it.</b>
///
/// <para><b>The defect these lock.</b> Every data path in the product was a typed string or a silent default to
/// Documents: the backup destination, the restore source, the import source, the export destination, the
/// <c>.eml</c> hand-off and the print-preview PDF. A search of <c>src/Apex.Desktop</c> for the storage provider,
/// both file dialogs, the folder dialog and the picker options type returned <b>zero hits for all five</b>. So a
/// user restoring from a backup had to type the full archive path from memory — and a mistyped restore path is
/// the difference between a backup feature and a data-loss event.</para>
///
/// <para><b>Why these tests are written this way.</b> This project's carry-forwards record the exact trap: a
/// service method that exists but that no keystroke and no button reaches is <b>not</b> a shipped capability, and
/// a census row closed on one is the worst outcome here. So every assertion below either drives a <b>real
/// keystroke</b> through <see cref="MainWindow"/>'s tunnel handler, or finds a <b>real realised control</b> in the
/// live visual tree. Nothing calls the picker directly to "prove" it works.</para>
///
/// <para><b>The picker itself is faked, and only the picker.</b> A real folder dialog cannot open headlessly, and
/// it is the OS's code, not ours. <see cref="FakePicker"/> stands in for the one seam
/// (<see cref="IFilePathPicker"/>) and records the request it was handed, so the tests can assert we asked the OS
/// for the <i>right shape</i> of thing — a folder for a destination, a file for a source — and that the answer
/// lands in the right field.</para>
/// </summary>
public sealed class FilePathPickerReachabilityTests
{
    /// <summary>Stands in for the OS dialog: records every request, answers with a scripted path (or null = the
    /// user pressed Cancel).</summary>
    private sealed class FakePicker : IFilePathPicker
    {
        private readonly string? _answer;
        public FakePicker(string? answer) => _answer = answer;

        public List<FilePathPickRequest> Requests { get; } = new();

        public Task<string?> PickAsync(FilePathPickRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(_answer);
        }
    }

    private static (MainWindow Window, MainWindowViewModel Vm, FakePicker Picker, string TempDir)
        NewWindow(string company, string? answer)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexPicker_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var picker = new FakePicker(answer);
        var window = new MainWindow { DataContext = vm };
        window.FilePathPicker = picker;
        window.Show();
        Pump(window);
        return (window, vm, picker, tempDir);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Alt+B — the browse chord — driven as a real keystroke through the window's tunnel handler.</summary>
    private static void PressBrowse(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>True iff some realised control in the live visual tree is a button offering to browse.</summary>
    private static bool HasBrowseButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
              .Any(b => b.Content is string s && s.Contains("Browse", StringComparison.OrdinalIgnoreCase));

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ============================================================ the backup destination (the T1-20 headline)

    /// <summary>
    /// The Backup panel's destination is a FOLDER, and Alt+B asks the OS for a folder — not a file — and puts the
    /// answer in <c>Folder</c>, so the resolved full path moves with it.
    /// </summary>
    [AvaloniaFact]
    public void Backup_destination_folder_is_chosen_with_a_folder_picker_not_typed()
    {
        var picked = Path.Combine(Path.GetTempPath(), "ApexPickedBackupDir");
        var (window, vm, picker, dir) = NewWindow("Backup Picker Co", picked);
        try
        {
            vm.OpenBackupCompany();
            Pump(window);
            Assert.Equal(Screen.BackupCompany, vm.CurrentScreen);

            var before = vm.BackupCompanyPanel!.Folder;
            PressBrowse(window);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(FilePathPickKind.Folder, request.Kind);
            Assert.Equal(before, request.StartFolder);

            Assert.Equal(picked, vm.BackupCompanyPanel.Folder);
            Assert.Equal(Path.Combine(picked, vm.BackupCompanyPanel.ResolvedFileName),
                         vm.BackupCompanyPanel.FullPath);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Cancelling the dialog must change nothing — a null answer is "the user changed their mind".</summary>
    [AvaloniaFact]
    public void Cancelling_the_chooser_leaves_the_existing_path_untouched()
    {
        var (window, vm, picker, dir) = NewWindow("Cancel Picker Co", answer: null);
        try
        {
            vm.OpenBackupCompany();
            Pump(window);
            vm.BackupCompanyPanel!.Folder = @"D:\Books\Backups";

            PressBrowse(window);

            Assert.Single(picker.Requests);
            Assert.Equal(@"D:\Books\Backups", vm.BackupCompanyPanel.Folder);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ the restore source

    /// <summary>
    /// The Restore panel's source is a FILE, and the chooser must offer the archive extension — the whole point of
    /// T1-20 is that the archive is found, not remembered.
    /// </summary>
    [AvaloniaFact]
    public void Restore_source_archive_is_chosen_with_an_open_file_picker_filtered_to_the_archive()
    {
        var picked = Path.Combine(Path.GetTempPath(), "Books_20260802-1430.apexbak");
        var (window, vm, picker, dir) = NewWindow("Restore Picker Co", picked);
        try
        {
            vm.OpenRestoreCompany();
            Pump(window);
            Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);

            PressBrowse(window);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(FilePathPickKind.OpenFile, request.Kind);
            Assert.Contains(request.FileTypes.SelectMany(t => t.Patterns), p => p.Contains(".apexbak"));

            Assert.Equal(picked, vm.RestoreCompanyPanel!.FilePath);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ import source + export destination

    [AvaloniaFact]
    public void Import_source_is_chosen_with_an_open_file_picker()
    {
        var picked = Path.Combine(Path.GetTempPath(), "incoming.json");
        var (window, vm, picker, dir) = NewWindow("Import Picker Co", picked);
        try
        {
            vm.OpenImport();
            Pump(window);
            Assert.Equal(Screen.ImportData, vm.CurrentScreen);

            PressBrowse(window);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(FilePathPickKind.OpenFile, request.Kind);
            Assert.Equal(picked, vm.ImportDataPanel!.FilePath);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    [AvaloniaFact]
    public void Export_data_destination_is_chosen_with_a_folder_picker()
    {
        var picked = Path.Combine(Path.GetTempPath(), "ApexPickedExportDir");
        var (window, vm, picker, dir) = NewWindow("Export Picker Co", picked);
        try
        {
            vm.OpenExportData();
            Pump(window);
            Assert.Equal(Screen.ExportData, vm.CurrentScreen);

            PressBrowse(window);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(FilePathPickKind.Folder, request.Kind);
            Assert.Equal(picked, vm.ExportDataPanel!.Folder);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ the two "Save as" screens actually write there

    /// <summary>
    /// The <c>.eml</c> hand-off used to go to a Documents path derived from the document title, with no way to say
    /// otherwise — the shipped code-behind said so in its own comment. Alt+B must write the message to the path
    /// the operator chose, and to no other.
    /// </summary>
    [AvaloniaFact]
    public void The_eml_hand_off_is_written_to_the_chosen_path_not_to_documents()
    {
        var chosenDir = Path.Combine(Path.GetTempPath(), "ApexEmlPick_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chosenDir);
        var chosen = Path.Combine(chosenDir, "statement-to-client.eml");

        var (window, vm, picker, dir) = NewWindow("Eml Picker Co", chosen);
        try
        {
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenEmailCompose();
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);

            vm.EmailCompose!.To = "client@example.com";
            PressBrowse(window);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(FilePathPickKind.SaveFile, request.Kind);
            Assert.EndsWith(".eml", request.SuggestedFileName);

            Assert.True(File.Exists(chosen), "the .eml was not written to the chosen path");
        }
        finally
        {
            window.Close();
            Cleanup(dir);
            try { Directory.Delete(chosenDir, recursive: true); } catch (IOException) { }
        }
    }

    // ============================================================ the affordance is visible, not only bound

    /// <summary>
    /// Keyboard-first does not mean keyboard-only: the chord must also be advertised by a real button on the
    /// panel, or a user who does not already know Alt+B never learns the chooser exists.
    ///
    /// <para>🔴 <b>"Every" is meant literally, and an earlier version of this test did not honour its own name.</b>
    /// It checked four panels — Backup, Restore, Import and Export Data — while <c>BrowseRequest()</c> serves
    /// SEVEN screens. The three it skipped (Export, E-Mail compose, Print Preview) are exactly the ones whose
    /// panels need a live report underneath before they will open, i.e. the ones most likely to be forgotten. A
    /// test whose name claims total coverage and delivers 4/7 is worse than one that claims 4/7, because it is
    /// the thing a reviewer trusts instead of looking. The roster below is now derived from
    /// <c>MainWindowViewModel.BrowseRequest()</c>'s own switch, and the final assertion pins the count so adding
    /// an eighth path screen without a button — or without a case here — fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void Every_path_panel_carries_a_visible_browse_button()
    {
        var (window, vm, _, dir) = NewWindow("Affordance Co", answer: null);
        var checkedScreens = new List<Screen>();

        void Check(string label, Screen expected)
        {
            Pump(window);
            Assert.Equal(expected, vm.CurrentScreen);
            Assert.True(vm.BrowseRequest() is not null,
                $"the {label} panel is on this roster but BrowseRequest() returns null for it — the button " +
                "below would be an affordance for a chooser that never opens.");
            Assert.True(HasBrowseButton(window), $"the {label} panel has no Browse button");
            checkedScreens.Add(expected);
        }

        try
        {
            vm.OpenBackupCompany();
            Check("Backup", Screen.BackupCompany);

            vm.Back();
            vm.OpenRestoreCompany();
            Check("Restore", Screen.RestoreCompany);

            vm.Back();
            vm.OpenImport();
            Check("Import", Screen.ImportData);

            vm.Back();
            vm.OpenExportData();
            Check("Export Data", Screen.ExportData);

            // The remaining three only exist over a live report, which is why they were skipped before.
            vm.Back();
            vm.LoadRobertDemo();
            vm.OpenReport(ReportKind.TrialBalance);

            vm.OpenExport();
            Check("Export", Screen.Export);

            vm.Back();
            vm.OpenEmailCompose();
            Check("E-Mail compose", Screen.EmailCompose);

            vm.Back();
            vm.OpenPrintPreview();
            Check("Print Preview", Screen.PrintPreview);

            Assert.Equal(7, checkedScreens.Distinct().Count());
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ the reservation this slice must not break

    /// <summary>
    /// 🔴 Ctrl+B IS RESERVED (the vendor's "Basis of Values" — a report re-basis that writes nothing). The browse
    /// chord deliberately is <b>not</b> Ctrl+B. This locks that the reservation survived this slice.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_b_is_still_unbound_and_reaches_no_chooser()
    {
        var (window, vm, picker, dir) = NewWindow("Reservation Co", Path.GetTempPath());
        try
        {
            vm.OpenBackupCompany();
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(picker.Requests);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
