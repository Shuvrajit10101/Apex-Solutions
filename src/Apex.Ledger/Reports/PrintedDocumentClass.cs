namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Whether a printed document is one WE issue, one we merely RECORD, or no statutory document at all.</b>
/// The first of the three axes <see cref="PrintedDocumentClass"/> separates (T0-11 / ADR-0002).
/// </summary>
public enum DocumentRole
{
    /// <summary>No statutory document is issued for this voucher at all. It prints as the plain Dr/Cr voucher, which
    /// states every posted leg exactly and names no document kind. Two shipped shapes reach it: an ordinary
    /// As-Voucher sale (no invoice-mode entry), and the §10 contradiction — a composition dealer's outward supply
    /// that nonetheless recorded forward tax, for which neither a tax invoice nor a bill of supply may be issued.
    /// <para><b>🔴 This member is NOT in the T0-11 design's two-value <c>Issued|Recorded</c> enum, and it is not
    /// optional.</b> The shipped code has a third outcome — pinned by
    /// <c>OneBillOfSupplyRuleDelegationTests.The_section_10_contradiction_files_BIL_while_printing_no_statutory_title_at_all</c>
    /// — and slice S1's contract is byte-identity, so the seam has to be able to express it. Collapsing it into
    /// <see cref="Recorded"/> would say a plain voucher is a recipient-side record document, which is a different
    /// (and false) statement.</para></summary>
    NoStatutoryDocument,

    /// <summary>WE are the statutory issuer: the document is one this company is entitled — or obliged — to issue,
    /// so our identity heads it and our declaration and signature belong on it. CGST Act §31(1)/(2) put the tax
    /// invoice on "a registered person supplying"; §31(3)(c) puts the bill of supply on the same person.</summary>
    Issued,

    /// <summary>We are the RECIPIENT and the document merely records a supply made TO us by someone else; the
    /// supplier is the statutory issuer, so we state no entitlement of our own on it.
    /// <para>Reserved by slice S1 and unreachable from <see cref="GstReportSupport.ClassifyPrintedDocument"/> until
    /// slice S2 lands the purchase record — S1 is a zero-behaviour-change refactor and may not create a document
    /// class that does not exist today.</para></summary>
    Recorded,
}

/// <summary>
/// <b>What the document may say about tax, and WHOSE tax it is.</b>
///
/// <para><b>🔴 Slice S2 replaced the boolean <c>StatesTaxWeCharged</c> this occupies the place of, and the reason is
/// the same one that produced the whole three-axis split: one boolean cannot carry three answers.</b> S1 could get
/// away with two values because every document it expressed was one WE issued, so "does it state tax" and "is the
/// tax ours" were the same question. On a recipient-side record they come apart — the record MUST state the tax
/// (it is what substantiates the input tax credit we claim) and that tax is emphatically NOT ours. Carrying the old
/// boolean as <c>true</c> there would have made the record assert we charged it; carrying it as <c>false</c> would
/// have suppressed the figures, because <c>VoucherPrintProjector</c> drives every tax suppression off it.</para>
/// </summary>
public enum TaxParticulars
{
    /// <summary><b>None at all.</b> CGST Rule 49 prescribes eight particulars for a bill of supply and none of them
    /// is a rate or an amount of tax; §10(4) forbids a composition dealer to collect any. The per-rate breakup, the
    /// per-head totals and the intra/inter caption are all dropped.</summary>
    None,

    /// <summary><b>Stated, as tax WE charged.</b> A Rule-46 tax invoice, whose (l) "rate of tax" and (m) "amount of
    /// tax charged" are our own charge to the recipient. Also the plain Dr/Cr voucher, which states every posted leg
    /// exactly as recorded — including any Output tax leg.</summary>
    AsChargedByUs,

    /// <summary><b>Stated, as tax the SUPPLIER charged us.</b> A recipient-side record: the figures are read from the
    /// posted Input legs and are the supplier's charge, so they are captioned as his. Suppressing them instead would
    /// make the document useless for verifying the input tax credit it exists to substantiate; captioning them as
    /// ours would be a false statutory statement on a document headed by someone else's identity.</summary>
    AsChargedByTheSupplier,
}

