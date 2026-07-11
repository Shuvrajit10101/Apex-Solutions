using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

// =========================================================================================================
//  Phase 7 slice 8 — TDS exception & outstanding reports (R1–R4). Pure read-only projections over the posted
//  TdsLineTax withholdings + recorded TdsChallan deposits; no schema, no new persistence. Every report follows
//  the Outstandings.cs pattern: a row record + a root record with computed Σ properties + a static Build.
// =========================================================================================================

// -------------------------------------------------- R1 TDS Outstandings -----------------------------------

/// <summary>One section's row in the <see cref="TdsOutstandingReport"/> (R1): TDS deducted vs deposited-covered,
/// the still-outstanding balance, and the overdue-days of the oldest still-undeposited deduction in the section.</summary>
public sealed record TdsOutstandingRow(
    string Section, string Nature, Money Deducted, Money Deposited, Money Outstanding, int OverdueDays);

/// <summary>
/// <b>R1 — TDS Outstandings</b> (Phase 7 slice 8): per income-tax section, the TDS <b>deducted but not yet
/// deposited</b> as of <see cref="AsOf"/>. Outstanding = Σ deducted − Σ deposited-covered, where coverage is the
/// FIFO <see cref="TdsCoverage"/> match capped at the challan's <b>DepositDate</b> ≤ asOf (a March deduction
/// deposited 7-Apr is NOT outstanding once the April challan covers it). The DepositDate is the legally-correct
/// coverage basis (the date funds reached the government); it differs from the Stat-Payment voucher posting date
/// only in an abnormal flow (a back/forward-dated challan), so R1 and <see cref="TdsDepositService.OutstandingPayable"/>
/// agree in the normal flow.
/// <para>
/// <b>Σ relationship (F2):</b> <see cref="TotalOutstanding"/> ≥ <see cref="TdsDepositService.OutstandingPayable"/>,
/// with equality in the normal one-liability-per-section regime. R1 is section-aware, so an over-deposit booked
/// against one section can never mask a still-outstanding liability under another; the aggregate
/// <c>OutstandingPayable</c> nets the single "TDS Payable" ledger (credit deductions − debit deposits, floored at
/// zero) and therefore <b>under-reports</b> under a multi-section over-deposit. Making <c>OutstandingPayable</c>
/// section-aware (Σ per-section max(0, owed)) is a deferred carry-forward (S9/user decision): it would change the
/// Stat-Payment over-deposit-guard semantics that deliberately rely on the netted ledger cap, so it is out of scope
/// for this read-only slice. Rows ordered by section (ordinal) for byte-stability. No UI, no DB.
/// </para>
/// </summary>
public sealed record TdsOutstandingReport(DateOnly AsOf, IReadOnlyList<TdsOutstandingRow> Rows)
{
    /// <summary>Σ TDS deducted across all sections.</summary>
    public Money TotalDeducted => new(Rows.Sum(r => r.Deducted.Amount));

    /// <summary>Σ TDS deposited-covered across all sections.</summary>
    public Money TotalDeposited => new(Rows.Sum(r => r.Deposited.Amount));

    /// <summary>Σ outstanding across all sections — ties to the "TDS Payable" ledger balance in the normal regime.</summary>
    public Money TotalOutstanding => new(Rows.Sum(r => r.Outstanding.Amount));

    /// <summary>Builds R1 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TdsOutstandingReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TdsCoverage.Match(company, asOf);

        var rows = units
            .GroupBy(u => u.Section, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var deducted = g.Sum(u => u.Tds);
                var deposited = g.Sum(u => u.Covered);
                var outstanding = g.Sum(u => u.Residual);

                // Overdue days of the OLDEST still-undeposited deduction (largest exposure); 0 if all covered.
                var oldestOpen = g.Where(u => u.Residual > 0m).OrderBy(u => u.Date).ThenBy(u => u.Order).FirstOrDefault();
                var overdue = oldestOpen is null
                    ? 0
                    : Math.Max(0, asOf.DayNumber - StatutoryInterest.StatutoryDueDate(oldestOpen.Date).DayNumber);

                var nature = company.FindNatureOfPayment(g.First().Tax.NatureId)?.Name ?? g.Key;
                return new TdsOutstandingRow(
                    g.Key, nature, new Money(deducted), new Money(deposited), new Money(outstanding), overdue);
            })
            .ToList();

        return new TdsOutstandingReport(asOf, rows);
    }
}

