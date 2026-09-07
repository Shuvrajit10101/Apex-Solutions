using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// The two navigation-shell rows this branch ships: <b>14.2 Switch To (Ctrl+G)</b> and <b>14.9 Company menu
/// (Alt+K)</b>.
///
/// <para><b>14.4 More Details (Ctrl+I) is deliberately absent</b>, and the reasoning is recorded beside the
/// chord table in <c>ShellChordTable.Table</c>: taking <c>Ctrl+I</c> means taking it from a two-way
/// item-invoice toggle that <c>ServiceAccountingInvoiceKeyboardTests.CtrlI_stays_a_two_way_item_toggle</c>
/// locks on purpose, and the census records that chord ruling as OPEN.</para>
///
/// <para>🔴 <b>Why several of these walk the REALISED VISUAL TREE.</b> Asserting a view-model flag is exactly
/// the test that passes on the broken build — <c>PayrollMasterHighlightVisibilityTests</c> is this codebase's
/// record of four screens whose <c>IsHighlighted</c> was perfect and whose templates drew nothing, and this
/// project has twice filed a fully-implemented, fully-tested capability with no door
/// (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>). So where a claim is "the
/// operator can SEE this", the test looks for it on screen.</para>
/// </summary>
public sealed class ShellNavigationRowsTests : IDisposable
{
    /// <summary>The established keyboard-highlight fill, shared with every other list in this shell.</summary>
    private static readonly Color HighlightFill = Color.Parse("#FFF3CD");

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ShellNavigationRowsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexShellNav_" + Guid.NewGuid().ToString("N"));
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

    private static List<Visual> VisualsFor(MainWindow window, object row) =>
        Descendants(window)
            .Where(v => v is Control c && ReferenceEquals(c.DataContext, row))
            .ToList();

    private static bool WearsHighlight(MainWindow window, object row) =>
        VisualsFor(window, row).Any(v =>
            v is Border { IsEffectivelyVisible: true } b
            && b.Bounds.Width > 0 && b.Bounds.Height > 0
            && b.Background is ISolidColorBrush s && s.Color == HighlightFill);

    /// <summary>Every non-empty string a realised, visible TextBlock is showing.</summary>
    private static List<string> VisibleText(MainWindow window) =>
        Descendants(window)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    /// <summary>The realised Switch To list, or null when the panel is not on screen.</summary>
    private static ItemsControl? SwitchToList(MainWindow window) =>
        Descendants(window).OfType<ItemsControl>().FirstOrDefault(c => c.Name == "SwitchToResults");

    /// <summary>
    /// The Switch To list's OWN <see cref="ScrollViewer"/> — the nearest one ABOVE it, found by walking up.
    ///
    /// <para>🔴 <b>Not "the first ScrollViewer in the window that contains the list".</b> That reading was tried
    /// first and it silently answers the wrong question: a descendant walk returns the OUTERMOST match, which is
    /// the page-level scroller, whose extent equals its viewport because the page fits. The test then asserted
    /// against a scroller that never had anything to scroll — measured, and it read as "the list does not
    /// overflow" while the list was 2673px tall inside a 624px column.</para>
    /// </summary>
    private static ScrollViewer? SwitchToScroller(MainWindow window)
    {
        if (SwitchToList(window) is not { } list) return null;
        for (var p = list.GetVisualParent(); p is not null; p = p.GetVisualParent())
            if (p is ScrollViewer s) return s;
        return null;
    }

    // ================================================================ 14.2 — SWITCH TO (Ctrl+G)

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY <c>main</c>: <c>Key.G</c> returns ZERO hits in the whole of
    /// <c>src/Apex.Desktop</c>.</b> Vendor, verbatim: <c>Ctrl+G</c> — <i>"To switch to a different report, and
    /// create masters and vouchers in the flow of work."</i>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_G_opens_switch_to_from_the_gateway()
    {
        var (window, vm) = OpenWindow("Switch To Gateway Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.SwitchTo);
            Assert.Equal(Screen.SwitchTo, vm.CurrentScreen);
            Assert.NotEmpty(vm.SwitchTo!.Rows);
        }
        finally { window.Close(); }
    }

