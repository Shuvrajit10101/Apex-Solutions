namespace Apex.Ledger.Domain;

/// <summary>
/// GST registration type of a company or party (catalog §12; phase4 RQ-2/RQ-7). In Phase 4 only
/// <see cref="Regular"/> has a working tax path; <see cref="Composition"/> is stored but inert (Phase 9),
/// while <see cref="Unregistered"/> and <see cref="Consumer"/> mark a B2C party (no GSTIN).
/// </summary>
public enum GstRegistrationType
{
    /// <summary>Regularly registered dealer with a GSTIN (the only working type in Phase 4).</summary>
    Regular,

    /// <summary>Composition dealer (stored; no composition tax path until Phase 9).</summary>
    Composition,

    /// <summary>Unregistered business (no GSTIN) — a B2C party.</summary>
    Unregistered,

    /// <summary>End consumer (no GSTIN) — a B2C party.</summary>
    Consumer,
}

/// <summary>
/// The composition-dealer sub-type (Phase 9 slice 3; RQ-4; §10 + Rule 7). Drives the tax-on-turnover rate AND the
/// turnover base: Manufacturer/Restaurant tax the TOTAL turnover in state (incl. exempt); Trader/ServiceProvider tax
/// only TAXABLE supplies. Meaningful only when the company's <see cref="GstRegistrationType"/> is Composition. Stored
/// as the enum ordinal (Manufacturer=0, Trader=1, Restaurant=2, ServiceProvider=3).
/// </summary>
public enum CompositionSubType
{
    /// <summary>§10(1) manufacturer of non-excluded goods — 1% (0.5% C + 0.5% S) on TOTAL turnover in State/UT.</summary>
    Manufacturer,

    /// <summary>§10(1) trader / other supplier of goods — 1% (0.5% + 0.5%) on TAXABLE supplies only.</summary>
    Trader,

    /// <summary>Sch-II 6(b) restaurant (non-alcohol) — 5% (2.5% + 2.5%) on TOTAL turnover in State/UT.</summary>
    Restaurant,

    /// <summary>§10(2A) service provider / mixed supplier — 6% (3% + 3%) on TAXABLE supplies; ₹50 L threshold.</summary>
    ServiceProvider,
}

/// <summary>
/// The GST taxability of a stock item / sales-purchase ledger line (catalog §12; phase4 RQ-8/RQ-15).
/// Only <see cref="Taxable"/> lines attract tax; the other three attract zero tax but still record value
/// for the returns.
/// </summary>
public enum GstTaxability
{
    /// <summary>Taxable at the resolved GST rate.</summary>
    Taxable,

    /// <summary>Exempt supply — no tax.</summary>
    Exempt,

    /// <summary>Nil-rated supply (0% rate) — no tax.</summary>
    NilRated,

    /// <summary>Outside GST (non-GST supply, e.g. petrol/alcohol) — no tax.</summary>
    NonGst,
}

/// <summary>GSTR-1 (and paired 3B) return periodicity election (catalog §12; phase4 RQ-2/RQ-23).</summary>
public enum GstReturnPeriodicity
{
    /// <summary>Monthly filing (turnover &gt; ₹1.5 cr).</summary>
    Monthly,

    /// <summary>Quarterly filing (QRMP scheme).</summary>
    Quarterly,
}

/// <summary>
/// The tax head of a GST tax ledger / tax line (catalog §12; phase4 RQ-4/RQ-12). <see cref="Central"/> and
/// <see cref="State"/> split an intra-state supply (each half the rate); <see cref="Integrated"/> carries the
/// full rate on an inter-state supply. <see cref="Cess"/> is a forward-compat seam only (ER-9) — no Cess
/// computation ships in Phase 4.
/// </summary>
public enum GstTaxHead
{
    /// <summary>CGST — central tax (intra-state, half rate).</summary>
    Central,

    /// <summary>SGST/UTGST — state or union-territory tax (intra-state, half rate). UTGST folds into this head.</summary>
    State,

    /// <summary>IGST — integrated tax (inter-state, full rate).</summary>
    Integrated,

    /// <summary>Compensation Cess — seam only; unused in Phase 4 (Phase 9).</summary>
    Cess,
}

/// <summary>
/// The direction of a GST tax ledger / tax line (catalog §12; phase4 DP-11). Derived from the voucher base
/// type: Sales/Credit-Note ⇒ <see cref="Output"/> (liability); Purchase/Debit-Note ⇒ <see cref="Input"/> (ITC).
/// </summary>
public enum GstTaxDirection
{
    /// <summary>Output tax — a liability on an outward supply (sale).</summary>
    Output,

    /// <summary>Input tax — an eligible ITC on an inward supply (purchase).</summary>
    Input,
}

/// <summary>Type of supply for a stock item / ledger (catalog §12; phase4 RQ-8): goods use HSN, services use SAC.</summary>
public enum GstSupplyType
{
    /// <summary>Goods — classified by HSN.</summary>
    Goods,

    /// <summary>Services — classified by SAC.</summary>
    Services,
}

