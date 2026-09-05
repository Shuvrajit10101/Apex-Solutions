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
/// The three navigation-shell rows: <b>14.2 Switch To (Ctrl+G)</b>, <b>14.9 Company menu (Alt+K)</b> and
/// <b>14.4 More Details (Ctrl+I)</b>.
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
                || r.Section.Split('›').Any(s => s.Trim().StartsWith("tr", StringComparison.OrdinalIgnoreCase)),
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
            Assert.Contains("›", d.Display, StringComparison.Ordinal);
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
            Assert.Equal(new[] { "Create", "Alter", "Select", "Shut" }, CompanyMenu.VerbsOf(column));

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
    /// 🔴 <b>THE HONEST-OMISSION LOCK.</b> The vendor's Alt+K list is Create · Alter · Select · TallyVault ·
    /// Change User · Edit Log. The last three are security &amp; audit, which this build does not have, and a
    /// row that opens a "not available" message is worse than no row. So they must be ABSENT as rows and
    /// PRESENT as a disclosure the operator can read.
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
            foreach (var withheld in CompanyMenu.WithheldVerbs)
                Assert.DoesNotContain(withheld, verbs);
            Assert.Equal(CompanyMenu.OfferedVerbs, verbs);

            // The disclosure is on screen, and it names each withheld verb.
            var shown = VisibleText(window);
            Assert.Contains(shown, t => t.Contains("Not in this build", StringComparison.Ordinal));
            foreach (var withheld in CompanyMenu.WithheldVerbs)
                Assert.Contains(shown, t => t.Contains(withheld, StringComparison.Ordinal));
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

    /// <summary>Alt+K is a no-op with no company — there is nothing to manage.</summary>
    [Fact]
    public void Alt_K_is_not_claimed_with_no_company_open()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.True(ShellChordTable.Match(vm, Key.K, KeyModifiers.Alt) is null);
    }

    // ================================================================ 14.4 — MORE DETAILS (Ctrl+I)

    /// <summary>
    /// A seeded company with an item and a bill-by-bill customer, standing on a Sales item-invoice whose
    /// Bill-wise screen is hidden by the shipped default option. That is the exact state the vendor's More
    /// Details exists for.
    /// </summary>
    private (MainWindow Window, MainWindowViewModel Vm, VoucherEntryViewModel Entry) OpenHiddenBillWiseInvoice(
        string companyName)
    {
        var (window, vm) = OpenWindow(companyName);
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", billWise: true);
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);

        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == item.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        line.QuantityText = "3";
        line.RateText = "1234.57";
        entry.RecalculateItemInvoice();

        // The state under test: the allocation APPLIES, and the screen option is hiding it.
        Assert.True(entry.InvoiceBillWiseApplies);
        Assert.True(entry.UseDefaultBillWiseAllocation);
        Assert.False(entry.ShowInvoiceBillWise);

        Pump(window);
        return (window, vm, entry);
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool billWise = false)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false)
        {
            MaintainBillByBill = billWise,
        };
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// 🔴 <b>FAILS ON TODAY <c>main</c>:</b> there, <c>Ctrl+I</c> on this voucher toggled item-invoice mode.
    /// Vendor: <i>"To add more details to a master or voucher for the current instance."</i>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_I_opens_more_details_on_an_open_voucher()
    {
        var (window, vm, entry) = OpenHiddenBillWiseInvoice("More Details Open Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Pump(window);

            Assert.NotNull(vm.MoreDetails);
            Assert.Equal(Screen.MoreDetails, vm.CurrentScreen);
            Assert.Contains(vm.MoreDetails!.Rows, r => r.Label == "Bill-wise Details");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE DEFINING BEHAVIOUR, VERBATIM FROM THE VENDOR, AND IT FAILS ON <c>main</c>:</b> <i>"press
    /// Ctrl+I (More Details) to enter any of the values <b>without activating the options in F12
    /// (Configure)</b>."</i>
    ///
    /// <para>So after More Details reveals the Bill-wise screen for THIS voucher, the owning option must be
    /// bit-for-bit what it was. A build that simply flipped the knob would satisfy "the field appeared" and
    /// fail here — which is the whole reason this assertion is separate from the one above.</para>
    /// </summary>
    [AvaloniaFact]
    public void More_details_reveals_the_field_and_does_not_flip_the_owning_option()
    {
        var (window, vm, entry) = OpenHiddenBillWiseInvoice("More Details No Flip Co");
        try
        {
            var optionBefore = entry.UseDefaultBillWiseAllocation;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.True(entry.ShowInvoiceBillWise,
                "More Details did not reveal the Bill-wise screen for this voucher.");
            Assert.Equal(optionBefore, entry.UseDefaultBillWiseAllocation);   // 🔴 the knob is untouched
            Assert.True(entry.UseDefaultBillWiseAllocation);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE HONEST-FOOTER LOCK.</b> The vendor's headline More Details example reaches <b>Ledger
    /// Narration</b> — a narration PER LINE — and <c>EntryLine</c> has no narration field, so this build
    /// cannot offer it without a schema change this track does not take. The panel must SAY so, on screen, so
    /// a later slice cannot quietly drop the disclosure and call census row 14.4 complete.
    /// </summary>
    [AvaloniaFact]
    public void The_more_details_panel_declares_the_field_it_cannot_offer()
    {
        var (window, vm, entry) = OpenHiddenBillWiseInvoice("More Details Footer Co");
        try
        {
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Pump(window);

            Assert.Contains(MoreDetailsViewModel.WithheldField, vm.MoreDetails!.Footnote, StringComparison.Ordinal);

            var shown = VisibleText(window);
            Assert.Contains(shown, t => t.Contains("Ledger Narration", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// When the option is already OFF the field is on the voucher and there is nothing to reveal, so More
    /// Details must not offer a row that would do nothing. An empty panel is the correct answer here, and it
    /// says so rather than looking broken.
    /// </summary>
    [AvaloniaFact]
    public void More_details_offers_no_row_for_a_field_that_is_already_visible()
    {
        var (window, vm, entry) = OpenHiddenBillWiseInvoice("More Details Already Shown Co");
        try
        {
            entry.UseDefaultBillWiseAllocation = false;   // the operator turned the option off by hand
            Assert.True(entry.ShowInvoiceBillWise);

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Pump(window);

            Assert.DoesNotContain(vm.MoreDetails!.Rows, r => r.Label == "Bill-wise Details");
            Assert.Contains("already on the screen", vm.MoreDetails.Status, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }
}
