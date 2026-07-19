using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE UNREACHABLE-FIELD RULE, generalised from two screens to the whole master-page CLASS.
///
/// <para><b>What this locks.</b> <see cref="LedgerMasterScrollLayoutTests"/> and
/// <see cref="ScenarioMasterScrollLayoutTests"/> lock the defect on the two templates that had actually
/// degraded. The defect is not specific to those two: it is a property of the page-root row shape
/// <c>&lt;Grid RowDefinitions="Auto,Auto,Auto,*"&gt;</c> with the create/edit FORM in row 1. An <c>Auto</c> row
/// takes its child's FULL desired height and NEVER constrains it, so as the form grows it (a) starves the
/// sibling <c>*</c> list row — measured at EXACTLY 0px on both Ledger and Scenario — and (b) never overflows,
/// so no <see cref="ScrollViewer"/> can see the excess and no scrollbar can ever appear. Fields a user cannot
/// reach are fields a user cannot fill.</para>
///
/// <para><b>Why a test and not 21 edits.</b> Measured on an unmodified tree, ZERO of the master pages strand
/// content today: every one reports rows summing exactly to the pane with a non-zero list row, at 1920x1080 /
/// 1600x900 / 1366x768 / 1280x720. They are fine because their forms happen to be SHORT, not because the shape
/// is sound — which is precisely how the bug arrived: the old catalogue recorded Ledger's list row at ~55px
/// and it had degraded to 0px by the time a user hit it. Converting 17 short-form screens to <c>3*</c> would
/// inflate a 200px form to 315px and shrink its list from ~325 to ~210 for no benefit — trading a real
/// usability regression for a hypothetical one. So the durable fix is this INVARIANT, which turns RED the
/// instant any future feature block pushes a form past its budget, on ANY of these screens.</para>
///
/// <para><b>Why it cannot be a shared Style.</b> <c>Grid.RowDefinitions</c> is a plain CLR property in Avalonia
/// 12.0.5, not an <see cref="AvaloniaProperty"/> — the shipped reference assembly exposes
/// <c>get_RowDefinitions</c>/<c>set_RowDefinitions</c> but no <c>RowDefinitionsProperty</c> field (contrast
/// <c>ShowGridLinesProperty</c>, which is present). <c>Setter.Property</c> requires an
/// <see cref="AvaloniaProperty"/>, and <see cref="Grid"/> is not a <see cref="TemplatedControl"/> so a
/// <c>ControlTheme</c> cannot reach it either. A shared approach would need a new custom control type — a far
/// larger blast radius than the defect.</para>
///
/// <para><b>Scope note.</b> The same row string also appears on 14 REPORT/drill pages, where row 1 is a
/// one-line <c>{Binding Subtitle}</c> and row 3 holds a scrollable body. Those are structurally correct and are
/// deliberately excluded — applying the Ledger fix there would inflate a 22px subtitle to ~318px and cut the
/// report body roughly in half on 14 screens.</para>
///
/// <para>Headless-safe throughout: layout bounds only. No Skia, no <c>CaptureRenderedFrame</c>, no
/// <c>TextLayout</c>/<c>TextRuns</c> — green on the 3-OS CI runners, which have no SkiaSharp.</para>
/// </summary>
public sealed class MasterPageRowStructureTests
{
    /// <summary>
    /// Every master page whose root is the create-form-over-list shape. 22 screens: the 21 that match
    /// <c>RowDefinitions="Auto,Auto,Auto,*"</c> plus <b>PriceLists</b>, which carries the identical pathology
    /// under the 5-row variant <c>"Auto,Auto,Auto,*,Auto"</c> and is INVISIBLE to that grep — the exact
    /// silent-skip this list exists to prevent. Ledger and Scenario are included too, to regression-lock the
    /// two fixes already merged.
    /// </summary>
    private static readonly Dictionary<string, Action<MainWindowViewModel>> Drivers = new()
    {
        ["Ledger"] = v => v.ShowLedgerMaster(),
        ["Scenario"] = v => v.ShowScenarioMaster(),
        ["CostCategory"] = v => v.ShowCostCategoryMaster(),
        ["CostCentre"] = v => v.ShowCostCentreMaster(),
        ["Budget"] = v => v.ShowBudgetMaster(),
        ["Currency"] = v => v.ShowCurrencyMaster(),
        ["AccountGroup"] = v => v.ShowAccountGroupMaster(),
        ["StockGroup"] = v => v.ShowStockGroupMaster(),
        ["StockCategory"] = v => v.ShowStockCategoryMaster(),
        ["Unit"] = v => v.ShowUnitMaster(),
        ["Godown"] = v => v.ShowGodownMaster(),
        ["StockItem"] = v => v.ShowStockItemMaster(),
        ["Batch"] = v => v.ShowBatchMaster(),
        ["Bom"] = v => v.ShowBomMaster(),
        ["PriceLevels"] = v => v.ShowPriceLevelsMaster(),
        ["PriceLists"] = v => v.ShowPriceListsMaster(),
        ["ReorderLevels"] = v => v.ShowReorderLevelsMaster(),
        ["EmployeeCategory"] = v => v.ShowEmployeeCategoryMaster(),
        ["EmployeeGroup"] = v => v.ShowEmployeeGroupMaster(),
        ["Employee"] = v => v.ShowEmployeeMaster(),
        ["PayrollUnit"] = v => v.ShowPayrollUnitMaster(),
        ["AttendanceType"] = v => v.ShowAttendanceTypeMaster(),
        ["NatureOfPayment"] = v => v.ShowNatureOfPaymentMaster(),
        ["NatureOfGoods"] = v => v.ShowNatureOfGoodsMaster(),
    };

