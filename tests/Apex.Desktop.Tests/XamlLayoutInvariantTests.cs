using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Apex.Desktop.Converters;

namespace Apex.Desktop.Tests;

/// <summary>
/// STATIC LAYOUT INVARIANTS over <c>src/Apex.Desktop/Views/MainWindow.axaml</c>.
///
/// WHY THIS FILE EXISTS. The whole UI is ONE 14,957-line MainWindow.axaml with ~800 inline
/// ColumnDefinitions, no shared layout contract, and copy-pasted header/row "twin" templates that
/// silently drift apart. That architecture is why the 2026-07-16 sweep found 328 defects across 140
/// screens. Renders are the only thing that CATCHES that defect class — but renders are slow,
/// out-of-tree, and depend on discipline that has already lapsed once (the sweep harness was deleted,
/// so nothing was watching). These tests are the mechanical watcher: they parse the .axaml as plain
/// XML — no rendering, no Avalonia app, no Skia, no headless platform — so they are fast,
/// deterministic, and cannot be defeated by a harness being deleted or by TestAppBuilder drifting.
///
/// THEY DO NOT REPLACE RENDERS. Rendering catches things arithmetic cannot (overlap, off-pane
/// controls, real font metrics). These catch the three MECHANISMS that produce most of the defects,
/// at zero cost, on every single test run.
///
/// HOW TO READ A FAILURE. Each test fails with the exact line number in MainWindow.axaml plus the
/// offending values. Fix the layout — do NOT add to an allow-list. The allow-lists below are frozen
/// inventories of KNOWN-UNFIXED sites that the UI-defect campaign will burn down; they may only ever
/// SHRINK. An allow-list entry that no longer violates is itself a failure (see the stale-entry
/// assertions), so the lists cannot rot.
///
/// THE RATCHET. Everything not on an allow-list is locked green today and cannot silently regrow.
/// The allow-list sizes are the campaign's progress metric.
/// </summary>
public sealed class XamlLayoutInvariantTests
{
    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Resolves MainWindow.axaml from THIS source file's location, so the test needs no
    /// build-time copy step and no dependency on the working directory.</summary>
    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        // tests/Apex.Desktop.Tests/XamlLayoutInvariantTests.cs -> repo root
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    private static readonly Lazy<XDocument> Doc = new(() =>
    {
        var path = AxamlPath();
        Assert.True(File.Exists(path), $"MainWindow.axaml not found at '{path}'.");
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    });

    private static int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;

