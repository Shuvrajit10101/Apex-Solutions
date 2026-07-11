namespace Apex.Ledger.Domain;

/// <summary>
/// The TCS collection detail carried on an <see cref="EntryLine"/> when a sale to a collectee attracts income-tax
/// TCS under a §206C Nature of Goods (Phase 7 slice 5; mirrors <see cref="TdsLineTax"/> / <see cref="GstLineTax"/>).
/// TCS is <b>additive</b> (collected on top), the mirror of the GST tax line — where the <see cref="TdsLineTax"/>
/// carve-out <i>withholds</i>, this line <i>collects</i>. It self-describes the collection for the returns/exception
/// projections (Form 27EQ, TCS Outstanding): the <see cref="NatureId"/> + <see cref="CollectionCode"/> (the §206C
/// nature), the <see cref="AssessableValue"/> the TCS was assessed on (the base per the nature's
/// <see cref="NatureOfGoods.BaseIncludesGst"/> flag — GST-<b>inclusive</b> for every §206C row, CBDT Circular
/// 17/2020), the applied <see cref="RateBasisPoints"/>, the <see cref="TcsAmount"/> collected (nearest rupee,
/// round-half-up), the <see cref="CollecteeLedgerId"/> (the buyer party) and whether the collectee's PAN was applied
/// (<see cref="PanApplied"/>; false ⇒ the §206CC higher no-PAN rate was used).
/// <para>
/// The detail rides the <b>TCS Payable</b> credit line of a sale when TCS is collected, or the party leg when the
/// sale was assessed but fell <b>below threshold</b> (<see cref="TcsAmount"/> = 0). This gives the cumulative-FY
/// receipts projection (§206C(1H)) exactly one assessable contribution per transaction, like <c>Gstr1</c> reads
/// posted <see cref="GstLineTax"/>. A non-TCS line carries no <see cref="TcsLineTax"/>.
/// </para>
/// </summary>
/// <remarks>Immutable value object with no identity. Amounts are paisa-exact (ER-2); the collected amount is a whole
/// rupee (income-tax rounding). Framework- and DB-agnostic.</remarks>
public sealed class TcsLineTax
{
    /// <summary>The <see cref="NatureOfGoods"/> (§206C category) id the collection was computed under.</summary>
    public Guid NatureId { get; }

    /// <summary>The §206C Form-27EQ collection code (e.g. "6CE" scrap), denormalised for the return projections.</summary>
    public string CollectionCode { get; }

    /// <summary>The assessable value the TCS was computed on — GST-inclusive per the nature's base flag (paisa-exact).</summary>
    public Money AssessableValue { get; }

    /// <summary>The applied rate in basis points (100 = 1% with-PAN scrap; 500 = 5% §206CC no-PAN; 100 = 1% 1H no-PAN cap).</summary>
    public int RateBasisPoints { get; }

    /// <summary>The TCS collected, rounded to the nearest rupee (round-half-up); 0 when below threshold (paisa-exact).</summary>
    public Money TcsAmount { get; }

    /// <summary>The collectee (buyer party) ledger id — the key, with <see cref="NatureId"/>, for the cumulative projection.</summary>
    public Guid CollecteeLedgerId { get; }

    /// <summary>True iff the collectee's PAN was present and valid, so the with-PAN rate applied (else the §206CC rate).</summary>
    public bool PanApplied { get; }

    public TcsLineTax(
        Guid natureId, string collectionCode, Money assessableValue, int rateBasisPoints, Money tcsAmount,
        Guid collecteeLedgerId, bool panApplied)
    {
        if (string.IsNullOrWhiteSpace(collectionCode))
            throw new ArgumentException("TCS collection code is required.", nameof(collectionCode));
        if (rateBasisPoints < 0)
            throw new ArgumentException("TCS rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (assessableValue.Amount < 0m)
            throw new ArgumentException("Assessable value must be ≥ 0.", nameof(assessableValue));
        if (!assessableValue.IsPaisaExact)
            throw new InvalidOperationException($"Assessable value {assessableValue} must be paisa-exact.");
        if (tcsAmount.Amount < 0m)
            throw new ArgumentException("TCS amount must be ≥ 0.", nameof(tcsAmount));
        if (!tcsAmount.IsPaisaExact)
            throw new InvalidOperationException($"TCS amount {tcsAmount} must be paisa-exact.");

        NatureId = natureId;
        CollectionCode = collectionCode.Trim();
        AssessableValue = assessableValue;
        RateBasisPoints = rateBasisPoints;
        TcsAmount = tcsAmount;
        CollecteeLedgerId = collecteeLedgerId;
        PanApplied = panApplied;
    }
}
