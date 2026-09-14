using System;
using System.Collections.Generic;
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
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 6.24 (Input Service Distributor) — the LAST mile, driven by the keyboard alone.</b>
///
/// <para>🔴 <b>WHY THIS FILE PRESSES KEYS AND WALKS THE REALISED VISUAL TREE.</b> This project has filed three
/// dead features — a correct engine with no way in, the largest ~625 lines whose only callers were tests. A test
/// that called <c>vm.OpenGstr6Report()</c> and asserted the property became non-null would stay green on the day
/// the menu row, the dispatch case or the DataTemplate went missing, and those three are what make a capability
/// reachable. So each test starts on the Gateway and walks Reports → Statutory Reports → GST Returns (Advanced) →
/// GSTR-6 (ISD) with ArrowDown and Enter through <see cref="MainWindow"/>'s own key handler.</para>
///
/// <para><b>The gate is tested in both directions</b>, because the GSTR-6 row is conditional: it appears only for
/// a company that actually holds an Input Service Distributor registration. GSTR-6 is filed by an ISD and by
/// nobody else (§39(4) of the CGST Act, <c>cbic-gst.gov.in</c>), so offering the row to everyone would be a false
/// affordance — and hiding it forever would make the row unreachable. Both failures are guarded here.</para>
///
/// <para>Headless-safe: visual-tree, text and enabled-state inspection only. Culture-invariant, and every path is
/// built with <see cref="Path.Combine"/>, so this behaves identically on the ubuntu and macos CI legs.</para>
/// </summary>
public sealed class Gstr6IsdReachabilityTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(1280, 800));
        w.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static List<TextBlock> VisibleTextBlocks(Window w) =>
        Descendants(w)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .ToList();

    private static bool ScreenShows(Window w, string fragment) =>
        VisibleTextBlocks(w).Any(t => (t.Text ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    private static List<T> VisibleControls<T>(Window w) where T : Control =>
        Descendants(w).OfType<T>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            .ToList();

    private static void KeyboardInto(MainWindow w, MainWindowViewModel vm, string label)
    {
        var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(offered.Contains(label),
            $"The keyboard cascade never offered '{label}'. This column offers: {string.Join(" | ", offered)}. " +
            "A screen with no menu row is unreachable and does not move a census row.");

        for (var i = 0; i <= vm.Menu.Count; i++)
        {
            if (vm.SelectedIndex >= 0
                && vm.SelectedIndex < vm.Menu.Count
                && vm.Menu[vm.SelectedIndex].Label == label)
            {
                w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Pump(w);
                return;
            }
            w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(w);
        }

        Assert.Fail($"ArrowDown never landed the highlight on '{label}' within one lap of the column.");
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexT3_" + tag + "_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        return (window, vm, dir);
    }

    private static void Cleanup(MainWindow w, string dir)
    {
        w.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A saved Regular-GST Karnataka company, optionally holding a head-office ISD registration beside
    /// its operating one and a Tamil Nadu branch — the CBIC ABC Ltd shape.</summary>
    private static void SeedCompany(MainWindowViewModel vm, string name, bool withIsd)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        if (withIsd)
        {
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Head Office (ISD)", Karnataka, GstinFor(Karnataka, "AAACC1206D"),
                GstRegistrationType.InputServiceDistributor, FyStart));
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Tamil Nadu Registration", "33", GstinFor("33", "AABCC1206D"),
                GstRegistrationType.Regular, FyStart));
            c.Gst!.EnsureValid();
        }

        vm.ShowGateway();
    }

    // ================================================================ the row

    /// <summary>
    /// 🔴 THE ROW-6.24 TEST. Gateway → Reports → Statutory Reports → GST Returns (Advanced) → GSTR-6 (ISD), by
    /// ArrowDown and Enter alone, ending on a realised page carrying the form's own captions, its three pickers
    /// and the Rule 39(1)(b) footing line.
    /// </summary>
    [AvaloniaFact]
    public void Gstr6_is_reachable_from_the_gateway_by_keyboard_alone_once_an_isd_registration_exists()
    {
        var (w, vm, dir) = NewWindow("Gstr6Reach");
        try
        {
            SeedCompany(vm, "ISD Reach Co", withIsd: true);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            KeyboardInto(w, vm, "GSTR-6 (ISD)");

            Assert.Equal(Screen.Gstr6Report, vm.CurrentScreen);
            Assert.NotNull(vm.Gstr6Report);

            // The page is REALISED — these captions exist only in the DataTemplate, so a missing template fails here.
            Assert.True(ScreenShows(w, "Form GSTR-6"), "The GSTR-6 title is not on screen.");
            Assert.True(ScreenShows(w, "Input Service Distributor"), "The distributor block is not on screen.");
            Assert.True(ScreenShows(w, "Relevant period for the turnover ratio"),
                "The Rule 39 Explanation (a) relevant period is not shown, so the basis of the pro rata is invisible.");
            Assert.True(ScreenShows(w, "Distribution of input tax credit (Rule 39)"),
                "The distribution table heading is not on screen.");

            // 🔴 The Rule 39(1)(b) check must be VISIBLE, not derivable. A filer who cannot see received vs distributed
            // cannot see the one error that matters on this return.
            Assert.True(ScreenShows(w, "Total credit received"), "The credit received is not on screen.");
            Assert.True(ScreenShows(w, "Total distributed"), "The distributed total is not on screen.");
            Assert.True(ScreenShows(w, "Undistributed (received − distributed)"),
                "The undistributed difference is not on screen — Rule 39(1)(b) cannot be checked at a glance.");

            // The operator can pick a distributor, a year and a month.
            Assert.True(VisibleControls<ComboBox>(w).Count >= 3,
                "The distributor / financial-year / month pickers are not realised.");

            // The head office ISD is offered by name, and the page is showing it.
            var page = vm.Gstr6Report!;
            Assert.Single(page.IsdRegistrations);
            Assert.Equal("Head Office (ISD)", page.SelectedIsd!.Name);
            Assert.True(ScreenShows(w, "Head Office (ISD)"), "The selected distributor is not shown on the page.");

            // 🔴 THE LAYOUT FIX MUST NOT HAVE COST A FACT. The distribution grid originally gave the recipient's
            // State and its Rule 39(1)(g) eligibility fixed columns of their own, which starved the star Recipient
            // column to 34px (XamlLayoutInvariantTests caught it). They moved to the row's sub-line — so the
            // heading must still ANNOUNCE them, or a reader of a filed statement cannot tell an eligible row from
            // an ineligible one, nor see the State that Rule 39(1)(j) turns on.
            Assert.True(ScreenShows(w, "Recipient (State · eligibility · GSTIN below)"),
                "The distribution grid no longer tells the reader where the State and eligibility are.");

            // …and the row view-model still carries all three, joined, whatever the grid does with them.
            var row = new Apex.Desktop.ViewModels.Gstr6RowVm
            {
                Recipient = "Karnataka Registration", StateCode = "29",
                Eligibility = "Eligible", Gstin = "29AAACC1206D1ZM",
            };
            Assert.Contains("29", row.SubLine);
            Assert.Contains("Eligible", row.SubLine);
            Assert.Contains("29AAACC1206D1ZM", row.SubLine);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The gate, the other way round: a company with no ISD registration is not offered the row at all, and the
    /// open path is a no-op even if something calls it. ER-13 — the GST Returns (Advanced) column such a company
    /// sees is the one it saw before this slice.
    /// </summary>
    [AvaloniaFact]
    public void A_company_without_an_isd_registration_is_not_offered_the_gstr6_row()
    {
        var (w, vm, dir) = NewWindow("Gstr6Gate");
        try
        {
            SeedCompany(vm, "No ISD Co", withIsd: false);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");

            var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.DoesNotContain("GSTR-6 (ISD)", offered);

            // The sibling rows are all still there — the column was not otherwise disturbed.
            Assert.Contains("Electronic Ledgers", offered);
            Assert.Contains("ITC Set-Off", offered);
            Assert.Contains("Offline Return Files (JSON)", offered);

            vm.OpenGstr6Report();
            Pump(w);
            Assert.Null(vm.Gstr6Report);
            Assert.NotEqual(Screen.Gstr6Report, vm.CurrentScreen);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The registration type has to be creatable, or the gate above can never open. The master's picker must offer
    /// "Input Service Distributor", and choosing it must produce a registration of that type — after which the
    /// GSTR-6 row appears in the menu that did not have it a moment earlier.
    /// </summary>
    [AvaloniaFact]
    public void Creating_an_isd_registration_through_the_master_makes_the_gstr6_row_appear()
    {
        var (w, vm, dir) = NewWindow("Gstr6Create");
        try
        {
            SeedCompany(vm, "ISD Create Co", withIsd: false);
            Pump(w);

            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Registration");

            var master = vm.GstRegistrationsMaster!;
            Assert.Contains("Input Service Distributor", master.RegistrationTypeOptions);

            // An ISD in the company's OWN State — CBIC's ABC Ltd example is exactly that, a Bangalore corporate
            // office acting as ISD beside a Bangalore business location. Census row 6.23's set rule used to refuse
            // this shape outright; the relaxation is narrow, and this is the screen that depends on it.
            master.SelectedState = master.StateOptions.Single(s => s.StartsWith(Karnataka, StringComparison.Ordinal));
            master.Gstin = GstinFor(Karnataka, "AAACC1206D");
            master.Name = "Head Office (ISD)";
            master.SelectedRegistrationType = "Input Service Distributor";
            Assert.True(master.Create(), $"Create failed: {master.Message}");
            Pump(w);

            var added = Assert.Single(vm.Company!.Gst!.AdditionalRegistrations);
            Assert.Equal(GstRegistrationType.InputServiceDistributor, added.RegistrationType);
            Assert.True(vm.Company!.Gst!.HasIsdRegistration);

            // …and the row is now in the menu it was absent from.
            vm.ShowGateway();
            Pump(w);
            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            Assert.Contains("GSTR-6 (ISD)", vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label));
        }
        finally { Cleanup(w, dir); }
    }
}
