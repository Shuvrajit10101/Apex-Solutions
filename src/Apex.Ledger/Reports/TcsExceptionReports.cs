using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

// =========================================================================================================
//  Phase 7 slice 8 — TCS exception & outstanding reports (R5–R8). The exact mirror of the TDS reports R1–R4,
//  keyed by §206C collection code, over the posted TcsLineTax collections + recorded TcsChallan deposits.
//  TCS is additive (collected on top), but discharged the same way, so the projections are structurally identical.
// =========================================================================================================

// -------------------------------------------------- R5 TCS Outstandings -----------------------------------

/// <summary>One collection-code row in the <see cref="TcsOutstandingReport"/> (R5).</summary>
public sealed record TcsOutstandingRow(
    string Code, string Nature, Money Collected, Money Deposited, Money Outstanding, int OverdueDays);

/// <summary>
/// <b>R5 — TCS Outstandings</b> (Phase 7 slice 8): the mirror of <see cref="TdsOutstandingReport"/> for the
/// collector — per §206C collection code, the TCS <b>collected but not yet deposited</b> as of <see cref="AsOf"/>
/// (Σ collected − Σ deposited-covered via <see cref="TcsCoverage"/>). Coverage is capped at the challan's
/// <b>DepositDate</b> (the legally-correct basis — the date funds reached the government); this differs from the
/// Stat-Payment voucher posting date only in an abnormal flow (a back/forward-dated challan), so R5 and
/// <see cref="TcsDepositService.OutstandingPayable"/> agree in the normal flow.
/// <para>
/// <b>Σ relationship (F2):</b> <see cref="TotalOutstanding"/> ≥ <see cref="TcsDepositService.OutstandingPayable"/>,
/// with equality in the normal one-liability-per-code regime. R5 is code-aware, so it never lets an over-deposit
/// against one code mask a still-outstanding liability under another; the aggregate <c>OutstandingPayable</c> nets
/// the single "TCS Payable" ledger and so <b>under-reports</b> under a multi-code over-deposit. Making
/// <c>OutstandingPayable</c> code-aware is a deferred carry-forward (S9/user decision): it would change the
/// Stat-Payment over-deposit-guard semantics that deliberately rely on the netted ledger cap, so it is out of
/// scope for this read-only slice. Rows ordered by code.
/// </para>
/// </summary>
public sealed record TcsOutstandingReport(DateOnly AsOf, IReadOnlyList<TcsOutstandingRow> Rows)
{
    /// <summary>Σ TCS collected across all codes.</summary>
    public Money TotalCollected => new(Rows.Sum(r => r.Collected.Amount));

    /// <summary>Σ TCS deposited-covered across all codes.</summary>
    public Money TotalDeposited => new(Rows.Sum(r => r.Deposited.Amount));

    /// <summary>Σ outstanding across all codes — ties to the "TCS Payable" ledger balance in the normal regime.</summary>
    public Money TotalOutstanding => new(Rows.Sum(r => r.Outstanding.Amount));

    /// <summary>Builds R5 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TcsOutstandingReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TcsCoverage.Match(company, asOf);

        var rows = units
            .GroupBy(u => u.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var collected = g.Sum(u => u.Tcs);
                var deposited = g.Sum(u => u.Covered);
                var outstanding = g.Sum(u => u.Residual);

                var oldestOpen = g.Where(u => u.Residual > 0m).OrderBy(u => u.Date).ThenBy(u => u.Order).FirstOrDefault();
                var overdue = oldestOpen is null
                    ? 0
                    : Math.Max(0, asOf.DayNumber - StatutoryInterest.StatutoryDueDate(oldestOpen.Date).DayNumber);

                var nature = company.FindNatureOfGoods(g.First().Tax.NatureId)?.Name ?? g.Key;
                return new TcsOutstandingRow(
                    g.Key, nature, new Money(collected), new Money(deposited), new Money(outstanding), overdue);
            })
            .ToList();

        return new TcsOutstandingReport(asOf, rows);
    }
}

// -------------------------------------------------- R6 TCS Not Collected ----------------------------------

/// <summary>One below-threshold collection in the <see cref="TcsNotCollectedReport"/> (R6).</summary>
public sealed record TcsNotCollectedRow(
    DateOnly Date, Guid PartyId, string Party, string Code, string Nature,
    Money Assessable, Money CumulativeInFy, Money Threshold, Money Shortfall);