/// <summary>
/// <b>Whose identity heads the printed document.</b> The third axis (ADR-0002) — the one neither the census nor the
/// original requirement saw. CGST Rule 46(a) puts "name, address and GSTIN of the supplier" at the head of the
/// document, so on a supply made TO us the SUPPLIER heads it and we appear as the recipient — the exact reverse of
/// every outward invoice this app has ever printed.
/// </summary>
public enum PartyOrientation
{
    /// <summary>We are the supplier: our company block heads the document and the counterparty is the recipient.
    /// Every shipped document is this way round.</summary>
    WeAreSupplier,

    /// <summary>We are the recipient: the counterparty's block heads the document and ours is the recipient block.
    /// Reserved by slice S1; first produced by slice S2's purchase record.</summary>
    WeAreRecipient,
}

/// <summary>
/// <b>The one classification a printed document is derived from — entitlement, rendering and identity-orientation
/// held apart instead of conflated into a single boolean</b> (census T0-11; ADR-0002).
///
/// <para><b>The conflation this exists to end.</b> <see cref="GstReportSupport.IsTaxInvoice"/> answers
/// "<i>may we issue a Rule-46 tax invoice?</i>" — and Sales-only is the CORRECT answer to that question, because
/// CGST Act §31(1) attaches the duty to "a registered person supplying". It was nonetheless the predicate the
/// printer used to answer a completely different question — "<i>should this render with item detail?</i>" — so a
/// Purchase item invoice printed as a Dr/Cr voucher with no item table at all. Adding a second parallel boolean
/// would have recreated the same conflation one layer down; this record makes each question its own field, and
/// every consumer (the projector, the PDF title, the drill badge, the on-screen mirror) reads the SAME instance —
/// which is the structural answer to the FIX-W1e failure class, where three layers each re-derived the document
/// kind and one of them drifted.</para>
///
/// <para><b>Computed, never stored.</b> It is derived at print time from the posted voucher and the company's
/// masters; nothing here is persisted, so it costs no schema version.</para>
///
/// <para><b>Ruling 9 — the axes themselves are OURS.</b> The Apex corpus documents no entitlement/rendering split
/// and no law-driven title derivation; its only title mechanism is a free-text per-voucher-type default. This
/// classification can therefore never join the corpus-verified set, whatever its statutory grounding.</para>
/// </summary>
/// <param name="Role">Whether we issue the document, merely record it, or issue none at all.</param>
/// <param name="Title">The statutory title the document bears, e.g. "TAX INVOICE". <b>Empty</b> when
/// <paramref name="Role"/> is <see cref="DocumentRole.NoStatutoryDocument"/> — a plain voucher names no document
/// kind, and a non-empty title there is precisely the false statement this seam exists to prevent.</param>
/// <param name="ScreenLabel">The same document kind spelled for the drill badge ("Tax Invoice" / "Bill of Supply"),
/// empty when there is none. It rides on the record rather than being re-spelled by the view model, because a
/// mechanical title-case of <paramref name="Title"/> yields "Bill Of Supply" — the badge and the paper have to come
/// out of ONE decision, and that decision has to carry both spellings or the view model grows a second one.</param>
/// <param name="RendersItemDetail">Whether the document renders as an invoice-shaped page (party blocks, item/line
/// table, totals) rather than as a plain Dr/Cr voucher. <b>This is the rendering question, and it is NOT the
/// entitlement question</b> — that is the whole point of the record.</param>
/// <param name="StatesTax">What the document may say about tax, and whose tax it is — see
/// <see cref="TaxParticulars"/>. <see cref="TaxParticulars.None"/> is what every tax suppression in the projector
/// and the renderer keys on, and it is exactly the bill-of-supply case.</param>
/// <param name="Heads">Whose identity heads the document (Rule 46(a)).</param>
/// <param name="StatesOurDeclarationAndSignature">Whether OUR declaration and signature block belong on the
/// document. Rule 46(q) and Rule 53(1A) put the signature on the ISSUER, so a document we merely record carries the
/// supplier's, never ours.</param>
public sealed record PrintedDocumentClass(
    DocumentRole Role,
    string Title,
    string ScreenLabel,
    bool RendersItemDetail,
    TaxParticulars StatesTax,
    PartyOrientation Heads,
    bool StatesOurDeclarationAndSignature);