/// <summary>
/// The rate class of a dated GST rate-history row (Phase 9 slice 1; RQ-1; GST 2.0 eff. 22-Sep-2025). Advisory
/// classification carried on <see cref="GstRateHistoryEntry"/> so the GST Rate Setup screen can group/filter the
/// slabs; it does <b>not</b> alter computation (the rate that flows in is the row's <c>RateBasisPoints</c>).
/// </summary>
public enum GstRateClass
{
    /// <summary>The 18% standard slab (~90% of former-28% items) — and the 0% nil bucket.</summary>
    Standard,

    /// <summary>The 5% merit slab (~99% of former-12% items moved here).</summary>
    Merit,

    /// <summary>A surviving special rate (3% bullion, 1.5% cut diamonds, 0.25% rough diamonds) alongside the slabs.</summary>
    Special,

    /// <summary>The 40% de-merit slab (luxury cars/SUVs, aerated drinks, betting, etc.) — ordinary GST, <b>not</b> a cess.</summary>
    DeMerit,

    /// <summary>The retained 28%-plus-cess tobacco/pan-masala carve-out that did NOT move to 40% on 22-Sep-2025.</summary>
    CarveOut,

    /// <summary>A pre-GST-2.0 legacy rate (12% / 28%) kept inactive-by-date so a pre-22-Sep voucher reprints correctly.</summary>
    Legacy,
}

/// <summary>
/// The GST valuation basis of a rate-history row / stock item (Phase 9 slice 1; RQ-1; GST 2.0 RSP valuation). Most
/// supplies are valued on the §15 <see cref="TransactionValue"/>; certain tobacco/pan-masala carve-outs are valued on
/// the declared <see cref="RetailSalePrice"/>.
/// </summary>
public enum GstValuationBasis
{
    /// <summary>§15 transaction value (the default for every existing item/line — byte-identical when off, ER-13).</summary>
    TransactionValue,

    /// <summary>Retail Sale Price basis (RSP-valued tobacco/pan-masala carve-out).</summary>
    RetailSalePrice,
}

/// <summary>
/// How a Compensation-Cess amount is valued (Phase 9 slice 1; RQ-2/RQ-9). The GST 2.0 cess schedule mixes all three:
/// ad-valorem on the taxable value, a specific per-unit/quantity amount, and an RSP-factor per unit.
/// </summary>
public enum CessValuationMode
{
    /// <summary>Ad-valorem: cess = taxable value × cess-rate% (e.g. aerated waters 12%).</summary>
    AdValorem,

    /// <summary>Specific: cess = quantity × per-unit amount (e.g. coal ₹400/tonne, cigarettes per 1,000 sticks).</summary>
    Specific,

    /// <summary>RSP-factor: cess = quantity × declared RSP × factor (e.g. pan masala ~0.32R).</summary>
    RetailSalePriceFactor,
}

/// <summary>
/// The legal limb a reverse-charge (RCM) category is notified under (Phase 9 slice 2; RQ-3/RQ-7). Drives applicability:
/// <see cref="Section9_3"/> fires on a notified supply-nature (GTA/legal/director/…) regardless of supplier registration,
/// while <see cref="Section9_4"/> fires only for the real-estate-promoter recipient on a purchase from an unregistered
/// supplier (the blanket §9(4) was rescinded in 2019 — promoter-only survives). Stored as the enum ordinal
/// (Section9_3=0, Section9_4=1).
/// </summary>
public enum RcmStream
{
    /// <summary>§9(3) / §5(3) IGST — supplies notified for reverse charge (Notn 13/2017-CT(R) &amp; 10/2017-IGST(R)).</summary>
    Section9_3,

    /// <summary>§9(4) — inward supply from an unregistered supplier to a notified (promoter) recipient (Notn 7/2019-CT(R)).</summary>
    Section9_4,
}

/// <summary>
/// A party (supplier or recipient) qualifier a reverse-charge category matches on (Phase 9 slice 2; RQ-3). A category
/// carries a supplier qualifier and a recipient qualifier; RCM fires only when the actual parties satisfy both. Stored
/// as the enum ordinal.
/// </summary>
public enum RcmParty
{
    /// <summary>Any party qualifies (no restriction).</summary>
    Any,

    /// <summary>An unregistered supplier (no GSTIN) — e.g. §9(4) promoter purchases, or GTA-from-unregistered.</summary>
    Unregistered,

    /// <summary>A non-body-corporate supplier (e.g. an individual GTA / advocate whose RCM shifts to a body-corporate recipient).</summary>
    NonBodyCorporate,

    /// <summary>A body-corporate recipient (the recipient that must pay RCM on security/renting-of-motor-vehicle/GTA supplies).</summary>
    BodyCorporate,

    /// <summary>Any registered recipient (the default §9(3) recipient qualifier).</summary>
    RegisteredPerson,

    /// <summary>A real-estate promoter recipient (the sole surviving §9(4) trigger, Notn 7/2019).</summary>
    Promoter,
}

/// <summary>
/// Which GSTR-3B ITC bucket a reverse-charge input-tax line belongs to (Phase 9 slice 2; RQ-7/RQ-11). Carried on the RCM
/// <see cref="GstLineTax"/> so the returns bucket the ITC without recomputing (ER-9). Stored as the enum ordinal.
/// </summary>
public enum RcmItcScheme
{
    /// <summary>Import of services under §5(3)/§13 — the RCM ITC lands in GSTR-3B Table 4(A)(2).</summary>
    ImportOfServices,