// -------------------------------------------------- R2 TDS Not Deducted -----------------------------------

/// <summary>One below-threshold assessment in the <see cref="TdsNotDeductedReport"/> (R2): TDS was applicable but
/// nothing was withheld because the section threshold was not (yet) crossed.</summary>
public sealed record TdsNotDeductedRow(
    DateOnly Date, Guid PartyId, string Party, string Section, string Nature,
    Money Assessable, Money CumulativeInFy, Money Threshold, Money Shortfall);

/// <summary>
/// <b>R2 — TDS Not Deducted</b> (Phase 7 slice 8): the payments where TDS was <b>applicable but ₹0 was withheld</b>
/// because the single-transaction and cumulative-FY thresholds were not crossed. Read off every posted
/// <see cref="TdsLineTax"/> with <c>TdsAmount == 0</c> (the below-threshold detail rides the party leg), dated ≤
/// <see cref="AsOf"/>. Shows the FY cumulative-so-far (<see cref="TdsService.ProjectPriorCumulative"/>) and the
/// <b>binding</b> threshold limb + shortfall — the limb nearest to triggering, matching the engine's
/// single-OR-cumulative gate (F1) so the advisory never implies TDS is due before it actually is. Rows ordered by
/// (date, party, section) with intrinsic tie-breaks for byte-stability. No UI, no DB.
/// </summary>
public sealed record TdsNotDeductedReport(DateOnly AsOf, IReadOnlyList<TdsNotDeductedRow> Rows)
{
    /// <summary>Σ assessable value across the below-threshold rows.</summary>
    public Money TotalAssessable => new(Rows.Sum(r => r.Assessable.Amount));

    /// <summary>Builds R2 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TdsNotDeductedReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var svc = new TdsService(company);
        var rows = new List<TdsNotDeductedRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t) continue;
                if (t.TdsAmount.Amount != 0m) continue; // only the applicable-but-below-threshold assessments

                var party = company.FindLedger(t.DeducteeLedgerId);
                var nature = company.FindNatureOfPayment(t.NatureId);
                var cumulative = svc.ProjectPriorCumulative(t.DeducteeLedgerId, t.NatureId, v.Date);
                var (threshold, shortfall) = BindingThreshold(nature, t.AssessableValue, cumulative);

                rows.Add(new TdsNotDeductedRow(
                    v.Date, t.DeducteeLedgerId, party?.Name ?? "(unknown)", t.SectionCode,
                    nature?.Name ?? t.SectionCode, t.AssessableValue, cumulative, threshold, shortfall));
            }
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byParty = string.Compare(a.Party, b.Party, StringComparison.OrdinalIgnoreCase);
            if (byParty != 0) return byParty;
            var bySection = string.Compare(a.Section, b.Section, StringComparison.Ordinal);
            if (bySection != 0) return bySection;
            // Intrinsic, input-order-independent final tie-breaks (F3) so equal (date, party, section) rows are
            // byte-stable regardless of the physical voucher order: deductee id, then assessable, then FY cumulative.
            var byPartyId = a.PartyId.CompareTo(b.PartyId);
            if (byPartyId != 0) return byPartyId;
            var byAssessable = a.Assessable.Amount.CompareTo(b.Assessable.Amount);
            if (byAssessable != 0) return byAssessable;
            return a.CumulativeInFy.Amount.CompareTo(b.CumulativeInFy.Amount);
        });
        return new TdsNotDeductedReport(asOf, rows);
    }

    /// <summary>
    /// The binding threshold limb + the shortfall to it, matching <c>TdsService.ThresholdCrossed</c> (F1): TDS is
    /// withheld once the <b>single</b> transaction exceeds the single-transaction limb OR the FY aggregate exceeds
    /// the <b>cumulative</b> limb, so the limb <b>nearest to triggering</b> (smaller gap) is surfaced. For a section
    /// carrying BOTH limbs (e.g. §194C ₹30,000 single / ₹1,00,000 cumulative) this keeps the advisory shortfall from
    /// implying TDS is due when a single below-single-limb payment (FY aggregate still under the cumulative limb) is
    /// not yet liable — the old "cumulative ?? single" logic silently dropped the single limb.
    /// <paramref name="cumulativeInFy"/> already includes this line (post-hoc projection). A tie prefers the single
    /// limb (a single large payment is the more immediate trigger); a no-threshold section has no shortfall.
    /// </summary>
    private static (Money Threshold, Money Shortfall) BindingThreshold(
        NatureOfPayment? nature, Money assessable, Money cumulativeInFy)
    {
        var single = nature?.SingleTransactionThreshold;
        var cumulative = nature?.CumulativeThreshold;
        decimal? singleGap = single is { } s ? Math.Max(0m, s.Amount - assessable.Amount) : null;
        decimal? cumGap = cumulative is { } c ? Math.Max(0m, c.Amount - cumulativeInFy.Amount) : null;

        if (singleGap is { } sg && (cumGap is not { } cg || sg <= cg))
            return (single!.Value, new Money(sg));
        if (cumGap is { } cg2)
            return (cumulative!.Value, new Money(cg2));
        return (Money.Zero, Money.Zero);
    }
}

