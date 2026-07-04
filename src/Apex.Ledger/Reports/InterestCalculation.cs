using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One accrued-interest row in the Interest Calculation report (catalog §7): a principal held over an
/// accrual window at a rate, and the interest it produced. For an <see cref="InterestApplicability.Always"/>
/// ledger there is one row per interest-enabled ledger; for <see cref="InterestApplicability.PostDue"/>
/// there is one row per still-open bill (keyed by <see cref="BillReference"/>), so the same ledger can
/// contribute several rows.
/// </summary>
/// <remarks>
/// <see cref="Principal"/> is the balance interest accrued on (a magnitude), <see cref="Days"/> the accrual
/// day-count, <see cref="Basis"/> the day-count denominator the rate is divided by (360 / 365 / actual
/// calendar days), and <see cref="Interest"/> the rounded amount. <see cref="PrincipalIsDebit"/> records
/// which side the balance sat on so the UI can show a Dr/Cr interest column.
/// </remarks>
public sealed record InterestLine(
    Guid LedgerId,
    string LedgerName,
    Money Principal,
    bool PrincipalIsDebit,
    decimal RatePercent,
    InterestPer Per,
    DateOnly From,
    DateOnly To,
    int Days,
    int Basis,
    Money Interest,
    string? BillReference = null);

/// <summary>
/// The whole Interest Calculation result over a period (catalog §7): one <see cref="InterestLine"/> per
/// interest-bearing balance/bill, plus the total interest across them.
/// </summary>
public sealed record InterestReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<InterestLine> Lines)
{
    /// <summary>Σ interest across every line.</summary>
    public Money TotalInterest
    {
        get
        {
            var s = 0m;
            foreach (var l in Lines) s += l.Interest.Amount;
            return new Money(s);
        }
    }
}

/// <summary>
/// Pure interest projection over the posted voucher set (catalog §7; plan.md §5). No UI, no DB. For each
/// interest-enabled ledger it computes interest on the outstanding balance over <c>[from, to]</c> per the
/// ledger's <see cref="InterestParameters"/>:
/// <list type="bullet">
/// <item><b>Simple</b> = principal × rate% × days / basis, where <b>basis</b> is 360 (30-day month), 365
///   (365-day year), or the actual calendar days in the month/year the window falls in.</item>
/// <item><b>On Debit / Credit only</b> — accrues only while the closing balance sits on the requested side;
///   <b>All</b> accrues on whichever side it sits.</item>
/// <item><b>PostDue</b> — interest accrues only after each open bill's due date (catalog §5 due dates), one
///   row per open bill; <b>Always</b> accrues on the whole balance from the calculate-from / period start.</item>
/// <item><b>Compound</b> — the window is split into calendar-month sub-periods; each month's interest is
///   capitalised into the principal before the next month accrues.</item>
/// <item><b>Rounding</b> — the configured <see cref="InterestParameters.ApplyRounding"/> is applied to the
///   per-line result.</item>
/// </list>
/// </summary>
public static class InterestCalculation
{
    /// <summary>Builds the Interest Calculation report for the whole company over <c>[from, to]</c>.</summary>
    public static InterestReport Build(Company company, DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentException("The 'to' date must be ≥ the 'from' date.", nameof(to));

        var lines = new List<InterestLine>();
        foreach (var ledger in company.Ledgers)
        {
            if (!ledger.InterestEnabled) continue;
            lines.AddRange(LinesFor(company, ledger, from, to));
        }
        return new InterestReport(from, to, lines);
    }

    /// <summary>
    /// The interest line(s) for a single interest-enabled ledger over <c>[from, to]</c> — the building
    /// block the UI Interest report binds to. Returns an empty set when the ledger is not enabled, the
    /// balance is on the wrong side for the On-filter, or the accrual window is empty.
    /// </summary>
    public static IReadOnlyList<InterestLine> LinesFor(
        Company company, Domain.Ledger ledger, DateOnly from, DateOnly to)
    {
        var p = ledger.Interest;
        if (p is null || !p.Enabled) return Array.Empty<InterestLine>();

        return p.Applicability == InterestApplicability.PostDue
            ? PostDueLines(company, ledger, p, from, to)
            : AlwaysLines(company, ledger, p, from, to);
    }

    // ---------------------------------------------------------------- Always (whole-balance) accrual

    private static IReadOnlyList<InterestLine> AlwaysLines(
        Company company, Domain.Ledger ledger, InterestParameters p, DateOnly from, DateOnly to)
    {
        // The balance interest accrues on: the ledger closing balance carried into the window (as of the
        // window-start date). This is the principal held flat across the accrual window.
        var windowStart = EffectiveStart(from, p.CalculateFrom);
        if (windowStart > to) return Array.Empty<InterestLine>();

        var closing = LedgerBalances.Closing(company, ledger, windowStart);
        if (!SideAllowed(closing.Side, p.OnBalance)) return Array.Empty<InterestLine>();

        var principal = closing.Amount.Amount;
        if (principal <= 0m) return Array.Empty<InterestLine>();

        var (interest, days, basis) = Accrue(p, principal, windowStart, to);
        if (days <= 0) return Array.Empty<InterestLine>();

        return new[]
        {
            new InterestLine(
                ledger.Id, ledger.Name,
                new Money(principal), closing.Side == DrCr.Debit,
                p.RatePercent, p.Per, windowStart, to, days, basis,
                new Money(interest)),
        };
    }

