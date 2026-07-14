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