    /// <summary>
    /// The master pages whose row shape has actually been CONVERTED from <c>"Auto,Auto,Auto,*"</c> to
    /// <c>"Auto,3*,Auto,2*"</c> because their form genuinely outgrew its budget.
    ///
    /// <para><b>Why an explicit roster and not "no master page may use an Auto form row".</b> A blanket rule
    /// would be WRONG. The other 21 screens keep <c>Auto</c> deliberately: their forms are short, so converting
    /// them buys nothing and costs real estate — CostCategory's 200px form would inflate to ~315px and its list
    /// would shrink from ~325px to ~210px. That trade was considered and rejected as a usability regression.
    /// So the invariant is scoped to the screens where the conversion is the fix, and it is a roster rather
    /// than a grep so that converting a 22nd screen is a deliberate, reviewed edit to this list.</para>
    /// </summary>
    private static readonly string[] ConvertedScreens = { "Ledger", "Scenario", "Employee" };

    public static IEnumerable<object[]> Converted() =>
        from screen in ConvertedScreens select new object[] { screen };

    /// <summary>The four display heights the shipped app is expected to be usable at.</summary>
    private static readonly (int W, int H)[] Sizes =
    {
        (1920, 1080), (1600, 900), (1366, 768), (1280, 720),
    };

    public static IEnumerable<object[]> ScreensAndSizes() =>
        from screen in Drivers.Keys
        from size in Sizes
        select new object[] { screen, size.W, size.H };

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
    /// Opens one master page through the REAL navigation path at an EXPLICIT window size, on a company with
    /// EVERY feature flag these screens are gated on turned ON. Without the flags,
    /// <c>ShowBatchMaster</c>/<c>ShowBomMaster</c>/<c>ShowEmployeeMaster</c>/… are silent no-ops and the whole
    /// test would pass while asserting nothing — the failure mode this fixture exists to avoid.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenMaster(
        string screen, int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexMasterRows_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();

        vm.NewCompanyName = "Master Rows Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.Gst = new GstConfig
        {
            Enabled = true,
            HomeStateCode = "19",
            RegistrationType = GstRegistrationType.Regular,
        };
        c.Tds = new TdsConfig { Enabled = true };
        c.Tcs = new TcsConfig { Enabled = true };
        c.MaintainBatchwiseDetails = true;
        c.SetComponentsBom = true;
        c.DefineBomComponentType = true;
        c.UseSeparateActualBilledQuantity = true;
        c.EnableMultiplePriceLevels = true;
        c.EnableJobOrderProcessing = true;
        c.PayrollEnabled = true;
        c.PayrollStatutoryEnabled = true;
        c.SalaryTdsEnabled = true;

