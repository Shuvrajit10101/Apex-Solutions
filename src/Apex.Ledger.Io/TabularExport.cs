using System.Globalization;

namespace Apex.Ledger.Io;

/// <summary>
/// The cell kind for a tabular export. <see cref="Text"/> cells carry a string; <see cref="Number"/> cells
/// carry an exact decimal so a spreadsheet stores a real number and can sum/aggregate it (RQ-14/17). Section
/// headers and totals are just rows whose cells happen to be text/number — a total row is still data.
/// </summary>
public enum CellType
{
    Text,
    Number,
}

/// <summary>
/// A column definition for a tabular export: a header caption and the cell kind of the column
/// (<see cref="CellType.Text"/> or <see cref="CellType.Number"/>). The kind drives how a writer formats the
/// column's cells (a Number column right-aligns / stores real numbers), but each cell also carries its own
/// kind so an occasional text-in-a-number-column (a blank, a label) still exports cleanly.
/// </summary>
public sealed class TabularColumn
{
    public string Header { get; }
    public CellType Type { get; }

    public TabularColumn(string header, CellType type = CellType.Text)
    {
        Header = header ?? string.Empty;
        Type = type;
    }
}

/// <summary>
/// One typed cell. A <see cref="CellType.Text"/> cell carries <see cref="TextValue"/>; a
/// <see cref="CellType.Number"/> cell carries an exact <see cref="NumberValue"/> (money is a decimal so the
/// spreadsheet can sum it). An empty cell (<see cref="TabularCell.Empty"/>) is a Text cell with no text —
/// it exports as a blank field / blank spreadsheet cell.
/// </summary>
public readonly struct TabularCell
{
    public CellType Type { get; }
    public string TextValue { get; }
    public decimal NumberValue { get; }
    public bool HasNumber { get; }

    private TabularCell(CellType type, string textValue, decimal numberValue, bool hasNumber)
    {
        Type = type;
        TextValue = textValue;
        NumberValue = numberValue;
        HasNumber = hasNumber;
    }

    /// <summary>An empty cell (blank field / blank spreadsheet cell).</summary>
    public static readonly TabularCell Empty = new(CellType.Text, string.Empty, 0m, false);

    /// <summary>A text cell carrying <paramref name="value"/> (null ⇒ empty).</summary>
    public static TabularCell Text(string? value)
        => string.IsNullOrEmpty(value) ? Empty : new(CellType.Text, value, 0m, false);

    /// <summary>A number cell carrying the exact decimal <paramref name="value"/>.</summary>
    public static TabularCell Number(decimal value) => new(CellType.Number, string.Empty, value, true);

    /// <summary>A number cell carrying the exact rupee amount of <paramref name="money"/>.</summary>
    public static TabularCell Money(Money money) => Number(money.Amount);

    /// <summary>The invariant string form used in CSV and as the XLSX <c>&lt;v&gt;</c> for a number cell.
    /// It preserves the decimal's OWN natural scale (RQ-15 on-screen fidelity): a money value carries scale 2
    /// (e.g. <c>355000.50</c>), a stock quantity or unit rate carries its real precision (e.g. <c>10.125</c>,
    /// <c>3.3333</c>), and a whole quantity stays whole (<c>5</c>, no invented <c>.00</c>). Plain
    /// <see cref="decimal.ToString(IFormatProvider)"/> round-trips the exact value at its stored scale under the
    /// invariant culture (a dot separator, no grouping). An empty/non-number cell yields an empty string.</summary>
    public string NumberText => HasNumber
        ? NumberValue.ToString(CultureInfo.InvariantCulture)
        : string.Empty;
}

/// <summary>
/// One export row: positional typed cells that align to the export's columns (missing cells export blank).
/// <see cref="IsTotal"/> / <see cref="IsHeader"/> are informational (a total/section-header row is still data
/// that exports verbatim); they let a writer bold a row later without changing the data.
/// </summary>
public sealed class TabularRow
{
    public IReadOnlyList<TabularCell> Cells { get; }
    public bool IsHeader { get; }
    public bool IsTotal { get; }

    public TabularRow(IReadOnlyList<TabularCell> cells, bool isHeader = false, bool isTotal = false)
    {
        Cells = cells ?? System.Array.Empty<TabularCell>();
        IsHeader = isHeader;
        IsTotal = isTotal;
    }

    public static TabularRow Of(params TabularCell[] cells) => new(cells);
    public static TabularRow Header(params TabularCell[] cells) => new(cells, isHeader: true);
    public static TabularRow Total(params TabularCell[] cells) => new(cells, isTotal: true);
}

/// <summary>
/// A framework-agnostic tabular model for a report or master list, ready to serialize to CSV or XLSX
/// (RQ-14). The UI projects its on-screen report/master-list — with money as exact decimals — into this
/// model, so the exported file carries the same figures the grid shows (RQ-15 fidelity). No Avalonia, no
/// clock, no engine re-computation: just a title (the worksheet name), columns and typed rows.
/// </summary>
public sealed class TabularExport
{
    public string Title { get; }
    public IReadOnlyList<TabularColumn> Columns { get; }
    public IReadOnlyList<TabularRow> Rows { get; }

    /// <summary>
    /// 🔴 <b>RULING 18 provenance seam — the tabular twin of <see cref="PrintReport.TitleCarriesMasterName"/>.</b>
    /// A <see cref="string"/> carries no record of who wrote it, so a writer handed <see cref="Title"/> cannot tell
    /// this product's own heading ("Trial Balance") from a heading the producer built by concatenating a label with
    /// a COUNTERPARTY'S legal name ("Ledger Monthly Summary — Tally Traders Pvt Ltd"). Only the producer knows,
    /// so the producer states it here and the writers read it — see <see cref="TitleText"/>.
    ///
    /// <para>Defaults to <see langword="false"/>, so every existing producer keeps the ER-11 guard on its title and
    /// is byte-identical.</para>
    /// </summary>
    public bool TitleCarriesMasterName { get; }

    /// <summary>
    /// 🔴 The ONE place the four title-writing formats (<see cref="HtmlReportWriter"/>, <see cref="XmlReportWriter"/>,
    /// <see cref="JsonReportWriter"/>, <see cref="XlsxWriter"/>) decide what a title says, so they cannot drift from
    /// each other or from the PDF. A product-authored title keeps the ER-11 scrub; a title the producer flagged as
    /// carrying a master name is emitted VERBATIM, because rewriting a real customer's, supplier's or bank's legal
    /// name inside the customer's own spreadsheet is exactly what ruling 18 forbids.
    /// </summary>
    public static string TitleText(TabularExport export)
    {
        System.ArgumentNullException.ThrowIfNull(export);
        return export.TitleCarriesMasterName ? export.Title : Debrand.Text(export.Title);
    }

    public TabularExport(
        string title,
        IReadOnlyList<TabularColumn> columns,
        IReadOnlyList<TabularRow> rows,
        bool titleCarriesMasterName = false)
    {
        Title = title ?? string.Empty;
        Columns = columns ?? System.Array.Empty<TabularColumn>();
        Rows = rows ?? System.Array.Empty<TabularRow>();
        TitleCarriesMasterName = titleCarriesMasterName;
    }
}
