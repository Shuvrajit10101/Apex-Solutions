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
using Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// COLUMN-BUDGET LOCKS for the two filed statutory certificates (Form 16A / Form 27D) and the Forex
/// Gain/Loss register — the three grids whose column budgets were rebalanced to close SILENT header
/// truncation at 1024 DIP (a real 1366x768 laptop at 125% scaling, the supported minimum).
///
/// <para><b>What was wrong.</b> On both certificates the column HEADERS — not the data — were cut with
/// no ellipsis, no tooltip and no scroll, so they misread as complete words: <c>BSR Code</c> -&gt;
/// <c>BSR Cod</c>, <c>Deposit Date</c> -&gt; <c>Deposit Dat</c>, and worst, <c>Rate %</c> -&gt;
/// <c>Rate</c>, which turns a <c>10.00</c> rate into something a deductee reads as ten rupees on a
/// document they FILE. On the Forex register the reverse: six over-provisioned fixed columns consumed
/// 600 of the 622px pane at 1024, leaving the star Ledger column 22px (6px of text) — no revaluation
/// row was attributable to a ledger at all.</para>
///
/// <para><b>Why widths and wrapping rather than TextTrimming.</b> An ellipsis is honest for long free
/// text, because a cue is all the reader needs. It is NOT a fix for a filed figure or for a label whose
/// meaning changes when clipped: there is no dropdown to open and no tooltip to hover on a printed
/// certificate. So the challan grids were WIDENED into slack that already existed inside the enclosing
/// StackPanel, and the two multi-word deduction headers WRAP to two lines, which keeps the "%" on
/// screen.</para>
///
/// <para><b>SECOND ROUND — the header fix was paid for out of the DATA.</b> That first rebalance held each
/// deduction grid's total at EXACTLY 382 and called it "provably zero-sum". Zero-sum was the bug: the
/// money columns were already too narrow, so freezing the total forbade the only correct remedy and the
/// suite then certified silent truncation of ₹10 lakh (TDS, 75.87 DIP in a 70px slot) and ₹1 crore
/// (Amount Paid, 88.52 in 86) on a filed certificate. The same mistake, in the same class, shipped four
/// more times on the Forex register and the Form 26Q/27EQ returns, because each lock capped a TOTAL or
/// guarded a CHOSEN PAIR of columns instead of pinning EVERY column at the floor its own worst-case
/// content needs. Every lock in this file is now per-column, header AND data, and the star-column
/// protection was moved out of "cap the fixed total" (a proxy satisfiable by cutting money) into a
/// MinWidth floor + an Auto horizontal scroller, so readability and correctness no longer compete.</para>
///
/// <para><b>Why nothing here asserts a text width.</b> The committed headless harness resolves no font,
/// so every glyph collapses to a stub roughly 82% wider than the real Consolas the app uses. A width
/// assertion would measure a fiction. These lock COLUMN-TRACK arithmetic (font-independent: a fixed
/// track is a fixed track) and, for the inherently non-geometric <c>TextWrapping</c>, a static XAML
/// assertion in the style of <see cref="XamlLayoutInvariantTests"/>. No Skia, no
/// <c>CaptureRenderedFrame</c>, no <c>TextLayout</c>/<c>TextRuns</c>.</para>
/// </summary>
public sealed class StatutoryColumnBudgetLockTests
{
    // ------------------------------------------------------------------ static XAML side

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

    /// <summary>
    /// Every Grid carrying a header whose text is <paramref name="headerText"/> AND having
    /// <paramref name="columnCount"/> columns. The column count is what pins the lookup to ONE grid:
    /// "Amount Paid" and "Rate %" also head the 8-column Form 26Q/27EQ return grids, so matching on the
    /// header literal alone silently pulls in a different screen.
    /// </summary>
    private static List<XElement> GridsWithHeader(string headerText, int columnCount) =>
        Doc.Value.Descendants(Av + "Grid")
            .Where(g => ((string?)g.Attribute("ColumnDefinitions") ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries).Length == columnCount
                        && g.Elements(Av + "TextBlock")
                            .Any(t => (string?)t.Attribute("Text") == headerText))
            .ToList();

