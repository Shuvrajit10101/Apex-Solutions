using Apex.Ledger;
using Apex.Ledger.Reports;

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
/// One <b>additional charge posted against the party</b> that is neither the value of the supply nor GST/cess on it —
/// an <b>additional cost of purchase</b> (Freight, Packing, …; Book pp.133–141, RQ-16) on a purchase record, or
/// §206C <b>TCS</b> collected on top of the GST-inclusive total on an outward invoice.
///
/// <para><b>🔴 WHY THIS TYPE EXISTS (T0-11 review C1/L1-01 — the class, not the case).</b>
/// <see cref="InvoicePrintData"/>'s money vocabulary used to be closed: <c>GrandTotal = TotalTaxable + TotalTax +
/// TotalCess + RoundOff</c>, with <c>TotalTaxable</c> read off the inventory lines alone. Any posted leg outside
/// {goods, GST, cess, round-off} that moves the party total was therefore <b>structurally unrepresentable</b>, and
/// the document silently footed to a different number than the books. Two members of that class were already
/// recorded — §206C TCS (measured 55,810.14 printed against 56,368.14 posted) and, after slice S2 routed a Purchase
/// item invoice here for the first time, the additional cost of purchase (measured 11,800.00 against 13,034.56).
/// A row per charge is the representation; <c>VoucherPrintProjector</c>'s footing refusal is what stops a THIRD
/// member being introduced silently.</para>
///
/// <para><b>The caption is the posted LEDGER's own name</b>, never a label this layer invents: the operator chose
/// "Freight Inward" and that is what the supplier is charging. Inventing a caption would put a description of the
/// charge on the document that no posted leg supports.</para>
/// </summary>
public sealed class InvoiceChargeRow
{
    /// <summary>The charge's caption — the posted ledger's own name (e.g. "Freight Inward", "TCS Payable").</summary>
    public string Caption { get; init; } = string.Empty;

    /// <summary>The paisa-exact amount this charge adds to what the party owes.</summary>
    public Money Amount { get; init; }
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
    ///
    /// <para><b>🔴 IT IS THE ROLE AXIS AND ONLY THE ROLE AXIS (T0-11 review C24/L3-10).</b> It used to answer three
    /// different axes' questions on its own — see <see cref="Heads"/> and
    /// <see cref="StatesOurDeclarationAndSignature"/>, which now carry the other two.</para>
    /// </summary>
    public bool IsRecipientRecord { get; init; }

    /// <summary>
    /// <b>True iff this document is a §34 credit or debit note</b> (T0-11 slice S4; RQ-11b) — issued by us, or
    /// recorded from the counterparty. Presentational and structural only: it carries no money and moves no figure.
    ///
    /// <para><b>What it is for, and why it is a flag rather than a title comparison.</b> The <b>nature of the
    /// document</b> is a mandatory Rule-53 particular, so the F12 title override must not reach a note — the same
    /// rule the bill of supply and the recipient-side record already carry, for the same reason: the document kind
    /// follows the transaction, not a print preference, and a knob that could re-title a credit note "TAX INVOICE"
    /// would state on paper that we supplied something we did not. Keying that refusal on the TITLE STRING instead
    /// would make the guard evaporate the moment the title moved, which is precisely how FIX-W1h and FIX-W2b were
    /// reached; the renderer derives it STRUCTURALLY, exactly as it does for the other two classes.</para>
    ///
    /// <para><b>Default false, so every already-shipped document is byte-identical (ER-13).</b></para>
    /// </summary>
    public bool StatesSection34Note { get; init; }

    /// <summary>
    /// <b>Whose identity HEADS the document</b> (CGST Rule 46(a)) — <see cref="PrintedDocumentClass.Heads"/>, carried
    /// through instead of being re-answered from <see cref="IsRecipientRecord"/>.
    ///
    /// <para><b>🔴 WHY IT EXISTS (T0-11 review C24/L3-10).</b> <see cref="PrintedDocumentClass"/> holds seven fields
    /// across three axes and this DTO carried ONE boolean for all of them, so <see cref="InvoicePdf"/> answered the
    /// ROLE questions (which title, which number caption, whether a place of supply may be stated), the ORIENTATION
    /// questions (which party caption, whose tax the breakup states) and the Rule 46(q) DECLARATION/SIGNATURE
    /// question off the same flag. Nothing pairs the axes upstream — <see cref="PrintedDocumentClass"/> is a bare
    /// positional record with no validating constructor, and the pairing rests solely on one <c>if</c> in
    /// <see cref="GstReportSupport.ClassifyPrintedDocument"/> — so any future branch returning
    /// <see cref="DocumentRole.Recorded"/> with <see cref="PartyOrientation.WeAreSupplier"/> (slice S4's
    /// purchase-return record, slice S5's §31(3)(f) self-invoice, where WE are the issuer) produced a half-swapped
    /// page: measured, our own name and GSTIN under the fixed literal "Supplier:", the counterparty under
    /// "Recipient:", and the record legend asserting the page records "a document issued by the supplier named
    /// above" — the party named above being us.</para>
    ///
    /// <para><b>The default is the COHERENT pairing, not a constant</b>: with nothing set it follows
    /// <see cref="IsRecipientRecord"/>, which is exactly what the classifier produces and exactly what every caller
    /// that predates this field meant. So every shipped document and every hand-built DTO is byte-identical (ER-13),
    /// while a caller that genuinely needs the other pairing can now say so instead of being silently overruled.</para>
    /// </summary>
    public PartyOrientation Heads
    {
        get => _heads ?? (IsRecipientRecord ? PartyOrientation.WeAreRecipient : PartyOrientation.WeAreSupplier);
        init => _heads = value;
    }
    private readonly PartyOrientation? _heads;