// -------------------------------------------------- R3 TDS Interest u/s 201(1A) ----------------------------

/// <summary>One interest row in the <see cref="TdsInterestReport"/> (R3): the late-deposit interest on one
/// deduction (or one deposit portion of it). <see cref="DepositDate"/> is null when the TDS is still undeposited
/// (interest then runs to the report's as-of date).</summary>
public sealed record TdsInterestRow(
    Guid PartyId, string Party, string Section, Money Tds,
    DateOnly DeductionDate, DateOnly? DepositDate, DateOnly DueDate, int Months, Money Interest);

/// <summary>
/// <b>R3 — TDS Interest u/s 201(1A)</b> (Phase 7 slice 8): the <b>late-deposit</b> interest limb (ii) — 1.5% per
/// month or part, from the <b>deduction date</b> to the <b>deposit date</b> (or to <see cref="AsOf"/> when still
/// undeposited). Only rows past their statutory due date accrue (an on-time deposit shows no interest). A deduction
/// discharged by several challans yields one row per deposit portion (plus one for any undeposited residual), each
/// with its own late-months, so a split challan is never double-charged. Rows ordered by (section, deduction date,
/// party). Limb (i) — failure to <i>deduct</i> at 1% — is <b>not computable</b> in this model (no deductible date
/// distinct from the voucher date) and is not shown; see <see cref="Footnote"/>.
/// </summary>
public sealed record TdsInterestReport(DateOnly AsOf, IReadOnlyList<TdsInterestRow> Rows)
{
    /// <summary>The one-line note that only the late-deposit limb is computed.</summary>
    public const string Footnote =
        "Interest is computed on the §201(1A)(ii) late-deposit limb only (1.5% per month from deduction to " +
        "deposit). The §201(1A)(i) late-deduction limb (1% per month) is not computable here — the model carries " +
        "no deductible date distinct from the voucher date.";

    /// <summary>Σ TDS across the interest-bearing rows (portion amounts).</summary>
    public Money TotalTds => new(Rows.Sum(r => r.Tds.Amount));

    /// <summary>Σ interest across all rows.</summary>
    public Money TotalInterest => new(Rows.Sum(r => r.Interest.Amount));

    /// <summary>Builds R3 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TdsInterestReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TdsCoverage.Match(company, asOf);
        var rows = new List<TdsInterestRow>();

        foreach (var u in units)
        {
            var party = company.FindLedger(u.Tax.DeducteeLedgerId);
            var due = StatutoryInterest.StatutoryDueDate(u.Date);

            // One row per deposited portion, gated by its own late-months.
            foreach (var p in u.Portions)
            {
                var months = StatutoryInterest.LateMonths(u.Date, p.DepositDate);
                if (months <= 0) continue;
                var interest = StatutoryInterest.LateInterest(new Money(p.Take), months, StatutoryInterest.TdsLatePaymentMonthlyRate);
                rows.Add(new TdsInterestRow(
                    u.Tax.DeducteeLedgerId, party?.Name ?? "(unknown)", u.Section, new Money(p.Take),
                    u.Date, p.DepositDate, due, months, interest));
            }

            // The still-undeposited residual accrues to the as-of date.
            if (u.Residual > 0m)
            {
                var months = StatutoryInterest.LateMonths(u.Date, asOf);
                if (months > 0)
                {
                    var interest = StatutoryInterest.LateInterest(new Money(u.Residual), months, StatutoryInterest.TdsLatePaymentMonthlyRate);
                    rows.Add(new TdsInterestRow(
                        u.Tax.DeducteeLedgerId, party?.Name ?? "(unknown)", u.Section, new Money(u.Residual),
                        u.Date, null, due, months, interest));
                }
            }
        }

