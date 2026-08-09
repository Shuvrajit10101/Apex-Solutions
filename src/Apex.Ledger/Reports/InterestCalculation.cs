using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One accrual <b>segment</b> inside an <see cref="InterestLine"/> (Phase 10.10 · WF-4/WF-5): a stretch
/// <c>[From, To)</c> over which the ledger balance, the day-count basis and the accruing side were all
/// constant. A line is the sum of its segments; the segments are the audit trail that makes a
/// multi-movement or multi-month row re-derivable.
/// </summary>
/// <remarks>
/// ⚠ <see cref="SignedPrincipal"/> means <b>different things on the two accrual paths</b>, and a consumer
/// must not read one contract into the other:
/// <list type="bullet">
/// <item><b><see cref="InterestApplicability.Always"/></b> — the ledger's <b>signed</b> balance in force at
///   <see cref="From"/> (Dr positive, Cr negative), by construction equal to
///   <see cref="LedgerBalances.SignedClosing(Company, Domain.Ledger, DateOnly)"/> at that date.
///   <c>InterestTests.Segment_principals_equal_SignedClosing_at_every_boundary</c> is the standing proof.</item>
/// <item><b><see cref="InterestApplicability.PostDue"/></b> — the <b>bill's</b> pending amount, signed by the
///   bill's side (receivable positive, payable negative). It is <b>not</b> the ledger balance and generally
///   does not equal <c>SignedClosing</c>: a party with several open bills emits one row per bill, each
///   carrying only its own outstanding, and a payable bill carries a negative value while the ledger's
///   signed closing may be positive. <c>InterestTests.PostDue_segments_carry_the_bill_pending_not_the_
///   ledger_balance</c> pins the divergence.</item>
/// </list>
/// <see cref="AccrualPrincipal"/> is the magnitude interest actually accrued on — the same number for
/// simple interest, and the balance <b>plus capitalised interest</b> for compound.
/// <see cref="Interest"/> is the segment's <b>unrounded</b> contribution; the configured rounding is
/// applied once, to the line total, never per segment.
/// </remarks>
public sealed record InterestSegment(
    DateOnly From,
    DateOnly To,
    decimal SignedPrincipal,
    Money AccrualPrincipal,
    int Days,
    int Basis,
    Money Interest);

/// <summary>
/// One accrued-interest row in the Interest Calculation report (catalog §7): a principal held over an
/// accrual window at a rate, and the interest it produced. For an <see cref="InterestApplicability.Always"/>
/// ledger there is one row per <b>contiguous same-side accruing run</b> of the running balance — normally
/// one row per interest-enabled ledger, but a window in which the balance flips Dr↔Cr (or stops accruing
/// and later resumes) yields one row per run, because Dr interest (receivable) and Cr interest (payable)
/// are different money and must never net. For <see cref="InterestApplicability.PostDue"/> there is one row
/// per still-open bill (keyed by <see cref="BillReference"/>).
/// </summary>
/// <remarks>
/// <see cref="Principal"/> is the <b>time-weighted average</b> of the principal interest accrued on
/// (a magnitude): <c>Σ(principalᵢ × daysᵢ) / Days</c> — <b>not</b> a balance the ledger ever held, and for
/// a compound row not even bounded by one, because it averages the <b>capitalised</b> principal. It is
/// defined that way so that on a <b>single-basis</b> row the identity the printed grid implies —
/// <c>Principal × Rate% × Days / Basis == Interest</c> — holds exactly and an auditor can re-derive the row
/// from the columns in front of them. <see cref="Days"/> is the accrual day-count (<c>To − From</c>, a
/// half-open count: a segment <c>[a, b)</c> is charged <c>b − a</c> days) and <see cref="Basis"/> the
/// day-count denominator the rate is divided by.
/// <para>
/// ⚠ On a <b>MULTI-BASIS</b> row that identity <b>cannot</b> hold and the row does not foot. A row spanning
/// a Calendar-Month or Calendar-Year boundary accrues under two or more divisors; <see cref="Basis"/> is a
/// single <see cref="int"/>, so it reports the <b>first</b> segment's basis and is <b>indicative only</b>.
/// The gap is real money — a 01-Dec-2023 → 31-Jan-2025 Calendar-Year row on ₹1,00,000.77 at 12% prints
/// ₹14,005.59 while its own columns re-derive to ₹14,038.46 — and it is pinned to the paisa by
/// <c>InterestTests.Calendar_year_basis_follows_the_year_each_segment_falls_in</c>. The exact arithmetic is
/// in <see cref="Segments"/>, which always foots to <see cref="Interest"/> before rounding; a grid that
/// wants a self-consistent row must drill into them rather than divide by <see cref="Basis"/>.
/// </para>
/// <see cref="PrincipalIsDebit"/> records which side the balance sat on so the UI can show a Dr/Cr
/// interest column.
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
    string? BillReference = null,
    IReadOnlyList<InterestSegment>? Segments = null)
{
    /// <summary>The accrual segments this row is the sum of. Never null; empty only for a hand-built row.</summary>
    public IReadOnlyList<InterestSegment> Segments { get; init; } =
        Segments ?? Array.Empty<InterestSegment>();
}