        vm.ShowCreateMenu();
        Drivers[screen](vm);
        Pump(window);
        return (window, vm, tempDir);
    }

    /// <summary>
    /// The page root: the outermost create-form-over-list Grid actually realised for this screen. Anchored by
    /// taking the DEEPEST such Grid so a shell/chrome Grid can never be mistaken for the page, and asserted to
    /// have really opened — a driver that silently no-ops must FAIL here, never quietly measure nothing.
    /// </summary>
    private static Grid PageRoot(MainWindow window, string screen, int width, int height)
    {
        int Depth(Visual v)
        {
            var d = 0;
            for (Visual? p = v.GetVisualParent(); p != null; p = p.GetVisualParent()) d++;
            return d;
        }

        var grid = Descendants(window).OfType<Grid>()
            .Where(g => g.RowDefinitions.Count is 4 or 5 && g.IsVisible && g.Bounds.Height > 50)
            .OrderByDescending(Depth)
            .FirstOrDefault();

        Assert.True(
            grid != null,
            $"{screen} master did not open at {width}x{height} — no 4/5-row page-root Grid was realised. " +
            "Either the navigation driver no-opped (a missing company feature flag in the fixture) or the " +
            "page root's row shape changed; either way this test would otherwise assert nothing.");

        return grid!;
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A — the page root's rows must FIT the pane the Miller shell gave it. This is the purest statement of the
    /// defect: pre-fix, Ledger's rows summed 1161 against a 942px pane and the excess was simply unreachable.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ScreensAndSizes))]
    public void Master_page_rows_fit_the_pane(string screen, int width, int height)
    {
        var (window, _, tempDir) = OpenMaster(screen, width, height);
        try
        {
            var grid = PageRoot(window, screen, width, height);
            var rowSum = grid.RowDefinitions.Sum(r => r.ActualHeight);
            Assert.True(
                rowSum <= grid.Bounds.Height + 0.5,
                $"{screen} master page rows overflow the pane at {width}x{height}: rows " +
                $"[{string.Join("|", grid.RowDefinitions.Select(r => r.ActualHeight.ToString("F0")))}] " +
                $"sum to {rowSum:F0} inside {grid.Bounds.Height:F0} — the excess is unreachable because " +
                "an Auto form row never overflows, so nothing can scroll.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// B — the list must keep a real share of the pane. The Auto form row starved Ledger's and Scenario's list
    /// to EXACTLY 0px at laptop heights, so the operator saw no list at all. Row 3 is the list on both the
    /// 4-row and the 5-row (PriceLists) variant.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ScreensAndSizes))]
    public void Master_page_list_row_is_not_starved(string screen, int width, int height)
    {
        var (window, _, tempDir) = OpenMaster(screen, width, height);
        try
        {
            var grid = PageRoot(window, screen, width, height);
            Assert.True(
                grid.RowDefinitions[3].ActualHeight > 0,
                $"{screen} master's list row was starved to " +
                $"{grid.RowDefinitions[3].ActualHeight:F0}px at {width}x{height} by the form row above it.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// C — REACHABILITY, not visibility. If the row-1 form's natural height exceeds the space the page has for
    /// form + list, then a vertically-enabled <see cref="ScrollViewer"/> MUST wrap it, MUST report
    /// <c>Extent &gt; Viewport</c>, and scrolling to the maximum offset MUST bring the form's bottom-most
    /// control inside the Miller pane's clip rectangle.
    ///
    /// <para>Deliberately never consults <see cref="Visual.IsEffectivelyVisible"/>, which stays <c>true</c> for
    /// a control sitting far below the clip edge because the clipper is an ANCESTOR — a visibility assertion
    /// would prove nothing about reachability. Equally deliberately compares against the
    /// <c>CascadeScroller</c>'s presenter bottom rather than the window height: the pane is what clips.</para>
    ///
    /// <para>A ScrollViewer ALONE does not satisfy this. Inside an <c>Auto</c> row it is measured at infinite
    /// height, sizes to its content, and reports <c>Extent == Viewport</c> with a maximum offset of zero — it
    /// exists and still never scrolls. The <c>Extent &gt; Viewport</c> clause is what makes that distinction
    /// bite, and it is why reverting the row shape while LEAVING the ScrollViewer in place still goes RED.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ScreensAndSizes))]
    public void Master_page_form_taller_than_its_budget_must_be_scrollable(string screen, int width, int height)
    {
        var (window, _, tempDir) = OpenMaster(screen, width, height);
        try
        {
            var grid = PageRoot(window, screen, width, height);

            var formChild = grid.Children.OfType<Control>()
                .FirstOrDefault(ch => Grid.GetRow(ch) == 1 && ch.IsVisible);
            Assert.True(formChild != null, $"{screen} master has no visible row-1 form at {width}x{height}.");

            // The form's own scroller, if any: the outermost vertically-enabled ScrollViewer at/below row 1.
            // TemplatedParent == null keeps this to scrollers the PAGE author wrote. Without it the search
            // finds the first one in tree order, which on the unconverted screens is the 19px scroller inside
            // a ComboBox's control template — not the form's wrapper, and enough to make a genuine geometric
            // failure read as "Extent 19 <= Viewport 19" and send the reader to the wrong control.
            var scroller = (formChild as ScrollViewer)
                ?? Descendants(formChild!).OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                                         && s.TemplatedParent == null);

            // Natural (unconstrained) form height. DesiredSize is the natural desire on an Auto row; where a
            // scroller clamps the measure (an explicit MaxHeight), its Extent carries the true content height.
            var naturalForm = Math.Max(
                formChild!.DesiredSize.Height,
                scroller?.Extent.Height ?? 0);

            // Space the page has for form + list = pane minus the fixed title/header (and PriceLists' footer).
            var budget = grid.Bounds.Height
                - grid.RowDefinitions[0].ActualHeight
                - grid.RowDefinitions[2].ActualHeight
                - (grid.RowDefinitions.Count == 5 ? grid.RowDefinitions[4].ActualHeight : 0);

            if (naturalForm <= budget + 0.5)
                return; // Form fits; nothing can strand. (Most screens, today.)

            Assert.True(
                scroller != null,
                $"{screen} master's form needs {naturalForm:F0}px but the page has only {budget:F0}px for " +
                $"form+list at {width}x{height}, and NO vertically-scrollable ScrollViewer wraps it — its " +
                "lower fields cannot be reached at any window size.");

            Assert.True(
                scroller!.Extent.Height > scroller.Viewport.Height,
                $"{screen} master's form scroller reports Extent {scroller.Extent.Height:F0} <= Viewport " +
                $"{scroller.Viewport.Height:F0} at {width}x{height}, so it will never scroll, even though the " +
                $"form needs {naturalForm:F0}px inside a {budget:F0}px budget. A ScrollViewer in an Auto row " +
                "is measured at infinite height and sizes to its content — the row shape must constrain it.");

            // The bottom-most real control in the form — the last thing an operator must be able to reach.
            var clipBottom = ClipBottom(window);
            var last = Descendants(formChild!).OfType<Control>()
                .Where(ctl => ctl.IsVisible && ctl.Bounds.Height > 0
                              && ctl is not Panel && ctl is not Border
                              && ctl.TranslatePoint(default, window) != null)
                .OrderByDescending(ctl => ctl.TranslatePoint(default, window)!.Value.Y)
                .FirstOrDefault();
            Assert.True(last != null, $"{screen} master's form realised no measurable controls.");

            scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
            Pump(window);

            var bottom = last!.TranslatePoint(default, window)!.Value.Y + last.Bounds.Height;
            Assert.True(
                bottom <= clipBottom + 0.5,
                $"{screen} master: after scrolling to the bottom, the form's last control still ends at " +
                $"y={bottom:F0}, below the pane clip at y={clipBottom:F0} ({width}x{height}) — unreachable.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>
    /// D — the STRUCTURAL lock on the conversion itself.
    ///
    /// <para><b>Why C is not enough.</b> Assertions A/B/C are GEOMETRIC: they only bite on a screen whose form
    /// actually overflows at one of the four test sizes. Ledger and Scenario overflow, so reverting them goes
    /// RED. <b>Employee does not.</b> Its form measures 418px against a ~525px budget at 1280x720, so C's
    /// <c>if (naturalForm &lt;= budget) return;</c> early-exits and A/B pass happily on the unfixed rows
    /// <c>36|418|22|107</c>. Reverting Employee's row shape to <c>"Auto,Auto,Auto,*"</c> — while leaving its
    /// ScrollViewer in place, so the diff looks like a tidy-up — left all 289 cases GREEN. The fix was
    /// unlocked: a future edit could undo it and CI would say nothing.</para>
    ///
    /// <para><b>What this locks instead.</b> Not geometry but SHAPE, which is size-independent and therefore
    /// bites whether or not today's form happens to overflow. On a converted page the form row must be a STAR
    /// track — that is the half of the fix that makes the Grid constrain the form, so the ScrollViewer below it
    /// sees real overflow instead of being measured at infinite height. And the list row must keep a
    /// proportional share, so no form growth can ever starve it to 0px again. Together they are exactly the
    /// two-part fix; either half alone is the bug in a different place.</para>
    ///
    /// <para>Size-independent by construction, so it runs at one representative (tightest) size rather than
    /// four: <c>RowDefinitions</c> is authored in the template and does not vary with the window.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Converted))]
    public void Converted_master_page_keeps_a_star_form_row_and_a_proportional_list_row(string screen)
    {
        var (window, _, tempDir) = OpenMaster(screen, 1280, 720);
        try
        {
            var grid = PageRoot(window, screen, 1280, 720);
            var shape = string.Join(",", grid.RowDefinitions.Select(r => r.Height.ToString()));

            Assert.True(
                grid.RowDefinitions[1].Height.IsStar,
                $"{screen} master's FORM row (row 1) is '{grid.RowDefinitions[1].Height}', not a star track — " +
                $"the page root's row shape is now \"{shape}\". {screen} is on the converted roster because its " +
                "form outgrew the page; an Auto row takes the form's FULL desired height and never constrains " +
                "it, so the ScrollViewer wrapping the form is measured at infinite height, sizes to its content " +
                "and reports Extent == Viewport — present, and permanently unable to scroll. Restore the star " +
                "track, or remove this screen from ConvertedScreens and justify it.");

            Assert.True(
                grid.RowDefinitions[3].Height.IsStar,
                $"{screen} master's LIST row (row 3) is '{grid.RowDefinitions[3].Height}', not a star track — " +
                $"the page root's row shape is now \"{shape}\". Without a proportional share the list is what " +
                "an oversized form starves first; it measured EXACTLY 0px on Ledger and Scenario before the fix.");
        }
        finally { Cleanup(window, tempDir); }
    }

    /// <summary>The Miller pane's clip boundary — the cascade content presenter's bottom edge.</summary>
    private static double ClipBottom(MainWindow window)
    {
        var cascade = Descendants(window).OfType<ScrollViewer>()
            .FirstOrDefault(s => s.Name == "CascadeScroller");
        Assert.True(cascade != null, "CascadeScroller not found.");
        var presenter = Descendants(cascade!).OfType<ScrollContentPresenter>().First();
        return presenter.TranslatePoint(default, window)!.Value.Y + presenter.Bounds.Height;
    }

    /// <summary>
    /// The roster guard. If a new master page is added with this row shape and NOT registered above, it is
    /// silently unguarded — the same class of blind spot that let PriceLists' 5-row variant hide from the
    /// 4-row grep. This pins the count so adding a screen is a deliberate, visible act.
    /// </summary>
    [AvaloniaFact]
    public void Every_known_master_page_is_covered_by_this_suite()
    {
        Assert.Equal(24, Drivers.Count);
        Assert.Contains("PriceLists", Drivers.Keys);   // the 5-row variant the grep misses
        Assert.Contains("Ledger", Drivers.Keys);       // already-fixed, regression-locked here
        Assert.Contains("Scenario", Drivers.Keys);     // already-fixed, regression-locked here
        Assert.Contains("Employee", Drivers.Keys);     // already-fixed, regression-locked here

        // Pin the converted set too: adding or dropping a conversion must be a deliberate edit, and every
        // converted screen must be drivable by this fixture or assertion D would silently measure nothing.
        Assert.Equal(3, ConvertedScreens.Length);
        Assert.All(ConvertedScreens, s => Assert.Contains(s, Drivers.Keys));
    }
}