    private static double[] Tracks(XElement grid) =>
        ((string?)grid.Attribute("ColumnDefinitions") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim() == "*" ? 0d : double.Parse(s.Trim()))
            .ToArray();

    /// <summary>
    /// EVERY track of the certificate deduction/collection grids, header AND data.
    ///
    /// <para>WHAT THIS TEST USED TO BE, AND WHY IT WAS PART OF THE PROBLEM. It asserted only that the grid
    /// total stayed EXACTLY 382 — "provably zero-sum". Holding the total fixed sounds conservative, but on
    /// a grid whose money columns were ALREADY too narrow it actively forbade the only correct fix: the
    /// TDS column sat at a 70px slot against ₹10,00,000.00 (75.87 DIP) and Amount Paid at 86 against
    /// ₹1,00,00,000.00 (88.52), so ₹10 lakh and ₹1 crore were silently chopped on a certificate the
    /// deductee FILES — and a zero-sum lock meant any widening had to starve a neighbour. The total is now
    /// 428 and the lock pins each column at the floor its own worst-case content needs.</para>
    ///
    /// <para>Floors (natural width of the ceiling string + the cell's own margin/padding + >=2px of real
    /// headroom; measured under a Skia harness on the real Consolas, 6.3228 DIP/glyph @11.5pt data and
    /// 6.5977 @12pt SemiBold headers, colHdr <c>Padding="8,4"</c> = 16):
    /// c0 Section 66 (header 62.18 — was 64, a +1.82 fit inside the rounding artifact),
    /// c1 FVU 40, c2 Date 80 ("31-Mar-2026" 69.55 + 8px of margin — was 78, a +0.45 fit),
    /// c3 Amount Paid 98 and c4 TDS 98 (NO margin, so slot == track; ceiling ₹99,99,99,999.00 = 15 ch =
    /// 94.84), c5 Rate % 46 (wrapped, longest word "Rate" 26.39 + 16 = 42.39 — was 44, a +1.61 fit).</para>
    /// </summary>
    /// <param name="header">"Amount Paid" = Form 16A (c0 header is "Section"); "Amount Recd" = Form 27D
    /// (c0 header is the much shorter "Code", so its c0 floor is lower — the floors below are per-family
    /// because a floor copied across families is how the 27D site got missed in the first place).</param>
    [Theory]
    [InlineData("Amount Paid", 66, 80, 46)] // Form 16A — "Section" 62.18 / Date 77.55 / wrapped "Rate" 42.39
    [InlineData("Amount Recd", 56, 82, 48)] // Form 27D — "Code" 42.39 already fits 56; Date/Rate % unchanged
    public void Certificate_deduction_grid_holds_every_column_and_twins_agree(
        string header, double c0Floor, double c2Floor, double c5Floor)
    {
        var grids = GridsWithHeader(header, columnCount: 6);
        Assert.Single(grids); // exactly one 6-column header grid carries this literal

        var t = Tracks(grids[0]);
        var floors = new (int Col, double Floor, string What)[]
        {
            (0, c0Floor, "the c0 HEADER ('Section' 62.18 / 'Code' 42.39, incl. colHdr padding)"),
            (1, 40, "the 'FVU' HEADER (35.79 incl. colHdr padding)"),
            (2, c2Floor, "a 'dd-MMM-yyyy' deduction date (69.55 + 8px of cell margin)"),
            (3, 98, "a ₹99,99,99,999.00 amount paid/received — this cell has NO margin, so slot == track"),
            (4, 98, "a ₹99,99,99,999.00 TDS/TCS figure — NO margin, so slot == track"),
            (5, c5Floor, "the wrapped 'Rate %' HEADER's longest word (42.39 incl. colHdr padding)"),
        };
        foreach (var (col, floor, what) in floors)
            Assert.True(t[col] >= floor,
                $"Certificate deduction track c{col} is {t[col]}; below {floor} it silently truncates {what} "
                + "on a document the deductee FILES. No cell in this grid carries TextTrimming.");

        // The row template is the sibling ItemsControl's grid: same column string, or the twins drifted.
        var rowGrids = Doc.Value.Descendants(Av + "Grid")
            .Where(g => (string?)g.Attribute("ColumnDefinitions") == (string?)grids[0].Attribute("ColumnDefinitions"))
            .ToList();
        Assert.Equal(2, rowGrids.Count); // header + row twin, in lockstep
    }