    // ---------------------------------------------------------------- PostDue (per-bill) accrual

    private static IReadOnlyList<InterestLine> PostDueLines(
        Company company, Domain.Ledger ledger, InterestParameters p, DateOnly from, DateOnly to)
    {
        // PostDue accrues per open bill, only after its due date (catalog §5 bill-wise due dates). It needs
        // bill-wise data; a ledger without it produces no post-due interest.
        if (!ledger.MaintainBillByBill) return Array.Empty<InterestLine>();

        var result = new List<InterestLine>();
        foreach (var bill in Outstandings.OpenBillsFor(company, ledger, to))
        {
            // Interest starts the day after the due date; never before the period start / calculate-from.
            var accrualStart = EffectiveStart(Max(from, bill.DueDate.AddDays(1)), p.CalculateFrom);
            if (accrualStart > to) continue;

            var side = bill.Kind == OutstandingKind.Receivable ? DrCr.Debit : DrCr.Credit;
            if (!SideAllowed(side, p.OnBalance)) continue;

            var principal = bill.Pending.Amount;
            if (principal <= 0m) continue;

            var (interest, days, basis) = Accrue(p, principal, accrualStart, to);
            if (days <= 0) continue;

            result.Add(new InterestLine(
                ledger.Id, ledger.Name,
                new Money(principal), side == DrCr.Debit,
                p.RatePercent, p.Per, accrualStart, to, days, basis,
                new Money(interest),
                bill.Reference));
        }
        return result;
    }

    // ---------------------------------------------------------------- core accrual math

    /// <summary>
    /// Accrues interest on a principal held from <paramref name="start"/> to <paramref name="to"/>
    /// (inclusive of the days between), returning the rounded interest, the day-count, and the day-count
    /// basis. Simple applies the formula once over the window; Compound capitalises month-by-month.
    /// </summary>
    private static (decimal Interest, int Days, int Basis) Accrue(
        InterestParameters p, decimal principal, DateOnly start, DateOnly to)
    {
        var days = to.DayNumber - start.DayNumber;
        if (days <= 0) return (0m, days, BasisFor(p.Per, start, to));

        if (p.Style == InterestStyle.Simple)
        {
            var basis = BasisFor(p.Per, start, to);
            var raw = principal * (p.RatePercent / 100m) * days / basis;
            return (p.ApplyRounding(raw), days, basis);
        }

        // Compound: split into calendar-month sub-periods, capitalising each month's interest.
        var runningPrincipal = principal;
        var totalInterest = 0m;
        var cursor = start;
        while (cursor < to)
        {
            var monthEnd = FirstOfNextMonth(cursor);
            var segEnd = monthEnd < to ? monthEnd : to;
            var segDays = segEnd.DayNumber - cursor.DayNumber;
            var segBasis = BasisFor(p.Per, cursor, segEnd);
            var segInterest = runningPrincipal * (p.RatePercent / 100m) * segDays / segBasis;
            totalInterest += segInterest;
            runningPrincipal += segInterest; // capitalise
            cursor = segEnd;
        }

        // Report the overall basis for the whole window for the row's Basis column.
        return (p.ApplyRounding(totalInterest), days, BasisFor(p.Per, start, to));
    }

    /// <summary>
    /// The day-count basis the rate is divided by for a window: 360 for a 30-day month, 365 for a
    /// 365-day year, and the <b>actual</b> number of days in the calendar month/year for the calendar bases.
    /// For a window spanning several calendar months/years the actual-day bases use the days in the window's
    /// starting month/year (the accrual is short-period and anchored at its start).
    /// </summary>
    public static int BasisFor(InterestPer per, DateOnly start, DateOnly to) => per switch
    {
        InterestPer.ThirtyDayMonth => 360,
        InterestPer.ThreeSixtyFiveDayYear => 365,
        InterestPer.CalendarMonth => DateTime.DaysInMonth(start.Year, start.Month) * 12,
        InterestPer.CalendarYear => DaysInYear(start.Year),
        _ => 365,
    };

    private static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    /// <summary>True iff a balance on <paramref name="side"/> accrues under the On-filter.</summary>
    public static bool SideAllowed(DrCr side, InterestOnBalance on) => on switch
    {
        InterestOnBalance.All => true,
        InterestOnBalance.DebitOnly => side == DrCr.Debit,
        InterestOnBalance.CreditOnly => side == DrCr.Credit,
        _ => true,
    };

    private static DateOnly EffectiveStart(DateOnly windowStart, DateOnly? calculateFrom)
        => calculateFrom is { } cf && cf > windowStart ? cf : windowStart;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly FirstOfNextMonth(DateOnly d)
    {
        var year = d.Year;
        var month = d.Month + 1;
        if (month > 12) { month = 1; year++; }
        return new DateOnly(year, month, 1);
    }
}
