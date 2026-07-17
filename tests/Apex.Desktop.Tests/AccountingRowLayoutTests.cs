using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Apex.Desktop.Tests;

/// <summary>
/// Regression lock for the accounting-report row templates (MainWindow.axaml — the shared
/// ShowSingleAccountingGrid row and the Ledger Vouchers / Account Book row).
///
/// THE BUG THIS LOCKS: both templates put the Particulars text and the grey Secondary tag in a
/// HORIZONTAL StackPanel. A horizontal StackPanel measures its children at INFINITE width, so
/// TextTrimming can never engage — long text simply overflowed the Particulars cell and PAINTED OVER
/// the Debit/Credit amount, e.g. "Indirect Expen1,00,000.00". The leading digit became illegible, so
/// Rs 1,00,000 could be read as Rs 4,00,000: a financial misread on the most-viewed screen.
///
/// WHY IT ASSERTS BOUNDS AND NOT VISIBILITY: Avalonia's IsEffectivelyVisible stays TRUE when a parent
/// clips, so binding/visibility assertions are structurally blind to this defect class. Layout bounds
/// are not — an overflowing TextBlock reports a right edge beyond its cell. That is measurable in plain
/// headless (layout runs without a drawing backend), so this test does not depend on the Skia harness.
/// </summary>
public sealed class AccountingRowLayoutTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AccountingRowLayoutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRowLayout_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    /// <summary>
    /// Robert's own names are short ("Insurance"), which HIDES the defect entirely. Long names must be
    /// synthesized — no saved company on disk still carries them.
    ///
    /// The renamed "Insurance" (an Indirect Expense) is the ProfitAndLoss case's overflow driver, and it
    /// has to carry the whole load alone: P&L rows have NO Secondary tag, so — unlike Trial Balance, where
    /// a short name is pushed past the cell edge by its appended group tag — a P&L name overflows only if
    /// its OWN width exceeds the 298px report cell. The previous "Professional & Technical Consultancy
    /// Fees" measured 282px and FIT, so the P&L case stayed green even with the C1 bug reintroduced (the
    /// horizontal StackPanel that disables trimming) — it gave zero regression signal. This longer name
    /// measures ~430px, so reverting C1 overflows the P&L cell on the name alone and the P&L case BITES.
    /// (The fixed template trims any length to the cell, so the green case is unaffected by the length.)
    /// </summary>
    private MainWindowViewModel LongNames()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.Company!.FindLedgerByName("Insurance")!.Name = "Professional & Technical Consultancy and Advisory Retainer Fees";
        vm.Company!.FindLedgerByName("Global Traders")!.Name = "Southern Vantage Distributors LLP (Karnataka)";
        vm.Company!.FindLedgerByName("SBI Bank")!.Name = "HDFC Bank Limited — Current A/c 50200012345678";
        vm.ShowGateway();
        return vm;
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        // MainWindow.axaml hardcodes Width/Height, so the size must be re-asserted after Show().
        var win = new MainWindow { DataContext = vm };
        win.Width = 1920; win.Height = 1080;
        win.Show();
        win.Width = 1920; win.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        return win;
    }

    /// <summary>
    /// Every piece of text in the Particulars cell must stay INSIDE that cell. Returns the number of
    /// text runs actually measured, so a caller can prove the assertion was not vacuous.
    /// </summary>
    private static int AssertParticularsStayInTheirCell(MainWindow win, int amountColumnCount)
    {
        // The row template's outer Grid: Particulars in column 0, then the amount columns.
        var rows = win.GetVisualDescendants()
                      .OfType<Grid>()
                      .Where(g => g.ColumnDefinitions.Count == amountColumnCount + 1
                                  && g.Children.Any(c => Grid.GetColumn(c) == 0))
                      .ToList();

        Assert.NotEmpty(rows);

        var measured = 0;
        foreach (var row in rows)
        {
            var cell = row.Children.FirstOrDefault(c => Grid.GetColumn(c) == 0);
            if (cell is null || cell.Bounds.Width <= 0) continue;

            // The cell is either the trimming TextBlock itself (fixed) or a panel wrapping the two
            // TextBlocks (the bug). Handle both shapes so the test is meaningful either way.
            var texts = new List<TextBlock>();
            if (cell is TextBlock self) texts.Add(self);
            texts.AddRange(cell.GetVisualDescendants().OfType<TextBlock>());

            foreach (var t in texts)
            {
                if (t.Bounds.Width <= 0) continue;

                // Right edge of this text run, expressed in the Particulars cell's own coordinates.
                var rightEdge = t.TranslatePoint(new Point(t.Bounds.Width, 0), cell);
                if (rightEdge is null) continue;

                Assert.True(
                    rightEdge.Value.X <= cell.Bounds.Width + 0.5,
                    $"Text overflows the Particulars cell and paints over the amount column: " +
                    $"right edge {rightEdge.Value.X:F1} > cell width {cell.Bounds.Width:F1}.");
                measured++;
            }
        }

        return measured;
    }

    /// <summary>Trial Balance / Balance Sheet / P&amp;L share this row template (Grid "*,150,150").</summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.TrialBalance)]
    [InlineData(ReportKind.BalanceSheet)]
    [InlineData(ReportKind.ProfitAndLoss)]
    [InlineData(ReportKind.DayBook)]
    public void Accounting_report_particulars_never_paint_over_the_amount(ReportKind kind)
    {
        var vm = LongNames();
        vm.OpenReport(kind);
        var win = Show(vm);

        var measured = AssertParticularsStayInTheirCell(win, amountColumnCount: 2);
        Assert.True(measured > 0, "No text runs were measured — the assertion would have been vacuous.");
    }

    /// <summary>
    /// Ledger Vouchers / Account Book row template (Grid "*,130,130,140"). Here Particulars is the row
    /// IDENTITY ("date + voucher no") and Secondary is an unbounded counter-ledger name.
    /// </summary>
    [AvaloniaFact]
    public void Ledger_vouchers_particulars_never_paint_over_the_amount()
    {
        var vm = LongNames();
        vm.OpenAccountBook("Southern Vantage Distributors LLP (Karnataka)");
        var win = Show(vm);

        var measured = AssertParticularsStayInTheirCell(win, amountColumnCount: 3);
        Assert.True(measured > 0, "No text runs were measured — the assertion would have been vacuous.");
    }

    /// <summary>
    /// The Particulars cells of the Day Book's DATA rows, and nothing else.
    ///
    /// SCOPING MATTERS HERE. Searching the whole window for a date string is very nearly vacuous: the
    /// shell chrome renders two of its own April-2020 strings — the title bar's "Robert Transport
    /// Services — 01-Apr-2020 to 30-Apr-2020" and the status bar's "Current Date: 01-Apr-2020" — so a
    /// whole-window count starts at 2 before a single report row has rendered. Restricting to
    /// ListBoxItems drops the chrome; matching the row-template SHAPE (a star Particulars column plus
    /// two absolute amount columns) drops the top bar — "Auto,*,Auto", also a 3-column Grid — and the
    /// column header. The shape is matched rather than the exact "*,150,150" pixels on purpose: a
    /// legitimate future rebalance of the amount columns must not turn this into a confusing
    /// "expected 13, found 0" failure.
    ///
    /// THE CAST TO TextBlock IS LOAD-BEARING. It asserts the leftmost-priority SHAPE: column 0 must be
    /// the single trimming TextBlock. Under the rejected "*,Auto" alternative, column 0 holds an inner
    /// Grid instead, so every cell drops out here and the count assertion fails loudly.
    /// </summary>
    private static List<TextBlock> DayBookParticularsCells(MainWindow win) =>
        win.GetVisualDescendants()
           .OfType<ListBoxItem>()
           .SelectMany(item => item.GetVisualDescendants().OfType<Grid>())
           .Where(g => g.ColumnDefinitions.Count == 3
                       && g.ColumnDefinitions[0].Width.IsStar
                       && g.ColumnDefinitions[1].Width.IsAbsolute
                       && g.ColumnDefinitions[2].Width.IsAbsolute)
           .Select(g => g.Children.FirstOrDefault(c => Grid.GetColumn(c) == 0) as TextBlock)
           .Where(t => t is { Bounds.Width: > 0 })
           .Select(t => t!)
           .ToList();

    /// <summary>
    /// The ordered logical Run texts of a Particulars TextBlock — leftmost first.
    ///
    /// This is the LOGICAL run structure (Inlines), NOT the painted glyph runs. It is exactly the signal
    /// the headless platform provides faithfully: the model strings and their order. (XAML materialises a
    /// couple of whitespace-only Runs between the authored <c>&lt;Run&gt;</c>s; those are filtered where
    /// order matters, below.)
    ///
    /// WHY NOT THE PAINTED GLYPHS. The earlier version of this test reconstructed the painted text from
    /// <c>TextLayout.TextLines[].TextRuns</c> (ShapedTextRun). Glyph shaping needs Skia; under the
    /// committed plain-headless harness the fallback shaper has coarse, inflated metrics that trim the
    /// identity itself ("02-Apr-2020  Payment N…"), so a painted-glyph assertion is false-RED without
    /// Skia — and a Skia-dependent test goes RED on the Linux/macOS CI runners. This test therefore
    /// proves the same invariant from headless-safe signals only: the logical Run ORDER plus advance-width
    /// MEASUREMENT (the very path the sibling overflow test relies on).
    /// </summary>
    private static List<string> RunTexts(TextBlock t) =>
        (t.Inlines ?? new InlineCollection()).OfType<Run>().Select(r => r.Text ?? "").ToList();

    /// <summary>"02-Apr-2020  Payment No. 1" — the row identity that must be the leftmost (priority) run.</summary>
    private static readonly Regex DayBookIdentity =
        new(@"^\d{2}-[A-Za-z]{3}-\d{4}\s+\w+ No\. \d+", RegexOptions.Compiled);

    /// <summary>The date token alone ("02-Apr-2020") — the leading identity that must never be trimmed.</summary>
    private static readonly Regex DayBookDate =
        new(@"^\d{2}-[A-Za-z]{3}-\d{4}", RegexOptions.Compiled);

    /// <summary>
    /// Advance-width of <paramref name="text"/> in the SAME font as the live cell, measured off-tree.
    /// This is the identical measured-width path the overflow test reads from the laid-out cell — headless
    /// supplies advance widths (that is why the overflow test is headless-safe), so this is too.
    /// </summary>
    private static double MeasuredWidth(TextBlock like, string text)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontSize = like.FontSize,
            FontFamily = like.FontFamily,
            FontWeight = like.FontWeight,
            FontStyle = like.FontStyle,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>
    /// The Day Book's date + voucher number must survive trimming — WITHOUT reading a single painted glyph.
    ///
    /// The Particulars cell is ONE trimming TextBlock (CharacterEllipsis) whose Inlines are, in order,
    /// [date+voucher no] · [2-space gap] · [narration]. CharacterEllipsis trims from the RIGHT, so the
    /// leftmost run has priority: put the identity first and the narration is what ellipsises. Two
    /// headless-safe halves prove that, and the regressions each half catches are:
    ///
    ///   (a) ORDER — the first non-whitespace Run is the date+voucher (matches <see cref="DayBookIdentity"/>).
    ///       Catches runs reordered so the narration leads (which would erase the date), and — via the
    ///       TextBlock cast in <see cref="DayBookParticularsCells"/> plus the exact-13 count — the rejected
    ///       inner-Grid "*,Auto" construct that put a panel (not a single trimming TextBlock) in column 0.
    ///
    ///   (b) FIT — the date token measures inside the cell's text area, so the date can never be the part
    ///       trimmed; and the full logical content measures WIDER than that area, so trimming genuinely
    ///       engages (the assertion is not vacuous) and — the date being pinned left and fitting — the
    ///       narration on the right is necessarily what gets ellipsised. Both use advance-width only.
    ///
    /// (Under plain headless the fallback font is coarse enough that the whole identity "02-Apr-2020
    /// Payment No. 1" measures ~325px against a 298px cell; with a real font it fits. So (b) measures the
    /// DATE token, whose survival is the load-bearing financial invariant, not the full prefix.)
    /// </summary>
    [AvaloniaFact]
    public void Day_book_rows_keep_their_date_and_voucher_number_painted()
    {
        var vm = LongNames();
        vm.OpenReport(ReportKind.DayBook);
        var win = Show(vm);

        var cells = DayBookParticularsCells(win);

        // CALIBRATION. Robert is a frozen deterministic fixture: his April-2020 Day Book holds exactly
        // 13 vouchers, and at 1080px tall all 13 realize. An exact count is the point, and the cast to
        // TextBlock inside DayBookParticularsCells is load-bearing: the rejected inner-Grid construct puts
        // a panel in column 0, which drops every cell here and fails this count loudly ("expected 13,
        // found 0") rather than silently.
        Assert.Equal(13, cells.Count);

        var failures = new List<string>();
        foreach (var cell in cells)
        {
            var runs = RunTexts(cell);
            var identity = runs.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

            // (a) ORDER: the date + voucher number is the leftmost (priority) run.
            if (!DayBookIdentity.IsMatch(identity))
            {
                failures.Add($"leftmost run is not the date+voucher identity: '{identity}'");
                continue;
            }

            // (b) FIT: measured against the cell's own text area (Bounds minus the TextBlock's margins).
            var textArea = cell.Bounds.Width - (cell.Margin.Left + cell.Margin.Right);
            var dateWidth = MeasuredWidth(cell, DayBookDate.Match(identity).Value);
            var fullWidth = MeasuredWidth(cell, string.Concat(runs));

            if (dateWidth > textArea + 0.5)
                failures.Add($"date token width {dateWidth:F1} exceeds the {textArea:F1}px cell — the date itself would be trimmed: '{identity}'");
            else if (fullWidth <= textArea)
                failures.Add($"full content width {fullWidth:F1} fits the {textArea:F1}px cell — nothing trims, so 'the date survives trimming' is vacuous: '{identity}'");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {cells.Count} Day Book row(s) do not keep their date + voucher number painted:\n  "
            + string.Join("\n  ", failures));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException) { }
    }
}
