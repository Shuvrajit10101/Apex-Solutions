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
/// touches disk, dialogs, OS-print or the clock (ER-12). No brand text is ever introduced; any user-supplied
/// label is de-branded inside the writers.</para>
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

        var columns = BuildColumns(vm);
        var rows = new List<TabularRow>(vm.Rows.Count);
        foreach (var r in vm.Rows)
            rows.Add(ProjectRow(vm, r, columns.Count));

        return new TabularExport(vm.Title, columns, rows);
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

        return new TabularExport(vm.Title, columns, rows);
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
        return new TabularExport(vm.Title, columns, rows);
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

        // The wide inventory/GST reports keep their real captions in per-report XAML DataTemplates the projector
        // cannot see, so it carries the SAME on-screen captions here (RQ-18 header row; RQ-15 match-screen). The
        // caption for the first column replaces the generic "Particulars"; a caption missing for a column falls
        // back to blank (never a "Col N" placeholder). Number vs Text is still inferred from the body cells.
        string[] captions = HeadersFor(vm.Kind);
        var cols = new List<TabularColumn>(used);
        for (int i = 0; i < used; i++)
        {
            string header = i < captions.Length ? captions[i] : string.Empty;
            CellType type = i == 0
                ? CellType.Text                                                    // the label column is always text
                : ColumnIsNumeric(vm, i) ? CellType.Number : CellType.Text;
            cols.Add(new TabularColumn(header, type));
        }
        return cols;
    }

    /// <summary>
    /// The on-screen column captions for a wide inventory/GST <paramref name="kind"/>, matching the report's
    /// per-ReportKind DataTemplate headers in the shell exactly (so an exported header row reads like the screen;
    /// RQ-15/18). An empty array (an unmapped kind) yields blank headers, never a placeholder. Kept here beside
    /// the projection because these captions are a property of the export, not of the accounting VM.
    /// </summary>
    private static string[] HeadersFor(ReportKind kind) => kind switch
    {
        ReportKind.StockSummary        => new[] { "Stock Item", "Inward", "Outward", "Closing Qty", "Rate", "Value" },
        ReportKind.GodownSummary       => new[] { "Godown", "Stock Item", "Quantity", "Value" },
        ReportKind.StockItemMovement   => new[] { "Date", "Voucher Type", "Inward", "Outward", "Balance", "Value" },
        ReportKind.ReorderStatus       => new[] { "Stock Item", "Closing", "Reorder Level", "Pending POs", "SOs Due", "Shortfall", "Order to be Placed" },
        ReportKind.PhysicalStockRegister => new[] { "Date", "Stock Item", "Godown", "Book", "Counted", "Variance" },
        ReportKind.OrderRegister       => new[] { "Date", "Voucher", "Party", "Stock Item", "Godown", "Ordered", "Pending", "Rate" },
        ReportKind.ReceiptNoteRegister or ReportKind.DeliveryNoteRegister or ReportKind.RejectionRegister
        or ReportKind.MaterialInRegister or ReportKind.MaterialOutRegister
                                       => new[] { "Date", "No.", "Party", "Stock Item", "Godown", "Qty", "Rate", "Value" },
        ReportKind.JobWorkInOrderBook or ReportKind.JobWorkOutOrderBook
                                       => new[] { "Date", "Order No.", "Party", "Item", "Track", "Ordered", "Fulfilled", "Pending" },
        ReportKind.TaxAnalysis         => new[] { "Rate / Head", "CGST", "SGST", "IGST", "Taxable", "Tax" },
        ReportKind.Gstr1               => new[] { "Party / HSN", "GSTIN / Description", "Invoice / UQC", "POS / Qty", "Taxable", "CGST", "SGST", "IGST" },
        ReportKind.Gstr3b              => new[] { "Particulars", "Taxable Value", "CGST", "SGST", "IGST" },
        _                              => System.Array.Empty<string>(),
    };

    private static TabularRow ProjectRow(ReportsViewModel vm, ReportRow r, int colCount)
    {
        TabularCell[] cells;
        if (vm.IsAccountingReport)
        {
            var particulars = TabularCell.Text(r.Particulars);
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
