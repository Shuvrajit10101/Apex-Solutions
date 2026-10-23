using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A COMPARATIVE REPORT'S FIGURES SURVIVE ONTO THE PRINTED PAGE — ASSERTED ON THE EMITTED PDF BYTES.</b>
///
/// <para><b>The defect these lock, measured on real artefacts rather than predicted.</b> The comparative print
/// branch emitted each column's TOTAL as a data row inside a right-aligned column, and that total is a
/// reconciliation sentence — <c>"Open 1,00,000.00 · Net 37,000.00 In · Close 1,37,000.00"</c>, fifty-odd
/// characters. <c>ReportPdf.IsFigureColumn</c> is <c>Align == Right</c> and a figure column's width FLOOR is its
/// full requirement, so every comparison column's floor was set by that sentence instead of by a money figure.
/// Past a handful of columns the floors alone exceeded the sheet, the renderer took its own documented
/// "not even the floors fit, nothing can be printed whole" branch, and the page came out unreadable: at the
/// default Alt+N <b>monthly</b> Cash Flow gesture (14 print columns) every Particulars cell drew as literally
/// <c>"..."</c> and every figure as <c>"37,000...."</c> — <b>with the In/Out suffix clipped off, so a clipped
/// outflow was typographically identical to an inflow.</b> Weekly (55 columns) drew the single characters
/// <c>0,1,2,3</c>; Daily (367 columns) produced a 1142-byte PDF; a comparative Trial Balance rendered 0 of 12
/// labels and 0 of 12 figures. The suite was green throughout because the only print assertion read the
/// <see cref="PrintReport"/> MODEL and never rendered a page.</para>
///
/// <para><b>Why every assertion here is on the drawn text.</b> A clipped cell does not exist in the print model
/// at all — it is created by <c>PdfWriter.FitToWidth</c> during drawing. So these render through the production
/// <see cref="ReportPdf"/> at the production <see cref="PageConfig"/> (the one <c>ExportViewModel</c> hands the
/// PDF format and <c>PrintPreviewViewModel</c> builds for Ctrl+P) and read the literal <c>Tj</c> operands back
/// out of the bytes. A fix that moves the model around without widening anything cannot satisfy them.</para>
///
/// <para><b>The invariant is deliberately asymmetric, matching the renderer's own documented rule.</b> A FIGURE
/// is never cut — a clipped money cell reads as a real, smaller amount, and a clipped <c>" Out"</c> reads as an
/// inflow. PROSE may abbreviate: a ledger name has no correct width and a reader who sees its first dozen
/// characters can ask for the rest. So the row labels are held to "it says something recognisable", not to
/// "it says everything", while every figure is held to the byte.</para>
/// </summary>
public sealed class ComparativePrintedPageTests
{
    private static readonly DateOnly BooksBegin = new(2020, 4, 1);
    private static readonly DateOnly YearEnd = new(2021, 3, 31);

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    /// <summary>The shipped default page configuration for a report print / PDF export — A4 portrait with the
    /// footer <c>PrintPreviewViewModel.BuildConfig</c> builds when the operator configures nothing. Portrait is
    /// deliberate: <c>ReportPdf.FitOrientation</c> is part of what is under test and must be allowed to act.</summary>
    private static PageConfig ShippedDefault() => new()
    {
        Size = PageSize.A4,
        Orientation = PageOrientation.Portrait,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };

    public static TheoryData<ReportKind, ColumnPeriodicity> KindAndPeriodicity()
    {
        var data = new TheoryData<ReportKind, ColumnPeriodicity>();
        foreach (var kind in new[] { ReportKind.CashFlow, ReportKind.FundsFlow, ReportKind.TrialBalance, ReportKind.BalanceSheet })
            foreach (var p in new[] { ColumnPeriodicity.Quarterly, ColumnPeriodicity.Monthly, ColumnPeriodicity.Weekly })
                data.Add(kind, p);
        return data;
    }

