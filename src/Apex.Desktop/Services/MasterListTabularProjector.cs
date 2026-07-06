using System.Collections.Generic;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects an on-screen MASTER LIST (Chart of Accounts, the existing-ledgers list, the stock-items list, …)
/// into a framework-agnostic <see cref="TabularExport"/> for the CSV / XLSX / PDF writers (RQ-14/16, slice 13).
/// It is the master-list sibling of <see cref="ReportTabularProjector"/>: same discipline — real column
/// captions, <b>Number</b> cells for the amount columns (an opening balance / opening value) so a spreadsheet
/// stores a real number and can sum it, Text cells for names / groups / natures. Unicode (₹, em-dash) survives
/// natively — CSV/XLSX are Unicode — and any brand text is de-branded inside the writers.
///
/// <para>The mapping is pure and reads only the VM's already-built display rows (it mirrors exactly what the
/// grid shows; RQ-15). Amounts are recovered from the grid's Indian-grouped strings via the shared
/// <see cref="ReportTabularProjector.TryParseAmount"/> parser (so "1,05,000.00 Dr" ⇒ the exact decimal
/// <c>105000.00</c> in a Number cell, and the Dr/Cr side becomes its own text column); a placeholder cell
/// ("—" / blank) exports as an empty cell, never a spurious zero. No Avalonia, no clock, no engine
/// re-computation (ER-8/ER-12).</para>
/// </summary>
public static class MasterListTabularProjector
{
    /// <summary>
    /// GENERIC master-list projection (slice-13 audit): turns any <see cref="IMasterListExportSource"/>'s
    /// <see cref="MasterListSnapshot"/> — its captions, per-column numeric flag and already-displayed string
    /// rows — into a <see cref="TabularExport"/>. A numeric column emits a <b>Number</b> cell parsed from the
    /// grid's Indian-grouped string (so "1,05,000.00" ⇒ the exact decimal <c>105000.00</c> a spreadsheet can
    /// sum), falling back to an empty cell for a blank / placeholder ("—") value; a text column emits a Text
    /// cell verbatim. Any Dr/Cr suffix on a numeric opening is split off into the amount (the side, when the
    /// master keeps it, is its OWN text column in the snapshot). This is the uniform path EVERY master-list
    /// screen exports through — groups, cost centres/categories, godowns, units, currencies, scenarios,
    /// budgets, stock groups/categories, parties, … — no per-screen code beyond the tiny snapshot each VM builds.
    /// </summary>
    public static TabularExport ProjectSource(IMasterListExportSource source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var snapshot = source.ToMasterListSnapshot();

        var columns = new TabularColumn[snapshot.Columns.Count];
        for (int i = 0; i < snapshot.Columns.Count; i++)
        {
            var c = snapshot.Columns[i];
            columns[i] = new TabularColumn(c.Caption, c.IsNumeric ? CellType.Number : CellType.Text);
        }

        var rows = new List<TabularRow>(snapshot.Rows.Count);
        foreach (var row in snapshot.Rows)
        {
            var cells = new TabularCell[snapshot.Columns.Count];
            for (int i = 0; i < snapshot.Columns.Count; i++)
            {
                string? value = i < row.Count ? row[i] : null;
                cells[i] = snapshot.Columns[i].IsNumeric ? NumericCell(value) : TabularCell.Text(value);
            }
            rows.Add(new TabularRow(cells));
        }

        return new TabularExport(snapshot.Title, columns, rows);
    }

    /// <summary>A numeric grid string ("1,05,000.00 Dr", "2,550.00", "—", "") → the exact amount as a Number
    /// cell (any Dr/Cr side stripped), else an empty cell (never a spurious zero).</summary>
    private static TabularCell NumericCell(string? formatted)
        => SplitAmountAndSide(formatted).Amount;