    /// <summary>Every other reverse-charge inward supply — the RCM ITC lands in GSTR-3B Table 4(A)(3).</summary>
    OtherRcm,
}

/// <summary>
/// The kind of an RCM generated document (Phase 9 slice 2; RQ-8). A registered §9(3) supplier issues the tax invoice,
/// so no self-invoice is generated; an unregistered supplier triggers a Rule-47A <see cref="SelfInvoice"/>, and every
/// supplier payment may carry a Rule-52 <see cref="PaymentVoucher"/>. Stored as the enum ordinal (SelfInvoice=0,
/// PaymentVoucher=1).
/// </summary>
public enum RcmDocumentKind
{
    /// <summary>A Rule-47A self-invoice raised by the recipient for an unregistered-supplier RCM inward supply.</summary>
    SelfInvoice,

    /// <summary>A Rule-52 payment voucher raised at the time of the supplier payment.</summary>
    PaymentVoucher,
}

/// <summary>
/// A §34 note direction (Phase 9 slice 2; RQ-24). A <see cref="Credit"/> note reduces the supplier's output liability;
/// a <see cref="Debit"/> note increases it. Stored as the enum ordinal (Credit=0, Debit=1). (The CDN engine is built in
/// S2b; the enum + link table land in S2a so the v39 schema is complete at one version bump.)
/// </summary>
public enum CdnType
{
    /// <summary>A credit note — reduces the original supply's output tax (issuance capped by §34(2), the 30-Nov limit).</summary>
    Credit,

    /// <summary>A debit note — increases the original supply's output tax (uncapped issuance).</summary>
    Debit,
}

/// <summary>
/// The IRP lifecycle state of a per-voucher <see cref="EInvoiceRecord"/> (Phase 9 slice 4a; RQ-5; ER-5). Stored as the
/// enum ordinal. The IRN and the signed QR are only ever populated by the IRP response (never computed locally): a fresh
/// record is <see cref="Pending"/>, becomes <see cref="Generated"/> when the IRP returns the IRN/Ack/QR, and
/// <see cref="Cancelled"/> after a 24-h full-document cancel. <see cref="NotApplicable"/> covers a voucher outside the
/// covered set; <see cref="Failed"/> records an IRP rejection.
/// </summary>
public enum EInvoiceStatus
{
    /// <summary>The voucher is not an e-invoice candidate (excluded/exempt/not-applicable) — no request was built.</summary>
    NotApplicable = 0,

    /// <summary>An INV-01 request was assembled and staged/submitted; no IRN yet (the offline baseline stays here).</summary>
    Pending = 1,

    /// <summary>The IRP returned an IRN + Ack + signed QR — stored verbatim (ER-5); the document is e-invoiced.</summary>
    Generated = 2,

    /// <summary>The IRN was cancelled within 24 h (full document only); the document number is not reusable.</summary>
    Cancelled = 3,

    /// <summary>The IRP rejected the request (an error code + message was returned).</summary>
    Failed = 4,
}

/// <summary>
/// The typed e-invoicing exemptions a supplier's business class may carry (Phase 9 slice 4a; RQ-5; §2.5). A
/// <c>[Flags]</c> set stored as the ordinal on <see cref="GstConfig"/>. When any flag matches the supplier's class the
/// document is <see cref="EInvoiceCoverage.Exempt"/> regardless of turnover. <b>Note:</b> a SEZ <b>unit</b> is exempt
/// (<see cref="SezUnit"/>); a SEZ <b>developer</b> is NOT — the two must not be conflated.
/// </summary>
[Flags]
public enum EInvoiceExemptionClass
{
    /// <summary>No exemption (the default — an ordinary supplier is covered once over threshold).</summary>
    None = 0,

    /// <summary>An SEZ <b>unit</b> (exempt). A SEZ <b>developer</b> is not exempt and must NOT set this flag.</summary>
    SezUnit = 1,

    /// <summary>An insurer / banking company / NBFC (exempt).</summary>
    InsurerBankNbfc = 2,

    /// <summary>A goods-transport agency (GTA) (exempt).</summary>
    Gta = 4,

    /// <summary>A supplier of passenger-transport services (exempt).</summary>
    PassengerTransport = 8,

    /// <summary>A supplier of services by way of admission to a multiplex/cinema exhibition (exempt).</summary>
    MultiplexCinema = 16,

    /// <summary>A Government department / local authority (exempt).</summary>
    GovtOrLocalAuthority = 32,

    /// <summary>An OIDAR service provider (exempt).</summary>
    Oidar = 64,
}

/// <summary>
/// The GST-portal transport mode selected on the company config (Phase 9 slice 4a; RQ-30). Persisted selector; the
/// Desktop composition root resolves it to an <c>IGstPortalConnector</c>. <see cref="OfflineJson"/> is the
/// zero-credential default (writes/ingests INV-01 JSON); <see cref="CustomerNicDirect"/> is the optional live path using
/// the customer's OWN NIC creds (wired-but-deferred); <see cref="Gsp"/> is a future GSP integration (stubbed, not built
/// in Phase 9). Stored as the enum ordinal (OfflineJson=0, CustomerNicDirect=1, Gsp=2).
/// </summary>
public enum GstConnectorMode
{
    /// <summary>Offline JSON interchange — the zero-credential default (ER-16 baseline).</summary>
    OfflineJson = 0,