/// <summary>
/// The whole Interest Calculation result over a period (catalog §7): one <see cref="InterestLine"/> per
/// interest-bearing balance/bill, plus the total interest across them.
/// </summary>
public sealed record InterestReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<InterestLine> Lines)
{
    /// <summary>
    /// Σ interest across every line, as a sum of <b>magnitudes</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ This is a report footer, <b>not a net position</b>. Receivable (Dr) and payable (Cr) interest are
    /// different money: they are never subtracted from one another here, and they are never merged into a
    /// single row either (<see cref="InterestLine"/> is emitted per contiguous same-side run). A caller that
    /// wants either side on its own filters <see cref="InterestLine.PrincipalIsDebit"/> over
    /// <see cref="Lines"/>. Pinned by
    /// <c>InterestTests.Rounding_is_applied_once_per_printed_row_and_the_total_never_nets_the_two_sides</c>.
    /// </remarks>
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
/// Pure interest projection over the posted voucher set (catalog §7; plan.md §5, Phase 10.10 · WF-4/WF-5).
/// No UI, no DB. For each interest-enabled ledger it computes interest per the ledger's
/// <see cref="InterestParameters"/>:
/// <list type="bullet">
/// <item><b>Always</b> — accrues on the <b>running balance</b>: the window is cut at every dated movement
///   inside it (and at every calendar-period boundary the "Per" style needs), each segment accrues on the
///   balance actually in force, and the row is the sum. A bill raised inside the window therefore starts
///   earning the day after it is raised, and a repayment stops the interest on the money repaid.</item>
/// <item><b>PostDue</b> — interest accrues only after each open bill's due date (catalog §5 due dates), one
///   row per open bill, on the bill's pending amount.</item>
/// <item><b>On Debit / Credit only</b> — evaluated <b>per segment</b>: a ledger sitting on the disallowed
///   side for part of the window simply contributes nothing for that part, instead of suppressing the
///   whole row. <b>All</b> accrues on whichever side the balance sits.</item>
/// <item><b>Simple</b> = Σ over segments of <c>principal × rate% × days / basis</c>;
///   <b>Compound</b> is the same walk with each calendar month's interest capitalised into the principal
///   before the next month accrues.</item>
/// <item><b>Rounding</b> — the configured <see cref="InterestParameters.ApplyRounding"/> is applied
///   <b>once per printed row</b>, to that row's total. Never per segment: per-segment rounding over a long
///   window is exactly the kind of accumulated-paisa drift this engine must not have. Note the unit is the
///   ROW, not the ledger — a window that flips Dr↔Cr emits several rows and each rounds on its own, because
///   a receivable and a payable are different money and must not be pooled before rounding. Under a
///   directional method that is visible: two rows ceiling to ₹66 + ₹188 where the pooled ₹252.30 would
///   ceil to ₹253. Pinned by
///   <c>InterestTests.Rounding_is_applied_once_per_printed_row_and_the_total_never_nets_the_two_sides</c>.</item>
/// </list>
/// <para>
/// <b>Day convention (pinned).</b> A segment <c>[a, b)</c> is charged <c>b − a</c> days — half-open. A
/// movement dated <c>d</c> therefore creates a boundary <b>at</b> <c>d</c>, and the money it brings first
/// earns on <c>d+1</c> counted inclusively, which is TallyPrime's "Always — calculate interest from next
/// day of transaction" [CORPUS-BOOK printed p.118].
/// </para>
/// </summary>
public static class InterestCalculation
{
    // ================================================================================================
    // ⚠⚠ THE DIVISOR VALUES BELOW ARE NOT THIS SLICE'S TO CHANGE. ⚠⚠
    //
    // plan.md Phase 10.10 splits the interest work in two. THIS slice is S1 = WF-4 + WF-5: accrue on the
    // running balance, and resolve the divisor from each accrual SEGMENT's own start date instead of
    // anchoring the whole window on its first month. Both are measurement-independent.
    //
    // The divisor TABLE is WF-6 = slice S3, and plan.md gates it verbatim:
    //     "S3 — Interest divisor table (WF-6) — DO NOT MERGE THE CONSTANTS UNTIL T8 LANDS."
    // T8 is a live TallyPrime run — ₹44,000 at 10%, Per = 30-Day Month, 30-day window. ₹4,400 means the
    // rate is quoted PER PERIOD (divisors 30 / 365 / DaysInMonth / DaysInYear); ≈₹366.67 means PER ANNUM.
    //
    // So `BasisFor` keeps the values the engine already shipped, to the paisa. In particular the
    // `DaysInMonth × 12` arm — the IV-8b defect — STAYS until T8 lands. It is wrong under both answers,
    // and it is still not fixable here, because the two answers prescribe DIFFERENT replacements and
    // there is no safe interim between them: for a 28-day February, 336 is nearer the per-period answer
    // 28 than 365 is, so "hedge toward per-annum" would move a live customer figure FURTHER from the
    // corpus-supported answer while looking like a fix. Worse, DaysInYear for Calendar Month makes it
    // byte-identical to Calendar Year, collapsing two persisted, user-selectable styles the corpus
    // defines separately ("Month-wise (28, 29, 30 or 31 Days)" vs "Year-wise (365 or 366)"
    // [CORPUS-BOOK printed p.117]) into one behaviour — see
    // InterestTests.Calendar_month_and_calendar_year_must_never_resolve_to_the_same_divisor.
    //
    // WHEN T8 LANDS (slice S3): rewrite the two blocked arms below, and with them the expectations in the
    // two InterestTests methods whose names carry PROVISIONAL_PENDING_T8. Nothing else moves — the other
    // two arms are identical under both answers and are already final.
    // ================================================================================================

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
    /// accrual window is empty, or no part of the window carries an accruing balance on an allowed side.
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