        rows.Sort(CompareInterestRows);
        return new TdsInterestReport(asOf, rows);
    }

    private static int CompareInterestRows(TdsInterestRow a, TdsInterestRow b)
    {
        var bySection = string.Compare(a.Section, b.Section, StringComparison.Ordinal);
        if (bySection != 0) return bySection;
        var byDate = a.DeductionDate.CompareTo(b.DeductionDate);
        if (byDate != 0) return byDate;
        var byParty = string.Compare(a.Party, b.Party, StringComparison.OrdinalIgnoreCase);
        if (byParty != 0) return byParty;
        return a.PartyId.CompareTo(b.PartyId);
    }
}

// -------------------------------------------------- R4 TDS Nature-of-Payment summary -----------------------

/// <summary>One section's row in the <see cref="TdsNatureSummaryReport"/> (R4).</summary>
public sealed record TdsNatureSummaryRow(
    string Section, string Nature, Money Assessable, Money Deducted, Money Deposited, Money Outstanding,
    int BelowThresholdCount);

/// <summary>
/// <b>R4 — TDS Nature-of-Payment-wise summary</b> (Phase 7 slice 8): every §194x section with any TDS activity as
/// of <see cref="AsOf"/> — the assessable base (across deducted AND below-threshold lines), the deducted, the
/// deposited-covered, the outstanding, and the count of below-threshold (not-deducted) transactions. Rows ordered
/// by section (ordinal). No UI, no DB.
/// </summary>
public sealed record TdsNatureSummaryReport(DateOnly AsOf, IReadOnlyList<TdsNatureSummaryRow> Rows)
{
    /// <summary>Σ deducted across all sections.</summary>
    public Money TotalDeducted => new(Rows.Sum(r => r.Deducted.Amount));

    /// <summary>Σ outstanding across all sections.</summary>
    public Money TotalOutstanding => new(Rows.Sum(r => r.Outstanding.Amount));

    /// <summary>Builds R4 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static TdsNatureSummaryReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var units = TdsCoverage.Match(company, asOf); // TDS>0 units (deducted/deposited/outstanding)

        // Deducted/deposited/outstanding + deducted-assessable per section, from the coverage units.
        var deducted = new Dictionary<string, (decimal Assessable, decimal Deducted, decimal Deposited, decimal Outstanding, Guid NatureId)>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            deducted.TryGetValue(u.Section, out var acc);
            acc.Assessable += u.Tax.AssessableValue.Amount;
            acc.Deducted += u.Tds;
            acc.Deposited += u.Covered;
            acc.Outstanding += u.Residual;
            if (acc.NatureId == Guid.Empty) acc.NatureId = u.Tax.NatureId;
            deducted[u.Section] = acc;
        }

        // Below-threshold assessable + counts per section (TdsAmount == 0 lines).
        var below = new Dictionary<string, (decimal Assessable, int Count, Guid NatureId)>(StringComparer.Ordinal);
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date > asOf) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t || t.TdsAmount.Amount != 0m) continue;
                below.TryGetValue(t.SectionCode, out var acc);
                acc.Assessable += t.AssessableValue.Amount;
                acc.Count += 1;
                if (acc.NatureId == Guid.Empty) acc.NatureId = t.NatureId;
                below[t.SectionCode] = acc;
            }
        }

        var sections = deducted.Keys.Union(below.Keys, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);
        var rows = new List<TdsNatureSummaryRow>();
        foreach (var section in sections)
        {
            deducted.TryGetValue(section, out var d);
            below.TryGetValue(section, out var b);
            var natureId = d.NatureId != Guid.Empty ? d.NatureId : b.NatureId;
            var nature = company.FindNatureOfPayment(natureId)?.Name ?? section;
            rows.Add(new TdsNatureSummaryRow(
                section, nature,
                new Money(d.Assessable + b.Assessable), new Money(d.Deducted), new Money(d.Deposited),
                new Money(d.Outstanding), b.Count));
        }

        return new TdsNatureSummaryReport(asOf, rows);
    }
}