    /// <summary>Ctrl+G is reachable from a report too — it is a jump-anywhere chord, not a Gateway one.</summary>
    [AvaloniaFact]
    public void Ctrl_G_opens_switch_to_from_an_open_report()
    {
        var (window, vm) = OpenWindow("Switch To Report Co");
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.SwitchTo);
        }
        finally { window.Close(); }
    }

    /// <summary>Re-pressing must refocus, never stack a second panel — the guard <c>OpenSavedViews</c> carries.</summary>
    [AvaloniaFact]
    public void Ctrl_G_twice_does_not_stack_a_second_panel()
    {
        var (window, vm) = OpenWindow("Switch To Restack Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);
            var columns = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(columns, vm.Columns.Count);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE VENDOR-ATTESTED DISTINGUISHING BEHAVIOUR, and it FAILS ON <c>main</c>.</b> Switch To's one
    /// documented difference from Go To is that it does <b>not</b> return you to where you were (Go To
    /// <i>"takes you back to where you left"</i>). So after a jump the pre-jump page must be GONE from the
    /// cascade — buried is not gone.
    /// </summary>
    [AvaloniaFact]
    public void Switch_to_replaces_the_cascade_and_leaves_no_return_path()
    {
        var (window, vm) = OpenWindow("Switch To Replace Co");
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Pump(window);
            var preJump = vm.Columns.Last();
            Assert.Equal("Balance Sheet", vm.Reports!.Title);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            var target = vm.SwitchTo!.Rows.First(r => r.Label == "Trial Balance");
            Assert.True(vm.NavigateTo(target.Destination));
            Pump(window);

            Assert.DoesNotContain(preJump, vm.Columns);
            Assert.Null(vm.SwitchTo);
            Assert.Equal("Trial Balance", vm.Reports!.Title);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE FIRST PREFIX FILTER IN THE PRODUCT, and it FAILS ON <c>main</c> twice over</b> — the panel
    /// does not exist there, and the settled keyboard contract's PREFIX filtering exists nowhere in
    /// <c>src/</c> at all (S5 shipped type-to-JUMP). Typing must shrink the list AND the typed text must be
    /// on screen: a list that silently shrinks under an invisible filter is the defect, not the feature. This
    /// reads the REALISED TEXT, so a bound-but-undrawn <c>PrefixDisplay</c> cannot satisfy it.
    /// </summary>
    [AvaloniaFact]
    public void Typing_a_prefix_filters_the_destinations_and_the_typed_text_is_on_screen()
    {
        var (window, vm) = OpenWindow("Switch To Prefix Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);
            var all = vm.SwitchTo!.Rows.Count;
            Assert.True(all > 5, $"Only {all} destinations — the fixture would assert nothing about filtering.");

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
            Pump(window);

            Assert.Equal("tr", vm.SwitchTo!.Prefix);
            Assert.True(vm.SwitchTo.Rows.Count < all, "Typing a prefix did not shrink the list.");
            Assert.All(vm.SwitchTo.Rows, r => Assert.True(
                r.Label.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
                || r.Section.Split('→').Any(s => s.Trim().StartsWith("tr", StringComparison.OrdinalIgnoreCase)),
                $"'{r.Display}' matched a prefix filter it does not start with."));

            // 🔴 the typed text is VISIBLE — read off the realised tree, not off the view model.
            Assert.Contains(VisibleText(window), t => t.Contains("tr", StringComparison.Ordinal));

            // Backspace walks it back.
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Pump(window);
            Assert.Equal("t", vm.SwitchTo!.Prefix);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The keyboard cursor on the Switch To list is actually DRAWN — on the highlighted row and on no other.
    /// Enter fires the row the operator can see, so an invisible cursor here is the
    /// <c>PayrollMasterHighlight</c> defect with a jump instead of a delete.
    /// </summary>
    [AvaloniaFact]
    public void The_switch_to_cursor_is_visible_on_the_row_it_is_on()
    {
        var (window, vm) = OpenWindow("Switch To Cursor Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);

            var here = vm.SwitchTo!.Highlighted;
            Assert.NotNull(here);
            var other = vm.SwitchTo.Rows.First(r => !ReferenceEquals(r, here));

            Assert.True(WearsHighlight(window, here!),
                "The highlighted Switch To row is not wearing the cursor on screen.");
            Assert.False(WearsHighlight(window, other),
                "A non-highlighted Switch To row is wearing the cursor — the template paints it unconditionally.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE ANTI-FLAT-DUMP LOCK.</b> The standing rule is that every screen nests items under a parent
    /// section, never a flat dump. Each destination therefore carries the breadcrumb above it, and the panel
    /// draws the section before the label.
    /// </summary>
    [Fact]
    public void Every_destination_carries_a_parent_section()
    {
        var vm = NewCompany("Destination Sections Co");
        var destinations = ShellDestinations.Build(vm);

        Assert.NotEmpty(destinations);
        Assert.All(destinations, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Section), $"'{d.Label}' has no parent section.");
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
            Assert.Contains("→", d.Display, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 🔴 <b>THE REACHABILITY NET.</b> Every destination the jump list advertises is opened by REPLAYING the
    /// operator's own keystrokes through the menus — highlight the Group row, drill in, repeat, highlight the
    /// Page row, drill in — and must actually leave the Gateway. This is the direct answer to "a capability
    /// that exists as a service method no user can reach is NOT complete": a route that has rotted fails here
    /// rather than shipping as a dead row.
    /// </summary>
    [Fact]
    public void Every_destination_in_the_registry_is_openable_by_the_menus_that_advertise_it()
    {
        var vm = NewCompany("Reachability Co");
        var destinations = ShellDestinations.Build(vm);
        Assert.True(destinations.Count > 50,
            $"Only {destinations.Count} destinations were walked — the registry did not build.");

        var dead = new List<string>();
        foreach (var d in destinations)
        {
            if (!vm.NavigateTo(d)) { dead.Add(d.Display + "  (route not found)"); continue; }

            // "Opened" means the cascade actually went somewhere: the walk pushed at least one column past the
            // root, and something other than the bare Gateway root is the active pane.
            if (vm.Columns.Count <= 1) dead.Add(d.Display + "  (opened nothing)");
        }

        Assert.True(dead.Count == 0,
            "Destinations advertised by Switch To that no keystroke sequence reaches:\n  "
            + string.Join("\n  ", dead));
    }

    /// <summary>
    /// 🔴 <b>ONE REGISTRY, NOT TWO — the anti-drift lock.</b> Switch To (14.2) and Go To (14.1) are the same
    /// jump list with one documented difference (the return path), so they must offer the SAME destinations.
    /// A second, independently-maintained walk of the Gateway tree is how a navigation index goes stale in
    /// both directions, and this branch shipped exactly that before it was measured: its own walk indexed Page
    /// rows only and therefore could not reach a single menu HUB.
    ///
    /// <para><b>FAILS on this branch's earlier draft</b>, where the two sets differed by every group in the
    /// tree. Compared as multisets of (Section, Label, Path, OpensSubmenu) so a divergence in the breadcrumb
    /// spelling or in the replay path is caught too, not just a missing row.</para>
    /// </summary>
    [Fact]
    public void Switch_to_and_go_to_offer_exactly_the_same_destinations()
    {
        var vm = NewCompany("One Registry Co");

        static string Key(string section, string label, IReadOnlyList<string> path, bool submenu) =>
            $"{section}|{label}|{string.Join("/", path)}|{submenu}";

        var switchTo = ShellDestinations.Build(vm)
            .Select(d => Key(d.Section, d.Label, d.Path, d.OpensSubmenu)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var goTo = vm.BuildGoToIndex()
            .Select(d => Key(d.Section, d.Label, d.Path, d.OpensSubmenu)).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(switchTo);
        Assert.Equal(goTo, switchTo);
    }

    /// <summary>
    /// 🔴 <b>THE HOLE THE DUPLICATE WALK LEFT, locked shut.</b> Several report families are menu GROUPS rather
    /// than pages — the Account-Books pickers among them — so an index of Page rows alone cannot reach the
    /// Cash Book at all, which is precisely the kind of screen a jump list exists to find. Switch To must
    /// therefore both ADVERTISE a hub and actually OPEN it.
    ///
    /// <para><b>FAILS on this branch's earlier draft</b>, whose walk added a destination only for
    /// <c>MenuItemKind.Page</c> and recursed silently past every group.</para>
    /// </summary>
    [Fact]
    public void Switch_to_reaches_a_menu_hub_and_not_only_leaf_pages()
    {
        var vm = NewCompany("Hub Destination Co");
        var destinations = ShellDestinations.Build(vm);

        var hubs = destinations.Where(d => d.OpensSubmenu).ToList();
        Assert.True(hubs.Count > 0, "Switch To advertises no menu hub, so whole report families are unreachable.");

        var cashBook = destinations.FirstOrDefault(d => d.Label == "Cash Book");
        Assert.True(cashBook is not null,
            "Switch To cannot reach the Cash Book. It is a menu GROUP, so a page-only index misses it entirely.");

        Assert.True(vm.NavigateTo(cashBook!), "Switch To advertises the Cash Book but the replay does not reach it.");
        Assert.True(vm.Columns.Count > 1, "Jumping to the Cash Book opened nothing.");
    }

    /// <summary>A picker over company data is not a menu of screens, so its rows are not destinations.</summary>
    [Fact]
    public void Company_ledger_names_are_not_advertised_as_destinations()
    {
        var vm = NewCompany("No Ledger Rows Co");
        var c = vm.Company!;
        var group = c.FindGroupByName("Sundry Debtors")!;
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Zephyr Trading", group.Id, Money.Zero, openingIsDebit: true));

        var destinations = ShellDestinations.Build(vm);
        Assert.DoesNotContain(destinations, d => d.Label == "Zephyr Trading");
    }

    // ================================================================ 14.9 — COMPANY MENU (Alt+K)

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY <c>main</c>:</b> <c>Alt+K</c> outside report context reached nothing. Vendor,
    /// verbatim: <i>"To open the company menu with the list of actions related to managing your company."</i>
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_opens_the_company_menu_from_the_gateway()
    {
        var (window, vm) = OpenWindow("Company Menu Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(Screen.CompanyMenu, vm.CurrentScreen);
            var column = vm.Columns.Last();
            Assert.True(column.IsMenu, "The company menu was built as a page column, not a menu column.");
            Assert.Equal(CompanyMenu.ColumnTitle, column.Title);
            // W-I1 / census 16.2: the vendor's two user-management rows joined this menu, which is exactly
            // where the vendor reaches them ("Press Alt+K (Company) > Users and Passwords" / "> Password
            // Policy"). Both open a real screen — see SecurityControlScreenTests.
            Assert.Equal(
                new[] { "Create", "Alter", "Select", "Shut", "Users and Passwords", "Password Policy" },
                CompanyMenu.VerbsOf(column));

            // The keyboard cursor lands on a selectable row, not on a header.
            Assert.True(column.Selected?.IsSelectable == true);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE INCUMBENT-PRESERVATION LOCK.</b> Saved Views (census 14.7) is bound to <c>Alt+K</c> on a
    /// report and has no menu row anywhere, so that chord is its ONLY door. The company menu is scoped OUT of
    /// report context precisely so claiming <c>Alt+K</c> does not delete a shipped feature. This passes before
    /// and after; the day it goes red, a feature lost its only route in.
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_on_a_report_still_opens_saved_views()
    {
        var (window, vm) = OpenWindow("Saved Views Preserved Co");
        try
        {
            vm.OpenReport(ReportKind.BalanceSheet);
            Pump(window);
            Assert.True(vm.IsReportContext);

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.NotNull(vm.SavedViews);
            Assert.Equal(Screen.SavedViews, vm.CurrentScreen);
            Assert.NotEqual(Screen.CompanyMenu, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE ANTI-REGRESSION THAT CAUGHT W2-18, SHOWN STILL GREEN AND UNMODIFIED.</b> A "Company" section
    /// on the Gateway root column was built and removed TWICE. The Alt+K menu is an overlay column and touches
    /// no column builder, so the root column is byte-identical to what <c>GatewayHierarchyTests</c> pins.
    /// </summary>
    [AvaloniaFact]
    public void The_gateway_root_column_is_untouched_by_the_company_menu()
    {
        var (window, vm) = OpenWindow("Root Untouched Co");
        try
        {
            var expected = new[] { "Masters", "Statutory", "Transactions", "Reports", "Data" };
            Assert.Equal(expected, vm.Columns[0].Items.Where(i => i.IsHeader).Select(i => i.Label));

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(expected, vm.Columns[0].Items.Where(i => i.IsHeader).Select(i => i.Label));
            Assert.DoesNotContain(vm.Columns[0].Items, i => i.Label == "Company");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The three rows of the vendor's Alt+K list this build does not have. They live HERE, in the test, and
    /// deliberately not in <c>src</c>: the first is a vendor product name carrying the "Tally" brand, and
    /// <see cref="No_rendered_text_in_the_company_menu_carries_the_reference_products_brand"/> is the test
    /// that stops it reaching a screen. Naming the reference product in a test file is correct; shipping it
    /// in a rendered string is not.
    /// </summary>
    /// <summary>
    /// The vendor rows this menu still withholds. 🔴 <b>The list SHRANK on 2026-09-07 (census 16.2, W-I1)</b>:
    /// "Users and Passwords" and "Password Policy" are now built and now offered, so keeping them here would
    /// assert the opposite of what shipped. What remains withheld is the data-vault row (census 16.1, whose
    /// page-encryption half needs a new native dependency and a user ruling), "Change User" (which needs a
    /// signed-in session, deferred with the actor work) and "Edit Log" (census 16.4, reachable elsewhere).
    /// Naming the reference product is correct HERE, in the test file, and nowhere in <c>src/</c>.
    /// </summary>
    private static readonly string[] WithheldVendorRows = { "TallyVault", "Change User", "Edit Log" };

    /// <summary>
    /// 🔴 <b>THE HONEST-OMISSION LOCK.</b> The vendor's Alt+K list is Create · Alter · Select · TallyVault ·
    /// Change User · Edit Log. The last three are security &amp; audit, which this build does not have, and a
    /// row that opens a "not available" message is worse than no row. So they must be ABSENT as rows and the
    /// gap must be PRESENT as a disclosure the operator can read.
    /// </summary>
    [AvaloniaFact]
    public void The_company_menu_offers_only_verbs_this_application_has_and_says_what_it_withholds()
    {
        var (window, vm) = OpenWindow("Company Menu Honesty Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            var verbs = CompanyMenu.VerbsOf(vm.Columns.Last());
            foreach (var withheld in WithheldVendorRows)
                Assert.DoesNotContain(withheld, verbs);
            Assert.Equal(CompanyMenu.OfferedVerbs, verbs);

            // The disclosure is ON SCREEN. The cascade draws header rows through an uppercasing converter, so
            // the comparison is case-insensitive by necessity, not by laziness — the shipped glyphs really are
            // "COMPANY DATA ENCRYPTION IS NOT IN THIS BUILD" (narrowed from the security-and-audit wording when
            // census 16.2 shipped and made the broader claim false).
            Assert.Contains(
                VisibleText(window),
                t => t.Contains(CompanyMenu.Disclosure, StringComparison.OrdinalIgnoreCase));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE DE-BRAND LOCK, and it caught a live leak.</b> The predecessor draft of this menu composed its
    /// disclosure from the vendor's own row names and rendered
    /// <c>"NOT IN THIS BUILD: TALLYVAULT, CHANGE USER, EDIT LOG (SECURITY &amp; AUDIT)"</c> on screen — the
    /// reference product's brand, in a user-visible string, in the shipped app. Every sibling report screen in
    /// this suite carries an <c>Assert.DoesNotContain("Tally", …)</c>; the company menu had none, which is
    /// exactly why the leak survived to a commit. This one reads the REALISED TREE rather than a view-model
    /// string, so it also covers anything a template composes on its way to the screen.
    /// </summary>
    [AvaloniaFact]
    public void No_rendered_text_in_the_company_menu_carries_the_reference_products_brand()
    {
        var (window, _) = OpenWindow("Company Menu Debrand Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            foreach (var shown in VisibleText(window))
                Assert.DoesNotContain("Tally", shown, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE DISCLOSURE MUST BE READABLE, NOT MERELY PRESENT — and this is the assertion the "it is on
    /// screen" test above cannot make.</b> A <c>TextBlock</c> whose <c>Text</c> property holds the whole
    /// sentence satisfies every string assertion in this file while painting 39% of it and hard-cutting the
    /// rest mid-word, because <c>Text</c> is what the view-model set, not what the operator read. That is
    /// precisely what shipped: measured at 1280x720, the predecessor line needed <b>888px</b> of advance width
    /// and was arranged into <b>350px</b> with <c>TextWrapping=NoWrap</c> and <c>TextTrimming=None</c>, so the
    /// screen read "NOT IN THIS BUILD: TALLYV" and nothing signalled the loss.
    ///
    /// <para>The fix is two-sided and both sides are asserted here: the disclosure was shortened to a budget,
    /// and the cascade's shared section-header template was given <c>TextWrapping="Wrap"</c> so that a header
    /// too wide for its column FOLDS instead of vanishing. So this measures the realised row: every line the
    /// wrapped header produces must fit the width it was actually given.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_company_menus_disclosure_is_fully_readable_and_not_silently_cut()
    {
        var (window, _) = OpenWindow("Company Menu Fit Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            var block = Descendants(window)
                .OfType<TextBlock>()
                .Single(t => t.IsEffectivelyVisible
                             && (t.Text ?? string.Empty)
                                 .Contains(CompanyMenu.Disclosure, StringComparison.OrdinalIgnoreCase));

            Assert.True(block.Bounds.Width > 0 && block.Bounds.Height > 0,
                "The disclosure is not laid out at all.");

            // A header that neither wraps nor trims is a hard cut with no ellipsis — the defect this locks out.
            Assert.True(
                block.TextWrapping != TextWrapping.NoWrap || block.TextTrimming != TextTrimming.None,
                "The cascade section header can neither wrap nor trim, so any header wider than its column is " +
                "cut mid-word with no signal to the operator.");

            // The realised paint must fit the width it was given. Measured in the SAME font, wrapped to the
            // SAME width — headless supplies advance widths, which is what makes this assertion portable.
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

            Assert.True(
                probe.DesiredSize.Width <= block.Bounds.Width + 0.5,
                $"The disclosure needs {probe.DesiredSize.Width:F0}px but was arranged into " +
                $"{block.Bounds.Width:F0}px, so part of it is not on screen.");
            Assert.True(
                block.Bounds.Height + 0.5 >= probe.DesiredSize.Height,
                $"The disclosure wraps to {probe.DesiredSize.Height:F0}px but was arranged into " +
                $"{block.Bounds.Height:F0}px, so a wrapped line is clipped.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE LAYOUT-NEUTRALITY PROOF for the <c>TextWrapping="Wrap"</c> added to the shared cascade
    /// section-header template.</b> That template draws the header of EVERY menu column on every screen in
    /// this shell, so the change is only defensible if it is a no-op for the headers that already fit — and
    /// "it should be" is not a measurement. The five Gateway root headers need 88 / 113 / 150 / 88 / 50px
    /// against ~349px of column, so none of them can reach a second line; this asserts each is still a single
    /// unwrapped line occupying exactly the width it needs.
    /// </summary>
    [AvaloniaFact]
    public void Wrapping_the_cascade_section_header_left_the_gateway_headers_on_one_line()
    {
        var (window, vm) = OpenWindow("Header Neutrality Co");
        try
        {
            var headers = vm.Columns[0].Items.Where(i => i.IsHeader).Select(i => i.Label).ToArray();
            Assert.Equal(new[] { "Masters", "Statutory", "Transactions", "Reports", "Data" }, headers);

            foreach (var label in headers)
            {
                var block = Descendants(window)
                    .OfType<TextBlock>()
                    .Single(t => t.IsEffectivelyVisible
                                 && string.Equals(t.Text, label.ToUpperInvariant(), StringComparison.Ordinal));

                var probe = new TextBlock
                {
                    Text = block.Text,
                    FontSize = block.FontSize,
                    FontFamily = block.FontFamily,
                    FontWeight = block.FontWeight,
                    FontStyle = block.FontStyle,
                    LetterSpacing = block.LetterSpacing,
                };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                Assert.True(
                    probe.DesiredSize.Width <= block.Bounds.Width + 0.5,
                    $"Gateway header \"{label}\" needs {probe.DesiredSize.Width:F0}px in " +
                    $"{block.Bounds.Width:F0}px — it no longer fits on one line.");
                Assert.True(
                    block.Bounds.Height <= probe.DesiredSize.Height + 0.5,
                    $"Gateway header \"{label}\" grew to {block.Bounds.Height:F0}px from a natural " +
                    $"{probe.DesiredSize.Height:F0}px — wrapping was NOT layout-neutral for it.");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The menu's Shut row runs the same release <c>Ctrl+F3</c> does — the second door that makes publishing
    /// <c>ReleaseOpenCompany</c> correct rather than another method no operator can reach.
    /// </summary>
    [AvaloniaFact]
    public void The_company_menus_shut_row_releases_the_open_company()
    {
        var (window, vm) = OpenWindow("Company Menu Shut Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            var column = vm.Columns.Last();
            var shutIndex = column.Items.ToList().FindIndex(i => i.Label == "Shut");
            Assert.True(shutIndex >= 0, "The company menu has no Shut row.");
            column.SetSelected(shutIndex);

            vm.DrillIn();     // Enter on the highlighted row

            Assert.Null(vm.Company);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    // 🔴 CARRY-FORWARD, NOT SHIPPED. The company menu's Create / Alter / Select rows are asserted by LABEL
    // above but never PRESSED — only Shut is driven through DrillIn. A label is an advertisement, and this
    // project has twice filed a capability that was implemented, tested and had no working door. A per-row
    // activation test was written for exactly this and is NOT included: on this machine it made xUnit test
    // DISCOVERY hang (>7 min, then "Catastrophic failure … exit code 143" with the assembly's tests never
    // enumerated) both as an [AvaloniaTheory] and as three [AvaloniaFact]s, while the rest of this class
    // continued to run in ~14s. The hang was NOT diagnosed and may be a contended-machine artefact
    // (eleven worktrees were compiling) rather than a product defect — but it was not proven either way, so
    // shipping the test would have been shipping an unexplained hang. Re-attempt on a quiet machine.

    /// <summary>Alt+K is a no-op with no company — there is nothing to manage.</summary>
    [Fact]
    public void Alt_K_is_not_claimed_with_no_company_open()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.True(ShellChordTable.Match(vm, Key.K, KeyModifiers.Alt) is null);
    }

    /// <summary>
    /// 🔴 <b>Re-pressing Alt+K must not stack a second company menu</b> — the Alt+K counterpart of
    /// <see cref="Ctrl_G_twice_does_not_stack_a_second_panel"/>, and it is here because a review found the
    /// re-entrancy line in <c>OpenCompanyMenu</c> had NO test at all: deleting it left the whole suite green.
    /// A guard nothing exercises is a comment.
    ///
    /// <para>The column count alone would not have caught the interesting half — <c>OpenCompanyMenu</c> calls
    /// <c>ClearSubScreens</c> before pushing, so a second open would also tear down whatever the first was
    /// stacked over — so the number of COMPANY columns is asserted too.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_twice_does_not_stack_a_second_company_menu()
    {
        var (window, vm) = OpenWindow("Company Menu Restack Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);
            var columns = vm.Columns.Count;
            Assert.Equal(Screen.CompanyMenu, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.Equal(columns, vm.Columns.Count);
            Assert.Equal(1, vm.Columns.Count(c => c.Title == "Company"));
        }
        finally { window.Close(); }
    }

    // ================================================================ the shell state machine
    //
    // 🔴 THE BLANK WINDOW THAT OWNED THE KEYBOARD, reachable in TWO KEYSTROKES from the Gateway, and both of
    // these tests are RED on this branch before the fix.
    //
    // ShowCompanySelect (bare F3, the button bar's Company action, and the new Alt+F3) runs LeaveCascade() —
    // Columns.Clear() and IsGatewayCascade = false — but deliberately does NOT release the company: the operator
    // is choosing another book and the loaded one stays loaded until they pick. Both new chords were predicated
    // on `Company is not null`, which is TRUE in that state, so each pushed its column into a region nothing
    // draws AND moved CurrentScreen to its own screen id — which took IsMenuScreen false as well, so the
    // centred company list vanished too and the window went blank with the panel still eating every keystroke.

    /// <summary>Alt+F3 then Ctrl+G: the panel must not be raised into a cascade that is hidden and empty.</summary>
    [AvaloniaFact]
    public void Ctrl_G_after_alt_F3_does_not_raise_a_panel_into_the_hidden_cascade()
    {
        var (window, vm) = OpenWindow("Switch To Blank Window Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Alt);
            Pump(window);

            // The state the defect needed: book open, cascade hidden and empty.
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.NotNull(vm.Company);
            Assert.False(vm.IsGatewayCascade);
            Assert.Empty(vm.Columns);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            Assert.Null(vm.SwitchTo);
            Assert.Empty(vm.Columns);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.True(vm.IsMenuScreen, "the centred company list is gone — this is the blank window.");
            Assert.True(ShellChordTable.Match(vm, Key.G, KeyModifiers.Control) is null,
                "the table still CLAIMS Ctrl+G in a state that cannot show its panel.");

            // ...and the operator can still read the company list they are standing on.
            Assert.Contains(VisibleText(window), t => t.Contains("Create Company", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>Alt+F3 then Alt+K: the same hole, reached through the company menu instead.</summary>
    [AvaloniaFact]
    public void Alt_K_after_alt_F3_does_not_raise_the_company_menu_into_the_hidden_cascade()
    {
        var (window, vm) = OpenWindow("Company Menu Blank Window Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.NotNull(vm.Company);

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.Empty(vm.Columns);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.True(vm.IsMenuScreen);
            Assert.True(ShellChordTable.Match(vm, Key.K, KeyModifiers.Alt) is null);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Alt+K is the OTHER door to the destructive verb, and it destroyed the work on the way IN.</b>
    /// <c>OpenCompanyMenu</c> calls <c>ClearSubScreens</c> before it builds the menu, which nulls
    /// <c>VoucherEntry</c> unconditionally — so guarding only <c>ShutCompany</c> would have been decorative: by
    /// the time the Shut row ran, the voucher it was meant to protect was already gone.
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_refuses_over_a_half_keyed_voucher_and_says_why()
    {
        var (window, vm) = OpenWindow("Company Menu Dirty Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Pump(window);
            vm.VoucherEntry!.Narration = "half keyed — must survive Alt+K";
            Assert.True(vm.VoucherEntry.HasUnsavedWork);
            var entry = vm.VoucherEntry;

            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
            Pump(window);

            Assert.Same(entry, vm.VoucherEntry);
            Assert.Equal("half keyed — must survive Alt+K", vm.VoucherEntry!.Narration);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.DoesNotContain(vm.Columns, c => c.Title == "Company");
            Assert.Contains("discard", vm.Notice, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    // ================================================================ the two jump lists

    /// <summary>
    /// 🔴 <b>TWO JUMP LISTS AT ONCE, and the one the operator could NOT see took the typing.</b> Go To is a
    /// floating overlay; the window's Go To arm owns only Up/Down/Enter/Escape while it is up, so Ctrl+G fell
    /// straight through to the chord table and opened Switch To underneath it. Every printable character then
    /// reached the Switch To arm — which sits below Go To's in the chain — and was filtered into the hidden list
    /// while the visible search box stayed empty.
    ///
    /// <para><b>Ctrl+G REPLACES Go To rather than declining.</b> Declining would leave Ctrl+G a dead key: there
    /// is no other arm anywhere below that takes it, so the operator would press the vendor's Switch To chord
    /// and get nothing at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_G_replaces_an_open_go_to_rather_than_stacking_a_second_jump_list()
    {
        var (window, vm) = OpenWindow("Two Jump Lists Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsGoToOpen);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            Assert.False(vm.IsGoToOpen, "both jump lists are up at once.");
            Assert.NotNull(vm.SwitchTo);
            Assert.Equal(Screen.SwitchTo, vm.CurrentScreen);

            // And the typing now reaches the list that is on screen.
            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
            Pump(window);
            Assert.Equal("b", vm.SwitchTo!.Prefix);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The mirror image, and it is the half that actually loses the keystrokes: Go To raised OVER an open Switch
    /// To column. The overlay is what the operator is looking at and its search box is focused, so the Switch To
    /// arm must stand down — otherwise it eats the characters and filters a list nobody can see.
    /// </summary>
    [AvaloniaFact]
    public void A_go_to_raised_over_switch_to_does_not_lose_the_typing_to_the_hidden_list()
    {
        var (window, vm) = OpenWindow("Jump List Typing Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);
            Assert.NotNull(vm.SwitchTo);

            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsGoToOpen);

            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(string.Empty, vm.SwitchTo!.Prefix);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>The jump list must not outlive the company it indexes.</b> <c>ClearSubScreens</c> does not null
    /// <c>GoTo</c> — the overlay is not a column — so a shut left Go To floating over Company Select, listing
    /// screens of a book that is no longer open, with Enter still armed. <c>CanOpenGoTo</c> already refuses to
    /// OPEN it on that screen; one already up was the same state arrived at from the other side.
    /// </summary>
    [AvaloniaFact]
    public void Shutting_the_company_dismisses_the_go_to_overlay()
    {
        var (window, vm) = OpenWindow("Go To Outlives Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsGoToOpen);

            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Control);
            Pump(window);

            Assert.Null(vm.Company);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.False(vm.IsGoToOpen, "the jump list is still up, indexing a company that is shut.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>AUTO-SCROLL ON SELECTION — ~4/5 of this list was unreachable by keyboard.</b> The Switch To cursor
    /// is a bound flag on the row, never focus (focus has to stay put so a bare letter types into the prefix
    /// filter), so nothing drags the viewport after it the way a focusable grid does. The list opens unfiltered
    /// over ~99 destinations at 26px each, several times the column's height. The identical defect and the
    /// identical fix are recorded on Go To in <c>GoToChordReachabilityTests</c>.
    /// </summary>
    [AvaloniaFact]
    public void Arrowing_past_the_fold_scrolls_the_switch_to_highlight_into_view()
    {
        var (window, vm) = OpenWindow("Switch To Scroll Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            var list = SwitchToList(window);
            Assert.NotNull(list);
            var scroller = SwitchToScroller(window);
            Assert.NotNull(scroller);
            Assert.True(scroller!.Extent.Height > scroller.Viewport.Height,
                "the unfiltered list must overflow the column for this test to mean anything.");
            Assert.Equal(0d, scroller.Offset.Y);

            for (var i = 0; i < 40; i++) window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(40, vm.SwitchTo!.SelectedIndex);

            Assert.True(scroller.Offset.Y > 0d,
                "the highlight moved 40 rows down and the panel never scrolled — the operator cannot see it.");

            var row = (Visual?)list!.ContainerFromIndex(40);
            Assert.NotNull(row);
            var top = row!.TranslatePoint(default, scroller)!.Value.Y;
            Assert.InRange(top, -1d, scroller.Viewport.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Typing REBUILDS the list and drops the highlight back to row one, so the column has to come back up with
    /// it — otherwise the operator filters from a scrolled-down position and reads an empty stretch of list while
    /// row one is selected far above them. Backspace is the same rebuild in reverse.
    /// </summary>
    [AvaloniaFact]
    public void Filtering_brings_the_switch_to_list_back_to_the_first_row()
    {
        var (window, vm) = OpenWindow("Switch To Refilter Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
            Pump(window);

            var scroller = SwitchToScroller(window);
            Assert.NotNull(scroller);

            for (var i = 0; i < 40; i++) window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.True(scroller!.Offset.Y > 0d);

            window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(0, vm.SwitchTo!.SelectedIndex);
            Assert.Equal(0d, scroller.Offset.Y);
        }
        finally { window.Close(); }
    }
}
