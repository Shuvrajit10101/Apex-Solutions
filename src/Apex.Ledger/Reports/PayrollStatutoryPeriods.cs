using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One month of a statutory reporting period — its first and last day. The PF and ESI statutory forms are all
/// month-walks over a longer window (a PF <b>currency period</b> or an ESI <b>contribution period</b>), so the
/// walk is written once, here, rather than three times in three projections.
/// </summary>
public readonly record struct StatutoryMonth(DateOnly From, DateOnly To);

/// <summary>
/// The statutory reporting <b>periods</b> the PF and ESI forms are drawn over (census rows 7.20 / 7.21). Pure,
/// deterministic, culture-invariant date arithmetic — no clock, no company state.
///
/// <para><b>PF currency period — 1 March to 28/29 February.</b> Per the reference product's Form 3A page
/// (help.tallysolutions.com/docs/te9rel66/Payroll/Form_3A.htm, "Contribution card for currency period from"),
/// the period is <i>"1st March of the current year to 28th or 29th February of next year"</i>. It is therefore
/// <b>NOT</b> the April–March financial year, and a form built over the financial year would be a different
/// twelve months from the one the form asks for.</para>
///
/// <para><b>ESI contribution period — April–September and October–March.</b> These already exist in the engine as
/// <see cref="EsiContribution.ContributionPeriodStart"/> / <see cref="EsiContribution.ContributionPeriodEnd"/>
/// (Phase 8 slice 5, ESI (General) Regulations 1950 Reg. 4). They are re-exposed here — never re-derived — so the
/// statutory forms and the ESI coverage decision can never drift onto two different definitions of the same
/// period.</para>
/// </summary>
public static class PayrollStatutoryPeriods
{
    /// <summary>The calendar month a PF currency period starts in (March).</summary>
    public const int CurrencyPeriodStartMonth = 3;

    /// <summary>
    /// The first day (1 March) of the PF currency period that contains <paramref name="date"/>. March–December
    /// belong to the period that started in March of the same year; <b>January and February belong to the period
    /// that started in March of the PREVIOUS year</b> — which is the whole reason this is not the financial year.
    /// </summary>
    public static DateOnly CurrencyPeriodStart(DateOnly date)
        => date.Month >= CurrencyPeriodStartMonth
            ? new DateOnly(date.Year, CurrencyPeriodStartMonth, 1)
            : new DateOnly(date.Year - 1, CurrencyPeriodStartMonth, 1);

    /// <summary>The last day (28 or 29 February) of the PF currency period beginning on
    /// <paramref name="currencyPeriodStart"/>. Leap years fall out of the date arithmetic; no day count is
    /// hardcoded.</summary>
    public static DateOnly CurrencyPeriodEnd(DateOnly currencyPeriodStart)
        => currencyPeriodStart.AddYears(1).AddDays(-1);

    /// <summary>The twelve <see cref="StatutoryMonth"/>s of the PF currency period beginning on
    /// <paramref name="currencyPeriodStart"/>, in order (March … February).</summary>
    public static IReadOnlyList<StatutoryMonth> CurrencyPeriodMonths(DateOnly currencyPeriodStart)
        => Months(currencyPeriodStart, CurrencyPeriodEnd(currencyPeriodStart));

    /// <summary>The first day (1 April or 1 October) of the ESI contribution period containing
    /// <paramref name="date"/>. Delegates to the shipped coverage engine so the two can never disagree.</summary>
    public static DateOnly EsiContributionPeriodStart(DateOnly date)
        => EsiContribution.ContributionPeriodStart(date);

    /// <summary>The last day (30 September or 31 March) of the ESI contribution period containing
    /// <paramref name="date"/>. Delegates to the shipped coverage engine.</summary>
    public static DateOnly EsiContributionPeriodEnd(DateOnly date)
        => EsiContribution.ContributionPeriodEnd(date);

    /// <summary>The six <see cref="StatutoryMonth"/>s of the ESI contribution period containing
    /// <paramref name="date"/>, in order.</summary>
    public static IReadOnlyList<StatutoryMonth> EsiContributionPeriodMonths(DateOnly date)
        => Months(EsiContributionPeriodStart(date), EsiContributionPeriodEnd(date));

    /// <summary>
    /// The whole calendar months spanned by <c>[from, to]</c>, in order — each as its own first/last day.
    /// <paramref name="from"/> is normalised to the first of its month, so a window that starts mid-month still
    /// yields whole wage months (the statutory forms are month-columned; a part month is not one of their
    /// columns). Returns an empty list when <paramref name="to"/> precedes <paramref name="from"/>.
    /// </summary>
    public static IReadOnlyList<StatutoryMonth> Months(DateOnly from, DateOnly to)
    {
        var months = new List<StatutoryMonth>();
        if (to < from) return months;

        var cursor = new DateOnly(from.Year, from.Month, 1);
        while (cursor <= to)
        {
            months.Add(new StatutoryMonth(cursor, cursor.AddMonths(1).AddDays(-1)));
            cursor = cursor.AddMonths(1);
        }
        return months;
    }

    /// <summary>Whether <paramref name="date"/> falls inside <c>[from, to]</c> (inclusive). Null never falls
    /// inside — an unrecorded date is not an event in the month.</summary>
    public static bool FallsWithin(DateOnly? date, DateOnly from, DateOnly to)
        => date is { } d && d >= from && d <= to;
}
