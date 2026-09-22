using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>COLUMN LOCK for the GSTR-1 report grid</b> — the shared B2B / B2C / rate-wise / HSN table, and in particular
/// the two Table-12 money cells this slice added: <b>Cess</b>, and the <b>Total Value</b> that rides the filed JSON.
///
/// <para><b>Why this file exists.</b> Adding the statutory Cess column did not widen the grid — the 950px MinWidth
/// floor is load-bearing (raising it is what once scrolled IGST off behind the shortcut bar), so the 90px Cess track
/// was paid for by SHRINKING four existing columns: GSTIN/Description 150→120, Invoice/UQC 150→115, POS/Qty 90→75
/// and Taxable 130→120. That change justified itself with a SUM — "the fixed budget is unchanged at 850px" — which
/// is precisely the reasoning <see cref="ChallanReconColumnLockTests"/> records as the hole that let five width
/// regressions ship green: a sum is satisfiable by redistribution, and redistribution is exactly what happened.
/// This lock is therefore PER-TRACK and per-header, never a total.</para>
///
/// <para><b>What the shrink actually cost, measured.</b> The <c>colHdr</c> style is 12pt Consolas (advance
/// 6.5977px, monospace ⇒ width = len × advance) with Padding 8,4, so a header's slot is <c>track − 16</c>:
/// <list type="bullet">
///   <item>"GSTIN / Description" is 19 chars = 125.4px against a 104px slot — it ELLIPSIZED.</item>
///   <item>"POS / Qty" is 9 chars = 59.4px against a 59px slot — it ellipsized by 0.4px.</item>
/// </list>
/// Both are fixed by shortening the LABEL rather than the track, because the track is already spoken for. This test
/// pins that: every header must fit its own slot, computed from the grid's own declared widths.</para>
///
/// <para><b>Shape.</b> Static XAML only — no rendering, no Skia — so it is green on the ubuntu and macos legs of the
/// gate as well as windows. The advances are the same measured Consolas figures the challan lock records.</para>
/// </summary>
public sealed class Gstr1HsnColumnLockTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    /// <summary>Measured Consolas advances: the colHdr style is 12pt, the data cells 11.5pt.</summary>
    private const double HeaderAdvance = 6.5977;
    private const double CellAdvance = 6.3228;

    /// <summary>colHdr declares Padding="8,4", so a header loses 16px of its track to padding.</summary>
    private const double HeaderPadding = 16;

    private static string AxamlPath([CallerFilePath] string thisFile = "")
        => Path.Combine(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")),
            "src", "Apex.Desktop", "Views", "MainWindow.axaml");

    private static readonly Lazy<XDocument> Doc = new(() =>
        XDocument.Load(AxamlPath(), LoadOptions.SetLineInfo));

    private static int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;

    private static double[] Parse(string spec) => spec.Split(',')
        .Select(t => t.Trim())
        .Select(t => t.EndsWith('*')
            ? -1.0
            : double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
        .ToArray();

    /// <summary>
    /// The GSTR-1 grids: located by the header row that declares the "Party / HSN" column, then by the sibling
    /// data Grid that declares the SAME number of tracks. Found by CONTENT, not by line number, so an edit
    /// elsewhere in the 12k-line file cannot silently move this lock off its subject.
    /// </summary>
    private static List<(XElement Grid, string Spec, double[] Tracks)> Sites()
    {
        var sites = new List<(XElement, string, double[])>();
        foreach (var grid in Doc.Value.Root!.DescendantsAndSelf().Where(e => e.Name == Av + "Grid"))
        {
            if ((string?)grid.Attribute("ColumnDefinitions") is not { } spec) continue;

            // The GSTR-1 table is the one whose first column is the shared "Party / HSN" header, or whose
            // template binds Col9 — the cell that exists only for this grid's Cess column.
            var isHeader = grid.Elements().Any(e =>
                e.Name == Av + "TextBlock" && (string?)e.Attribute("Text") == "Party / HSN");
            // DIRECT children only: an ANCESTOR grid also contains these TextBlocks, and matching on
            // Descendants() pulled in an enclosing "*,168" layout grid that declares nothing about this table.
            var isRow = grid.Elements().Any(e =>
                e.Name == Av + "TextBlock" && ((string?)e.Attribute("Text"))?.Contains("Col9") == true);

            if (isHeader || isRow) sites.Add((grid, spec, Parse(spec)));
        }
        return sites;
    }

    /// <summary>
    /// NON-VACUITY. Two sites — the header twin and the row twin — is the measured count. Every other clause
    /// iterates this set, so an empty result would make them all pass trivially.
    /// </summary>
    [Fact]
    public void Both_gstr1_column_sites_are_found()
    {
        var sites = Sites();
        Assert.True(sites.Count == 2,
            $"Expected the GSTR-1 header and row Grids, found {sites.Count}. The template was renamed or the "
            + "\"Party / HSN\" header changed; re-locate the sites before editing this file.");
    }

    /// <summary>
    /// The header and row twins declare IDENTICAL tracks. If they drift, every value below the drift point is
    /// printed under the WRONG heading — a filed cess figure reading as IGST, and nothing on screen says so.
    /// </summary>
    [Fact]
    public void The_header_and_row_twins_declare_the_same_nine_columns()
    {
        var specs = Sites().Select(s => s.Spec).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(specs.Count == 1,
            "The GSTR-1 header and row Grids declare DIFFERENT columns, so every value lands under the wrong "
            + "heading: " + string.Join(" || ", specs));

        var tracks = Sites()[0].Tracks;
        Assert.True(tracks.Length == 9,
            $"The GSTR-1 grid declares {tracks.Length} tracks, expected 9 (Party/HSN, GSTIN/Desc, Invoice/UQC, "
            + "POS/Qty, Taxable, CGST, SGST, IGST, Cess). Adding a tenth is NOT a free edit — see the class "
            + "remarks: the 850px fixed budget sits inside a 950px floor and has no slack left.");
    }

    /// <summary>
    /// 🔴 <b>EVERY HEADER FITS ITS OWN SLOT.</b> Computed from the grid's declared width, so shrinking a track
    /// without shortening its label fails here rather than at a user's screen. This is the clause that catches
    /// the "GSTIN / Description" and "POS / Qty" truncations the Cess rebalance introduced.
    /// </summary>
    [Fact]
    public void Every_column_header_fits_inside_its_own_declared_track()
    {
        var site = Sites().Single(s => s.Grid.Elements().Any(e =>
            e.Name == Av + "TextBlock" && (string?)e.Attribute("Text") == "Party / HSN"));

        var failures = new List<string>();
        foreach (var tb in site.Grid.Elements().Where(e => e.Name == Av + "TextBlock"))
        {
            var text = (string?)tb.Attribute("Text");
            if (string.IsNullOrEmpty(text)) continue;

            var col = int.Parse((string?)tb.Attribute(Av + "Grid.Column")
                                ?? (string?)tb.Attribute("Grid.Column") ?? "0", CultureInfo.InvariantCulture);
            var track = site.Tracks[col];
            if (track < 0) continue;   // the star column flexes; it is not a fixed budget

            var need = text.Length * HeaderAdvance;
            var slot = track - HeaderPadding;
            if (need > slot)
                failures.Add($"MainWindow.axaml({Line(tb)}) col{col}: header \"{text}\" needs {need:F1}px "
                    + $"({text.Length} chars x {HeaderAdvance}px at 12pt) but its slot is {slot:F0}px "
                    + $"(track {track:F0} - {HeaderPadding} padding). SHORTEN THE LABEL — the tracks are "
                    + "already fully committed, so widening this one truncates another.");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} GSTR-1 column header(s) are truncated by their own track:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// 🔴 <b>PER-TRACK FLOORS for the FILED money columns.</b> Named one at a time, so redistribution cannot buy
    /// compliance. The floors are the widths these columns actually declare today; they lock the SHRINK direction
    /// only, because widening is always safe.
    ///
    /// <para><b>Cess is called out separately and deliberately.</b> At a 90px track less its 10px right margin it
    /// holds 80px = 12 characters at the 11.5pt cell advance, so it states up to "99,99,999.99" in full and
    /// ELLIPSIZES a cess of ₹1 crore or more, where its 110px IGST/CGST/SGST siblings would not. That is recorded
    /// here as a known, measured limit rather than left for a filer to discover — see the report for this slice.</para>
    /// </summary>
    [Theory]
    [InlineData(4, 120, "Taxable — a filed Table-12 money cell")]
    [InlineData(5, 110, "CGST — a filed money cell")]
    [InlineData(6, 110, "SGST — a filed money cell")]
    [InlineData(7, 110, "IGST — a filed money cell")]
    [InlineData(8, 90, "Cess — the Table-12 statutory cess cell")]
    public void Each_filed_money_column_keeps_its_track(int col, double floor, string why)
    {
        foreach (var site in Sites())
        {
            var w = site.Tracks[col];
            Assert.False(double.IsNaN(w),
                $"MainWindow.axaml({Line(site.Grid)}) col{col} ({why}): width is not a plain number, so its "
                + "content budget cannot be checked.");
            Assert.False(w < 0,
                $"MainWindow.axaml({Line(site.Grid)}) col{col} ({why}): became a \"*\" track, which collapses "
                + "at the 950px floor and takes a filed figure with it.");
            Assert.True(w >= floor,
                $"MainWindow.axaml({Line(site.Grid)}) col{col} ({why}): declares {w:F0}px, floor is {floor:F0}px. "
                + "Shrinking a filed money column does not create room — it moves the loss onto a figure a "
                + "taxpayer FILES. Re-measure by render before changing this.");
        }
    }

    /// <summary>
    /// The Cess cell's capacity, stated as arithmetic rather than prose so it cannot rot: the declared track,
    /// less the 10px right margin the template gives it, divided by the measured 11.5pt advance. If a future
    /// edit widens Cess this test still passes — it asserts the floor, not the exact figure.
    /// </summary>
    [Fact]
    public void The_cess_cell_states_at_least_a_ninety_nine_lakh_figure_in_full()
    {
        var track = Sites()[0].Tracks[8];
        var slot = track - 10;                       // Margin="0,0,10,0" on the Col9 TextBlock
        var characters = Math.Floor(slot / CellAdvance);

        // "99,99,999.99" is 12 characters — the largest cess this cell can state without an ellipsis today.
        Assert.True(characters >= 12,
            $"The Cess column declares {track:F0}px, leaving a {slot:F0}px slot = {characters} characters at "
            + $"{CellAdvance}px. It can no longer state even \"99,99,999.99\" (12 chars) in full, so a filed "
            + "cess figure is silently abbreviated on screen.");
    }
}