/// <summary>
/// <b>R6 — TCS Not Collected</b> (Phase 7 slice 8): the mirror of <see cref="TdsNotDeductedReport"/> — the sales
/// where TCS was <b>applicable but ₹0 was collected</b> (below the §206C value threshold, e.g. §206C(1F)
/// ₹10,00,000 or §206C(1H) ₹50,00,000). Read off every posted <see cref="TcsLineTax"/> with <c>TcsAmount == 0</c>,
/// dated ≤ <see cref="AsOf"/>. The <c>Threshold</c>/<c>Shortfall</c> columns follow the nature's actual gate (F1):
/// the single-transaction shortfall (per line) for a §206C(1F)-style single gate, the cumulative shortfall for the
/// §206C(1H) legacy cumulative gate — so the advisory never implies TCS is due when the engine collects ₹0. Known
/// carry-forward (S5): on a mixed sale with more than one below-threshold TCS nature only the first nature's detail
/// is persisted; this report shows what exists and never double-counts.
/// </summary>
public sealed record TcsNotCollectedReport(DateOnly AsOf, IReadOnlyList<TcsNotCollectedRow> Rows)
{
    /// <summary>Σ assessable value across the below-threshold rows.</summary>
    public Money TotalAssessable => new(Rows.Sum(r => r.Assessable.Amount));

    /// <summary>Builds R6 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TcsNotCollectedReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var svc = new TcsService(company);
        var rows = new List<TcsNotCollectedRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t) continue;
                if (t.TcsAmount.Amount != 0m) continue;

                var party = company.FindLedger(t.CollecteeLedgerId);
                var nature = company.FindNatureOfGoods(t.NatureId);
                var cumulative = svc.ProjectPriorCumulative(t.CollecteeLedgerId, t.NatureId, v.Date);
                var (threshold, shortfall) = BindingThreshold(nature, t.AssessableValue, cumulative);

                rows.Add(new TcsNotCollectedRow(
                    v.Date, t.CollecteeLedgerId, party?.Name ?? "(unknown)", t.CollectionCode,
                    nature?.Name ?? t.CollectionCode, t.AssessableValue, cumulative, threshold, shortfall));
            }
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byParty = string.Compare(a.Party, b.Party, StringComparison.OrdinalIgnoreCase);
            if (byParty != 0) return byParty;
            var byCode = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            if (byCode != 0) return byCode;
            // Intrinsic, input-order-independent final tie-breaks (F3) so equal (date, party, code) rows are
            // byte-stable regardless of the physical voucher order: collectee id, then assessable, then FY cumulative.
            var byPartyId = a.PartyId.CompareTo(b.PartyId);
            if (byPartyId != 0) return byPartyId;
            var byAssessable = a.Assessable.Amount.CompareTo(b.Assessable.Amount);
            if (byAssessable != 0) return byAssessable;
            return a.CumulativeInFy.Amount.CompareTo(b.CumulativeInFy.Amount);
        });
        return new TcsNotCollectedReport(asOf, rows);
    }

    /// <summary>
    /// The binding threshold + shortfall matching <c>TcsService.ThresholdCrossed</c> (F1): a §206C(1H) <b>legacy</b>
    /// nature gates on the FY-cumulative receipts (shortfall vs the cumulative), while every other threshold-bearing
    /// nature (e.g. §206C(1F) motor-vehicle ₹10,00,000) gates <b>per single transaction</b> (shortfall vs THIS sale's
    /// assessable — a per-line constant). This keeps the advisory shortfall from ever implying TCS is due merely
    /// because the FY aggregate has crossed a single-transaction threshold the engine never applies cumulatively.
    /// A no-threshold nature (which always collects) has no shortfall.
    /// </summary>
    private static (Money Threshold, Money Shortfall) BindingThreshold(
        NatureOfGoods? nature, Money assessable, Money cumulativeInFy)
    {
        if (nature?.Threshold is not { } threshold) return (Money.Zero, Money.Zero);
        var basis = nature.IsLegacy ? cumulativeInFy.Amount : assessable.Amount;
        return (threshold, new Money(Math.Max(0m, threshold.Amount - basis)));
    }
}

// -------------------------------------------------- R7 TCS Interest u/s 206C(7) ----------------------------

/// <summary>One interest row in the <see cref="TcsInterestReport"/> (R7).</summary>
public sealed record TcsInterestRow(
    Guid PartyId, string Party, string Code, Money Tcs,
    DateOnly CollectionDate, DateOnly? DepositDate, DateOnly DueDate, int Months, Money Interest);

/// <summary>
/// <b>R7 — TCS Interest u/s 206C(7)</b> (Phase 7 slice 8): the mirror of <see cref="TdsInterestReport"/> — a single
/// limb, 1% per month or part, from the <b>collection date</b> to the <b>deposit date</b> (or to <see cref="AsOf"/>
/// when undeposited). Only rows past their statutory due date accrue; a split challan yields one row per portion so
/// interest is never double-charged. Rows ordered by (code, collection date, party).
/// </summary>
public sealed record TcsInterestReport(DateOnly AsOf, IReadOnlyList<TcsInterestRow> Rows)
{
    /// <summary>Σ TCS across the interest-bearing rows (portion amounts).</summary>
    public Money TotalTcs => new(Rows.Sum(r => r.Tcs.Amount));

    /// <summary>Σ interest across all rows.</summary>
    public Money TotalInterest => new(Rows.Sum(r => r.Interest.Amount));

    /// <summary>Builds R7 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TcsInterestReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TcsCoverage.Match(company, asOf);
        var rows = new List<TcsInterestRow>();

