using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Apex.Desktop.Converters;

namespace Apex.Desktop.Tests;

/// <summary>
/// DURABLE REGRESSION LOCK for C4 (UI-D2) — the viewport-aware Miller-column page width.
///
/// WHAT C4 FIXED. The whole cascade shell used <see cref="ColumnWidthConverter"/>, whose Convert is a
/// literal <c>value is true ? 300.0 : 640.0</c>: EVERY page column was hardcoded 640px regardless of the
/// window size. On a wide window a ~640px page column sat beside a huge band of DEAD cream — the user
/// summarised the whole defect family as "only half the screen is appearing", and it hit at the app's own
/// default size too (a 3-column cascade needs ~1240px, so the app h-scrolled at its 1100px default). C4
/// replaced the fixed width with <see cref="CascadeColumnWidthConverter"/>: a MENU column content-sizes
/// within a clamp, and a PAGE column FILLS the leftover viewport (<c>viewport − ownX − 6</c>) while never
/// shrinking below a 640 floor (so the h-scroll engages instead of squeezing a report).
///
/// WHY THIS SHAPE OF LOCK — AND WHY IT IS HEADLESS-SAFE. C4's touch is global (window size + every column),
/// so the lock is two independent layers, BOTH provable without a rendering backend (no Skia, no
/// CaptureRenderedFrame, no TextLayout/TextRuns — so it is green on the plain-headless 3-OS CI whose runners
/// have no SkiaSharp native libs):
///   (1) CONVERTER MATH — invoke the converter directly and assert the page width is a viewport-relative
///       function that GROWS with the viewport (not the pre-C4 constant) and FLOORS at 640; that a menu
///       column resolves to Auto within the [260,380] clamp; and (F1) that a NON-terminal page column
///       (IsLast=false) holds the bounded 640 floor while the terminal one still fills the viewport, so a
///       report and its drill fit side-by-side. This is the arithmetic C4/F1 rest on, exercised in
///       isolation the way the runtime exercises it during arrange.
///   (2) XAML WIRING — parse MainWindow.axaml and assert the cascade column Border actually binds its Width
///       through that converter's <c>&lt;Border.Width&gt;</c> MultiBinding (fed the viewport + own-offset +
///       IsLast), carries NO hard-constant Width, still WIRES both the MinWidth/MaxWidth clamp converters
///       (F3 — the load-bearing 380 menu MaxWidth), and that NOTHING in the file still binds Width to the
///       legacy fixed ColWidth converter. This is what proves the math is actually WIRED to the live column.
///
/// A revert of C4/F1 reds a layer no matter which half is reverted: the converter back to a constant (or
/// dropping the IsLast position-awareness) reds (1); the Border back to a fixed <c>Width="…"</c>, the legacy
/// ColWidth binding, a missing IsLast binding, or deleting the Min/MaxWidth clamp attributes reds (2).
///
/// (A runtime "the page column grows as the window widens" assertion was deliberately NOT added: under the
/// committed plain-headless platform a top-level window's cascade ScrollViewer never establishes a non-zero
/// viewport — the page column always falls back to the 640 floor, exactly as the D1 AccountingRowLayoutTests
/// observe — so such a test can only pass under Skia and would RED the CI. Layer (1) captures the same
/// property, headless-safe, by exercising the converter's viewport→width function directly.)
/// </summary>
public sealed class CascadeColumnWidthTests
{
    // =============================================================================================
    // LAYER 1 — the converter math is viewport-relative, floors at 640, and content-sizes menus.
    // =============================================================================================

    private static double PageWidth(double viewport, double ownX) => (double)CascadeColumnWidthConverter.Instance
        .Convert(new object?[] { false, viewport, ownX }, typeof(double), null, CultureInfo.InvariantCulture);