    /// <summary>The customer's own NIC-IRP API credentials (protected-at-rest; live path deferred).</summary>
    CustomerNicDirect = 1,

    /// <summary>A future GSP integration — stubbed (throws); NOT built in Phase 9.</summary>
    Gsp = 2,
}

/// <summary>
/// The e-invoice supply category of a covered outward document (Phase 9 slice 4a; RQ-18; §2.5) — drives the INV-01
/// <c>TranDtls.SupTyp</c> (B2B/EXPWP/EXPWOP/SEZWP/SEZWOP/DEXP) and the covered-set membership. Resolved from the party
/// GST block + voucher. Stored as the enum ordinal.
/// <para>
/// <b>Domain note:</b> the party GST block currently expresses only registered/B2C + place-of-supply, so S4a resolves
/// <see cref="Regular"/> (ordinary B2B), <see cref="Export"/> (overseas place of supply, GST code 96/97) and
/// <see cref="RcmSupplierLiable"/> (an outward reverse-charge supply). <see cref="SezWithPayment"/>,
/// <see cref="SezWithoutPayment"/> and <see cref="DeemedExport"/> are modelled here (and mapped by the INV-01 writer) so
/// no fidelity is lost when a later slice adds the party SEZ/deemed-export flag; the S4a resolver does not yet mint them.
/// </para>
/// </summary>
public enum EInvoiceSupplyCategory
{
    /// <summary>A regular B2B supply to a registered recipient (INV-01 <c>SupTyp = B2B</c>).</summary>
    Regular = 0,

    /// <summary>An export supply (INV-01 <c>SupTyp = EXPWP</c> when made on payment of IGST).</summary>
    Export = 1,

    /// <summary>A supply to an SEZ unit/developer WITH payment of IGST (INV-01 <c>SupTyp = SEZWP</c>).</summary>
    SezWithPayment = 2,

    /// <summary>A supply to an SEZ unit/developer WITHOUT payment (LUT) (INV-01 <c>SupTyp = SEZWOP</c>).</summary>
    SezWithoutPayment = 3,

    /// <summary>A deemed export (INV-01 <c>SupTyp = DEXP</c>).</summary>
    DeemedExport = 4,

    /// <summary>An outward supply on which the recipient pays under reverse charge (RegRev = Y).</summary>
    RcmSupplierLiable = 5,
}

/// <summary>
/// The e-invoice applicability verdict for a single voucher (Phase 9 slice 4a; RQ-18; §2.2). Advisory classification
/// returned by <c>EInvoiceService.CoverageOf</c>; not persisted (recomputed each time). <see cref="NotApplicable"/> when
/// e-invoicing is off / the company is Composition / below threshold / the voucher pre-dates applicability;
/// <see cref="Excluded"/> for a document class that never gets an IRN (B2C / Bill of Supply / ISD / import);
/// <see cref="Exempt"/> when a typed exemption class matches; <see cref="Covered"/> when an IRN must be generated.
/// </summary>
public enum EInvoiceCoverage
{
    /// <summary>The document must be reported to the IRP and carry an IRN.</summary>
    Covered,

    /// <summary>A document class that is never e-invoiced (B2C / Bill of Supply / ISD / import).</summary>
    Excluded,

    /// <summary>The supplier's business class is exempt from e-invoicing (regardless of turnover).</summary>
    Exempt,

    /// <summary>e-invoicing is not applicable to this company/voucher (off / composition / below threshold / pre-date).</summary>
    NotApplicable,
}

// ==================================================================================================================
//  e-Way Bill (Phase 9 slice 5; Rule 138; RQ-6/RQ-19; DP-12). The outbound-artefact sibling of the S4a e-Invoice —
//  it REUSES the GstConnectorMode + INicCredentialStore seams (no new secret surface) and adds a validity/
//  consignment-value engine. Every enum defaults to its 0-ordinal so an e-Way-off company is byte-identical (ER-13).
// ==================================================================================================================

/// <summary>
/// How the Rule-138 <b>consignment value</b> is composed (Phase 9 slice 5; §2.6). The threshold check <b>always</b>
/// includes CGST/SGST/IGST + Compensation-Cess; the basis only decides whether the <b>exempt-supply</b> portion is
/// added in. <see cref="Rule138Default"/> (the statutory default) is §15 taxable value + taxes + cess with the exempt
/// portion <b>excluded</b>. Stored as the enum ordinal (Rule138Default=0).
/// </summary>
public enum EWayConsignmentBasis
{
    /// <summary>§15 taxable value + CGST/SGST/IGST + cess, <b>excluding</b> the exempt-supply portion (the Rule-138 default).</summary>
    Rule138Default = 0,

    /// <summary>Adds the exempt/nil-supply line value on top of the taxable value + taxes + cess.</summary>
    TaxablePlusExempt = 1,

