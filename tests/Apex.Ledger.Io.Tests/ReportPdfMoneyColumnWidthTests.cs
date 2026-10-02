using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>The renderer's money-column arithmetic, pinned — because a rationale comment got it wrong by measuring
/// the WRONG STRING, and that wrong number was the stated justification for a design decision.</b>
///
/// <para>The trap, in one sentence: the SCREEN shows a money amount in grouped Indian form
/// (<c>99,99,99,999.00</c>) and the PRINTED page does not. <c>ExportViewModel.TabularToPrint</c> takes
/// <see cref="TabularCell.NumberText"/> for a <see cref="CellType.Number"/> cell, and that is
/// <c>decimal.ToString(InvariantCulture)</c> — a dot, and <b>no grouping</b>. So the page carries
/// <c>999999999.00</c>. Measuring the on-screen string instead of the emitted one overstates every money column
/// in this renderer by three separator widths (~6.75pt at 9pt), which is the difference between "this column
/// clips" and "this column fits".</para>
///
/// <para>A comment in <c>Drc03PaymentViewModel.ToMasterListSnapshot</c> rejected a seventh, cash-amount column on
/// the ground that it would clip the FILED figures, citing 64.0pt against a 58.3pt cell. 64.04pt is the grouped
/// string; the emitted one is 57.29pt and does NOT clip. The decision to keep the balances off the tabular
/// artefact stands on its other reason — a snapshot carries one column set for every row — but the arithmetic is
/// asserted here so it cannot be restated wrongly from memory a fourth time.</para>
/// </summary>
public sealed class ReportPdfMoneyColumnWidthTests
{
    // The shipped default this arithmetic describes: A4 portrait (595.28pt wide), 36pt margins, 9pt body, and
    // ExportViewModel.TabularToPrint's weights — 2.4 for the label column, 1.0 for every other column.
    private const double BodyWidth = 595.28 - 72.0;   // 523.28
    private const double BodyFont = 9.0;
    private const double CellPad = 2.0;               // ReportPdf.DrawRowCells insets both sides

    // Weight 1.0 goes to every NON-LABEL column whatever its type — TabularToPrint keys the weight on the index,
    // not on CellType — so the divisor counts all of them, Text columns included. The DRC-03 register's six are
    // Cause / Period / Tax / Interest / Total / Demand Ref.: one label plus FIVE at weight 1.0, of which only three
    // are Number. Getting this wrong is how the arithmetic drifts.
    private static double InnerCellWidth(int nonLabelColumns)
        => BodyWidth / (2.4 + nonLabelColumns) - 2 * CellPad;

    /// <summary>The widest amount a DRC-03 money column must hold — ₹99,99,99,999.00 — as the PAGE receives it.</summary>
    private const string EmittedWidest = "999999999.00";

    /// <summary>The same amount as the SCREEN shows it. The renderer never draws this form.</summary>
    private const string GroupedWidest = "99,99,99,999.00";

    [Fact]
    public void A_money_cell_reaches_the_page_ungrouped_so_the_grouped_form_is_never_what_a_column_must_fit()
    {
        // This is the whole trap, asserted rather than trusted: the Number cell's emitted text has no separators.
        Assert.Equal(EmittedWidest, TabularCell.Number(999_999_999.00m).NumberText);
        Assert.DoesNotContain(",", TabularCell.Number(999_999_999.00m).NumberText);
    }

    [Fact]
    public void The_widest_emitted_money_string_measures_57_29pt_not_the_64_04pt_of_its_grouped_form()
    {
        Assert.Equal(57.29, PdfWriter.MeasureHelvetica(EmittedWidest, BodyFont), 2);
        Assert.Equal(64.04, PdfWriter.MeasureHelvetica(GroupedWidest, BodyFont), 2);

        // ~6.75pt of separators is the entire error, and it straddles the 58.30pt cell below.
        double overstatement = PdfWriter.MeasureHelvetica(GroupedWidest, BodyFont)
                             - PdfWriter.MeasureHelvetica(EmittedWidest, BodyFont);
        Assert.Equal(6.75, overstatement, 2);
    }

    [Fact]
    public void A_seventh_column_would_NOT_clip_the_filed_figures_it_would_leave_1_00pt_of_margin()
    {
        double inner7 = InnerCellWidth(6);            // the register's five, plus a cash-amount column
        Assert.Equal(58.30, inner7, 2);

        // The claim the comment used to make, refuted at the renderer's own seam: FitToWidth returns the string
        // UNTOUCHED, so nothing is cut and no ellipsis is appended.
        Assert.Equal(EmittedWidest, PdfWriter.FitToWidth(EmittedWidest, inner7, BodyFont));
        Assert.DoesNotContain(PdfWriter.Ellipsis, PdfWriter.FitToWidth(EmittedWidest, inner7, BodyFont));

        // It fits, but only just — 1.00pt, ~1.7% of the cell. THAT is the reason to refuse the seventh column.
        Assert.Equal(1.00, inner7 - PdfWriter.MeasureHelvetica(EmittedWidest, BodyFont), 2);

        // And the grouped form is what would NOT have fitted — the measurement the comment actually performed.
        Assert.NotEqual(GroupedWidest, PdfWriter.FitToWidth(GroupedWidest, inner7, BodyFont));
    }

    [Fact]
    public void At_the_shipped_six_columns_the_widest_money_figure_draws_whole_with_9_42pt_of_margin()
    {
        double inner6 = InnerCellWidth(5);            // the DRC-03 register's shipped shape
        Assert.Equal(66.71, inner6, 2);
        Assert.Equal(EmittedWidest, PdfWriter.FitToWidth(EmittedWidest, inner6, BodyFont));
        Assert.Equal(9.42, inner6 - PdfWriter.MeasureHelvetica(EmittedWidest, BodyFont), 2);
    }
}
