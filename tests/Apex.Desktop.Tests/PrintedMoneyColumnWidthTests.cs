using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>NO PRINTED MONEY FIGURE IS EVER CUT MID-NUMBER — ASSERTED ON THE EMITTED PDF BYTES, ON EVERY REPORT
/// KIND THAT DECLARES A COLUMN BAND.</b>
///
/// <para><b>The defect these lock, measured rather than predicted.</b> <c>ReportPrintProjector.ProjectBanded</c>
/// gave every figure column the fixed relative weight <c>1.5</c>, every non-leading label column <c>2.2</c> and
/// the leading label column <c>3</c>; <c>ReportPdf.ComputeColumnX</c> then split the content width in proportion
/// to those weights and <c>ReportPdf.DrawRowCells</c> clipped each cell with <c>PdfWriter.FitToWidth</c>, which
/// appends an ellipsis. Nothing in that chain knows how wide a rupee figure is, so on a band with enough columns
/// the figure column was narrower than the figure and the number was cut mid-number on the printed page.</para>
///
/// <para><b>The arithmetic at the shipped default</b> (A4 portrait, 36pt margins ⇒ 523.276pt of content, 9pt
/// Helvetica body, 2pt cell padding each side). <c>99,99,99,999.00</c> — the widest figure an Indian money column
/// can hold below a hundred crore, in the grouped form the report rows actually carry — measures
/// 11×556 + 4×250 = 7116 units ⇒ <b>64.04pt</b> through <c>PdfWriter.MeasureHelvetica</c>. A figure column's inner
/// width is 523.276 × 1.5 ÷ W − 4, where W is the band's total weight, so the figure needs W ≤ 11.53. Measured
/// against the shipped bands, <b>26 of the 37 banded kinds exceeded it</b>. The worst was GSTR-1 (four label
/// columns + five figure columns ⇒ W = 17.1, inner width <b>41.90pt</b>), where even <c>1,00,000.00</c> — 46.78pt
/// — was cut: a filed GST return printed with its taxable value and all four tax figures truncated from one lakh
/// upward. Order Register, the five allocation/material registers, both job-work order books, Batchwise, Batch Age
/// Analysis, Price List, both bills-pending registers, Reorder Status, Physical Stock Register, both CST
/// declaration-forms registers and eight of the TDS/TCS registers carried the same cut.</para>
///
/// <para><b>Why the assertion is on the bytes and not on a width.</b> This project has already shipped a printed
/// invoice total that differed from the posted total, and a money figure clipped mid-number, both under a green
/// suite — because the tests read the model that fed the renderer instead of the artefact it produced. Every
/// assertion below renders through the production <see cref="ReportPdf"/> at the production
/// <see cref="PageConfig"/> and reads the literal <c>Tj</c> operands out of the emitted PDF, which is the only
/// level at which a clipped cell exists at all. A relabelling or a reweighting that does not actually widen the
/// column cannot satisfy them.</para>
///
/// <para><b>What is deliberately NOT asserted.</b> A LABEL column may still ellipsise — a party name or a
/// narration is prose, it has no correct width, and cutting it loses nothing a reader cannot ask for again. The
/// invariant is narrower and is the one that matters on a document that goes to a bank or a tax officer: a
/// <i>figure</i> is never cut, because a cut figure is a WRONG figure and reads as a real, smaller amount.</para>
///
/// <para>🔴 <b>WHY THERE ARE TWO SWEEPS AND NOT ONE, WHICH IS THE WHOLE LESSON OF THIS FILE.</b> The first fix
/// for this defect redistributed only the SLACK — width held by columns needing less than their weight share —
/// and it made every case in this file pass. It was still broken: on real data with real party names the prose
/// columns hold no slack at all, so there is nothing to move and the figures cut anyway. The suite was green
/// because every case fed the renderer a one-character label. The second sweep,
/// <see cref="Every_banded_report_prints_the_widest_money_figure_whole_beside_realistic_labels"/>, puts a
/// 42-character party name in every label column, and it is the one that fails on 28 kinds when the prose floor
/// is taken out. <b>A sweep that only ever sees convenient input proves that the input was convenient.</b> If a
/// third shape of this defect is ever found, add a third sweep rather than loosening these.</para>
/// </summary>
public sealed class PrintedMoneyColumnWidthTests
{
    /// <summary>
    /// The widest figure an Indian money column of these reports must hold, in the grouped form the report rows
    /// carry (<c>IndianMoneyFormat.Amount</c> emits <c>#,##0.00</c> under the Indian grouping culture). Ninety-nine
    /// crore ninety-nine lakh ninety-nine thousand nine hundred and ninety-nine rupees.
    /// </summary>
    private const string WidestMoney = "99,99,99,999.00";

    /// <summary>
    /// The threshold figure at which the worst band (GSTR-1) was already cutting: one lakh. Kept as its own
    /// constant because "it only breaks at a hundred crore" would be a comfortable and false reading of this
    /// defect — the cut began three orders of magnitude lower.
    /// </summary>
    private const string OneLakh = "1,00,000.00";

