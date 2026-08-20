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
/// (RQ-11; Rule 46) — or, when <see cref="IsBillOfSupply"/> is set, as the <b>bill of supply</b> CGST Act §31(3)(c)
/// requires instead (Rule 49; W0-1). The thin Avalonia layer resolves the company (seller) and party (buyer) masters, runs
/// the item-invoice through <c>GstService.ComputeInvoiceTax</c>, and fills this DTO with the seller/buyer
/// blocks, the item rows, the per-rate tax breakup and the money totals; the renderer only lays it out.
/// Deterministic — every date is pre-formatted; no clock, no RNG.
/// </summary>
public sealed class InvoicePrintData
{
    /// <summary>
    /// The <b>statutory document title</b> this supply must be issued under — <c>"TAX INVOICE"</c> (CGST Rule 46) or
    /// <c>"BILL OF SUPPLY"</c> (CGST Rule 49). Set by the projector from what the supply IS, never from a print
    /// preference; see <see cref="IsBillOfSupply"/>.
    /// <para><b>FIX-W1h — the default is EMPTY, not "TAX INVOICE".</b> A non-blank default defeated
    /// <c>InvoicePdf.Render</c>'s blank-only fallback: a caller writing <c>new InvoicePrintData { IsBillOfSupply =
    /// true, … }</c> without a title got a page with every tax particular suppressed and the title "TAX INVOICE" —
    /// the illegal document, produced by the safety net. Empty means the renderer supplies the title that matches the
    /// document kind, so neither side has to remember.</para>
    /// </summary>
    public string DocumentTitle { get; init; } = string.Empty;

    /// <summary>
    /// True iff this document is a <b>bill of supply</b> under CGST Act §31(3)(c) — issued "instead of a tax invoice"
    /// by "a registered person supplying exempted goods or services or both <b>or</b> paying tax under the provisions
    /// of section 10". It is the structural flag the renderer gates on: CGST Rule 49 prescribes eight particulars and
    /// <b>none of them is a rate or an amount of tax</b> (contrast Rule 46 (l) "rate of tax", (m) "amount of tax
    /// charged", (n) place of supply for an inter-State supply), so a bill of supply shows no tax breakup, no per-head
    /// totals and no intra/inter routing caption. A composition dealer showing tax on his document would also assert a
    /// collection §10(4) and §32(2) forbid him.
    /// </summary>
    public bool IsBillOfSupply { get; init; }

    /// <summary>
    /// True iff this document is a <b>recipient-side RECORD of a supply made TO us</b> — what a Purchase prints as
    /// (RQ-11a; census T0-11 slice S2). It is the structural counterpart of <see cref="IsBillOfSupply"/>: a flag the
    /// renderer gates on, so the renderer is safe against a caller that fills the DTO wrongly rather than merely
    /// safe when the projector fills it rightly.
    ///
    /// <para><b>What it changes, and why each is a truth condition rather than a style choice.</b> The document is
    /// headed by the SUPPLIER (<see cref="Seller"/> carries HIS block and <see cref="Buyer"/> carries ours — CGST
    /// Rule 46(a)), so: the title may be neither outward title (§31(1) and Rule 49 both put those on the supplier);
    /// <see cref="InvoiceNumber"/> is captioned as OUR record reference and never "Invoice No.", which under his
    /// identity would call our number the serial of a document we did not issue; <see cref="PlaceOfSupply"/> is not
    /// stated at all (Rule 46(n) is a supplier particular — we do not determine the place of supply of a supply made
    /// to us); the tax IS stated, because the record is what substantiates the input tax credit we claim, but
    /// captioned as the supplier's charge; and OUR declaration and OUR signature block are suppressed, the signature
    /// most sharply of all — drawn unchanged it would print "For {supplier}" over a signature line on a page we
    /// produced.</para>
    ///
    /// <para><b>Default false, so every already-shipped document is byte-identical (ER-13).</b> Presentational and
    /// structural only: it adds no money field, and no figure on any page moves because of it.</para>
    /// </summary>
    public bool IsRecipientRecord { get; init; }

    /// <summary>
    /// The declaration CGST Rule 5(1)(f) requires "at the <b>top</b> of the bill of supply" issued by a composition
    /// taxable person — "composition taxable person, not eligible to collect tax on supplies". Blank on every other
    /// document, including a <b>regular</b> dealer's exempt bill of supply: he is not a composition taxable person and
    /// Rule 5(1)(f) does not bind him.
    /// </summary>
    public string TopDeclaration { get; init; } = string.Empty;

    /// <summary>Seller (supplier) name / address / GSTIN block.</summary>
    public InvoicePartyBlock Seller { get; init; } = new();

