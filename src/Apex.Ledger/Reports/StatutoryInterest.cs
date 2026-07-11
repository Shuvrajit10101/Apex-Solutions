using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// Pure income-tax interest arithmetic for the TDS/TCS exception reports (Phase 7 slice 8). No persistence, no
/// clock, no company state — just the three verified building blocks the interest/outstanding projections share:
/// the <b>statutory due date</b> for a deduction/collection, the <b>calendar months spanned</b> (part-month = full
/// month), and the <b>late months</b> (gated by the due date). All web-verified against §201(1A) / §206C(7)
/// (Phase-7 law notes, R7) and centralised here so R1/R3/R5/R7 compute interest identically.
/// </summary>
public static class StatutoryInterest
{
    /// <summary>
    /// The statutory deposit due date for a deduction/collection dated <paramref name="date"/> (non-government
    /// deductor): the <b>7th of the following month</b>, except a <b>March</b> deduction/collection which is due on
    /// <b>30-April</b> of the same year. Used ONLY to decide whether a row is late (see <see cref="LateMonths"/>);
    /// the interest itself still runs from the deduction/collection date.
    /// </summary>
    public static DateOnly StatutoryDueDate(DateOnly date)
    {
        if (date.Month == 3) return new DateOnly(date.Year, 4, 30); // March ⇒ 30-April (same year)
        var next = date.AddMonths(1);
        return new DateOnly(next.Year, next.Month, 7);              // 7th of the next month
    }

    /// <summary>
    /// The number of calendar months spanned from <paramref name="from"/> to <paramref name="to"/> inclusive:
    /// <c>(toY×12 + toM) − (fromY×12 + fromM) + 1</c>, floored at 1 when <paramref name="to"/> ≥
    /// <paramref name="from"/>, and 0 when <paramref name="to"/> is strictly before <paramref name="from"/>. Part of
    /// a month counts as a whole month (the income-tax "month or part thereof" rule). Verified: 30-Apr → 10-Jun = 3.
    /// </summary>
    public static int CalendarMonthsSpanned(DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        var months = (to.Year * 12 + to.Month) - (from.Year * 12 + from.Month) + 1;
        return months < 1 ? 1 : months;
    }

    /// <summary>
    /// The interest-bearing "late" months for a deduction/collection dated <paramref name="date"/> whose tax was
    /// deposited (or, if undeposited, whose exposure runs to) <paramref name="depositOrAsOf"/>: <b>0 when the
    /// deposit is on or before the statutory due date</b> (deposited on time — no interest), otherwise the full
    /// <see cref="CalendarMonthsSpanned"/> from the deduction/collection date to the deposit/as-of date.
    /// </summary>
    public static int LateMonths(DateOnly date, DateOnly depositOrAsOf)
    {
        if (depositOrAsOf <= StatutoryDueDate(date)) return 0; // deposited on time (or not yet past due) ⇒ no interest
        return CalendarMonthsSpanned(date, depositOrAsOf);
    }

    /// <summary>
    /// Simple interest on <paramref name="tax"/> at <paramref name="monthlyRateFraction"/> per month for
    /// <paramref name="months"/> months, rounded to the nearest rupee (round-half-up — the income-tax rounding rule,
    /// reusing <see cref="TdsService.NearestRupee"/>). Zero months ⇒ ₹0.
    /// </summary>
    public static Money LateInterest(Money tax, int months, decimal monthlyRateFraction)
    {
        if (months <= 0) return Money.Zero;
        return TdsService.NearestRupee(tax.Amount * monthlyRateFraction * months);
    }

    /// <summary>The §201(1A)(ii) TDS late-deposit rate: 1.5% per month or part thereof.</summary>
    public const decimal TdsLatePaymentMonthlyRate = 0.015m;

    /// <summary>The §206C(7) TCS late-deposit rate: 1% per month or part thereof.</summary>
    public const decimal TcsLatePaymentMonthlyRate = 0.01m;
}
