using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The collector (filer) block of a <see cref="Form27EQ"/> return — the TAN, collector legal type and the
/// person-responsible-for-collection identity captured on F11 (<see cref="TcsConfig"/>). Denormalised onto the
/// projection so the FVU writer needs no further company lookup. Mirrors <see cref="Form26QDeductor"/>.
/// </summary>
public sealed record Form27EQCollector(
    string Tan,
    DeductorType CollectorType,
    string? ResponsiblePersonName,
    string? ResponsiblePersonPan,
    string? ResponsiblePersonDesignation,
    string? ResponsiblePersonAddress);

/// <summary>
/// One <b>collectee detail</b> row of Form 27EQ — a single TCS collection on a party (buyer) in the return quarter:
/// the party <see cref="CollecteePan"/> + name, the §206C <see cref="CollectionCode"/> and its Form-27EQ/FVU
/// <see cref="FvuCollectionCode"/>, the <see cref="CollectionDate"/> (the voucher date), the
/// <see cref="AmountReceived"/> (the GST-<b>inclusive</b> assessable base the TCS was assessed on — Circular
/// 17/2020), the <see cref="TcsAmount"/> collected, the applied <see cref="RateBasisPoints"/>, whether the collectee
/// PAN drove the rate (<see cref="PanApplied"/>) and an optional §206C(9) lower-collection certificate
/// <see cref="LowerCollectionReason"/> (absent until modelled). Read verbatim off the posted <see cref="TcsLineTax"/>
/// — never recomputed — so the rows reconcile to the "TCS Payable" credit postings for the quarter by construction.
/// Mirrors <see cref="Form26QDeducteeRow"/> (a withholding deductee), but TCS is <b>additive</b>: it was collected on
/// top of the sale.
/// </summary>
public sealed record Form27EQCollecteeRow(
    Guid CollecteeLedgerId,
    string CollecteeName,
    string? CollecteePan,
    string CollectionCode,
    string FvuCollectionCode,
    DateOnly CollectionDate,
    Money AmountReceived,
    Money TcsAmount,
    int RateBasisPoints,
    bool PanApplied,
    string? LowerCollectionReason = null)
{
    /// <summary>The applied rate as a percentage (e.g. 1.00 for 100 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;
}

/// <summary>
/// One <b>challan block</b> of Form 27EQ — an ITNS-281 deposit (<see cref="TcsChallan"/>) attributed to this return
/// quarter because it covers (discharges) at least one collection that falls in the quarter (period attribution is
/// by the <b>collection</b> quarter, not the challan deposit date — a March collection deposited on 7-Apr belongs to
/// Q4, with the April challan listed there). <see cref="CollecteeRows"/> are the collections this challan discharges
/// whose collection date falls in the quarter, in FIFO order. Mirrors <see cref="Form26QChallan"/>.
/// </summary>
public sealed record Form27EQChallan(
    Guid ChallanId,
    string ChallanNo,
    string BsrCode,
    DateOnly DepositDate,
    Money Amount,
    string CollectionCode,
    string MinorHead,
    IReadOnlyList<Form27EQCollecteeRow> CollecteeRows);

/// <summary>
/// Form-27A-style <b>control totals</b> for a Form 27EQ return (Phase 7 slice 6): the record counts and money totals
/// a filer cross-checks before generating the FVU upload file. <see cref="Validate"/> emulates the FVU control-total
/// checks and returns a human-readable warning/error list — empty when the return tallies. Mirrors
/// <see cref="Form26QControlTotals"/>.
/// </summary>
/// <param name="CollecteeRecordCount">Count of collectee detail rows in the quarter.</param>
/// <param name="ChallanRecordCount">Count of challan blocks in the quarter.</param>
/// <param name="TotalTcsCollected">Σ TCS collected across the collectee rows (the TCS-Payable credit for the quarter).</param>
/// <param name="TotalAmountReceived">Σ assessable amount received/credited across the collectee rows.</param>
/// <param name="TotalDepositedAsPerChallans">Σ deposit amount across the challan blocks attributed to the quarter.</param>
/// <param name="TotalTcsDepositedForQuarter">Σ TCS this quarter's collections were discharged by (via FIFO challan matching).</param>
public sealed record Form27EQControlTotals(
    int CollecteeRecordCount,
    int ChallanRecordCount,
    Money TotalTcsCollected,
    Money TotalAmountReceived,
    Money TotalDepositedAsPerChallans,
    Money TotalTcsDepositedForQuarter)
{
    /// <summary>
    /// Emulates the FVU control-total cross-checks and returns the mismatch messages (empty ⇒ the return tallies):
    /// the TCS collected for the quarter must equal the TCS deposited against this quarter's collections (a positive
    /// gap ⇒ undeposited TCS; a negative gap ⇒ over-deposit); no total may be negative.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (TotalTcsCollected.Amount < 0m)
            problems.Add($"Total TCS collected is negative ({TotalTcsCollected}).");
        if (TotalAmountReceived.Amount < 0m)
            problems.Add($"Total amount received is negative ({TotalAmountReceived}).");

        var gap = TotalTcsCollected.Amount - TotalTcsDepositedForQuarter.Amount;
        if (gap > 0m)
            problems.Add(
                $"TCS collected ({TotalTcsCollected}) exceeds TCS deposited against this quarter's collections " +
                $"({TotalTcsDepositedForQuarter}); {new Money(gap)} is undeposited.");
        else if (gap < 0m)
            problems.Add(
                $"TCS deposited against this quarter's collections ({TotalTcsDepositedForQuarter}) exceeds TCS " +
                $"collected ({TotalTcsCollected}) by {new Money(-gap)}.");

        return problems;
    }

    /// <summary>True iff the control totals tally (no warnings/errors).</summary>
    public bool Tallies => Validate().Count == 0;
}

