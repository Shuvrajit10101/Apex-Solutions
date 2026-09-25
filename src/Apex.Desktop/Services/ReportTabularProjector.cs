using System.Collections.Generic;
using System.Globalization;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects the on-screen report held by a <see cref="ReportsViewModel"/> into a framework-agnostic
/// <see cref="TabularExport"/> for the CSV / XLSX writers (RQ-14/15). It mirrors
/// <see cref="ReportPrintProjector"/> — same columns, same rows, same header/total flags — with one crucial
/// difference for a spreadsheet: money goes into <b>Number</b> cells carrying the <b>exact decimal</b> (parsed
/// back from the grid's Indian-grouped string) so the spreadsheet stores a real number and can sum it, rather
/// than the display string. Label cells stay Text. Unicode (₹, em-dash) survives natively — CSV/XLSX are
/// Unicode, so no ASCII folding is needed here (unlike the PDF path).
///
/// <para>The mapping is pure and Avalonia-free apart from reading the VM's already-built rows: it never
/// touches disk, dialogs, OS-print or the clock (ER-12). No brand text is ever introduced.</para>
///
/// <para>🔴 <b>RULING 18 — a BODY CELL is no longer de-branded by the writers.</b> Every text cell is book data
/// (a party, bank or item master name, a narration, a bill reference) and is exported VERBATIM, because the
/// scrub that used to run in the writers rewrote a counterparty's legal name inside the customer's own
/// spreadsheet. What the writers still guard is CHROME: the column captions, which are compile-time strings
/// this product authored. The TITLE is the one string that can be either, so it is decided by the producer's
/// <see cref="ReportsViewModel.TitleCarriesMasterName"/> flag, carried into
/// <see cref="TabularExport.TitleCarriesMasterName"/> here and resolved once in
/// <see cref="TabularExport.TitleText"/>.</para>
/// </summary>
public static class ReportTabularProjector
{
    /// <summary>Builds the tabular export model for the report currently shown by <paramref name="vm"/>.</summary>
    public static TabularExport Project(ReportsViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        // Phase 8 slice 8 payroll reports carry their data outside the generic Col1..Col8 rows: a wide tabular
        // payroll report projects its dynamic matrix (money columns typed Number so a spreadsheet sums them); the
        // Payslip projects its earning/deduction detail.
        if (vm.IsPayrollMatrix) return ProjectPayrollMatrix(vm);
        if (vm.IsPayslipReport) return ProjectPayslip(vm);

        // 🔴 THE DECLARED COLUMN BAND COMES FIRST. When the kind declares one (ReportColumnBands), it is the
        // single source both this projector and ReportPrintProjector caption from, and it names the CELL each
        // column reads — which is the only way to export a report that skips Col4 (CST forms) or keeps its
        // money column in Secondary (Batchwise / Batch Age / Price List) without mis-aligning the captions.
        var band = ReportColumnBands.For(vm);
        if (band.Count > 0) return ProjectBanded(vm, band);

        var columns = BuildColumns(vm);
        var rows = new List<TabularRow>(vm.Rows.Count);
        foreach (var r in vm.Rows)
            rows.Add(ProjectRow(vm, r, columns.Count));

        // 🔴 RULING 18: carry the producer's provenance flag into the export model, do not re-decide it here — the
        // view model built the heading and is the only party that knows whether a master name is inside it. One
        // flag then decides HTML, XML, JSON and XLSX identically (TabularExport.TitleText).
        return new TabularExport(vm.Title, columns, rows, vm.TitleCarriesMasterName);
    }

    /// <summary>
    /// Projects a report that declares a column band: one export column per band entry, captioned from the band
    /// and filled from the cell that entry names. A figure column stores a real spreadsheet Number when the cell
    /// parses as one, and falls back to Text when it does not — so "onwards" in Price List's To-quantity column
    /// and "Undeposited" in the TDS interest deposit-date column survive verbatim instead of being coerced.
    /// </summary>
    private static TabularExport ProjectBanded(ReportsViewModel vm, IReadOnlyList<ReportColumnBands.Spec> band)
    {
        var columns = new List<TabularColumn>(band.Count);
        foreach (var c in band)
            columns.Add(new TabularColumn(c.Caption, c.IsNumeric ? CellType.Number : CellType.Text));

        var rows = new List<TabularRow>(vm.Rows.Count);
        foreach (var r in vm.Rows)
        {
            var cells = new TabularCell[band.Count];
            for (int i = 0; i < band.Count; i++)
            {
                string text = band[i].Cell(r) ?? string.Empty;
                cells[i] = band[i].IsNumeric && TryParseAmount(text, out var v)
                    ? TabularCell.Number(v)
                    : TabularCell.Text(text);
            }
            rows.Add(new TabularRow(cells, isHeader: r.IsHeader, isTotal: r.IsTotal));
        }

        // 🔴 RULING 18: carry the producer's provenance flag, do not re-decide it here.
        return new TabularExport(vm.Title, columns, rows, vm.TitleCarriesMasterName);
    }

