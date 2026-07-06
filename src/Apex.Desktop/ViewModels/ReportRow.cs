using System;
using Apex.Ledger;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A single presentation row for the report grids. Carries a particulars label,
/// an optional secondary label (group), and up to two amount columns (debit/credit or a single
/// amount). <see cref="IsTotal"/> and <see cref="IsHeader"/> drive bold/underlined styling.
/// <see cref="IsTwoColumn"/> tells the grid which value cell(s) to show — Debit+Credit for a
/// two-column (Trial Balance) row, or the single Amount cell otherwise — so the Credit and Amount
/// cells (which share the same grid column) never render on top of each other.
/// </summary>
public sealed class ReportRow
{
    public string Particulars { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string Debit { get; init; } = string.Empty;
    public string Credit { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public bool IsTotal { get; init; }
    public bool IsHeader { get; init; }

    /// <summary>
    /// Left-indent (pixels) for a hierarchical particulars label — 0 for a flat row, deeper for a
    /// nested one (e.g. a child cost centre under its parent in the Cost Centre Break-up). Bound via the
    /// account tree's <c>IndentToThicknessConverter</c> so nesting reads visually.
    /// </summary>
    public double Indent { get; init; }

    /// <summary>
    /// True for a Dr/Cr row (Debit + Credit cells shown); false for a single-amount row
    /// (only the Amount cell shown). Drives per-cell visibility so the Credit and Amount cells,
    /// which occupy the same grid column, are mutually exclusive.
    /// </summary>
    public bool IsTwoColumn { get; init; }

    // ---------------------------------------------------------------- inventory-report columns (slice 3.4b)
    // The accounting reports (TB/BS/P&L/Day Book) use Particulars/Secondary/Debit/Credit/Amount above. The
    // inventory reports need wider, per-report column sets (e.g. Stock Summary = Item | Closing Qty | Rate |
    // Value; a register = Date | No. | Party | Item | Godown | Qty | Rate | Value | Batch). Rather than a
    // second row model, these generic string cells carry each inventory report's columns, projected by the
    // ReportsViewModel Build* methods and rendered by the per-ReportKind inventory DataTemplates in the view.

    /// <summary>Inventory column 1 (e.g. Godown / Date / Item, per report).</summary>
    public string Col1 { get; init; } = string.Empty;

    /// <summary>Inventory column 2.</summary>
    public string Col2 { get; init; } = string.Empty;

    /// <summary>Inventory column 3.</summary>
    public string Col3 { get; init; } = string.Empty;

    /// <summary>Inventory column 4.</summary>
    public string Col4 { get; init; } = string.Empty;

    /// <summary>Inventory column 5.</summary>
    public string Col5 { get; init; } = string.Empty;

    /// <summary>Inventory column 6.</summary>
    public string Col6 { get; init; } = string.Empty;

    /// <summary>Inventory column 7.</summary>
    public string Col7 { get; init; } = string.Empty;

    /// <summary>Inventory column 8.</summary>
    public string Col8 { get; init; } = string.Empty;

    /// <summary>
    /// The stock item this row drills to (Stock Summary → Stock Item Movement). Non-null only on a
    /// selectable Stock-Summary item row; null for headers, totals and other reports. Drives whether
    /// Enter/double-click drills in.
    /// </summary>
    public Guid? DrillStockItemId { get; init; }

    /// <summary>
    /// The ledger this row drills to (RQ-7 universal drill-down): Enter on a Trial-Balance / Balance-Sheet /
    /// Profit-&amp;-Loss ledger row opens that ledger's vouchers (a <c>LedgerBook</c>) for the report period.
    /// <see cref="Guid.Empty"/> on synthetic/total/heading rows (folded Net Profit, derived Stock-in-Hand,
    /// section headers) so Enter is a safe no-op there. Mirrors the engine row's <c>LedgerId</c>.
    /// </summary>
    public Guid DrillLedgerId { get; init; }

    /// <summary>
    /// The voucher this row drills to (RQ-7): Enter on a Day Book row — or on a ledger-vouchers row inside a
    /// drilled <c>LedgerBook</c> column — opens that voucher's read-only detail. <see cref="Guid.Empty"/> on
    /// header/total/no-voucher rows. Mirrors the engine row's <c>VoucherId</c>.
    /// </summary>
    public Guid DrillVoucherId { get; init; }

    /// <summary>
    /// True when this row can be drilled into by any of the three drill keys (a stock item, a ledger, or a
    /// voucher). Section headers, totals and computed/synthetic rows carry no drill key, so Enter is a safe
    /// no-op on them (it never calls the engine with <see cref="Guid.Empty"/>).
    /// </summary>
    public bool CanDrill => DrillStockItemId is not null
        || DrillLedgerId != Guid.Empty
        || DrillVoucherId != Guid.Empty;

    /// <summary>Alias of <see cref="CanDrill"/> for the RQ-7 accounting-report grid (Enter guard).</summary>
    public bool IsDrillable => CanDrill;

    public static ReportRow Line(string particulars, Money amount, string secondary = "")
        => new() { Particulars = particulars, Secondary = secondary, Amount = IndianFormat.Amount(amount) };

    public static ReportRow DrCrLine(string particulars, Money debit, Money credit, string secondary = "")
        => new()
        {
            Particulars = particulars,
            Secondary = secondary,
            Debit = IndianFormat.Amount(debit),
            Credit = IndianFormat.Amount(credit),
            IsTwoColumn = true,
        };

    public static ReportRow Total(string particulars, Money amount)
        => new() { Particulars = particulars, Amount = IndianFormat.AmountAlways(amount), IsTotal = true };

    public static ReportRow DrCrTotal(string particulars, Money debit, Money credit)
        => new()
        {
            Particulars = particulars,
            Debit = IndianFormat.AmountAlways(debit),
            Credit = IndianFormat.AmountAlways(credit),
            IsTotal = true,
            IsTwoColumn = true,
        };
}