    private static IEnumerable<XElement> Elements() => Doc.Value.Root!.DescendantsAndSelf();

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    private static IEnumerable<XElement> Ancestors(XElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent) yield return p;
    }

    private static bool Is(XElement e, string localName) => e.Name == Av + localName;

    private static bool IsHorizontalStackPanel(XElement e)
        => Is(e, "StackPanel") && Attr(e, "Orientation") == "Horizontal";

    private static bool HasClass(XElement e, string cls)
        => (Attr(e, "Classes") ?? "").Split(' ').Contains(cls);

    /// <summary>The 0-based index a child occupies (Grid.Column defaults to 0 when absent).</summary>
    private static int ColumnIndexOf(XElement child)
        => int.TryParse(Attr(child, "Grid.Column") ?? "0", out var c) ? c : 0;

    /// <summary>An explicit numeric Width/MaxWidth, or null when absent / bound / non-numeric.</summary>
    private static double? ExplicitWidth(XElement e)
    {
        foreach (var name in new[] { "Width", "MaxWidth" })
        {
            var raw = Attr(e, name);
            if (raw is null) continue;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        }
        return null;
    }

    private static bool HasAnyWidthConstraint(XElement e)
        => Attr(e, "Width") is not null || Attr(e, "MaxWidth") is not null;

    /// <summary>The DataTemplate x:DataType governing an element — a stable identity for allow-lists
    /// (unlike line numbers, which shift on every edit to this actively-churning file).</summary>
    private static string DataTypeOf(XElement e)
        => Ancestors(e).FirstOrDefault(a => Is(a, "DataTemplate")) is { } dt
            ? Attr(dt, X + "DataType") ?? "(untyped-template)"
            : "(no-template)";

    private static string? Attr(XElement e, XName name) => e.Attribute(name)?.Value;

    // ---------------------------------------------------------------------------------------------
    // The horizontal budget of a Grid
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The MINIMUM width the Miller-column PAGE column gives its content. Read from the production floor
    /// source (<see cref="ColumnWidthConverter"/>) rather than hardcoded. BEFORE C4 the page column was a
    /// fixed 640px and this was its exact content width; AFTER C4 the page column is viewport-aware
    /// (<see cref="CascadeColumnWidthConverter"/>) and fills the leftover viewport, but it NEVER shrinks
    /// below <see cref="CascadeColumnWidthConverter.PageFloor"/> (== 640, deliberately the same number),
    /// so this is now the page column's *floor* content width — the narrowest it is ever laid out at
    /// (when the shell floors it at MinWidth). A grid that does not fit this floor does not fit the page
    /// column at any window size, so the static budget checks correctly measure against it.
    /// </summary>
    private static double PageColumnContentWidth
    {
        get
        {
            var w = (double)ColumnWidthConverter.Instance
                .Convert(false /* IsMenu:false => page column */, typeof(double), null, CultureInfo.InvariantCulture);
            return w - 2.0; // the column Border has BorderThickness="1" (1px each side)
        }
    }

    /// <summary>
    /// The finite width available to <paramref name="grid"/>, or null when it is measured at INFINITE
    /// width (inside a horizontal StackPanel or an h-scrolling ScrollViewer) and therefore cannot
    /// starve a * column at all — a different defect class (wider-than-pane), not this one.
    ///
    /// This deliberately IGNORES intervening Margin/Padding, which only ever OVERSTATES the budget.
    /// The result is a one-sided bound: it can miss a real violation, but it cannot invent one.
    /// </summary>
    private static double? FiniteBudget(XElement grid)
    {
        foreach (var a in Ancestors(grid))
        {
            var raw = Attr(a, "Width");
            if (raw is not null)
            {
                // LEGACY page-column shape: Width="{Binding IsMenu, Converter={StaticResource ColWidth}}".
                if (raw.Contains("ColWidth", StringComparison.Ordinal)) return PageColumnContentWidth;
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? v
                    : null; // some other binding -> unknowable statically -> do not judge
            }
            // C4 page-column shape: Width is now a <Border.Width> MultiBinding using
            // CascadeColumnWidthConverter (viewport-aware). Statically we cannot know the live width — it
            // fills the viewport — but we CAN bound it below: the converter never returns less than
            // PageFloor (640), and the shell floors the page column at MinWidth. So the page column is
            // AT LEAST PageColumnContentWidth (638) wide, and a grid that does not fit 638 does not fit
            // the page column at its narrowest either. Judge it against that floor — identical number to
            // the legacy branch, so every budget/allow-list stays valid.
            if (IsPageColumnBorder(a)) return PageColumnContentWidth;
            if (IsHorizontalStackPanel(a)) return null;
            if (Is(a, "ScrollViewer")
                && Attr(a, "HorizontalScrollBarVisibility") is "Auto" or "Visible") return null;
        }
        return null;
    }

    /// <summary>The C4 cascade page/menu column Border: a Border whose <c>Width</c> is set by a
    /// <c>&lt;Border.Width&gt;</c> MultiBinding using <see cref="CascadeColumnWidthConverter"/>. (Menu
    /// columns share this Border but never host a *-column report Grid, so returning the page floor for
    /// both is harmless — menu-row grids carry an Auto column and are filtered out upstream.)</summary>
    private static bool IsPageColumnBorder(XElement e)
        => Is(e, "Border")
           && e.Element(Av + "Border.Width") is { } bw
           && bw.Descendants().Any(m => Is(m, "MultiBinding")
                && (Attr(m, "Converter") ?? "").Contains(
                       nameof(CascadeColumnWidthConverter), StringComparison.Ordinal));

    private enum ColKind { Fixed, Star, Auto, Other }

    private readonly record struct Col(ColKind Kind, double Value);

    private static IReadOnlyList<Col> ParseColumns(string spec) => spec
        .Split(',')
        .Select(t => t.Trim())
        .Select(t =>
        {
            if (t.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return new Col(ColKind.Auto, 0);
            if (t.EndsWith('*'))
            {
                var f = t[..^1];
                return new Col(ColKind.Star,
                    f.Length == 0 ? 1.0
                    : double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1.0);
            }
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? new Col(ColKind.Fixed, v)
                : new Col(ColKind.Other, 0);
        })
        .ToList();

    private readonly record struct GridSite(XElement Element, string Spec, IReadOnlyList<Col> Cols, double Budget)
    {
        public double FixedTotal => Cols.Where(c => c.Kind == ColKind.Fixed).Sum(c => c.Value);
        public double StarWeight => Cols.Where(c => c.Kind == ColKind.Star).Sum(c => c.Value);
        public double NarrowestStar =>
            (Budget - FixedTotal) * (Cols.Where(c => c.Kind == ColKind.Star).Min(c => c.Value) / StarWeight);
    }

    /// <summary>
    /// Every Grid with a * column, a finite budget, and NO Auto column (an Auto column's width depends
    /// on runtime content, so its budget is not statically knowable — skipped rather than guessed).
    /// </summary>
    private static List<GridSite> MeasurableGrids()
    {
        var sites = new List<GridSite>();
        foreach (var g in Elements().Where(e => Is(e, "Grid")))
        {
            var spec = Attr(g, "ColumnDefinitions");
            if (spec is null) continue;
            var cols = ParseColumns(spec);
            if (!cols.Any(c => c.Kind == ColKind.Star)) continue;
            if (cols.Any(c => c.Kind is ColKind.Auto or ColKind.Other)) continue;
            if (FiniteBudget(g) is not { } budget) continue;
            sites.Add(new GridSite(g, spec.Replace(" ", ""), cols, budget));
        }
        return sites;
    }

    private static void AssertNoStaleAllowListEntries(
        IEnumerable<string> allowList, IEnumerable<string> stillViolating, string testName)
    {
        var stale = allowList.Except(stillViolating).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"{testName}: {stale.Count} allow-list entr{(stale.Count == 1 ? "y is" : "ies are")} STALE — the site "
            + "was fixed (or removed) but the exemption was left behind, which would silently re-permit the "
            + "defect. DELETE these entries:\n  " + string.Join("\n  ", stale));
    }

    // =============================================================================================
    // INVARIANT 1 — TextTrimming / TextWrapping declared inside a horizontal StackPanel is DEAD.
    // =============================================================================================
    //
    // THE MECHANISM. A horizontal StackPanel measures every child at INFINITE width. TextTrimming and
    // TextWrapping only ever engage against a FINITE constraint. So a TextBlock that declares either
    // one inside a horizontal StackPanel — with no explicit Width on itself or on an ancestor below
    // the panel — is DEAD CONFIGURATION: the author asked for an ellipsis, and it can never appear.
    // The text instead overflows and PAINTS OVER whatever sits to its right. This is the exact
    // mechanism behind the worst defect in the sweep: Trial Balance drew the group tag over the
    // amount ("Indirect Expen1,00,000.00"), making the leading digit illegible — Rs 1,00,000 reads as
    // Rs 4,00,000, a financial misread. (AccountingRowLayoutTests locks the fixed row templates at
    // RUNTIME; this locks the whole file at parse time, for free.)
    //
    // WHY THIS FORMULATION IS PRECISE. It does not guess whether text "looks long". It reports a
    // provable contradiction between a declared intent and the measure contract. A TextBlock with an
    // explicit Width, or under an ancestor with an explicit Width BELOW the panel, is genuinely
    // constrained and is correctly NOT reported.

    private readonly record struct DeadTrim(XElement Text, XElement Panel, string Intent)
    {
        // Keyed by template DataType + bound text, NOT by line number: this file churns constantly and
        // a line-numbered allow-list would go stale on every unrelated edit above it.
        public string Key => $"{DataTypeOf(Text)}|{Attr(Text, "Text") ?? "(no Text attr)"}|{Intent}";
    }

    /// <summary>
    /// KNOWN-UNFIXED sites. These are REAL defects, deliberately not fixed here (this file is the
    /// regression lock, not the fix). Each entry names the cluster that will delete it.
    /// </summary>
    private static readonly HashSet<string> DeadTrimAllowList = new(StringComparer.Ordinal)
    {
        // Comparative report row: Label + GroupName sit in a horizontal StackPanel Width="220", so the
        // Label's CharacterEllipsis can never fire and a long ledger name paints over the GroupName tag
        // and then over the first amount column. Same shape as the Trial Balance bug already fixed in
        // AccountingRowLayoutTests. REMOVED BY: the comparative-report row-template cluster.
        "vm:ComparativeRowVM|{Binding Label}|TextTrimming=CharacterEllipsis",

        // GSTR-2B reconciliation row: the Note TextBlock asks to Wrap inside a horizontal StackPanel,
        // so it can never wrap; a long mismatch note runs off the row instead.
        // REMOVED BY: the GSTR-2B recon row-template cluster.
        "vm:Gstr2bReconRowVm|{Binding Note}|TextWrapping=Wrap",
    };

    private static List<DeadTrim> FindDeadTrims()
    {
        var found = new List<DeadTrim>();
        foreach (var t in Elements().Where(e => Is(e, "TextBlock")))
        {
            var intents = new List<string>();
            if (Attr(t, "TextTrimming") is { } tt && tt != "None") intents.Add("TextTrimming=" + tt);
            if (Attr(t, "TextWrapping") is { } tw && tw != "NoWrap") intents.Add("TextWrapping=" + tw);
            if (intents.Count == 0) continue;

            // Its own explicit width constrains it -> trimming/wrapping works even in a h-StackPanel.
            if (HasAnyWidthConstraint(t)) continue;

            foreach (var a in Ancestors(t))
            {
                if (IsHorizontalStackPanel(a))
                {
                    // NOTE: a Width on the h-StackPanel ITSELF does not rescue the child — the panel
                    // still measures children at infinite width. So this is a violation regardless.
                    foreach (var intent in intents) found.Add(new DeadTrim(t, a, intent));
                    break;
                }
                if (HasAnyWidthConstraint(a)) break; // a finite constraint above -> genuinely constrained
            }
        }
        return found;
    }

    [Fact]
    public void TextTrimming_and_TextWrapping_must_not_be_declared_inside_a_horizontal_StackPanel()
    {
        var all = FindDeadTrims();

        // NON-VACUITY: this file is full of TextBlocks that declare trimming; if the walk finds
        // nothing to consider, the parse or the traversal broke and the test below would pass by
        // doing nothing. Assert the search actually searched.
        var considered = Elements().Count(e => Is(e, "TextBlock")
            && (Attr(e, "TextTrimming") is { } v && v != "None" || Attr(e, "TextWrapping") is { } w && w != "NoWrap"));
        Assert.True(considered > 100,
            $"Only {considered} TextBlocks declaring TextTrimming/TextWrapping were found — the XAML walk is "
            + "broken, so this test would be vacuous.");

        var violations = all.Where(d => !DeadTrimAllowList.Contains(d.Key)).ToList();

        Assert.True(violations.Count == 0,
            $"{violations.Count} TextBlock(s) declare TextTrimming/TextWrapping inside a horizontal StackPanel, "
            + "which measures children at INFINITE width — so the trimming can NEVER engage and the text will "
            + "overflow and paint over its neighbour. Give the TextBlock a Grid column (or an explicit Width) "
            + "instead of a horizontal StackPanel slot:\n"
            + string.Join("\n", violations.Select(d =>
                $"  MainWindow.axaml({Line(d.Text)}): {d.Intent} on Text={Attr(d.Text, "Text")} "
                + $"-> horizontal StackPanel at line {Line(d.Panel)}")));

        AssertNoStaleAllowListEntries(DeadTrimAllowList, all.Select(d => d.Key), nameof(DeadTrimAllowList));
    }

    // =============================================================================================
    // INVARIANT 2 — fixed columns must FIT the page column at all.
    // =============================================================================================
    //
    // THE MECHANISM. When the fixed column widths alone meet or exceed the pane width, the * column is
    // squeezed to ZERO — its content vanishes entirely — AND the trailing fixed columns are pushed off
    // the right edge. Verified against baseline render 059-Report-JobWorkInOrderBook.png: the grid is
    // "95,120,150,*,110,95,95,95" = 760px of fixed columns in a 638px pane; the render shows the
    // "Item" header (the * column) COMPLETELY ABSENT between "Party" and "Track", and "Fulfilled" /
    // "Pending" clipped away at "Fulfil". Pure arithmetic predicted exactly what the render shows.
    //
    // This is the catastrophic tier: a whole column of data silently disappears. It is kept separate
    // from Invariant 3 (and their site sets are disjoint by construction) so that the worst sites can
    // never hide inside the larger, softer allow-list.

    /// <summary>
    /// KNOWN-UNFIXED sites, keyed by ColumnDefinitions string (edit-stable; header/row twins share a
    /// key by design). 4 strings / 12 sites. EVERY ONE loses a column outright.
    /// </summary>
    private static readonly HashSet<string> OverflowingGridAllowList = new(StringComparer.Ordinal)
    {
        // e-Invoice / e-Way status + amendment tables (6 sites). 670px of fixed in 638px.
        // REMOVED BY: the GST e-invoice/e-way + amendments screen cluster.
        "140,*,140,140,140,110",
        // NOTE (UI-defect D4 / C6): the three inventory-register grids that used to overflow this pane —
        // Order Register "100,150,*,*,120,100,100,110", the allocation/material registers
        // "95,60,150,*,120,90,100,120", and the Job Work Order Books "95,120,150,*,110,95,95,95" — were
        // fixed by wrapping each in a horizontal ScrollViewer and pinning a MinWidth so the "*" columns keep
        // >=150px and the grid scrolls instead of starving. Their allow-list entries were therefore deleted.
    };

    [Fact]
    public void Fixed_column_widths_must_fit_inside_the_page_column()
    {
        var sites = MeasurableGrids();

        // NON-VACUITY: guard the whole filter chain (budget resolution, column parsing). If any of it
        // silently stops matching, the assertion below becomes an empty loop.
        Assert.True(sites.Count > 300,
            $"Only {sites.Count} measurable Grids found — the budget/column analysis is broken, so this test "
            + "would be vacuous.");
        Assert.Equal(638.0, PageColumnContentWidth); // the constant this whole test rests on

        var overflowing = sites.Where(s => s.FixedTotal >= s.Budget).ToList();
        var violations = overflowing.Where(s => !OverflowingGridAllowList.Contains(s.Spec)).ToList();

        Assert.True(violations.Count == 0,
            $"{violations.Count} Grid(s) declare fixed columns that alone meet or exceed the available width. "
            + "The * column collapses to ZERO (its data disappears) and the trailing columns are clipped off "
            + "the pane. Narrow the fixed columns:\n"
            + string.Join("\n", violations.Select(s =>
                $"  MainWindow.axaml({Line(s.Element)}): \"{s.Spec}\" -> fixed={s.FixedTotal:F0}px "
                + $">= budget {s.Budget:F0}px")));

        AssertNoStaleAllowListEntries(OverflowingGridAllowList, overflowing.Select(s => s.Spec),
            nameof(OverflowingGridAllowList));
    }

    // =============================================================================================
    // INVARIANT 3 — a * column must stay wide enough to read.
    // =============================================================================================
    //
    // THE MECHANISM. The * column in these report grids is the name column (ledger / party / stock
    // item). Fixed sibling columns are sized for their SHORT hardcoded header labels, so the * column
    // absorbs every rounding error and starves — and real database names ellipsize down to nothing.
    // This is systemic root cause (1) from the sweep.
    //
    // THRESHOLD. 150px at the 12.5px monospace body font is ~18 characters — the minimum to tell two
    // party ledgers apart. It is a judgement call, documented here so it can be argued with; the
    // arithmetic around it is exact.

    private const double MinimumStarWidth = 150.0;

    /// <summary>
    /// KNOWN-UNFIXED sites, keyed by ColumnDefinitions string. 34 strings / 69 sites at the time of
    /// writing. This list is the campaign's progress metric: it may only SHRINK.
    /// REMOVED BY: the per-screen column-budget clusters (each string names its screens in the sweep
    /// catalogue). NOTE: sites whose fixed columns do not fit AT ALL belong to Invariant 2 and are
    /// excluded here, so the two lists never overlap.
    /// </summary>
    private static readonly HashSet<string> StarvedStarAllowList = new(StringComparer.Ordinal)
    {
        "*,92,110,100,92,92,140",      // Stock/Godown movement report twins
        "110,*,80,70,100,110,100,55",  // e-Way bill list twins
        "*,90,130,110,80,130,60",      // Payroll statutory report twins
        "*,150,150,90,90,110",         // Bill-wise outstandings twins
        "140,110,*,70,140,110",        // Electronic ledger twins
        "150,90,*,120,110,100",        // ITC reversal twins
        "*,110,150,80,80,150",         // ITC set-off twins
        "90,*,120,120,120,100",        // Manufacturing/BOM twins
        "90,*,80,80,140,70,90",        // QRMP / IFF twins
        "100,140,110,110,*,*",         // GSTR-9C reconciliation twins
        "150,120,*,*,*,*",             // Annual return twins
        "120,60,90,*,*,*,*",           // Annual return detail twins
        "110,*,*,110,110,110",         // Batch/expiry report twins
        "110,*,120,*,120,90",          // Cost centre report
        "110,*,110,*,110,*",           // Comparative columns
        "*,*,*,*,*,*",                 // 6-way even split (GST recon)
        "*,110,110,110,120",           // Consumption twins (560px pane)
        "118,*,96,92,124,96",          // TDS/TCS report twins
        "*,58,68,80,86,54,96",         // Form 24Q twins (560px pane)
        "120,*,70,80,*,*",             // IMS action twins
        "130,2*,130,*",                // Import 2B twins
        "130,*,*,*,*",                 // e-invoice generate twins
        "*,*,*,*,*",                   // 5-way even split
        "90,*,110,90,90,*",            // Price list
        "*,110,90,90,120,90",          // Reorder level twins
        "140,*,90,90,90,90",           // Set-off & pay twins
        "80,*,80,80,120,140",          // ITC gate twins
        "140,150,70,*,*",              // Job work twins
        "100,*,90,80,80,*",            // POS billing
        "*,64,82,44,50,80,96",         // Form 27D twins (560px pane)
        "100,*,46,88,88,92",           // Form 16A twins (560px pane)
        "*,150,70,150,120",            // Scenario twins
        "*,130,130,140,90",            // Interest calc twins
        "170,*,170,*",                 // Two-up compare
    };

    [Fact]
    public void Star_columns_must_stay_wide_enough_to_read()
    {
        var sites = MeasurableGrids();
        Assert.True(sites.Count > 300,
            $"Only {sites.Count} measurable Grids found — the analysis is broken, so this test would be vacuous.");

        // Disjoint from Invariant 2: only grids whose fixed columns actually fit are judged here.
        var starved = sites.Where(s => s.FixedTotal < s.Budget && s.NarrowestStar < MinimumStarWidth).ToList();
        var violations = starved.Where(s => !StarvedStarAllowList.Contains(s.Spec)).ToList();

        Assert.True(violations.Count == 0,
            $"{violations.Count} Grid(s) starve their * column below {MinimumStarWidth:F0}px — the name column "
            + "ellipsizes real database names down to nothing. Trim the fixed columns or drop one:\n"
            + string.Join("\n", violations.Select(s =>
                $"  MainWindow.axaml({Line(s.Element)}): \"{s.Spec}\" -> fixed={s.FixedTotal:F0}px of "
                + $"{s.Budget:F0}px leaves narrowest * = {s.NarrowestStar:F0}px")));

        AssertNoStaleAllowListEntries(StarvedStarAllowList, starved.Select(s => s.Spec),
            nameof(StarvedStarAllowList));
    }

    // =============================================================================================
    // INVARIANT 4 — a header Grid and its row-template twin must declare IDENTICAL columns.
    // =============================================================================================
    //
    // THE MECHANISM. Every report here is written twice: a header Grid (TextBlocks with Classes="colHdr"
    // over a navy Border) and, in the ItemsControl/ListBox ItemTemplate below it, a row Grid that
    // copy-pastes the same ColumnDefinitions string. Nothing links them. When someone widens one column
    // in one of the twins, the header silently stops sitting over its data — the "misaligned" (50) and
    // "clipped-column" (30) defect classes in the sweep.
    //
    // THIS ONE HAS NO ALLOW-LIST: all 112 pairs match TODAY. This test is a pure ratchet — the drift
    // mechanism is now mechanically impossible to reintroduce without going red.
    //
    // COVERAGE (59 -> 112). 112 header/row twins are paired: 59 where the items control is a DIRECT
    // sibling of its header band, PLUS 53 where the items control is WRAPPED IN A SCROLLVIEWER and the
    // header sits beside that ScrollViewer — the dominant report shape here:
    //     <Grid><Border>header</Border><ScrollViewer><ListBox/></ScrollViewer></Grid>.
    // Those 53 were SILENTLY SKIPPED before (the items control's only "sibling" is nothing; its parent is
    // the ScrollViewer), so the claim that drift was "mechanically impossible" was overstated: it did not
    // cover them. Among the 53 are the accounting grid ("*,150,150") and Ledger Vouchers ("*,130,130,140")
    // — the exact templates C1 fixed, which had NO header/row drift lock at all until this covered them.
    //
    // PAIRING RULE (deliberately conservative). A row template is paired with a header only when the
    // header is found in a PRECEDING SIBLING subtree of the items control — climbing first through any
    // enclosing ScrollViewer(s), since a ScrollViewer has a single content child and so cannot reach past
    // an unrelated block — AND no other items control sits between them (an intervening control already
    // owns that header). An earlier, looser rule paired an editable form's ItemsControl with an unrelated
    // report header two blocks up and invented 5 phantom mismatches; that is why the header must be
    // positively identified by its colHdr TextBlocks rather than guessed positionally. Unpairable row
    // grids are simply not judged.

    private static bool IsHeaderGrid(XElement e)
        => Is(e, "Grid")
           && Attr(e, "ColumnDefinitions") is not null
           && e.Elements().Any(k => Is(k, "TextBlock")
                                    && (Attr(k, "Classes") ?? "").Split(' ').Contains("colHdr"));

    private static bool IsItemsControl(XElement e)
        => Is(e, "ListBox") || Is(e, "ItemsControl") || Is(e, "ItemsRepeater");

    /// <summary>Grids inside a DataTemplate belong to a row template, not to the surrounding block.</summary>
    private static IEnumerable<XElement> OutsideTemplates(XElement subtree)
    {
        if (Is(subtree, "DataTemplate")) yield break;
        yield return subtree;
        foreach (var k in subtree.Elements())
            foreach (var d in OutsideTemplates(k))
                yield return d;
    }

    [Fact]
    public void Header_and_row_template_column_definitions_must_match()
    {
        var pairs = new List<(XElement Header, XElement Row)>();

        foreach (var ctl in Elements().Where(IsItemsControl))
        {
            var tmpl = ctl.Descendants().FirstOrDefault(e => Is(e, "DataTemplate"));
            if (tmpl is null) continue;

            // The row Grid = the OUTERMOST Grid carrying ColumnDefinitions in the template.
            var rowGrid = tmpl.DescendantsAndSelf()
                              .FirstOrDefault(e => Is(e, "Grid") && Attr(e, "ColumnDefinitions") is not null);
            if (rowGrid is null) continue;

            // Climb through any enclosing ScrollViewer(s): in the wrapped report pattern the header is a
            // sibling of the SCROLLVIEWER, not of the items control (whose only ancestor-side neighbour is
            // the ScrollViewer itself). Scanning ctl's own siblings finds nothing and skips the pair; the
            // anchor lifts the sibling scan to the level where the header actually lives. A ScrollViewer
            // holds a single content child, so this cannot cross into an unrelated block.
            var anchor = ctl;
            while (anchor.Parent is { } sv && Is(sv, "ScrollViewer")) anchor = sv;

            var siblings = anchor.Parent?.Elements().ToList();
            if (siblings is null) continue;
            var idx = siblings.IndexOf(anchor);

            XElement? header = null;
            for (var i = idx - 1; i >= 0; i--)
            {
                var sib = siblings[i];
                // Another items control already claims any header before it -> stop, do not reach past.
                if (sib.DescendantsAndSelf().Any(e => IsItemsControl(e)
                                                      && e.Descendants().Any(d => Is(d, "DataTemplate")))) break;
                header = OutsideTemplates(sib).LastOrDefault(IsHeaderGrid);
                if (header is not null) break;
            }
            if (header is null) continue;

            pairs.Add((header, rowGrid));
        }

        // NON-VACUITY: 112 pairs exist today (59 direct-sibling + 53 ScrollViewer-wrapped). If the pairing
        // rule silently stops matching (a renamed colHdr class, a restructured block, the ScrollViewer
        // climb removed), this test would pass while checking NOTHING. The floor sits above the 59 the
        // pre-ScrollViewer rule reached, so dropping the wrapped-pattern coverage alone trips it.
        Assert.True(pairs.Count >= 105,
            $"Only {pairs.Count} header/row twins were paired (expected ~112) — the pairing rule has stopped "
            + "matching, so this test would be vacuous. Fix the rule; do not lower this floor.");

        var drifted = pairs
            .Where(p => Attr(p.Header, "ColumnDefinitions")!.Replace(" ", "")
                     != Attr(p.Row, "ColumnDefinitions")!.Replace(" ", ""))
            .ToList();

        Assert.True(drifted.Count == 0,
            $"{drifted.Count} header/row template twin(s) have DRIFTED apart — the header no longer sits over "
            + "its own data:\n"
            + string.Join("\n", drifted.Select(p =>
                $"  header MainWindow.axaml({Line(p.Header)}): \"{Attr(p.Header, "ColumnDefinitions")}\"\n"
                + $"  row    MainWindow.axaml({Line(p.Row)}): \"{Attr(p.Row, "ColumnDefinitions")}\"")));
    }

    // =============================================================================================
    // INVARIANT 5 — the C2 label-column contract (Auto + a live shared-size scope + trimming).
    // =============================================================================================
    //
    // WHAT C2 WAS. Statutory/entry forms hardcoded their label column in px ("150,*,150,*"). The px
    // was sized by eye for a short label, so a real label OVERFLOWED it and was sliced at the input
    // border — "Applicable From" painted as "Applicable Fror", "Default credit period (days)" as
    // "…(day". The fix converted those label columns to Width="Auto" (Auto takes the label's full
    // desired width, so a label can never be sliced), added SharedSizeGroup where stacked rows must
    // keep their input edges aligned, and added TextTrimming as the last-resort safety net for when
    // the grid as a whole IS over budget and Auto columns do get compressed.
    //
    // WHY THIS TEST EXISTS — AND WHY IT IS *THIS* SHAPE. C2 shipped with no lock at all, and worse,
    // the conversion SHRANK this file's own coverage: MeasurableGrids() deliberately skips any grid
    // carrying an Auto column (an Auto width is not statically knowable), and it only reads the
    // ColumnDefinitions *attribute* — so the moment a grid moved to <Grid.ColumnDefinitions> elements
    // with Width="Auto", Invariants 2 and 3 stopped looking at it entirely. Every C2-fixed grid left
    // coverage BECAUSE it was fixed. This invariant is the replacement lock for exactly those grids:
    // where 2 and 3 measure widths, this one holds the CONTRACT that made measuring unnecessary.
    //
    // NO ALLOW-LIST. All 26 sites satisfy all three clauses today. This is a pure ratchet: an
    // Auto -> px revert (C2 verbatim), a deleted scope, or a dropped TextTrimming all go red.
    //
    // SCOPE, HONESTLY. This locks the label columns that were FIXED. It does NOT assert the general
    // rule "no literal label may sit in a hardcoded-px column" — measured, that rule has 256 live
    // violations in this file, every one of them also lacking TextTrimming. A 256-entry allow-list is
    // not a ratchet, it is noise that would be deleted at the first merge conflict, so it is
    // deliberately NOT written here. That 256 is the remaining C2 campaign surface; when the
    // per-form clusters burn it down, THAT is the moment to widen this test.

    private readonly record struct LabelColumn(XElement Grid, XElement Def, int Index, string Group)
    {
        public bool HasLiveScope
        {
            get
            {
                for (var p = Def.Parent; p is not null; p = p.Parent)
                    if (Attr(p, "Grid.IsSharedSizeScope") == "True") return true;
                return false;
            }
        }

        public IEnumerable<XElement> Cells
        {
            // 'this' cannot be captured by a lambda inside a struct — copy the fields out first.
            get
            {
                var (grid, index) = (Grid, Index);
                return grid.Elements()
                           .Where(e => !Is(e, "Grid.ColumnDefinitions") && ColumnIndexOf(e) == index);
            }
        }
    }

    /// <summary>Every ColumnDefinition carrying a SharedSizeGroup, with the Grid that owns it.</summary>
    private static List<LabelColumn> LabelColumns()
    {
        var found = new List<LabelColumn>();
        foreach (var grid in Elements().Where(e => Is(e, "Grid")))
        {
            var block = grid.Elements(Av + "Grid.ColumnDefinitions").FirstOrDefault();
            if (block is null) continue;
            var defs = block.Elements(Av + "ColumnDefinition").ToList();
            for (var i = 0; i < defs.Count; i++)
                if (Attr(defs[i], "SharedSizeGroup") is { } g)
                    found.Add(new LabelColumn(grid, defs[i], i, g));
        }
        return found;
    }

    [Fact]
    public void Shared_size_label_columns_must_stay_Auto_inside_a_live_scope_and_keep_trimming()
    {
        var cols = LabelColumns();

        // NON-VACUITY: 26 exist today. A rename of the element form, or the C2 forms being deleted,
        // would otherwise leave this test asserting over an empty list.
        Assert.True(cols.Count >= 26,
            $"Only {cols.Count} SharedSizeGroup label columns found (expected >= 26) — the C2 label-column "
            + "inventory has shrunk or the element-form parse broke, so this test would be vacuous.");

        // (a) Auto is the whole fix. A px width here IS C2, restored.
        var notAuto = cols.Where(c => Attr(c.Def, "Width") != "Auto").ToList();
        Assert.True(notAuto.Count == 0,
            $"{notAuto.Count} label column(s) carry a SharedSizeGroup but are NOT Width=\"Auto\". Auto is what "
            + "guarantees the label gets its full desired width and can never be sliced at the input border — a "
            + "fixed px width here re-introduces C2 verbatim:\n"
            + string.Join("\n", notAuto.Select(c =>
                $"  MainWindow.axaml({Line(c.Def)}): SharedSizeGroup=\"{c.Group}\" Width=\"{Attr(c.Def, "Width")}\"")));

        // (b) A SharedSizeGroup with no Grid.IsSharedSizeScope ancestor is DEAD CONFIGURATION — it
        //     silently does nothing and the stacked rows' input edges drift apart again.
        var orphaned = cols.Where(c => !c.HasLiveScope).ToList();
        Assert.True(orphaned.Count == 0,
            $"{orphaned.Count} label column(s) declare a SharedSizeGroup with NO Grid.IsSharedSizeScope=\"True\" "
            + "ancestor. The group is then inert: the columns silently stop sharing a width and the input edges "
            + "they were added to align drift apart:\n"
            + string.Join("\n", orphaned.Select(c =>
                $"  MainWindow.axaml({Line(c.Def)}): SharedSizeGroup=\"{c.Group}\"")));

        // (c) The safety net. Auto columns ARE compressed when the whole grid is over budget, and an
        //     untrimmed label then paints over the input instead of ellipsizing. Only TextBlocks are
        //     judged — the two CheckBoxes parked in these columns have no TextTrimming property.
        var untrimmed = cols
            .SelectMany(c => c.Cells.Where(e => Is(e, "TextBlock")).Select(e => (Col: c, Text: e)))
            .Where(x => Attr(x.Text, "TextTrimming") is null or "None")
            .ToList();
        Assert.True(untrimmed.Count == 0,
            $"{untrimmed.Count} label TextBlock(s) in a shared-size label column declare no TextTrimming. Auto "
            + "columns are still compressed when the grid as a whole is over budget, and an untrimmed label then "
            + "paints over the input next to it instead of ellipsizing:\n"
            + string.Join("\n", untrimmed.Select(x =>
                $"  MainWindow.axaml({Line(x.Text)}): Text=\"{Attr(x.Text, "Text")}\" in group \"{x.Col.Group}\"")));

        // NON-VACUITY for (c): the clause must actually have TextBlocks to judge.
        var judged = cols.SelectMany(c => c.Cells).Count(e => Is(e, "TextBlock"));
        Assert.True(judged >= 24,
            $"Only {judged} label TextBlocks were found in shared-size columns (expected >= 24) — the "
            + "Grid.Column -> ColumnDefinition mapping is broken, so clause (c) would be vacuous.");
    }

    // =============================================================================================
    // INVARIANT 6 — the C3 stat-cell contract: a label and its value share ONE alignment.
    // =============================================================================================
    //
    // WHAT C3 WAS. A stat strip is a Grid "*,*,*,*" of cells, each a VERTICAL StackPanel holding a
    // label TextBlock stacked over a value TextBlock. A vertical StackPanel stretches every child to
    // the full cell width. So a value tagged TextAlignment="Right" sits at its cell's RIGHT edge while
    // its own label sits at the cell's LEFT edge — and the value ends up visually adjacent to the NEXT
    // cell's label. The reader attaches the figure to the WRONG head: on RunSetOff the IGST liability
    // 5,400.00 read as "RCM (cash-only)", which is really 0.00. The gap GROWS as the pane widens.
    //
    // THE FIX, AND WHAT THIS LOCKS. Classes="statCell" + a style that right-aligns label AND value
    // TOGETHER. That is why the fix cannot itself reintroduce C3: C3 was label and value DIVERGING
    // inside one cell, and the class pins them to the same edge. This test holds exactly that
    // property — not "everything goes right", which would be a NEW defect on the text/count cells.
    //
    // NO ALLOW-LIST. All 78 label/value stacks agree today (41 tagged right/right, 37 untagged
    // left/left). Pure ratchet: dropping Classes="statCell" from a cell whose value is Right-aligned,
    // or adding a new strip cell with a Right value and a Left label, goes red.
    //
    // MEASURED, NOT ASSUMED. 14 further money cells (label ends "₹") are untagged and left/left. They
    // are NOT flagged: label and value MATCH, so they are not C3. Flagging them would contradict the
    // documented rule and would be exactly the kind of over-reach that gets a test deleted.

    /// <summary>
    /// The edge a TextBlock's text visually sits against inside a vertical StackPanel cell.
    /// A non-Stretch HorizontalAlignment shrinks the block to its content and wins outright;
    /// otherwise the block is stretched to the cell and TextAlignment decides. An untagged cell's
    /// TextBlock defaults to Left; a statCell one inherits Right from the style (a LOCAL
    /// TextAlignment still wins over a style setter, so it is checked first).
    /// </summary>
    private static string TextEdge(XElement text, bool inStatCell)
    {
        var ha = Attr(text, "HorizontalAlignment");
        if (ha is not null && ha != "Stretch") return ha;
        var ta = Attr(text, "TextAlignment") ?? (inStatCell ? "Right" : null) ?? "Left";
        return ta switch { "Start" => "Left", "End" => "Right", _ => ta };
    }

    /// <summary>The RIGHT component of a Thickness attribute value ("0,0,12,0" -> 12). A 1-value Thickness
    /// is uniform; a 2-value Thickness is "horizontal,vertical" so its right IS the horizontal value; a
    /// 4-value Thickness is "left,top,right,bottom". Returns 0 when absent or unparseable.</summary>
    private static double MarginRightInset(string? margin)
    {
        if (margin is null) return 0;
        var p = margin.Split(',');
        double At(int i) => double.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return p.Length switch { 1 => At(0), 2 => At(0), 4 => At(2), _ => 0 };
    }

    /// <summary>The "label stacked over value" shape: a vertical StackPanel whose children are ALL
    /// TextBlocks, at least two of them.</summary>
    private static List<XElement> LabelValueStacks() => Elements()
        .Where(e => Is(e, "StackPanel")
                    && (Attr(e, "Orientation") ?? "Vertical") == "Vertical"
                    && e.Elements().Count() >= 2
                    && e.Elements().All(k => Is(k, "TextBlock")))
        .ToList();

    [Fact]
    public void Stat_cell_label_and_value_must_share_one_alignment()
    {
        // (a) The class is only meaningful because of its style. If the style is deleted or its setter
        //     changed, every tagged cell silently reverts to left/left labels beside right-aligned
        //     values — C3, restored — while the divergence check below still read "statCell => Right"
        //     and stayed green. Assert the style this test's whole model rests on.
        var style = Elements().FirstOrDefault(e => Is(e, "Style")
                                                   && (Attr(e, "Selector") ?? "") == "StackPanel.statCell > TextBlock");
        Assert.True(style is not null,
            "The 'StackPanel.statCell > TextBlock' style is GONE. Classes=\"statCell\" is then inert decoration: "
            + "every tagged stat cell reverts to a left label beside a right-aligned value (C3).");
        Assert.True(
            style!.Elements(Av + "Setter").Any(s => Attr(s, "Property") == "TextAlignment" && Attr(s, "Value") == "Right"),
            "The 'StackPanel.statCell > TextBlock' style no longer sets TextAlignment=\"Right\". The class stops "
            + "pinning the label to the value's edge, so C3 returns on all tagged cells.");

        // (a2) THE GUTTER (C3's second half). The strips carry NO gutter of their own: a right-aligned
        //      value ends EXACTLY on its cell boundary and the next cell's left-aligned text begins on that
        //      same boundary, so the two collide with no separating space — Form 24Q rendered the amount
        //      12,45,678.50 butting the neighbouring count 6 as "12,45,678.506", the count reading as a
        //      trailing DIGIT of the figure (the same misread class C3 fixed). The style's Margin
        //      right-inset is the ONLY thing holding the columns apart; it applies to label AND value
        //      equally, so it cannot itself reintroduce C3. A dead agent already DROPPED this exact setter
        //      once this slice, and clause (a) above stayed green because TextAlignment was untouched — so
        //      the gutter needs its own lock. Assert the Margin setter is present with a NON-ZERO right inset.
        var gutter = style!.Elements(Av + "Setter").FirstOrDefault(s => Attr(s, "Property") == "Margin");
        Assert.True(gutter is not null,
            "The 'StackPanel.statCell > TextBlock' style no longer declares a Margin gutter. Without it a "
            + "right-aligned stat value ends flush on its cell boundary and butts the next cell's text with no "
            + "separating space — the money<->count collision C3's gutter was added to prevent (Form 24Q).");
        var rightInset = MarginRightInset(Attr(gutter!, "Value"));
        Assert.True(rightInset > 0,
            $"The 'StackPanel.statCell > TextBlock' Margin gutter declares a right inset of {rightInset:F0} — it "
            + "must be > 0. A zero right inset lets a right-aligned value end exactly on its cell boundary and "
            + "collide with the neighbouring cell's text (a figure reading as if the next count were its trailing "
            + "digit). Restore the load-bearing right inset (it shipped as \"0,0,12,0\").");

        var stacks = LabelValueStacks();

        // NON-VACUITY: 78 label/value stacks exist today, 41 of them tagged. If the shape match breaks,
        // the divergence check below becomes an empty loop.
        Assert.True(stacks.Count > 60,
            $"Only {stacks.Count} label/value stacks found (expected ~78) — the stat-cell shape match is broken, "
            + "so this test would be vacuous.");
        var tagged = stacks.Count(s => HasClass(s, "statCell"));
        Assert.True(tagged > 30,
            $"Only {tagged} stat cells carry Classes=\"statCell\" (expected ~41) — the C3 fix has been largely "
            + "reverted, or the class lookup is broken.");

        // (b) The invariant itself.
        var diverging = stacks
            .Select(s => (Stack: s, Edges: s.Elements()
                                            .Select(t => TextEdge(t, HasClass(s, "statCell")))
                                            .Distinct()
                                            .ToList()))
            .Where(x => x.Edges.Count > 1)
            .ToList();

        Assert.True(diverging.Count == 0,
            $"{diverging.Count} stat cell(s) let their label and value drift to DIFFERENT edges of the same cell. "
            + "A vertical StackPanel stretches both TextBlocks to the full cell width, so the value slides to the "
            + "far edge while its own label stays put — the figure then reads as belonging to the NEXT cell's "
            + "label (a financial mis-attribution). Tag the cell Classes=\"statCell\" so the pair moves together:\n"
            + string.Join("\n", diverging.Select(x =>
                $"  MainWindow.axaml({Line(x.Stack)}): statCell={HasClass(x.Stack, "statCell")}, edges=["
                + string.Join(", ", x.Stack.Elements().Select(t =>
                    $"{Attr(t, "Text")}=>{TextEdge(t, HasClass(x.Stack, "statCell"))}")) + "]")));
    }

    // =============================================================================================
    // INVARIANT 7 — the C5b contract: a wide "tier-3" report grid must be FLUID, never a hard wide
    //               constant Width.
    // =============================================================================================
    //
    // WHAT C5b WAS. Seven report bodies hardcoded a fixed WIDE size: a Width constant (Tax Analysis 830,
    // GSTR-1 1030, Batch Age 1030, Batchwise 1080, Price List 1080, PF ECR 1120) or — Form 24Q's
    // Annexure-II — a bare wide MinWidth (1160) with no Width at all. At the 1120/1440 default window the
    // page column is NARROWER than that constant, so the grid overflowed the pane and hid its rightmost
    // columns behind an inner horizontal scrollbar WHILE the pane still had empty room to the side (the
    // "wider-than-pane" / "off-pane-blank" defect class from the sweep). The fix made each grid FLUID:
    // Width bound to the enclosing page-pane's Bounds.Width — so it fills the pane and the widest TEXT
    // column (turned to *) absorbs the slack — with MinWidth = the OLD constant, so at a genuinely narrow
    // window it keeps its size and the existing inner ScrollViewer h-scrolls rather than SQUEEZING the
    // numeric/amount columns (financial legibility).
    //
    // WHY THIS TEST EXISTS. The header/row twin identity is already locked by Invariant 4, and the
    // starved-* / fixed-overflow arithmetic by Invariants 2-3 — but NONE of them sees a wide constant on
    // the OUTER body grid. Those bodies sit inside an h-scrolling ScrollViewer, so FiniteBudget() is
    // infinite and MeasurableGrids() skips them entirely; and Invariant 4 stays green when BOTH twins are
    // reverted together. So a revert of the C5b outer Width — back to Width="830", or back to a bare wide
    // MinWidth with no fluid Width — would go completely unnoticed. This invariant is that missing lock.
    //
    // TWO PURE RATCHETS, NO ALLOW-LIST (both green today; each may only ever stay at zero).
    //   (A) No Grid may hardcode a numeric Width at or above the page-column floor (638). Such a grid is by
    //       construction wider than the narrowest pane and overflows it. Today the only numeric Grid Widths
    //       in the file are the 560/596 statutory panes, both < 638; every wide report body binds its Width
    //       fluidly. Reverting any C5b (or D1/D2) fluid body to Width="<constant>" trips this.
    //   (B) Every WIDE REPORT BODY — a Grid carrying a numeric MinWidth >= the floor and CONTAINING a colHdr
    //       header (so it is provably a report body), excluding the header grid itself and any row-template
    //       grid — must declare a fluid Width binding (a Binding to some element's .Bounds.). This catches
    //       the Form 24Q shape, whose defect was a bare wide MinWidth with NO Width: (A) cannot see that
    //       (there is no numeric Width to flag), but (B) demands the fluid Width be present.

    private static double? NumericAttr(XElement e, string name)
        => Attr(e, name) is { } raw
           && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : (double?)null;

    /// <summary>A wide report BODY: a Grid whose MinWidth floor meets/exceeds the page-column floor and
    /// which CONTAINS a colHdr header grid as a descendant (so it is provably the OUTER report body — a
    /// header grid IS its own colHdr TextBlocks' parent, so it is excluded by <see cref="IsHeaderGrid"/>,
    /// and a per-row template grid contains only data cells, no colHdr, so the header-containment
    /// requirement excludes it too — no DataTemplate test needed, which matters because the PF-ECR /
    /// Form-24Q bodies legitimately live inside a report-level ContentTemplate).</summary>
    private static List<XElement> WideReportBodies() => Elements()
        .Where(e => Is(e, "Grid")
                    && NumericAttr(e, "MinWidth") is { } m && m >= PageColumnContentWidth
                    && !IsHeaderGrid(e)
                    && e.Descendants().Any(IsHeaderGrid))
        .ToList();

    [Fact]
    public void Wide_report_grids_must_be_fluid_not_a_hardcoded_wide_width()
    {
        Assert.Equal(638.0, PageColumnContentWidth); // the floor this whole test rests on

        // (A) No Grid may hardcode a numeric Width >= the page floor.
        var wideFixed = Elements()
            .Where(e => Is(e, "Grid") && NumericAttr(e, "Width") is { } w && w >= PageColumnContentWidth)
            .ToList();
        Assert.True(wideFixed.Count == 0,
            $"{wideFixed.Count} Grid(s) hardcode a numeric Width >= the page-column floor "
            + $"({PageColumnContentWidth:F0}px). A grid wider than the narrowest pane overflows it and hides its "
            + "rightmost columns behind an inner h-scrollbar while the pane still has room (C5b). Bind Width to the "
            + "enclosing page-pane's Bounds.Width (MinWidth = the old constant) instead:\n"
            + string.Join("\n", wideFixed.Select(g =>
                $"  MainWindow.axaml({Line(g)}): Width=\"{Attr(g, "Width")}\"")));

        // (B) Every wide report body must be fluid.
        var bodies = WideReportBodies();

        // NON-VACUITY: 8 wide report bodies exist today (the 7 C5b grids + the D1/D2 GSTR-3B body). If the
        // signature stops matching (a renamed colHdr class, the MinWidth floors dropped, a restructured
        // body), both this clause and clause (A) — which share the same XAML walk — would be judging an
        // empty set. The floor guards the whole test.
        Assert.True(bodies.Count >= 6,
            $"Only {bodies.Count} wide report bodies found (expected ~8) — the wide-report-body signature has "
            + "stopped matching, so this test would be vacuous. Fix the signature; do not lower this floor.");

        var notFluid = bodies
            .Where(g => Attr(g, "Width") is not { } w
                        || !(w.Contains("Binding", StringComparison.Ordinal)
                             && w.Contains(".Bounds.", StringComparison.Ordinal)))
            .ToList();
        Assert.True(notFluid.Count == 0,
            $"{notFluid.Count} wide report body Grid(s) (numeric MinWidth >= {PageColumnContentWidth:F0}px, "
            + "containing a colHdr header) do NOT bind Width fluidly to a pane's .Bounds.Width. A bare wide "
            + "MinWidth with no fluid Width re-introduces C5b (the Form 24Q shape): the body overflows the default "
            + "pane. Restore Width=\"{Binding #<Pane>.Bounds.Width}\":\n"
            + string.Join("\n", notFluid.Select(g =>
                $"  MainWindow.axaml({Line(g)}): MinWidth=\"{Attr(g, "MinWidth")}\" "
                + $"Width=\"{Attr(g, "Width") ?? "(none)"}\"")));
    }

    // =============================================================================================
    // INVARIANT 8 — the D4 C6/C7 contract: the reusable empty-state spans the WHOLE body, and the
    //               un-starved inventory-register grids keep their MinWidth pin.
    // =============================================================================================
    //
    // WHAT C6/C7 WAS. ~19 report screens improvised an "empty" message by writing it into ONE grid
    // column (Col4). In four over-committed inventory registers that column is the "*" (Item) column,
    // clamped to ZERO width when the fixed columns already fill the pane — so the message (and the
    // Grand-Total) were laid out and painted into zero px: invisible. THE FIX was two parts: (C7) a
    // single reusable <EmptyState> control dropped into the body and spanning the WHOLE body (never
    // routed into one column), shown when the row collection is empty; and (C6) wrapping the three
    // register grids in an h-scrolling ScrollViewer and pinning a MinWidth so the "*" columns keep
    // width instead of starving.
    //
    // WHY THIS TEST EXISTS. Nothing else locks either half. Invariant 2 measures fixed-column fit, but
    // once the register grids moved inside an h-scrolling ScrollViewer FiniteBudget() is infinite and
    // MeasurableGrids() skips them — so a revert that keeps the ScrollViewer but DROPS the MinWidth
    // (letting the "*" columns collapse again) would go completely unseen. And the empty-state is a
    // brand-new component with no coverage at all: deleting the report-body overlay, or routing a new
    // one back into a single column, would silently reopen C6/C7. Two headless-safe static clauses.

    private static bool IsEmptyState(XElement e) => e.Name.LocalName == "EmptyState";

    /// <summary>The number of columns a Grid declares (attribute form "a,b,*"), or 1 when it declares
    /// none (a single implicit column that spans the whole width — the safe, full-body shape).</summary>
    private static int GridColumnCount(XElement grid)
        => Attr(grid, "ColumnDefinitions") is { } spec ? ParseColumns(spec).Count : 1;

    /// <summary>The three inventory-register column specs the C6 fix un-starved (Order Register's two-"*"
    /// grid, the allocation/material registers, and the Job Work Order Books). Header + row twins share
    /// each spec, so every match must carry the MinWidth pin.</summary>
    private static readonly string[] C6RegisterSpecs =
    {
        "100,150,*,*,120,100,100,110",
        "95,60,150,*,120,90,100,120",
        "95,120,150,*,110,95,95,95",
    };

    [Fact]
    public void Reusable_empty_state_spans_the_body_and_C6_registers_stay_minwidth_pinned()
    {
        // ---- (A) the reusable empty-state is PRESENT (C7). ----
        var emptyStates = Elements().Where(IsEmptyState).ToList();

        // NON-VACUITY + PRESENCE: 4 exist today — the shared report body (bound to IsEmpty) plus the
        // Scenario / Price-Level / Reorder-Level master overlays (bound via the CollectionEmpty converter).
        // Deleting the report-body overlay, or the whole component, drops below this floor.
        Assert.True(emptyStates.Count >= 4,
            $"Only {emptyStates.Count} <EmptyState> element(s) found (expected >= 4). The reusable empty-state "
            + "(UI-defect C7) has been removed or the element renamed — report/master screens with no data "
            + "would then show a blank body again. Restore the <views:EmptyState> overlays.");

        // The shared report body's overlay is the centrepiece: it must stay bound to the IsEmpty gate, or a
        // populated report would be covered / an empty one left blank.
        var reportBodyOverlay = emptyStates.Count(e => (Attr(e, "IsVisible") ?? "").Contains("IsEmpty", StringComparison.Ordinal));
        Assert.True(reportBodyOverlay >= 1,
            "No <EmptyState> is bound to IsVisible=\"{Binding IsEmpty}\" — the shared report-body empty-state "
            + "(the C6/C7 fix's centrepiece) is gone. Without it the register reports render a blank body with no "
            + "\"no entries\" message when a period has no data.");

        // ---- (B) the empty-state spans the WHOLE body — never routed into ONE column (the C6 mistake). ----
        var singleColumnRouted = emptyStates
            .Where(e =>
            {
                if (e.Parent is not { } parent || !Is(parent, "Grid")) return false; // not in a Grid → not this defect
                var cols = GridColumnCount(parent);
                if (cols <= 1) return false;                                          // single-column parent → full width
                var col = int.TryParse(Attr(e, "Grid.Column") ?? "0", out var c) ? c : 0;
                var span = int.TryParse(Attr(e, "Grid.ColumnSpan") ?? "1", out var s) ? s : 1;
                return col + span < cols;                                             // does not reach the last column
            })
            .ToList();
        Assert.True(singleColumnRouted.Count == 0,
            $"{singleColumnRouted.Count} <EmptyState>(s) are routed into a SINGLE column of a multi-column grid "
            + "instead of spanning the whole body — exactly the C6 mistake (a message laid out into a starved, "
            + "possibly zero-width column). Give the EmptyState a single-column parent or a Grid.ColumnSpan that "
            + "reaches the last column:\n"
            + string.Join("\n", singleColumnRouted.Select(e => $"  MainWindow.axaml({Line(e)})")));

        // ---- (C) the three un-starved inventory-register grids keep their MinWidth pin (C6). ----
        var registerGrids = Elements()
            .Where(e => Is(e, "Grid") && Attr(e, "ColumnDefinitions") is { } spec
                        && C6RegisterSpecs.Contains(spec.Replace(" ", "")))
            .ToList();

        // NON-VACUITY: 6 grids today (3 specs × header/row twin). If the specs are renamed/restructured this
        // clause must not silently pass over an empty set.
        Assert.True(registerGrids.Count >= 6,
            $"Only {registerGrids.Count} inventory-register grids matched the C6 specs (expected >= 6) — the "
            + "register templates were renamed/restructured, so this clause would be vacuous. Update C6RegisterSpecs.");

        var unpinned = registerGrids.Where(g => !(NumericAttr(g, "MinWidth") is { } m && m >= PageColumnContentWidth)).ToList();
        Assert.True(unpinned.Count == 0,
            $"{unpinned.Count} inventory-register grid(s) lost their MinWidth pin (>= {PageColumnContentWidth:F0}px). "
            + "These grids sit inside an h-scrolling ScrollViewer, so Invariant 2 no longer measures them — without "
            + "the MinWidth the \"*\" Item column starves back to zero in a narrow pane and the Grand-Total / "
            + "empty-state paint into zero px again (C6). Restore MinWidth:\n"
            + string.Join("\n", unpinned.Select(g =>
                $"  MainWindow.axaml({Line(g)}): \"{Attr(g, "ColumnDefinitions")}\" MinWidth=\"{Attr(g, "MinWidth") ?? "(none)"}\"")));

        // Each register grid must also live inside an h-scrolling ScrollViewer (the other half of the C6 fix:
        // the pinned width scrolls instead of overflowing the pane).
        var notScrolled = registerGrids
            .Where(g => !Ancestors(g).Any(a => Is(a, "ScrollViewer")
                        && Attr(a, "HorizontalScrollBarVisibility") is "Auto" or "Visible"))
            .ToList();
        Assert.True(notScrolled.Count == 0,
            $"{notScrolled.Count} inventory-register grid(s) are no longer wrapped in an h-scrolling ScrollViewer. "
            + "A MinWidth-pinned grid with no h-scroll overflows the pane and clips its rightmost columns instead "
            + "of scrolling (C6). Restore the enclosing <ScrollViewer HorizontalScrollBarVisibility=\"Auto\">:\n"
            + string.Join("\n", notScrolled.Select(g => $"  MainWindow.axaml({Line(g)})")));
    }

    // =============================================================================================
    // INVARIANT 9 — the D5 C10 numeric-cell style + the D5 C7-completion master empty-states.
    // =============================================================================================
    //
    // WHAT C10 / C7-COMPLETION WERE. (C10) The accounting-report value cells (Debit / Credit / single
    // Amount) each carried an ad-hoc TextAlignment="Right" Margin="0,0,10,0", a 10px right inset that did
    // NOT match the shared colHdr header Padding.Right (8), so a value's right edge sat 2px left of its
    // column header's right edge. The fix is a single shared <Style Selector="TextBlock.numCell"> —
    // TextAlignment=Right + Margin="0,0,8,0" (right inset == the colHdr Padding.Right) — that the value
    // cells adopt via Classes="numCell", pinning every figure flush under its header. (C7-completion) six
    // more masters that improvised no "empty" message at all got the reusable <EmptyState> overlay D4
    // introduced, gated by the CollectionEmpty converter so it shows ONLY when the list is empty.
    //
    // WHY THIS TEST EXISTS. Neither piece has any other lock. The numCell style is a shared resource: if
    // it is deleted or its right-alignment / gutter is changed, every migrated value cell silently
    // reverts and the column drifts off its header again — nothing else would notice (Invariant 4 checks
    // header/row COLUMN definitions, not per-cell alignment). And the six D5 empty-states are additive
    // overlays: deleting one, or dropping its CollectionEmpty gate (so it either never shows on an empty
    // list, or COVERS a populated one), is invisible to every other invariant. Two headless-safe clauses.

    /// <summary>The six D5 C7-completion master empty-states, keyed by their (edit-stable) Message text.
    /// Each MUST be present and gated by the CollectionEmpty converter on a <c>.Count</c> binding.</summary>
    private static readonly string[] D5EmptyStateMessages =
    {
        "No budgets created yet.",
        "No bills of materials created yet.",
        "No employee categories created yet.",
        "No payroll units created yet.",
        "No deductee rows for this quarter yet.",
        "No collectees for this quarter yet.",
    };

    [Fact]
    public void Numeric_cell_style_and_D5_master_empty_states_are_present_and_gated()
    {
        // ---- (A) the shared numeric-value-cell style exists, right-aligns, and keeps its gutter (C10). ----
        var numCell = Elements().FirstOrDefault(e => Is(e, "Style")
                                                     && (Attr(e, "Selector") ?? "") == "TextBlock.numCell");
        Assert.True(numCell is not null,
            "The 'TextBlock.numCell' style is GONE. Every accounting value cell tagged Classes=\"numCell\" then "
            + "renders with default (left) alignment and no gutter — the money columns un-align from their headers "
            + "(C10). Restore <Style Selector=\"TextBlock.numCell\"> with TextAlignment=\"Right\" Margin=\"0,0,8,0\".");
        Assert.True(
            numCell!.Elements(Av + "Setter").Any(s => Attr(s, "Property") == "TextAlignment" && Attr(s, "Value") == "Right"),
            "The 'TextBlock.numCell' style no longer sets TextAlignment=\"Right\". A money value must right-align so "
            + "its last digit lands under the column header's right edge (C10).");
        var numCellGutter = numCell.Elements(Av + "Setter").FirstOrDefault(s => Attr(s, "Property") == "Margin");
        Assert.True(numCellGutter is not null && MarginRightInset(Attr(numCellGutter!, "Value")) > 0,
            "The 'TextBlock.numCell' style no longer declares a Margin with a non-zero RIGHT inset. That inset is "
            + "what makes a value's right edge coincide with its colHdr header's right edge (the header's Padding.Right "
            + "is 8); a zero inset pins the value hard against the pane edge, 8px right of its header (C10). Restore "
            + "Margin=\"0,0,8,0\".");

        // (A2) The style is only meaningful if the value cells USE it. The three accounting value cells
        //      (two-column Debit + Credit, and the single-amount Amount) were migrated to Classes="numCell";
        //      a revert to the ad-hoc local TextAlignment/Margin drops every reference.
        var numCellUsers = Elements().Count(e => Is(e, "TextBlock") && HasClass(e, "numCell"));
        Assert.True(numCellUsers >= 3,
            $"Only {numCellUsers} TextBlock(s) carry Classes=\"numCell\" (expected >= 3: the accounting Debit, "
            + "Credit and single-Amount value cells). The C10 migration has been reverted to ad-hoc per-cell "
            + "TextAlignment/Margin, so the shared alignment contract no longer binds those cells.");

        // ---- (B) the six D5 master empty-states are present and correctly gated (C7-completion). ----
        var emptyStates = Elements().Where(IsEmptyState).ToList();

        // NON-VACUITY: 10 <EmptyState> overlays exist after D5 (D4's report-body + Scenario/Price-Level/
        // Reorder-Level, plus these six). If the element is renamed or the walk breaks, the per-message loop
        // below becomes an empty search that passes while proving nothing.
        Assert.True(emptyStates.Count >= 10,
            $"Only {emptyStates.Count} <EmptyState> element(s) found (expected >= 10 after D5). The reusable "
            + "empty-state has been removed or renamed; the per-master checks below would be vacuous.");

        foreach (var msg in D5EmptyStateMessages)
        {
            var es = emptyStates.FirstOrDefault(e => Attr(e, "Message") == msg);
            Assert.True(es is not null,
                $"The D5 master empty-state '{msg}' is MISSING. Its screen shows a blank body again when the list "
                + "is empty (C7). Restore <views:EmptyState Message=\"" + msg + "\" IsVisible=\"{Binding <List>.Count, "
                + "Converter={StaticResource CollectionEmpty}}\"/> spanning the list body.");

            var vis = Attr(es!, "IsVisible") ?? "";
            Assert.True(
                vis.Contains("CollectionEmpty", StringComparison.Ordinal) && vis.Contains(".Count", StringComparison.Ordinal),
                $"The D5 master empty-state '{msg}' is not gated by the CollectionEmpty converter on a .Count binding "
                + $"(IsVisible=\"{vis}\"). Without that gate it either never appears on an empty list, or it COVERS a "
                + "populated one. Restore IsVisible=\"{Binding <List>.Count, Converter={StaticResource CollectionEmpty}}\".");
        }
    }

    // =============================================================================================
    // INVARIANT 10 — no INTERNAL BUILD JARGON leaks into user-facing text (C12 de-brand guard).
    // =============================================================================================
    //
    // THE RULE FAMILY. The shipped app is "Apex Solutions"; it must never surface the internal build
    // vocabulary the team writes in comments and commits — slice/phase codenames ("Phase 1 defaults",
    // "S7 poster", "UI-2", "Cluster 2", "slice 8") — in text a user actually reads. C12 removed two such
    // leaks that had reached the UI: the create-company hint "…base currency ₹ INR (Phase 1 defaults)."
    // and the ITC-reversal heading "Reversal candidates (for the S7 poster)". This is the same de-brand
    // discipline as the no-"Tally" rule, and it had NO lock — a third leak could ship unseen.
    //
    // WHAT IS SCANNED. Only USER-FACING LITERAL strings: the content attributes below (and any literal
    // element text), on real XElements. XML COMMENTS are XComment nodes, never XElement attributes, so the
    // hundreds of legitimate "Phase 6 slice 8" build annotations in comments are correctly invisible here
    // — this guard fires only on jargon that escaped a comment into a Text/Content/Message/etc. that
    // paints on screen. Binding/markup-extension values ("{Binding …}") are skipped: they are code paths,
    // not literals, and a property name like ControlTotalsTally is not a displayed string.

    /// <summary>Attribute local-names whose LITERAL value is painted on screen for the user to read.</summary>
    private static readonly HashSet<string> UserFacingAttrs = new(StringComparer.Ordinal)
    {
        "Text", "Content", "Header", "Watermark", "PlaceholderText", "Title", "Message", "Description", "ToolTip.Tip",
    };

    /// <summary>The internal build vocabulary that must never reach a user-facing string. Each pattern is
    /// case-insensitive and documented; the two D5 leaks it was calibrated to catch are named.</summary>
    private static readonly (string Pattern, string What)[] JargonPatterns =
    {
        (@"\bPhase\s*\d+\b",           "a phase number (e.g. the removed \"(Phase 1 defaults)\")"),
        (@"\bslice\s*\d+\b",           "a slice number (internal build unit)"),
        (@"\bposter\b",                "the internal \"poster\" term (e.g. the removed \"(for the S7 poster)\")"),
        (@"\bS[1-9]\b\s*(?:poster|slice)","a slice codename (e.g. \"S7 poster\")"),
        (@"\bUI-[1-9]\b",              "a UI-slice codename (UI-1 / UI-2 / UI-3)"),
        (@"\bCluster\s*\d+\b",         "a build-cluster codename (e.g. \"Cluster 2\")"),
    };

    /// <summary>Every user-facing LITERAL string in the file, paired with the element carrying it. A value
    /// beginning with '{' is a binding / markup extension (not a literal a user reads) and is excluded.</summary>
    private static IEnumerable<(XElement Owner, string Source, string Value)> UserFacingLiterals()
    {
        foreach (var e in Elements())
        {
            foreach (var a in e.Attributes())
            {
                if (!UserFacingAttrs.Contains(a.Name.LocalName)) continue;
                var v = a.Value.Trim();
                if (v.Length == 0 || v[0] == '{') continue; // binding / markup extension -> not a literal
                yield return (e, a.Name.LocalName, a.Value);
            }
            // Literal element text: <TextBlock>Some literal</TextBlock>. XComment children are skipped by
            // taking XText nodes only, so comment jargon never reaches this scan.
            foreach (var t in e.Nodes().OfType<XText>())
            {
                var v = t.Value.Trim();
                if (v.Length == 0 || v[0] == '{') continue;
                yield return (e, "(text)", t.Value);
            }
        }
    }

    [Fact]
    public void No_internal_build_jargon_in_user_facing_text()
    {
        var literals = UserFacingLiterals().ToList();

        // NON-VACUITY: this file has thousands of user-facing literals. If the extraction breaks (a renamed
        // content attribute, the XText walk failing), the jargon scan below would judge an empty set.
        Assert.True(literals.Count > 500,
            $"Only {literals.Count} user-facing literal strings were extracted — the content-attribute / text walk "
            + "is broken, so this de-brand guard would be vacuous.");

        var regexes = JargonPatterns
            .Select(j => (Rx: new Regex(j.Pattern, RegexOptions.IgnoreCase), j.What))
            .ToArray();

        var leaks = new List<string>();
        foreach (var (owner, source, value) in literals)
            foreach (var (rx, what) in regexes)
                if (rx.Match(value) is { Success: true } m)
                    leaks.Add($"  MainWindow.axaml({Line(owner)}): {source}=\"{value}\" leaks {what} "
                              + $"(matched \"{m.Value}\")");

        Assert.True(leaks.Count == 0,
            $"{leaks.Count} user-facing string(s) leak internal build jargon (slice/phase/cluster codenames). "
            + "The shipped UI must read as \"Apex Solutions\", never the internal build vocabulary — reword to "
            + "plain user-facing text:\n" + string.Join("\n", leaks));
    }
}
