using System.Collections.Generic;
using System.Text;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects the on-screen report held by a <see cref="ReportsViewModel"/> into a framework-agnostic
/// <see cref="PrintReport"/> for the <see cref="ReportPdf"/> renderer (RQ-9). The mapping is pure and
/// Avalonia-free apart from reading the VM's already-built rows, so the whole IO path stays in
/// <c>Apex.Ledger.Io</c> (ER-12): this class only shapes the columns/rows, it never touches disk,
/// dialogs, OS-print or the clock.
///
/// <para>The report's amounts are already <c>IndianFormat</c>-formatted on the <see cref="ReportRow"/>,
/// so they pass through verbatim and the PDF shows exactly the grid figures. Non-ASCII glyphs the PDF
/// writer would render as '?' (the em-dash in a subtitle, the ₹ sign) are folded to ASCII here so the
/// output is clean and de-branded — never a stray '?'. No brand text is ever introduced.</para>
/// </summary>
public static class ReportPrintProjector
{
    /// <summary>Builds the print model for the report currently shown by <paramref name="vm"/>.</summary>
    public static PrintReport Project(ReportsViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        // Phase 8 slice 8 payroll reports carry their data in dedicated collections, not the generic Col1..Col8
        // rows: a wide tabular payroll report (Pay Sheet / Payroll Register / Attendance / Payment Advice) projects
        // its dynamic matrix; the Payslip projects its earning/deduction detail (its Print path also renders the
        // dedicated PayslipPdf, but a plain report projection keeps Export working).
        if (vm.IsPayrollMatrix) return ProjectPayrollMatrix(vm);
        if (vm.IsPayslipReport) return ProjectPayslip(vm);

        var columns = BuildColumns(vm);
        var rows = new List<PrintRow>(vm.Rows.Count);
        foreach (var r in vm.Rows)
            rows.Add(ProjectRow(vm, r));

        return new PrintReport
        {
            Title = Ascii(vm.Title),
            Subtitle = Ascii(vm.Subtitle),
            Columns = columns,
            Rows = rows,
        };
    }

    /// <summary>Projects a wide tabular payroll report from its dynamic matrix: one column per
    /// <see cref="ReportsViewModel.PayrollColumns"/> entry (numeric columns right-aligned, the label column left),
    /// one row per <see cref="ReportsViewModel.PayrollRows"/> entry. Figures are already formatted on the cells.</summary>
    private static PrintReport ProjectPayrollMatrix(ReportsViewModel vm)
    {
        var columns = new List<PrintColumn>(vm.PayrollColumns.Count);
        foreach (var c in vm.PayrollColumns)
            columns.Add(new PrintColumn(Ascii(c.Header), c.IsNumeric ? 1.3 : 2.4,
                c.IsNumeric ? CellAlign.Right : CellAlign.Left));

        var rows = new List<PrintRow>(vm.PayrollRows.Count);
        foreach (var r in vm.PayrollRows)
        {
            var cells = new string[r.Cells.Count];
            for (int i = 0; i < r.Cells.Count; i++) cells[i] = Ascii(r.Cells[i].Text);
            rows.Add(new PrintRow { Cells = cells, IsTotal = r.IsTotal });
        }

        return new PrintReport { Title = Ascii(vm.Title), Subtitle = Ascii(vm.Subtitle), Columns = columns, Rows = rows };
    }