    /// <summary>Projects a wide tabular payroll report from its dynamic matrix: a Number column for each numeric
    /// (money/day) column so the spreadsheet stores real numbers; the label/text columns stay Text.</summary>
    private static TabularExport ProjectPayrollMatrix(ReportsViewModel vm)
    {
        var columns = new List<TabularColumn>(vm.PayrollColumns.Count);
        foreach (var c in vm.PayrollColumns)
            columns.Add(new TabularColumn(c.Header, c.IsNumeric ? CellType.Number : CellType.Text));

        var rows = new List<TabularRow>(vm.PayrollRows.Count);
        foreach (var r in vm.PayrollRows)
        {
            var cells = new TabularCell[r.Cells.Count];
            for (int i = 0; i < r.Cells.Count; i++)
            {
                var cell = r.Cells[i];
                cells[i] = cell.IsNumeric && TryParseAmount(cell.Text, out var v)
                    ? TabularCell.Number(v)
                    : TabularCell.Text(cell.Text);
            }
            rows.Add(new TabularRow(cells, isTotal: r.IsTotal));
        }

        // ---- The matrix's optional SECOND section (W7-D2): PF Form 6A page 2. One export sheet carries one column
        // band, so page 2 is appended as a captioned block (its title, its own headings, then its rows) rather than
        // being dropped. Its figures stay typed as numbers so the spreadsheet still sums the challan account heads —
        // which is the one arithmetic anybody actually does with page 2.
        if (vm.PayrollRows2.Count > 0)
        {
            rows.Add(TabularRow.Header(TabularCell.Text(vm.PayrollSection2Title)));
            var headings = new TabularCell[vm.PayrollColumns2.Count];
            for (int i = 0; i < vm.PayrollColumns2.Count; i++)
                headings[i] = TabularCell.Text(vm.PayrollColumns2[i].Header);
            rows.Add(TabularRow.Header(headings));
            foreach (var r in vm.PayrollRows2)
            {
                var cells = new TabularCell[r.Cells.Count];
                for (int i = 0; i < r.Cells.Count; i++)
                {
                    var cell = r.Cells[i];
                    cells[i] = cell.IsNumeric && TryParseAmount(cell.Text, out var v)
                        ? TabularCell.Number(v)
                        : TabularCell.Text(cell.Text);
                }
                rows.Add(new TabularRow(cells, isTotal: r.IsTotal));
            }
        }

        // ---- The statutory footnotes: the only thing that says a blank column is unmaintained rather than broken.
        foreach (var note in vm.PayrollFootnotes)
            rows.Add(TabularRow.Of(TabularCell.Text(note)));

        // 🔴 RULING 18: carry the producer's provenance flag into the export model, do not re-decide it here — the
        // view model built the heading and is the only party that knows whether a master name is inside it. One
        // flag then decides HTML, XML, JSON and XLSX identically (TabularExport.TitleText).
        return new TabularExport(vm.Title, columns, rows, vm.TitleCarriesMasterName);
    }