    /// <summary>
    /// The certificate body's host StackPanel floors itself at or above the deduction grid's total, so the
    /// grid is never squeezed below its content. Widening the grid 382 -> 428 without moving this floor
    /// would simply have relocated the truncation from the money columns to the pane edge.
    /// </summary>
    [Theory]
    [InlineData("Amount Paid")] // Form 16A certificate body
    [InlineData("Amount Recd")] // Form 27D certificate body
    public void Certificate_body_min_width_clears_the_widened_deduction_grid(string header)
    {
        var grid = GridsWithHeader(header, columnCount: 6).Single();
        var total = Tracks(grid).Sum();

        var host = grid.Ancestors(Av + "StackPanel")
            .FirstOrDefault(s => (string?)s.Attribute("MinWidth") is not null);
        Assert.True(host is not null, "The certificate body StackPanel lost its MinWidth floor.");

        var min = double.Parse((string)host!.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
        Assert.True(min >= total,
            $"Certificate body MinWidth {min} is under the {total} deduction grid, so the grid is squeezed "
            + "and its money columns clip again.");

        var scroller = grid.Ancestors(Av + "ScrollViewer")
            .FirstOrDefault(s => (string?)s.Attribute("HorizontalScrollBarVisibility") == "Auto");
        Assert.True(scroller is not null,
            "No Auto horizontal scroller carries the certificate body, so the MinWidth above would clip.");
    }

    /// <summary>
    /// The two multi-word certificate headers must WRAP. Clipping "Rate %" to "Rate" is a semantic loss
    /// on a filed document — the rate stops being a percentage — and an ellipsis cannot restore the "%".
    /// This is the one property here that is not geometric, so it is asserted statically.
    /// </summary>
    [Theory]
    [InlineData("Amount Paid")]  // Form 16A certificate + Form 26Q return
    [InlineData("Amount Recd")]  // Form 27D certificate + Form 27EQ return
    [InlineData("Rate %")]       // all four of the above
    [InlineData("Overdue Days")] // TDS/TCS Outstanding — read as "Overdue Day"
    public void Certificate_multiword_headers_wrap_rather_than_clip(string headerText)
    {
        var headers = Doc.Value.Descendants(Av + "TextBlock")
            .Where(t => (string?)t.Attribute("Text") == headerText
                        && ((string?)t.Attribute("Classes"))?.Contains("colHdr") == true)
            .ToList();

        Assert.NotEmpty(headers);
        foreach (var h in headers)
            Assert.Equal("Wrap", (string?)h.Attribute("TextWrapping"));
    }

    /// <summary>
    /// EVERY track of both challan grids (Form 16A and Form 27D), header AND data — plus, additionally, a
    /// containment ceiling against the host StackPanel's own MinWidth.
    ///
    /// <para><b>WHAT THIS TEST USED TO BE, AND WHY IT IS THE FILE'S WORST OFFENDER.</b> Its entire body was
    /// <c>Assert.Equal(356, total)</c> plus <c>total &lt;= 384</c>. It asserted a SUM and said nothing about
    /// any column. That is not a weak lock, it is an anti-lock: it is satisfied by ANY redistribution of the
    /// same 356px, and the verifier demonstrated exactly that by rewriting the grid
    /// <c>89,69,96,102</c> -> <c>69,69,96,122</c> — still 356, starving <c>Challan No.</c> by 19.57px — and
    /// the full suite stayed green at <c>Failed: 0, Passed: 1524</c>. Five consecutive slices shipped a
    /// silent money truncation through locks of this shape.</para>
    ///
    /// <para><b>AND 356 WAS NOT EVEN A FIT.</b> On the DECLARED tracks the four headers cleared by
    /// +0.43 / +0.22 / +0.83 / +0.23 — every one of them inside Avalonia's ~+-1px-per-fixed-track arrange
    /// artifact, which is literally visible here (a declared 89/69/96/102 arranges as 90/70/97/103). A
    /// sub-1px margin on a declared track is a coin toss, not a fit, so all eight columns across the two
    /// grids were re-floored at >= 2px of real headroom: 91/71/98/104 = 364, still far inside the host's
    /// 430 MinWidth, so nothing new scrolls and no column is starved to pay for another.</para>
    ///
    /// <para>The ceiling is read from the host's OWN MinWidth attribute rather than hard-coded — the
    /// previous version asserted against 384 long after the host had moved to 430, so the containment half
    /// of the lock had quietly stopped meaning anything.</para>
    /// </summary>
    [Theory]
    [InlineData("TDS Deposited")] // Form 16A challan block
    [InlineData("TCS Deposited")] // Form 27D challan block — the twin the last two rebalances each forgot
    public void Challan_grid_holds_every_column_and_fits_its_host(string header)
    {
        var grids = GridsWithHeader(header, columnCount: 4);
        Assert.Single(grids);

        var def = (string)grids[0].Attribute("ColumnDefinitions")!;

        // Header band + row template must carry the SAME column string, or the twins have drifted and the
        // floors below would be guarding a band whose rows are laid out to a different budget. Scoped to
        // THIS certificate's DataTemplate: both certificates deliberately carry the identical budget, so a
        // repo-wide count would be 4 and would not tell us the pair belongs to the same screen.
        var certificate = grids[0].Ancestors(Av + "DataTemplate").First();
        var twins = certificate.Descendants(Av + "Grid")
            .Where(g => (string?)g.Attribute("ColumnDefinitions") == def)
            .ToList();
        Assert.Equal(2, twins.Count);

        var t = Tracks(grids[0]);
        Assert.Equal(4, t.Length);

        foreach (var (col, floor, what) in ChallanFloors)
            Assert.True(t[col] >= floor,
                $"Challan track c{col} is {t[col]}; below {floor} it silently truncates {what} on a figure "
                + "that is FILED with the return. No cell in this grid carries TextTrimming.");

        // The total is asserted ADDITIONALLY and only as a CEILING against the real host MinWidth. It bounds
        // what the grid costs its container; it can never be traded against the floors above.
        var total = t.Sum();
        var host = grids[0].Ancestors(Av + "StackPanel")
            .First(s => (string?)s.Attribute("MinWidth") is not null);
        var hostMin = double.Parse((string)host.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
        Assert.True(total <= hostMin,
            $"challan grid {total} would overflow the {hostMin} host MinWidth, buying header readability with "
            + "a brand-new horizontal scrollbar at 1024 DIP.");
    }

    /// <summary>
    /// The challan floors, identical on both certificates because all four headers are (the 27D's
    /// "TCS Deposited" is the same 13 characters as the 16A's "TDS Deposited").
    ///
    /// <para>Headers: 12pt SemiBold Consolas, 6.5977 DIP/glyph, colHdr <c>Padding="8,4"</c> => slot =
    /// track - 16. Data: 11.5pt, 6.3228 DIP/glyph, slot = track - the cell's own margin. Each floor is
    /// max(header, data) + >= 2px of real headroom on the DECLARED track.</para>
    /// <list type="bullet">
    /// <item>c0 91 — "Challan No." 11 ch = 72.57 + 16 = 88.57 (+2.43). Data is a 5-digit CIN challan serial,
    /// 31.61 + 8px margin, far inside; the HEADER governs.</item>
    /// <item>c1 71 — "BSR Code" 8 ch = 52.78 + 16 = 68.78 (+2.22). Data is a 7-character BSR code,
    /// 44.26 + 8 = 52.26; the HEADER governs.</item>
    /// <item>c2 98 — "Deposit Date" 12 ch = 79.17 + 16 = 95.17 (+2.83). Data "31-Mar-2026" 11 ch =
    /// 69.55 + 8 = 77.55; the HEADER governs.</item>
    /// <item>c3 104 — "TDS/TCS Deposited" 13 ch = 85.77 + 16 = 101.77 (+2.23), and the DATA is the REAL
    /// formatter <c>IndianFormat.AmountAlways</c> ("#,##0.00", Indian 3;2 grouping) at its ceiling
    /// "99,99,99,999.00" = 15 ch = 94.84 + its 4px right margin = 98.84 (+5.16). Header governs; both
    /// clear.</item>
    /// </list>
    /// </summary>
    private static readonly (int Col, double Floor, string What)[] ChallanFloors =
    {
        (0, 91,  "the 'Challan No.' HEADER (88.57 incl. colHdr padding)"),
        (1, 71,  "the 'BSR Code' HEADER (68.78 incl. colHdr padding)"),
        (2, 98,  "the 'Deposit Date' HEADER (95.17 incl. colHdr padding)"),
        (3, 104, "the 'TDS/TCS Deposited' HEADER (101.77) or a ₹99,99,99,999.00 deposit (98.84 incl. margin)"),
    };

    // ------------------------------------------------------------------ runtime geometry side

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
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
    }

    /// <summary>
    /// THE ONE THAT ACTUALLY BITES. At 1024 DIP the Forex pane is ~622px wide; the six fixed columns used
    /// to eat 600 of it, arranging the star Ledger column to 22px — a 6px text slot, i.e. the ledger name
    /// was gone. Trimming those six to their measured need frees 140px. This asserts the ARRANGED width of
    /// the star track at the supported minimum window size, which is pure geometry and so is identical
    /// with or without a real font.
    /// </summary>
    [AvaloniaFact]
    public void Forex_ledger_column_survives_at_the_1024_minimum_window()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexForexLock_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1024, Height = 700 };
        window.Show();
        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);
        vm.OpenForexReport();
        Pump(window);

        var grid = Descendants(window).OfType<Grid>()
            .First(g => g.ColumnDefinitions.Count == 7
                        && g.GetVisualChildren().OfType<TextBlock>().Any(t => t.Text == "Ledger"));

        var star = grid.ColumnDefinitions[0].ActualWidth;
        var fixedSum = grid.ColumnDefinitions.Skip(1).Sum(c => c.ActualWidth);

        // Pre-fix this was 22.0 against 600 of fixed track.
        Assert.True(star >= 140, $"Forex Ledger column arranged to {star:0.0}px at 1024 DIP — it is being starved again.");

        // STRENGTHENED TWICE. It began as a bare `fixedSum <= 470`; that ceiling was only ever a PROXY for
        // "the star column is not starved" — which the line above asserts DIRECTLY — and as a lone
        // one-sided bound it actively invited the opposite defect, because it could be satisfied by
        // shrinking a money column until a figure was silently chopped. It then was, four times over.
        //
        // The ceiling is now GONE rather than merely raised, and deliberately so: capping the fixed total
        // is the wrong instrument. The grid's header band and rows were moved inside one horizontal
        // ScrollViewer with a MinWidth floor, so the star column's readability no longer competes with the
        // money columns' correctness at all — the floor guarantees the star column its 150px and the
        // scroller absorbs the rest. What remains is a per-column FLOOR on every fixed track, asserted
        // here on the ARRANGED geometry (and statically, over every column, in
        // SharedGridVariantBudgetLockTests.Forex_every_track_holds_its_own_worst_case_content).
        var floors = new (int Col, double Floor, string What)[]
        {
            (1, 72,  "the 'Currency' header"),
            (2, 124, "a crore-scale foreign balance WITH its ' Dr'/' Cr' suffix"),
            (3, 112, "a ₹99,99,99,999.99 booked amount"),
            (4, 78,  "an 8-character exchange rate such as 105.2537"),
            (5, 112, "a ₹99,99,99,999.99 revalued amount"),
            (6, 40,  "the 'G/L' header"),
        };
        foreach (var (col, floor, what) in floors)
            Assert.True(grid.ColumnDefinitions[col].ActualWidth >= floor,
                $"Forex track c{col} arranged to {grid.ColumnDefinitions[col].ActualWidth:0.0}px at 1024 DIP; "
                + $"below {floor} it silently truncates {what}.");
        Assert.True(fixedSum >= 538,
            $"Forex fixed tracks total {fixedSum:0.0} — below 538 at least one money column is back under "
            + "its measured ceiling.");

        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The Form 26Q / 27EQ return grids: EVERY column, plus the structural guarantee that the star
    /// party-name column and the money columns no longer compete.
    ///
    /// <para>HISTORY, because both failure modes have now shipped. First the grids carried 625px of fixed
    /// track inside a ~600px pane and the star Deductee/Collectee column was arranged to 1px — the party a
    /// line belongs to was not on screen. The remedy capped the fixed total at 480, which bought the name
    /// column back by cutting the money: TDS fell to a 72px slot against ₹10,00,000.00 (75.87 DIP) and
    /// Amount Paid to 88 against ₹1,00,00,000.00 (88.52), so an ordinary 194J figure was chopped on a
    /// FILED RETURN. A cap on the fixed total cannot distinguish those two outcomes, so it is replaced
    /// here by (a) a per-column floor and (b) a MinWidth floor that reserves the star column its 150px
    /// independently, with the enclosing horizontal scroller absorbing the remainder.</para>
    /// </summary>
    [Fact]
    public void Return_grid_holds_every_column_and_reserves_the_party_name()
    {
        var headerGrids = Doc.Value.Descendants(Av + "Grid")
            .Where(g => g.Elements(Av + "TextBlock")
                         .Any(t => (string?)t.Attribute("Text") is "Deductee" or "Collectee")
                        // 8 columns pins this to the RETURN grids: the certificate deductee/collectee
                        // PICKER lists share the header literal but are 3-column grids on another screen.
                        && Tracks(g).Length == 8)
            .ToList();

        Assert.Equal(2, headerGrids.Count); // Form 26Q + Form 27EQ header bands

        var floors = new (int Col, double Floor, string What)[]
        {
            (0, 80, "a 10-character PAN (63.23 + 8px of cell margin)"),
            (2, 66, "the 'Section' HEADER (62.18 incl. colHdr padding)"),
            (3, 40, "the 'FVU' HEADER (35.79 incl. colHdr padding)"),
            (4, 78, "a 'dd-MMM-yyyy' date (69.55 + 4px of cell margin)"),
            (5, 98, "a ₹99,99,99,999.00 amount paid/received — NO margin, so slot == track"),
            (6, 98, "a ₹99,99,99,999.00 TDS/TCS figure — NO margin, so slot == track"),
            (7, 46, "the wrapped 'Rate %' HEADER's longest word (42.39 incl. colHdr padding)"),
        };

        foreach (var g in headerGrids)
        {
            var t = Tracks(g);
            Assert.Equal(8, t.Length);
            foreach (var (col, floor, what) in floors)
                Assert.True(t[col] >= floor,
                    $"Form 26Q/27EQ track c{col} is {t[col]}; below {floor} it silently truncates {what} "
                    + "on a FILED RETURN.");

            // The star party-name column is reserved by the MinWidth floor, NOT by capping the money.
            var host = g.Ancestors(Av + "Grid").FirstOrDefault(a => (string?)a.Attribute("MinWidth") is not null);
            Assert.True(host is not null,
                "The return grid lost the MinWidth-pinned host that reserves the star party-name column.");
            var min = double.Parse((string)host!.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
            Assert.True(min - t.Sum() >= 150,
                $"MinWidth {min} leaves the star party-name column {min - t.Sum():F0}px once the {t.Sum():F0}px "
                + "of fixed track is taken — below the 150px readability floor.");
            Assert.True(min <= 986,
                $"MinWidth {min} exceeds the 986px 1440-default report pane, so the grid would h-scroll its "
                + "rightmost column off-pane at the DEFAULT window.");

            Assert.True(g.Ancestors(Av + "ScrollViewer")
                    .Any(s => (string?)s.Attribute("HorizontalScrollBarVisibility") == "Auto"),
                "No Auto horizontal scroller wraps the return grid, so its MinWidth would clip, not scroll.");
        }
    }

    /// <summary>
    /// THE ONE THAT RENDERS THE DATA INSTEAD OF DERIVING IT. Everything above reads the ColumnDefinitions
    /// string out of the XAML; that is enough for a fixed track, but it can only ever prove the budget the
    /// markup DECLARES, never that the row template is the twin those tracks were sized for, nor that the
    /// declared tracks survive arrange.
    ///
    /// <para><b>Why this gap existed and why it mattered.</b> <see cref="PopulatedCompanyFixture"/> produces
    /// no deductee, no collectee and no challan, so on the certificate screens the
    /// <c>Deductions</c>/<c>Collections</c>/<c>Challans</c> <c>ItemsControl</c>s instantiate ZERO row
    /// visuals — the header band is the only thing on screen. Every previous audit of these grids therefore
    /// DERIVED the data-row figures from the markup and said so. Deriving is what missed that the challan
    /// tracks arrange ~+1px wider than declared, which is the whole reason a +0.43 header clearance was
    /// mistaken for a fit. So this test injects a worst-case row THROUGH THE REAL ROW TEMPLATE and asserts
    /// the ARRANGED <c>ActualWidth</c> of every track, on the header band and the data twin alike, at all
    /// three supported window widths.</para>
    ///
    /// <para>The money strings come from the REAL formatter at its REAL ceiling
    /// (<c>IndianFormat.AmountAlways</c> = "#,##0.00" on the Indian 3;2 grouping), asserted literally below
    /// so that a formatter change cannot quietly move the ceiling this budget was sized against. Nothing
    /// here measures TEXT — the committed harness resolves no font, so a glyph measurement would be a
    /// fiction; track geometry is font-independent.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true, 1024, 700)]
    [InlineData(true, 1280, 800)]
    [InlineData(true, 1920, 1080)]
    [InlineData(false, 1024, 700)]
    [InlineData(false, 1280, 800)]
    [InlineData(false, 1920, 1080)]
    public void Certificate_grids_hold_every_column_with_a_rendered_worst_case_row(
        bool tds, int width, int height)
    {
        // The REAL formatter at the ceiling this budget is sized against — not a plausible literal.
        var money = IndianFormat.AmountAlways(99_99_99_999m);
        Assert.Equal("99,99,99,999.00", money);

        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCertLock_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var company = PopulatedCompanyFixture.BuildRegular();
        company.Tds = new TdsConfig { Enabled = true };
        company.Tcs = new TcsConfig { Enabled = true };
        storage.Save(company);

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);

        try
        {
            // The deduction grid's c0 header differs per family ("Section" vs "Code"), so the c0 floor does
            // too — a floor copied across the twins is how the 27D site was missed twice before.
            double c0Floor, c2Floor, c5Floor;
            if (tds)
            {
                vm.OpenForm16A();
                Pump(window);
                var page = vm.Form16A;
                Assert.NotNull(page);
                page!.Deductions.Clear();
                page.Deductions.Add(new Form16ADeductionRowVm
                {
                    Section = "194J(b)", FvuCode = "94J", Date = "31-Mar-2026",
                    AmountPaid = money, Tds = money, Rate = "10.00",
                });
                page.Challans.Clear();
                page.Challans.Add(new Form16AChallanRowVm
                {
                    ChallanNo = "00042", BsrCode = "0510308", DepositDate = "31-Mar-2026",
                    TdsDeposited = money,
                });
                (c0Floor, c2Floor, c5Floor) = (66, 80, 46);
            }
            else
            {
                vm.OpenForm27D();
                Pump(window);
                var page = vm.Form27D;
                Assert.NotNull(page);
                page!.Collections.Clear();
                page.Collections.Add(new Form27DCollectionRowVm
                {
                    CollectionCode = "6CH", FvuCode = "6CH", Date = "31-Mar-2026",
                    AmountReceived = money, Tcs = money, Rate = "0.10",
                });
                page.Challans.Clear();
                page.Challans.Add(new Form27DChallanRowVm
                {
                    ChallanNo = "00042", BsrCode = "0510308", DepositDate = "31-Mar-2026",
                    TcsDeposited = money,
                });
                (c0Floor, c2Floor, c5Floor) = (56, 82, 48);
            }
            Pump(window);

            var what = tds ? "Form 16A" : "Form 27D";

            // ---- the 4-column challan grids: header band + the row we just injected ----
            var challanGrids = Descendants(window).OfType<Grid>()
                .Where(g => g.ColumnDefinitions.Count == 4 && g.IsEffectivelyVisible)
                .Where(g => g.Children.OfType<TextBlock>().Any(t => t.Text is "TDS Deposited" or "TCS Deposited")
                         || g.Children.OfType<TextBlock>().Any(t => t.Text == "0510308"))
                .ToList();

            // Header band AND the data twin — if the row template stopped instantiating, this test must not
            // pass vacuously on the header alone. That vacuity is exactly what previous audits admitted to.
            Assert.Equal(2, challanGrids.Count);
            Assert.Contains(challanGrids,
                g => g.Children.OfType<TextBlock>().Any(t => t.Text is "TDS Deposited" or "TCS Deposited"));
            Assert.Contains(challanGrids, g => g.Children.OfType<TextBlock>().Any(t => t.Text == "0510308"));

            foreach (var g in challanGrids)
            {
                var band = g.Children.OfType<TextBlock>().Any(t => t.Text == "0510308") ? "data row" : "header band";
                foreach (var (col, floor, cut) in ChallanFloors)
                    Assert.True(g.ColumnDefinitions[col].ActualWidth >= floor,
                        $"{what} challan {band} at {width}x{height}: c{col} arranged to "
                        + $"{g.ColumnDefinitions[col].ActualWidth:0.00} — below {floor} it silently truncates {cut}.");
            }

            // ---- the 6-column deduction / collection grids: header band + the injected row ----
            var deductionGrids = Descendants(window).OfType<Grid>()
                .Where(g => g.ColumnDefinitions.Count == 6 && g.IsEffectivelyVisible)
                .Where(g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Rate %")
                         || g.Children.OfType<TextBlock>().Any(t => t.Text is "10.00" or "0.10"))
                .ToList();

            Assert.Equal(2, deductionGrids.Count);
            Assert.Contains(deductionGrids, g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Rate %"));
            Assert.Contains(deductionGrids, g => g.Children.OfType<TextBlock>().Any(t => t.Text is "10.00" or "0.10"));

            var deductionFloors = new (int Col, double Floor, string What)[]
            {
                (0, c0Floor, "the c0 HEADER ('Section' 62.18 / 'Code' 42.39, incl. colHdr padding)"),
                (1, 40, "the 'FVU' HEADER (35.79 incl. colHdr padding)"),
                (2, c2Floor, "a 'dd-MMM-yyyy' deduction date (69.55 + 8px of cell margin)"),
                (3, 98, "a ₹99,99,99,999.00 amount paid/received — NO margin, so slot == track"),
                (4, 98, "a ₹99,99,99,999.00 TDS/TCS figure — NO margin, so slot == track"),
                (5, c5Floor, "the wrapped 'Rate %' HEADER's longest word (42.39 incl. colHdr padding)"),
            };

            foreach (var g in deductionGrids)
            {
                var band = g.Children.OfType<TextBlock>().Any(t => t.Text == "Rate %") ? "header band" : "data row";
                foreach (var (col, floor, cut) in deductionFloors)
                    Assert.True(g.ColumnDefinitions[col].ActualWidth >= floor,
                        $"{what} deduction {band} at {width}x{height}: c{col} arranged to "
                        + $"{g.ColumnDefinitions[col].ActualWidth:0.00} — below {floor} it silently truncates {cut} "
                        + "on a document the deductee FILES.");
            }
        }
        finally
        {
            window.Close();
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The item-invoice "Qty (Billed)" HEADER wraps. At <c>Width="78"</c> less the colHdr
    /// <c>Padding="8,4"</c> the text slot is 62px against a 79.17 DIP label, so it rendered "Qty (Bill" —
    /// width-invariant (the track is fixed, so no window size helps) and with no <c>TextTrimming</c>, so
    /// not even an ellipsis disclosed it. Wrapping costs zero width here (longest word "(Billed)" =
    /// 52.78 + 16 = 68.78, inside the same 78) and so starves no neighbour; widening would have taken
    /// 20px from the star Stock Item column, which is how the money columns elsewhere got cut.
    /// </summary>
    [Fact]
    public void Item_invoice_billed_qty_header_wraps_rather_than_clipping()
    {
        var headers = Doc.Value.Descendants(Av + "TextBlock")
            .Where(t => (string?)t.Attribute("Text") == "Qty (Billed)"
                        && ((string?)t.Attribute("Classes"))?.Contains("colHdr") == true)
            .ToList();

        Assert.NotEmpty(headers);
        foreach (var h in headers)
        {
            Assert.Equal("Wrap", (string?)h.Attribute("TextWrapping"));
            // Non-vacuity: if the width ever grows past the label's natural 79.17 + 16 padding, the wrap
            // is no longer load-bearing and this lock should be revisited rather than silently kept.
            Assert.Equal("78", (string?)h.Attribute("Width"));
        }
    }
}