    /// <summary>The whole invoice value (all supply lines incl. exempt) + taxes + cess.</summary>
    InvoiceValue = 2,
}

/// <summary>
/// The e-Way transaction nature that drives the "mandatory irrespective of value" carve-out (Phase 9 slice 5; §2.6). An
/// inter-state principal↔job-worker (<see cref="JobWork"/>) or inter-state handicraft (<see cref="Handicraft"/>) movement
/// requires an EWB <b>regardless</b> of consignment value. Stored as the enum ordinal (Regular=0).
/// </summary>
public enum EWayTransactionType
{
    /// <summary>An ordinary supply — the threshold test applies.</summary>
    Regular = 0,

    /// <summary>Principal↔job-worker movement — mandatory irrespective of value when inter-state.</summary>
    JobWork = 1,

    /// <summary>Handicraft goods movement — mandatory irrespective of value when inter-state.</summary>
    Handicraft = 2,
}

/// <summary>
/// The transport mode for the EWB Part-B (Phase 9 slice 5; NIC transMode codes). <see cref="Ship"/> (and any multimodal
/// over-dimensional cargo) triggers the 20-km-per-day validity rule; the others use 200 km/day. Stored as the NIC code
/// (Road=1, Rail=2, Air=3, Ship=4).
/// </summary>
public enum EWayTransportMode
{
    /// <summary>Road (NIC transMode = 1).</summary>
    Road = 1,

    /// <summary>Rail (NIC transMode = 2).</summary>
    Rail = 2,

    /// <summary>Air (NIC transMode = 3).</summary>
    Air = 3,

    /// <summary>Ship / vessel (NIC transMode = 4) — multimodal-shipping / ODC uses the 20-km/day validity rule.</summary>
    Ship = 4,
}

/// <summary>
/// The EWB lifecycle state of a per-voucher <see cref="EWayBillRecord"/> (Phase 9 slice 5; ER-5 twin). Mirrors
/// <see cref="EInvoiceStatus"/> plus an <see cref="Expired"/> state the validity engine <b>derives</b> (the stored status
/// stays <see cref="Generated"/>; expiry is a view over <c>ValidUpto</c>). The 12-digit EWB number + validity are only
/// ever populated by the portal response (never computed locally, ER-5). Stored as the enum ordinal.
/// </summary>
public enum EWayStatus
{
    /// <summary>The voucher is not an e-Way candidate (below threshold / not a goods movement / pre-date / off).</summary>
    NotApplicable = 0,

    /// <summary>An EWB-01 request was assembled and staged/submitted; no EWB number yet (the offline baseline stays here).</summary>
    Pending = 1,

    /// <summary>The portal returned the 12-digit EWB number + validity — stored verbatim (ER-5); the movement is covered.</summary>
    Generated = 2,

    /// <summary>The EWB was cancelled within 24 h (full document only); the movement may be re-billed.</summary>
    Cancelled = 3,

    /// <summary>The portal rejected the request (an error code + message was returned).</summary>
    Failed = 4,

    /// <summary>A <b>derived</b> terminal view (not stored): the validity window has elapsed for a Generated EWB.</summary>
    Expired = 5,
}

/// <summary>
/// The Rule-138 applicability verdict for a voucher (Phase 9 slice 5; §2.6). Advisory classification returned by
/// <c>EWayBillService.CoverageOf</c>; not persisted. <see cref="NotApplicable"/> when e-Way is off / the voucher pre-dates
/// applicability / it is not a goods-movement document; <see cref="MandatoryIrrespectiveOfValue"/> for inter-state
/// job-work / handicraft (short-circuits the threshold); <see cref="NotRequired"/> when the consignment value is at or
/// below the effective threshold (or an intra-state movement in a state that exempts intra-state e-Way);
/// <see cref="Required"/> when the consignment value strictly exceeds the effective threshold.
/// </summary>
public enum EWayCoverage
{
    /// <summary>An EWB must be generated (consignment value strictly exceeds the effective threshold).</summary>
    Required,

    /// <summary>No EWB is required (value ≤ threshold, or an intra-state movement the state exempts).</summary>
    NotRequired,

    /// <summary>An EWB is mandatory regardless of value (inter-state job-work / handicraft).</summary>
    MandatoryIrrespectiveOfValue,

    /// <summary>e-Way is not applicable to this company/voucher (off / pre-date / not a goods movement).</summary>
    NotApplicable,
}

// ==================================================================================================================
//  GSTR-2A/2B inbound reconciliation + IMS + §17(5) (Phase 9 slice 6; §16(2)(aa); RQ-12..RQ-15, RQ-26; DP-13..DP-15,
//  DP-31). The FIRST inbound GST data path — imported external portal data, reconciled ADVISORY-only against the books
//  (ER-14: never posts ITC/a reversal). Every enum defaults to its 0-ordinal so a company that never imports 2B is
//  byte-identical (ER-13). S6a lands GstStatementType/Gstr2bDocType/ReconBucket (the import + reconcile spine); the
//  IMS + §17(5) enums (ImsStatus/ItcEligibility/BlockedCreditCategory) land now but their engines arrive in S6b.
// ==================================================================================================================