/// <summary>
/// <b>Form 27EQ</b> — the quarterly TCS return (Phase 7 slice 6; catalog §13) as a pure, read-only projection over
/// the posted <see cref="TcsLineTax"/> collections + recorded <see cref="TcsChallan"/> deposits for a financial year
/// + quarter. The exact mirror of <see cref="Form26Q"/> for the collector's side: it reads the collected tax off
/// each line — never recomputing — so the return reconciles to the "TCS Payable" credit postings for the quarter
/// <b>by construction</b>. Where Form 26Q reports withheld TDS, this reports <b>additive</b> TCS.
/// <para>
/// <b>Period attribution (the cross-FY rule):</b> a <b>collection</b> is attributed to the quarter of its
/// <b>collection (voucher) date</b>. A <b>challan</b> is attributed to the quarter of the collection(s) it covers —
/// resolved by FIFO-matching each collection code's challans (ordered by deposit date) against that code's
/// collections (ordered by collection date), <b>not</b> naively by the challan's own deposit date. So a 20-Mar
/// collection deposited by a 7-Apr challan lands in Q4 (the collection quarter) with the April challan listed there —
/// it is never double-windowed into the next quarter/FY.
/// </para>
/// A non-TCS company (or a quarter with no collection) yields an empty return — no challan, no collectee row, zero
/// totals — and the FVU writer still emits a valid (header-only) file. Deterministic (no clock/RNG); ordered for
/// byte-stability. No UI, no DB.
/// </summary>
public sealed record Form27EQ(
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    Form27EQCollector Collector,
    IReadOnlyList<Form27EQChallan> Challans,
    IReadOnlyList<Form27EQCollecteeRow> Collectees)
{
    /// <summary>The financial-year label (e.g. "2024-25") of <see cref="FinancialYearStartYear"/>.</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>Σ TCS collected across the return's collectee rows (the TCS-Payable credit for the quarter).</summary>
    public Money TotalTcsCollected => new(Collectees.Sum(d => d.TcsAmount.Amount));

    /// <summary>Σ assessable amount received/credited across the return's collectee rows.</summary>
    public Money TotalAmountReceived => new(Collectees.Sum(d => d.AmountReceived.Amount));

    /// <summary>Σ deposit amount across the challan blocks attributed to the quarter.</summary>
    public Money TotalDepositedAsPerChallans => new(Challans.Sum(c => c.Amount.Amount));

    /// <summary>Σ TCS this quarter's collections were discharged by (via FIFO challan matching).</summary>
    public Money TotalTcsDepositedForQuarter => new(Challans.Sum(c => c.CollecteeRows.Sum(r => r.TcsAmount.Amount)));

    /// <summary>The Form-27A-style control totals for the return.</summary>
    public Form27EQControlTotals ControlTotals => new(
        Collectees.Count, Challans.Count, TotalTcsCollected, TotalAmountReceived,
        TotalDepositedAsPerChallans, TotalTcsDepositedForQuarter);

    /// <summary>True iff there is nothing to file this quarter (no collection and no attributed challan).</summary>
    public bool IsEmpty => Collectees.Count == 0 && Challans.Count == 0;

    /// <summary>The inclusive date window of quarter <paramref name="quarter"/> (1..4) of FY starting
    /// <paramref name="fyStartYear"/>-04-01: Q1 Apr-Jun, Q2 Jul-Sep, Q3 Oct-Dec, Q4 Jan-Mar (next calendar year).</summary>
    public static (DateOnly From, DateOnly To) QuarterWindow(int fyStartYear, int quarter) =>
        Form26Q.QuarterWindow(fyStartYear, quarter);

    /// <summary>Builds Form 27EQ for <paramref name="company"/> for quarter <paramref name="quarter"/> (1..4) of the
    /// financial year starting <paramref name="fyStartYear"/>-04-01.</summary>
    public static Form27EQ Build(Company company, int fyStartYear, int quarter)
    {
        ArgumentNullException.ThrowIfNull(company);
        var (from, to) = QuarterWindow(fyStartYear, quarter);

        var collector = BuildCollector(company);

        // All collections across the company's whole history (needed for cross-quarter FIFO challan matching),
        // one unit per posted (non-cancelled) TCS-Payable collection with TCS > 0. Ordered deterministically by
        // (date, voucher order) so FIFO is stable.
        var allUnits = CollectCollections(company);

        // FIFO-match each collection code's challans (by deposit date) against its collections (by collection date),
        // globally. Each coverage entry carries the actual portion a challan discharged (a challan boundary can split
        // one collection across two challans), so a split collection is never double-counted at its full amount.
        var challanCoverage = MatchChallansToCollections(company, allUnits);

        // The quarter's collectee rows: the collections whose date falls in [from, to].
        var collectees = allUnits
            .Where(u => u.Date >= from && u.Date <= to)
            .OrderBy(u => u.Date)
            .ThenBy(u => u.Order)
            .Select(u => u.Row)
            .ToList();

        // The quarter's challan blocks: every challan that covers ≥1 collection in [from, to] (an orphan challan
        // covering no collection falls back to its own deposit-date quarter so it is never lost).
        var challans = new List<Form27EQChallan>();
        foreach (var ch in company.TcsChallans.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
        {
            if (!ChallanIsLive(company, ch.Id)) continue;
            var covered = challanCoverage.TryGetValue(ch.Id, out var list) ? list : new List<Coverage>();

            var inQuarterCoverage = covered.Where(cov => cov.Unit.Date >= from && cov.Unit.Date <= to)
                .OrderBy(cov => cov.Unit.Date).ThenBy(cov => cov.Unit.Order).ToList();
            var inQuarter = inQuarterCoverage.Select(RowFor).ToList();

            bool belongsHere = covered.Count == 0
                ? (ch.DepositDate >= from && ch.DepositDate <= to)   // orphan fallback: deposit-date quarter
                : inQuarter.Count > 0;                               // covered ⇒ attributed by collection quarter
            if (!belongsHere) continue;

            // A challan is attributed to each quarter only for the PORTION of it that discharges THAT quarter's
            // collections. A challan covering collections in more than one quarter must never be listed at its full
            // amount in every covered quarter — that would double-count the same deposit across the quarterly returns
            // and make the CD deposit total disagree with the Σ CL tax within each return (an FVU-rejecting file). Any
            // unmatched surplus (an over-deposit) and a pure orphan challan carry the full amount, attributed once to
            // the challan's own deposit-date quarter, so no deposit is silently dropped.
            decimal inQuarterTake = inQuarterCoverage.Sum(cov => cov.Take);
            decimal surplus = ch.Amount.Amount - covered.Sum(cov => cov.Take);
            bool depositHere = ch.DepositDate >= from && ch.DepositDate <= to;
            var attributedAmount = new Money(inQuarterTake + (depositHere ? surplus : 0m));

            challans.Add(new Form27EQChallan(
                ch.Id, ch.ChallanNo, ch.BsrCode, ch.DepositDate, attributedAmount, ch.CollectionCode, ch.MinorHead, inQuarter));
        }

        return new Form27EQ(fyStartYear, quarter, from, to, collector, challans, collectees);
    }

    private static Form27EQCollector BuildCollector(Company company)
    {
        var cfg = company.Tcs;
        if (cfg is null || !cfg.Enabled)
            return new Form27EQCollector(string.Empty, DeductorType.Company, null, null, null, null);
        return new Form27EQCollector(
            cfg.Tan ?? string.Empty, cfg.CollectorType, cfg.ResponsiblePersonName, cfg.ResponsiblePersonPan,
            cfg.ResponsiblePersonDesignation, cfg.ResponsiblePersonAddress);
    }

    /// <summary>One posted TCS collection, decorated with its deterministic order and its 27EQ collectee row, plus a
    /// mutable <see cref="Remaining"/> the FIFO matcher consumes.</summary>
    private sealed class CollectionUnit
    {
        public required DateOnly Date;
        public required int Order;
        public required string Code;
        public required decimal Remaining;   // TCS liability still awaiting a matching challan (FIFO)
        public required Form27EQCollecteeRow Row;
    }

    /// <summary>The portion (<see cref="Take"/>) of a <see cref="CollectionUnit"/> a single challan discharged.
    /// <see cref="Opens"/> is true when this is the first challan to touch the collection, so the full assessable
    /// amount-received is attributed to that (opening) portion and continuation portions carry zero received — the TCS
    /// portions still sum to the discharged tax while the amount-received is never double-counted.</summary>
    private sealed record Coverage(CollectionUnit Unit, decimal Take, bool Opens);

    /// <summary>Projects a coverage entry to the collectee row a challan block should list: the original row (same
    /// reference, for byte-stability) when the challan discharged the collection in full at first touch; otherwise a
    /// portion row carrying the discharged <see cref="Coverage.Take"/> as its TCS (and the full amount-received only on
    /// the opening portion) so a boundary-split collection is counted once at its true portions, not twice at full.</summary>
    private static Form27EQCollecteeRow RowFor(Coverage cov)
    {
        var row = cov.Unit.Row;
        if (cov.Opens && cov.Take == row.TcsAmount.Amount)
            return row; // full single-challan coverage — unchanged reference
        return row with
        {
            TcsAmount = new Money(cov.Take),
            AmountReceived = cov.Opens ? row.AmountReceived : Money.Zero,
        };
    }

    /// <summary>Collects every posted, non-cancelled TCS collection (TCS &gt; 0) into collection units, ordered by
    /// (date, voucher index, line index) for stable FIFO and byte-stable output.</summary>
    private static List<CollectionUnit> CollectCollections(Company company)
    {
        var units = new List<CollectionUnit>();
        int order = 0;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) { order++; continue; }
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t) continue;
                if (t.TcsAmount.Amount <= 0m) continue; // below-threshold assessment carries no deposit obligation
                var collectee = company.FindLedger(t.CollecteeLedgerId);
                var nature = company.FindNatureOfGoods(t.NatureId);
                var row = new Form27EQCollecteeRow(
                    t.CollecteeLedgerId,
                    collectee?.Name ?? "(unknown)",
                    collectee?.PartyPan,
                    t.CollectionCode,
                    nature?.FvuCode ?? t.CollectionCode,
                    v.Date,
                    t.AssessableValue,
                    t.TcsAmount,
                    t.RateBasisPoints,
                    t.PanApplied);
                units.Add(new CollectionUnit
                {
                    Date = v.Date, Order = order, Code = t.CollectionCode,
                    Remaining = t.TcsAmount.Amount, Row = row,
                });
            }
            order++;
        }
        return units;
    }

    /// <summary>
    /// FIFO-matches each collection code's challans (ordered by deposit date) against that code's collections (ordered
    /// by collection date) over the whole company history, returning, per challan id, the collection units it
    /// discharges. A challan discharges the earliest still-undeposited collections of its code up to its amount; this
    /// is what attributes a challan to the <b>collection</b> quarter rather than its deposit date. Over-deposit (a
    /// challan with no remaining collection to cover) yields an empty coverage list for the surplus.
    /// </summary>
    private static Dictionary<Guid, List<Coverage>> MatchChallansToCollections(
        Company company, List<CollectionUnit> allUnits)
    {
        var coverage = new Dictionary<Guid, List<Coverage>>();

        // Collections per code, in FIFO order (already ordered by (date, order) from CollectCollections).
        var collectionsByCode = allUnits
            .GroupBy(u => u.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<CollectionUnit>(g), StringComparer.Ordinal);

        var challansByCode = company.TcsChallans
            .Where(c => ChallanIsLive(company, c.Id))
            .GroupBy(c => c.CollectionCode, StringComparer.Ordinal);

        foreach (var group in challansByCode)
        {
            if (!collectionsByCode.TryGetValue(group.Key, out var queue))
                queue = new Queue<CollectionUnit>();

            foreach (var ch in group.OrderBy(c => c.DepositDate).ThenBy(c => c.ChallanNo, StringComparer.Ordinal))
            {
                var covered = new List<Coverage>();
                var remaining = ch.Amount.Amount;
                while (remaining > 0m && queue.Count > 0)
                {
                    var unit = queue.Peek();
                    // Opens ⇒ first challan to touch this collection (still at its full TCS): the assessable
                    // amount-received is attributed here so continuation portions don't double-count it.
                    bool opens = unit.Remaining == unit.Row.TcsAmount.Amount;
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
    /// deposit is live on the "TCS Payable" ledger (mirrors <see cref="TcsChallanReconciliation"/>).</summary>
    private static bool ChallanIsLive(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToTcsChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }
}
