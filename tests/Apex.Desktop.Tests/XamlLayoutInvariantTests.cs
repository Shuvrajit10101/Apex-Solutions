using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// The width the Miller-column PAGE column gives its content. Read from the PRODUCTION converter
    /// (<see cref="ColumnWidthConverter"/>, bound as Width on the column Border in MainWindow.axaml)
    /// rather than hardcoded, so if the shell is re-sized these tests follow it automatically.
    /// NOTE: the XAML comment at that Border still says "page columns = 560" — it is STALE; the
    /// converter is the truth (this is exactly why these tests read code, never prose).
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
                // The cascade page column: Width="{Binding IsMenu, Converter={StaticResource ColWidth}}".
                if (raw.Contains("ColWidth", StringComparison.Ordinal)) return PageColumnContentWidth;
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? v
                    : null; // some other binding -> unknowable statically -> do not judge
            }
            if (IsHorizontalStackPanel(a)) return null;
            if (Is(a, "ScrollViewer")
                && Attr(a, "HorizontalScrollBarVisibility") is "Auto" or "Visible") return null;
        }
        return null;
    }

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
        // Job Work In / Out Order Book — header + row twins. 760px of fixed in 638px: the "Item" (*)
        // column is gone and Fulfilled/Pending are clipped. REMOVED BY: the job-work report cluster.
        "95,120,150,*,110,95,95,95",
        // Job Work Order/Stock voucher grids. 735px of fixed in 638px.
        // REMOVED BY: the job-work report cluster.
        "95,60,150,*,120,90,100,120",
        // e-Invoice / e-Way status + amendment tables (6 sites). 670px of fixed in 638px.
        // REMOVED BY: the GST e-invoice/e-way + amendments screen cluster.
        "140,*,140,140,140,110",
        // Job Work stock/consumption statement. 680px of fixed in 638px, and TWO * columns at zero.
        // REMOVED BY: the job-work report cluster.
        "100,150,*,*,120,100,100,110",
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
}