        foreach (var u in units)
        {
            var party = company.FindLedger(u.Tax.CollecteeLedgerId);
            var due = StatutoryInterest.StatutoryDueDate(u.Date);

            foreach (var p in u.Portions)
            {
                var months = StatutoryInterest.LateMonths(u.Date, p.DepositDate);
                if (months <= 0) continue;
                var interest = StatutoryInterest.LateInterest(new Money(p.Take), months, StatutoryInterest.TcsLatePaymentMonthlyRate);
                rows.Add(new TcsInterestRow(
                    u.Tax.CollecteeLedgerId, party?.Name ?? "(unknown)", u.Code, new Money(p.Take),
                    u.Date, p.DepositDate, due, months, interest));
            }

            if (u.Residual > 0m)
            {
                var months = StatutoryInterest.LateMonths(u.Date, asOf);
                if (months > 0)
                {
                    var interest = StatutoryInterest.LateInterest(new Money(u.Residual), months, StatutoryInterest.TcsLatePaymentMonthlyRate);
                    rows.Add(new TcsInterestRow(
                        u.Tax.CollecteeLedgerId, party?.Name ?? "(unknown)", u.Code, new Money(u.Residual),
                        u.Date, null, due, months, interest));
                }
            }
        }

        rows.Sort(CompareInterestRows);
        return new TcsInterestReport(asOf, rows);
    }

    private static int CompareInterestRows(TcsInterestRow a, TcsInterestRow b)
    {
        var byCode = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
        if (byCode != 0) return byCode;
        var byDate = a.CollectionDate.CompareTo(b.CollectionDate);
        if (byDate != 0) return byDate;
        var byParty = string.Compare(a.Party, b.Party, StringComparison.OrdinalIgnoreCase);
        if (byParty != 0) return byParty;
        return a.PartyId.CompareTo(b.PartyId);
    }
}

// -------------------------------------------------- R8 TCS Nature-of-Goods summary -------------------------

/// <summary>One collection-code row in the <see cref="TcsNatureSummaryReport"/> (R8).</summary>
public sealed record TcsNatureSummaryRow(
    string Code, string Nature, Money Assessable, Money Collected, Money Deposited, Money Outstanding,
    int BelowThresholdCount);

/// <summary>
/// <b>R8 — TCS Nature-of-Goods-wise summary</b> (Phase 7 slice 8): the mirror of <see cref="TdsNatureSummaryReport"/>
/// — every §206C collection code with any TCS activity as of <see cref="AsOf"/>, with assessable, collected,
/// deposited-covered, outstanding, and the below-threshold count. Rows ordered by code.
/// </summary>
public sealed record TcsNatureSummaryReport(DateOnly AsOf, IReadOnlyList<TcsNatureSummaryRow> Rows)
{
    /// <summary>Σ collected across all codes.</summary>
    public Money TotalCollected => new(Rows.Sum(r => r.Collected.Amount));

    /// <summary>Σ outstanding across all codes.</summary>
    public Money TotalOutstanding => new(Rows.Sum(r => r.Outstanding.Amount));

    /// <summary>Builds R8 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TcsNatureSummaryReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TcsCoverage.Match(company, asOf);

        var collected = new Dictionary<string, (decimal Assessable, decimal Collected, decimal Deposited, decimal Outstanding, Guid NatureId)>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            collected.TryGetValue(u.Code, out var acc);
            acc.Assessable += u.Tax.AssessableValue.Amount;
            acc.Collected += u.Tcs;
            acc.Deposited += u.Covered;
            acc.Outstanding += u.Residual;
            if (acc.NatureId == Guid.Empty) acc.NatureId = u.Tax.NatureId;
            collected[u.Code] = acc;
        }

        var below = new Dictionary<string, (decimal Assessable, int Count, Guid NatureId)>(StringComparer.Ordinal);
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t || t.TcsAmount.Amount != 0m) continue;
                below.TryGetValue(t.CollectionCode, out var acc);
                acc.Assessable += t.AssessableValue.Amount;
                acc.Count += 1;
                if (acc.NatureId == Guid.Empty) acc.NatureId = t.NatureId;
                below[t.CollectionCode] = acc;
            }
        }

        var codes = collected.Keys.Union(below.Keys, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);
        var rows = new List<TcsNatureSummaryRow>();
        foreach (var code in codes)
        {
            collected.TryGetValue(code, out var d);
            below.TryGetValue(code, out var b);
            var natureId = d.NatureId != Guid.Empty ? d.NatureId : b.NatureId;
            var nature = company.FindNatureOfGoods(natureId)?.Name ?? code;
            rows.Add(new TcsNatureSummaryRow(
                code, nature,
                new Money(d.Assessable + b.Assessable), new Money(d.Collected), new Money(d.Deposited),
                new Money(d.Outstanding), b.Count));
        }

        return new TcsNatureSummaryReport(asOf, rows);
    }
}
