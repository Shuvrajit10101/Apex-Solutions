using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Apex.Desktop.Tests;

/// <summary>
/// PER-TRACK FLOORS for the two Challan Reconciliation screens — the six sites behind the
/// <c>OverflowingGridAllowList</c> entry <c>"140,*,140,140,140,110"</c> (TDS Challan Reconciliation at
/// MainWindow.axaml 12052/12067/12088, TCS Challan Reconciliation at 13673/13688/13709).
///
/// <para><b>Why this file exists.</b> These grids declare 670px of FIXED track against a 638px page floor, so
/// at 1024x768 the <c>*</c> column arranges to exactly ZERO and column 5's track hangs 31-43px past the pane
/// clip. Rendered measurement (Skia + real Consolas, worst-case row injected through the real row template)
/// showed that NO DATA IS ACTUALLY CUT: the star is a DEAD SPACER — column 1 has no header text and no row or
/// total cell at all — and every glyph, including the longest status "Matched", lands inside the clip with
/// room to spare. So the site is left unfixed and its allow-list entry stands (fixing it would buy a
/// horizontal scrollbar to reveal blank space; see the entry's comment).</para>
///
/// <para><b>What that leaves.</b> Two ASSUMPTIONS holding the "harmless" verdict up, neither self-evident
/// from the XAML, both one careless edit from becoming the catastrophic defect the allow-list originally
/// claimed: (A) column 1 stays EMPTY, and (B) the columns that do carry content keep enough track to hold it.
/// This file pins both, so the verdict cannot rot silently.</para>
///
/// <para><b>Sites are located STRUCTURALLY, never by the width string.</b> A lock that finds its sites by
/// matching <c>"140,*,140,140,140,110"</c> would stop matching the instant someone edited a width — i.e. it
/// would go vacuous exactly when it was needed. These sites are found by their enclosing report
/// <c>DataTemplate</c>'s <c>x:DataType</c>, and every width below is then read from the site's OWN declared
/// <c>ColumnDefinitions</c>.</para>
///
/// <para><b>Shape.</b> Static XAML only — no rendering, no Skia, no <c>TextLayout</c>/<c>TextRuns</c>/
/// <c>FormattedText</c> — so it is green on CI runners with no SkiaSharp. The floors it asserts were derived
/// from rendered measurement (recorded below) but are checked here as declared tracks.</para>
///
/// <para><b>PER-TRACK, NEVER A SUM.</b> Every assertion names a single column. A total is deliberately never
/// asserted: a sum lock is satisfiable by redistribution (shrink the money column, grow the spacer, total
/// unchanged) and that exact hole let five width regressions ship green.</para>
/// </summary>
public sealed class ChallanReconColumnLockTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The two report view-models whose templates own the six sites.</summary>
    private static readonly string[] ReportDataTypes =
    {
        "vm:ChallanReconciliationViewModel",
        "vm:TcsChallanReconciliationViewModel",
    };

    private static string AxamlPath([CallerFilePath] string thisFile = "")
        => Path.Combine(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")),
            "src", "Apex.Desktop", "Views", "MainWindow.axaml");

    private static readonly Lazy<XDocument> Doc = new(() =>
        XDocument.Load(AxamlPath(), LoadOptions.SetLineInfo));

    private static int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;

    /// <summary>A located site: the Grid plus the widths IT declares (-1 marks a "*" track).</summary>
    private readonly record struct Site(XElement Grid, string Spec, double[] Tracks)
    {
        public int Line => ((System.Xml.IXmlLineInfo)Grid).LineNumber;
    }

    private static double[] Parse(string spec) => spec.Split(',')
        .Select(t => t.Trim())
        .Select(t => t.EndsWith('*')
            ? -1.0
            : double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
        .ToArray();

    /// <summary>
    /// The six report-table Grids, found by walking the two report DataTemplates and taking every Grid that
    /// declares columns. Deliberately width-agnostic: editing a track cannot hide a site from this lock.
    /// </summary>
    private static List<Site> Sites()
    {
        var sites = new List<Site>();

        foreach (var dataType in ReportDataTypes)
        {
            var template = Doc.Value.Root!.DescendantsAndSelf().FirstOrDefault(e =>
                e.Name == Av + "DataTemplate" && (string?)e.Attribute(X + "DataType") == dataType);
            Assert.True(template is not null,
                $"No <DataTemplate x:DataType=\"{dataType}\"> found in MainWindow.axaml. The Challan "
                + "Reconciliation report template was renamed or removed; re-locate the sites and re-measure "
                + "them by RENDER before editing this file.");

            foreach (var grid in template!.DescendantsAndSelf().Where(e => e.Name == Av + "Grid"))
            {
                if ((string?)grid.Attribute("ColumnDefinitions") is not { } spec) continue;
                sites.Add(new Site(grid, spec, Parse(spec)));
            }
        }

        return sites;
    }

    /// <summary>NON-VACUITY. Six sites is the measured, exhaustive count: a header, row and total twin for
    /// each of the two screens. Every other clause iterates this set, so if it ever empties they all pass
    /// trivially — this is the floor that stops that.</summary>
    [Fact]
    public void The_six_sites_still_exist()
    {
        var sites = Sites();
        Assert.True(sites.Count == 6,
            $"Expected exactly 6 column-declaring Grids inside the TDS + TCS Challan Reconciliation report "
            + $"templates (header/row/total twins) but found {sites.Count}. If the screens were legitimately "
            + "restructured, re-measure them by RENDER and update this file and the OverflowingGridAllowList "
            + "entry together — do not just adjust this number.\n  "
            + string.Join("\n  ", sites.Select(s => $"MainWindow.axaml({s.Line}): \"{s.Spec}\"")));
    }

    /// <summary>All six twins must keep declaring the SAME six columns — the header/row/total alignment
    /// contract. (XamlLayoutInvariantTests Invariant 4 pairs header with row; the total band is a third twin
    /// it does not reach, so it is pinned here.)</summary>
    [Fact]
    public void All_six_twins_declare_the_same_columns()
    {
        var sites = Sites();
        Assert.Equal(6, sites.Count);

        var distinct = sites.Select(s => s.Spec.Replace(" ", "")).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(distinct.Count == 1,
            $"The six Challan Reconciliation twins no longer agree on their columns ({distinct.Count} distinct "
            + "specs). A header that stops sitting over its data is the \"misaligned\" defect class:\n  "
            + string.Join("\n  ", sites.Select(s => $"MainWindow.axaml({s.Line}): \"{s.Spec}\"")));

        foreach (var s in sites)
            Assert.True(s.Tracks.Length == 6,
                $"MainWindow.axaml({s.Line}) declares {s.Tracks.Length} columns (\"{s.Spec}\"), not 6. The "
                + "floors in this file are indexed by column, so the shape must hold.");
    }

    // ---------------------------------------------------------------------------------------------
    // (A) The dead-spacer assumption: column 1 carries NOTHING.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE LOAD-BEARING ASSUMPTION. The <c>*</c> column arranges to ZERO at the 1024 page floor (measured).
    /// That is tolerable ONLY because nothing is in it. A cell added to column 1 — or a non-empty Text on the
    /// header placeholder — would be laid out into zero px and its glyphs never produced: invisible, with no
    /// ellipsis and no scrollbar to recover it. That is the catastrophic-tier defect this spec was originally
    /// (mis)filed under, and this is the assertion that stops it arriving for real.
    /// </summary>
    [Fact]
    public void Column_1_is_a_dead_spacer_and_must_stay_empty()
    {
        var sites = Sites();
        Assert.Equal(6, sites.Count);

        var offenders = new List<string>();

        foreach (var site in sites)
        {
            foreach (var child in site.Grid.Elements())
            {
                var col = int.TryParse((string?)child.Attribute("Grid.Column") ?? "0", out var c) ? c : 0;
                if (col != 1) continue;

                var text = (string?)child.Attribute("Text");
                // The header's placeholder TextBlock (Text="") is the ONLY permitted occupant.
                if (child.Name == Av + "TextBlock" && text == string.Empty && !child.HasElements) continue;

                offenders.Add($"MainWindow.axaml({Line(child)}): <{child.Name.LocalName}> in Grid.Column=1 "
                    + $"(Text={text ?? "(none)"})");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} element(s) occupy column 1 of the Challan Reconciliation tables. That column "
            + "arranges to ZERO width at the 1024x768 page floor (measured by render), so ANY content placed "
            + "there is laid out into zero px — the glyphs are never produced, and neither TextTrimming nor a "
            + "scrollable ancestor can recover them. If this column must carry data, the grid needs "
            + "re-budgeting FIRST (and the OverflowingGridAllowList entry re-measured), not a cell dropped "
            + "into it:\n  " + string.Join("\n  ", offenders));
    }

    // ---------------------------------------------------------------------------------------------
    // (B) Per-track floors for the columns that DO carry content.
    // ---------------------------------------------------------------------------------------------
    //
    // MEASURED NEEDS (real Consolas advances: 11.5pt = 6.3228px, 12pt = 6.5977px; the font is monospace so
    // width = len x advance exactly). Slot = track - own Margin - Padding; colHdr contributes Padding 8,4.
    //
    //   col 0  Section / Code   header "Section" 47 + 16 pad = 63 | data "194J(b)" 47 + 8 margin = 55  -> 63
    //   col 2  Deducted/Collected, col 3 Deposited, col 4 Remaining (MONEY, IndianFormat.Amount "#,##0.00"):
    //          measured natural for a 17-char "9,99,99,99,999.00" at 12pt = 113px, no margin           -> 113
    //   col 5  Status            header "Status" 40 + 16 = 56 | data "Matched" 45 + 6 margin = 51      -> 56
    //
    // The floors below sit at or above those needs. They are FLOORS, not equalities: widening a column is
    // always safe, so only the shrink direction is locked.

    private static readonly (int Col, double Floor, string Why)[] TrackFloors =
    {
        (0, 140, "Section / Collection-code column: header \"Section\" needs 63px incl. colHdr padding."),
        (2, 140, "MONEY (Deducted / Collected): IndianFormat.Amount needs 113px at 12pt for 17 characters."),
        (3, 140, "MONEY (Deposited): IndianFormat.Amount needs 113px at 12pt for 17 characters."),
        (4, 140, "MONEY (Remaining): IndianFormat.Amount needs 113px at 12pt for 17 characters."),
        (5, 110, "Status column: header needs 56px; the longest value \"Matched\" needs 51px at 11.5pt."),
    };

    /// <summary>
    /// PER-TRACK FLOORS, asserted on EVERY site's OWN declared widths. Each column is judged on its own, so
    /// the only way to satisfy this test is to keep every individual track at least as wide as its measured
    /// content — redistribution (shrink one, grow another, total unchanged) cannot buy compliance.
    /// </summary>
    [Fact]
    public void Every_content_bearing_track_keeps_its_measured_floor()
    {
        var sites = Sites();
        Assert.Equal(6, sites.Count);

        var failures = new List<string>();

        foreach (var site in sites)
        {
            if (site.Tracks.Length != 6) continue; // shape is guarded by All_six_twins_declare_the_same_columns

            foreach (var (col, floor, why) in TrackFloors)
            {
                var w = site.Tracks[col];
                if (double.IsNaN(w))
                {
                    failures.Add($"MainWindow.axaml({site.Line}) col{col}: \"{site.Spec}\" — width is not a "
                        + "plain number, so its content budget cannot be checked. {why}");
                    continue;
                }
                if (w < 0)
                {
                    failures.Add($"MainWindow.axaml({site.Line}) col{col}: became a \"*\" track. {why} A star "
                        + "here collapses to zero at the 638px page floor, exactly like column 1 already does.");
                    continue;
                }
                if (w < floor)
                    failures.Add($"MainWindow.axaml({site.Line}) col{col}: declares {w:F0}px, floor is "
                        + $"{floor:F0}px. {why}");
            }

            // The * column must REMAIN a star: turning the dead spacer into a fixed track would hand its width
            // to nothing and push the money columns further off-pane at every window size.
            if (site.Tracks[1] >= 0)
                failures.Add($"MainWindow.axaml({site.Line}) col1: is no longer a \"*\" column (declares "
                    + $"{site.Tracks[1]:F0}px). It is the flexible spacer that lets the money columns spread at "
                    + "wide windows; pinning it to a fixed width guarantees overflow at every size.");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} Challan Reconciliation track(s) fell below their measured content floor. These "
            + "six grids ALREADY commit 670px of fixed track inside a 638px page floor, so the column that "
            + "gives way is the trailing one, off-pane and unreachable — shrinking a track here does not "
            + "create room, it moves the loss onto real data. Re-measure by render before changing this:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The money columns must be IDENTICALLY sized, on every twin. They sit side by side under Deducted /
    /// Deposited / Remaining and are read across the row; a per-column drift is the "misaligned" defect class,
    /// and it is also the shape a redistribution attack takes (shave one money column, feed another).
    /// </summary>
    [Fact]
    public void The_three_money_tracks_stay_equal()
    {
        var sites = Sites();
        Assert.Equal(6, sites.Count);

        var drifted = sites
            .Where(s => s.Tracks.Length == 6 && !(s.Tracks[2] == s.Tracks[3] && s.Tracks[3] == s.Tracks[4]))
            .ToList();

        Assert.True(drifted.Count == 0,
            $"{drifted.Count} Challan Reconciliation grid(s) have money tracks that drifted apart. They carry "
            + "Deducted/Collected, Deposited and Remaining — figures compared across the row — and must stay "
            + "equal:\n  " + string.Join("\n  ", drifted.Select(s =>
                $"MainWindow.axaml({s.Line}): col2={s.Tracks[2]:F0} col3={s.Tracks[3]:F0} col4={s.Tracks[4]:F0} "
                + $"(\"{s.Spec}\")")));
    }
}