    /// <summary>Projects the Payslip as a two-column Particulars | Amount export (earnings, gross, deductions,
    /// net, employer contributions) with the amounts typed Number so the spreadsheet sums them.</summary>
    private static TabularExport ProjectPayslip(ReportsViewModel vm)
    {
        var columns = new[] { new TabularColumn("Particulars", CellType.Text), new TabularColumn("Amount", CellType.Number) };
        var rows = new List<TabularRow>();
        rows.Add(TabularRow.Header(TabularCell.Text(vm.PayslipEmployee), TabularCell.Empty));
        rows.Add(TabularRow.Header(TabularCell.Text("Earnings"), TabularCell.Empty));
        foreach (var e in vm.PayslipEarnings) rows.Add(TabularRow.Of(TabularCell.Text(e.Name), MoneyCell(e.Amount)));
        rows.Add(TabularRow.Total(TabularCell.Text("Gross Earnings"), MoneyCell(vm.PayslipGross)));
        rows.Add(TabularRow.Header(TabularCell.Text("Deductions"), TabularCell.Empty));
        foreach (var d in vm.PayslipDeductions) rows.Add(TabularRow.Of(TabularCell.Text(d.Name), MoneyCell(d.Amount)));
        rows.Add(TabularRow.Total(TabularCell.Text("Total Deductions"), MoneyCell(vm.PayslipTotalDeductions)));
        rows.Add(TabularRow.Total(TabularCell.Text("Net Pay"), MoneyCell(vm.PayslipNet)));
        if (vm.PayslipEmployerContributions.Count > 0)
        {
            rows.Add(TabularRow.Header(TabularCell.Text("Employer Contributions (not part of net pay)"), TabularCell.Empty));
            foreach (var c in vm.PayslipEmployerContributions) rows.Add(TabularRow.Of(TabularCell.Text(c.Name), MoneyCell(c.Amount)));
        }
        // 🔴 RULING 18: carry the producer's provenance flag into the export model, do not re-decide it here — the
        // view model built the heading and is the only party that knows whether a master name is inside it. One
        // flag then decides HTML, XML, JSON and XLSX identically (TabularExport.TitleText).
        return new TabularExport(vm.Title, columns, rows, vm.TitleCarriesMasterName);
    }

    /// <summary>
    /// The column set for the report kind, mirroring <see cref="ReportPrintProjector"/>. Accounting reports use
    /// Particulars(Text) + Debit/Credit(Number) for a two-column Trial Balance, else a single Amount(Number).
    /// The wider inventory/GST reports fall back to their generic populated cells: the first column is a Text
    /// label; trailing columns are typed Number when every populated body cell in that column parses as a
    /// number (quantities/rates/values/tax), else Text (so a "Party" or "Batch" column stays text).
    /// </summary>
    private static IReadOnlyList<TabularColumn> BuildColumns(ReportsViewModel vm)
    {
        if (vm.IsAccountingReport)
        {
            if (vm.IsTwoColumn)
                return new[]
                {
                    new TabularColumn("Particulars", CellType.Text),
                    new TabularColumn("Debit", CellType.Number),
                    new TabularColumn("Credit", CellType.Number),
                };
            return new[]
            {
                new TabularColumn("Particulars", CellType.Text),
                new TabularColumn("Amount", CellType.Number),
            };
        }

        int used = MaxUsedGenericCells(vm);
        if (used == 0)
            return new[] { new TabularColumn("Particulars", CellType.Text) };

        // 🔴 THE LAST-RESORT PATH FOR A KIND THAT DECLARED NO BAND, AND IT IS DELIBERATELY UGLY. Captions come
        // from ReportColumnBands now — the single source this projector and ReportPrintProjector share. A kind
        // that reaches here is one that was born without a band, which is precisely what
        // ReportColumnBandCoverageTests fails on; it exports blank headings rather than a "Col N" placeholder
        // until that test is satisfied. The old private HeadersFor switch that used to caption 16 kinds here was
        // DELETED with this change rather than left beside the band: two caption tables for one report is how
        // the printed and exported headings drifted apart in the first place.
        var cols = new List<TabularColumn>(used);
        for (int i = 0; i < used; i++)
        {
            CellType type = i == 0
                ? CellType.Text                                                    // the label column is always text
                : ColumnIsNumeric(vm, i) ? CellType.Number : CellType.Text;
            cols.Add(new TabularColumn(string.Empty, type));
        }
        return cols;
    }

