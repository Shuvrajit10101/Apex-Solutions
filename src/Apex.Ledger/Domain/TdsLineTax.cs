namespace Apex.Ledger.Domain;

/// <summary>
/// The TDS withholding detail carried on an <see cref="EntryLine"/> when a payment/credit to a deductee is
/// subject to income-tax TDS (Phase 7 slice 2; mirrors <see cref="GstLineTax"/>). It self-describes the
/// withholding for the returns/exception projections (Form 26Q, TDS Outstanding/Not-Deducted): the
/// <see cref="NatureId"/> + <see cref="SectionCode"/> (the §194x nature), the <see cref="AssessableValue"/> the
/// TDS was assessed on (the <b>GST-exclusive</b> base, CBDT Circular 23/2017), the applied
/// <see cref="RateBasisPoints"/>, the <see cref="TdsAmount"/> withheld (nearest rupee, round-half-up), the
/// <see cref="DeducteeLedgerId"/> (the party) and whether the deductee's PAN was applied
/// (<see cref="PanApplied"/>; false ⇒ the §206AA/§194Q no-PAN rate was used).
/// <para>
/// The detail rides <b>one</b> line per (voucher, party, nature): the <b>TDS Payable</b> credit line when TDS is
/// withheld, or the party leg when the payment was assessed but fell <b>below threshold</b> (<see cref="TdsAmount"/>
/// = 0). This gives the cumulative-FY threshold projection exactly one assessable contribution per transaction,
/// like <c>Gstr1</c> reads posted <see cref="GstLineTax"/>. A non-TDS line carries no <see cref="TdsLineTax"/>.
/// </para>
/// </summary>
/// <remarks>Immutable value object with no identity. Amounts are paisa-exact (ER-2); the withheld amount is a whole
/// rupee (income-tax rounding). Framework- and DB-agnostic.</remarks>
public sealed class TdsLineTax
{
    /// <summary>The <see cref="NatureOfPayment"/> (TDS section) id the withholding was computed under.</summary>
    public Guid NatureId { get; }

    /// <summary>The income-tax section code (e.g. "194J(b)"), denormalised for the return projections.</summary>
    public string SectionCode { get; }

    /// <summary>The assessable value the TDS was computed on — the <b>GST-exclusive</b> base (paisa-exact).</summary>
    public Money AssessableValue { get; }

    /// <summary>The applied rate in basis points (1000 = 10% with-PAN; 2000 = 20% §206AA; 500 = 5% §194Q no-PAN).</summary>
    public int RateBasisPoints { get; }

    /// <summary>The TDS withheld, rounded to the nearest rupee (round-half-up); 0 when below threshold (paisa-exact).</summary>
    public Money TdsAmount { get; }

    /// <summary>The deductee (party) ledger id — the key, with <see cref="NatureId"/>, for the cumulative projection.</summary>
    public Guid DeducteeLedgerId { get; }

    /// <summary>True iff the deductee's PAN was present and valid, so the with-PAN rate applied (else the no-PAN rate).</summary>
    public bool PanApplied { get; }

    public TdsLineTax(
        Guid natureId, string sectionCode, Money assessableValue, int rateBasisPoints, Money tdsAmount,
        Guid deducteeLedgerId, bool panApplied)
    {
        if (string.IsNullOrWhiteSpace(sectionCode))
            throw new ArgumentException("TDS section code is required.", nameof(sectionCode));
        if (rateBasisPoints < 0)
            throw new ArgumentException("TDS rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (assessableValue.Amount < 0m)
            throw new ArgumentException("Assessable value must be ≥ 0.", nameof(assessableValue));
        if (!assessableValue.IsPaisaExact)
            throw new InvalidOperationException($"Assessable value {assessableValue} must be paisa-exact.");
        if (tdsAmount.Amount < 0m)
            throw new ArgumentException("TDS amount must be ≥ 0.", nameof(tdsAmount));
        if (!tdsAmount.IsPaisaExact)
            throw new InvalidOperationException($"TDS amount {tdsAmount} must be paisa-exact.");

        NatureId = natureId;
        SectionCode = sectionCode.Trim();
        AssessableValue = assessableValue;
        RateBasisPoints = rateBasisPoints;
        TdsAmount = tdsAmount;
        DeducteeLedgerId = deducteeLedgerId;
        PanApplied = panApplied;
    }
}