    /// <summary>
    /// Projects the Chart-of-Accounts tree. Columns: <b>Name</b>, <b>Type</b> (Primary / Sub-Group / Ledger),
    /// <b>Nature</b> (a group's nature, blank for a ledger), <b>Opening</b> (a ledger's opening amount as a
    /// Number cell) and <b>Dr/Cr</b> (its side). Group rows carry the nature and no opening; ledger rows carry
    /// the opening + side. The tree order (and its indentation, prefixed onto the name) matches the screen.
    /// </summary>
    public static TabularExport ProjectChartOfAccounts(ChartOfAccountsViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        var columns = new[]
        {
            new TabularColumn("Name", CellType.Text),
            new TabularColumn("Type", CellType.Text),
            new TabularColumn("Nature", CellType.Text),
            new TabularColumn("Opening", CellType.Number),
            new TabularColumn("Dr/Cr", CellType.Text),
        };

        var rows = new List<TabularRow>(vm.Rows.Count);
        foreach (var r in vm.Rows)
        {
            string name = Indent(r.Depth) + r.Name;
            string type = r.Kind switch
            {
                ChartNodeKind.Primary => "Primary",
                ChartNodeKind.SubGroup => "Sub-Group",
                _ => "Ledger",
            };

            if (r.IsGroup)
            {
                // A group's Detail is its nature; it has no opening amount.
                rows.Add(new TabularRow(new[]
                {
                    TabularCell.Text(name),
                    TabularCell.Text(type),
                    TabularCell.Text(r.Detail),
                    TabularCell.Empty,
                    TabularCell.Empty,
                }, isHeader: r.IsPrimary));
            }
            else
            {
                // A ledger's Detail is "<amount> Dr" / "<amount> Cr" (blank when zero).
                var (amount, side) = SplitAmountAndSide(r.Detail);
                rows.Add(new TabularRow(new[]
                {
                    TabularCell.Text(name),
                    TabularCell.Text(type),
                    TabularCell.Empty,
                    amount,
                    TabularCell.Text(side),
                }));
            }
        }

        return new TabularExport(vm.Title, columns, rows);
    }

    /// <summary>
    /// Projects the existing-ledgers list (ledger-creation master). Columns: <b>Name</b>, <b>Under</b> (group),
    /// <b>Opening</b> (Number) + <b>Dr/Cr</b>, <b>Currency</b> and <b>Interest</b>. The row order matches the
    /// name-sorted grid.
    /// </summary>
    public static TabularExport ProjectLedgers(LedgerMasterViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        var columns = new[]
        {
            new TabularColumn("Name", CellType.Text),
            new TabularColumn("Under", CellType.Text),
            new TabularColumn("Opening", CellType.Number),
            new TabularColumn("Dr/Cr", CellType.Text),
            new TabularColumn("Currency", CellType.Text),
            new TabularColumn("Interest", CellType.Text),
        };

        var rows = new List<TabularRow>(vm.Existing.Count);
        foreach (var r in vm.Existing)
        {
            var (amount, side) = SplitAmountAndSide(r.Opening);
            rows.Add(new TabularRow(new[]
            {
                TabularCell.Text(r.Name),
                TabularCell.Text(r.Under),
                amount,
                TabularCell.Text(side),
                TabularCell.Text(r.Currency),
                TabularCell.Text(r.Interest),
            }));
        }

        return new TabularExport("Ledgers", columns, rows);
    }

    /// <summary>
    /// Projects the existing stock-items list (stock-item-creation master). Columns: <b>Name</b>, <b>Under</b>
    /// (stock group), <b>Unit</b>, <b>Valuation</b> and <b>Opening Value</b> (a Number cell). The row order
    /// matches the grid.
    /// </summary>
    public static TabularExport ProjectStockItems(StockItemMasterViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        var columns = new[]
        {
            new TabularColumn("Name", CellType.Text),
            new TabularColumn("Under", CellType.Text),
            new TabularColumn("Unit", CellType.Text),
            new TabularColumn("Valuation", CellType.Text),
            new TabularColumn("Opening Value", CellType.Number),
        };

        var rows = new List<TabularRow>(vm.Existing.Count);
        foreach (var r in vm.Existing)
        {
            rows.Add(new TabularRow(new[]
            {
                TabularCell.Text(r.Name),
                TabularCell.Text(r.Under),
                TabularCell.Text(r.Unit),
                TabularCell.Text(r.Valuation),
                AmountCell(r.OpeningValue),
            }));
        }

        return new TabularExport("Stock Items", columns, rows);
    }

