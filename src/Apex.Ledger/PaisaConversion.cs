using System;

namespace Apex.Ledger;

/// <summary>
/// <b>The ONE home for rupees → integer paisa</b> (drift lock D3), exposing the two semantics this app
/// genuinely needs under names that say which is which.
///
/// <para><b>The divergence this replaces.</b> Eight private copies of <c>rupees × 100</c> existed, splitting
/// into two <i>incompatible</i> behaviours with nothing in their names to tell them apart. A sub-paisa amount
/// <b>threw</b> at the persist/export boundary (<c>Apex.Ledger.Io.MoneyCodec.ToPaisa</c>,
/// <c>Apex.Persistence.Sqlite.Paisa.FromDecimal</c>) and was <b>silently rounded</b> on six report/service
/// paths (<c>ItcGateView</c>, <c>Gstr2bReconciler</c>, <c>GstReversalService</c>, <c>EWayBillService</c>,
/// <c>ItcSetOffReportViewModel</c>, <c>RunSetOffViewModel</c>). The same value was fatal in one place and
/// quietly rounded in another, and a reader of any single call site could not tell which they had.</para>
///
/// <para><b>Both semantics are kept, because both are correct in their own place</b> — this is deliberately
/// NOT collapsed to one method:</para>
/// <list type="bullet">
/// <item><see cref="ToPaisaExact(Money)"/> — the <b>persist / export boundary</b>. Paisa is the canonical
/// stored and wire form, so a sub-paisa amount reaching it means the value would be silently altered on a
/// round-trip; losing precision in the system of record is unacceptable, so it throws. Every
/// <see cref="Money"/> the domain persists is already paisa-exact (<see cref="Money.RoundToPaisa"/>), so this
/// throw is an invariant check, not an expected path.</item>
/// <item><see cref="ToPaisaRounded(Money)"/> — <b>derived report and set-off arithmetic</b>. These paths
/// compute intermediate values (shares, apportionments, tolerance comparisons) that are legitimately sub-paisa
/// before they are quantised, and they must produce a number rather than abort a report. Rounding is
/// away-from-zero, matching every replaced copy and <see cref="Money.RoundToPaisa"/>.</item>
/// </list>
///
/// <para><b>Why the split is preserved rather than unified.</b> Making the report paths throw would turn
/// ordinary sub-paisa intermediates into crashes in GSTR-2B reconciliation and ITC set-off; making the
/// persistence paths round would silently corrupt stored money. Neither behaviour is safe in the other's
/// position, so the honest fix is two methods whose names carry the difference — not one shared method and not
/// eight anonymous copies.</para>
/// </summary>
public static class PaisaConversion
{
    /// <summary>
    /// Rupees → integer paisa, <b>exact</b>. Throws <see cref="InvalidOperationException"/> when
    /// <paramref name="rupees"/> carries more than two decimal places, because the caller is about to persist or
    /// serialise the value and truncation there is silent data loss.
    /// </summary>
    public static long ToPaisaExact(decimal rupees)
    {
        var scaled = rupees * 100m;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Amount {rupees} is not paisa-exact (more than 2 decimal places); cannot persist or serialise without loss.");
        return (long)truncated;
    }

    /// <summary>Rupees → integer paisa, exact; throws on a sub-paisa amount. See <see cref="ToPaisaExact(decimal)"/>.</summary>
    public static long ToPaisaExact(Money money) => ToPaisaExact(money.Amount);

    /// <summary>
    /// Rupees → integer paisa, <b>rounding</b> a sub-paisa amount to the nearest paisa, halves away from zero.
    /// For derived report / set-off arithmetic, where a sub-paisa intermediate is expected and the computation
    /// must yield a number rather than abort.
    /// </summary>
    public static long ToPaisaRounded(decimal rupees) =>
        (long)Math.Round(rupees * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Rupees → integer paisa, rounding a sub-paisa amount. See <see cref="ToPaisaRounded(decimal)"/>.</summary>
    public static long ToPaisaRounded(Money money) => ToPaisaRounded(money.Amount);

    /// <summary>
    /// True iff <paramref name="rupees"/> is exactly representable in whole paisa (at most two decimal places).
    /// The ONE sub-paisa test: <see cref="Money.IsPaisaExact"/> and the typed-amount parsers on the IMS, ITC-
    /// reversal and set-off screens each re-implemented this same comparison.
    /// </summary>
    public static bool IsPaisaExact(decimal rupees)
    {
        var scaled = rupees * 100m;
        return scaled == decimal.Truncate(scaled);
    }

    /// <summary>
    /// Non-throwing <see cref="ToPaisaExact(decimal)"/> for parsing user-typed amounts: returns false and leaves
    /// <paramref name="paisa"/> zero when the value is sub-paisa, so the screen can reject the input rather than
    /// surface an exception.
    /// </summary>
    public static bool TryToPaisaExact(decimal rupees, out long paisa)
    {
        if (!IsPaisaExact(rupees)) { paisa = 0; return false; }
        paisa = (long)decimal.Truncate(rupees * 100m);
        return true;
    }

    /// <summary>
    /// The largest rupee amount the INTEGER-paisa store can carry: <c>long.MaxValue</c> paisa, i.e.
    /// ₹92,23,37,20,36,85,47,758.07. It lives here, beside the conversion whose narrowing cast defines it, because
    /// it IS part of the rupees→paisa rule (drift lock D3) — a screen that re-derived its own ceiling would drift
    /// from the conversion the moment either changed.
    /// </summary>
    public static readonly decimal MaxStorableRupees = long.MaxValue / 100m;

    /// <summary>
    /// True iff <paramref name="rupees"/> can be persisted as INTEGER paisa at all: within
    /// <see cref="MaxStorableRupees"/> <b>and</b> paisa-exact.
    ///
    /// <para><b>The magnitude test comes FIRST, and must stay first.</b> <see cref="IsPaisaExact"/> itself scales
    /// by a hundred, which overflows <c>decimal</c> past ~7.9e26, and <see cref="ToPaisaExact(decimal)"/> then
    /// narrows to <c>long</c>, which overflows past 17 rupee digits. Both raise <see cref="OverflowException"/> —
    /// an <see cref="ArithmeticException"/> that no domain-refusal filter in the app treats as a refusal — so a
    /// predicate that tested exactness first would THROW on the very input it exists to reject. This is the same
    /// branch order <c>GstConfigViewModel.TryStatutoryRupees</c> earned.</para>
    ///
    /// <para><see cref="Math.Abs(decimal)"/> is deliberately not used: it throws on
    /// <see cref="decimal.MinValue"/>, which is precisely the extreme this predicate must answer for.</para>
    /// </summary>
    public static bool FitsPaisaStore(decimal rupees) =>
        rupees >= -MaxStorableRupees && rupees <= MaxStorableRupees && IsPaisaExact(rupees);

    /// <summary>Integer paisa → rupees, exact (paisa ÷ 100). Shared by both semantics — the inverse never loses.</summary>
    public static decimal ToRupees(long paisa) => paisa / 100m;

    /// <summary>Integer paisa → <see cref="Money"/>, exact.</summary>
    public static Money ToMoney(long paisa) => new(ToRupees(paisa));
}
