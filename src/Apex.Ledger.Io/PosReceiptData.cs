using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// One item line on a POS retail receipt (catalog §11; Phase 6 slice 7 RQ-44; Study Guide pp.240–242). Quantities
/// and rates arrive pre-formatted from the UI so the renderer stays layout-only; the raw <see cref="Value"/> is
/// kept as <see cref="Money"/> for footing.
/// </summary>
public sealed class PosReceiptItem
{
    /// <summary>Item description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Quantity, already formatted (e.g. "1").</summary>
    public string QuantityText { get; init; } = string.Empty;

    /// <summary>Per-unit rate, already formatted.</summary>
    public string RateText { get; init; } = string.Empty;

    /// <summary>The line's taxable value (paisa-exact) — qty × rate.</summary>
    public Money Value { get; init; }
}

/// <summary>One per-rate GST group on the receipt tax breakup (paisa-exact figures from <c>GstService.ComputeInvoiceTax</c>).</summary>
public sealed class PosReceiptTaxRow
{
    /// <summary>The integrated rate label for the group (e.g. "18%").</summary>
    public string RateLabel { get; init; } = string.Empty;

    /// <summary>The taxable subtotal this group's tax was computed on.</summary>
    public Money TaxableValue { get; init; }

    /// <summary>CGST for the group (0 on an inter-state supply).</summary>
    public Money Cgst { get; init; }

    /// <summary>SGST for the group (0 on an inter-state supply).</summary>
    public Money Sgst { get; init; }

    /// <summary>IGST for the group (0 on an intra-state supply).</summary>
    public Money Igst { get; init; }
}

/// <summary>One tender line on the receipt: the tender kind label, its posted amount, plus optional reference text.</summary>
public sealed class PosReceiptTender
{
    /// <summary>The tender kind label (e.g. "Cash", "Credit/Debit Card", "Cheque/DD", "Gift Voucher").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The posted payable share for this tender (Cash = residual, not tendered).</summary>
    public Money Amount { get; init; }

    /// <summary>A reference detail line printed under the tender (card no / bank + cheque no); blank when none.</summary>
    public string Reference { get; init; } = string.Empty;
}

/// <summary>
/// A framework-agnostic projection of a POS bill ready to render as a retail <b>receipt</b> (catalog §11; RQ-44;
/// DP-6). The thin Avalonia layer resolves the company + POS config, runs the item lines through
/// <c>GstService.ComputeInvoiceTax</c> and fills this DTO; the renderer only lays it out. Deterministic — every
/// date is pre-formatted, no clock/RNG — and de-branded (never the word "Tally", ER-11).
/// </summary>
public sealed class PosReceiptData
{
    /// <summary>The receipt title (POS config default, or a fallback); de-branded on render.
    /// <para>Ignored when <see cref="IsBillOfSupply"/> is set — the document kind follows the supply, not a print
    /// preference (the same rule <c>InvoicePdf</c> applies to the F12 title override).</para></summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// <b>W0-1b.</b> True iff this over-the-counter sale must be documented as a <b>bill of supply</b> rather than a
    /// retail (tax) invoice — CGST Act §31(3)(c) requires one "instead of a tax invoice" from a registered person
    /// supplying exempted goods or services, or paying tax under §10 (composition), and §10(4) bars a composition
    /// dealer from collecting "any tax from the recipient on supplies made by him".
    /// <para>The flag is routed from the SAME predicate the voucher-screen invoice path uses
    /// (<c>VoucherPrintProjector.IsBillOfSupply</c>) — never a second copy of the rule — so one dealer cannot get two
    /// answers from two screens. When set, <c>PosReceiptPdf</c> drops the "Taxable" caption in favour of Rule 49(g)'s
    /// "Value of Supply", drops every tax head and the per-rate breakup (CGST Rule 49 prescribes eight particulars and
    /// none of them is a rate or an amount of tax), and titles the document structurally.</para>
    /// </summary>
    public bool IsBillOfSupply { get; init; }

    /// <summary>
    /// The declaration CGST Rule 5(1)(f) requires a <b>composition</b> taxable person to "mention … at the top of the
    /// bill of supply issued by him", or blank. Blank for a REGULAR dealer's exempt bill of supply — he is not a
    /// composition taxable person — and blank on every ordinary retail invoice, so nothing prints (ER-13).
    /// </summary>
    public string TopDeclaration { get; init; } = string.Empty;

    /// <summary>Seller (store) name printed under the title.</summary>
    public string StoreName { get; init; } = string.Empty;

    /// <summary>Bill / voucher number.</summary>
    public string BillNumber { get; init; } = string.Empty;

    /// <summary>Bill date, already formatted.</summary>
    public string DateText { get; init; } = string.Empty;

    /// <summary>Party / customer name (a walk-in "(cash)" when B2C).</summary>
    public string Party { get; init; } = string.Empty;

    /// <summary>True for an inter-state supply (IGST); false for intra-state (CGST+SGST).</summary>
    public bool IsInterState { get; init; }

    /// <summary>The item rows.</summary>
    public IReadOnlyList<PosReceiptItem> Items { get; init; } = Array.Empty<PosReceiptItem>();

    /// <summary>The per-rate GST breakup groups.</summary>
    public IReadOnlyList<PosReceiptTaxRow> TaxRows { get; init; } = Array.Empty<PosReceiptTaxRow>();

    /// <summary>The tender lines (in stable Gift, Card, Cheque, Cash order).</summary>
    public IReadOnlyList<PosReceiptTender> Tenders { get; init; } = Array.Empty<PosReceiptTender>();

    /// <summary>Σ taxable value over all item rows.</summary>
    public Money TotalTaxable { get; init; }

    /// <summary>Σ CGST over the bill.</summary>
    public Money TotalCgst { get; init; }

    /// <summary>Σ SGST over the bill.</summary>
    public Money TotalSgst { get; init; }

    /// <summary>Σ IGST over the bill.</summary>
    public Money TotalIgst { get; init; }

    /// <summary>Cash tendered by the customer (0 when no cash tender).</summary>
    public Money CashTendered { get; init; }

    /// <summary>The informational change returned = cash tendered − cash payable (≥ 0; never posted).</summary>
    public Money Change { get; init; }

    /// <summary>Thank-you message line 1 (POS config); blank when none.</summary>
    public string Message1 { get; init; } = string.Empty;

    /// <summary>Thank-you message line 2 (POS config); blank when none.</summary>
    public string Message2 { get; init; } = string.Empty;

    /// <summary>The declaration line (POS config); blank when none.</summary>
    public string Declaration { get; init; } = string.Empty;

    /// <summary>Σ all tax (CGST+SGST+IGST).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>The bill grand total = taxable + tax (paisa-exact) — the sum the tenders reconcile to.</summary>
    public Money GrandTotal => new(TotalTaxable.Amount + TotalTax.Amount);
}
