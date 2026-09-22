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
}
