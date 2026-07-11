using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The deductor (filer) block of a <see cref="Form26Q"/> return — the TAN, deductor legal type and the
/// person-responsible-for-deduction identity captured on F11 (<see cref="TdsConfig"/>). Denormalised onto the
/// projection so the FVU writer needs no further company lookup.
/// </summary>
public sealed record Form26QDeductor(
    string Tan,
    DeductorType DeductorType,
    string? ResponsiblePersonName,
    string? ResponsiblePersonPan,
    string? ResponsiblePersonDesignation,
    string? ResponsiblePersonAddress);

/// <summary>
/// One <b>deductee detail</b> row of Form 26Q — a single TDS withholding on a party in the return quarter: the
/// party <see cref="DeducteePan"/> + name, the income-tax <see cref="SectionCode"/> and its Form-26Q/FVU
/// <see cref="FvuSectionCode"/>, the <see cref="DeductionDate"/> (the voucher date), the
/// <see cref="AmountPaid"/> (the GST-exclusive assessable base credited), the <see cref="TdsAmount"/> withheld,
/// the applied <see cref="RateBasisPoints"/>, whether the deductee PAN drove the rate (<see cref="PanApplied"/>)
/// and an optional §197 lower/nil-deduction certificate <see cref="Section197Reason"/> (absent until modelled).
/// Read verbatim off the posted <see cref="TdsLineTax"/> — never recomputed — so the rows reconcile to the
/// "TDS Payable" credit postings for the quarter by construction.
/// </summary>
public sealed record Form26QDeducteeRow(
    Guid DeducteeLedgerId,
    string DeducteeName,
    string? DeducteePan,
    string SectionCode,
    string FvuSectionCode,
    DateOnly DeductionDate,
    Money AmountPaid,
    Money TdsAmount,
    int RateBasisPoints,
    bool PanApplied,
    string? Section197Reason = null)
{
    /// <summary>The applied rate as a percentage (e.g. 10.00 for 1000 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;
}

/// <summary>
/// One <b>challan block</b> of Form 26Q — an ITNS-281 deposit (<see cref="TdsChallan"/>) attributed to this
/// return quarter because it covers (discharges) at least one deduction that falls in the quarter (period
/// attribution is by the <b>deduction</b> quarter, not the challan deposit date — a March deduction deposited on
/// 7-Apr belongs to Q4, with the April challan listed there). <see cref="DeducteeRows"/> are the deductions this
/// challan discharges whose deduction date falls in the quarter, in FIFO order.
/// </summary>
public sealed record Form26QChallan(
    Guid ChallanId,
    string ChallanNo,
    string BsrCode,
    DateOnly DepositDate,
    Money Amount,
    string Section,
    string MinorHead,
    IReadOnlyList<Form26QDeducteeRow> DeducteeRows);

/// <summary>
/// Form-27A-style <b>control totals</b> for a Form 26Q return (Phase 7 slice 4): the record counts and money
/// totals a filer cross-checks before generating the FVU upload file. <see cref="Validate"/> emulates the FVU
/// control-total checks and returns a human-readable warning/error list — empty when the return tallies.
/// </summary>
/// <param name="DeducteeRecordCount">Count of deductee detail rows in the quarter.</param>
/// <param name="ChallanRecordCount">Count of challan blocks in the quarter.</param>
/// <param name="TotalTdsDeducted">Σ TDS withheld across the deductee rows (the TDS-Payable credit for the quarter).</param>
/// <param name="TotalAmountPaid">Σ assessable amount paid/credited across the deductee rows.</param>
/// <param name="TotalDepositedAsPerChallans">Σ deposit amount across the challan blocks attributed to the quarter.</param>
/// <param name="TotalTdsDepositedForQuarter">Σ TDS this quarter's deductions were discharged by (via FIFO challan matching).</param>
public sealed record Form26QControlTotals(
    int DeducteeRecordCount,
    int ChallanRecordCount,
    Money TotalTdsDeducted,
    Money TotalAmountPaid,
    Money TotalDepositedAsPerChallans,
    Money TotalTdsDepositedForQuarter)
{
    /// <summary>
    /// Emulates the FVU control-total cross-checks and returns the mismatch messages (empty ⇒ the return tallies):
    /// the TDS deducted for the quarter must equal the TDS deposited against this quarter's deductions (a positive
    /// gap ⇒ undeposited TDS; a negative gap ⇒ over-deposit); no total may be negative.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (TotalTdsDeducted.Amount < 0m)
            problems.Add($"Total TDS deducted is negative ({TotalTdsDeducted}).");
        if (TotalAmountPaid.Amount < 0m)
            problems.Add($"Total amount paid is negative ({TotalAmountPaid}).");

        var gap = TotalTdsDeducted.Amount - TotalTdsDepositedForQuarter.Amount;
        if (gap > 0m)
            problems.Add(
                $"TDS deducted ({TotalTdsDeducted}) exceeds TDS deposited against this quarter's deductions " +
                $"({TotalTdsDepositedForQuarter}); {new Money(gap)} is undeposited.");
        else if (gap < 0m)
            problems.Add(
                $"TDS deposited against this quarter's deductions ({TotalTdsDepositedForQuarter}) exceeds TDS " +
                $"deducted ({TotalTdsDeducted}) by {new Money(-gap)}.");

        return problems;
    }

    /// <summary>True iff the control totals tally (no warnings/errors).</summary>
    public bool Tallies => Validate().Count == 0;
}