/// <summary>
/// Which portal statement a <see cref="Gstr2bSnapshot"/> holds (Phase 9 slice 6; DP-13). Both share ONE staging
/// table pair, discriminated by this. <see cref="Gstr2b"/> (the static monthly ITC gatekeeper) is the §16(2)(aa)
/// ITC-eligibility basis; <see cref="Gstr2a"/> (the dynamic view) is supplementary — importable and reconcilable but
/// never the statutory gate. Stored as the enum ordinal (Gstr2b=0, Gstr2a=1).
/// </summary>
public enum GstStatementType
{
    /// <summary>GSTR-2B — the static, monthly, ITC-eligibility statement (§16(2)(aa) basis).</summary>
    Gstr2b = 0,

    /// <summary>GSTR-2A — the dynamic, supplementary inward-supply statement (not the ITC gate).</summary>
    Gstr2a = 1,
}

/// <summary>
/// The document class of one imported <see cref="Gstr2bLine"/> (Phase 9 slice 6; the GSTN 2B <c>docdata</c> sections).
/// Stored as the enum ordinal; drives no computation (advisory grouping only). Amendments (B2BA/CDNRA) are kept as
/// distinct classes so a re-stated document is visible in the reconciliation.
/// </summary>
public enum Gstr2bDocType
{
    /// <summary>A regular B2B inward supply (2B <c>b2b</c> section).</summary>
    B2b = 0,

    /// <summary>An amendment to a B2B inward supply (2B <c>b2ba</c> section).</summary>
    B2bAmendment = 1,

    /// <summary>A supplier credit note reducing an inward supply (2B <c>cdnr</c>, note type C).</summary>
    CreditNote = 2,

    /// <summary>A supplier debit note increasing an inward supply (2B <c>cdnr</c>, note type D).</summary>
    DebitNote = 3,

    /// <summary>An amendment to a credit note (2B <c>cdnra</c>, note type C).</summary>
    CreditNoteAmendment = 4,

    /// <summary>An amendment to a debit note (2B <c>cdnra</c>, note type D).</summary>
    DebitNoteAmendment = 5,

    /// <summary>Import of goods from overseas (2B <c>impg</c>, from the Bill of Entry).</summary>
    ImportOfGoods = 6,

    /// <summary>Import of goods from an SEZ unit/developer (2B <c>impgsez</c>).</summary>
    ImportOfGoodsSez = 7,

    /// <summary>An Input-Service-Distributor credit (2B <c>isd</c>).</summary>
    Isd = 8,
}

/// <summary>
/// The reconciliation bucket a 2B line (or a booked purchase) falls into (Phase 9 slice 6; RQ-13; DP-13). The three
/// portal-side buckets are persisted on <c>gstr2b_recon</c> (keyed to the immutable <see cref="Gstr2bLine"/>);
/// <see cref="InBooksOnly"/> has no 2B line to key on and is a report-time derivation only (§6.4). Stored as the enum
/// ordinal (Matched=0, PartialMismatch=1, InPortalOnly=2, InBooksOnly=3).
/// </summary>
public enum ReconBucket
{
    /// <summary>The 2B line matched a booked purchase — keys equal AND value/tax within tolerance.</summary>
    Matched = 0,

    /// <summary>The keys matched (GSTIN + normalised doc-no + date window) but value/tax differ beyond tolerance.</summary>
    PartialMismatch = 1,

    /// <summary>The 2B line has no booked purchase — the portal has it, the books do not.</summary>
    InPortalOnly = 2,

    /// <summary>A booked purchase with no 2B line — the supplier has not filed ⇒ ITC ineligible (§16(2)(aa)). Report-only.</summary>
    InBooksOnly = 3,
}

/// <summary>
/// The offline mirror of a portal <b>IMS</b> (Invoice Management System) decision on a 2B line (Phase 9 slice 6; RQ-14;
/// DP-14). Kept as a separate mutable <c>ImsAction</c> record keyed to the immutable line (ER-6), never a line edit.
/// <see cref="NoAction"/> is <b>deemed-accepted</b> at 2B generation (a derived view, not a stored flip). Stored as the
/// enum ordinal (NoAction=0, Accepted=1, Rejected=2, Pending=3). <b>The IMS engine lands in S6b; the enum lands now.</b>
/// </summary>
public enum ImsStatus
{
    /// <summary>No action taken ⇒ deemed-accepted at 2B generation (the default; empty when unused, ER-13).</summary>
    NoAction = 0,

    /// <summary>The recipient accepted the record on the portal (mirrored here).</summary>
    Accepted = 1,

    /// <summary>The recipient rejected the record on the portal (mirrored here).</summary>
    Rejected = 2,

    /// <summary>The recipient kept the record pending one period (mirrored here).</summary>
    Pending = 3,
}

/// <summary>
/// The §17(5) ITC-eligibility of a purchase's item/expense ledger GST block (Phase 9 slice 6; RQ-26; DP-31) — a
/// per-purchase master flag distinct from the portal 2B "ITC-available" flag. Default <see cref="Eligible"/> ⇒
/// byte-identical when off (ER-13). Stored as the enum ordinal. <b>The ITC-gate logic lands in S6b; the enum + the
/// shared-block columns land now.</b>
/// </summary>
public enum ItcEligibility
{
    /// <summary>Fully eligible input tax credit (the default for every existing item/ledger).</summary>
    Eligible = 0,

