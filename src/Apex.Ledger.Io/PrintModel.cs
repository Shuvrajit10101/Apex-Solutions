namespace Apex.Ledger.Io;

/// <summary>
/// Horizontal alignment for a report cell / column.
/// </summary>
public enum CellAlign
{
    Left,
    Right,
    Center,
}

/// <summary>
/// A column definition for a printable report: a header caption, a relative width weight (the
/// available content width is split proportionally across columns), and an alignment. Amount columns
/// are typically <see cref="CellAlign.Right"/>.
/// </summary>
public sealed class PrintColumn
{
    public string Header { get; init; } = string.Empty;

    /// <summary>Relative width weight; columns share the content width in proportion to their weights.</summary>
    public double Weight { get; init; } = 1.0;

    public CellAlign Align { get; init; } = CellAlign.Left;

    public PrintColumn() { }

    public PrintColumn(string header, double weight = 1.0, CellAlign align = CellAlign.Left)
    {
        Header = header;
        Weight = weight;
        Align = align;
    }
}

/// <summary>
/// One printable row. Cells align positionally to the report's columns (extra cells are ignored, missing
/// cells render blank). <see cref="IsHeader"/> renders a section-heading row (bold, no rule);
/// <see cref="IsTotal"/> renders a total row (bold, top rule). <see cref="Indent"/> is a number of
/// leading spaces applied to the first cell so nesting reads visually in the flat-text PDF.
/// </summary>
public sealed class PrintRow
{
    public IReadOnlyList<string> Cells { get; init; } = System.Array.Empty<string>();
    public bool IsHeader { get; init; }
    public bool IsTotal { get; init; }
    public int Indent { get; init; }

    public PrintRow() { }

    public PrintRow(params string[] cells) => Cells = cells;

    public static PrintRow Header(params string[] cells) => new() { Cells = cells, IsHeader = true };
    public static PrintRow Total(params string[] cells) => new() { Cells = cells, IsTotal = true };
}

/// <summary>
/// A framework-agnostic, already-formatted report ready to render to PDF (or any other IO target). The
/// UI layer projects its report rows (with amounts already formatted via <c>IndianFormat</c>) into this
/// model, so the renderer shows exactly the figures the on-screen grid shows. No Avalonia, no engine
/// re-computation — just title, subtitle, columns and rows.
/// </summary>
public sealed class PrintReport
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// 🔴 <b>RULING 18 provenance seam.</b> A <see cref="string"/> carries no record of who wrote it, so a
    /// renderer handed <see cref="Title"/> cannot tell the product's own heading ("Trial Balance", an F12 title
    /// override the operator typed for OUR document) from a COUNTERPARTY'S legal name. The distinction is not
    /// per-character, it is structural, and only the producer knows it — so the producer states it here.
    ///
    /// <para><b>Everywhere else in this model the provenance is fixed by construction and needs no flag:</b>
    /// <see cref="PrintColumn.Header"/> captions are compile-time strings this product authored, and
    /// <see cref="PrintRow.Cells"/> are ALWAYS book data projected from masters and vouchers — party names,
    /// narrations, bill references, formatted amounts. Titles are the one place the two are concatenated
    /// (<c>"Ledger Account - " + ledger.Name</c>), which is why the flag lives on the title alone.</para>
    ///
    /// <para>Set it to <see langword="true"/> when any part of <see cref="Title"/> is a master name the user
    /// typed; the renderer then prints the heading verbatim instead of scrubbing the vendor token out of a real
    /// customer's, supplier's or bank's name. It defaults to <see langword="false"/>, so every product-authored
    /// heading keeps the ER-11 guard and every existing producer is byte-identical.</para>
    /// </summary>
    public bool TitleCarriesMasterName { get; init; }
    public IReadOnlyList<PrintColumn> Columns { get; init; } = System.Array.Empty<PrintColumn>();
    public IReadOnlyList<PrintRow> Rows { get; init; } = System.Array.Empty<PrintRow>();
}
