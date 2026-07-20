using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Apex.Desktop.Tests;

/// <summary>
/// PER-TRACK FLOORS for the three grids fixed in the D-1 / D-2 CA-audit round: the <b>Gratuity Provision
/// Register</b>, the <b>Statutory Bonus Register</b> and the <b>Ledger master list</b>.
///
/// <para><b>What was actually wrong (measured; Skia + real Consolas, headless, sanity-tell verified —
/// 20x'M' = 121 / 127 / 132 / 138px at 11 / 11.5 / 12 / 12.5pt, i.e. absolute advances 6.0479 / 6.3228 /
/// 6.5977 / 6.8726, font confirmed monospace and weight-independent).</b></para>
///
/// <para><b>D-1 was NOT an overprint.</b> The audit brief described over-long <c>colHdr</c> headers as
/// "overflowing and painting on top of the adjacent header". Rendered ink-scanning disproves that: Avalonia's
/// <c>TextBlock</c> self-clips to its arranged bounds, so a 16-character string in a 42px slot produced ink
/// stopping ONE PIXEL INSIDE its own right edge, and zero ink runs crossed a track boundary in either grid at
/// any of five viewports. The real class is a <b>silent hard cut</b> — sliced mid-glyph at the track edge with
/// no ellipsis, no wrap and no cue.</para>
///
/// <para><b>The live defect was in the ROWS, not the headers.</b> With a worst-case employee number injected
/// through the REAL row template, both registers cut it with <c>TextTrimming=None</c>:
/// Gratuity <c>"EMP/2011/00417"</c> slot 60 vs natural 89 = <b>CUT 29px</b>; Bonus <c>"EMP/1998/99999"</c>
/// slot 54 vs 89 = <b>CUT 35px</b>. A 14-character number was chopped to ~9 characters with no cue, while the
/// name column beside it ellipsized honestly. The six header overflows the brief named (Gratuity Emp. No. 5 /
/// Years 5 / Vested 6; Bonus Emp. No. 11 / Capped Base 3 / Eligible 1) all reproduced exactly, but five of the
/// six sat inside the 8px <c>colHdr</c> Padding buffer and so were LATENT, not visible.</para>
///
/// <para><b>D-2 is a financial misread and it was live.</b> <c>LedgerMasterViewModel.RefreshList</c> formats
/// Opening as <c>IndianFormat.Amount(...)</c> + a <c>" Dr"</c>/<c>" Cr"</c> SUFFIX the 110px column was never
/// sized for. Measured identically at 1920x1080 / 1280x720 / 1024x768: <c>"99,99,999.99 Cr"</c> (15 ch, 99px)
/// fit with 1px to spare, but <c>"1,00,00,000.00 Cr"</c> (17 ch, 113px) was CUT 13px and
/// <c>"99,99,99,999.99 Cr"</c> (18 ch, 119px) CUT 19px. <b>At and above ₹1 crore the Dr/Cr marker was silently
/// deleted</b>, so a credit opening on a Sundry Creditors ledger read as a debit.</para>
///
/// <para><b>Neither brief attribution for D-2 survived.</b> The GST set-off cells are <c>statCell</c> strips,
/// not a <c>colHdr</c> grid, and carry ~33px of headroom at 1024x768 — no cut. The "Batch/Lot report's Opening
/// column" does not exist: that report is Item | Batch | Mfg | Expiry | Godown | Inward | Outward | Closing |
/// Value. The Ledger master list is the real site.</para>
///
/// <para><b>Shape.</b> Static XAML only — no rendering, no Skia, no <c>TextLayout</c>/<c>TextRuns</c>/
/// <c>FormattedText</c>/<c>CaptureRenderedFrame</c> — so it is green on the 3-OS CI runners, which have no
/// SkiaSharp. Every floor below was derived from RENDERED measurement (recorded per column) but is checked
/// here as a declared track.</para>
///
/// <para><b>Sites are located STRUCTURALLY by HEADER TEXT, never by the width string.</b> A lock that found
/// its sites by matching <c>"*,64,82,44,50,80,96"</c> would stop matching the instant someone edited a width —
/// i.e. it would go vacuous exactly when it was needed. Each grid is found by its enclosing page
/// <c>DataTemplate</c>'s <c>x:DataType</c> plus its exact ordered header texts; every width is then read from
/// the site's OWN declared <c>ColumnDefinitions</c>.</para>
///
/// <para><b>PER-TRACK, NEVER A SUM.</b> Every width assertion names a single column. The only total that
/// appears is the ONE-SIDED anti-starvation bound in
/// <see cref="Star_column_keeps_its_full_width_after_the_widening"/> (grid Width minus the fixed tracks must
/// leave the <c>*</c> column at least its measured floor) — which is precisely the guard that stops a future
/// edit paying for a fixed track out of the star. A two-sided sum lock is deliberately never asserted: it is
/// satisfiable by redistribution (shrink the money column, grow its neighbour, total unchanged) and that exact
/// hole let five width regressions ship green.</para>
/// </summary>
public sealed class StatutoryRegisterAndOpeningColumnLockTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AxamlPath([CallerFilePath] string thisFile = "")
        => Path.Combine(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")),
            "src", "Apex.Desktop", "Views", "MainWindow.axaml");

    private static readonly Lazy<XDocument> Doc = new(() =>
        XDocument.Load(AxamlPath(), LoadOptions.SetLineInfo));

    private static int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;

    // ---------------------------------------------------------------- site model

    private readonly record struct Site(XElement Grid, string Spec, double[] Tracks)
    {
        public int Line => ((System.Xml.IXmlLineInfo)Grid).LineNumber;
    }

    /// <summary>-1 marks a "*" track; NaN marks an unparseable one.</summary>
    private static double[] Parse(string spec) => spec.Split(',')
        .Select(t => t.Trim())
        .Select(t => t.EndsWith('*')
            ? -1.0
            : double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
        .ToArray();

    private static XElement PageTemplate(string dataType)
    {
        var t = Doc.Value.Root!.DescendantsAndSelf().FirstOrDefault(e =>
            e.Name == Av + "DataTemplate" && (string?)e.Attribute(X + "DataType") == dataType);
        Assert.True(t is not null,
            $"No <DataTemplate x:DataType=\"{dataType}\"> found in MainWindow.axaml. That page template was "
            + "renamed or removed; re-locate the site and RE-MEASURE it by render before editing this file.");
        return t!;
    }

    private static int ColumnOf(XElement tb)
        => int.TryParse((string?)tb.Attribute(Av + "Grid.Column") ?? (string?)tb.Attribute("Grid.Column"),
            out var c) ? c : 0;

    private static bool HasClass(XElement e, string cls)
        => ((string?)e.Attribute("Classes"))?.Split(' ').Contains(cls) == true;

    /// <summary>
    /// The report grid inside <paramref name="dataType"/>'s page template whose <c>colHdr</c> children read
    /// exactly <paramref name="headers"/>, in order — plus its ROW twin, the Grid inside
    /// <paramref name="rowDataType"/>'s DataTemplate that declares the same number of columns.
    /// </summary>
    private static (Site Header, Site Row) Twins(string dataType, string rowDataType, string[] headers)
    {
        var page = PageTemplate(dataType);

        var header = page.DescendantsAndSelf()
            .Where(e => e.Name == Av + "Grid" && e.Attribute("ColumnDefinitions") is not null)
            .FirstOrDefault(g => g.Elements(Av + "TextBlock").Where(t => HasClass(t, "colHdr"))
                .OrderBy(ColumnOf).Select(t => (string?)t.Attribute("Text")).SequenceEqual(headers));
        Assert.True(header is not null,
            $"No Grid inside <DataTemplate x:DataType=\"{dataType}\"> carries exactly the colHdr headers "
            + $"[{string.Join(" | ", headers)}]. Either the report was restructured or a header was RENAMED. "
            + "Both mean the measured floors below no longer describe this screen: re-measure by render "
            + "(header natural width AND worst-case data through the real row template) before touching this "
            + "file. Do NOT just edit the header list to make this pass.");

        var rowTemplate = Doc.Value.Root!.DescendantsAndSelf().FirstOrDefault(e =>
            e.Name == Av + "DataTemplate" && (string?)e.Attribute(X + "DataType") == rowDataType);
        Assert.True(rowTemplate is not null,
            $"No <DataTemplate x:DataType=\"{rowDataType}\"> found — the row template for the "
            + $"{dataType} report is gone, so its header/row column alignment cannot be checked.");

        var hSpec = (string)header!.Attribute("ColumnDefinitions")!;
        var row = rowTemplate!.DescendantsAndSelf()
            .Where(e => e.Name == Av + "Grid" && e.Attribute("ColumnDefinitions") is not null)
            .FirstOrDefault(g => Parse((string)g.Attribute("ColumnDefinitions")!).Length == Parse(hSpec).Length);
        Assert.True(row is not null,
            $"<DataTemplate x:DataType=\"{rowDataType}\"> declares no Grid with the same column count as its "
            + "header twin. The row template no longer lines up with its own header.");

        var rSpec = (string)row!.Attribute("ColumnDefinitions")!;
        return (new Site(header, hSpec, Parse(hSpec)), new Site(row, rSpec, Parse(rSpec)));
    }

    // ---------------------------------------------------------------- the three sites

    private static readonly string[] GratuityHeaders =
        { "Employee", "Emp. No.", "Joined", "Years", "Vested", "Basic+DA", "Accrued" };

    private static readonly string[] BonusHeaders =
        { "Employee", "Emp. No.", "Eligible", "Basic+DA", "Capped Base", "Rate", "Annual Bonus" };

    private static readonly string[] LedgerHeaders =
        { "Ledger", "Under", "Opening", "Currency", "Interest" };

    private static (Site Header, Site Row) Gratuity() => Twins(
        "vm:GratuityProvisionRegisterViewModel", "vm:GratuityRegisterRowVm", GratuityHeaders);

    private static (Site Header, Site Row) Bonus() => Twins(
        "vm:BonusRegisterViewModel", "vm:BonusRegisterRowVm", BonusHeaders);

    private static (Site Header, Site Row) Ledger() => Twins(
        "vm:LedgerMasterViewModel", "vm:LedgerListRow", LedgerHeaders);

    /// <summary>
    /// A single measured per-track floor. <c>Need</c> records WHY, so a later reader can re-derive it without
    /// re-running the probe.
    /// </summary>
    private readonly record struct Floor(int Col, string Name, double Min, string Need);

    // Gratuity — header Padding 8,4 (=16 horizontal); row cells Margin 4 except col0 (12) and col6 (8).
    private static readonly Floor[] GratuityFloors =
    {
        new(1, "Emp. No.", 95, "data \"EMP/2011/00417\" 89 + 4 margin = 93, +2 headroom (header needs only 69)"),
        new(2, "Joined",   76, "data \"30-Apr-2016\" 70 + 4 = 74, +2"),
        new(3, "Years",    51, "header 33 + 16 padding = 49, +2 (data \"11\" needs 17)"),
        new(4, "Vested",   58, "header 40 + 16 = 56, +2 (data \"Yes\" needs 23)"),
        new(5, "Basic+DA", 71, "header 53 + 16 = 69, +2"),
        new(6, "Accrued",  65, "header 47 + 16 = 63, +2"),
    };

    private static readonly Floor[] BonusFloors =
    {
        new(1, "Emp. No.",     95, "data \"EMP/1998/99999\" 89 + 4 margin = 93, +2 headroom"),
        new(2, "Eligible",     71, "header 53 + 16 padding = 69, +2 (was 68 = 1px over, the artifact band)"),
        new(3, "Basic+DA",     71, "header 53 + 16 = 69, +2"),
        new(4, "Capped Base",  91, "header 73 + 16 = 89, +2"),
        new(5, "Rate",         45, "header 27 + 16 = 43, +2. A PERCENTAGE column, not money: the widest value the "
                              + "8.33%-20% statutory band can produce is 6 chars = 38px against a 46px slot. This is the one "
                              + "track that SHRANK (54 -> 50), to keep the grid under the 638px C5b page floor."),
        new(6, "Annual Bonus", 98, "header 80 + 16 = 96, +2 (was 96 = ZERO headroom)"),
    };

    // Ledger master list — row cells: Under bare, Opening numCell (8px inset), Currency bare, Interest 10px.
    private static readonly Floor[] LedgerFloors =
    {
        new(1, "Under",    150, "header 33 + 16 = 49; but the longest group name \"Loans & Advances (Asset)\" "
                              + "needs 159, so 150 is ALREADY 9px short and ellipsizes. Floored at its current "
                              + "width: it has no slack to donate and must never be shrunk further."),
        new(2, "Opening",  129, "data \"99,99,99,999.99 Cr\" (18 ch, the ₹99.99 crore ceiling) 119 + 8 numCell "
                              + "inset = 127, +2 headroom. THIS IS THE D-2 FIX: 110 -> 130."),
        new(3, "Currency",  70, "header 53 + 16 = 69, so 70 leaves 1px — inside the +/-1px artifact band, "
                              + "deliberately not widened (it would cost the star column for no visible gain). "
                              + "Floored so it cannot shrink into a real cut."),
        new(4, "Interest",  71, "header 53 + 16 = 69, +2. Data (\"18.75% p.m.-basis Compound\") overruns 120 but "
                              + "ellipsizes honestly."),
    };

    // ================================================================ non-vacuity

    /// <summary>
    /// NON-VACUITY. Every clause below iterates these three sites; if the locator silently stopped matching,
    /// they would all pass trivially. This is the floor that stops that — and it also pins the header TEXT of
    /// all nineteen columns, so renaming a header (which changes its natural width, and therefore every floor
    /// derived from it) fails here rather than rotting quietly.
    /// </summary>
    [Fact]
    public void All_three_sites_still_exist_with_their_measured_headers()
    {
        var (gh, gr) = Gratuity();
        var (bh, br) = Bonus();
        var (lh, lr) = Ledger();

        Assert.Equal(7, gh.Tracks.Length);
        Assert.Equal(7, gr.Tracks.Length);
        Assert.Equal(7, bh.Tracks.Length);
        Assert.Equal(7, br.Tracks.Length);
        Assert.Equal(5, lh.Tracks.Length);
        Assert.Equal(5, lr.Tracks.Length);

        // Column 0 is the flexible one at all three sites; the floors assume that.
        foreach (var (label, site) in new[]
                 {
                     ("Gratuity header", gh), ("Gratuity row", gr),
                     ("Bonus header", bh), ("Bonus row", br),
                     ("Ledger header", lh), ("Ledger row", lr),
                 })
            Assert.True(site.Tracks[0] == -1.0,
                $"{label} (MainWindow.axaml:{site.Line}) column 0 is no longer a \"*\" track — its spec is "
                + $"\"{site.Spec}\". Every floor in this file assumes the FIXED tracks are columns 1..n and "
                + "that column 0 absorbs the slack; re-measure before changing that.");
    }

    /// <summary>
    /// Header and row twins must keep declaring IDENTICAL tracks. Report columns that drift apart put every
    /// figure under the wrong heading — the misread this whole campaign exists to prevent.
    /// </summary>
    [Fact]
    public void Header_and_row_twins_declare_identical_tracks()
    {
        foreach (var (label, twins) in new[]
                 {
                     ("Gratuity Provision Register", Gratuity()),
                     ("Statutory Bonus Register", Bonus()),
                     ("Ledger master list", Ledger()),
                 })
        {
            var (h, r) = twins;
            Assert.True(h.Spec == r.Spec,
                $"{label}: the header grid (MainWindow.axaml:{h.Line}) declares \"{h.Spec}\" but its row twin "
                + $"(MainWindow.axaml:{r.Line}) declares \"{r.Spec}\". Every value would sit under the wrong "
                + "heading. Change BOTH or neither.");
        }
    }

    // ================================================================ per-track floors

    public static IEnumerable<object[]> AllFloors()
    {
        foreach (var f in GratuityFloors) yield return new object[] { "Gratuity", f.Col, f.Name, f.Min, f.Need };
        foreach (var f in BonusFloors) yield return new object[] { "Bonus", f.Col, f.Name, f.Min, f.Need };
        foreach (var f in LedgerFloors) yield return new object[] { "Ledger", f.Col, f.Name, f.Min, f.Need };
    }

    /// <summary>
    /// 🔴 THE FLOORS. One assertion per column, on BOTH twins. Each floor is
    /// <c>max(headerNatural + 16px Padding, dataNatural + own Margin)</c> plus the >=2px headroom the fixed-track
    /// rule requires (Avalonia adds ~+1px per fixed track when a Grid has slack, so a 1px margin is not a
    /// margin). No total is asserted here, so a redistribution that shrinks one of these columns to feed
    /// another fails on the shrunk column no matter what it did to the sum.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFloors))]
    public void Each_column_keeps_the_width_its_content_was_measured_to_need(
        string grid, int col, string name, double min, string need)
    {
        var twins = grid switch
        {
            "Gratuity" => Gratuity(),
            "Bonus" => Bonus(),
            _ => Ledger(),
        };

        foreach (var (kind, site) in new[] { ("header", twins.Header), ("row", twins.Row) })
        {
            var w = site.Tracks[col];
            Assert.False(double.IsNaN(w),
                $"{grid} {kind} (MainWindow.axaml:{site.Line}) column {col} (\"{name}\") is not a parseable "
                + $"fixed width in \"{site.Spec}\".");
            Assert.True(w >= min,
                $"{grid} {kind} grid (MainWindow.axaml:{site.Line}) column {col} \"{name}\" is {w}px, below its "
                + $"measured floor of {min}px.\n  WHY THAT FLOOR: {need}\n  Declared spec: \"{site.Spec}\"\n"
                + "  This column's content was measured by RENDER (Skia + real Consolas, worst-case data "
                + "injected through the real row template). If you need the width elsewhere, do NOT take it "
                + "from here — take it from the \"*\" column by growing the grid, which is what the D-1/D-2 fix "
                + "did. If the content genuinely shrank, re-measure and move the floor deliberately.");
        }
    }

    // ================================================================ anti-starvation

    /// <summary>
    /// 🔴 ANTI-STARVATION — the one place a total appears, and only as a ONE-SIDED bound.
    ///
    /// <para>The D-1 fix widened three Gratuity tracks (+48) and four Bonus tracks (+49) and paid for BOTH out
    /// of the grid's own declared <c>Width</c>, NOT out of the <c>*</c> Employee column. The Widths then went
    /// further still — 560 -> <b>614</b> and 560 -> <b>637</b> — to lift that star column from its old 144px /
    /// 118px to <b>150px</b>, the readability floor in
    /// <c>XamlLayoutInvariantTests.MinimumStarWidth</c>. That RETIRED both grids' entries from
    /// <c>StarvedStarAllowList</c> (a list whose contract is that it may only shrink); the alternative —
    /// re-keying those entries to the new width strings — would have been ADDING allow-list entries to
    /// suppress a failure, which is forbidden.</para>
    ///
    /// <para><b>What it costs.</b> Gratuity is free: 614px still fits the register ScrollViewer's 626px
    /// viewport at the 1024x768 floor (814 / 900 / 1134 / 1454 above it), so maxOffset stays 0 at all five
    /// sizes. Bonus at 637px overruns that 626px viewport at 1024x768 ONLY, by 11px, so a horizontal scrollbar
    /// appears at the narrowest supported window. That scroller is real and user-operable, and BOTH the header
    /// band and the row list sit inside it, so they scroll together and no figure drifts out from under its
    /// heading.</para>
    ///
    /// <para>This asserts the invariant that made it free: <c>Width - sum(fixed tracks) >= the star floor</c>.
    /// Growing a fixed track without growing <c>Width</c> starves the star, and that is the single move this
    /// campaign has shipped five regressions through.</para>
    /// </summary>
    [Theory]
    [InlineData("Gratuity", 614.0, 150.0)]
    [InlineData("Bonus", 637.0, 150.0)]
    public void Star_column_keeps_its_full_width_after_the_widening(string which, double width, double starFloor)
    {
        var twins = which == "Gratuity" ? Gratuity() : Bonus();

        // The declared Width lives on the RowDefinitions="Auto,*" Grid that wraps the header + row list.
        var wrapper = twins.Header.Grid.Ancestors(Av + "Grid")
            .FirstOrDefault(g => (string?)g.Attribute("Width") is not null);
        Assert.True(wrapper is not null,
            $"The {which} register report grid has no Width-bearing ancestor Grid. That Width is what pays for "
            + "the fixed tracks; without it the \"*\" column silently absorbs every future widening.");

        var declared = double.Parse((string)wrapper!.Attribute("Width")!, CultureInfo.InvariantCulture);
        Assert.True(declared >= width,
            $"{which} register grid Width is {declared}px, below the {width}px the measured tracks need "
            + $"(MainWindow.axaml:{Line(wrapper)}). Shrinking it starves the \"*\" Employee column.");

        var fixedSum = twins.Header.Tracks.Skip(1).Sum();
        var star = declared - fixedSum;
        Assert.True(star >= starFloor,
            $"{which} register: declared Width {declared}px minus the fixed tracks ({fixedSum}px) leaves the "
            + $"\"*\" Employee column only {star}px, below its measured floor of {starFloor}px "
            + $"(MainWindow.axaml:{twins.Header.Line}, spec \"{twins.Header.Spec}\").\n"
            + "  A fixed track was widened WITHOUT paying for it out of the grid Width, so the cost landed on "
            + "the employee-name column. Raise Width by the same amount instead — the scroller has room "
            + "(viewport 626px at the 1024x768 floor).");
    }

    // ================================================================ honest degradation

    /// <summary>
    /// 🔴 D-1's LIVE DEFECT. Both registers' <c>EmployeeNumber</c> row cells must keep
    /// <c>TextTrimming="CharacterEllipsis"</c>. The width fix holds a 14-character number (slot 92 vs natural
    /// 89), but an employee number is free text: the ellipsis is what stops the 15th character from vanishing
    /// mid-glyph the way all 14 used to. TextTrimming is a paint property with no geometric footprint, so this
    /// is asserted statically rather than measured.
    /// </summary>
    [Theory]
    [InlineData("vm:GratuityRegisterRowVm")]
    [InlineData("vm:BonusRegisterRowVm")]
    public void Employee_number_cells_degrade_honestly(string rowDataType)
    {
        var template = Doc.Value.Root!.DescendantsAndSelf().First(e =>
            e.Name == Av + "DataTemplate" && (string?)e.Attribute(X + "DataType") == rowDataType);

        var cell = template.Descendants(Av + "TextBlock").FirstOrDefault(t =>
            ((string?)t.Attribute("Text"))?.Contains("EmployeeNumber") == true);
        Assert.True(cell is not null,
            $"No EmployeeNumber cell found in <DataTemplate x:DataType=\"{rowDataType}\">. If the binding was "
            + "renamed, re-point this lock — do not delete it; this cell is the D-1 live defect.");

        Assert.True((string?)cell!.Attribute("TextTrimming") == "CharacterEllipsis",
            $"The EmployeeNumber cell in {rowDataType} (MainWindow.axaml:{Line(cell)}) has TextTrimming="
            + $"\"{(string?)cell.Attribute("TextTrimming") ?? "(unset)"}\". Without CharacterEllipsis an "
            + "over-long employee number is sliced mid-glyph with no cue — measured at 29px (Gratuity) and "
            + "35px (Bonus) of silent loss before this fix.");
    }

    /// <summary>
    /// 🔴 D-1's CLASS BACKSTOP. The shared <c>TextBlock.colHdr</c> style must keep its <c>TextTrimming</c>
    /// setter.
    ///
    /// <para>Measured blast radius across all <b>603</b> colHdr headers in this file (the in-repo "~277"
    /// figure was stale by a factor of two): 6 overflowed by more than 1px, 5 of which are the Gratuity/Bonus
    /// headers this same change fixed by width. The survivor, "Taxable Value" (:1231, 2.00px), sits inside the
    /// 8px Padding buffer and is not visibly cut — so the setter changes ZERO headers visibly today. Its value
    /// is prospective: 147 headers sit in "*" columns and 13 bind their text, none of which has a statically
    /// resolvable slot, and for those it turns a silent mid-glyph cut into an honest ellipsis.</para>
    ///
    /// <para>It is safe because it is LAYOUT-NEUTRAL, measured rather than assumed: DesiredSize 69.00 with the
    /// setter and 69.00 without; arranged width 58.00 either way; and a <c>TextWrapping="Wrap"</c> header is
    /// identical either way (w 49.00, h 37.00), so the 10 headers that already wrap are untouched.</para>
    /// </summary>
    [Fact]
    public void Shared_column_header_style_keeps_its_trimming_backstop()
    {
        var style = Doc.Value.Root!.Descendants(Av + "Style")
            .FirstOrDefault(s => (string?)s.Attribute("Selector") == "TextBlock.colHdr");
        Assert.True(style is not null,
            "The shared <Style Selector=\"TextBlock.colHdr\"> is gone. It carries the D-1 class backstop for "
            + "603 report headers; re-locate it rather than deleting this lock.");

        var setter = style!.Elements(Av + "Setter")
            .FirstOrDefault(s => (string?)s.Attribute("Property") == "TextTrimming");
        Assert.True(setter is not null && (string?)setter.Attribute("Value") == "CharacterEllipsis",
            $"TextBlock.colHdr (MainWindow.axaml:{Line(style)}) no longer sets "
            + "TextTrimming=\"CharacterEllipsis\". Removing it does not move a single pixel of layout — which "
            + "is exactly why its loss would go unnoticed — but it silently restores the silent-hard-cut class "
            + "across all 603 report headers, including the 147 in \"*\" columns that have never been "
            + "rendered or measured.");
    }

    /// <summary>
    /// 🔴 D-2. The Ledger master list's Opening cell must keep <c>Classes="numCell"</c> and must NOT re-acquire
    /// a local Margin or TextAlignment.
    ///
    /// <para>The floor in <see cref="LedgerFloors"/> is arithmetic on an 8px right inset — the one numCell
    /// applies. The cell previously carried an ad-hoc <c>Margin="0,0,10,0"</c>, which both cost 2px of the
    /// width this defect needed AND left the documented 2px drift between the figure and its own right-aligned
    /// header (colHdr's Padding is 8,4). A local Margin would beat the style setter and silently re-open both.</para>
    /// </summary>
    [Fact]
    public void Ledger_opening_cell_uses_the_shared_numeric_inset()
    {
        var template = Doc.Value.Root!.DescendantsAndSelf().First(e =>
            e.Name == Av + "DataTemplate" && (string?)e.Attribute(X + "DataType") == "vm:LedgerListRow");

        var cell = template.Descendants(Av + "TextBlock").FirstOrDefault(t =>
            ((string?)t.Attribute("Text"))?.Contains("Opening") == true);
        Assert.True(cell is not null, "No Opening cell found in <DataTemplate x:DataType=\"vm:LedgerListRow\">.");

        Assert.True(HasClass(cell!, "numCell"),
            $"The Ledger Opening cell (MainWindow.axaml:{Line(cell!)}) lost Classes=\"numCell\". The 130px "
            + "Opening floor is arithmetic on numCell's 8px inset; another inset changes the slot and can "
            + "silently re-open the D-2 cut of the \" Dr\"/\" Cr\" suffix.");

        Assert.True((string?)cell!.Attribute("Margin") is null,
            $"The Ledger Opening cell (MainWindow.axaml:{Line(cell)}) re-acquired a local "
            + $"Margin=\"{(string?)cell.Attribute("Margin")}\". A local value beats the numCell setter: it "
            + "shrinks the measured 122px slot and re-introduces the 2px header/value drift numCell exists to "
            + "remove.");

        Assert.True((string?)cell.Attribute("TextAlignment") is null,
            $"The Ledger Opening cell (MainWindow.axaml:{Line(cell)}) re-acquired a local TextAlignment; "
            + "numCell already right-aligns it, and a local override is how the two drift apart again.");
    }
}
