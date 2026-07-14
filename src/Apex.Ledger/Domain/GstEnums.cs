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
