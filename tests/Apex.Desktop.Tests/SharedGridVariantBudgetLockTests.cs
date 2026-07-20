using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// Locks for three column-budget defects whose common cause is a budget sized against ONE observed case
/// and then trusted for every other case the same markup has to serve.
///
/// <para><b>D-A — a shared grid measured on only one of its two variants.</b> One 8-column grid in
/// MainWindow.axaml renders BOTH the TDS §201(1A) and the TCS §206C(7) interest report; three of its
/// headers are bound to <c>ReportsViewModel.Stat*Header</c> and therefore carry two different strings
/// each. A previous rebalance sized those tracks against the TDS strings only, so on the TCS side
/// <c>Coll. Code</c> (65.98 DIP) sat in a 56px slot and <c>Coll. Date</c> in a 64px slot — cut by 9.98
/// and 1.98px with no ellipsis, at 1024, 1280 and 1920 alike (fixed tracks, so the defect is
/// width-invariant). Both are now 84 (68px slot).</para>
///
/// <para><b>D-B — a money column sized from a lakh-scale figure.</b> The Forex register's base-₹ columns
/// were sized from "12,34,567.89" (12 chars); at crore scale "10,00,00,000.00" (15 chars, 98.96 DIP)
/// overflowed the 92px slot by 6.96px. They went to 108 — and that 100px slot was still only a +1.04 fit,
/// i.e. INSIDE Avalonia's ~+1px-per-fixed-track slack artifact and not a safe fit at all. Now 112.</para>
///
/// <para><b>D-B2 — the lock for D-B was itself an instance of the disease.</b> It pinned cols 3 and 5 and
/// said NOTHING about cols 1, 2, 4 and 6, so the same rebalance that fixed the base-₹ columns cut
/// "Forex Bal." by 20.16 DIP and "Rate" by 6.78 to a fully green suite. Two lessons are now enforced
/// structurally rather than by comment: a lock must cover EVERY column of the grid it guards, header AND
/// data; and a money string must be derived from the REAL formatter at the ceiling designed for —
/// including any suffix. "Forex Bal." is the cautionary case on both counts: its value is built by
/// <c>ForexReportViewModel</c> as <c>Fmt(amount) + " Dr"/" Cr"</c>, where <c>Fmt</c> is "#,##0.##" on
/// INVARIANT culture (western grouping, because it is a FOREIGN-currency amount, not an Indian-grouped
/// rupee one) — so both the grouping and the 3-character suffix differ from the plausible-looking literal
/// a reviewer would type.</para>
///
/// <para><b>D-C — a stat label longer than its equal-star share.</b> On the PF ECR challan summary six
/// labels share a <c>*,*,*,*,*,*</c> grid; at 1024 each column is ~101px (89px slot) and
/// "A/c 22 — EDLI Adm ₹" (109.69 DIP) was cut by 20.69px. The other five fit, so this is a single
/// over-long label, not a pane-budget shortfall — the remedy is to WRAP rather than to widen.</para>
///
/// <para><b>Why nothing here asserts a text width.</b> The committed headless harness resolves no font,
/// so any glyph-advance assertion would measure a fiction. These lock font-independent things: column
/// TRACK widths, arranged <c>Bounds</c> geometry, and static XAML attributes. The DIP figures quoted
/// above were measured out-of-band under a Skia-enabled harness that resolves the real Consolas
/// (6.5977 DIP/glyph at 12pt) and are recorded as commentary only.</para>
/// </summary>
public sealed class SharedGridVariantBudgetLockTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    private static readonly Lazy<XDocument> Doc = new(() =>
    {
        var p = AxamlPath();
        Assert.True(File.Exists(p), $"MainWindow.axaml not found at '{p}'.");
        return XDocument.Load(p, LoadOptions.SetLineInfo);
    });

    private static double[] Tracks(string columnDefinitions) =>
        columnDefinitions.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim() == "*" ? 0d : double.Parse(s.Trim()))
            .ToArray();

    // ================================================================= D-A

    /// <summary>
    /// The per-track floor for the interest grid, from max(HEADER over BOTH variants, DATA worst case),
    /// each measured out-of-band under a Skia harness resolving the real Consolas (6.5977 DIP/glyph at
    /// 12pt; col7's value cell is 12.5pt) and each carrying >= 2px of headroom over Avalonia's +-1px
    /// per-fixed-track rounding artifact.
    ///
    /// <para>Index 0 is the "*" Party column and is not a fixed track — it is locked separately, at
    /// runtime, by <see cref="Interest_grid_holds_every_column_on_both_variants_with_a_worst_case_row"/>.</para>
    ///
    /// <para><b>Read the DATA column, not just the header.</b> Two successive rebalances of this grid sized
    /// a track against its header and trusted its data, and each time the untested side truncated with no
    /// ellipsis: col5 "Due Date" was cut to 72 against its 52.78 header while its data is the 11-character
    /// "07-May-2025" (72.57, cut by 8.57), and col2 sat at 64 while a crore-scale statutory amount
    /// "1,00,00,000" needs 72.57 (cut by 18.57, at EVERY window width, on BOTH variants).</para>
    /// </summary>
    /// <para><b>THE MONEY CEILING IS THE FORMATTER'S, NOT AN OBSERVED FIGURE.</b> col2 and col7 both bind
    /// <c>IndianFormat.Rupees</c> ("#,##0", Indian 3;2 grouping, whole rupees), whose ceiling string is
    /// <c>99,99,99,999</c> — TWELVE characters, not the eleven of the "1,00,00,000" this budget was
    /// previously sized against. col7 renders at 12.5pt (6.8726 DIP/glyph) behind a 10px right margin, so
    /// it needs 12*6.8726 + 10 = 92.47 of track; it was cut from a pre-stack 108 to 90, i.e. an 80px slot
    /// against 82.47 of digits — a 2.47px SILENT cut (the cell carries no TextTrimming) that begins at
    /// ₹10 crore, and a REGRESSION, since the width it replaced held. col2 is the same cell at 12pt
    /// (6.5977): 12*6.5977 + 10 = 89.17, against a 76px slot. Now 95 and 92.</para>
    private static readonly (int Col, double Floor, double Header, double Data, string What)[] InterestFloors =
    {
        (1, 84, 81.98, 60.78, "Section / Coll. Code   | 206C(1H)"),
        (2, 92, 35.79, 89.17, "TDS / TCS              | 99,99,99,999 @12pt +10 margin"),
        (3, 84, 81.98, 80.57, "Ded. Date / Coll. Date | 07-May-2025"),
        (4, 84, 62.18, 80.57, "Deposit Date (wraps)   | Undeposited"),
        (5, 84, 68.78, 80.57, "Due Date               | 07-May-2025"),
        (6, 58, 55.59, 29.79, "Months (one word)      | 144"),
        (7, 95, 68.78, 92.47, "Interest @1.5% (wraps) | 99,99,99,999 @12.5pt +10 margin"),
    };

    /// <summary>
    /// A CEILING, deliberately — never an equality. See
    /// <see cref="Interest_grid_twins_agree_and_hold_the_measured_per_track_budget"/>: an equality on the
    /// total is satisfiable by redistribution and is the exact shape that let five silent money truncations
    /// ship green. 84+92+84+84+84+58+95 = 581; the ceiling is what keeps the grid inside its MinWidth, the
    /// per-track floors are what keep each column readable, and only both together are a budget.
    /// </summary>
    private const double InterestFixedCeiling = 581d;

    /// <summary>
    /// What the grid's MinWidth must RESERVE for the star Party column over and above the fixed budget.
    /// 105 rather than the 100 the runtime lock asserts, so the reservation carries 5px of slack against
    /// Avalonia's ~+1px-per-fixed-track arrange artifact across seven fixed tracks.
    /// </summary>
    private const double InterestPartyReservation = 105d;

    /// <summary>The star Party column's ARRANGED floor at the 1024 window minimum (the row KEY must stay readable).</summary>
    private const double InterestPartyArrangedFloor = 100d;

    /// <summary>
    /// The interest grid's twin templates (header band + row) and their exact per-track budget.
    ///
    /// <para>This deliberately asserts EVERY track rather than a total. The previous version of this lock
    /// asserted <c>Sum == 538</c> plus three lower bounds, and that shape is what let the regression
    /// through: a "zero-sum" total is satisfied by moving width between columns, so col5 could be cut from
    /// 82 to 72 to fund col6 and the total-based assertion stayed green while a statutory due date started
    /// truncating. A per-track floor cannot be gamed by redistribution.</para>
    ///
    /// <para><b>STRENGTHENED — the total assertion was still here, and it was still the hole.</b> The
    /// version before this one had the per-track floors AND <c>Assert.Equal(570, t.Sum())</c>. That is not
    /// belt-and-braces; the equality actively BLOCKED the fix, because col7 could not be widened back to a
    /// safe 95 without robbing a neighbour of width the floors then refused to give up — which is how a
    /// column that HELD before this stack (108) came to be cut at ₹10 crore with the suite green. The
    /// equality is replaced by a CEILING. A ceiling and a floor are not the same instrument: the ceiling
    /// bounds what the grid may cost its container, the floors bound what each column may cost its reader,
    /// and neither can be satisfied by taking width from the other. Nothing here is weakened — the exact
    /// total was the ONE assertion redistribution could satisfy, and every track it silently permitted to
    /// shrink is now individually pinned.</para>
    /// </summary>
    [Fact]
    public void Interest_grid_twins_agree_and_hold_the_measured_per_track_budget()
    {
        // "Months" is unique to the interest grid in this file — "Party", "{Binding Col7}" and
        // "{Binding Col8}" are all shared with the GSTR, Batchwise and Outstanding grids, so matching on
        // any of those silently pulls in a different screen's budget.
        var header = Doc.Value.Descendants(Av + "Grid")
            .Single(g => g.Elements(Av + "TextBlock").Any(t => (string?)t.Attribute("Text") == "Months"));

        var def = (string)header.Attribute("ColumnDefinitions")!;

        // Header band + row template must carry the SAME column string — this one-file XAML keeps
        // producing twin drift, where a header moves and its row template does not.
        var twins = Doc.Value.Descendants(Av + "Grid")
            .Where(g => (string?)g.Attribute("ColumnDefinitions") == def)
            .ToList();
        Assert.Equal(2, twins.Count);

        var t = Tracks(def);
        Assert.Equal(8, t.Length);

        // PER-TRACK FLOORS FIRST. These are the assertions redistribution cannot satisfy.
        foreach (var (col, floor, hdr, data, what) in InterestFloors)
            Assert.True(t[col] >= floor,
                $"interest col{col} ({what}) is {t[col]} — needs >= {floor} " +
                $"(header {hdr:0.00}, DATA {data:0.00}, whichever is larger, + margin + 2px headroom).");

        // The total is asserted ADDITIONALLY and as a CEILING, never as an equality — see the remarks on
        // InterestFixedCeiling. It bounds what the grid costs its container; it says nothing about who owns
        // the width inside, which is the floors' job above.
        var fixedTotal = t.Sum();
        Assert.True(fixedTotal <= InterestFixedCeiling,
            $"interest fixed track total {fixedTotal} exceeds the {InterestFixedCeiling} ceiling the enclosing "
            + "MinWidth and the star Party floor were sized against.");

        // 581 of fixed track cannot coexist with a readable Party column inside the ~638px pane at the
        // 1024 window minimum, so the grid is PINNED wider than the pane and its enclosing
        // StatInterestBody ScrollViewer (HorizontalScrollBarVisibility="Auto") carries the remainder.
        // Without this the "*" Party column silently absorbs the whole deficit.
        //
        // Asserted as a RELATION to the measured fixed total, not as a magic number: an equality here is the
        // same trap as an equality on the track sum — it would forbid the next legitimate widening and push
        // the cost back into a money column. What matters is that MinWidth reserves the fixed budget PLUS a
        // readable Party column, and stays inside the 986px 1440-default report pane.
        var body = Doc.Value.Descendants(Av + "Grid")
            .Single(g => (string?)g.Attribute("Width") == "{Binding #StatInterestBody.Bounds.Width}");
        var min = double.Parse((string)body.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
        Assert.True(min - fixedTotal >= InterestPartyReservation,
            $"MinWidth {min} leaves the star Party column {min - fixedTotal} once the {fixedTotal} of fixed "
            + $"track is taken — below its {InterestPartyReservation}px reservation, so Party (the row KEY) "
            + "absorbs the fixed budget's growth instead of the Auto h-scroller.");
        Assert.True(min <= 986,
            $"MinWidth {min} exceeds the 986px 1440-default report pane, so the grid would h-scroll at the "
            + "DEFAULT window, not only at the 1024 minimum.");
    }

    /// <summary>
    /// THE ONE THAT COVERS BOTH VARIANTS AND THE DATA ROW. The static lock above pins the column string;
    /// this renders the grid on BOTH report kinds, puts a worst-case row through the REAL row template,
    /// and asserts the ARRANGED track widths of the header band and the data twin alike.
    ///
    /// <para>Both failures this file exists to prevent were invisible to a static or single-variant test:
    /// the first sized the grid on the TDS variant while the TCS strings were longer, the second sized
    /// col5 on the header band while the data twin was what overflowed. So the sweep is variant x
    /// (header, data), not one or the other.</para>
    ///
    /// <para>Nothing here asserts a TEXT width — the committed harness resolves no font, so any glyph
    /// measurement would be a fiction. Track geometry is font-independent: a fixed track is a fixed track,
    /// and each cell stretches to it.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.TdsInterest, 1024, 640)]
    [InlineData(ReportKind.TcsInterest, 1024, 640)]
    [InlineData(ReportKind.TdsInterest, 1280, 800)]
    [InlineData(ReportKind.TcsInterest, 1280, 800)]
    [InlineData(ReportKind.TdsInterest, 1920, 1080)]
    [InlineData(ReportKind.TcsInterest, 1920, 1080)]
    public void Interest_grid_holds_every_column_on_both_variants_with_a_worst_case_row(
        ReportKind kind, int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInterestLock_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);

        try
        {
            vm.OpenReport(kind);
            Pump(window);
            Assert.True(vm.Reports!.IsStatutoryInterest);

            // A worst-case row through the real row template: crore-scale statutory amounts, 11-character
            // dates, the 11-character "Undeposited" literal Col5 can bind, and a 3-digit month count.
            //
            // The money cells are produced by the REAL formatter at its REAL ceiling, not by a plausible
            // literal: IndianFormat.Rupees is "#,##0" on the Indian 3;2 grouping, so its widest output is
            // "99,99,99,999" (12 characters), and sizing this grid against the 11-character "1,00,00,000"
            // is exactly what cut col7 by 2.47px at ₹10-crore scale.
            var moneyCeiling = IndianFormat.Rupees(99_99_99_999m);
            Assert.Equal("99,99,99,999", moneyCeiling);

            vm.Reports.Rows.Clear();
            vm.Reports.Rows.Add(new ReportRow
            {
                Col1 = "Hindustan Petroleum Corporation Ltd",
                Col2 = kind == ReportKind.TcsInterest ? "206C(1H)" : "194J(b)",
                Col3 = moneyCeiling,
                Col4 = "07-May-2025",
                Col5 = "Undeposited",
                Col6 = "07-May-2025",
                Col7 = "144",
                Col8 = moneyCeiling,
            });
            Pump(window);

            var grids = Descendants(window).OfType<Grid>()
                .Where(g => g.ColumnDefinitions.Count == 8 && g.IsEffectivelyVisible)
                .Where(g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Months")
                         || g.Children.OfType<TextBlock>().Any(t => t.Text == "144"))
                .ToList();

            // Header band AND the data twin must both be arranged — if the row template stopped rendering
            // this test would otherwise pass vacuously on the header alone.
            Assert.Equal(2, grids.Count);
            Assert.Contains(grids, g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Months"));
            Assert.Contains(grids, g => g.Children.OfType<TextBlock>().Any(t => t.Text == "144"));

            foreach (var g in grids)
            {
                var which = g.Children.OfType<TextBlock>().Any(t => t.Text == "Months") ? "header band" : "data row";

                foreach (var (col, floor, hdr, data, what) in InterestFloors)
                    Assert.True(g.ColumnDefinitions[col].ActualWidth >= floor,
                        $"{kind} {which} at {width}x{height}: col{col} ({what}) arranged to " +
                        $"{g.ColumnDefinitions[col].ActualWidth:0.00} — needs >= {floor} " +
                        $"(header {hdr:0.00} / DATA {data:0.00}).");

                // The star Party column is the report's row KEY. Pinning the grid at MinWidth 686 is what
                // keeps it at 105 rather than letting it absorb the fixed budget's growth; at 610 it fell
                // to 70 (a 54px slot — 8 characters of a party name) at the 1024 minimum.
                Assert.True(g.ColumnDefinitions[0].ActualWidth >= InterestPartyArrangedFloor,
                    $"{kind} {which} at {width}x{height}: the star Party column arranged to " +
                    $"{g.ColumnDefinitions[0].ActualWidth:0.00} — it is being starved to fund the fixed tracks.");
            }
        }
        finally
        {
            window.Close();
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// THE ONE THAT PINS THE VARIANTS. The 84/92/84/84/84/58/95 budget above was measured against an EXACT set of
    /// header strings. If a new report kind is folded onto this shared grid, or a label is reworded to
    /// something longer, the geometry test above still passes while the screen silently truncates again —
    /// which is precisely how the TCS variant was missed. Enumerating the bound strings here means any
    /// change to them fails loudly and forces a re-measure.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.TdsInterest, "Section", "TDS", "Ded. Date", "Interest @1.5%")]
    [InlineData(ReportKind.TcsInterest, "Coll. Code", "TCS", "Coll. Date", "Interest @1%")]
    public void Interest_grid_bound_headers_are_exactly_the_measured_set(
        ReportKind kind, string code, string tax, string eventDate, string interest)
    {
        var vm = new ReportsViewModel(PopulatedCompanyFixture.BuildRegular(), kind);

        Assert.True(vm.IsStatutoryInterest);
        Assert.Equal(code, vm.StatCodeHeader);
        Assert.Equal(tax, vm.StatTaxHeader);
        Assert.Equal(eventDate, vm.StatEventDateHeader);
        Assert.Equal(interest, vm.StatInterestHeader);
    }

    // ================================================================= D-B

    /// <summary>
    /// EVERY Forex register track, header AND data — not the chosen pair this lock used to guard.
    ///
    /// <para>WHY THE OLD SHAPE WAS ITSELF THE DISEASE. This test used to pin cols 3 and 5 (the two base-₹
    /// columns) and say NOTHING about cols 1, 2, 4 and 6. A later rebalance then cut col 2 ("Forex Bal.",
    /// −20.16) and col 4 ("Rate", −6.78) to zero complaint from a green suite: a lock that guards two of
    /// six columns is exactly how a regression ships. Every column is now pinned at the floor its OWN
    /// worst-case content needs, so no future rebalance can pay for one column out of another's data.</para>
    ///
    /// <para>FLOORS, each = natural width of the ceiling string + the cell's own margin/padding + >=2px of
    /// real headroom (a sub-2px margin sits inside Avalonia's ~+1px-per-fixed-track slack artifact and is
    /// NOT a safe fit). Ceilings are produced by the REAL formatters in <c>ForexReportViewModel</c>, not by
    /// plausible literals, and the DIP figures were measured under a Skia harness resolving the real
    /// Consolas (6.5977 DIP/glyph @12pt, 6.0479 @11pt) — recorded here as commentary only, since the
    /// committed harness resolves no font and any text-width assertion would measure a fiction.</para>
    /// <list type="bullet">
    /// <item>c1 Currency 100 — floor = max(HEADER, DATA), and the DATA is what governs. The previous floor
    /// of 72 was written as "HEADER governs: 'Currency' 52.78 + 16 colHdr padding = 68.78" — a comment that
    /// was itself the defect. The header governs ONLY if the data is a 3-letter ISO code, which is what an
    /// inspection of <c>Currency.FormalName</c> ("ISO-style code, e.g. USD") was taken to mean. It is not
    /// true: <c>Currency</c> only <c>.Trim()</c>s FormalName, the master TextBox declares no MaxLength and
    /// <c>CurrencyMasterViewModel.CreateCurrency</c> rejects only blank — FormalName is UNBOUNDED free user
    /// text, and <c>ForexReportViewModel.CurrencyName</c> renders "{FormalName} ({Symbol})". The cell has no
    /// margin and no padding, so slot == track and floor(72/6.0479) = 11 characters fit; the string is
    /// FormalName+4, so every FormalName over 7 characters was cut, with <c>TextTrimming=None</c> and hence
    /// no cue. Rendering the real row template measured "US Dollar ($)" at 13 ch = 78.62 vs 72 = −6.62 at
    /// 1024, 1280 and 1920. DATA CEILING this floor is sized for: FormalName ≤ 12 chars with a 3-char symbol
    /// (or ≤ 14 with a 1-char symbol) — "UAE Dirham (AED)" / "Japanese Yen (¥)" = 16 ch = 96.77 → +3.23.
    /// Because the field is free text NO width is a complete fix, so the cell also carries TextTrimming and
    /// ToolTip.Tip; the width makes the ordinary case fit, the trimming makes the rest honest.</item>
    /// <item>c2 Forex Bal. 124 — <c>Fmt</c> is "#,##0.##" on INVARIANT culture (a FOREIGN-currency amount,
    /// so WESTERN grouping) and the VM appends a " Dr"/" Cr" SUFFIX — the part the cut sizing omitted.
    /// "999,999,999.99 Dr" = 17 ch = 112.16, + the 8px right margin = 120.16.</item>
    /// <item>c3/c5 Booked/Revalued ₹ 112 — <c>IndianFormat.Amount</c> ceiling ₹99,99,99,999.99 = 15 ch =
    /// 98.96, + 8px margin = 106.96. (Was 108: a +1.04 fit inside the rounding artifact, not a safe one.)</item>
    /// <item>c4 Rate 78 — "#,##0.####" ceiling "9,999.9999" = 10 ch = 65.98, + 8px margin = 73.98. A
    /// GBP/INR rate is 8 characters, not the 6 the cut track assumed.</item>
    /// <item>c6 G/L 40 — header "G/L" 19.79 + 16 = 35.79.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Forex_every_track_holds_its_own_worst_case_content()
    {
        var grids = Doc.Value.Descendants(Av + "Grid")
            .Where(g => g.Elements(Av + "TextBlock").Any(x => (string?)x.Attribute("Text") == "Revalued ₹")
                        || g.Elements(Av + "TextBlock").Any(x => (string?)x.Attribute("Text") == "{Binding RevaluedBase}"))
            .ToList();

        Assert.Equal(2, grids.Count); // header band + row twin

        // (column, floor, what is cut below it)
        var floors = new (int Col, double Floor, string What)[]
        {
            (1, 100, "a descriptive FormalName such as 'UAE Dirham (AED)' (96.77; slot == track, no padding) "
                     + "— the DATA governs here, not the 68.78 'Currency' header"),
            (2, 124, "a crore-scale foreign balance WITH its ' Dr'/' Cr' suffix (120.16 incl. margin)"),
            (3, 112, "a ₹99,99,99,999.99 booked amount (106.96 incl. margin)"),
            (4, 78,  "an 8-character exchange rate such as 105.2537 (73.98 incl. margin)"),
            (5, 112, "a ₹99,99,99,999.99 revalued amount (106.96 incl. margin)"),
            (6, 40,  "the 'G/L' HEADER (35.79 incl. colHdr padding)"),
        };

        foreach (var g in grids)
        {
            var t = Tracks((string)g.Attribute("ColumnDefinitions")!);
            Assert.Equal(7, t.Length);
            foreach (var (col, floor, what) in floors)
                Assert.True(t[col] >= floor,
                    $"Forex track c{col} is {t[col]}; below {floor} it truncates {what}. "
                    + "Only c0 and c1 carry TextTrimming; in every money cell the digits are simply not drawn.");
        }
    }

    /// <summary>
    /// The Currency cell SIGNALS its overflow. The width floor above sizes c1 for the descriptive currency
    /// names users actually type, but <c>Currency.FormalName</c> is unbounded free text, so a width alone can
    /// never be a complete fix — "Singapore Dollar (S$)" is 127.00 against a 100 track. This pins the other
    /// half: the cell declares CharacterEllipsis AND a ToolTip carrying the same binding, so an overflow is
    /// visible rather than silent and the full value stays readable. Same shape as the BankOptions Pay-From
    /// pickers. Without this, dropping either attribute would restore a silent cut with the suite still green.
    /// </summary>
    [Fact]
    public void Forex_currency_cell_signals_overflow_it_cannot_size_away()
    {
        // Identify the row template STRUCTURALLY (the grid carrying the RevaluedBase binding, the same
        // selector the track-floor test uses) rather than by its literal track string — otherwise a
        // legitimate width change would fail this test with a misleading "cell not found".
        var rowGrid = Doc.Value.Descendants(Av + "Grid")
            .Single(g => g.Elements(Av + "TextBlock")
                          .Any(x => (string?)x.Attribute("Text") == "{Binding RevaluedBase}"));

        var cell = rowGrid.Elements(Av + "TextBlock")
            .Single(x => (string?)x.Attribute("Text") == "{Binding Currency}");

        Assert.Equal("CharacterEllipsis", (string?)cell.Attribute("TextTrimming"));
        Assert.Equal("{Binding Currency}", (string?)cell.Attribute("ToolTip.Tip"));
    }

    /// <summary>
    /// The Forex register's header band and its rows sit inside ONE horizontal scroller with a MinWidth
    /// floor, so the six money/label tracks above can be sized for their real content WITHOUT starving the
    /// star Ledger column. This is the structural half of the fix: 538 of fixed track + a >=150px readable
    /// name column cannot both fit the 638px page floor, and the previous rebalance resolved that squeeze
    /// by cutting the money. 690 = 538 + 152. It is also < the 986px 1440-default report pane, so at the
    /// default window the grid still fits and nothing scrolls at all.
    /// </summary>
    [Fact]
    public void Forex_grid_scrolls_horizontally_rather_than_starving_its_name_column()
    {
        var band = Doc.Value.Descendants(Av + "Grid")
            .First(g => g.Elements(Av + "TextBlock").Any(x => (string?)x.Attribute("Text") == "Revalued ₹"));

        var minWidthHost = band.Ancestors(Av + "Grid")
            .FirstOrDefault(a => (string?)a.Attribute("MinWidth") is not null);
        Assert.True(minWidthHost is not null,
            "The Forex header band no longer sits inside a MinWidth-pinned Grid — the fixed tracks would be "
            + "squeezed against the page floor and the star Ledger column starved again.");

        var min = double.Parse((string)minWidthHost!.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
        var fixedTotal = Tracks((string)band.Attribute("ColumnDefinitions")!).Skip(1).Sum();
        Assert.True(min - fixedTotal >= 150,
            $"MinWidth {min} leaves the star Ledger column {min - fixedTotal:F0}px once the {fixedTotal:F0}px of "
            + "fixed track is taken — below the 150px readability floor the whole campaign is measured against.");
        Assert.True(min <= 986,
            $"MinWidth {min} exceeds the 986px 1440-default report pane, so the grid would force itself wider "
            + "than the pane at the DEFAULT window and h-scroll its rightmost column off behind the sidebar.");

        var scroller = band.Ancestors(Av + "ScrollViewer")
            .FirstOrDefault(s => (string?)s.Attribute("HorizontalScrollBarVisibility") == "Auto");
        Assert.True(scroller is not null,
            "No Auto horizontal ScrollViewer wraps the Forex header band, so the MinWidth above would clip "
            + "instead of scroll.");
        Assert.Contains(scroller!.Descendants(Av + "Grid"),
            g => (string?)g.Attribute("ColumnDefinitions") == (string?)band.Attribute("ColumnDefinitions")
                 && g.Ancestors(Av + "DataTemplate").Any());
    }

    // ================================================================= D-C

    private static readonly string[] PfEcrStatLabels =
    {
        "A/c 1 — EPF ₹", "A/c 2 — Admin ₹", "A/c 10 — EPS ₹",
        "A/c 21 — EDLI ₹", "A/c 22 — EDLI Adm ₹", "Total ₹",
    };

    /// <summary>
    /// All six PF ECR challan labels wrap, and all six reserve the SAME two-line height. Wrapping alone
    /// would fix the truncation but break the summary's alignment: only the long label wraps, so its
    /// value would sit a line lower than its five neighbours. The shared MinHeight makes every column
    /// reserve two lines, so the band grows by exactly one line once and the figures stay on one baseline.
    /// </summary>
    [Fact]
    public void PfEcr_stat_labels_wrap_and_reserve_a_uniform_two_line_height()
    {
        foreach (var label in PfEcrStatLabels)
        {
            var tbs = Doc.Value.Descendants(Av + "TextBlock")
                .Where(t => (string?)t.Attribute("Text") == label
                            && (string?)t.Attribute("FontSize") == "10.5"
                            && (string?)t.Attribute("TextWrapping") == "Wrap")
                .ToList();

            Assert.True(tbs.Count >= 1, $"PF ECR stat label '{label}' is not marked TextWrapping=Wrap.");
            Assert.Contains(tbs, t => (string?)t.Attribute("MinHeight") == "26");
        }
    }

    // NOTE — there is deliberately NO runtime "all six figures share one baseline" test here, although
    // that is the property the MinHeight above exists to protect and it WAS verified out-of-band (all six
    // values arranged at y=26.0 at 1024/1280/1920 under a Skia harness resolving the real Consolas).
    // Written as a headless test it is a LIAR: the committed harness resolves no font and its stub glyphs
    // are ~82% wider, so "A/c 22 — EDLI Adm ₹" wraps to THREE lines instead of two, overflows the 26px
    // reservation, and the assertion fails (observed: baselines 26 and 35) on markup that is correct in
    // the real app. A test whose verdict tracks the test harness's font rather than the product's layout
    // is worse than no test, so the wrap+MinHeight contract is locked statically above instead.

    // ---------------------------------------------------------------- helpers

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
        for (var i = 0; i < 4; i++) { w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }
}
