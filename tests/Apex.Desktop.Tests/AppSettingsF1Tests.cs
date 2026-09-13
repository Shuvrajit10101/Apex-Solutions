using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
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
/// 🔴 <b>CENSUS ROW 1.8 — F1 (Help) &gt; SETTINGS, THE APPLICATION-WIDE CONFIGURATION SURFACE.</b>
///
/// <para><b>What the row was, and why it was retitled.</b> It read "F12 Configure — the global configuration
/// tree" and graded <c>ABSENT</c>. Two build tracks in a row carried it and DECLINED to build it, because the
/// reference product does not ship that surface — it removed it. Vendor verbatim, re-fetched first-hand for
/// this slice from
/// <c>help.tallysolutions.com/developer-reference/release-notes-whats-new-in-tdl/working-of-tally-erp-9-customisations-with-tallyprime/</c>:
/// <i>"Button F12 has been removed from Menu context."</i>; <i>"The menu 'Configuration' has been removed. The
/// related configurations have been added under the respective features."</i>; <i>"With the enhanced Popup Menu
/// capability, the items in the General Configuration have now been placed in the Popup Menu, F1: Help invoked
/// from the top buttons (F1: Help &gt; Settings)."</i> User ruling 21 retitled the row to the surface the vendor
/// actually ships. <b>These tests are written against F1, and a test that passed by building an F12 tree would
/// be testing an invented feature.</b></para>
///
/// <para><b>The vendor's Settings groups, from the vendor's own pages.</b> <i>Display</i> (including <i>"You can
/// set Show bottom bar to No if you need to disable the bottom bar to increase viewing space on your
/// screen."</i>), <i>Country &gt; Date and Number Format</i> (<i>"Show Quantity and Number in millions"</i> —
/// <i>"once the option is set to yes, you can see the amount as 1,000,000 instead of 10,00,000"</i>, applying
/// <i>"in the book as well as on cheques"</i>), plus <i>Startup</i>, <i>Language</i>, <i>Connectivity</i> and
/// <i>Licence</i>. Only the first two ship: this build has no startup company-loading step, no second language,
/// no client/server mode and no licence, so the rest could only be knobs with nothing behind them.</para>
///
/// <para>🔴 <b>WHY THESE TESTS ASK THE REALISED VISUAL TREE.</b> Asserting <c>vm.ShowBottomBar == false</c> is
/// precisely the test that passes on a build where the flag is bound to nothing. Each case below flips the
/// setting and then asks what an operator would ask: <i>is the control on screen?</i> and <i>did the thing it
/// claims to gate actually leave the screen / change its digits?</i></para>
///
/// <para>🔴 <b>WHY THE MILLIONS CASES LIVE IN THIS ASSEMBLY.</b> <c>AmountDisplay.Grouping</c> is process-wide,
/// and <c>Apex.Desktop.Tests</c> is the one test assembly that disables class parallelisation
/// (<c>AssemblyInfo.cs</c>). Written in <c>Apex.Ledger.Tests</c> they would re-group amounts under whatever
/// class happened to be running alongside them. Every case restores the default in a <c>finally</c>.</para>
///
/// <para>Headless-safe: visual-tree and layout-bounds inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class AppSettingsF1Tests
{
    /// <summary>The two vendor captions this build actually carries, verbatim.</summary>
    public static IEnumerable<object[]> ShippedVendorCaptions() => new[]
    {
        new object[] { "Show bottom bar" },
        new object[] { "Show Quantity and Number in millions" },
    };

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>
    /// Every REALISED, EFFECTIVELY-VISIBLE, non-degenerate <see cref="CheckBox"/> whose content reads exactly
    /// <paramref name="caption"/>. Effective visibility plus non-zero bounds is the difference between a control
    /// an operator can reach and one that merely exists in the XAML.
    /// </summary>
    private static List<CheckBox> VisibleCheckBoxes(MainWindow window, string caption) =>
        Descendants(window)
            .OfType<CheckBox>()
            .Where(cb => cb.IsEffectivelyVisible
                         && cb.Bounds.Width > 0 && cb.Bounds.Height > 0
                         && string.Equals(cb.Content as string, caption, StringComparison.Ordinal))
            .ToList();

    /// <summary>The window's status strip, by name, whether or not it is currently on screen.</summary>
    private static Border StatusBar(MainWindow window) =>
        Descendants(window).OfType<Border>().Single(b => b.Name == "StatusBar");

    private static bool StatusBarIsOnScreen(MainWindow window)
    {
        var bar = StatusBar(window);
        return bar.IsEffectivelyVisible && bar.Bounds.Height > 0;
    }

    private static List<TextBlock> VisibleTextBlocks(MainWindow window, string name) =>
        Descendants(window)
            .OfType<TextBlock>()
            .Where(t => t.Name == name && t.IsEffectivelyVisible && t.Bounds.Height > 0)
            .ToList();

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexF1Settings_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "F1 Settings Co";
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        AmountDisplay.ResetToDefault();   // belt and braces: never leak the grouping out of a case
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ==========================================================================================
    // 1. THE DOOR. The row is only shipped if a user can REACH it from the keyboard.
    // ==========================================================================================

    /// <summary>
    /// 🔴 <b>REACHABILITY, FROM THE KEYBOARD, WITH NO VIEW-MODEL CALL.</b> A bare F1 press from the Gateway must
    /// land on the Settings page. <b>This is the clause that fails on today's main</b>, where F1 sets a stub
    /// string ("Apex Solutions — accounting (Phase 1).") and opens nothing at all.
    /// </summary>
    [AvaloniaFact]
    public void F1_from_the_gateway_opens_the_application_settings_page()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.AppSettings != null,
                "F1 did not open the application Settings page. The vendor puts the former General "
              + "Configuration items behind exactly this key (F1: Help > Settings), so without it the whole "
              + "surface is unreachable.");
            Assert.Equal(Screen.AppSettings, vm.CurrentScreen);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The page is a CASCADE COLUMN nested under the existing hierarchy — it does not replace the Gateway — and
    /// Escape pops it, leaving the shell exactly where it was. A panel that could not be dismissed, or that tore
    /// down what was beneath it, would not match any other column in this shell.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_page_is_a_cascade_column_and_escape_pops_it()
    {
        var (window, vm, dir) = Open();
        try
        {
            var before = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(before + 1, vm.Columns.Count);
            Assert.NotNull(vm.Columns[^1].AppSettings);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Null(vm.AppSettings);
            Assert.Equal(before, vm.Columns.Count);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>F1 works from a REPORT page too, and popping it leaves the report LIVE beneath.</b> Two hazards in
    /// one case. First, the window's key tunnel claims bare F2 and bare F12 for the report before the button bar
    /// ever sees them — F1 must not be swallowed the same way, or the settings surface would be unreachable from
    /// the screen an operator spends most time on. Second, a settings column that tore down the report under it
    /// would orphan the page: Escape has to hand the report back, exactly as it does under the F12 config column.
    /// </summary>
    [AvaloniaFact]
    public void F1_opens_over_a_live_report_and_escape_hands_the_report_back()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.NotNull(vm.Reports);

            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.AppSettings != null,
                "F1 on a report page opened nothing — the report shortcuts have swallowed the key.");

            // 🔴 MEASURED: this is the clause that bites, and the post-Escape one below does NOT. Nulling the
            // report on the way in (a ClearSubScreens the opener must not do) is INVISIBLE after Escape, because
            // BackFromPage rehydrates the surviving column and hands the report back regardless. The damage is
            // done WHILE the settings column is up: the report pane beneath it is unbound and renders empty.
            Assert.True(vm.Reports != null,
                "Opening the settings column unbound the report beneath it, so the report pane goes blank while "
              + "the settings page is up. Like the F12 config column, this page must not trim what it opens over.");

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Null(vm.AppSettings);
            Assert.True(vm.Reports != null,
                "Popping the settings column left the report beneath it orphaned.");
            Assert.Equal(Screen.Report, vm.CurrentScreen);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Re-pressing F1 while the page is open must not stack a second settings column. Every other cascade panel
    /// in this shell carries the same guard.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_F1_twice_does_not_stack_a_second_settings_column()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var once = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(once, vm.Columns.Count);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 Each vendor caption this build carries must be a SINGLE control the operator can actually see on the
    /// page. Fails on today's main, where neither caption exists anywhere in the tree.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ShippedVendorCaptions))]
    public void Each_shipped_vendor_caption_is_a_visible_control_on_the_settings_page(string caption)
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);

            var found = VisibleCheckBoxes(window, caption);
            Assert.True(found.Count == 1,
                $"The vendor's F1 > Settings option \"{caption}\" is not a single visible control on the page "
              + $"(found {found.Count}). A capability no user can reach is not complete.");
        }
        finally { Cleanup(window, dir); }
    }

    // ==========================================================================================
    // 2. THE KNOBS ARE NOT DEAD. Each changes something the operator can see.
    // ==========================================================================================

    /// <summary>
    /// 🔴 <b>"Show bottom bar" gates the window's status strip.</b> Both halves are asserted against the same
    /// window: a gate that hid the strip unconditionally would satisfy the OFF clause on its own and prove
    /// nothing. On today's main the strip has no <c>IsVisible</c> binding at all, so the OFF half fails there.
    /// </summary>
    [AvaloniaFact]
    public void Show_bottom_bar_off_takes_the_status_bar_off_the_screen()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var page = vm.AppSettings!;

            Assert.True(page.ShowBottomBar, "Fixture guard: the page must open seeded from the live state (shown).");
            Assert.True(StatusBarIsOnScreen(window),
                "Fixture guard: the status bar must be on screen before the switch is cleared, or the OFF "
              + "clause below proves nothing.");

            page.ShowBottomBar = false;
            page.Apply();
            Pump(window);

            Assert.False(StatusBarIsOnScreen(window),
                "'Show bottom bar' = No left the status bar on screen — the switch is a dead knob.");

            page.ShowBottomBar = true;
            page.Apply();
            Pump(window);
            Assert.True(StatusBarIsOnScreen(window),
                "'Show bottom bar' = Yes did not bring the status bar back; the gate is one-way.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE GATE IS SCOPED, AND THAT IS DELIBERATE.</b> Hiding the bottom bar must NOT silence the window's
    /// notice line — those are the Phase 10.11 lifecycle refusals, the one channel on which a destructive verb
    /// explains itself, and the vendor's sentence is about reclaiming viewing space, not about switching off
    /// messages. Without this case, "hide everything in that grid row" would pass the test above.
    /// </summary>
    [AvaloniaFact]
    public void Hiding_the_bottom_bar_does_not_silence_the_notice_line()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var page = vm.AppSettings!;
            page.ShowBottomBar = false;
            page.Apply();

            vm.Notice = "A refusal the operator has to be able to read.";
            Pump(window);

            Assert.False(StatusBarIsOnScreen(window), "Fixture guard: the bottom bar must be hidden here.");

            var noticeVisible = Descendants(window)
                .OfType<TextBlock>()
                .Any(t => t.IsEffectivelyVisible && t.Bounds.Height > 0
                          && string.Equals(t.Text, "A refusal the operator has to be able to read.",
                                           StringComparison.Ordinal));
            Assert.True(noticeVisible,
                "Hiding the bottom bar also hid the window's notice line. That is a different — and far worse — "
              + "feature wearing this one's caption: it would let an operator switch off the refusals that "
              + "explain why a destructive verb declined.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// A company whose Trial Balance is exactly ₹1,05,000 on each side. 105000 is above the grouping boundary,
    /// so it renders differently under the two rules and the expected strings are hand-derived, not read off the
    /// code: Indian 3;2 → <c>1,05,000.00</c>; Millions (flat 3) → <c>105,000.00</c>.
    /// </summary>
    private static Company OneLakhFiveCompany()
    {
        var company = Apex.Ledger.Services.CompanyFactory.CreateSeeded(
            "Grouping " + Guid.NewGuid().ToString("N"),
            new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));

        Guid GroupId(string name) => company.FindGroupByName(name)!.Id;

        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Petty Cash", GroupId("Cash-in-Hand"),
            Money.FromRupees(105000m), openingIsDebit: true));
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Owner Capital", GroupId("Capital Account"),
            Money.FromRupees(105000m), openingIsDebit: false));

        return company;
    }

    /// <summary>
    /// 🔴 <b>"Show Quantity and Number in millions" re-groups the FIGURES IN A REPORT.</b> This is the clause
    /// that makes the switch a feature rather than a stored boolean: the same books, projected either side of
    /// the setting, must print different digits. The vendor's own worked example is 1,000,000 versus 10,00,000;
    /// this fixture uses 1,05,000 / 105,000, which straddles the same boundary.
    /// </summary>
    [Fact]
    public void The_millions_setting_regroups_the_figures_in_a_trial_balance()
    {
        try
        {
            var beforeVm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
            var beforeRow = beforeVm.Rows.Single(r => r.Particulars.StartsWith("Petty Cash", StringComparison.Ordinal));
            Assert.Equal("1,05,000.00", beforeRow.Debit);

            using (AmountDisplay.Scoped(AmountDigitGrouping.Millions))
            {
                var afterVm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
                var afterRow = afterVm.Rows.Single(r => r.Particulars.StartsWith("Petty Cash", StringComparison.Ordinal));
                Assert.Equal("105,000.00", afterRow.Debit);
            }

            // The scope restored the default, so a freshly built report is Indian-grouped again.
            var restoredVm = new ReportsViewModel(OneLakhFiveCompany(), ReportKind.TrialBalance);
            var restoredRow = restoredVm.Rows.Single(r => r.Particulars.StartsWith("Petty Cash", StringComparison.Ordinal));
            Assert.Equal("1,05,000.00", restoredRow.Debit);
        }
        finally { AmountDisplay.ResetToDefault(); }
    }

    /// <summary>
    /// 🔴 <b>EVERY ENTRY POINT OF THE GRID FORMATTER FOLLOWS THE SETTING, not just the one a report row happens
    /// to call.</b>
    ///
    /// <para><b>This case exists because a mutation SURVIVED without it.</b> The Trial-Balance case above goes
    /// through <c>IndianFormat.Amount</c>, which delegates straight to <c>IndianMoneyFormat.Amount</c> — so
    /// pointing <c>IndianFormat</c>'s own private culture back at the unconditional <c>Culture</c> changed
    /// nothing it could see, and the test stayed green. <b>Four other entry points DO use that private culture</b>
    /// — the always-render total (<c>AmountAlways</c>, the grand-total rows), the side-qualified ledger-book
    /// balance (<c>Signed</c>), stock <c>Quantity</c>, and the whole-rupee statutory columns (<c>Rupees</c>) —
    /// and each of them would have gone on grouping the Indian way while every other surface switched. That is
    /// the "same money printed two ways from the same assembly" defect this formatter exists to prevent,
    /// re-created by a half-wired setting.</para>
    /// </summary>
    [Fact]
    public void Every_entry_point_of_the_grid_formatter_follows_the_millions_setting()
    {
        try
        {
            // Defaults — the Indian rule, hand-derived from 1,05,000 / 1,05,000.5.
            Assert.Equal("1,05,000.00", IndianFormat.Amount(105000m));
            Assert.Equal("1,05,000.00", IndianFormat.AmountAlways(105000m));
            Assert.Equal("1,05,000.00 Dr", IndianFormat.Signed(105000m, DrCr.Debit));
            Assert.Equal("1,05,000.5", IndianFormat.Quantity(105000.5m));
            Assert.Equal("1,05,000", IndianFormat.Rupees(105000m));
            Assert.Equal("1,05,000", IndianFormat.RupeesAlways(105000m));

            using (AmountDisplay.Scoped(AmountDigitGrouping.Millions))
            {
                Assert.Equal("105,000.00", IndianFormat.Amount(105000m));
                Assert.Equal("105,000.00", IndianFormat.AmountAlways(105000m));
                Assert.Equal("105,000.00 Dr", IndianFormat.Signed(105000m, DrCr.Debit));
                Assert.Equal("105,000.5", IndianFormat.Quantity(105000.5m));
                Assert.Equal("105,000", IndianFormat.Rupees(105000m));
                Assert.Equal("105,000", IndianFormat.RupeesAlways(105000m));
            }

            Assert.Equal("1,05,000.00", IndianFormat.AmountAlways(105000m));
        }
        finally { AmountDisplay.ResetToDefault(); }
    }

    /// <summary>
    /// 🔴 <b>The switch on the PAGE is what moves the rule</b> — not a test reaching past it into
    /// <c>AmountDisplay</c>. Ticking the caption and pressing Apply must be enough, or the control is decoration
    /// over a setting only code can reach.
    /// </summary>
    [AvaloniaFact]
    public void Applying_the_millions_switch_on_the_page_moves_the_grouping_rule()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var page = vm.AppSettings!;

            Assert.False(page.ShowAmountsInMillions, "Fixture guard: the shipped default is lakhs, not millions.");
            Assert.Equal("1,05,000.00", IndianMoneyFormat.Amount(105000m));

            page.ShowAmountsInMillions = true;
            page.Apply();

            Assert.Equal(AmountDigitGrouping.Millions, AmountDisplay.Grouping);
            Assert.Equal("105,000.00", IndianMoneyFormat.Amount(105000m));

            page.ShowAmountsInMillions = false;
            page.Apply();
            Assert.Equal("1,05,000.00", IndianMoneyFormat.Amount(105000m));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The page's worked example is LIVE and is computed from the two frozen cultures, so the operator sees the
    /// effect before committing to it. A hard-coded example string would keep reading correctly after the rule
    /// behind it changed — which is how a preview stops previewing anything.
    /// </summary>
    [AvaloniaFact]
    public void The_number_example_on_the_page_follows_the_switch_before_it_is_applied()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var page = vm.AppSettings!;

            Assert.Equal("₹ 10,00,000.00", page.NumberPreview);
            Assert.Single(VisibleTextBlocks(window, "NumberPreviewText"));

            page.ShowAmountsInMillions = true;
            Pump(window);
            Assert.Equal("₹ 1,000,000.00", page.NumberPreview);
            Assert.Equal("₹ 1,000,000.00", VisibleTextBlocks(window, "NumberPreviewText").Single().Text);

            // …and merely PREVIEWING must not have moved the live rule. Apply is the commit point.
            Assert.Equal(AmountDigitGrouping.Indian, AmountDisplay.Grouping);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Opening the page and applying it with NO edits is a no-op. The page seeds from the live state, so a
    /// stray Ctrl+A must never silently re-group the books or move the bottom bar.
    /// </summary>
    [AvaloniaFact]
    public void Applying_with_no_edits_changes_nothing()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            // Guard: without this the case passes vacuously on a build where F1 opens nothing, because
            // ApplyAppSettings() is a no-op against a null page and the defaults below are simply untouched.
            Assert.NotNull(vm.AppSettings);
            vm.ApplyAppSettings();
            Pump(window);

            Assert.True(vm.ShowBottomBar);
            Assert.True(StatusBarIsOnScreen(window));
            Assert.Equal(AmountDigitGrouping.Indian, AmountDisplay.Grouping);
            Assert.Equal("1,05,000.00", IndianMoneyFormat.Amount(105000m));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The four vendor groups this build does NOT carry are named on screen with their reason, rather than being
    /// silently missing or — far worse — shipped as switches wired to nothing. "Clone, never invent" cuts both
    /// ways: do not invent the behaviour, and do not hide that the vendor has it.
    /// </summary>
    [AvaloniaFact]
    public void The_vendor_groups_this_build_lacks_are_shown_and_marked_unavailable()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);
            var groups = vm.AppSettings!.Groups;

            foreach (var caption in new[] { "Display", "Country", "Startup", "Language", "Connectivity", "Licence" })
                Assert.Contains(groups, g => g.Caption == caption);

            foreach (var caption in new[] { "Startup", "Language", "Connectivity", "Licence" })
                Assert.True(groups.Single(g => g.Caption == caption).IsUnavailable,
                    $"\"{caption}\" is offered as available, but this build has no behaviour behind it. "
                  + "A switch wired to nothing is worse than an honestly absent row.");

            foreach (var caption in new[] { "Display", "Country" })
                Assert.True(groups.Single(g => g.Caption == caption).IsAvailable);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The page states plainly that the settings do not survive a restart. This build has no store for them
    /// (the slice carried no schema budget), and a settings page that stayed silent about that would let the
    /// operator believe otherwise.
    /// </summary>
    [AvaloniaFact]
    public void The_page_says_the_settings_are_not_saved_between_sessions()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);

            Assert.Contains("not saved", vm.AppSettings!.Notice, StringComparison.OrdinalIgnoreCase);
            Assert.Single(VisibleTextBlocks(window, "AppSettingsNotice"));
        }
        finally { Cleanup(window, dir); }
    }

    // ==========================================================================================
    // 3. NO "TALLY" ANYWHERE THE OPERATOR CAN SEE IT.
    // ==========================================================================================

    /// <summary>
    /// The shipped app must never show the reference product's brand. This page quotes the vendor heavily in its
    /// code comments, which is exactly the file where such a string could leak into a caption.
    /// </summary>
    [AvaloniaFact]
    public void No_visible_string_on_the_settings_page_names_the_reference_product()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump(window);

            var offenders = Descendants(window)
                .OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Text is { } s
                            && s.Contains("Tally", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Text!)
                .ToList();
            Assert.True(offenders.Count == 0,
                "A visible string names the reference product: " + string.Join(" | ", offenders));
        }
        finally { Cleanup(window, dir); }
    }
}