/// <summary>
/// <b>Form 26Q</b> — the quarterly TDS return (Phase 7 slice 4; catalog §13) as a pure, read-only projection over
/// the posted <see cref="TdsLineTax"/> withholdings + recorded <see cref="TdsChallan"/> deposits for a financial
/// year + quarter. Mirrors <see cref="Gstr1"/>: it reads the withheld tax off each line — never recomputing — so
/// the return reconciles to the "TDS Payable" credit postings for the quarter <b>by construction</b>.
/// <para>
/// <b>Period attribution (the cross-FY rule):</b> a <b>deduction</b> is attributed to the quarter of its
/// <b>deduction (voucher) date</b>. A <b>challan</b> is attributed to the quarter of the deduction(s) it covers —
/// resolved by FIFO-matching each section's challans (ordered by deposit date) against that section's deductions
/// (ordered by deduction date), <b>not</b> naively by the challan's own deposit date. So a 20-Mar deduction
/// deposited by a 7-Apr challan lands in Q4 (the deduction quarter) with the April challan listed there — it is
/// never double-windowed into the next quarter/FY.
/// </para>
/// A non-TDS company (or a quarter with no withholding) yields an empty return — no challan, no deductee row,
/// zero totals — and the FVU writer still emits a valid (header-only) file. Deterministic (no clock/RNG); ordered
/// for byte-stability. No UI, no DB.
/// </summary>
public sealed record Form26Q(
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    Form26QDeductor Deductor,
    IReadOnlyList<Form26QChallan> Challans,
    IReadOnlyList<Form26QDeducteeRow> Deductees)
{
    /// <summary>The financial-year label (e.g. "2024-25") of <see cref="FinancialYearStartYear"/>.</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>Σ TDS withheld across the return's deductee rows (the TDS-Payable credit for the quarter).</summary>
    public Money TotalTdsDeducted => new(Deductees.Sum(d => d.TdsAmount.Amount));

    /// <summary>Σ assessable amount paid/credited across the return's deductee rows.</summary>
    public Money TotalAmountPaid => new(Deductees.Sum(d => d.AmountPaid.Amount));

    /// <summary>Σ deposit amount across the challan blocks attributed to the quarter.</summary>
    public Money TotalDepositedAsPerChallans => new(Challans.Sum(c => c.Amount.Amount));

    /// <summary>Σ TDS this quarter's deductions were discharged by (via FIFO challan matching).</summary>
    public Money TotalTdsDepositedForQuarter => new(Challans.Sum(c => c.DeducteeRows.Sum(r => r.TdsAmount.Amount)));

    /// <summary>The Form-27A-style control totals for the return.</summary>
    public Form26QControlTotals ControlTotals => new(
        Deductees.Count, Challans.Count, TotalTdsDeducted, TotalAmountPaid,
        TotalDepositedAsPerChallans, TotalTdsDepositedForQuarter);

    /// <summary>True iff there is nothing to file this quarter (no deduction and no attributed challan).</summary>
    public bool IsEmpty => Deductees.Count == 0 && Challans.Count == 0;

    /// <summary>The inclusive date window of quarter <paramref name="quarter"/> (1..4) of FY starting
    /// <paramref name="fyStartYear"/>-04-01: Q1 Apr-Jun, Q2 Jul-Sep, Q3 Oct-Dec, Q4 Jan-Mar (next calendar year).</summary>
    public static (DateOnly From, DateOnly To) QuarterWindow(int fyStartYear, int quarter) => quarter switch
    {
        1 => (new DateOnly(fyStartYear, 4, 1), new DateOnly(fyStartYear, 6, 30)),
        2 => (new DateOnly(fyStartYear, 7, 1), new DateOnly(fyStartYear, 9, 30)),
        3 => (new DateOnly(fyStartYear, 10, 1), new DateOnly(fyStartYear, 12, 31)),
        4 => (new DateOnly(fyStartYear + 1, 1, 1), new DateOnly(fyStartYear + 1, 3, 31)),
        _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be 1..4."),
    };

    /// <summary>Builds Form 26Q for <paramref name="company"/> for quarter <paramref name="quarter"/> (1..4) of the
    /// financial year starting <paramref name="fyStartYear"/>-04-01.</summary>
    public static Form26Q Build(Company company, int fyStartYear, int quarter)
    {
        ArgumentNullException.ThrowIfNull(company);
        var (from, to) = QuarterWindow(fyStartYear, quarter);

        var deductor = BuildDeductor(company);

        // All deductions across the company's whole history (needed for cross-quarter FIFO challan matching),
        // one unit per posted (non-cancelled) TDS-Payable withholding with TDS > 0. Ordered deterministically by
        // (date, voucher order) so FIFO is stable.
        var allUnits = CollectDeductions(company);

        // FIFO-match each section's challans (by deposit date) against its deductions (by deduction date), globally.
        // Each coverage entry carries the actual portion a challan discharged (a challan boundary can split one
        // deduction across two challans), so a split deduction is never double-counted at its full amount.
        var challanCoverage = MatchChallansToDeductions(company, allUnits);

        // The quarter's deductee rows: the deductions whose date falls in [from, to].
        var deductees = allUnits
            .Where(u => u.Date >= from && u.Date <= to)
            .OrderBy(u => u.Date)
            .ThenBy(u => u.Order)
            .Select(u => u.Row)
            .ToList();

        // The quarter's challan blocks: every challan that covers ≥1 deduction in [from, to] (an orphan challan
        // covering no deduction falls back to its own deposit-date quarter so it is never lost).
        var challans = new List<Form26QChallan>();
        foreach (var ch in company.TdsChallans.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
        {
            if (!ChallanIsLive(company, ch.Id)) continue;
            var covered = challanCoverage.TryGetValue(ch.Id, out var list) ? list : new List<Coverage>();

            var inQuarter = covered.Where(cov => cov.Unit.Date >= from && cov.Unit.Date <= to)
                .OrderBy(cov => cov.Unit.Date).ThenBy(cov => cov.Unit.Order).Select(RowFor).ToList();

            bool belongsHere = covered.Count == 0
                ? (ch.DepositDate >= from && ch.DepositDate <= to)   // orphan fallback: deposit-date quarter
                : inQuarter.Count > 0;                               // covered ⇒ attributed by deduction quarter
            if (!belongsHere) continue;

            challans.Add(new Form26QChallan(
                ch.Id, ch.ChallanNo, ch.BsrCode, ch.DepositDate, ch.Amount, ch.Section, ch.MinorHead, inQuarter));
        }

        return new Form26Q(fyStartYear, quarter, from, to, deductor, challans, deductees);
    }

    private static Form26QDeductor BuildDeductor(Company company)
    {
        var cfg = company.Tds;
        if (cfg is null || !cfg.Enabled)
            return new Form26QDeductor(string.Empty, DeductorType.Company, null, null, null, null);
        return new Form26QDeductor(
            cfg.Tan ?? string.Empty, cfg.DeductorType, cfg.ResponsiblePersonName, cfg.ResponsiblePersonPan,
            cfg.ResponsiblePersonDesignation, cfg.ResponsiblePersonAddress);
    }

    /// <summary>One posted TDS withholding, decorated with its deterministic order and its 26Q deductee row, plus a
    /// mutable <see cref="Remaining"/> the FIFO matcher consumes.</summary>
    private sealed class DeductionUnit
    {
        public required DateOnly Date;
        public required int Order;
        public required string Section;
        public required decimal Remaining;   // TDS liability still awaiting a matching challan (FIFO)
        public required Form26QDeducteeRow Row;
    }

    /// <summary>The portion (<see cref="Take"/>) of a <see cref="DeductionUnit"/> a single challan discharged.
    /// <see cref="Opens"/> is true when this is the first challan to touch the deduction, so the full assessable
    /// amount-paid is attributed to that (opening) portion and continuation portions carry zero paid — the TDS
    /// portions still sum to the discharged tax while the amount-paid is never double-counted.</summary>
    private sealed record Coverage(DeductionUnit Unit, decimal Take, bool Opens);

    /// <summary>Projects a coverage entry to the deductee row a challan block should list: the original row (same
    /// reference, for byte-stability) when the challan discharged the deduction in full at first touch; otherwise a
    /// portion row carrying the discharged <see cref="Coverage.Take"/> as its TDS (and the full amount-paid only on
    /// the opening portion) so a boundary-split deduction is counted once at its true portions, not twice at full.</summary>
    private static Form26QDeducteeRow RowFor(Coverage cov)
    {
        var row = cov.Unit.Row;
        if (cov.Opens && cov.Take == row.TdsAmount.Amount)
            return row; // full single-challan coverage — unchanged reference
        return row with
        {
            TdsAmount = new Money(cov.Take),
            AmountPaid = cov.Opens ? row.AmountPaid : Money.Zero,
        };
    }

    /// <summary>Collects every posted, non-cancelled TDS withholding (TDS &gt; 0) into deduction units, ordered by
    /// (date, voucher index, line index) for stable FIFO and byte-stable output.</summary>
    private static List<DeductionUnit> CollectDeductions(Company company)
    {
        var units = new List<DeductionUnit>();
        int order = 0;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) { order++; continue; }
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t) continue;
                if (t.TdsAmount.Amount <= 0m) continue; // below-threshold assessment carries no deposit obligation
                var deductee = company.FindLedger(t.DeducteeLedgerId);
                var nature = company.FindNatureOfPayment(t.NatureId);
                var row = new Form26QDeducteeRow(
                    t.DeducteeLedgerId,
                    deductee?.Name ?? "(unknown)",
                    deductee?.PartyPan,
                    t.SectionCode,
                    nature?.FvuSectionCode ?? t.SectionCode,
                    v.Date,
                    t.AssessableValue,
                    t.TdsAmount,
                    t.RateBasisPoints,
                    t.PanApplied);
                units.Add(new DeductionUnit
                {
                    Date = v.Date, Order = order, Section = t.SectionCode,
                    Remaining = t.TdsAmount.Amount, Row = row,
                });
            }
            order++;
        }
        return units;
    }

    /// <summary>
    /// FIFO-matches each section's challans (ordered by deposit date) against that section's deductions (ordered by
    /// deduction date) over the whole company history, returning, per challan id, the deduction units it discharges.
    /// A challan discharges the earliest still-undeposited deductions of its section up to its amount; this is what
    /// attributes a challan to the <b>deduction</b> quarter rather than its deposit date. Over-deposit (a challan
    /// with no remaining deduction to cover) yields an empty coverage list for the surplus.
    /// </summary>
    private static Dictionary<Guid, List<Coverage>> MatchChallansToDeductions(
        Company company, List<DeductionUnit> allUnits)
    {
        var coverage = new Dictionary<Guid, List<Coverage>>();

        // Deductions per section, in FIFO order (already ordered by (date, order) from CollectDeductions).
        var deductionsBySection = allUnits
            .GroupBy(u => u.Section, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<DeductionUnit>(g), StringComparer.Ordinal);

        var challansBySection = company.TdsChallans
            .Where(c => ChallanIsLive(company, c.Id))
            .GroupBy(c => c.Section, StringComparer.Ordinal);

        foreach (var group in challansBySection)
        {
            if (!deductionsBySection.TryGetValue(group.Key, out var queue))
                queue = new Queue<DeductionUnit>();

            foreach (var ch in group.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
            {
                var covered = new List<Coverage>();
                var remaining = ch.Amount.Amount;
                while (remaining > 0m && queue.Count > 0)
                {
                    var unit = queue.Peek();
                    // Opens ⇒ first challan to touch this deduction (still at its full TDS): the assessable
                    // amount-paid is attributed here so continuation portions don't double-count it.
                    bool opens = unit.Remaining == unit.Row.TdsAmount.Amount;
                    var take = Math.Min(remaining, unit.Remaining);
                    covered.Add(new Coverage(unit, take, opens));
                    unit.Remaining -= take;
                    remaining -= take;
                    if (unit.Remaining <= 0m) queue.Dequeue();
                }
                coverage[ch.Id] = covered;
            }
        }
        return coverage;
    }

    /// <summary>True iff the challan's booking Stat-Payment voucher still exists and is not cancelled — i.e. the
    /// deposit is live on the "TDS Payable" ledger (mirrors <see cref="ChallanReconciliation"/>).</summary>
    private static bool ChallanIsLive(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }
}