    /// <summary>The shipped default page configuration for a report print preview: A4, portrait, the same footer
    /// <c>PrintPreviewViewModel.BuildConfig</c> builds when the operator configures nothing.</summary>
    private static PageConfig ShippedDefault() => new()
    {
        Size = PageSize.A4,
        Orientation = PageOrientation.Portrait,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };

    private static readonly Lazy<Company> Populated = new(() =>
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        new VatService(c).EnableVat(tin: "29123456789");
        return c;
    });

    public static TheoryData<ReportKind> BandedKinds()
    {
        var data = new TheoryData<ReportKind>();
        foreach (var k in Enum.GetValues<ReportKind>())
        {
            var vm = new ReportsViewModel(Populated.Value, k);
            if (!ReportColumnBands.NeedsBand(vm)) continue;
            if (ReportColumnBands.For(vm).Count == 0) continue;
            data.Add(k);
        }
        return data;
    }

    /// <summary>
    /// 🔴 THE LOCK. For every report kind that declares a column band: take the columns the production projector
    /// really emits, fill every figure column with the widest figure it must hold, render through the production
    /// renderer at the shipped default, and read the strings the PDF drew. Every figure must appear whole.
    /// </summary>
    [Theory]
    [MemberData(nameof(BandedKinds))]
    public void Every_banded_report_prints_the_widest_money_figure_whole(ReportKind kind)
    {
        var vm = new ReportsViewModel(Populated.Value, kind);
        var band = ReportColumnBands.For(vm);
        var projected = ReportPrintProjector.Project(vm);

        Assert.Equal(band.Count, projected.Columns.Count);

        // One row carrying the worst case each column must hold: the widest grouped rupee figure in every figure
        // column, a short label in every label column (so a clipped LABEL cannot be what reddens this).
        var cells = new string[band.Count];
        for (int i = 0; i < band.Count; i++) cells[i] = band[i].IsNumeric ? WidestMoney : "X";

        var report = new PrintReport
        {
            Title = projected.Title,
            Subtitle = projected.Subtitle,
            Columns = projected.Columns,
            Rows = new[] { new PrintRow { Cells = cells } },
        };

        var drawn = DrawnStrings(ReportPdf.Render(report, ShippedDefault()));
        int figureColumns = band.Count(b => b.IsNumeric);
        int whole = drawn.Count(s => s == WidestMoney);

        Assert.False(
            drawn.Any(IsCutFigure),
            $"{kind}: the printed page CUT a money figure mid-number. Drawn cells ending in an ellipsis: "
            + string.Join(" | ", drawn.Where(IsCutFigure))
            + $". The band has {figureColumns} figure column(s) and {whole} of them drew {WidestMoney} whole. "
            + "A cut figure is not a cosmetic clip: it reads as a real, smaller amount on a document that goes "
            + "to a bank, an auditor or a tax officer.");

        Assert.Equal(figureColumns, whole);
    }

    /// <summary>
    /// 🔴 <b>THE SAME LOCK WITH REALISTIC LABELS, WHICH IS THE HARDER CASE AND THE ONE THAT MATTERS.</b>
    ///
    /// <para>The sweep above fills label columns with <c>"X"</c>. That isolates the figure geometry cleanly, but it
    /// is also the most FAVOURABLE input the allocator can get: tiny labels mean large slack, and a lock that only
    /// ever sees tiny labels would pass while real output still cut. With a 42-character party name in every label
    /// column the label columns hold no slack at all, so the figure columns must be served either out of what
    /// little remains or by turning the page — and either way the invariant is the same one: <b>the figures are
    /// whole.</b> The labels may clip here, and that is allowed; <see cref="IsCutFigure"/> only flags a cell that
    /// is digits and separators, so a clipped party name cannot redden this and a clipped amount cannot hide.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(BandedKinds))]
    public void Every_banded_report_prints_the_widest_money_figure_whole_beside_realistic_labels(ReportKind kind)
    {
        const string LongLabel = "Brightline Industrial Supplies Private Ltd";   // 42 characters

        var vm = new ReportsViewModel(Populated.Value, kind);
        var band = ReportColumnBands.For(vm);
        var projected = ReportPrintProjector.Project(vm);

        var cells = new string[band.Count];
        for (int i = 0; i < band.Count; i++) cells[i] = band[i].IsNumeric ? WidestMoney : LongLabel;

        var report = new PrintReport
        {
            Title = projected.Title,
            Subtitle = projected.Subtitle,
            Columns = projected.Columns,
            Rows = new[] { new PrintRow { Cells = cells } },
        };

        var drawn = DrawnStrings(ReportPdf.Render(report, ShippedDefault()));
        int figureColumns = band.Count(b => b.IsNumeric);

        Assert.False(
            drawn.Any(IsCutFigure),
            $"{kind}: with realistic labels the printed page CUT a money figure mid-number: "
            + string.Join(" | ", drawn.Where(IsCutFigure))
            + ". Prose columns may clip; figure columns may not, and they are topped up FIRST for that reason.");

        Assert.Equal(figureColumns, drawn.Count(s => s == WidestMoney));
    }

    /// <summary>
    /// 🔴 THE FIGURE THAT MADE THIS URGENT RATHER THAN THEORETICAL: GSTR-1, the worst band, was cutting at ONE
    /// LAKH — not at a hundred crore. Pinned as its own test so a fix that merely widens the column enough for a
    /// lakh, leaving crores cut, still fails the sweep above while this one passes, and the two readings cannot be
    /// confused for each other.
    /// </summary>
    [Fact]
    public void Gstr1_the_worst_band_prints_a_one_lakh_taxable_value_whole()
    {
        var vm = new ReportsViewModel(Populated.Value, ReportKind.Gstr1);
        var band = ReportColumnBands.For(vm);
        var projected = ReportPrintProjector.Project(vm);

        var cells = new string[band.Count];
        for (int i = 0; i < band.Count; i++) cells[i] = band[i].IsNumeric ? OneLakh : "X";

        var report = new PrintReport
        {
            Title = projected.Title,
            Columns = projected.Columns,
            Rows = new[] { new PrintRow { Cells = cells } },
        };

        var drawn = DrawnStrings(ReportPdf.Render(report, ShippedDefault()));

        Assert.False(
            drawn.Any(IsCutFigure),
            "GSTR-1 printed a one-lakh figure cut mid-number: "
            + string.Join(" | ", drawn.Where(IsCutFigure)));
        Assert.Equal(band.Count(b => b.IsNumeric), drawn.Count(s => s == OneLakh));
    }

    /// <summary>
    /// 🔴 THE HEADINGS MUST SURVIVE THE WIDENING. Giving figure columns the width their figures need takes that
    /// width from the label columns, so the obvious way to pass the two tests above is to starve the leading
    /// Particulars column until the captions themselves are cut. The printed header band carries the captions a
    /// reader needs to know WHICH figure each column is, so it is held here: no caption may be ellipsised at the
    /// shipped default on any banded kind.
    /// </summary>
    [Theory]
    [MemberData(nameof(BandedKinds))]
    public void Widening_the_figure_columns_does_not_cut_a_column_caption(ReportKind kind)
    {
        var vm = new ReportsViewModel(Populated.Value, kind);
        var band = ReportColumnBands.For(vm);
        var projected = ReportPrintProjector.Project(vm);

        var cells = new string[band.Count];
        for (int i = 0; i < band.Count; i++) cells[i] = band[i].IsNumeric ? WidestMoney : "X";

        var report = new PrintReport
        {
            Title = projected.Title,
            Columns = projected.Columns,
            Rows = new[] { new PrintRow { Cells = cells } },
        };

        var drawn = DrawnStrings(ReportPdf.Render(report, ShippedDefault()));

        foreach (var column in projected.Columns)
        {
            if (column.Header.Length == 0) continue;
            Assert.Contains(column.Header, drawn);
        }
    }

    /// <summary>A drawn cell that is a money figure cut short: digits and separators, then the ellipsis
    /// <c>PdfWriter.FitToWidth</c> appends when it has to cut. "1,00,00..." is the shape; "Long party na..." is
    /// not, and is allowed.</summary>
    private static bool IsCutFigure(string drawnCell)
    {
        if (!drawnCell.EndsWith("...", StringComparison.Ordinal)) return false;
        string head = drawnCell[..^3];
        return head.Length > 0 && head.All(ch => char.IsAsciiDigit(ch) || ch is ',' or '.' or '-' or ' ');
    }

    /// <summary>
    /// The strings a PDF actually DREW, in order. <c>Apex.Ledger.Io</c> writes uncompressed content streams, so
    /// each <c>Tj</c> operand is the literal text of one cell — already through <c>FitToWidth</c>. Reading these
    /// is reading the artefact, not the model that fed it.
    ///
    /// <para>🔴 <b>THIS HONOURS <c>PdfWriter.EscapeString</c>'S BACKSLASH ESCAPES, AND THE SIMPLER VERSION
    /// MANUFACTURED A DEFECT THAT DID NOT EXIST.</b> Scanning back from <c>") Tj"</c> to the nearest <c>'('</c>
    /// splits any caption that itself contains a parenthesis — <c>PdfWriter.cs:328</c> emits those as <c>\(</c> and
    /// <c>\)</c> — so the Stock Item Cost Analysis caption "Cost (Expense)" was read as "Expense)" and this file's
    /// caption lock reported it CUT when the PDF had drawn it whole. A cut cell is a real defect and a mis-parsed
    /// operand looks exactly like one, so the parse walks forward from an unescaped <c>'('</c> and consumes
    /// <c>\x</c> pairs, which is what the format actually says.</para>
    /// </summary>
    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            if (i > 0 && text[i - 1] == '\\') continue;   // an escaped '(' INSIDE a string, not the start of one

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