    private static double PageWidth(double viewport, double ownX, bool isLast) => (double)CascadeColumnWidthConverter.Instance
        .Convert(new object?[] { false, viewport, ownX, isLast }, typeof(double), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Cascade_page_width_tracks_the_viewport_and_is_not_a_constant()
    {
        // The exact C4 formula: page = max(PageFloor, viewport − ownX − RightMargin). ownX (the cumulative
        // width of the menu columns to the left) is identical across widths, so a wider viewport MUST yield a
        // wider page column. These are the live production numbers (measured off the real render): a 2-col
        // cascade has ownX = 266, so viewport 1260 (a 1440 window) -> 988, viewport 1740 (1920) -> 1468.
        Assert.Equal(988.0, PageWidth(1260, 266));
        Assert.Equal(1468.0, PageWidth(1740, 266));

        // THE HEART OF THE LOCK: the page column is NOT the constant 640 the pre-C4 converter returned — it
        // is strictly larger on any non-trivial viewport, and it GROWS with the viewport. If C4 is reverted to
        // a constant, both of these collapse.
        Assert.True(PageWidth(1740, 266) > PageWidth(1260, 266) + 400,
            "The page column must widen with the viewport (C4). It is not tracking the window — the fixed-width "
            + "converter has been restored.");
        Assert.True(PageWidth(1260, 266) > CascadeColumnWidthConverter.PageFloor + 100,
            "At a 1440 window the page column must exceed the 640 floor — it is pinned to the floor, so the "
            + "viewport-relative width is gone.");
    }

    [Fact]
    public void Cascade_non_terminal_page_column_keeps_the_bounded_floor_so_a_drill_fits_beside_it()
    {
        // F1 (position-aware width). A page column that is NOT the rightmost (IsLast=false) — e.g. a Trial
        // Balance report with a ledger-vouchers drill column to its RIGHT — must NOT greedily fill the
        // viewport; it holds the 640 floor so the report and its drill can be shown side-by-side. If it
        // filled the viewport (the pre-F1 bug), report_width + drill_width always exceeded the viewport and
        // the drill could never sit beside the report.
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(1740, 266, isLast: false));
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(1260, 266, isLast: false));
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(1360, 906, isLast: false));

        // The TERMINAL page column (IsLast=true) still fills the leftover viewport — the dead-cream win is
        // NOT regressed: a single terminal page column absorbs the whole viewport to its right.
        Assert.Equal(1468.0, PageWidth(1740, 266, isLast: true));
        Assert.True(PageWidth(1740, 266, isLast: true) > CascadeColumnWidthConverter.PageFloor + 100,
            "The rightmost page column must still fill the viewport (no dead cream) — the F1 fix must not "
            + "regress the terminal fill-to-viewport behaviour.");

        // Concrete side-by-side proof at a wide (1920) window: report (non-terminal, floored 640) + drill
        // (terminal, fills the rest) fit within the viewport, so BOTH are visible without h-scroll.
        var report = PageWidth(1740, 266, isLast: false);          // 640
        var drillOwnX = 266 + report;                               // menus + the floored report to its left
        var drill = PageWidth(1740, drillOwnX, isLast: true);       // fills the remainder
        Assert.True(266 + report + drill <= 1740 + 0.001,
            $"report({report}) + drill({drill}) + menus(266) = {266 + report + drill} must fit the 1740 "
            + "viewport so they sit side-by-side.");
    }

    [Fact]
    public void Cascade_page_width_floors_at_640_when_the_viewport_is_narrow_or_unmeasured()
    {
        // Below the floor the h-ScrollViewer must scroll rather than squeeze the report: never < PageFloor.
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(700, 300)); // 394 -> floored to 640
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(0, 0));     // pre-arrange -> floor
        Assert.Equal(CascadeColumnWidthConverter.PageFloor, PageWidth(double.NaN, double.NaN));
        Assert.Equal(640.0, CascadeColumnWidthConverter.PageFloor); // the number every static budget rests on
    }

    [Fact]
    public void Cascade_menu_column_is_content_sized_within_a_clamp()
    {
        // A MENU column returns NaN (=> Width=Auto, content-sized), NOT a fixed width.
        var menu = CascadeColumnWidthConverter.Instance
            .Convert(new object?[] { true, 1740.0, 0.0 }, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.True(menu is double d && double.IsNaN(d),
            "A menu column must resolve to NaN (Width=Auto) so it content-sizes; it is being given a fixed width.");

        // The clamp that makes content-sizing safe: menu [260,380], page [640,+inf].
        Assert.Equal(260.0, ColumnMinWidthConverter.Instance.Convert(true, typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(380.0, ColumnMaxWidthConverter.Instance.Convert(true, typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(CascadeColumnWidthConverter.PageFloor,
            ColumnMinWidthConverter.Instance.Convert(false, typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(double.PositiveInfinity,
            ColumnMaxWidthConverter.Instance.Convert(false, typeof(double), null, CultureInfo.InvariantCulture));
    }

    // =============================================================================================
    // LAYER 2 — the XAML actually binds the column width through the viewport-aware converter.
    // =============================================================================================

    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    [Fact]
    public void Cascade_column_border_binds_Width_through_the_viewport_aware_converter_not_a_constant()
    {
        var doc = XDocument.Load(AxamlPath());
        var elements = doc.Root!.DescendantsAndSelf().ToList();

        // The cascade column Border is the one whose Width is set by a <Border.Width> MultiBinding that uses
        // CascadeColumnWidthConverter. Exactly one such site should exist (the Miller-column template).
        var pageBorders = elements.Where(e =>
                e.Name == Av + "Border"
                && e.Element(Av + "Border.Width") is { } bw
                && bw.Descendants().Any(m => m.Name == Av + "MultiBinding"
                    && (m.Attribute("Converter")?.Value ?? "").Contains(nameof(CascadeColumnWidthConverter),
                        StringComparison.Ordinal)))
            .ToList();

        Assert.True(pageBorders.Count == 1,
            $"Expected exactly ONE cascade column Border whose <Border.Width> MultiBinding uses "
            + $"{nameof(CascadeColumnWidthConverter)}; found {pageBorders.Count}. The viewport-aware column "
            + "width wiring (C4) has been removed or duplicated.");

        var border = pageBorders[0];

        // It must NOT also carry a hard-constant Width attribute — that would override the MultiBinding and
        // re-pin the column, which is exactly the pre-C4 defect (MainWindow.axaml:202 before C4).
        Assert.True(border.Attribute("Width") is null,
            "The cascade column Border carries a constant Width= attribute alongside its <Border.Width> "
            + "MultiBinding — the constant wins and re-pins the column (pre-C4). Remove the fixed Width.");

        // And the MultiBinding must feed the converter the viewport + own-offset + position it needs to be
        // viewport-aware AND position-aware: four child bindings (IsMenu, the ScrollViewer viewport width,
        // this column's own X offset, and IsLast — F1, so a non-terminal report keeps a bounded width).
        var multi = border.Element(Av + "Border.Width")!.Descendants()
            .First(m => m.Name == Av + "MultiBinding");
        var childBindings = multi.Elements(Av + "Binding").ToList();
        Assert.True(childBindings.Count == 4,
            $"The C4/F1 width MultiBinding must pass 4 values (IsMenu, viewport width, own X offset, IsLast) "
            + $"— found {childBindings.Count}. Without IsLast a non-terminal report greedily fills the "
            + "viewport and its drill column can never sit beside it (the F1 regression).");
        var paths = childBindings.Select(b => b.Attribute("Path")?.Value ?? "").ToList();
        Assert.Contains(paths, p => p.Contains("Bounds.Width", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.Contains("Bounds.X", StringComparison.Ordinal));
        Assert.Contains(paths, p => p == "IsLast");

        // F3: the column Border must WIRE both clamp converters — MinWidth through ColumnMinWidthConverter
        // (keeps a menu column ≥ 260 / a page column ≥ 640) and MaxWidth through ColumnMaxWidthConverter
        // (the LOAD-BEARING 380 menu clamp — without it a long ledger name measured at infinite width in the
        // horizontal StackPanel would balloon its menu column and shove the page column off-screen). The
        // Border.Width MultiBinding alone does not protect these: deleting the two attributes leaves the
        // width lock green while silently dropping the clamps, so assert them here directly.
        var minWidth = border.Attribute("MinWidth")?.Value ?? "";
        var maxWidth = border.Attribute("MaxWidth")?.Value ?? "";
        Assert.Contains(nameof(ColumnMinWidthConverter), minWidth, StringComparison.Ordinal);
        Assert.Contains(nameof(ColumnMaxWidthConverter), maxWidth, StringComparison.Ordinal);

        // The legacy fixed binding must be GONE from every live width site: no attribute may bind Width to the
        // old fixed ColWidth converter. (The converter class itself is retained as the static-test floor source
        // and may still be declared as a resource, but it must not be BOUND to a Width anywhere.)
        var legacyBoundWidth = elements
            .Where(e => (e.Attribute("Width")?.Value ?? "").Contains("ColWidth", StringComparison.Ordinal))
            .ToList();
        Assert.True(legacyBoundWidth.Count == 0,
            $"{legacyBoundWidth.Count} element(s) still bind Width to the legacy fixed ColWidth converter — the "
            + "pre-C4 hardcoded 300/640 column width has been restored.");
    }
}