    /// <summary>
    /// <b>Whether OUR declaration and OUR signature block belong on this document</b> —
    /// <see cref="PrintedDocumentClass.StatesOurDeclarationAndSignature"/>, carried through. CGST Rule 46(q) and Rule
    /// 53(1A) put the signature on the ISSUER, so a document we merely record carries the supplier's, never ours.
    ///
    /// <para><b>🔴 The axis was WRITE-ONLY before this (T0-11 review C22/L3-08, C24/L3-10).</b> The classifier set it
    /// on both branches and no production code read it: the renderer dropped the signature off
    /// <see cref="IsRecipientRecord"/> instead — a proxy from a different axis, and the whole reason the axes were
    /// split apart. A purpose-built field that nothing reads is a claim the code does not keep.</para>
    ///
    /// <para><b>Default = the coherent pairing</b> (<c>!IsRecipientRecord</c>), so nothing already shipped moves.</para>
    /// </summary>
    public bool StatesOurDeclarationAndSignature
    {
        get => _statesOurDeclarationAndSignature ?? !IsRecipientRecord;
        init => _statesOurDeclarationAndSignature = value;
    }
    private readonly bool? _statesOurDeclarationAndSignature;

    /// <summary>
    /// True iff this is a recipient-side record of an inward supply that <b>bore no tax and could bear none</b> — a
    /// wholly exempt, nil-rated or non-GST purchase. Derived by the projector from the POSTED legs (no tax, no cess),
    /// like every other figure on this path; never from a live master.
    ///
    /// <para><b>🔴 CARRIED, NOT YET READ — and that is deliberate, not an oversight (T0-11 review C6/L1-06,
    /// C7/L1-07).</b> Two statements a wholly exempt record makes today have no referent on the transaction: the
    /// intra/inter head caption with its <c>CGST 0.00 / SGST 0.00</c> head-row pair, and the totals-band label
    /// "Taxable Value" over money that was never taxable (the outward twin of that supply says "Value of Supply").
    /// Both are corrections to <b>what a purchase record says about tax</b>, which is an OPEN R12 question for the
    /// user (<c>plan.md</c> Phase 10.13 question 1), so neither may be moved on this pass. What could be closed is
    /// the reason a renderer-only patch would have been WRONG for both: the fact simply could not be expressed —
    /// <see cref="IsRecipientRecord"/> is true on a TAXED record too, where "Taxable Value" is the truth and the head
    /// rows have a referent. It is expressible now, and pinned.</para>
    /// </summary>
    public bool IsInwardExempt { get; init; }

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

    // ---------------------------------------------------------------- e-Invoice (census T0-9)

    /// <summary>
    /// The IRP-issued <b>signed QR string</b>, verbatim, for a supply issued under CGST Rule 48(4); blank on every
    /// other document. When set, <see cref="InvoicePdf"/> encodes it as a QR symbol and prints it - the particular
    /// CGST Rule 46(r) requires: "Quick Response code, having embedded Invoice Reference Number (IRN) in it, in case
    /// invoice has been issued in the manner prescribed under sub-rule (4) of rule 48" (inserted w.e.f. 30-09-2020 by
    /// Notification 72/2020-CT; source <c>https://taxinformation.cbic.gov.in/</c>, CGST Rule 46).
    ///
    /// <para><b>Why this is not cosmetic.</b> CGST Rule 48(5): "Every invoice issued by a person to whom sub-rule (4)
    /// applies in any manner other than the manner specified in the said sub-rule <b>shall not be treated as an
    /// invoice</b>." A covered supply whose printed document omits the QR is not a document with a missing decoration;
    /// it is a document the law declines to recognise, and the recipient's input tax credit hangs off it.</para>
    ///
    /// <para><b>VERBATIM, and that is structural (ER-5).</b> This carries the IRP's own signed string, character for
    /// character - never re-derived, re-serialised, case-folded or de-branded. The signature over the payload is the
    /// entire point: it is what lets anyone verify the invoice offline against the IRP's public key. A QR rebuilt from
    /// parsed fields would encode the same facts and prove nothing.</para>
    ///
    /// <para>Blank on every document that is not an e-invoice, and nothing is drawn or measured when it is blank -
    /// so every already-shipped invoice PDF renders byte-identically (ER-13).</para>
    /// </summary>
    public string EInvoiceSignedQr { get; init; } = string.Empty;