    /// <summary>Blocked under §17(5) (the blocked-credit sub-clause is in <see cref="BlockedCreditCategory"/>).</summary>
    BlockedSection17_5 = 1,

    /// <summary>Ineligible for another reason (e.g. non-business / personal use, time-barred).</summary>
    Ineligible = 2,
}

/// <summary>
/// The §17(5) blocked-credit sub-clause a purchase falls under (Phase 9 slice 6; RQ-26; advisory grouping). Meaningful
/// only when <see cref="ItcEligibility.BlockedSection17_5"/>. Default <see cref="None"/> ⇒ byte-identical when off
/// (ER-13). Stored as the enum ordinal. <b>Used by the S6b ITC-gate; the enum lands now.</b>
/// </summary>
public enum BlockedCreditCategory
{
    /// <summary>No blocked-credit category (the default; consistent with <see cref="ItcEligibility.Eligible"/>).</summary>
    None = 0,

    /// <summary>§17(5)(a)/(aa) motor vehicles + vessels/aircraft.</summary>
    MotorVehicles = 1,

    /// <summary>§17(5)(b) food &amp; beverages, health services, club membership, travel benefits.</summary>
    FoodBeveragesHealth = 2,

    /// <summary>§17(5)(c)/(d) works-contract &amp; construction of immovable property (on own account).</summary>
    WorksContractConstruction = 3,

    /// <summary>§17(5)(b)(ii) membership of a club, health &amp; fitness centre.</summary>
    MembershipClubs = 4,

    /// <summary>§17(5)(g) goods/services for personal consumption.</summary>
    PersonalConsumption = 5,

    /// <summary>§17(5)(h) goods lost, stolen, destroyed, written off, or disposed of as gifts/free samples.</summary>
    GiftsFreeSamples = 6,

    /// <summary>§17(5)(h) goods lost/stolen/destroyed/written off.</summary>
    LostStolenWrittenOff = 7,

    /// <summary>Any other §17(5) blocked credit.</summary>
    Other = 8,
}

// ==================================================================================================================
//  Electronic ledgers + Rule-88A set-off + GST payment + ITC reversal (Phase 9 slice 7; RQ-20..RQ-22, RQ-27;
//  §49/§49A, Rule 88A, Rules 85/86/87, PMT-01/02/05/06, DRC-03; ER-7/ER-2/ER-3/ER-10/ER-13/ER-14). S7 is the FIRST
//  inbound-side slice that POSTS to the ledger. Every enum defaults to its 0-ordinal so a company that never sets
//  off / pays / reverses is byte-identical (ER-13). S7a lands ResidualIgstSplit/GstMinorHead/GstAdjustmentKind/
//  ItcReversalRule + the four v44 tables; the ITC-reversal engine (Rule 42/43/37/37A) is S7b.
// ==================================================================================================================

/// <summary>
/// How the residual IGST credit (the IGST credit left after fully discharging the IGST liability) is split across the
/// CGST and SGST/UTGST liabilities in the Rule-88A set-off (Phase 9 slice 7; RQ-21; DP-16). Rule 88A relaxed the old
/// rigid order, so the intra-CGST/SGST split of the residual IGST credit is <b>discretionary / editable</b> on the
/// portal (the IGST-first-and-fully-exhausted rule is hard-enforced; A14-CONFIRMED). The engine computes a compliant
/// default (<see cref="CgstFirst"/>) but MUST allow the user to override via the editable Table 6.1. Stored as the
/// enum ordinal (CgstFirst=0).
/// </summary>
public enum ResidualIgstSplit
{
    /// <summary>Pay the CGST liability from the residual IGST credit before the SGST liability (the default, DP-16).</summary>
    CgstFirst = 0,

    /// <summary>Pay the SGST/UTGST liability from the residual IGST credit before the CGST liability.</summary>
    SgstFirst = 1,

    /// <summary>Split the residual IGST credit across CGST + SGST liabilities in proportion to their outstanding amounts.</summary>
    Proportionate = 2,
}

/// <summary>
/// The <b>minor head</b> of the electronic cash ledger (PMT-05) — the second axis of its 2-D matrix MAJOR head
/// {IGST/CGST/SGST-UTGST/Cess} × MINOR head (Phase 9 slice 7; RQ-20; §49 + Rule 87; A14-CONFIRMED §11.2). Cash
/// deposited under one (major, minor) cell is drawable ONLY within that cell (cross-cell movement needs PMT-09,
/// out of scope). The credit ledger (PMT-02) can settle <b>only</b> the <see cref="Tax"/> minor head — interest /
/// penalty / fee / late-fee are cash-only (§49(4) / Rule 86(2)). Stored as the enum ordinal (Tax=0).
/// </summary>
public enum GstMinorHead
{
    /// <summary>Tax — the only minor head the credit ledger may discharge (§49(4)).</summary>
    Tax = 0,

    /// <summary>Interest (§50) — cash-only.</summary>
    Interest = 1,