    /// <summary>Buyer (recipient) name / address / GSTIN block.</summary>
    public InvoicePartyBlock Buyer { get; init; } = new();

    /// <summary>
    /// True when the voucher behind this document has been CANCELLED (Phase 10.11 S3). The renderer over-prints
    /// <c>CANCELLED</c> immediately under the document title, on every page, so a printed copy of a voided
    /// invoice cannot be passed off as a live one once it is off the screen — the case that matters most here,
    /// because this document is the one that leaves the building.
    ///
    /// <para><b>Default false, and nothing prints when it is false</b> — every already-shipped invoice PDF is
    /// byte-identical (ER-13). Presentation only: not one figure, particular or total moves, and the statutory
    /// title (<see cref="DocumentTitle"/>) is untouched — a cancelled tax invoice is still, structurally, the
    /// document it was issued as.</para>
    ///
    /// <para><b>🔴 UNVERIFIED-BY-DESIGN — ours, corpus silent.</b> The source corpus describes no printed
    /// treatment of a cancelled document. The over-print is our decision (R7).</para>
    /// </summary>
    public bool IsCancelled { get; init; }

    /// <summary>Invoice serial number (Rule 46 (b)).</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>Invoice date, already formatted (Rule 46 (c)).</summary>
    public string InvoiceDateText { get; init; } = string.Empty;

    /// <summary>The counterparty document number (numbering §8) — on a Sales tax invoice this is the buyer's
    /// "Reference No." (e.g. their PO number); blank when none was captured. Printed only when non-empty, so an
    /// invoice without one stays byte-identical (ER-13).</summary>
    public string ReferenceNo { get; init; } = string.Empty;

    /// <summary>The label for <see cref="ReferenceNo"/> (per base type; "Reference No." on a Sales invoice).</summary>
    public string ReferenceCaption { get; init; } = "Reference No.";

    /// <summary>The counterparty document's date, already formatted; blank when none was captured.</summary>
    public string ReferenceDateText { get; init; } = string.Empty;

    /// <summary>Place of supply — State name + code (Rule 46 (m/n)); required for inter-state supplies.</summary>
    public string PlaceOfSupply { get; init; } = string.Empty;

    /// <summary>
    /// <c>true</c> for an inter-state supply (IGST); <c>false</c> for intra-state (CGST+SGST); <b><c>null</c> when the
    /// document cannot state a head at all</b> — the voucher posted no forward tax leg AND the book declares no home
    /// State, so nothing established a routing (W0-15).
    /// <para><b>Why nullable.</b> On a plain <c>bool</c> the unknown case collapsed into <c>false</c>, and <c>false</c>
    /// is not an absence here — it is the positive claim "intra-state", which <see cref="InvoicePdf"/> spends on a
    /// "Intra-State (CGST + SGST)" caption and a CGST/SGST head-row PAIR. A <c>null</c> emits NEITHER head row and no
    /// caption, exactly as <see cref="IsBillOfSupply"/> already does for a document that states no tax particular.
    /// Every renderer therefore tests <c>is true</c> / <c>is false</c>, never truthiness.</para>
    /// </summary>
    public bool? IsInterState { get; init; }

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

    /// <summary>
    /// Σ <b>Compensation Cess</b> over the invoice (paisa-exact); 0 when the supply bears none — which is every
    /// invoice outside the de-merit/luxury HSNs, so an invoice without cess renders byte-identically to before
    /// (ER-13).
    /// <para>Ring-fenced OUT of <see cref="TotalTax"/> (which stays CGST+SGST+IGST, mirroring
    /// <c>GstService.InvoiceTax.TotalCess</c> and ER-2) but IN <see cref="GrandTotal"/> — cess is a charge the
    /// recipient actually pays, and the posting side already adds it to the party leg, so an invoice that left it out
    /// of the Grand Total under-billed the customer by the whole cess.</para>
    /// </summary>
    public Money TotalCess { get; init; }

    /// <summary>The signed round-off applied to the grand total (0 when none).</summary>
    public Money RoundOff { get; init; }

    /// <summary>Optional narration; printed only when <see cref="PrintConfig.ShowNarration"/> is set.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Σ all GST tax (CGST+SGST+IGST) — <b>excludes</b> the ring-fenced <see cref="TotalCess"/> (ER-2).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>The invoice grand total = taxable + tax + cess + round-off (paisa-exact) — the amount the recipient
    /// owes, which must foot to the posted party leg.</summary>
    public Money GrandTotal =>
        new(TotalTaxable.Amount + TotalTax.Amount + TotalCess.Amount + RoundOff.Amount);
}
