using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// CA-audit layout locks — D6 (Batch-wise item column), D10 (Employee list gutter) and D8 (statutory
/// bank picker truncation), each measured on the POPULATED fixture before the fix and re-measured after.
///
/// <para><b>Why none of these assert a text width.</b> The committed plain-headless harness resolves NO
/// font: every family collapses to a stub glyph ~82% wider than the real Consolas the app actually uses
/// (<c>MainWindow.axaml</c> sets <c>FontFamily="Consolas, …"</c> on the root Window). A width assertion
/// here would measure a fiction that neither matches the shipped app nor the CI runners. So these lock
/// COLUMN-TRACK arithmetic and RECTANGLE gaps — both font-independent, because every cell TextBlock
/// stretches to its track and its rect therefore follows from the track width and its own Margin — plus,
/// for the one property that is inherently non-geometric (<c>TextTrimming</c>), a static XAML assertion
/// in the style of <see cref="XamlLayoutInvariantTests"/>. No Skia, no <c>CaptureRenderedFrame</c>, no
/// <c>TextLayout</c>/<c>TextRuns</c>: green on the 3-OS CI runners, which have no SkiaSharp.</para>
/// </summary>
public sealed class CaAuditLayoutLockTests
{
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
    /// Opens the REALISTICALLY POPULATED fixture (28 stock items with batches, 8 employees, 38 ledgers —
    /// longest stock item 62 chars) through the real company-select path at an EXPLICIT window size. A thin
    /// seed would make every assertion below vacuous: a starved column only hurts when it has real names in it.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) OpenPopulated(int width, int height)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCaAudit_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        storage.Save(PopulatedCompanyFixture.BuildRegular());

        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();