    /// <summary>Penalty — cash-only.</summary>
    Penalty = 2,

    /// <summary>Fee / late fee — cash-only.</summary>
    Fee = 3,

    /// <summary>Other (any residual head) — cash-only.</summary>
    Other = 4,
}

/// <summary>
/// The kind of GST <b>adjustment</b> a posted set-off / cash-payment / reversal / reclaim line carries (Phase 9 slice 7;
/// RQ-21/RQ-22/RQ-27). Carried as a nullable discriminant on <see cref="GstLineTax"/> (default <c>null</c> ⇒ an
/// ordinary forward outward/ITC line — byte-identical to a v43 line, ER-13), mirroring how S2 added
/// <see cref="RcmItcScheme"/>. It lets the Table 6.1 (set-off) and Table 4(B) (reversal) projections classify the
/// posted adjustment lines <b>without</b> polluting the existing outward/ITC sums (the adjustment vouchers are
/// Journal/Payment base ⇒ excluded from <c>PostedGstVouchers</c> anyway). Stored as the enum ordinal (SetOff=0).
/// </summary>
public enum GstAdjustmentKind
{
    /// <summary>A Rule-88A credit-utilisation set-off line (Dr Output {head} / Cr Input {head}) — Table 6.1.</summary>
    SetOff = 0,

    /// <summary>A cash discharge of output tax from the electronic cash ledger (Dr Output {head} / Cr Cash Ledger) — Table 6.1.</summary>
    CashPayment = 1,

    /// <summary>A Rule 37 (180-day non-payment) ITC reversal (S7b) — Table 4(B)(2), reclaimable.</summary>
    ReversalRule37 = 2,

    /// <summary>A Rule 37A (supplier non-filing) ITC reversal (S7b) — Table 4(B)(2), reclaimable.</summary>
    ReversalRule37A = 3,

    /// <summary>A Rule 42 (inputs / input-services common-credit) ITC reversal (S7b) — Table 4(B)(1), non-reclaimable.</summary>
    ReversalRule42 = 4,

    /// <summary>A Rule 43 (capital-goods common-credit) ITC reversal (S7b) — Table 4(B)(1), non-reclaimable.</summary>
    ReversalRule43 = 5,

    /// <summary>A §17(5) blocked-credit ITC reversal (S7b) — Table 4(B)(1), non-reclaimable.</summary>
    ReversalSection17_5 = 6,

    /// <summary>An "ineligible ITC" reversal / disclosure (S7b) — Table 4(D) / 4(B)(1) if availed.</summary>
    ReversalIneligible = 7,

    /// <summary>An IMS-accepted / §34 credit-note ITC reversal (S7b) — Table 4(B).</summary>
    ReversalCreditNote = 8,

    /// <summary>A re-availment (reclaim) of an earlier Rule 37 / 37A reversal (S7b) — Table 4(D)(1).</summary>
    Reclaim = 9,
}

/// <summary>
/// The statutory ITC-reversal rule an <c>itc_reversals</c> row records (Phase 9 slice 7; RQ-27; DP-30/DP-31). The
/// reversal <b>engine</b> (which computes + posts each) arrives in S7b; the <c>itc_reversals</c> table + this enum land
/// in S7a (empty). Stored as the enum ordinal (Rule37=0).
/// </summary>
public enum ItcReversalRule
{
    /// <summary>Rule 37 — 180-day non-payment to the supplier (reclaimable on later payment; no time bar).</summary>
    Rule37 = 0,

    /// <summary>Rule 37A — supplier has not filed GSTR-3B by 30-Sep following the FY (reclaimable on supplier filing).</summary>
    Rule37A = 1,

    /// <summary>Rule 42 — inputs &amp; input-services common-credit apportionment (D1 + D2; non-reclaimable).</summary>
    Rule42 = 2,

    /// <summary>Rule 43 — capital-goods common-credit apportionment (60-month tranche; non-reclaimable).</summary>
    Rule43 = 3,

    /// <summary>§17(5) blocked credit reversed after being availed (non-reclaimable).</summary>
    Section17_5 = 4,

    /// <summary>Ineligible ITC (non-business / time-barred) reversed after being availed.</summary>
    Ineligible = 5,
}

/// <summary>
/// The GSTR-3B Table-4 bucket a posted ITC reversal / reclaim routes to (Phase 9 slice 7; RQ-27; A14-CONFIRMED §11.5,
/// GSTN advisory 01-Sep-2022). Persisted on <c>itc_reversals.table4b_bucket</c>. Stored as the enum ordinal
/// (Table4B1=0). The reversal engine (S7b) sets it; the table lands empty in S7a.
/// </summary>
public enum Table4bBucket
{
    /// <summary>Table 4(B)(1) — non-reclaimable reversals (Rules 38 / 42 / 43 + §17(5)).</summary>
    Table4B1 = 0,

    /// <summary>Table 4(B)(2) — reclaimable reversals (Rule 37 / 37A).</summary>
    Table4B2 = 1,

    /// <summary>Table 4(D)(1) — a reclaim of an earlier reversal (pulled into net ITC via 4(A)(5)).</summary>
    Table4D1 = 2,
}
