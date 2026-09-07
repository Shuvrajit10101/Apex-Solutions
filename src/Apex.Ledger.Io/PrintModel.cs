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
    /// <para><see cref="PrintColumn.Header"/> captions need no flag: every one is a compile-time string this
    /// product authored, so the renderer scrubs the whole caption band unconditionally.</para>
    ///
    /// <para>🔴 <b><see cref="PrintRow.Cells"/> are NOT all book data, and a body cell is nonetheless drawn
    /// VERBATIM — so a producer that puts ITS OWN text in one must de-brand it AT SOURCE.</b> The overwhelming
    /// majority of body cells are projected from masters and vouchers (party names, narrations, bill references,
    /// formatted amounts) and must ship untouched, which is why the renderer's body-cell scrub was removed. But
    /// several producers do write product-authored prose into a body cell — the <c>"For &lt;company&gt;"</c>
    /// signatory line of the Reminder Letter and the Confirmation of Accounts, and the preview mirror's
    /// salutations, copy-marking label, composition declaration and store messages. An earlier version of this
    /// paragraph claimed body cells are ALWAYS book data; that claim was false, and it let our own company name
    /// reach the signature line of two letters posted to counterparties. Cells carry no flag because the fix
    /// belongs at the producer, which already knows the string is ours;
    /// <c>MultiAccountPrintProjector.SignatoryLine</c> is the worked example and
    /// <c>MultiAccountPartyNameOnPaperTests</c> pins it on both letters.</para>
    ///
    /// <para>Set it to <see langword="true"/> when any part of <see cref="Title"/> is a master name the user
    /// typed; the renderer then prints the heading verbatim instead of scrubbing the vendor token out of a real
    /// customer's, supplier's or bank's name. It defaults to <see langword="false"/>, so every product-authored
    /// heading keeps the ER-11 guard and every existing producer is byte-identical. Its tabular twin is
    /// <see cref="TabularExport.TitleCarriesMasterName"/>, so the PDF and the four tabular formats cannot
    /// disagree about one heading.</para>
    /// </summary>
    public bool TitleCarriesMasterName { get; init; }
    public IReadOnlyList<PrintColumn> Columns { get; init; } = System.Array.Empty<PrintColumn>();
    public IReadOnlyList<PrintRow> Rows { get; init; } = System.Array.Empty<PrintRow>();
}