    /// <summary>
    /// The 64-character Invoice Reference Number, printed as human-readable text beside the QR; blank on every other
    /// document.
    ///
    /// <para><b>OURS - permitted, not required.</b> The GST Network's e-invoice FAQ (v1.4, 30-03-2021) Q69 answers
    /// "is it mandatory to print the IRN on the invoice" with: "No. It's optional. IRN is anyway embedded in the QR
    /// Code which is one of the mandatory particulars on invoice"
    /// (<c>https://www.gstn.org.in/assets/mainDashboard/Pdf/GST%20e-invoice%20System%20-%20FAQs%20-%20Version%201.4%20Dt.%2030-3-2021.pdf</c>).
    /// We print it because a human reading a paper invoice cannot read a QR code, and the IRN is the one string that
    /// lets them look the document up. The source corpus is silent on the whole subject (A14 sweep, 2026-08-19: zero
    /// occurrences of "IRN", "QR" or "IRP" across all ten PDFs), so nothing here narrows an attested behaviour.</para>
    /// </summary>
    public string EInvoiceIrn { get; init; } = string.Empty;

    /// <summary>The IRP acknowledgement number; blank on every other document. <b>OURS</b> - the same GSTN FAQ (Q73)
    /// says "There is no mandate to print these particulars on invoice copy" and that they "are only for reference".
    /// Printed because they are what the portal asks for when a document is queried.</summary>
    public string EInvoiceAckNo { get; init; } = string.Empty;

    /// <summary>The IRP acknowledgement date, already formatted; blank on every other document. <b>OURS</b>, on the
    /// same footing as <see cref="EInvoiceAckNo"/>.</summary>
    public string EInvoiceAckDateText { get; init; } = string.Empty;

    /// <summary>
    /// True iff this document carries e-invoice particulars to print. <b>The single gate</b> the renderer measures
    /// with and draws with, and the on-screen preview mirrors - three surfaces, one predicate, so none of them can
    /// state an e-invoice the others do not.
    /// </summary>
    public bool StatesEInvoice =>
        !string.IsNullOrWhiteSpace(EInvoiceSignedQr) || !string.IsNullOrWhiteSpace(EInvoiceIrn);

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

    /// <summary>
    /// The <b>additional charges posted against the party</b> that are neither the value of the supply nor GST/cess
    /// on it — see <see cref="InvoiceChargeRow"/> for the defect class this closes. Each row is stated on the
    /// document under the posted ledger's own name and each reaches <see cref="GrandTotal"/>, so the printed demand
    /// is the debt the general ledger recorded.
    ///
    /// <para><b>Placed AFTER the tax heads, deliberately.</b> These charges did not bear the GST stated above them
    /// (the accept path computes GST from the item lines alone), so listing them before the heads would invite a
    /// reader to conclude the tax was charged on them too.</para>
    ///
    /// <para><b>Default empty ⇒ nothing prints ⇒ every already-shipped document is byte-identical (ER-13).</b></para>
    /// </summary>
    public IReadOnlyList<InvoiceChargeRow> OtherCharges { get; init; } = Array.Empty<InvoiceChargeRow>();

    /// <summary>The signed round-off applied to the grand total (0 when none).</summary>
    public Money RoundOff { get; init; }

    /// <summary>Optional narration; printed only when <see cref="PrintConfig.ShowNarration"/> is set.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Σ all GST tax (CGST+SGST+IGST) — <b>excludes</b> the ring-fenced <see cref="TotalCess"/> (ER-2).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>Σ <see cref="OtherCharges"/> (paisa-exact); 0 when the document bears none.</summary>
    public Money TotalOtherCharges
    {
        get
        {
            var total = 0m;
            foreach (var c in OtherCharges) total += c.Amount.Amount;
            return new Money(total);
        }
    }

    /// <summary>The invoice grand total = taxable + tax + cess + other charges + round-off (paisa-exact) — the amount
    /// the recipient owes, which must foot to the posted party leg.
    /// <para><b>The <see cref="TotalOtherCharges"/> term is what makes that last clause true rather than aspirational</b>
    /// (T0-11 review C1). Before it, any posted party-side charge outside {goods, GST, cess, round-off} was dropped
    /// from the demand with nothing on the page naming it; <c>VoucherPrintProjector</c> now refuses to project a
    /// document whose Grand Total and posted party leg disagree, so the two can only be reconciled by stating the
    /// charge, never by quietly omitting it.</para></summary>
    public Money GrandTotal =>
        new(TotalTaxable.Amount + TotalTax.Amount + TotalCess.Amount + TotalOtherCharges.Amount + RoundOff.Amount);
}