    private static TabularRow ProjectRow(ReportsViewModel vm, ReportRow r, int colCount)
    {
        TabularCell[] cells;
        if (vm.IsAccountingReport)
        {
            // 🔴 Phase 10.11 S3 — the export twin of the same defect described in
            // `ReportPrintProjector.ProjectRow`: this projection is what CSV, XLSX and the emailed workbook are
            // built from, and it emitted a cancelled Day Book row with its full amount and no marker of any kind
            // (the "(Cancelled)" tag lives in `ReportRow.Secondary`, which has no cell here). The Amount cell stays
            // a real Number so the spreadsheet still sums the column exactly as the screen totals it; the fact
            // rides on the label, where a reader sees it.
            // 🔴 The label cell is composed by ReportColumnBands.AccountingLabel — the SAME composition the
            // print twin uses, and the same one the screen uses. The hand-rolled `IsCancelled ? … + "
            // (Cancelled)"` this replaces is subsumed by it (the builder writes "(Cancelled) " into Secondary),
            // and everything ELSE the accounting reports put in Secondary — the Cheque Register's reconciled
            // counts, the Deposit Slip's and e-Payments' voucher numbers, the Ledger Monthly Summary's opening
            // and closing money — travels now instead of being dropped on the way out of the building.
            var particulars = TabularCell.Text(ReportColumnBands.AccountingLabel(r));
            cells = vm.IsTwoColumn
                ? new[] { particulars, MoneyCell(r.Debit), MoneyCell(r.Credit) }
                : new[] { particulars, MoneyCell(r.Amount) };
        }
        else
        {
            string[] gen = { r.Col1, r.Col2, r.Col3, r.Col4, r.Col5, r.Col6, r.Col7, r.Col8 };
            cells = new TabularCell[colCount];
            for (int i = 0; i < colCount; i++)
            {
                string text = i < gen.Length ? gen[i] : string.Empty;
                if (i == 0)
                    cells[i] = TabularCell.Text(text);                 // the label column is always text
                else if (vm.Rows.Count > 0 && ColumnIsNumeric(vm, i) && TryParseAmount(text, out var value))
                    cells[i] = TabularCell.Number(value);              // a numeric column with a parseable figure
                else
                    cells[i] = TabularCell.Text(text);                 // non-numeric column, or a blank/label cell
            }
        }

        return new TabularRow(cells, isHeader: r.IsHeader, isTotal: r.IsTotal);
    }

    /// <summary>An accounting money cell: blank grid string ⇒ empty cell; else the exact parsed decimal as a
    /// Number so the spreadsheet sums it (never the formatted display string).</summary>
    private static TabularCell MoneyCell(string formatted)
        => TryParseAmount(formatted, out var value) ? TabularCell.Number(value) : TabularCell.Empty;

    private static int MaxUsedGenericCells(ReportsViewModel vm)
    {
        int max = 0;
        foreach (var r in vm.Rows)
        {
            string[] c = { r.Col1, r.Col2, r.Col3, r.Col4, r.Col5, r.Col6, r.Col7, r.Col8 };
            for (int i = c.Length - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(c[i])) { if (i + 1 > max) max = i + 1; break; }
        }
        return max;
    }

    /// <summary>
    /// True when generic column <paramref name="index"/> (0-based over Col1..Col8) is a numeric column — i.e.
    /// at least one body row populates it and <b>every</b> populated cell in it parses as a number. A column
    /// with any non-numeric populated cell (Party / Godown / Batch text) stays Text so labels are not coerced.
    /// </summary>
    private static bool ColumnIsNumeric(ReportsViewModel vm, int index)
    {
        bool anyPopulated = false;
        foreach (var r in vm.Rows)
        {
            string cell = index switch
            {
                1 => r.Col2, 2 => r.Col3, 3 => r.Col4, 4 => r.Col5,
                5 => r.Col6, 6 => r.Col7, 7 => r.Col8, _ => string.Empty,
            };
            if (string.IsNullOrWhiteSpace(cell)) continue;
            anyPopulated = true;
            if (!TryParseAmount(cell, out _)) return false;
        }
        return anyPopulated;
    }

    /// <summary>
    /// Parses a grid-formatted figure (Indian grouping, e.g. "1,05,000.00", "12.5", "(2,000.00)") back to its
    /// exact decimal. Handles the comma group separator, a leading currency glyph, a trailing/leading minus and
    /// accounting parentheses for negatives. A blank cell yields false (⇒ an empty cell, not a zero).
    /// </summary>
    internal static bool TryParseAmount(string? formatted, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(formatted)) return false;

        string s = formatted.Trim();
        bool negative = false;

        // Accounting parentheses ⇒ negative.
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
        {
            negative = true;
            s = s.Substring(1, s.Length - 2).Trim();
        }

        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == ',' || c == '₹' || c == ' ') continue; // drop group separators, ₹, spaces
            if (c == '-') { negative = true; continue; }
            if (c == '+') continue;
            if (char.IsDigit(c) || c == '.') { sb.Append(c); continue; }
            return false; // any other glyph (a letter, a "%", "Dr"/"Cr") ⇒ not a pure number cell
        }

        if (sb.Length == 0) return false;
        if (!decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return false;
        if (negative) value = -value;
        return true;
    }
}