    // ---------------------------------------------------------------- Always (running-balance) accrual

    private static IReadOnlyList<InterestLine> AlwaysLines(
        Company company, Domain.Ledger ledger, InterestParameters p, DateOnly from, DateOnly to)
    {
        var windowStart = EffectiveStart(from, p.CalculateFrom);
        if (windowStart >= to) return Array.Empty<InterestLine>();

        // The balance carried into the window, plus every dated movement inside it. Interest accrues on
        // the balance actually in force at each moment — not on a single opening figure held flat.
        var carriedIn = WalkRunningBalance(company, ledger, windowStart, to, out var movements);
        var boundaries = Boundaries(p, windowStart, to, movements.Keys);

        var segments = new List<Span>(boundaries.Count);
        var signed = carriedIn;
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            var segStart = boundaries[i];
            // Movements are keyed strictly after windowStart, so the first segment never adjusts here.
            if (movements.TryGetValue(segStart, out var delta)) signed += delta;
            segments.Add(new Span(segStart, boundaries[i + 1], signed));
        }

        return EmitRuns(ledger, p, segments, billReference: null);
    }

    /// <summary>
    /// One pass over the voucher set producing (a) the signed balance carried into
    /// <paramref name="windowStart"/> and (b) the signed movement on each date strictly inside
    /// <c>(windowStart, to)</c>.
    /// </summary>
    /// <remarks>
    /// The counting predicate is <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/>
    /// evaluated at the voucher's <b>own</b> date, which asks exactly "does this voucher ever reach the real
    /// books?" — its only as-of-dependent clause is <c>PostDated &amp;&amp; Date &gt; asOf</c>, false when
    /// <c>asOf == Date</c>. Because of that, <c>carriedIn + Σ{movements dated ≤ d}</c> is <b>identically</b>
    /// <see cref="LedgerBalances.SignedClosing(Company, Domain.Ledger, DateOnly)"/> at every <c>d</c> in the
    /// window, so the interest report can never drift from the Balance Sheet. The predicate is reused, never
    /// re-implemented; <c>InterestTests.Segment_principals_equal_SignedClosing_at_every_boundary</c> is the
    /// standing proof.
    /// A movement dated on or after <paramref name="to"/> is ignored: it would open a zero-day segment.
    /// </remarks>
    private static decimal WalkRunningBalance(
        Company company, Domain.Ledger ledger, DateOnly windowStart, DateOnly to,
        out SortedDictionary<DateOnly, decimal> movements)
    {
        movements = new SortedDictionary<DateOnly, decimal>();
        var carriedIn = ledger.SignedOpening;

        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, v.Date, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

            var delta = 0m;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledger.Id)
                    delta += line.Signed;
            if (delta == 0m) continue;