    /// <summary>
    /// 🔴 THE LOCK. Press Alt+N at a periodicity, render the real PDF, and require that EVERY figure the print
    /// model carries is drawn verbatim — sign suffix and all — and that every row says what it is about.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindAndPeriodicity))]
    public void Every_comparative_figure_is_drawn_whole_on_the_printed_page(ReportKind kind, ColumnPeriodicity periodicity)
    {
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, YearEnd);
        Assert.True(vm.AutoColumnsByPeriodicity(periodicity), "Alt+N produced no columns");
        Assert.True(vm.IsComparative);

        var print = ReportPrintProjector.Project(vm);
        var drawn = DrawnStrings(ReportPdf.Render(print, ShippedDefault()));

        // ---- every figure, verbatim. The figure cells are every cell of every non-stub column.
        var figures = new List<string>();
        foreach (var row in print.Rows)
            for (int i = 1; i < row.Cells.Count; i++)
                if (!string.IsNullOrWhiteSpace(row.Cells[i])) figures.Add(row.Cells[i]);

        Assert.NotEmpty(figures);

        var missing = figures.Where(f => !drawn.Contains(f)).Distinct().ToList();
        Assert.True(
            missing.Count == 0,
            $"{kind} / {periodicity}: {missing.Count} distinct figure(s) of {figures.Distinct().Count()} did NOT "
            + "reach the printed page whole. First few: " + string.Join(" | ", missing.Take(6))
            + ". Cut cells actually drawn: " + string.Join(" | ", drawn.Where(IsCut).Distinct().Take(8))
            + ". A clipped money cell reads as a real, smaller amount, and a clipped ' Out' reads as an inflow.");

        // ---- no cell was drawn as nothing but an ellipsis, on any sheet.
        Assert.DoesNotContain("...", drawn);