    /// <summary>
    /// Projects the <b>Parties</b> master-list view (RQ; slice-13 audit LOW): the ledger-creation list filtered
    /// to party ledgers — those under <b>Sundry Debtors</b> or <b>Sundry Creditors</b> (a receivable / payable
    /// party; catalog §5). Same columns as the Ledgers export (Name / Under / Opening [Number] / Dr/Cr /
    /// Currency / Interest) so a party opening balance stays a summable number. Reads only the ledger master's
    /// already-displayed rows — no engine re-computation.
    /// </summary>
    public static TabularExport ProjectParties(LedgerMasterViewModel vm)
    {
        System.ArgumentNullException.ThrowIfNull(vm);

        var columns = new[]
        {
            new TabularColumn("Name", CellType.Text),
            new TabularColumn("Under", CellType.Text),
            new TabularColumn("Opening", CellType.Number),
            new TabularColumn("Dr/Cr", CellType.Text),
            new TabularColumn("Currency", CellType.Text),
            new TabularColumn("Interest", CellType.Text),
        };

        var rows = new List<TabularRow>();
        foreach (var r in vm.Existing)
        {
            if (!IsPartyUnder(r.Under)) continue;
            var (amount, side) = SplitAmountAndSide(r.Opening);
            rows.Add(new TabularRow(new[]
            {
                TabularCell.Text(r.Name),
                TabularCell.Text(r.Under),
                amount,
                TabularCell.Text(side),
                TabularCell.Text(r.Currency),
                TabularCell.Text(r.Interest),
            }));
        }

        return new TabularExport("Parties", columns, rows);
    }

    /// <summary>True iff the ledger's under-group name is (or nests under) a party group — Sundry Debtors /
    /// Creditors. The grid's "Under" label carries the immediate parent group's name.</summary>
    private static bool IsPartyUnder(string? under)
        => !string.IsNullOrWhiteSpace(under)
           && (under.Contains("Sundry Debtor", System.StringComparison.OrdinalIgnoreCase)
               || under.Contains("Sundry Creditor", System.StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- helpers

    /// <summary>Two leading spaces per indent level so the exported name keeps the tree's nesting readable.</summary>
    private static string Indent(int depth) => depth <= 0 ? string.Empty : new string(' ', depth * 2);

    /// <summary>An amount-only cell from a grid string ("₹1,05,000.00", "—", ""): a Number cell for a parseable
    /// figure, else an empty cell (never a spurious zero).</summary>
    private static TabularCell AmountCell(string? formatted)
        => ReportTabularProjector.TryParseAmount(formatted, out var value)
            ? TabularCell.Number(value)
            : TabularCell.Empty;

    /// <summary>
    /// Splits an opening-balance grid string ("1,05,000.00 Dr" / "2,000.00 Cr" / blank) into its exact amount
    /// (a Number cell) and its Dr/Cr side (a text label). A blank / non-amount string yields an empty amount
    /// cell and a blank side.
    /// </summary>
    private static (TabularCell Amount, string Side) SplitAmountAndSide(string? opening)
    {
        if (string.IsNullOrWhiteSpace(opening))
            return (TabularCell.Empty, string.Empty);

        string side = string.Empty;
        string amountText = opening.Trim();
        if (amountText.EndsWith(" Dr", System.StringComparison.OrdinalIgnoreCase))
        {
            side = "Dr";
            amountText = amountText[..^3];
        }
        else if (amountText.EndsWith(" Cr", System.StringComparison.OrdinalIgnoreCase))
        {
            side = "Cr";
            amountText = amountText[..^3];
        }

        return (AmountCell(amountText), side);
    }
}