            if (v.Date <= windowStart) carriedIn += delta;
            else if (v.Date < to)
                movements[v.Date] = movements.TryGetValue(v.Date, out var running) ? running + delta : delta;
        }

        return carriedIn;
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
            // ⚠ Whether TallyPrime runs this from the due date itself is measurement T8c and is deliberately
            // NOT touched here — this slice changes the basis resolution, never the PostDue day-count.
            var accrualStart = EffectiveStart(Max(from, bill.DueDate.AddDays(1)), p.CalculateFrom);
            if (accrualStart >= to) continue;

            var side = bill.Kind == OutstandingKind.Receivable ? DrCr.Debit : DrCr.Credit;
            if (!SideAllowed(side, p.OnBalance)) continue;

            var principal = bill.Pending.Amount;
            if (principal <= 0m) continue;

            // A bill's pending amount is constant across the accrual; the segmentation exists so the basis
            // is resolved from each segment's own calendar period rather than anchored at the window start.
            var signed = side == DrCr.Debit ? principal : -principal;
            var boundaries = Boundaries(p, accrualStart, to, Array.Empty<DateOnly>());
            var segments = new List<Span>(boundaries.Count);
            for (var i = 0; i + 1 < boundaries.Count; i++)
                segments.Add(new Span(boundaries[i], boundaries[i + 1], signed));

            result.AddRange(EmitRuns(ledger, p, segments, bill.Reference));
        }
        return result;
    }

    // ---------------------------------------------------------------- core accrual math

    /// <summary>A half-open accrual span <c>[Start, End)</c> and the signed ledger balance in force over it.</summary>
    private readonly record struct Span(DateOnly Start, DateOnly End, decimal Signed);

    /// <summary>
    /// The boundary dates that cut <c>[start, end)</c> into segments: the window ends, every dated movement
    /// inside it, and — for the calendar "Per" styles, or for compound (which capitalises monthly) — every
    /// calendar-period start inside it. The 30-day and 365-day styles have a constant basis and need none.
    /// Month boundaries are a superset of year boundaries, so a compound Calendar-Year accrual is covered.
    /// </summary>
    private static List<DateOnly> Boundaries(
        InterestParameters p, DateOnly start, DateOnly end, IEnumerable<DateOnly> movementDates)
    {
        var set = new SortedSet<DateOnly> { start, end };
        foreach (var d in movementDates)
            if (d > start && d < end) set.Add(d);

        if (p.Per == InterestPer.CalendarMonth || p.Style == InterestStyle.Compound)
        {
            for (var c = FirstOfNextMonth(start); c < end; c = FirstOfNextMonth(c)) set.Add(c);
        }
        else if (p.Per == InterestPer.CalendarYear)
        {
            for (var c = FirstOfNextYear(start); c < end; c = FirstOfNextYear(c)) set.Add(c);
        }

        return new List<DateOnly>(set);
    }

    /// <summary>
    /// Folds the segments into one <see cref="InterestLine"/> per contiguous run of segments that actually
    /// accrue on the same side. A segment with a zero balance, a zero day-count, or a balance on a side the
    /// On-filter disallows contributes nothing and closes the current run.
    /// </summary>
    private static IReadOnlyList<InterestLine> EmitRuns(
        Domain.Ledger ledger, InterestParameters p, IReadOnlyList<Span> segments, string? billReference)
    {
        // 1. Partition into maximal contiguous same-side accruing runs.
        var runs = new List<List<Span>>();
        List<Span>? current = null;
        var currentIsDebit = false;

        foreach (var seg in segments)
        {
            var isDebit = seg.Signed >= 0m;
            var magnitude = isDebit ? seg.Signed : -seg.Signed;
            var days = seg.End.DayNumber - seg.Start.DayNumber;
            var accrues = magnitude > 0m && days > 0
                && SideAllowed(isDebit ? DrCr.Debit : DrCr.Credit, p.OnBalance);

            if (!accrues) { current = null; continue; }

            if (current is null || isDebit != currentIsDebit)
            {
                current = new List<Span>();
                runs.Add(current);
                currentIsDebit = isDebit;
            }
            current.Add(seg);
        }

        if (runs.Count == 0) return Array.Empty<InterestLine>();

        // 2. Accrue each run.
        var lines = new List<InterestLine>(runs.Count);
        var rate = p.RatePercent / 100m;
        var compound = p.Style == InterestStyle.Compound;

        foreach (var run in runs)
        {
            var accrued = new List<InterestSegment>(run.Count);
            var raw = 0m;

            // For compound, `runningPrincipal` is the balance magnitude PLUS the interest capitalised so
            // far. It is carried incrementally — a balance movement adjusts it by the delta — so that a
            // window with no movement reproduces the previously shipped month-by-month chain exactly.
            var previousMagnitude = run[0].Signed >= 0m ? run[0].Signed : -run[0].Signed;
            var runningPrincipal = previousMagnitude;
            var pending = 0m;   // interest accrued since the last capitalisation

            foreach (var seg in run)
            {
                var magnitude = seg.Signed >= 0m ? seg.Signed : -seg.Signed;
                runningPrincipal += magnitude - previousMagnitude;
                previousMagnitude = magnitude;

                var days = seg.End.DayNumber - seg.Start.DayNumber;
                var basis = BasisFor(p.Per, seg.Start);
                var principal = compound ? runningPrincipal : magnitude;
                var interest = principal * rate * days / basis;

                raw += interest;
                accrued.Add(new InterestSegment(
                    seg.Start, seg.End, seg.Signed, new Money(principal), days, basis, new Money(interest)));

                if (compound)
                {
                    pending += interest;
                    if (seg.End.Day == 1) { runningPrincipal += pending; pending = 0m; }  // capitalise monthly
                }
            }

            lines.Add(BuildLine(ledger, p, accrued, raw, billReference));
        }

        return lines;
    }

    /// <summary>
    /// Collapses an accrued run into the printed row. <b>Principal</b> is the time-weighted average
    /// <c>Σ(principalᵢ × daysᵢ) / Days</c>, which makes <c>Principal × Rate% × Days / Basis</c> reproduce the
    /// printed interest exactly <b>while the run is on one basis</b> — see the ⚠ on
    /// <see cref="InterestLine"/> for why a multi-basis run cannot foot against a single integer
    /// <see cref="InterestLine.Basis"/>. Rounding is applied once, here, to this ROW's summed raw interest —
    /// a sign-flipping window produces several rows and each is rounded on its own.
    /// </summary>
    private static InterestLine BuildLine(
        Domain.Ledger ledger, InterestParameters p, List<InterestSegment> run, decimal raw, string? billReference)
    {
        var from = run[0].From;
        var to = run[^1].To;
        var days = to.DayNumber - from.DayNumber;   // == Σ segment days: a run's segments are contiguous

        var weighted = 0m;
        foreach (var s in run) weighted += s.AccrualPrincipal.Amount * s.Days;

        return new InterestLine(
            ledger.Id, ledger.Name,
            new Money(weighted / days), run[0].SignedPrincipal >= 0m,
            p.RatePercent, p.Per, from, to, days, run[0].Basis,
            new Money(p.ApplyRounding(raw)),
            billReference, run);
    }

    /// <summary>
    /// The day-count denominator the rate is divided by for one accrual <b>segment</b>, resolved from that
    /// segment's own start date — so a window crossing a month/year boundary prices each part on its own
    /// calendar period instead of anchoring the whole accrual on the window's first month (which is what
    /// made the Simple path disagree with the Compound path over the same "Per").
    /// </summary>
    /// <remarks>
    /// ⚠ Two of the four arms are <b>still the defect IV-8b records</b> and are deliberately left that way:
    /// <see cref="InterestPer.ThirtyDayMonth"/> (360) and <see cref="InterestPer.CalendarMonth"/>
    /// (<c>DaysInMonth × 12</c> — 336, 348, 360 or 372, which is no real period length) belong to
    /// plan.md Phase 10.10 · WF-6 / slice <b>S3</b> and are BLOCKED on measurement <b>T8</b>. This slice
    /// changes only <i>which date</i> the divisor is resolved from, never the divisor. See the block above
    /// this class's members for why no interim value is safe.
    /// <see cref="InterestPer.ThreeSixtyFiveDayYear"/> (365) and <see cref="InterestPer.CalendarYear"/>
    /// (365/366) are identical under both answers to T8 and are final.
    /// The exact table in force is asserted, literal by literal, by
    /// <c>InterestTests.BasisFor_resolves_the_exact_divisor_in_force_for_every_style_and_date</c>.
    /// </remarks>
    public static int BasisFor(InterestPer per, DateOnly segmentStart) => per switch
    {
        // ---- FINAL: identical under both answers to T8 ------------------------------------------------
        InterestPer.ThreeSixtyFiveDayYear => 365,
        InterestPer.CalendarYear => DaysInYear(segmentStart.Year),

        // ---- T8-BLOCKED: owned by slice S3 / WF-6, held at the pre-slice value -------------------------
        InterestPer.ThirtyDayMonth => 360,
        InterestPer.CalendarMonth => DateTime.DaysInMonth(segmentStart.Year, segmentStart.Month) * 12,

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

    private static DateOnly FirstOfNextYear(DateOnly d) => new(d.Year + 1, 1, 1);
}