        vm.ShowCompanySelect();
        vm.Menu.First(m => m.Label == PopulatedCompanyFixture.RegularCompanyName).Activate();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string tempDir)
    {
        window.Close();
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
    }

    // ============================================================ D6 — Batch-wise item column

    /// <summary>
    /// The Batch-wise grid's nine tracks. The eight FIXED tracks are the budget; whatever they leave is all
    /// the <c>*</c> Item column ever gets, at EVERY window size, because the grid is pinned at
    /// <c>MinWidth="1080"</c> whenever the pane is narrower.
    /// </summary>
    private const double BatchwiseFixedBudget = 840.0;   // 110+95+95+120+100+100+100+120 (was 900)
    private const double BatchwiseItemFloor = 200.0;     // measured 240 after the fix, 180 before

    /// <summary>
    /// D6 — 🔴 the Item column must keep a readable share of the Batch-wise grid.
    ///
    /// <para><b>Measured before the fix</b> (Regular fixture, Batch-wise report): the eight fixed tracks spent
    /// <b>900px</b> of the 1080px floor — <c>Batch 120 | Mfg 110 | Expiry 110 | Godown 140 | Inward 100 |
    /// Outward 100 | Closing 100 | Value 120</c> — leaving the <c>*</c> Item column just <b>180px</b> at
    /// 1280x720 / 1366x768 / 1440x900 / 1600x900 and 447px at 1920x1080, to hold stock-item names up to 62
    /// characters. Mfg and Expiry each render a 9-character date ("01-Apr-25") and were 110px wide; Godown was
    /// 140. Trimming those four over-generous fixed tracks to 110/95/95/120 returns 60px to the Item column at
    /// EVERY size: <b>180 -> 240</b> at the four constrained sizes and <b>447 -> 507</b> at 1920x1080.</para>
    ///
    /// <para>The grid's own <c>MinWidth</c> is deliberately UNCHANGED at 1080. Lowering it to 980 was tried and
    /// REJECTED by measurement: it made the Item column NARROWER at the constrained sizes (240 -> 140), buying
    /// 100px of reduced horizontal overflow at the cost of the very readability this locks. The rightmost
    /// Value column still needs the body's horizontal scroll below a ~1470px window; that scroller is real and
    /// user-operable (<c>HorizontalScrollBarVisibility="Auto"</c>, maxOffset 366px at 1280x720), which is what
    /// separates this from the unreachable-content defect class.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1440, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Batchwise_item_column_keeps_a_readable_share(int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.OpenReport(ReportKind.Batchwise);
            Pump(window);

            // NON-VACUITY: a Batch-wise report with no batch rows would make any column assertion meaningless.
            Assert.True(
                vm.Reports!.Rows.Count > 1,
                $"Batch-wise produced {vm.Reports.Rows.Count} row(s) on the populated fixture — the fixture no "
                + "longer carries batch-tracked stock, so this lock would be vacuous.");

            var grids = Descendants(window).OfType<Grid>()
                .Where(g => g.IsEffectivelyVisible && g.ColumnDefinitions.Count == 9
                            && Math.Abs(g.MinWidth - 1080.0) < 0.5)
                .ToList();
            Assert.True(
                grids.Count >= 2,
                $"Found {grids.Count} Batch-wise 9-column grid(s) at MinWidth 1080 (expected the header twin AND "
                + "at least one row twin). The body was restructured, so this lock cannot anchor.");

            foreach (var grid in grids)
            {
                var fixedSum = grid.ColumnDefinitions.Skip(1).Sum(c => c.ActualWidth);
                Assert.True(
                    fixedSum <= BatchwiseFixedBudget + 0.5,
                    $"Batch-wise fixed tracks spend {fixedSum:F0}px at {width}x{height}, over the "
                    + $"{BatchwiseFixedBudget:F0}px budget. Every px here is taken straight from the '*' Item "
                    + "column, which holds 62-character stock-item names.");

                Assert.True(
                    grid.ColumnDefinitions[0].ActualWidth >= BatchwiseItemFloor,
                    $"The Batch-wise Item column is {grid.ColumnDefinitions[0].ActualWidth:F0}px at "
                    + $"{width}x{height}, under the {BatchwiseItemFloor:F0}px floor — stock-item names are "
                    + "ellipsized into unreadability while the fixed date/godown tracks hold slack.");
            }
        }
        finally { Cleanup(window, tempDir); }
    }

    // ============================================================ D10 — Employee list gutter

    /// <summary>
    /// D10 — the Employee list's Group and Designation values must not abut.
    ///
    /// <para><b>Measured before the fix:</b> in the existing-employees row template
    /// (<c>ColumnDefinitions="*,180,160,90"</c>) the Employee cell carried <c>Margin="8,0"</c> and the Regime
    /// cell <c>Margin="0,0,10,0"</c>, but <b>Group (col 1) and Designation (col 2) carried NO Margin at all</b>.
    /// A cell TextBlock has no <c>HorizontalAlignment</c>, so it STRETCHES to its whole track: Group's rect ran
    /// to the exact pixel where Designation's rect began — a ZERO-pixel gutter, so a Group value that filled its
    /// 180px track read as one continuous string with the Designation beside it. They were also the only two
    /// cells misaligned with their own <c>colHdr</c> headers, whose Padding is "8,4".</para>
    ///
    /// <para>Asserting the RECT GAP rather than any text measurement is what keeps this font-independent: both
    /// TextBlocks stretch to their tracks, so the gap is a pure function of the track widths and the margins —
    /// identical under the headless stub font and the real Consolas.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Employee_list_group_and_designation_do_not_abut(int width, int height)
    {
        var (window, vm, tempDir) = OpenPopulated(width, height);
        try
        {
            vm.ShowEmployeeMaster();
            Pump(window);

            var master = vm.EmployeeMaster;
            Assert.True(master != null, "Employee Creation did not open.");
            // NON-VACUITY: the gutter can only be measured on a REALISED row.
            Assert.True(
                master!.Existing.Count > 0,
                "The populated fixture produced no existing employees, so no list row realises and this lock "
                + "would be vacuous.");

            // Scoped to the ROW template by its Height="24". The header band shares the same 4-track spec but
            // its cells are Classes="colHdr", whose 8px inset is PADDING (inside the rect), so header rects
            // legitimately touch and would make this assertion fire on a non-defect.
            var rowGrids = Descendants(window).OfType<Grid>()
                .Where(g => g.IsEffectivelyVisible && g.ColumnDefinitions.Count == 4
                            && Math.Abs(g.Height - 24.0) < 0.5
                            && Math.Abs(g.ColumnDefinitions[1].ActualWidth - 180.0) < 0.5
                            && Math.Abs(g.ColumnDefinitions[2].ActualWidth - 160.0) < 0.5
                            && g.Children.OfType<TextBlock>().Count() == 4)
                .ToList();
            Assert.True(
                rowGrids.Count > 0,
                $"No realised Employee list row (4 tracks, 180/160 Group/Designation) at {width}x{height}; "
                + "the row template changed, so this lock cannot anchor.");

            foreach (var row in rowGrids)
            {
                var cells = row.Children.OfType<TextBlock>()
                    .ToDictionary(t => Grid.GetColumn(t), t => t);
                Assert.True(cells.ContainsKey(1) && cells.ContainsKey(2),
                    "The Employee row no longer has TextBlocks at columns 1 (Group) and 2 (Designation).");

                var group = cells[1];
                var desig = cells[2];
                var groupRight = group.TranslatePoint(default, window)!.Value.X + group.Bounds.Width;
                var desigLeft = desig.TranslatePoint(default, window)!.Value.X;
                var gutter = desigLeft - groupRight;

                Assert.True(
                    gutter >= 8.0,
                    $"Group and Designation leave a {gutter:F0}px gutter at {width}x{height} (need >= 8). "
                    + "Both cells stretch to their tracks, so with no Margin their rects touch and the two "
                    + "values read as a single string.");
            }
        }
        finally { Cleanup(window, tempDir); }
    }

    // ============================================================ D8 — statutory bank picker

    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    /// <summary>
    /// D8 — 🔴 the statutory Pay-From bank picker must ELLIPSIZE, never hard-cut.
    ///
    /// <para><b>Measured before the fix</b> (TDS Stat Payment, TDS enabled on the populated fixture): the
    /// Pay-From ComboBox is column 3 of a fixed <c>"160,*,160,*"</c> form row and resolves to <b>147-160px at
    /// ALL FOUR window sizes</b> — it never widens. The selected ledger was
    /// <c>"Bank of Maharashtra — CC A/c 60123456789 (Bhosari)"</c> (50 chars), and the item template was a bare
    /// <c>&lt;TextBlock Text="{Binding Name}"/&gt;</c> with NO <c>TextTrimming</c>, so the name was HARD-CUT
    /// mid-word to "Bank of Maharas" with no ellipsis. A truncation with no visual cue is worse than a visible
    /// one: a half-shown name reads as a COMPLETE and different bank, and this is the screen that deposits
    /// statutory TDS/TCS — picking the wrong bank is a real-money error.</para>
    ///
    /// <para>The <c>"160,*,160,*"</c> track spec is deliberately NOT widened: the three grids in that form are
    /// stacked rows that must stay column-aligned, so widening one would misalign the form. The fix is to make
    /// the truncation HONEST (ellipsis) and recoverable (ToolTip with the full name).</para>
    ///
    /// <para><c>TextTrimming</c> is a text-render property with no geometric proxy, so — unlike the two locks
    /// above — this is asserted statically over the .axaml, in the style of
    /// <see cref="XamlLayoutInvariantTests"/>. That keeps it font-independent and Skia-free.</para>
    /// </summary>
    [Fact]
    public void Statutory_bank_pickers_ellipsize_rather_than_hard_cut()
    {
        var path = AxamlPath();
        Assert.True(File.Exists(path), $"MainWindow.axaml not found at '{path}'.");
        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);

        var bankCombos = doc.Root!.DescendantsAndSelf()
            .Where(e => e.Name.LocalName == "ComboBox"
                        && e.Attribute("ItemsSource")?.Value.Replace(" ", "") == "{BindingBankOptions}")
            .ToList();

        // NON-VACUITY: the TDS and TCS deposit screens plus the payment picker carry one each today. If a
        // rename made this set empty the assertion below would pass while proving nothing.
        Assert.True(
            bankCombos.Count >= 3,
            $"Only {bankCombos.Count} bank-picker ComboBox(es) bound to BankOptions (expected >= 3). The "
            + "statutory deposit screens were restructured, so this lock would be vacuous.");

        var untrimmed = bankCombos
            .SelectMany(c => c.DescendantsAndSelf().Where(e => e.Name.LocalName == "TextBlock"))
            .Where(t => t.Attribute("TextTrimming") is null)
            .ToList();

        Assert.True(
            untrimmed.Count == 0,
            $"{untrimmed.Count} bank-picker TextBlock(s) declare no TextTrimming, so a ledger name wider than "
            + "the ~147px Pay-From cell is HARD-CUT with no ellipsis and a half-shown bank reads as a complete "
            + "one — on a statutory deposit screen:\n"
            + string.Join("\n", untrimmed.Select(t =>
                $"  MainWindow.axaml({((System.Xml.IXmlLineInfo)t).LineNumber}): "
                + $"Text=\"{t.Attribute("Text")?.Value}\"")));
    }
}
