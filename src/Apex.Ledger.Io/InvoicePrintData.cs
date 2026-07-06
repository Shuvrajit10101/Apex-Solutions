using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// A name / multi-line address / GSTIN block for either party on a tax invoice (the seller "from" block or
/// the buyer "bill-to" block). Rule 46 requires the name, address and GSTIN of both supplier and recipient.
/// </summary>
public sealed class InvoicePartyBlock
{
    /// <summary>Legal / trade name of the party.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Address lines (each printed on its own line); may be empty.</summary>
    public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();

    /// <summary>The party's GSTIN, or blank for an unregistered / B2C recipient.</summary>
    public string Gstin { get; init; } = string.Empty;

    /// <summary>The party's State name + 2-digit GST code (e.g. "West Bengal (19)"); blank when unset.</summary>
    public string StateText { get; init; } = string.Empty;
}

/// <summary>
/// One item row on a tax invoice (Rule 46: description, HSN/SAC, quantity, rate, taxable value). Quantities
/// and money are already formatted to display strings by the UI so the renderer stays layout-only; the raw
/// <see cref="TaxableValue"/> is kept as <see cref="Money"/> for footing checks.
/// </summary>
public sealed class InvoiceItemRow
{
    /// <summary>Item / service description (Rule 46 (f)).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>HSN (goods) or SAC (services) code (Rule 46 (g)).</summary>
    public string HsnSac { get; init; } = string.Empty;

    /// <summary>Quantity, already formatted (e.g. "10.000"); blank for a service line.</summary>
    public string QuantityText { get; init; } = string.Empty;

    /// <summary>Per-unit rate, already formatted.</summary>
    public string RateText { get; init; } = string.Empty;

    /// <summary>The line's taxable (assessable) value (paisa-exact) — its qty × rate.</summary>
    public Money TaxableValue { get; init; }
}

/// <summary>
/// One GST rate group in the tax breakup: the rate label (e.g. "18%"), its taxable subtotal and the tax
/// under each head. For an intra-state supply CGST and SGST are populated (each half the rate) and IGST is
/// zero; for an inter-state supply IGST carries the whole tax and CGST/SGST are zero. These are the paisa-
/// exact figures the GST engine (<c>GstService.ComputeInvoiceTax</c>) produced, so the printed breakup
/// reconciles with the posted tax ledgers to the paisa.
/// </summary>
public sealed class InvoiceTaxRow
{
    /// <summary>The integrated rate label for the group (e.g. "18%").</summary>
    public string RateLabel { get; init; } = string.Empty;

    /// <summary>The taxable subtotal this group's tax was computed on (paisa-exact).</summary>
    public Money TaxableValue { get; init; }

    /// <summary>CGST for the group (0 on an inter-state supply).</summary>
    public Money Cgst { get; init; }

    /// <summary>SGST for the group (0 on an inter-state supply).</summary>
    public Money Sgst { get; init; }

    /// <summary>IGST for the group (0 on an intra-state supply).</summary>
    public Money Igst { get; init; }
}

/// <summary>
/// A framework-agnostic projection of an item-invoice (Sales) ready to render as a GST <b>tax invoice</b>
/// (RQ-11; Rule 46). The thin Avalonia layer resolves the company (seller) and party (buyer) masters, runs
/// the item-invoice through <c>GstService.ComputeInvoiceTax</c>, and fills this DTO with the seller/buyer
/// blocks, the item rows, the per-rate tax breakup and the money totals; the renderer only lays it out.
/// Deterministic — every date is pre-formatted; no clock, no RNG.
/// </summary>
public sealed class InvoicePrintData
{
    /// <summary>Seller (supplier) name / address / GSTIN block.</summary>
    public InvoicePartyBlock Seller { get; init; } = new();

    /// <summary>Buyer (recipient) name / address / GSTIN block.</summary>
    public InvoicePartyBlock Buyer { get; init; } = new();

    /// <summary>Invoice serial number (Rule 46 (b)).</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>Invoice date, already formatted (Rule 46 (c)).</summary>
    public string InvoiceDateText { get; init; } = string.Empty;

    /// <summary>Place of supply — State name + code (Rule 46 (m/n)); required for inter-state supplies.</summary>
    public string PlaceOfSupply { get; init; } = string.Empty;

    /// <summary>True for an inter-state supply (IGST); false for intra-state (CGST+SGST).</summary>
    public bool IsInterState { get; init; }

    /// <summary>The item rows.</summary>
    public IReadOnlyList<InvoiceItemRow> Items { get; init; } = Array.Empty<InvoiceItemRow>();

    /// <summary>The per-rate GST breakup groups.</summary>
    public IReadOnlyList<InvoiceTaxRow> TaxRows { get; init; } = Array.Empty<InvoiceTaxRow>();

    /// <summary>Σ taxable value over all item rows (paisa-exact).</summary>
    public Money TotalTaxable { get; init; }

    /// <summary>Σ CGST over the invoice (paisa-exact).</summary>
    public Money TotalCgst { get; init; }

    /// <summary>Σ SGST over the invoice (paisa-exact).</summary>
    public Money TotalSgst { get; init; }

    /// <summary>Σ IGST over the invoice (paisa-exact).</summary>
    public Money TotalIgst { get; init; }

    /// <summary>The signed round-off applied to the grand total (0 when none).</summary>
    public Money RoundOff { get; init; }

    /// <summary>Optional narration; printed only when <see cref="PrintConfig.ShowNarration"/> is set.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Σ all tax (CGST+SGST+IGST).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>The invoice grand total = taxable + tax + round-off (paisa-exact).</summary>
    public Money GrandTotal => new(TotalTaxable.Amount + TotalTax.Amount + RoundOff.Amount);
}
