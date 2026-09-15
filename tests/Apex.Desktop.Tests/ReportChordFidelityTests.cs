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
}
