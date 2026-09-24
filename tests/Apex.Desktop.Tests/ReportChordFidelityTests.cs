using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE REPORT CHORD FAMILY — the fidelity defect that was wrong on all 82 report kinds at once, and the
/// three bulk menus the vendor documents and this build did not have.</b>
///
/// <para>Every claim here is grounded in a vendor page (RULING 14 tier 1), quoted at the test that locks it:
/// <c>help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/</c> for the six Across-TallyPrime chords,
/// and <c>help.tallysolutions.com/use-save-view-feature-in-tallyprime/</c> for Ctrl+L / Ctrl+H.</para>
///
/// <para>🔴 <b>WHY HALF THE TESTS IN THIS FILE ARE ABOUT CHORDS THAT DID NOT MOVE.</b> Every chord this slice
/// takes, it takes beside an incumbent: Ctrl+L was the Optional toggle, Ctrl+H is Change Mode, Ctrl+E is
/// Restore's Examine, Alt+K was Saved Views. A chord is the one thing a user feels immediately and the easiest
/// thing to break invisibly, so for each of those there is a test that the OLD OWNER STILL WORKS WHERE IT
/// SHOULD, not only that the new one works. Those are the tests that matter most in this file; a fully green
/// gate has hidden exactly this class of regression on this project before.</para>
///
/// <para><b>What FAILS on today main, per test, is stated in that test's own summary.</b></para>
/// </summary>
public sealed class ReportChordFidelityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReportChordFidelityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReportChords_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- scaffolding

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindow(string name)
    {
        var vm = NewCompany(name);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        Pump(window);
        return (window, vm);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// Every non-empty string a REALISED, VISIBLE TextBlock is showing. The project's standing preference is a
    /// test that reads the visual tree over one that reads a view-model flag: a flag is exactly the assertion
    /// that passes on the broken build, and this codebase has filed four screens whose flags were perfect and
    /// whose templates drew nothing.
    ///
    /// <para>🔴 <b>IT MUST CONCATENATE THE INLINE RUNS, AND THAT COST THIS SLICE A DEBUGGING CYCLE — SO IT IS
    /// WRITTEN DOWN.</b> A cascade MENU ROW's label is not a <c>Text</c> binding. WI-9 paints the bare-letter
    /// hotkey red, so the row template is three <c>&lt;Run&gt;</c>s inside one TextBlock
    /// (<c>HotKeyBefore</c> / <c>HotKeyText</c> / <c>HotKeyAfter</c>) and <c>TextBlock.Text</c> is therefore
    /// NULL on every selectable menu row in this application. A helper that reads only <c>Text</c> sees the
    /// column HEADERS (which are plain Text) and none of the rows — i.e. it reports a perfectly-rendered menu as
    /// empty. Any future realised-tree test over a menu column needs this shape.</para>
    /// </summary>
    private static List<string> VisibleText(MainWindow window) =>
        Descendants(window)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .Select(TextOf)
            .Where(s => s.Length > 0)
            .ToList();

    /// <summary>A TextBlock's visible string, whether it carries a <c>Text</c> or a run of inlines.</summary>
    private static string TextOf(TextBlock t)
    {
        if (!string.IsNullOrEmpty(t.Text)) return t.Text!;
        if (t.Inlines is null) return string.Empty;
        return string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text ?? string.Empty));
    }

    private static void OpenAReport(MainWindow window, MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.TrialBalance);
        Pump(window);
        Assert.True(vm.IsReportContext);
    }

    // ================================================================ A — Ctrl+L: the vendor's Save View

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main.</b> There, the Optional-toggle arm consumed <c>Ctrl+L</c> UNCONDITIONALLY
    /// (<c>e.Handled = true</c> on every screen in the product) and <c>ToggleOptional()</c> then no-ops off
    /// <c>Screen.VoucherEntry</c> — so on a report the key was swallowed and nothing happened at all.
    ///
    /// <para>Vendor, verbatim (help.tallysolutions.com/use-save-view-feature-in-tallyprime/): "Press Ctrl+L
    /// (Save View) to save the report with the specific configurations."</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_L_on_a_report_opens_save_view()
    {
        var (window, vm) = OpenWindow("Save View Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.SaveView);
            Assert.Equal(Screen.SaveView, vm.CurrentScreen);

            // 🔴 AND THE CAPTION THE OPERATOR READS NAMES THE VENDOR'S CHORD. It used to read
            // "Save View — Ctrl+S" — an Apex-invented key — while this very slice bound Ctrl+L and corrected the
            // Saved-Views empty-state sentence to say so. A caption is a promise about a keystroke; two strings
            // in one feature promising different keys is how a half-done correction survives a green gate.
            // Asserted on the REALISED screen, not the property, because the property is what was already right.
            Assert.Contains(VisibleText(window), s => s.Contains("Ctrl+L", StringComparison.Ordinal));
            Assert.DoesNotContain(VisibleText(window), s => s.Contains("Ctrl+S", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR Ctrl+L's OLD OWNER — the most important test in this file.</b> The
    /// Optional toggle is a SHIPPED voucher behaviour and the new report arm sits directly above it. It passes
    /// before and after; the day it goes red, a report chord broke a voucher chord.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_L_on_a_voucher_still_toggles_optional()
    {
        var (window, vm) = OpenWindow("Optional Toggle Preserved Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Journal);
            Pump(window);
            var entry = vm.VoucherEntry!;
            Assert.False(entry.IsOptional);

            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
            Pump(window);
            Assert.True(entry.IsOptional);          // the shipped verb still answers to the key

            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
            Pump(window);
            Assert.False(entry.IsOptional);         // and it is still a TWO-WAY toggle

            // And the new arm did not leak onto the voucher screen: no Save-View column was pushed.
            Assert.Null(vm.SaveView);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    // ================================================================ B — Ctrl+H: the vendor's Change View

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main</b>, where the only <c>Ctrl+H</c> arm is gated on <c>IsChangeModeEntry</c> — a
    /// VOUCHER predicate — so on a report the chord fell through every arm and did nothing.
    ///
    /// <para>Vendor, verbatim: "press Ctrl+H (Change View), and select the view"; the same menu carries
    /// "Ctrl+H (Change View) &gt; Delete Saved Views" and "&gt; Show Original View".</para>
    ///
    /// <para>This one reads the REALISED VISUAL TREE, not the column model: the three rows must actually be
    /// drawn on screen for the operator to choose one.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_H_on_a_report_draws_the_change_view_menu()
    {
        var (window, vm) = OpenWindow("Change View Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.ChangeViewMenu, vm.CurrentScreen);

            var text = VisibleText(window);
            Assert.Contains(ChangeViewMenu.SavedViewsVerb, text);
            Assert.Contains(ChangeViewMenu.DeleteSavedViewsVerb, text);
            Assert.Contains(ChangeViewMenu.ShowOriginalViewVerb, text);
            // 🔴 A section HEADER is drawn through an Upper converter (MainWindow.axaml's menu-row template), so
            // the honest-disclosure line reaches the screen uppercased. Asserting the literal would fail against
            // a correctly-rendered menu — the same trap as the inline Runs above.
            Assert.Contains(ChangeViewMenu.Disclosure.ToUpperInvariant(), text);

            // 🔴 The shipped brand rule: the reference product's name may never reach a user-visible string.
            Assert.DoesNotContain(text, s => s.Contains("Tally", StringComparison.OrdinalIgnoreCase));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR Ctrl+H's OLD OWNER.</b> Ruling 17 re-homed the item-invoice toggle onto
    /// <c>Ctrl+H</c> (Change Mode) with NO Ctrl+I alias, and <c>ShellChordTable</c>'s remarks warn against ever
    /// "freeing" that chord. The report arm must claim it only where the voucher arm declines.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_H_on_an_invoiceable_voucher_still_changes_mode()
    {
        var (window, vm) = OpenWindow("Change Mode Preserved Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Sales);
            Pump(window);
            var entry = vm.VoucherEntry!;
            var before = entry.IsItemInvoice;

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(window);

            Assert.NotEqual(before, entry.IsItemInvoice);   // Change Mode still cycles
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Ctrl+H &gt; <b>Show Original View</b> reverts the report to a fresh one of the same kind. Vendor,
    /// verbatim: "Ctrl+H (Change View) &gt; Show Original View".
    /// </summary>
    [AvaloniaFact]
    public void Change_view_show_original_view_reopens_the_report_at_its_default()
    {
        var (window, vm) = OpenWindow("Show Original View Co");
        try
        {
            OpenAReport(window, vm);
            var detailedDefault = vm.Reports!.Detailed;
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);   // move it off its default config
            Pump(window);
            Assert.NotEqual(detailedDefault, vm.Reports!.Detailed);

            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.ChangeViewMenu, vm.CurrentScreen);

            // Arrow to the third row and take it, entirely from the keyboard.
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(ChangeViewMenu.ShowOriginalViewVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);
            Assert.Equal(detailedDefault, vm.Reports!.Detailed);   // back at the default configuration
        }
        finally { window.Close(); }
    }

    // ================================================================ C — Alt+P: the vendor's print menu

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main, where Alt+P IS INERT ON EVERY SCREEN IN THE PRODUCT.</b> The bare-P arm reads
    /// <c>!KeyModifiers.HasFlag(Alt)</c>, the Ctrl+P arms require Control, and the bare-letter menu quick-jump
    /// requires <c>KeyModifiers == None</c> — no arm matched Alt+P at all.
    ///
    /// <para>Vendor, verbatim: <c>Alt+P</c> — "To open the print menu for printing transactions or reports."</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_P_on_a_report_draws_the_print_menu()
    {
        var (window, vm) = OpenWindow("Print Menu Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
            var text = VisibleText(window);
            Assert.Contains(ReportPrintMenu.CurrentVerb, text);
            Assert.Contains(ReportPrintMenu.OthersVerb, text);
            Assert.Contains(ReportPrintMenu.Disclosure.ToUpperInvariant(), text);
            Assert.DoesNotContain(text, s => s.Contains("Tally", StringComparison.OrdinalIgnoreCase));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Alt+P &gt; <b>Current</b> reaches the same print preview Ctrl+P reaches — the vendor's own pairing — and
    /// it reaches it through the KEYBOARD, which is the whole point of a menu row.
    /// </summary>
    [AvaloniaFact]
    public void Print_menu_current_row_opens_the_print_preview()
    {
        var (window, vm) = OpenWindow("Print Menu Current Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(ReportPrintMenu.CurrentVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR THE P FAMILY.</b> Bare P still opens the preview directly, and Ctrl+P on
    /// an open preview still opens the Printer column (census 12.5). The new Alt+P arm sits above both.
    /// </summary>
    [AvaloniaFact]
    public void Bare_P_and_Ctrl_P_still_mean_what_they_meant()
    {
        var (window, vm) = OpenWindow("P Family Preserved Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);   // bare P: straight to the preview

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.Printer, vm.CurrentScreen);        // Ctrl+P on a preview: the printer column
        }
        finally { window.Close(); }
    }

    // ================================================================ D — Alt+E / Ctrl+E: the export pair

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main, where Alt+E SILENTLY DID Ctrl+E's JOB.</b> The E arm guarded only
    /// <c>!Control</c>, so bare E and Alt+E both opened the current-object export panel directly.
    ///
    /// <para>Vendor, verbatim: <c>Alt+E</c> — "To open the export menu for exporting masters, transactions, or
    /// reports."</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_E_on_a_report_draws_the_export_menu_instead_of_the_export_panel()
    {
        var (window, vm) = OpenWindow("Export Menu Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);
            Assert.Null(vm.ExportPanel);                            // NOT the current-object panel any more
            var menuText = VisibleText(window);
            Assert.Contains(ReportExportMenu.CurrentVerb, menuText);
            Assert.Contains(ReportExportMenu.Disclosure.ToUpperInvariant(), menuText);

            // …and its Current row still reaches the panel, from the keyboard.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main, where Ctrl+E IS INERT ON A REPORT</b> — the only Key.E + Control arm is scoped
    /// to <c>Screen.RestoreCompany</c>.
    ///
    /// <para>Vendor, verbatim: <c>Ctrl+E</c> — "To export the current voucher or report."</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_E_on_a_report_exports_the_current_report()
    {
        var (window, vm) = OpenWindow("Export Current Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR Ctrl+E's INCUMBENT — and it uses a REAL archive, not a bogus path.</b>
    /// Ctrl+E on the Restore panel Examines the chosen backup; the destructive step is a separate Ctrl+A that
    /// refuses until Examine has passed. If the new report arm had stolen the chord, <c>CanRestore</c> would
    /// stay false and the operator could never restore anything again.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_E_on_the_restore_panel_still_examines_the_backup()
    {
        var (window, vm) = OpenWindow("Restore Examine Preserved Co");
        try
        {
            vm.OpenBackupCompany();
            Pump(window);
            var archive = vm.BackupCompanyPanel!.FullPath;
            Assert.True(vm.ApplyBackup(), vm.BackupCompanyPanel.Status);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            vm.OpenRestoreCompany();
            Pump(window);
            Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);
            vm.RestoreCompanyPanel!.FilePath = archive;
            Assert.False(vm.RestoreCompanyPanel.CanRestore);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
            Pump(window);

            Assert.True(vm.RestoreCompanyPanel.CanRestore, vm.RestoreCompanyPanel.Status);
            Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);   // the export arm did not hijack the screen
            Assert.Null(vm.ExportPanel);
        }
        finally { window.Close(); }
    }

    /// <summary>Bare E still opens the export panel directly — this application's own advertised quick key.</summary>
    [AvaloniaFact]
    public void Bare_E_still_opens_the_export_panel()
    {
        var (window, vm) = OpenWindow("Bare E Preserved Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
        }
        finally { window.Close(); }
    }

    // ================================================================ E — Alt+M: the vendor's Share menu

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY main, where Alt+M IS INERT.</b> The only Key.M arm excludes Alt by construction, so
    /// the vendor's Share chord matched nothing anywhere.
    ///
    /// <para>Vendor, verbatim: <c>Alt+M</c> — "To open the Share menu for sharing transactions or reports
    /// through e-mail or WhatsApp." This also closes IV-64: WhatsApp now hangs off the vendor's own access point
    /// beside e-mail, instead of only an invented W chord.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_M_on_a_report_draws_the_share_menu_with_both_channels()
    {
        var (window, vm) = OpenWindow("Share Menu Chord Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);
            var text = VisibleText(window);
            Assert.Contains(ReportShareMenu.EmailVerb, text);
            Assert.Contains(ReportShareMenu.WhatsAppVerb, text);
            Assert.Contains(ReportShareMenu.Disclosure.ToUpperInvariant(), text);
            Assert.DoesNotContain(text, s => s.Contains("Tally", StringComparison.OrdinalIgnoreCase));

            // The first row reaches the e-mail compose column from the keyboard.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            Assert.NotNull(vm.EmailCompose);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Alt+M &gt; <b>WhatsApp</b> reaches the second channel — the row IV-64 asked for.
    /// </summary>
    [AvaloniaFact]
    public void Share_menu_whatsapp_row_opens_the_whatsapp_column()
    {
        var (window, vm) = OpenWindow("Share Menu WhatsApp Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(ReportShareMenu.WhatsAppVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Assert.NotNull(vm.WhatsAppShare);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR THE M FAMILY.</b> Bare M and Ctrl+M still open the e-mail compose panel
    /// directly; the new Alt+M arm sits above them and must not swallow either.
    /// </summary>
    [AvaloniaFact]
    public void Bare_M_and_Ctrl_M_still_open_the_email_compose_panel()
    {
        var (window, vm) = OpenWindow("M Family Preserved Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    // ================================================================ F — the menus are ACTION menus

    /// <summary>
    /// 🔴 <b>THE DEFECT THE "pop, then act" RULE PREVENTS, LOCKED AS A TEST.</b> The four report menus must NOT
    /// clear the page beneath them the way the company menu does — every row acts on the live report. If a
    /// menu ever starts calling <c>ClearSubScreens</c>, the report goes null and every row becomes a no-op, so
    /// this asserts the report survives underneath and comes back on Escape.
    /// </summary>
    [AvaloniaFact]
    public void The_report_stays_live_beneath_each_of_the_four_menus()
    {
        var (window, vm) = OpenWindow("Menus Are Additive Co");
        try
        {
            foreach (var (key, screen) in new (PhysicalKey, Screen)[]
                     {
                         (PhysicalKey.H, Screen.ChangeViewMenu),
                         (PhysicalKey.P, Screen.PrintMenu),
                         (PhysicalKey.E, Screen.ExportMenu),
                         (PhysicalKey.M, Screen.ShareMenu),
                     })
            {
                OpenAReport(window, vm);
                var modifiers = key == PhysicalKey.H ? RawInputModifiers.Control : RawInputModifiers.Alt;

                window.KeyPressQwerty(key, modifiers);
                Pump(window);
                Assert.Equal(screen, vm.CurrentScreen);
                Assert.NotNull(vm.Reports);                       // the report is STILL BOUND beneath the menu

                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(Screen.Report, vm.CurrentScreen);    // …and Escape lands back on it
                Assert.NotNull(vm.Reports);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE WRONG-ATTACHMENT TEST. This is what <c>PopMenuColumn</c> exists for, and nothing else in the
    /// suite catches it.</b>
    ///
    /// <para><see cref="MainWindowViewModel.OpenEmailCompose"/> decides between "share the drilled voucher" and
    /// "share the report underneath" by reading <c>CurrentScreen == Screen.VoucherDetail</c>. Open the Share
    /// menu over a drilled voucher and, with the menu column still on top, that screen id is
    /// <c>Screen.ShareMenu</c>: the voucher branch misses and <see cref="MainWindowViewModel.IsReportContext"/>
    /// — which excludes VoucherDetail BY SCREEN ID — goes true, so the operator would be handed the REPORT
    /// instead of the invoice they were looking at. Every row pops its own column before acting, and this test
    /// is the lock on that. Make <c>PopMenuColumn</c> a no-op and this goes red while everything else stays
    /// green.</para>
    /// </summary>
    [AvaloniaFact]
    public void Share_menu_over_a_drilled_voucher_shares_the_VOUCHER_not_the_report()
    {
        var (window, vm) = OpenWindow("Share Drilled Voucher Co");
        try
        {
            var company = vm.Company!;
            var rent = new DomainLedger(Guid.NewGuid(), "Rent",
                company.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
            company.AddLedger(rent);
            var cash = company.FindLedgerByName("Cash")!;

            vm.OpenVoucher(VoucherBaseType.Journal);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = rent;
            entry.Lines[0].Side = DrCr.Debit;
            entry.Lines[0].AmountText = "1500";
            entry.Lines[1].SelectedLedger = cash;
            entry.Lines[1].Side = DrCr.Credit;
            entry.Lines[1].AmountText = "1500";
            Assert.True(entry.Accept());
            var voucherId = company.Vouchers.Last().Id;

            vm.OpenVoucherDetail(voucherId);
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            var voucherTitle = vm.VoucherDetail!.Title;

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            // The compose panel is built FROM the drilled voucher, so its subject/attachment name it. If the
            // menu column had still been on top, this panel would have been built from a report instead.
            Assert.Contains(voucherTitle, vm.EmailCompose!.Subject);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE SAME DEFECT ON THE EXPORT SIDE.</b> <c>BuildExportPanel</c> reads <c>TopMasterExportSource()</c>
    /// — the TOP cascade column. With the export menu still on top, a master list would export as whatever
    /// report was last bound, or as nothing at all. Alt+E on the Chart of Accounts must still export the Chart
    /// of Accounts.
    /// </summary>
    [AvaloniaFact]
    public void Export_menu_over_a_master_list_exports_the_MASTER_LIST()
    {
        var (window, vm) = OpenWindow("Export Master List Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
            Assert.True(vm.IsExportablePage);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
            // DocumentTitle is what the panel was BUILT FROM (its Title is the constant "Export"), so this is the
            // assertion that distinguishes "exported the master list" from "exported some report".
            Assert.Contains("Chart of Accounts", vm.ExportPanel!.DocumentTitle, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE SHARE MENU HAS NO MASTER-LIST HALF, AND BEFORE THIS TEST IT PRETENDED TO — Alt+M DREW A MENU
    /// WHOSE EVERY ROW WAS DEAD.</b> <c>OpenShareMenu</c> was gated on <c>IsPrintablePage</c> under a comment
    /// reading "the gate both channels already carry". It is not that gate. Printability has a third arm,
    /// <c>TopMasterExportSource()</c>, so it is TRUE on every master list in the product; but
    /// <c>OpenEmailCompose</c> and <c>OpenWhatsAppShare</c> each accept only a drilled voucher or a live report
    /// and <c>return</c> otherwise. On the Chart of Accounts the operator therefore got a <i>Share</i> column
    /// with <i>E-Mail</i> and <i>WhatsApp</i> painted and lettered, and taking either one popped the column and
    /// did nothing at all.
    ///
    /// <para><b>It is the same shape of bug as the one this branch was withheld for, one step milder.</b> There,
    /// a comment asserted an invariant the code did not hold and a letter reached the wrong document; here, a
    /// comment asserted a gate the code did not have and a row reached nothing. Both were green on the full
    /// gate, and both are invisible to any assertion about keystrokes — "Alt+M opened the Share menu" passed.</para>
    ///
    /// <para><b>What this asserts is the two halves that matter:</b> the menu does not open where it cannot act,
    /// AND Alt+M is not swallowed there (a handled-but-inert key is the dead-chord defect census 14.4 was graded
    /// ABSENT for). Export is exercised alongside as the control — it genuinely DOES have a master-list half, so
    /// a fix that simply narrowed every menu would fail this test.</para>
    ///
    /// <para>🔴 <b>FAILS WITHOUT THE FIX</b> at the first assertion: <c>Expected: ChartOfAccounts / Actual:
    /// ShareMenu</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_M_over_a_master_list_draws_no_menu_because_neither_channel_can_act()
    {
        var (window, vm) = OpenWindow("Share Master List Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);

            // The premise, measured rather than assumed: this page IS printable and IS exportable…
            Assert.True(vm.IsPrintablePage);
            Assert.True(vm.IsExportablePage);
            // …and is NOT shareable, because neither channel can build a document from a master list.
            Assert.False(vm.IsShareablePage);

            var depth = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);

            // No menu was drawn, and nothing was pushed onto the cascade.
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
            Assert.Equal(depth, vm.Columns.Count);

            // Prove the rows really cannot act, so the gate is not merely a taste call: call both channels
            // directly on this page and watch each refuse to construct a panel.
            vm.OpenEmailCompose();
            vm.OpenWhatsAppShare();
            Pump(window);
            Assert.Null(vm.EmailCompose);
            Assert.Null(vm.WhatsAppShare);
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);

            // THE CONTROL: Export's menu has a real master-list half and must be untouched by the narrowing.
            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The Share menu still opens, and both of its rows still act, on the two pages that CAN share — the live
    /// report and the drilled voucher. The narrowing above must not have taken the menu away from either.
    /// </summary>
    [AvaloniaFact]
    public void The_share_menu_still_opens_on_both_pages_that_can_actually_share()
    {
        var (window, vm) = OpenWindow("Share Still Opens Co");
        try
        {
            // (1) the live report
            OpenAReport(window, vm);
            Assert.True(vm.IsShareablePage);
            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // (2) the drilled voucher — the page the confidentiality defect was measured on.
            var voucherId = PostAJournal(vm, "Share Gate Expense", 410m);
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.OpenVoucherDetail(voucherId);
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                        // the Day Book is still bound beneath
            Assert.True(vm.IsShareablePage);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);

            // And the WhatsApp row still reaches the VOUCHER by its painted letter — the confidentiality
            // assertion, re-run through the narrowed gate so the fix above cannot have re-opened it.
            var voucherTitle = vm.VoucherDetail!.Title;
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Assert.Equal(voucherTitle, vm.WhatsAppShare!.DocumentTitle);
            Assert.DoesNotContain("Day Book", vm.WhatsAppShare!.DocumentTitle, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE <i>Delete Saved Views</i> ROW LED TO A VERB NO KEYBOARD COULD REACH, AND THAT IS THIS
    /// PROJECT'S NAMED COMPLETENESS FAILURE, NOT A NICETY.</b> The row itself was reachable — Ctrl+H, then its
    /// painted <b>D</b> — and it brought the operator to the Saved-Views panel with the right status line. But
    /// the ONLY route to <c>DeleteSelectedSavedView</c> anywhere in the product was a mouse <c>Click</c> handler
    /// (<c>MainWindow.axaml.cs: OnDeleteSavedViewClick</c>): <c>ActivateSelected</c> had no
    /// <c>Screen.SavedViews</c> case at all, so the panel's bare Enter did nothing, and Ctrl+A OPENED the view
    /// instead. A row whose verb has no keystroke is the dead end census 14.4 was graded ABSENT for, and this
    /// branch ADDED the row.
    ///
    /// <para><b>The flow asserted here is the vendor's own, quoted</b>
    /// (help.tallysolutions.com/use-save-view-feature-in-tallyprime/, <i>Delete Saved View of a Report</i>,
    /// opened by content): Ctrl+H (Change View) → <i>Delete Saved Views</i> → choose the view and press
    /// <b>Enter</b> → <i>"Press Enter or Y to confirm deletion"</i>. Two presses, because this destroys
    /// something; one press must NOT delete.</para>
    ///
    /// <para>🔴 <b>AND THE ARMING IS PER-ROW, WHICH IS THE ASSERTION WITH TEETH.</b> Moving the highlight after
    /// the first Enter throws the confirmation away. Without that, a stale confirmation deletes whichever row
    /// the operator arrowed onto afterwards — the exact mis-target this shell has already filed once, where
    /// Alt+X raised a cancellation for the voucher BEHIND the column the operator was standing in.</para>
    ///
    /// <para>🔴 <b>FAILS WITHOUT THE FIX</b> at "the second Enter deleted it": on today's branch the store still
    /// holds both views, because no keystroke ever reached Delete.</para>
    /// </summary>
    [AvaloniaFact]
    public void Change_view_delete_saved_views_row_can_delete_from_the_keyboard_and_needs_two_presses()
    {
        var (window, vm) = OpenWindow("Delete Saved Views Co");
        try
        {
            // Two saved views, made the way an operator makes them: Ctrl+L on a report, name it, accept.
            foreach (var name in new[] { "Alpha View", "Beta View" })
            {
                vm.OpenReport(ReportKind.TrialBalance);
                Pump(window);
                window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
                Pump(window);
                Assert.Equal(Screen.SaveView, vm.CurrentScreen);
                vm.SaveView!.Name = name;
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                Pump(window);
            }

            OpenAReport(window, vm);

            // Ctrl+H, then the letter the product paints on "Delete Saved Views".
            var letter = PaintedLetterOf(window, vm, ChangeViewMenu.DeleteSavedViewsVerb);
            window.KeyPressQwerty(PhysicalKeyFor(letter), RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
            var panel = vm.SavedViews!;
            Assert.True(panel.IsDeleteMode);          // arrived by the DELETE row, not the open row
            Assert.Equal(2, panel.Views.Count);

            var doomed = panel.Views[0].Name;
            var survivor = panel.Views[1].Name;
            panel.Selected = panel.Views[0];
            Pump(window);

            // PRESS ONE — arms, names the view it is about to delete, and DELETES NOTHING.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, panel.Views.Count);
            Assert.Contains(doomed, panel.Status, StringComparison.Ordinal);

            // MOVING THE HIGHLIGHT CANCELS IT — the stale-confirmation mis-target.
            panel.Selected = panel.Views[1];
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);   // re-arms on the NEW row
            Pump(window);
            Assert.Equal(2, panel.Views.Count);
            Assert.Contains(survivor, panel.Status, StringComparison.Ordinal);

            // Back to the doomed row: one press to arm, a second to delete.
            panel.Selected = panel.Views[0];
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, panel.Views.Count);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            // 🔴 THE ASSERTION THAT SEES IT: the LIST, not the keystroke. Exactly one view is gone, and it is
            // the one that was armed.
            Assert.Single(panel.Views);
            Assert.Equal(survivor, panel.Views[0].Name);
            Assert.DoesNotContain(panel.Views, v => v.Name == doomed);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The OTHER Ctrl+H row onto the same panel must be unchanged: arriving by <i>Saved Views</i>, Enter still
    /// OPENS (applies) the highlighted view and deletes nothing. The delete door above is a second meaning for
    /// one key, and the whole risk of that is the first meaning quietly becoming destructive.
    /// </summary>
    [AvaloniaFact]
    public void Saved_views_row_still_opens_and_never_deletes()
    {
        var (window, vm) = OpenWindow("Saved Views Open Co");
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
            Pump(window);
            vm.SaveView!.Name = "Kept View";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            OpenAReport(window, vm);
            var letter = PaintedLetterOf(window, vm, ChangeViewMenu.SavedViewsVerb);
            window.KeyPressQwerty(PhysicalKeyFor(letter), RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
            Assert.False(vm.SavedViews!.IsDeleteMode);
            Assert.Single(vm.SavedViews!.Views);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            // It APPLIED the view — a live report is the active pane again — and the view still exists.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);

            OpenAReport(window, vm);
            var again = PaintedLetterOf(window, vm, ChangeViewMenu.SavedViewsVerb);
            window.KeyPressQwerty(PhysicalKeyFor(again), RawInputModifiers.None);
            Pump(window);
            Assert.Single(vm.SavedViews!.Views);
            Assert.Equal("Kept View", vm.SavedViews!.Views[0].Name);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Re-pressing a menu's own chord while that menu is up must not stack a second copy of it — the same
    /// re-entrancy rule every panel opener in this shell carries.
    /// </summary>
    [AvaloniaFact]
    public void Re_pressing_a_menu_chord_does_not_stack_a_second_column()
    {
        var (window, vm) = OpenWindow("No Stacking Co");
        try
        {
            OpenAReport(window, vm);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            var depth = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(depth, vm.Columns.Count);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    // ================================================================ F — THE CONFIDENTIALITY DEFECT
    //
    // 🔴 THIS SECTION EXISTS BECAUSE THIS BRANCH WAS WITHHELD FROM MERGE, ON A FULLY GREEN GATE, FOR A DEFECT
    // THAT SENT THE WRONG DOCUMENT TO A THIRD PARTY. Everything above this line passed while it was live.
    //
    // WHAT THE TESTS ABOVE ASSERT, AND WHY IT WAS NOT ENOUGH. The two wrong-attachment tests above drive each
    // menu with ENTER. Enter runs the row, the row calls PopMenuColumn, and the screen id is restored before the
    // verb reads it — so the Enter route was correct all along and green. The operator's other route is the
    // LETTER the product itself paints red on the row (WI-9). That letter never reached the row: the bare-letter
    // arms in MainWindow (E / P / M / W) are gated on IsPrintablePage / IsExportablePage, both of which STAYED
    // TRUE under a menu column because PushMenuColumn deliberately does not ClearSubScreens, and those arms sit
    // hundreds of lines EARLIER in the first-match-wins chain than HandleMenuLetter. So the letter fired the
    // bare verb with the menu still on top, and the pop never happened.
    //
    // 🔴 THESE ASSERT ON THE DOCUMENT, NOT ON THE KEYSTROKE. A test reading "Alt+M opened a menu", or even
    // "W opened the WhatsApp panel", passed throughout the defect — the panel opened perfectly, built from the
    // wrong thing. The only assertion that sees it is WHICH DOCUMENT came out, so these read DocumentTitle and
    // then the BYTES OF THE EMITTED FILE.

    /// <summary>
    /// 🔴 <b>THE CONFIDENTIALITY TEST. Drill one voucher out of the Day Book, ask to share it on WhatsApp by the
    /// product's own painted letter, and the file that comes out must be THAT VOUCHER.</b>
    ///
    /// <para>What was measured before the fix: Alt+M put the Share menu up (CurrentScreen = ShareMenu, with
    /// Reports still bound to the Day Book beneath). W then matched the bare-W arm, whose guard
    /// <c>IsPrintablePage</c> was still true, and called <c>OpenWhatsAppShare()</c> with the menu column in
    /// place. Its voucher branch tests <c>CurrentScreen == Screen.VoucherDetail</c> and missed;
    /// <c>IsReportContext</c> — which excludes VoucherDetail BY SCREEN ID — went true; the panel was built from
    /// the REPORT. An operator sharing one invoice with one customer was handed a document titled "Day Book":
    /// every voucher of every party in the period. That is a disclosure of other customers' data, not a routing
    /// bug, which is why the emitted FILE is asserted here and not merely the panel's title.</para>
    ///
    /// <para>The fix is <c>MainWindowViewModel.IsActionMenuColumn</c>, a clause on all three page predicates, so
    /// that no arm outside an action menu can claim a bare letter while one is up and the letter reaches the row
    /// the product painted it on.</para>
    ///
    /// <para>🔴 <b>WHAT THIS TEST ACTUALLY CATCHES, MEASURED BY MUTATION RATHER THAN ASSERTED.</b> An earlier
    /// draft of this remark claimed "delete that clause and this test goes red". That claim was run and is
    /// <b>FALSE for either clause on its own</b>, because the clauses are deliberately redundant — the sentence
    /// is corrected here rather than left standing, since a comment asserting something the code does not hold is
    /// precisely how the defect below shipped. The three measurements, each a separate build and run of this
    /// class (29 tests):
    /// <list type="bullet">
    /// <item>Neutralise <c>!IsActionMenuColumn</c> on <see cref="MainWindowViewModel.IsPrintablePage"/> alone →
    /// <b>29/29 still pass.</b> That clause is the belt of belt-and-braces; <c>IsReportContext</c> is still false
    /// under the menu, so the bare-letter arm stays shut.</item>
    /// <item>Neutralise it on <see cref="MainWindowViewModel.IsReportContext"/> alone → <b>1 failure</b>, and it
    /// is <c>All_four_action_menus_make_the_page_predicates_false</c>, NOT this test:
    /// <c>IsPrintablePage</c>'s own clause still guards the W arm.</item>
    /// <item>Neutralise <see cref="MainWindowViewModel.IsActionMenuColumn"/> itself — the true pre-fix code —
    /// → <b>3 failures</b>: this test, its e-mail twin, and the predicate test. This test's message is the defect
    /// verbatim: <c>Expected: "Journal No. 1" / Actual: "Day Book"</c>.</item>
    /// </list>
    /// So the honest statement is: <b>this test is the only assertion in the suite that sees the BREACH</b> (the
    /// predicate test sees only the predicates), and it fires when the fix is genuinely absent — but the two
    /// clauses each independently close the hole, so no single-clause edit can be used to prove it live. Revert
    /// <c>IsActionMenuColumn</c> to <c>false</c> if you need to watch it fail.</para>
    /// </summary>
    [AvaloniaFact]
    public void Share_menu_W_over_a_drilled_voucher_emits_the_VOUCHER_document_not_the_Day_Book()
    {
        var (window, vm) = OpenWindow("Confidentiality Co");
        try
        {
            var voucherId = PostAJournal(vm, "Rent", 1500m);

            // Stand where the operator stands: the Day Book, drilled into ONE voucher. Reports stays bound to
            // the Day Book beneath the drill column — that is the whole hazard.
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            var dayBookTitle = vm.Reports!.Title;
            // The Day Book's OWN shared bytes, produced through the shipped path (same ctor the Share menu uses,
            // same SaveDocument that writes the file) — no test-only seam. This is the document the defect
            // handed out; the assertion at the end is that the emitted file is NOT these bytes.
            var dayBookReference = Path.Combine(_tempDir, "daybook-reference.pdf");
            Assert.True(new WhatsAppShareViewModel(vm.Reports!).SaveDocument(dayBookReference));
            var dayBookBytes = File.ReadAllBytes(dayBookReference);

            vm.OpenVoucherDetail(voucherId);
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                       // the Day Book really is still bound beneath
            var voucherTitle = vm.VoucherDetail!.Title;
            var voucherBytes = vm.VoucherDetail!.BuildPrintPreview().PdfBytes;
            Assert.NotEqual(dayBookTitle, voucherTitle);      // the two documents are genuinely different

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);

            // 🔴 THE DEFECT'S OWN KEYSTROKE: the bare W the Share menu paints on its WhatsApp row.
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Assert.NotNull(vm.WhatsAppShare);
            Assert.Equal(voucherTitle, vm.WhatsAppShare!.DocumentTitle);
            Assert.DoesNotContain("Day Book", vm.WhatsAppShare!.DocumentTitle, StringComparison.OrdinalIgnoreCase);

            // 🔴 AND THE ACTUAL FILE, because the title is a label and the file is what leaves the building.
            var path = Path.Combine(_tempDir, "shared.pdf");
            Assert.True(vm.SaveWhatsAppDocument(path));
            var emitted = File.ReadAllBytes(path);
            Assert.Equal(voucherBytes, emitted);
            Assert.NotEqual(dayBookBytes, emitted);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE SAME DEFECT ON THE E-MAIL CHANNEL, BY THE SAME LETTER ROUTE.</b> The Share menu's first row is
    /// <i>E-Mail</i>, so WI-9 paints <b>E</b> on it — and the bare-E arm (guard <c>IsExportablePage</c>) sat
    /// earlier in the chain. Before the fix this opened the EXPORT panel for the Day Book: a different panel
    /// entirely from the one the operator chose, over a different document. Asserts the compose panel names the
    /// voucher, which is what decides the attachment.
    /// </summary>
    [AvaloniaFact]
    public void Share_menu_E_over_a_drilled_voucher_composes_for_the_VOUCHER()
    {
        var (window, vm) = OpenWindow("Share Letter EMail Co");
        try
        {
            var voucherId = PostAJournal(vm, "Printing", 900m);

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.OpenVoucherDetail(voucherId);
            Pump(window);
            var voucherTitle = vm.VoucherDetail!.Title;

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            Assert.Null(vm.ExportPanel);                      // NOT the export panel the bare-E arm used to open
            Assert.Contains(voucherTitle, vm.EmailCompose!.Subject);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE EXPORT MENU'S OWN LETTER, over a MASTER LIST — and the letter is READ OFF THE COLUMN, not
    /// hard-coded.</b>
    ///
    /// <para>🔴 <b>THIS TEST PREVIOUSLY ASSERTED A FALSE PREMISE AND WAS RED.</b> It pressed <b>E</b>, on the
    /// stated belief that E is "painted on <i>Current</i>". It is not. <c>GatewayColumn.AssignHotKeys</c> takes
    /// the label's first FREE letter, and the label is <i>Current</i> — so the letter this menu actually paints
    /// is <b>C</b>. Pressing E therefore did nothing at all once the fix landed (the bare-E arm is correctly
    /// dead under a menu column, and no row answers to E), and the menu just stayed up. The premise was wrong,
    /// not the fix.</para>
    ///
    /// <para>So the letter is now taken from <see cref="MenuItemViewModel.HotKey"/> on the live column — the
    /// very character the product paints red on the row. That is the operator's actual contract ("press the red
    /// letter"), and it cannot rot when a label changes: rename <i>Current</i> and this test follows it, where a
    /// hard-coded letter would have gone quietly red again. The <c>Assert.NotEqual('E', …)</c> pins the specific
    /// mistake so it is not re-introduced.</para>
    ///
    /// <para>What it proves: the row runs against the MASTER LIST. Before the fix <c>OpenExport()</c> reached by
    /// a bare arm ran with the menu column still on top, where <c>TopMasterExportSource()</c> reads the MENU as
    /// the top column and finds no page — so the Chart of Accounts would have exported as whatever report was
    /// last bound, or not at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void Export_menu_letter_over_a_master_list_exports_the_MASTER_LIST()
    {
        var (window, vm) = OpenWindow("Export Letter Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.True(vm.IsExportablePage);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);

            // The letter the product itself paints on the Current row, read off the live column.
            var row = vm.Columns[^1].Items.First(i => i.IsSelectable);
            Assert.Equal(ReportExportMenu.CurrentVerb, row.Label);
            Assert.True(row.HasHotKey);
            var painted = char.ToUpperInvariant(row.HotKey!.Value);
            Assert.Equal('C', painted);
            Assert.NotEqual('E', painted);   // the false premise this test used to carry

            window.KeyPressQwerty(PhysicalKeyFor(painted), RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
            Assert.Contains("Chart of Accounts", vm.ExportPanel!.DocumentTitle, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>Maps an uppercase A–Z to its QWERTY <see cref="PhysicalKey"/>, so a test can press a letter it
    /// read off a menu row rather than one it hard-coded.</summary>
    private static PhysicalKey PhysicalKeyFor(char upper) =>
        upper is >= 'A' and <= 'Z'
            ? (PhysicalKey)Enum.Parse(typeof(PhysicalKey), upper.ToString())
            : throw new ArgumentOutOfRangeException(nameof(upper), upper, "Not an A-Z letter.");

    /// <summary>
    /// 🔴 <b>THE INVARIANT ITSELF, stated as an assertion instead of as a comment — which is the lesson this
    /// defect taught.</b> <c>GatewayColumn.ReservedLetters</c> reserves only O and Y and justifies itself by
    /// asserting in prose that <c>IsExportablePage</c> / <c>IsPrintablePage</c> "are both false while a menu
    /// column is on top". That sentence was FALSE for the four action menus for as long as they existed, and
    /// nothing in the suite could tell. It is now enforced by <c>IsActionMenuColumn</c> and pinned here, for all
    /// four menus, so the comment and the code cannot drift apart again.
    /// </summary>
    [AvaloniaFact]
    public void All_four_action_menus_make_the_page_predicates_false()
    {
        var (window, vm) = OpenWindow("Menu Invariant Co");
        try
        {
            var voucherId = PostAJournal(vm, "Freight", 250m);
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);

            foreach (var (chord, screen) in new[]
                     {
                         (PhysicalKey.P, Screen.PrintMenu),
                         (PhysicalKey.E, Screen.ExportMenu),
                         (PhysicalKey.M, Screen.ShareMenu),
                     })
            {
                window.KeyPressQwerty(chord, RawInputModifiers.Alt);
                Pump(window);
                Assert.Equal(screen, vm.CurrentScreen);

                Assert.True(vm.IsActionMenuColumn);
                Assert.False(vm.IsReportContext);
                Assert.False(vm.IsPrintablePage);
                Assert.False(vm.IsExportablePage);
                Assert.NotNull(vm.Reports);      // …and the report is STILL BOUND, which is the point of them

                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(Screen.Report, vm.CurrentScreen);
                Assert.True(vm.IsReportContext);   // the predicates come straight back when the menu pops
            }

            // Ctrl+H is the fourth, and it is a Control chord rather than an Alt one.
            window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.ChangeViewMenu, vm.CurrentScreen);
            Assert.True(vm.IsActionMenuColumn);
            Assert.False(vm.IsReportContext);
            Assert.False(vm.IsPrintablePage);
            Assert.False(vm.IsExportablePage);

            Assert.NotEqual(Guid.Empty, voucherId);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION PROOF FOR THE FIX ITSELF.</b> Narrowing three predicates is exactly the shape of
    /// change that fixes a symptom by disabling a feature, and this project has shipped that before. The bare
    /// letters must still do their shipped jobs everywhere an action menu is NOT up: W on a drilled voucher
    /// still shares that voucher directly, and E on a report still opens Export. If the guard were written too
    /// wide, these go red.
    /// </summary>
    [AvaloniaFact]
    public void Bare_letters_still_work_where_no_action_menu_is_up()
    {
        var (window, vm) = OpenWindow("Bare Letters Preserved Co");
        try
        {
            var voucherId = PostAJournal(vm, "Stationery", 400m);
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);

            // E on the live report still opens the current-object Export panel (no menu in the way).
            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Export, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            // W straight off a drilled voucher still shares THAT VOUCHER — the shipped IV-64 door, untouched.
            vm.OpenVoucherDetail(voucherId);
            Pump(window);
            var voucherTitle = vm.VoucherDetail!.Title;

            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            Assert.Equal(voucherTitle, vm.WhatsAppShare!.DocumentTitle);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE CLASS, NOT THE INSTANCE: the OTHER bare letter that a menu paints, on the one report where it
    /// is also a live verb.</b>
    ///
    /// <para>The reviewed defect was found on <b>W</b>. Fixing only W would have left the class open, so the
    /// remaining bare-letter arms were enumerated in <c>MainWindow.axaml.cs</c>. Exactly one is a no-modifier
    /// letter outside the P/E/M/W family: <b>bare C</b> (<c>:812</c>), "convert the highlighted memorandum",
    /// gated on <c>IsMemorandumRegisterReport</c>. And <b>C is the letter <c>AssignHotKeys</c> paints on the
    /// <i>Current</i> row of BOTH the Print and the Export menu</b> — the label's first free letter. So on the
    /// Memorandum Register, and nowhere else, a menu's own painted letter and a live bare verb are the same
    /// key.</para>
    ///
    /// <para>🔴 <b>This one is safe for a DIFFERENT reason than the W family, and that is exactly why it is
    /// pinned here rather than assumed.</b> <c>IsMemorandumRegisterReport</c> requires
    /// <c>CurrentScreen == Screen.Report</c>, so it is false under a menu column by screen id — it never
    /// depended on the three page predicates <c>IsActionMenuColumn</c> fixed. Nothing in the fix protects it;
    /// it is safe by its own construction, and a later hand relaxing that clause to
    /// <c>Reports is { Kind: MemorandumRegister }</c> — which reads like a harmless widening, and is the exact
    /// shape <c>IsChequeRegisterDetailReport</c> next door uses — would silently reopen the class on a new
    /// letter. Then Alt+P then C would CONVERT A MEMORANDUM INTO A REAL VOUCHER instead of printing: a posting
    /// to the books from a keystroke the operator pressed to print.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_menu_letter_that_is_also_a_live_bare_verb_runs_the_MENU_row()
    {
        var (window, vm) = OpenWindow("Memorandum Letter Co");
        try
        {
            vm.OpenReport(ReportKind.MemorandumRegister);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.True(vm.IsMemorandumRegisterReport);   // the bare-C verb really is live on this page

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);

            // C is what the Print menu paints on Current — and it is the live verb's letter too.
            var current = vm.Columns[^1].Items.First(i => i.IsSelectable);
            Assert.Equal(ReportPrintMenu.CurrentVerb, current.Label);
            Assert.Equal('C', char.ToUpperInvariant(current.HotKey!.Value));
            Assert.False(vm.IsMemorandumRegisterReport);  // …and it is dead while the menu is up

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);

            // The MENU ROW ran: the preview opened. Not the conversion verb.
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
        }
        finally { window.Close(); }
    }

    // ============================================ G — EVERY ROW REACHABLE BY THE LETTER THE PRODUCT PAINTS
    //
    // 🔴 WHY THIS SECTION WAS ADDED ON TOP OF A GREEN SECTION F. Section F drives the menus by ENTER and, for
    // the three rows the confidentiality defect touched, by their painted LETTER. That leaves rows whose ONLY
    // proven door is Enter — and the defect above was precisely a row whose Enter route was green while its
    // letter route handed a third party the wrong document. "Reachable from the keyboard" is the project's
    // completeness bar, and the letter is half of the keyboard here: the product paints it red on the row, so
    // it is a promise to the operator. Every selectable row of all four menus is driven by its own painted
    // letter below.

    /// <summary>
    /// 🔴 <b>EVERY SELECTABLE ROW OF ALL FOUR MENUS IS PAINTED WITH A LETTER, AND NO TWO ROWS OF A MENU SHARE
    /// ONE.</b> <c>GatewayColumn.AssignHotKeys</c> leaves <c>HotKeyIndex</c> at −1 when a row's every letter is
    /// already claimed — deliberately, because a missing accelerator beats two rows answering one key — so a row
    /// silently having NO door is a real outcome of the assigner, not a hypothetical. It is one relabel away at
    /// any time: <b>O</b> is reserved, and <i>Others</i> begins with O.
    ///
    /// <para>The <c>Assert.DoesNotContain</c> on the reserved set is the second half. A painted letter that is
    /// also a reserved bare accelerator is the exact shape of the defect this branch was withheld for — a red
    /// letter on a row that does something else entirely.</para>
    /// </summary>
    [AvaloniaFact]
    public void Every_action_menu_row_is_painted_with_its_own_unreserved_letter()
    {
        var (window, vm) = OpenWindow("Painted Letters Co");
        try
        {
            OpenAReport(window, vm);

            foreach (var (key, mods, screen) in new[]
                     {
                         (PhysicalKey.H, RawInputModifiers.Control, Screen.ChangeViewMenu),
                         (PhysicalKey.P, RawInputModifiers.Alt, Screen.PrintMenu),
                         (PhysicalKey.E, RawInputModifiers.Alt, Screen.ExportMenu),
                         (PhysicalKey.M, RawInputModifiers.Alt, Screen.ShareMenu),
                     })
            {
                window.KeyPressQwerty(key, mods);
                Pump(window);
                Assert.Equal(screen, vm.CurrentScreen);

                var rows = vm.Columns[^1].Items.Where(i => i.IsSelectable).ToList();
                Assert.NotEmpty(rows);

                var painted = new List<char>();
                foreach (var row in rows)
                {
                    Assert.True(row.HasHotKey, $"{screen}: row '{row.Label}' has NO painted letter — unreachable.");
                    var letter = char.ToUpperInvariant(row.HotKey!.Value);
                    Assert.Contains(letter, row.Label.ToUpperInvariant());   // it really is a letter OF the label
                    painted.Add(letter);
                }

                // O and Y are the bare Gateway accelerators GatewayColumn reserves; no row may wear one.
                Assert.DoesNotContain('O', painted);
                Assert.DoesNotContain('Y', painted);
                Assert.Equal(painted.Count, painted.Distinct().Count());     // no two rows share a letter

                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(Screen.Report, vm.CurrentScreen);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE ONE MENU ROW NO TEST REACHED BY ITS LETTER, AND THE LETTER IS A LOADED ONE.</b> <i>Others</i>
    /// cannot be painted with <b>O</b> — O is reserved (it is the Gateway's bare Import key) — so
    /// <c>AssignHotKeys</c> walks the label and lands on the next free letter, <b>T</b>. And <b>T is one of the
    /// four bare-letter QUICK-JUMPS</b> in <c>MainWindow.axaml.cs</c> (B/P/T/D, the button-bar jumps). So this
    /// row's painted letter collides by name with a live navigation verb, exactly as <b>W</b> did on the Share
    /// menu and <b>C</b> does on the Memorandum Register.
    ///
    /// <para><b>It is safe for a THIRD reason again, which is why it is pinned rather than assumed.</b> The
    /// quick-jumps are gated on <c>CanQuickJump</c> → <c>vm.IsMenuScreen</c>, and <c>IsMenuScreen</c> requires
    /// <c>Reports is null</c> and <c>!IsGatewayCascade</c> — it describes the PRE-COMPANY centred menus only, so
    /// it is false over any report. Neither <c>IsActionMenuColumn</c> nor <c>ReservedLetters</c> protects this
    /// one; a later hand widening <c>CanQuickJump</c> to "any menu column" would read like a generalisation and
    /// would silently make Alt+P then T open the Trial Balance instead of the multi-account print job.</para>
    /// </summary>
    [AvaloniaFact]
    public void Print_menu_Others_painted_letter_opens_the_multi_account_print_job()
    {
        var (window, vm) = OpenWindow("Print Others Letter Co");
        try
        {
            OpenAReport(window, vm);
            Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);

            var row = vm.Columns[^1].Items.Last(i => i.IsSelectable);
            Assert.Equal(ReportPrintMenu.OthersVerb, row.Label);
            Assert.True(row.HasHotKey);
            var painted = char.ToUpperInvariant(row.HotKey!.Value);
            Assert.NotEqual('O', painted);   // O is reserved — the assigner had to walk past the first letter
            Assert.Equal('T', painted);

            window.KeyPressQwerty(PhysicalKeyFor(painted), RawInputModifiers.None);
            Pump(window);

            // The MENU ROW ran. Not the T quick-jump, which would have swapped the report underneath.
            Assert.Equal(Screen.MultiAccountPrint, vm.CurrentScreen);
            Assert.NotNull(vm.MultiAccountPrint);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE CHANGE VIEW MENU'S THREE ROWS, EACH BY ITS OWN PAINTED LETTER.</b> Section F reaches these rows
    /// with Enter and arrow keys only. <i>Show Original View</i> is the interesting one: <b>S</b> is taken by
    /// <i>Saved Views</i> above it, so the assigner walks into the label and paints <b>H</b> — a letter that is
    /// not the row's initial, which is precisely the case a hard-coded expectation gets wrong and a reader
    /// guesses wrong. Each row is driven from a FRESH menu, because taking a row pops the column.
    /// </summary>
    [AvaloniaFact]
    public void Change_view_rows_each_run_from_their_own_painted_letter()
    {
        var (window, vm) = OpenWindow("Change View Letters Co");
        try
        {
            OpenAReport(window, vm);
            var detailedDefault = vm.Reports!.Detailed;

            // Row 1 — Saved Views. Opens the saved-views panel (empty here, which is the panel's own state).
            var savedViews = PaintedLetterOf(window, vm, ChangeViewMenu.SavedViewsVerb);
            window.KeyPressQwerty(PhysicalKeyFor(savedViews), RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
            Assert.NotNull(vm.SavedViews);
            Assert.DoesNotContain("Ctrl+S", vm.SavedViews!.Status);   // the invented chord is gone from the copy
            Assert.Contains("Ctrl+L", vm.SavedViews!.Status);         // the vendor's Save View chord is named
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            // Row 3 — Show Original View, whose painted letter is NOT its initial.
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);   // move the report off its default
            Pump(window);
            Assert.NotEqual(detailedDefault, vm.Reports!.Detailed);

            var showOriginal = PaintedLetterOf(window, vm, ChangeViewMenu.ShowOriginalViewVerb);
            Assert.NotEqual('S', showOriginal);   // Saved Views took S; the assigner had to walk the label
            Assert.Equal('H', showOriginal);
            window.KeyPressQwerty(PhysicalKeyFor(showOriginal), RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.TrialBalance, vm.Reports!.Kind);
            Assert.Equal(detailedDefault, vm.Reports!.Detailed);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>ALL FOUR DISCLOSURES ARE MEASURED ON SCREEN, BECAUSE A DISCLOSURE THAT IS CUT IN HALF IS WORSE THAN
    /// NO DISCLOSURE — IT READS AS A DIFFERENT, SHORTER, FALSE SENTENCE.</b> This is a filed defect of this
    /// shell, not a hypothetical: the COMPANY menu's disclosure needed 888px, was arranged into 350, and with
    /// <c>NoWrap</c>/<c>TextTrimming=None</c> the screen read a hard cut at 39% with nothing signalling the loss.
    /// <c>ShellNavigationRowsTests.The_company_menus_disclosure_is_fully_readable_and_not_silently_cut</c> locks
    /// that one; these four were never measured, and one of them was LENGTHENED in this slice (the print menu's,
    /// to admit the vendor's <i>All Tiles</i> row as well as <i>Configuration</i>) — so the budget is now proven
    /// rather than asserted in a code comment.
    ///
    /// <para>A string assertion cannot see this. <c>TextBlock.Text</c> is what the view model set, not what the
    /// operator read, so every "the disclosure is on screen" test in this file passes on the cut render. The
    /// probe re-measures the realised block in its own font, wrapped to the width it was actually given.</para>
    ///
    /// <para>🔴 <b>THE LINE BUDGET IS THE ASSERTION THAT ACTUALLY BITES, AND IT IS HERE BECAUSE THE OBVIOUS
    /// ASSERTION IS A DEAD GUARD. THIS WAS MEASURED, NOT REASONED.</b> The first draft of this test copied the
    /// shipped width probe from
    /// <c>ShellNavigationRowsTests.The_company_menus_disclosure_is_fully_readable_and_not_silently_cut</c> —
    /// <c>probe.DesiredSize.Width &lt;= block.Bounds.Width</c>. It was then mutated: the print disclosure was
    /// replaced with a <b>152-character</b> sentence, roughly five times its budget, and <b>all 29 tests stayed
    /// green</b>. The reason is structural and applies to that shipped test too. Once the shared cascade header
    /// template carries <c>TextWrapping="Wrap"</c>, a wrapped block's desired WIDTH can never exceed the
    /// constraint it was measured against — that is what wrapping means — so the width comparison is a
    /// tautology, and the height comparison only re-states that the Border grew to fit. Both survive as
    /// regression guards on the TEMPLATE (they fail the day someone puts <c>NoWrap</c> back, which is the cut
    /// they were written for), but neither can see an over-long STRING, which is the other half of the original
    /// two-sided fix. <b>The line count is what sees it</b>: the header is budgeted at two lines in its own
    /// source remarks, so that budget is measured here rather than asserted in prose. The mutation above takes
    /// this test red at six lines.</para>
    /// </summary>
    [AvaloniaFact]
    public void Every_action_menu_disclosure_is_fully_readable_and_not_silently_cut()
    {
        var (window, vm) = OpenWindow("Disclosure Fit Co");
        try
        {
            OpenAReport(window, vm);

            foreach (var (key, mods, screen, disclosure) in new[]
                     {
                         (PhysicalKey.H, RawInputModifiers.Control, Screen.ChangeViewMenu, ChangeViewMenu.Disclosure),
                         (PhysicalKey.P, RawInputModifiers.Alt, Screen.PrintMenu, ReportPrintMenu.Disclosure),
                         (PhysicalKey.E, RawInputModifiers.Alt, Screen.ExportMenu, ReportExportMenu.Disclosure),
                         (PhysicalKey.M, RawInputModifiers.Alt, Screen.ShareMenu, ReportShareMenu.Disclosure),
                     })
            {
                window.KeyPressQwerty(key, mods);
                Pump(window);
                Assert.Equal(screen, vm.CurrentScreen);

                var block = Descendants(window)
                    .OfType<TextBlock>()
                    .Single(t => t.IsEffectivelyVisible
                                 && (t.Text ?? string.Empty)
                                     .Contains(disclosure, StringComparison.OrdinalIgnoreCase));

                Assert.True(block.Bounds.Width > 0 && block.Bounds.Height > 0,
                    $"{screen}: the disclosure is not laid out at all.");
                Assert.True(
                    block.TextWrapping != TextWrapping.NoWrap || block.TextTrimming != TextTrimming.None,
                    $"{screen}: the header can neither wrap nor trim — any overflow is a silent mid-word cut.");

                var probe = new TextBlock
                {
                    Text = block.Text,
                    FontSize = block.FontSize,
                    FontFamily = block.FontFamily,
                    FontWeight = block.FontWeight,
                    FontStyle = block.FontStyle,
                    LetterSpacing = block.LetterSpacing,
                    TextWrapping = block.TextWrapping,
                };
                probe.Measure(new Size(block.Bounds.Width, double.PositiveInfinity));

                Assert.True(probe.DesiredSize.Width <= block.Bounds.Width + 0.5,
                    $"{screen}: the disclosure needs {probe.DesiredSize.Width:F0}px but was arranged into " +
                    $"{block.Bounds.Width:F0}px, so part of it is not on screen.");
                Assert.True(block.Bounds.Height + 0.5 >= probe.DesiredSize.Height,
                    $"{screen}: the disclosure wraps to {probe.DesiredSize.Height:F0}px but was arranged into " +
                    $"{block.Bounds.Height:F0}px, so a wrapped line is clipped.");

                // 🔴 The line budget — the only one of the four assertions that an over-long STRING can fail.
                // One line's height in this very block, measured by re-probing the same text unwrapped.
                var oneLine = new TextBlock
                {
                    Text = block.Text,
                    FontSize = block.FontSize,
                    FontFamily = block.FontFamily,
                    FontWeight = block.FontWeight,
                    FontStyle = block.FontStyle,
                    LetterSpacing = block.LetterSpacing,
                    TextWrapping = TextWrapping.NoWrap,
                };
                oneLine.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(oneLine.DesiredSize.Height > 0, $"{screen}: cannot measure a line height.");

                var lines = (int)Math.Round(probe.DesiredSize.Height / oneLine.DesiredSize.Height,
                                            MidpointRounding.AwayFromZero);
                Assert.True(lines <= 2,
                    $"{screen}: the disclosure \"{block.Text}\" wraps to {lines} lines in " +
                    $"{block.Bounds.Width:F0}px. The cascade header is budgeted at two; a taller one pushes the " +
                    "menu's own rows down and reads as a paragraph rather than a caveat. Shorten the string.");

                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(Screen.Report, vm.CurrentScreen);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>Opens the Change View menu and returns the letter the product paints on <paramref name="label"/>,
    /// leaving the menu up and ready for that keystroke.</summary>
    private static char PaintedLetterOf(MainWindow window, MainWindowViewModel vm, string label)
    {
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal(Screen.ChangeViewMenu, vm.CurrentScreen);

        var row = vm.Columns[^1].Items.Single(i => i.IsSelectable && i.Label == label);
        Assert.True(row.HasHotKey, $"'{label}' has no painted letter — it is unreachable by letter.");
        return char.ToUpperInvariant(row.HotKey!.Value);
    }

    /// <summary>Posts a two-line journal (expense Dr / Cash Cr) and returns its id.</summary>
    private static Guid PostAJournal(MainWindowViewModel vm, string expenseName, decimal amount)
    {
        var company = vm.Company!;
        var expense = new DomainLedger(Guid.NewGuid(), expenseName,
            company.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        company.AddLedger(expense);
        var cash = company.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;
        entry.Lines[0].SelectedLedger = expense;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        entry.Lines[1].SelectedLedger = cash;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(entry.Accept());
        return company.Vouchers.Last().Id;
    }

    // ================================================================ G — THE SAVED VIEWS PANEL AS A PAGE
    //
    // 🔴 THIS SECTION EXISTS BECAUSE THE SLICE ABOVE SHIPPED FOUR MENUS THAT WERE ALL DEAD ON ONE SCREEN, AND
    // EVERY TEST ABOVE PASSED WHILE THEY WERE. The four action menus are built on "pop my own column, THEN act on
    // the page beneath" (PopMenuColumn). That pop runs BackFromPage → ClearSubScreens, which nulls every page
    // property, and RehydratePageFromRightmostColumn then re-bound only the RIGHTMOST surviving column. Over the
    // Saved Views panel the page the menus act on is TWO deep — the panel sits on the report it was opened from —
    // so the report came back null and every row acted on nothing. And the panel itself had no arm in
    // BindPageColumn at all, so the shell fell to Screen.Gateway with a page column still drawn.
    //
    // The tests below drive the REALISED WINDOW with real keystrokes, because the state is reached by a gesture
    // and every view-model flag involved was individually correct at each step.

    /// <summary>Saves one view off a fresh report the way an operator does: Ctrl+L, name it, Ctrl+A.</summary>
    private void SaveOneView(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.OpenReport(ReportKind.TrialBalance);
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal(Screen.SaveView, vm.CurrentScreen);
        vm.SaveView!.Name = name;
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
    }

    /// <summary>Report → Ctrl+H → the painted letter of the given Change View row, entirely from the keyboard.</summary>
    private static void OpenSavedViewsPanelByKeyboard(MainWindow window, MainWindowViewModel vm, string verb)
    {
        var letter = PaintedLetterOf(window, vm, verb);
        window.KeyPressQwerty(PhysicalKeyFor(letter), RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
        Assert.NotNull(vm.SavedViews);
    }

    /// <summary>The REALISED button-bar Button whose item carries <paramref name="key"/> as its badge.</summary>
    private static Button BarButton(MainWindow window, string key) =>
        Descendants(window)
            .OfType<Button>()
            .Single(b => b.DataContext is ButtonBarItem item && item.Key == key);

    /// <summary>
    /// 🔴 <b>Alt+P &gt; Current, INVOKED FROM THE SAVED VIEWS PANEL, MUST ACTUALLY PRINT — AND IT DID NOT.</b>
    ///
    /// <para><b>Measured before the fix</b>, on this exact sequence: the Print menu drew, <i>Current</i> was
    /// chosen, and nothing appeared. <c>CurrentScreen</c> was <c>Gateway</c>, <c>PrintPreview</c> was null,
    /// <c>Reports</c> was null and <c>SavedViews</c> was null — while the Saved Views column was still the
    /// rightmost pane drawn on screen. That second half is worse than a dead row: the Gateway's own bare letters
    /// (Y = Export Data, O = Import) became live over a report cascade, which is precisely the
    /// blank-shell-that-owns-the-keyboard state <c>HasLiveCompanyShell</c> was written to prevent.</para>
    ///
    /// <para><b>Both halves are asserted, and the second is the one that matters most</b>: the preview really
    /// opens, AND the shell is never left at the Gateway with a page column drawn.</para>
    /// </summary>
    [AvaloniaFact]
    public void Print_menu_current_row_prints_when_it_is_invoked_from_the_Saved_Views_panel()
    {
        var (window, vm) = OpenWindow("Saved Views Print Co");
        try
        {
            SaveOneView(window, vm, "Printable View");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.SavedViewsVerb);

            // The report is deliberately still bound BENEATH the panel — that is what makes Alt+P offer itself.
            Assert.NotNull(vm.Reports);
            Assert.True(vm.IsPrintablePage);

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
            Assert.Equal(ReportPrintMenu.CurrentVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            // 🔴 THE ASSERTION THAT SEES IT: a preview really exists. A "the menu opened" assertion passed
            // throughout the defect.
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);

            // 🔴 AND THE SHELL IS COHERENT: not Gateway, and the screen matches the rightmost column that is drawn.
            Assert.NotEqual(Screen.Gateway, vm.CurrentScreen);
            Assert.Equal(vm.Columns.Count - 1, vm.ActiveColumnIndex);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE SAME ROOT CAUSE SEEN THROUGH A SECOND MENU, AND THIS ONE PROVES THE PAGE BENEATH IS RE-BOUND
    /// RATHER THAN MERELY NON-NULL.</b> Alt+E &gt; Current from the Saved Views panel must export the REPORT the
    /// panel was opened over — so the assertion is the exported panel's <c>DocumentTitle</c>, not that a column
    /// appeared. Before the fix <c>Reports</c> was null at the moment <c>OpenExport</c> ran and no panel was
    /// built at all.
    /// </summary>
    [AvaloniaFact]
    public void Export_menu_current_row_exports_the_report_beneath_the_Saved_Views_panel()
    {
        var (window, vm) = OpenWindow("Saved Views Export Co");
        try
        {
            SaveOneView(window, vm, "Exportable View");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.SavedViewsVerb);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);
            Assert.Contains("Trial Balance", vm.ExportPanel!.DocumentTitle, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Ctrl+H OVER AN ALREADY-OPEN SAVED VIEWS PANEL STACKED A SECOND, IDENTICAL COLUMN.</b>
    ///
    /// <para><b>Measured before the fix</b>: two Saved Views columns side by side, the first a dead ghost whose
    /// Enter did nothing, and <c>Reports</c> null beneath both — so every report chord on that cascade went inert
    /// as well. The re-entrancy guard in <c>OpenSavedViews</c> ("panel already open — don't stack a second")
    /// could not help, because <c>PopMenuColumn</c> had nulled <c>SavedViews</c> one statement earlier and the
    /// rehydrate had no arm to bring it back. The guard was not wrong; it was BLIND.</para>
    ///
    /// <para>The second half of the assertion is the one a "column count" test would miss: the report must still
    /// be bound beneath, or the panel is a cul-de-sac.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_H_over_an_open_Saved_Views_panel_does_not_stack_a_second_one()
    {
        var (window, vm) = OpenWindow("Saved Views No Stack Co");
        try
        {
            SaveOneView(window, vm, "Only View");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.SavedViewsVerb);

            var depth = vm.Columns.Count;
            var panel = vm.SavedViews!;

            // Ctrl+H again — it is offered here, because IsReportContext is TRUE on the panel — then the same row.
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.SavedViewsVerb);

            Assert.Equal(depth, vm.Columns.Count);
            Assert.Equal(1, vm.Columns.Count(c => c.Page is SavedViewsViewModel));
            Assert.Same(panel, vm.SavedViews);                 // the SAME panel, not a replacement
            Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                        // the report beneath is still live
            Assert.Equal(vm.Columns.Count - 1, vm.ActiveColumnIndex);

            // And the panel that is on screen still answers Enter: it applies the view, it is not a ghost.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The OTHER Ctrl+H row over an already-open panel must not be swallowed either: arriving by <b>Delete Saved
    /// Views</b> while the list is up arms the delete on the panel that is there, rather than no-opping because a
    /// guard saw a non-null panel. A documented vendor row that does nothing in a reachable state is the dead-row
    /// defect this slice exists to remove.
    /// </summary>
    [AvaloniaFact]
    public void Delete_saved_views_row_arms_the_panel_that_is_already_open()
    {
        var (window, vm) = OpenWindow("Saved Views Rearm Co");
        try
        {
            SaveOneView(window, vm, "Armable View");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.SavedViewsVerb);
            Assert.False(vm.SavedViews!.IsDeleteMode);

            var depth = vm.Columns.Count;
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.DeleteSavedViewsVerb);

            Assert.Equal(depth, vm.Columns.Count);
            Assert.True(vm.SavedViews!.IsDeleteMode);
        }
        finally { window.Close(); }
    }

    // ================================================================ H — THE VENDOR'S Y CONFIRMATION
    //
    // 🔴 The panel's own remark quoted help.tallysolutions.com/use-save-view-feature-in-tallyprime/ verbatim —
    // "Press Enter or Y to confirm deletion" — and only Enter answered. A red-flagged vendor quote with half its
    // behaviour behind it is worse than no quote, because the next reader takes it as measured.

    /// <summary>
    /// 🔴 <b>Y CONFIRMS AN ARMED DELETE.</b> Arm with Enter, press <b>Y</b> on the realised window, and the view
    /// is gone — the vendor's second confirmation key. <b>Measured before the fix: the view survived.</b>
    /// </summary>
    [AvaloniaFact]
    public void Y_confirms_an_armed_saved_view_delete_as_the_vendor_documents()
    {
        var (window, vm) = OpenWindow("Saved Views Y Confirm Co");
        try
        {
            SaveOneView(window, vm, "Doomed By Y");
            SaveOneView(window, vm, "Survivor");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.DeleteSavedViewsVerb);

            var panel = vm.SavedViews!;
            Assert.True(panel.IsDeleteMode);
            Assert.Equal(2, panel.Views.Count);
            panel.Selected = panel.Views.Single(v => v.Name == "Doomed By Y");
            Pump(window);

            // PRESS ONE — Enter arms and deletes nothing, and the status line now offers BOTH keys.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, panel.Views.Count);
            Assert.True(vm.IsSavedViewDeleteArmed);
            Assert.Contains("Y", panel.Status, StringComparison.Ordinal);

            // PRESS TWO — the vendor's Y.
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.Single(panel.Views);
            Assert.Equal("Survivor", panel.Views[0].Name);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Y NEVER ARMS, AND THAT IS THE SAFETY HALF OF THE SAME FIX.</b> The vendor's arming press is Enter
    /// alone ("choose the view and press Enter"); Y appears only in the confirmation sentence. So a bare Y on a
    /// delete-mode panel with nothing armed must change NOTHING — not delete, and not put a confirmation on
    /// screen the operator never asked for. Moving the highlight must disarm it for Y exactly as it does for
    /// Enter, so this also drives the stale-confirmation mis-target.
    /// </summary>
    [AvaloniaFact]
    public void Y_alone_neither_arms_nor_deletes_a_saved_view()
    {
        var (window, vm) = OpenWindow("Saved Views Y Safety Co");
        try
        {
            SaveOneView(window, vm, "Alpha Kept");
            SaveOneView(window, vm, "Beta Kept");
            OpenAReport(window, vm);
            OpenSavedViewsPanelByKeyboard(window, vm, ChangeViewMenu.DeleteSavedViewsVerb);

            var panel = vm.SavedViews!;
            panel.Selected = panel.Views[0];
            Pump(window);

            // Y with nothing armed: no delete, and nothing armed by it either.
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, panel.Views.Count);
            Assert.Null(panel.PendingDeleteName);
            Assert.False(vm.IsSavedViewDeleteArmed);

            // Arm row 0 with Enter, then ARROW AWAY: the arming is thrown away, so Y on the new row is inert.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.IsSavedViewDeleteArmed);

            panel.Selected = panel.Views[1];
            Pump(window);
            Assert.False(vm.IsSavedViewDeleteArmed);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(2, panel.Views.Count);
        }
        finally { window.Close(); }
    }

    // ================================================================ I — THE TWO SHARE BADGES
    //
    // 🔴 The branch corrected OpenShareMenu off IsPrintablePage and onto IsShareablePage, and left the button
    // bar's own M and W badges on the wrong predicate three lines away. Printability is TRUE on every master list
    // (its third arm is TopMasterExportSource()); neither share channel can build a document from one.

    /// <summary>
    /// 🔴 <b>ON A MASTER LIST BOTH SHARE BADGES RENDERED ENABLED AND FIRED NOTHING.</b> Asserted on the REALISED
    /// Button's own enabled state, and then by clicking it — because the defect is an affordance that lies, and a
    /// view-model flag assertion is exactly the one that passed while it did.
    ///
    /// <para>The report half is the control: narrowing the gate must not dim a badge where the channel really
    /// works, which is what distinguishes this fix from simply switching the badges off.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_share_badges_are_dead_on_a_master_list_and_live_on_a_report()
    {
        var (window, vm) = OpenWindow("Share Badge Co");
        try
        {
            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
            Assert.True(vm.IsPrintablePage);        // the wrong predicate IS true here — that was the whole bug
            Assert.False(vm.IsShareablePage);       // and the right one is not

            foreach (var badge in new[] { "M", "W" })
            {
                var button = BarButton(window, badge);
                Assert.False(button.IsEffectivelyEnabled,
                    $"the '{badge}' share badge renders enabled on a master list and fires nothing");
            }

            // Belt and braces: the verbs behind them really cannot act here, so the dimming is honest.
            vm.OpenEmailCompose();
            vm.OpenWhatsAppShare();
            Pump(window);
            Assert.Null(vm.EmailCompose);
            Assert.Null(vm.WhatsAppShare);
            Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);

            // THE CONTROL — on a live report both channels work, so both badges must be live.
            OpenAReport(window, vm);
            foreach (var badge in new[] { "M", "W" })
                Assert.True(BarButton(window, badge).IsEffectivelyEnabled,
                    $"the '{badge}' share badge is dimmed on a report, where the channel does work");

            BarButton(window, "W").Command!.Execute(null);
            Pump(window);
            Assert.NotNull(vm.WhatsAppShare);
        }
        finally { window.Close(); }
    }

    // ================================================================ J — Alt+K OVER AN ACTION MENU
    //
    // 🔴 HasLiveCompanyShell excludes only the company-select screens, so it is TRUE on all four action-menu
    // screen ids. Alt+K therefore ran OpenCompanyMenu with an action menu up: ClearSubScreens unbound the report
    // the menu was standing on, and a NAVIGATION column landed on top of the ACTION menu, which stayed drawn
    // beneath it with every row now pointing at a null page.

    /// <summary>
    /// A shell navigation chord must be inert while one of the four modal action menus is up — Escape pops the
    /// menu first, exactly as for the other three menu chords. Both table entries that can fire there are driven:
    /// <b>Alt+K</b> (Company) and <b>Ctrl+G</b> (Switch To). Then the menu is escaped and Alt+K must still work,
    /// so this is a narrowing and not a deletion.
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_and_Ctrl_G_are_inert_while_an_action_menu_is_up()
    {
        var (window, vm) = OpenWindow("Action Menu Shell Chord Co");
        try
        {
            OpenAReport(window, vm);
            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
            var depth = vm.Columns.Count;

            // 🔴 ASSERTED SEPARATELY, ONE CHORD AT A TIME, SO EACH CLAUSE IS INDEPENDENTLY COVERED. Driving both
            // keys and then asserting once let whichever chord fired FIRST mask the other — measured: with only
            // the Ctrl+G clause removed the test went red on SwitchTo and said nothing about Alt+K.
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
            Assert.Equal(depth, vm.Columns.Count);
            Assert.NotNull(vm.Reports);        // the report the menu acts on is still bound

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.PrintMenu, vm.CurrentScreen);
            Assert.Equal(depth, vm.Columns.Count);
            Assert.Null(vm.SwitchTo);
            Assert.NotNull(vm.Reports);

            // Escape pops the menu, and the vendor's chord works again on the report beneath.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.CompanyMenu, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }
}