    /// <summary>Projects the Payslip as a two-column Particulars | Amount report (earnings, gross, deductions, net,
    /// employer contributions) — used for the Export path; Print renders the dedicated PayslipPdf.</summary>
    private static PrintReport ProjectPayslip(ReportsViewModel vm)
    {
        var rows = new List<PrintRow>();
        rows.Add(PrintRow.Header(Ascii(vm.PayslipEmployee), string.Empty));
        rows.Add(PrintRow.Header("Earnings", string.Empty));
        foreach (var e in vm.PayslipEarnings) rows.Add(new PrintRow(Ascii(e.Name), Ascii(e.Amount)));
        rows.Add(PrintRow.Total("Gross Earnings", Ascii(vm.PayslipGross)));
        rows.Add(PrintRow.Header("Deductions", string.Empty));
        foreach (var d in vm.PayslipDeductions) rows.Add(new PrintRow(Ascii(d.Name), Ascii(d.Amount)));
        rows.Add(PrintRow.Total("Total Deductions", Ascii(vm.PayslipTotalDeductions)));
        rows.Add(PrintRow.Total("Net Pay", Ascii(vm.PayslipNet)));
        if (vm.PayslipEmployerContributions.Count > 0)
        {
            rows.Add(PrintRow.Header("Employer Contributions (not part of net pay)", string.Empty));
            foreach (var c in vm.PayslipEmployerContributions) rows.Add(new PrintRow(Ascii(c.Name), Ascii(c.Amount)));
        }

        return new PrintReport
        {
            Title = Ascii(vm.Title),
            Subtitle = Ascii(vm.Subtitle),
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    /// <summary>
    /// The column set for the report kind. The four accounting reports use Particulars + (Debit/Credit
    /// for a two-column Trial Balance, else a single Amount). The wider inventory/GST reports fall back to
    /// their generic Col1..Col8 cells (amount-looking columns right-aligned). Kept deliberately simple:
    /// the renderer only needs headers + alignment; the figures are already formatted on the rows.
    /// </summary>
    private static IReadOnlyList<PrintColumn> BuildColumns(ReportsViewModel vm)
    {
        if (vm.IsAccountingReport)
        {
            if (vm.IsTwoColumn)
                return new[]
                {
                    new PrintColumn("Particulars", 3, CellAlign.Left),
                    new PrintColumn("Debit", 1.5, CellAlign.Right),
                    new PrintColumn("Credit", 1.5, CellAlign.Right),
                };
            return new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            };
        }

        // Inventory / GST reports: project the populated generic cells. The first cell is a left label; any
        // trailing cells (quantities, rates, values, tax) are right-aligned so figures line up.
        int used = MaxUsedGenericCells(vm);
        if (used == 0)
            return new[] { new PrintColumn("Particulars", 3, CellAlign.Left) };

        var cols = new List<PrintColumn>(used);
        cols.Add(new PrintColumn("Particulars", 3, CellAlign.Left));
        // The wide inventory/GST reports keep their real column captions in per-report XAML templates, which
        // are not exposed to this projector. Emit blank right-aligned headers rather than a meaningless
        // hardcoded "Col 2"/"Col 3" placeholder — a stray "Col N" word in the printed header band reads as a
        // bug, whereas an empty caption keeps the figures lined up without inventing text.
        for (int i = 1; i < used; i++)
            cols.Add(new PrintColumn(string.Empty, 1.5, CellAlign.Right));
        return cols;
    }

    private static PrintRow ProjectRow(ReportsViewModel vm, ReportRow r)
    {
        string[] cells;
        if (vm.IsAccountingReport)
        {
            string particulars = Ascii(r.Particulars);
            cells = vm.IsTwoColumn
                ? new[] { particulars, Ascii(r.Debit), Ascii(r.Credit) }
                : new[] { particulars, Ascii(r.Amount) };
        }
        else
        {
            cells = new[]
            {
                Ascii(r.Col1), Ascii(r.Col2), Ascii(r.Col3), Ascii(r.Col4),
                Ascii(r.Col5), Ascii(r.Col6), Ascii(r.Col7), Ascii(r.Col8),
            };
        }

        return new PrintRow
        {
            Cells = cells,
            IsHeader = r.IsHeader,
            IsTotal = r.IsTotal,
            // ReportRow.Indent is in pixels (~8 px per nesting level); convert to a small space count.
            Indent = (int)(r.Indent / 8),
        };
    }

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
    /// Folds the common non-ASCII glyphs the reports use (em/en dash, the rupee sign, curly quotes, the
    /// bullet) to ASCII so the ASCII-only PDF writer never emits a '?'. Any other non-ASCII char is dropped
    /// to '-' as a last resort. This keeps the printed output clean and de-branded.
    /// </summary>
    internal static string Ascii(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '—': // em dash
                case '–': // en dash
                    sb.Append('-'); break;
                case '₹': // ₹ rupee sign
                    sb.Append("Rs."); break;
                case '‘': // ' left single quote
                case '’': // ' right single quote
                    sb.Append('\''); break;
                case '“': // " left double quote
                case '”': // " right double quote
                    sb.Append('"'); break;
                case '•': // • bullet
                    sb.Append('*'); break;
                default:
                    if (c <= 126) sb.Append(c);
                    else sb.Append('-');
                    break;
            }
        }
        return sb.ToString();
    }
}