        // ---- every row says what it is about. Prose may abbreviate; it may not vanish.
        foreach (var row in print.Rows)
        {
            string label = row.Cells.Count > 0 ? row.Cells[0] ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(label)) continue;
            string stem = label[..Math.Min(8, label.Length)];
            Assert.True(
                drawn.Any(d => d.StartsWith(stem, StringComparison.Ordinal)),
                $"{kind} / {periodicity}: the printed page does not carry even the first {stem.Length} "
                + $"characters of the row '{label}'.");
        }
    }

    /// <summary>
    /// 🔴 <b>THE FIGURES REACH PAPER IN MONEY-WIDTH COLUMNS, NOT ONE COLUMN TO A SHEET — WHICH IS WHAT THE
    /// SHORT PER-METRIC TOTAL ROWS BUY, AND NOTHING ELSE IN THIS FILE OBSERVES IT.</b>
    ///
    /// <para>This assertion exists because a mutation found a hole in the rest of the file. Reverting the
    /// per-metric total rows — putting the joined reconciliation sentence back into the comparison columns —
    /// left every printed-page test above GREEN, because the column split then simply deals the sheet into
    /// narrower and narrower groups until even a fifty-character cell fits. The figures survive; the document
    /// does not. At the default Alt+N monthly gesture the sentence sets each column's floor to ~230pt against
    /// A4 portrait's 523.276pt of content, so each sheet holds ONE comparison column and the thirteen months
    /// print on thirteen sheets instead of four.</para>
    ///
    /// <para>So the artefact's own SHEET COUNT is held, measured at 4 for this gesture and bounded generously at
    /// 8 — the bound is deliberately loose, because the exact count moves with the money figure's width and the
    /// page, and pinning 4 would make this a transcription of today's arithmetic. Thirteen is far outside it.
    /// Counted from the emitted bytes (<c>/Type /Page</c> objects), not from a model.</para>
    /// </summary>
    [Fact]
    public void The_default_monthly_comparative_prints_on_a_handful_of_sheets_not_one_per_column()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);
        vm.SetPeriod(BooksBegin, YearEnd);
        Assert.True(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));
        Assert.Equal(13, vm.ComparativeColumns.Count);   // the whole-period base column + 12 months

        var pdf = ReportPdf.Render(ReportPrintProjector.Project(vm), ShippedDefault());
        var drawn = DrawnStrings(pdf);

        // 🔴 NO RECONCILIATION SENTENCE REACHES A COMPARISON COLUMN. This is the direct byte-level statement of
        // the fix, and it reddens whatever the sheet count does: the joined line is "Open X · Net Y · Close Z",
        // which the projector ASCII-folds to "Open X - Net Y - Close Z", so a drawn cell carrying " - Net " is
        // the sentence arriving in a money column.
        var sentences = drawn.Where(d => d.Contains(" - Net ", StringComparison.Ordinal)
                                      || d.Contains(" - Cr ", StringComparison.Ordinal)
                                      || d.Contains(" - Assets ", StringComparison.Ordinal)
                                      || d.Contains(" - Applic. ", StringComparison.Ordinal)).ToList();
        Assert.True(
            sentences.Count == 0,
            "a joined column-total sentence was drawn inside a comparison column: "
            + string.Join(" | ", sentences.Take(4))
            + ". That cell's width sets the column's print FLOOR, and thirteen such floors do not fit a sheet.");

        // And the consequence on the artefact: the thirteen months come out on a handful of sheets. The bound is
        // deliberately loose — the exact count moves with the money figures in the book and with the page, and
        // on this fixture it is ONE (the whole grid fits a turned sheet once the totals are money-width).
        // Thirteen — one comparison column per sheet, which is what the joined sentence forces — is far outside
        // it, so this reddens on that mutation without pinning today's arithmetic.
        int sheets = SheetCount(pdf);
        Assert.True(
            sheets <= 8,
            $"the 13-column monthly comparative printed on {sheets} sheets. Past about eight the cause is not "
            + "paper: it is a comparison column whose widest CELL is a reconciliation sentence rather than a "
            + "money figure, which sets that column's floor and leaves room for nothing else.");
        Assert.True(sheets >= 1);
    }

    /// <summary>
    /// 🔴 THE SIDE SUFFIX IS THE WHOLE READING OF A FLOW STATEMENT, so it is held on its own. Every drawn Cash
    /// Flow figure that the model marked " In" or " Out" is drawn WITH that word: a figure whose suffix was
    /// clipped is not a cosmetic loss, it is an outflow that reads as an inflow.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.CashFlow, " In", " Out")]
    [InlineData(ReportKind.FundsFlow, " Src", " App")]
    public void The_side_suffix_of_every_flow_cell_survives_printing(ReportKind kind, string positive, string negative)
    {
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, YearEnd);
        Assert.True(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Monthly));

        var print = ReportPrintProjector.Project(vm);
        var drawn = DrawnStrings(ReportPdf.Render(print, ShippedDefault()));

        int expected = 0;
        foreach (var row in print.Rows)
            for (int i = 1; i < row.Cells.Count; i++)
            {
                string cell = row.Cells[i] ?? string.Empty;
                if (!cell.EndsWith(positive, StringComparison.Ordinal)
                    && !cell.EndsWith(negative, StringComparison.Ordinal)) continue;
                expected++;
                Assert.True(
                    drawn.Contains(cell),
                    $"{kind}: the signed cell '{cell}' was not drawn on the page. A clipped side suffix makes "
                    + "an outflow typographically identical to an inflow.");
            }

        Assert.True(expected > 0, $"{kind}: the fixture produced no signed flow cell to assert on.");
    }

    /// <summary>
    /// 🔴 THE PAGE IS A REPORT, NOT A STUB. Daily over a financial year is 365 buckets; the branch's PDF for it
    /// was 1142 bytes — a single sheet of ellipses. Whatever the sheet count, the drawn text must carry the
    /// figures, and the document must be large enough to be one.
    /// </summary>
    [Fact]
    public void A_daily_comparative_year_prints_its_figures_rather_than_a_single_stub_sheet()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);
        vm.SetPeriod(BooksBegin, YearEnd);
        Assert.True(vm.AutoColumnsByPeriodicity(ColumnPeriodicity.Daily));
        Assert.Equal(366, vm.ComparativeColumns.Count);   // the whole-period base column + 365 days

        var print = ReportPrintProjector.Project(vm);
        var pdf = ReportPdf.Render(print, ShippedDefault());
        var drawn = DrawnStrings(pdf);

        Assert.True(pdf.Length > 20_000, $"the daily comparative PDF is only {pdf.Length} bytes");
        Assert.DoesNotContain("...", drawn);

        var figures = print.Rows
            .SelectMany(r => r.Cells.Skip(1))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();
        Assert.NotEmpty(figures);
        Assert.All(figures, f => Assert.Contains(f, drawn));
    }

    /// <summary>
    /// 🔴 ER-13 — A SINGLE-COLUMN REPORT'S BYTES DO NOT MOVE. Every non-comparative report must render exactly
    /// the bytes it rendered before the sheet seam existed, so the seam cannot have cost anything anywhere else.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.TrialBalance)]
    [InlineData(ReportKind.BalanceSheet)]
    [InlineData(ReportKind.CashFlow)]
    [InlineData(ReportKind.Gstr1)]
    [InlineData(ReportKind.DayBook)]
    public void A_single_column_report_renders_the_same_bytes_through_the_sheet_seam(ReportKind kind)
    {
        var vm = new ReportsViewModel(Robert(), kind);
        vm.SetPeriod(BooksBegin, YearEnd);
        Assert.False(vm.IsComparative);

        var print = ReportPrintProjector.Project(vm);

        // The split seam hands a document that fits back as THE SAME INSTANCE, which is the exact test — not an
        // approximation — that nothing in the shipped repertoire moved.
        var slices = ReportPdf.SplitWideColumns(print, ShippedDefault());
        Assert.Same(print, Assert.Single(slices));
        Assert.Equal(ReportPdf.Render(print, ShippedDefault()), ReportPdf.Render(slices, ShippedDefault()));
    }

    /// <summary>The number of sheets a PDF carries, counted from its own page objects.</summary>
    private static int SheetCount(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int count = 0;
        for (int i = text.IndexOf("/Type /Page", StringComparison.Ordinal); i >= 0;
             i = text.IndexOf("/Type /Page", i + 1, StringComparison.Ordinal))
        {
            // "/Type /Pages" is the page TREE, not a sheet.
            if (i + 11 < text.Length && text[i + 11] == 's') continue;
            count++;
        }
        return count;
    }

    /// <summary>A drawn cell that was cut short — <c>PdfWriter.FitToWidth</c> appends an ellipsis when it has to
    /// clip.</summary>
    private static bool IsCut(string drawnCell) => drawnCell.EndsWith("...", StringComparison.Ordinal);

    /// <summary>
    /// The strings a PDF actually DREW, in order. <c>Apex.Ledger.Io</c> writes uncompressed content streams, so
    /// each <c>Tj</c> operand is the literal text of one cell, already through <c>FitToWidth</c>. The parse walks
    /// forward from an unescaped <c>'('</c> and consumes <c>\x</c> pairs, because <c>PdfWriter.EscapeString</c>
    /// emits <c>\(</c> and <c>\)</c> inside strings — scanning back from <c>") Tj"</c> instead splits any cell
    /// containing a parenthesis and manufactures a cut that does not exist. Same parse as
    /// <c>PrintedMoneyColumnWidthTests.DrawnStrings</c>, which is where that lesson was learned.
    /// </summary>
    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            if (i > 0 && text[i - 1] == '\\') continue;

            var sb = new StringBuilder();
            int j = i + 1;
            bool closed = false;
            for (; j < text.Length; j++)
            {
                char ch = text[j];
                if (ch == '\\')
                {
                    if (j + 1 < text.Length) sb.Append(text[++j]);
                    continue;
                }
                if (ch == ')') { closed = true; break; }
                sb.Append(ch);
            }

            if (!closed || j + 3 >= text.Length || text.Substring(j + 1, 3) != " Tj") continue;
            drawn.Add(sb.ToString());
            i = j + 3;
        }
        return drawn;
    }
}
