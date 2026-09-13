namespace Apex.Ledger.Domain;

/// <summary>
/// One row of the vendor's <b>Additional Accounting Entries</b> table on a <see cref="VoucherClass"/> (census 2.6;
/// schema v62) — an <b>additional-ledger rule</b>: freight, a per-unit duty, an invoice round-off and the like,
/// each with the basis its amount is computed from and the rounding applied to that amount.
/// </summary>
/// <remarks>
/// <para><b>R7 — ATTESTED, COLUMN BY COLUMN.</b> The vendor's TallyPrime page
/// <c>help.tallysolutions.com/tally-prime/importer-excise-masters/ei-configure-vch-class-customs-duty-tally/</c>
/// configures exactly this table, naming the <b>Ledger Name</b>, the <b>Type of Calculation</b> (there
/// <i>"Based on Quantity"</i>), the <b>Value Basis</b>, the <b>Rounding Method</b>, the <b>Rounding Limit</b> and
/// <b>Remove if Zero</b>. The rounding vocabulary is
/// <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c>; see
/// <see cref="VoucherClassRoundingMethod"/> and <see cref="VoucherClassCalculationType"/>, which carry the
/// per-option citations.</para>
///
/// <para>🔴 <b>APPORTIONMENT IS NOT A FIELD HERE, AND THE OMISSION IS DELIBERATE.</b> This product already
/// decides whether an expense ledger's amount is spread across the item lines to raise their landed stock rate:
/// <see cref="Ledger.MethodOfAppropriation"/> on the ledger master (Appropriate by Quantity / by Value), with a
/// group-level default behind it. That rule is a property of <b>the ledger</b> — freight either loads onto stock
/// or it does not, and it does so identically whichever voucher class happens to name it. Adding a second,
/// class-level apportionment switch would let the same ledger be an additional-cost ledger on one class and a
/// plain P&amp;L ledger on another, which is a shape no vendor page attests and a second source of truth for the
/// same question.</para>
///
/// <para>🔴 <b>SO IF THE TWO EVER DISAGREE, THE LEDGER WINS — and it wins because it is the only one of the two
/// that is asked.</b> The class answers <i>which</i> ledger and <i>how much</i>; the ledger master answers
/// <i>whether that amount loads onto stock and on what basis</i>. <see cref="Services.VoucherClassPosting"/>
/// computes the amount and stops there; the existing <c>AdditionalCostApportionment</c> then treats the resulting
/// leg exactly as it treats an operator-keyed one, reading the method off the ledger. There is no code path in
/// which a class overrides an appropriation method, so the disagreement cannot arise in the first place.</para>
/// </remarks>
public sealed class VoucherClassAdditionalEntry
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The vendor's <b>Ledger Name</b> — the account this rule posts to.</summary>
    public Guid LedgerId { get; set; }

    /// <summary>The vendor's <b>Type of Calculation</b> — how the amount is derived.</summary>
    public VoucherClassCalculationType CalculationType { get; set; }

    /// <summary>
    /// The vendor's <b>Value Basis</b>. Its meaning is set by <see cref="CalculationType"/>:
    /// <see cref="VoucherClassCalculationType.BasedOnQuantity"/> reads it as a rate PER BASE UNIT, and every other
    /// member ignores it (<see cref="VoucherClassCalculationType.AsTotalAmountRounding"/> derives its amount from
    /// the total and the rounding limit, not from a basis). Zero when unused.
    /// </summary>
    public Money ValueBasis { get; set; }

    /// <summary>The vendor's <b>Rounding Method</b> applied to the computed amount.</summary>
    public VoucherClassRoundingMethod RoundingMethod { get; set; }

    /// <summary>The vendor's <b>Rounding Limit</b> — the multiple the amount snaps to. Zero when
    /// <see cref="RoundingMethod"/> is <see cref="VoucherClassRoundingMethod.NotApplicable"/>, which is the
    /// vendor's own instruction to leave the field blank.</summary>
    public Money RoundingLimit { get; set; }

    /// <summary>The vendor's <b>Remove if Zero</b> — when set, a rule that computes to nil posts no leg at all
    /// rather than an explicit zero line on the voucher.</summary>
    public bool RemoveIfZero { get; set; }

    /// <summary>Creation order, so the entries list and post deterministically on every platform.</summary>
    public int Order { get; set; }

    public VoucherClassAdditionalEntry(
        Guid id,
        Guid ledgerId,
        VoucherClassCalculationType calculationType = VoucherClassCalculationType.NotApplicable,
        Money valueBasis = default,
        VoucherClassRoundingMethod roundingMethod = VoucherClassRoundingMethod.NotApplicable,
        Money roundingLimit = default,
        bool removeIfZero = false,
        int order = 0)
    {
        if (ledgerId == Guid.Empty)
            throw new ArgumentException("An additional accounting entry must name a ledger.", nameof(ledgerId));

        Id = id;
        LedgerId = ledgerId;
        CalculationType = calculationType;
        ValueBasis = valueBasis;
        RoundingMethod = roundingMethod;
        RoundingLimit = roundingLimit;
        RemoveIfZero = removeIfZero;
        Order = order;
    }
}
