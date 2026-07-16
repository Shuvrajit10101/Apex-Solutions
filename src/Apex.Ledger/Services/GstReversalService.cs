using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>ITC-reversal engine</b> (Phase 9 slice 7b; RQ-27; DP-30/DP-31; ER-14; A14-CONFIRMED §11.4/§11.5/§11.7) — the
/// <b>SOLE POSTER</b> (RQ-27) of the reversal candidates S6 surfaced. It (a) <b>consumes</b> the S6 candidates
/// (<see cref="ItcReversalCandidate"/> from <see cref="ItcGateView"/>), (b) <b>computes</b> each rule's amount
/// (Rule 42 D1+D2; Rule 43 60-month tranche; Rule 37/37A + reclaim; §17(5)/ineligible/credit-note), and (c) <b>posts</b>
/// each as a balanced stat-adjustment Journal — <c>Dr "ITC Reversal (Non-creditable)" / Cr Input {head}</c> per head,
/// tagged with the rule's <see cref="GstAdjustmentKind"/> — through the single guarded entry-point
/// <see cref="LedgerService.Post"/> (so <see cref="VoucherValidator"/> guarantees Σ Dr == Σ Cr), reducing the electronic
/// credit ledger and feeding GSTR-3B Table 4(B). Every posting records an idempotent <see cref="ItcReversal"/> row.
/// <para>
/// <b>Nothing auto-posts</b> — every reversal / reclaim is an explicit, user-initiated / period-run action (never a
/// silent side-effect of a report, §0 fact 3). <b>Idempotency</b> (§5.3): the <c>(rule, period, source)</c> key skips a
/// re-run (never a duplicate). <b>Reclaim</b> (Rule 37/37A only) posts once (keyed by <c>reclaim_of_id</c>) and is
/// <b>ECRS-capped</b> — it can never exceed the tracked per-head reversal balance (§11.7). A <c>Section16_2aaNotInPortal</c>
/// candidate is a <b>deferral</b>: it posts NOTHING (it only escalates to Rule 37A at the 30-Nov cut-off, §4.1).
/// </para>
/// <para>
/// <b>ER-14:</b> this service is deliberately named OUTSIDE the S6 advisory prefixes (<c>Gstr2b</c>/<c>Recon</c>/
/// <c>Ims</c>/<c>Itc</c>) — it legitimately posts, so it must not be caught by the S6 structural no-post guard. The S6
/// candidate surface (incl. the <c>Itc*</c> types) stays advisory; S7b is the only poster of what S6 surfaces.
/// </para>
/// </summary>
public sealed class GstReversalService
{
    private readonly Company _company;

    public GstReversalService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>A per-head ITC-reversal amount (integer paisa; each head ≥ 0).</summary>
    public readonly record struct ReversalAmount(long CgstPaisa, long SgstPaisa, long IgstPaisa, long CessPaisa)
    {
        /// <summary>Σ across the four heads, in paisa.</summary>
        public long TotalPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;

        /// <summary>True iff every head is zero (nothing to reverse / reclaim).</summary>
        public bool IsZero => TotalPaisa == 0;

        internal void EnsureNonNegative()
        {
            if (CgstPaisa < 0 || SgstPaisa < 0 || IgstPaisa < 0 || CessPaisa < 0)
                throw new ArgumentException("An ITC-reversal amount must be ≥ 0 paisa.");
        }
    }

    /// <summary>Rule 42 apportionment basis: the common credit C2 per head + exempt (E) and total (F) turnover (paisa).
    /// A14-CONFIRMED §11.4: monthly <b>D1 = (E ÷ F) × C2</b> and <b>D2 = 5% × C2</b> per head; reversal = D1 + D2.</summary>
    public readonly record struct Rule42Basis(ReversalAmount CommonCreditC2, long ExemptTurnoverPaisa, long TotalTurnoverPaisa);

    /// <summary>Rule 43 apportionment basis: the common capital-goods credit Tc per head + exempt (E) / total (F) turnover.
    /// A14-CONFIRMED §11.4: <b>Tm = Tc ÷ 60</b> (monthly tranche); reverse <b>Te = (E ÷ F) × Tm</b> each month for 60 months.</summary>
    public readonly record struct Rule43Basis(ReversalAmount CapitalGoodsCreditTc, long ExemptTurnoverPaisa, long TotalTurnoverPaisa);

    // ==============================================================================================================
    //  Table-4 routing + tag mapping (A14-CONFIRMED §11.5)
    // ==============================================================================================================

    /// <summary>The GSTR-3B Table-4(B) bucket a reversal <b>rule</b> routes to (A14-CONFIRMED §11.5): Rule 37/37A ⇒
    /// 4(B)(2) (reclaimable); Rule 42/43/§17(5)/Ineligible/CreditNote ⇒ 4(B)(1) (non-reclaimable). A reclaim row is
    /// 4(D)(1) (set on the reclaim itself, not derived from the rule).</summary>
    public static Table4bBucket BucketFor(ItcReversalRule rule) => rule switch
    {
        ItcReversalRule.Rule37 or ItcReversalRule.Rule37A => Table4bBucket.Table4B2,
        _ => Table4bBucket.Table4B1,
    };

    private static GstAdjustmentKind AdjustmentFor(ItcReversalRule rule) => rule switch
    {
        ItcReversalRule.Rule37 => GstAdjustmentKind.ReversalRule37,
        ItcReversalRule.Rule37A => GstAdjustmentKind.ReversalRule37A,
        ItcReversalRule.Rule42 => GstAdjustmentKind.ReversalRule42,
        ItcReversalRule.Rule43 => GstAdjustmentKind.ReversalRule43,
        ItcReversalRule.Section17_5 => GstAdjustmentKind.ReversalSection17_5,
        ItcReversalRule.Ineligible => GstAdjustmentKind.ReversalIneligible,
        ItcReversalRule.CreditNote => GstAdjustmentKind.ReversalCreditNote,
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown ITC-reversal rule."),
    };

    private static bool IsReclaimable(ItcReversalRule rule) => rule is ItcReversalRule.Rule37 or ItcReversalRule.Rule37A;

    // ==============================================================================================================
    //  ECRS — the tracked per-head reversal balance (Electronic Credit Reversal & Re-claimed Statement, §11.7)
    // ==============================================================================================================

    /// <summary>
    /// The tracked per-head reversal balance (ECRS, A14-CONFIRMED §11.7): Σ the reclaimable (Rule 37/37A) reversal rows,
    /// netted by their reclaims (<c>reclaim_of_id</c>). A reclaim can never overdraw this — the portal hard-validates a
    /// Table 4(D)(1) reclaim against it, so <see cref="Reclaim"/> rejects an over-reclaim (fail-fast).
    /// </summary>
    public ReversalAmount OutstandingReversalBalance()
    {
        long c = 0, s = 0, i = 0, cess = 0;
        foreach (var r in _company.ItcReversals)
        {
            if (r.ReclaimOfId is not null)
            {
                c -= r.CgstPaisa; s -= r.SgstPaisa; i -= r.IgstPaisa; cess -= r.CessPaisa; // a reclaim draws the balance down
            }
            else if (IsReclaimable(r.Rule))
            {
                c += r.CgstPaisa; s += r.SgstPaisa; i += r.IgstPaisa; cess += r.CessPaisa; // a reclaimable reversal adds to it
            }
        }
        return new ReversalAmount(c, s, i, cess);
    }

    // ==============================================================================================================
    //  Core poster (the single guarded entry-point; idempotent per (rule, period, source))
    // ==============================================================================================================

    /// <summary>
    /// Posts one ITC reversal for <paramref name="amounts"/> as a <b>balanced stat-adjustment Journal</b> —
    /// <c>Dr "ITC Reversal (Non-creditable)"</c> (the whole amount, becoming a cost) / <c>Cr Input {head}</c> per head
    /// (reducing the electronic credit ledger), each Cr leg tagged with the rule's <see cref="GstAdjustmentKind"/> so
    /// the Table 4(B) projection buckets it — through <see cref="LedgerService.Post"/> (Σ Dr == Σ Cr enforced). Records
    /// an idempotent <see cref="ItcReversal"/> row. Returns <c>null</c> for a zero reversal; returns the <b>existing</b>
    /// row (no duplicate) when a row already exists for this <c>(rule, period, source)</c> key (§5.3).
    /// </summary>
    public ItcReversal? PostReversal(ItcReversalRule rule, string period, ReversalAmount amounts, DateOnly date,
        Guid? sourceVoucherId = null, Guid? sourceLineId = null, long? d1BasisPaisa = null, long? d2BasisPaisa = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Reversal period is required.", nameof(period));
        amounts.EnsureNonNegative();
        if (amounts.IsZero) return null; // nothing to reverse (e.g. a no-reversal-declared credit note)

        // Idempotency (§5.3): a re-run for the same (rule, period, source) is NOT re-posted — return the existing row.
        var existing = _company.ItcReversals.FirstOrDefault(r =>
            r.ReclaimOfId is null && r.Rule == rule && r.Period == period
            && r.SourceVoucherId == sourceVoucherId && r.SourceLineId == sourceLineId);
        if (existing is not null) return existing;

        var gst = new GstService(_company);
        var cost = gst.EnsureItcReversalCostLedger();
        var type = new GstSetOffService(_company).EnsureStatAdjustmentType();

        var lines = new List<EntryLine> { new(cost.Id, new Money(amounts.TotalPaisa / 100m), DrCr.Debit) };
        AddInputLegs(gst, lines, amounts, AdjustmentFor(rule), DrCr.Credit);

        var voucher = new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC reversal — {rule} — {period}"));

        var row = new ItcReversal(
            Guid.NewGuid(), rule, period, amounts.CgstPaisa, amounts.SgstPaisa, amounts.IgstPaisa, amounts.CessPaisa,
            d1BasisPaisa, d2BasisPaisa, sourceVoucherId, sourceLineId, voucher.Id,
            reclaimOfId: null, drc03Id: null, BucketFor(rule), createdAt ?? DateTimeOffset.UnixEpoch);
        _company.AddItcReversal(row);
        return row;
    }

    // ==============================================================================================================
    //  Rule 42 (inputs & input services common credit) — D1 + D2, plus the September-following annual true-up
    // ==============================================================================================================

    /// <summary>Posts the monthly Rule-42 reversal (A14-CONFIRMED §11.4): per head <b>D1 = (E ÷ F) × C2</b> +
    /// <b>D2 = 5% × C2</b>, reversal = D1 + D2 ⇒ Table 4(B)(1) (non-reclaimable). Records the ΣD1 / ΣD2 apportionment
    /// basis (audit). Paisa-exact.</summary>
    public ItcReversal? PostRule42(string period, Rule42Basis basis, DateOnly date, DateTimeOffset? createdAt = null)
    {
        var (amount, d1, d2) = Rule42Amount(basis);
        return PostReversal(ItcReversalRule.Rule42, period, amount, date,
            d1BasisPaisa: d1, d2BasisPaisa: d2, createdAt: createdAt);
    }

    /// <summary>
    /// Posts the Rule-42 <b>annual true-up</b> (A14-CONFIRMED §11.4): the full-year reversal recomputed on the full-year
    /// E ÷ F (by the September-following return), <b>minus</b> the Rule-42 monthly rows already posted <b>for THIS FY</b>
    /// — i.e. the <b>signed</b> per-head delta. A positive delta is an additional reversal (Table 4(B)(1)); a NEGATIVE
    /// delta (the monthly rows over-reversed) is <b>re-credited</b> under Rule 42(2)(b) — <c>Dr Input {head} / Cr cost</c>
    /// routed into net ITC (Table 4(D)(1) / 4(A)(5)), NOT forfeited (S7b FIX 3). The monthly accumulation is scoped to
    /// ONLY the <c>yyyy-MM</c> months inside the FY identified by <paramref name="fyPeriod"/> (Apr(y)..Mar(y+1)) and
    /// EXCLUDES every true-up row (any row whose period is an FY string) — a cross-FY double-count no longer floors a
    /// later year's delta to zero (S7b FIX 2). Keyed to the FY period so a re-run is idempotent. Returns <c>null</c> when
    /// the year was already fully trued-up (zero delta).
    /// </summary>
    public ItcReversal? PostRule42AnnualTrueUp(string fyPeriod, Rule42Basis fullYearBasis, DateOnly date,
        DateTimeOffset? createdAt = null)
    {
        var (fullYear, d1, d2) = Rule42Amount(fullYearBasis);

        // Σ ONLY this FY's monthly (yyyy-MM) Rule-42 reversals — never another FY's months, never any true-up (FY-period)
        // row. FIX 2: the old scope subtracted every Rule-42 row ever posted, so a later FY's delta floored to zero.
        long mc = 0, ms = 0, mi = 0, mcess = 0;
        foreach (var r in _company.ItcReversals)
        {
            if (r.Rule != ItcReversalRule.Rule42 || r.ReclaimOfId is not null) continue;
            if (!IsMonthlyPeriodWithinFy(r.Period, fyPeriod)) continue;
            mc += r.CgstPaisa; ms += r.SgstPaisa; mi += r.IgstPaisa; mcess += r.CessPaisa;
        }

        // The SIGNED per-head delta (NOT floored) — FIX 3: a negative head is an over-reversal to re-credit, not forfeit.
        return PostRule42TrueUp(fyPeriod,
            fullYear.CgstPaisa - mc, fullYear.SgstPaisa - ms, fullYear.IgstPaisa - mi, fullYear.CessPaisa - mcess,
            d1, d2, date, createdAt);
    }

    /// <summary>
    /// Posts one Rule-42 annual true-up row from the <b>signed</b> per-head delta. Per head: a positive delta reverses
    /// more (<c>Cr Input {head}</c>, tagged Rule 42 ⇒ Table 4(B)(1)); a negative delta re-credits the over-reversal
    /// (<c>Dr Input {head}</c>, tagged Reclaim ⇒ Table 4(D)(1) / net ITC 4(A)(5), Rule 42(2)(b)). The reversal-cost ledger
    /// balances the net (a Dr for a net reversal, a Cr for a net re-credit; no cost leg when the two sides net to zero).
    /// One row per <c>(Rule42, fyPeriod)</c> — idempotent; records the abs per-head magnitudes + the FY apportionment
    /// basis. Returns <c>null</c> for an all-zero delta (already fully trued-up).
    /// </summary>
    private ItcReversal? PostRule42TrueUp(string fyPeriod, long dc, long ds, long di, long dcess,
        long d1BasisPaisa, long d2BasisPaisa, DateOnly date, DateTimeOffset? createdAt)
    {
        // Idempotency (§5.3): one true-up row per (Rule42, fyPeriod, no source) — a re-run returns the existing row.
        var existing = _company.ItcReversals.FirstOrDefault(r =>
            r.ReclaimOfId is null && r.Rule == ItcReversalRule.Rule42 && r.Period == fyPeriod
            && r.SourceVoucherId is null && r.SourceLineId is null);
        if (existing is not null) return existing;

        if (dc == 0 && ds == 0 && di == 0 && dcess == 0) return null; // already fully trued-up (zero delta)

        var gst = new GstService(_company);
        var cost = gst.EnsureItcReversalCostLedger();
        var type = new GstSetOffService(_company).EnsureStatAdjustmentType();

        var lines = new List<EntryLine>();
        long netCost = 0; // Σ signed delta: > 0 ⇒ Dr cost (a net reversal becomes cost); < 0 ⇒ Cr cost (un-does cost)

        void Head(GstTaxHead head, long delta)
        {
            if (delta == 0) return;
            if (head == GstTaxHead.Cess) gst.EnsureCessLedgers();
            var input = gst.FindTaxLedger(head, GstTaxDirection.Input)
                ?? throw new InvalidOperationException(
                    $"Input {head} ledger not found — enable GST first (EnableGst auto-creates it).");
            if (delta > 0)
                // Additional reversal — Cr Input {head} (reduce the credit pool), tagged Rule 42 ⇒ Table 4(B)(1).
                lines.Add(new EntryLine(input.Id, new Money(delta / 100m), DrCr.Credit,
                    gst: new GstLineTax(head, 0, Money.Zero, adjustment: GstAdjustmentKind.ReversalRule42)));
            else
                // Over-reversal correction (Rule 42(2)(b)) — Dr Input {head} (restore the credit pool), tagged Reclaim ⇒
                // Table 4(D)(1) / net ITC 4(A)(5). Never forfeited.
                lines.Add(new EntryLine(input.Id, new Money(-delta / 100m), DrCr.Debit,
                    gst: new GstLineTax(head, 0, Money.Zero, adjustment: GstAdjustmentKind.Reclaim)));
            netCost += delta;
        }

        Head(GstTaxHead.Central, dc);
        Head(GstTaxHead.State, ds);
        Head(GstTaxHead.Integrated, di);
        Head(GstTaxHead.Cess, dcess);

        if (netCost > 0) lines.Add(new EntryLine(cost.Id, new Money(netCost / 100m), DrCr.Debit));
        else if (netCost < 0) lines.Add(new EntryLine(cost.Id, new Money(-netCost / 100m), DrCr.Credit));
        // netCost == 0 ⇒ the per-head Cr and Dr Input legs already balance; no cost leg needed.

        var voucher = new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC reversal — Rule42 annual true-up — {fyPeriod}"));

        var bucket = netCost >= 0 ? Table4bBucket.Table4B1 : Table4bBucket.Table4D1;
        var row = new ItcReversal(
            Guid.NewGuid(), ItcReversalRule.Rule42, fyPeriod,
            Math.Abs(dc), Math.Abs(ds), Math.Abs(di), Math.Abs(dcess),
            d1BasisPaisa, d2BasisPaisa, sourceVoucherId: null, sourceLineId: null, voucher.Id,
            reclaimOfId: null, drc03Id: null, bucket, createdAt ?? DateTimeOffset.UnixEpoch);
        _company.AddItcReversal(row);
        return row;
    }

    /// <summary>True iff <paramref name="period"/> is a <c>yyyy-MM</c> month falling inside the financial year identified
    /// by <paramref name="fyPeriod"/> (Apr(y)..Mar(y+1)). An FY-string period (a true-up row, e.g. "2025-26" whose tail
    /// is > 12) is NOT a month ⇒ excluded — so a true-up never counts itself or another FY's monthly rows (FIX 2).</summary>
    private static bool IsMonthlyPeriodWithinFy(string period, string fyPeriod)
    {
        if (!TryParseMonth(period, out var year, out var month)) return false;
        if (!TryParseFyStartYear(fyPeriod, out var fyStart)) return false;
        var idx = year * 12 + month;                 // month index (month ∈ 1..12)
        var start = fyStart * 12 + 4;                // April of the FY start year
        var end = (fyStart + 1) * 12 + 3;            // March of the following year
        return idx >= start && idx <= end;
    }

    /// <summary>Parses a <c>yyyy-MM</c> period into (year, month) with a valid month 1..12. Any other shape — including an
    /// FY string like <c>"2025-26"</c> (tail 26 > 12) — returns false, which is exactly what excludes true-up rows.</summary>
    private static bool TryParseMonth(string period, out int year, out int month)
    {
        year = 0; month = 0;
        if (string.IsNullOrWhiteSpace(period)) return false;
        var parts = period.Split('-');
        if (parts.Length != 2 || parts[0].Length != 4) return false;
        return int.TryParse(parts[0], out year) && int.TryParse(parts[1], out month) && month is >= 1 and <= 12;
    }

    /// <summary>Parses the 4-digit FY start year from an FY period string (the part before the first <c>-</c>, e.g.
    /// <c>"2025-26"</c> ⇒ 2025).</summary>
    private static bool TryParseFyStartYear(string fyPeriod, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(fyPeriod)) return false;
        var head = fyPeriod.Split('-')[0];
        return head.Length == 4 && int.TryParse(head, out year);
    }

    private static (ReversalAmount Amount, long D1, long D2) Rule42Amount(Rule42Basis basis)
    {
        if (basis.TotalTurnoverPaisa <= 0)
            throw new ArgumentException("Rule 42 total turnover (F) must be > 0.", nameof(basis));
        if (basis.ExemptTurnoverPaisa < 0 || basis.ExemptTurnoverPaisa > basis.TotalTurnoverPaisa)
            throw new ArgumentException("Rule 42 exempt turnover (E) must be within [0, F].", nameof(basis));

        long D1(long c2) => (long)Math.Round(
            (decimal)c2 * basis.ExemptTurnoverPaisa / basis.TotalTurnoverPaisa, MidpointRounding.AwayFromZero);
        long D2(long c2) => (long)Math.Round((decimal)c2 * 5m / 100m, MidpointRounding.AwayFromZero);

        var c2 = basis.CommonCreditC2;
        c2.EnsureNonNegative();
        long d1c = D1(c2.CgstPaisa), d1s = D1(c2.SgstPaisa), d1i = D1(c2.IgstPaisa), d1cess = D1(c2.CessPaisa);
        long d2c = D2(c2.CgstPaisa), d2s = D2(c2.SgstPaisa), d2i = D2(c2.IgstPaisa), d2cess = D2(c2.CessPaisa);
        var amount = new ReversalAmount(d1c + d2c, d1s + d2s, d1i + d2i, d1cess + d2cess);
        return (amount, d1c + d1s + d1i + d1cess, d2c + d2s + d2i + d2cess);
    }

    // ==============================================================================================================
    //  Rule 43 (capital-goods common credit) — 60-month tranche
    // ==============================================================================================================

    /// <summary>Posts one month's Rule-43 tranche (A14-CONFIRMED §11.4): per head <b>Tm = Tc ÷ 60</b>, reverse
    /// <b>Te = (E ÷ F) × Tm</b> ⇒ Table 4(B)(1). Keyed to <paramref name="capitalGoodVoucherId"/> + <paramref name="period"/>
    /// so the 60-month schedule posts <b>once per (asset, month)</b> (idempotent re-run returns the existing row).</summary>
    public ItcReversal? PostRule43(string period, Guid capitalGoodVoucherId, Rule43Basis basis, DateOnly date,
        DateTimeOffset? createdAt = null)
    {
        if (basis.TotalTurnoverPaisa <= 0)
            throw new ArgumentException("Rule 43 total turnover (F) must be > 0.", nameof(basis));
        if (basis.ExemptTurnoverPaisa < 0 || basis.ExemptTurnoverPaisa > basis.TotalTurnoverPaisa)
            throw new ArgumentException("Rule 43 exempt turnover (E) must be within [0, F].", nameof(basis));

        long Te(long tc)
        {
            var tm = (decimal)tc / 60m; // the monthly tranche Tm
            return (long)Math.Round(tm * basis.ExemptTurnoverPaisa / basis.TotalTurnoverPaisa, MidpointRounding.AwayFromZero);
        }

        var tc = basis.CapitalGoodsCreditTc;
        tc.EnsureNonNegative();
        var amount = new ReversalAmount(Te(tc.CgstPaisa), Te(tc.SgstPaisa), Te(tc.IgstPaisa), Te(tc.CessPaisa));
        return PostReversal(ItcReversalRule.Rule43, period, amount, date,
            sourceVoucherId: capitalGoodVoucherId, createdAt: createdAt);
    }

    // ==============================================================================================================
    //  Rule 37 / 37A (reclaimable) + the reclaim path
    // ==============================================================================================================

    /// <summary>Posts a Rule-37 (180-day non-payment) reversal of the ITC availed on <paramref name="sourceVoucherId"/>
    /// ⇒ Table 4(B)(2) (reclaimable; reclaim on later payment, no time bar). <paramref name="amounts"/> defaults to the
    /// full forward ITC posted on that purchase. §50 interest is flag-only (DP-34; 18% if surfaced, §11.6).</summary>
    public ItcReversal PostRule37(Guid sourceVoucherId, string period, DateOnly date, ReversalAmount? amounts = null,
        DateTimeOffset? createdAt = null)
        => PostStatutory(ItcReversalRule.Rule37, sourceVoucherId, period, date, amounts, createdAt);

    /// <summary>Posts a Rule-37A (supplier had not filed GSTR-3B by 30-Sep following the FY) reversal ⇒ Table 4(B)(2)
    /// (the recipient reverses by 30-Nov; reclaim when the supplier subsequently files). <paramref name="amounts"/>
    /// defaults to the full forward ITC posted on the purchase.</summary>
    public ItcReversal PostRule37A(Guid sourceVoucherId, string period, DateOnly date, ReversalAmount? amounts = null,
        DateTimeOffset? createdAt = null)
        => PostStatutory(ItcReversalRule.Rule37A, sourceVoucherId, period, date, amounts, createdAt);

    private ItcReversal PostStatutory(ItcReversalRule rule, Guid sourceVoucherId, string period, DateOnly date,
        ReversalAmount? amounts, DateTimeOffset? createdAt)
    {
        if (sourceVoucherId == Guid.Empty)
            throw new ArgumentException("A source voucher is required for a Rule 37 / 37A reversal.", nameof(sourceVoucherId));
        var voucher = _company.FindVoucher(sourceVoucherId)
            ?? throw new InvalidOperationException($"Source voucher {sourceVoucherId} not found.");
        var amount = amounts ?? ForwardInputTaxOf(voucher);
        if (amount.IsZero)
            throw new InvalidOperationException($"Source voucher {sourceVoucherId} carries no forward ITC to reverse.");
        return PostReversal(rule, period, amount, date, sourceVoucherId: sourceVoucherId, createdAt: createdAt)
            ?? throw new InvalidOperationException("A non-zero reversal produced no posting.");
    }

    /// <summary>
    /// Re-avails (reclaims) an earlier <b>Rule 37 / 37A</b> reversal <paramref name="reversalId"/> — a linked
    /// <c>Dr Input {head} / Cr "ITC Reversal (Non-creditable)"</c> Journal (tagged <see cref="GstAdjustmentKind.Reclaim"/>)
    /// that restores the credit pool and routes to Table 4(D)(1). Guards: the target must be a reclaimable reversal (not
    /// a reclaim, not 4(B)(1)); a reversal reclaims <b>once</b> (keyed by <c>reclaim_of_id</c>); and the reclaim is
    /// <b>ECRS-capped</b> — it can never exceed the tracked per-head reversal balance (§11.7). <paramref name="amounts"/>
    /// defaults to the full reversed amount.
    /// </summary>
    public ItcReversal Reclaim(Guid reversalId, string period, DateOnly date, ReversalAmount? amounts = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Reclaim period is required.", nameof(period));
        var reversal = _company.FindItcReversal(reversalId)
            ?? throw new InvalidOperationException($"ITC reversal {reversalId} not found — nothing to reclaim.");
        if (reversal.ReclaimOfId is not null)
            throw new InvalidOperationException("Cannot reclaim a reclaim row — reclaim the original reversal.");
        if (!IsReclaimable(reversal.Rule))
            throw new InvalidOperationException(
                $"{reversal.Rule} is a non-reclaimable reversal (Table 4(B)(1)) — only Rule 37 / 37A can be reclaimed (§11.5).");
        if (_company.ItcReversals.Any(r => r.ReclaimOfId == reversalId))
            throw new InvalidOperationException($"ITC reversal {reversalId} has already been reclaimed (a reclaim posts once).");

        var amount = amounts ?? new ReversalAmount(reversal.CgstPaisa, reversal.SgstPaisa, reversal.IgstPaisa, reversal.CessPaisa);
        amount.EnsureNonNegative();
        if (amount.IsZero) throw new ArgumentException("A reclaim must re-avail a positive amount.", nameof(amounts));

        // ECRS (§11.7): a Table 4(D)(1) reclaim can never exceed the tracked per-head reversal balance.
        var balance = OutstandingReversalBalance();
        if (amount.CgstPaisa > balance.CgstPaisa || amount.SgstPaisa > balance.SgstPaisa
            || amount.IgstPaisa > balance.IgstPaisa || amount.CessPaisa > balance.CessPaisa)
            throw new InvalidOperationException(
                "ECRS: a reclaim cannot exceed the tracked reversal balance (Electronic Credit Reversal & Re-claimed Statement, §11.7).");

        var gst = new GstService(_company);
        var cost = gst.EnsureItcReversalCostLedger();
        var type = new GstSetOffService(_company).EnsureStatAdjustmentType();

        // Dr Input {head} (restore the credit pool), tagged Reclaim / Cr the reversal-cost ledger (the re-availment
        // un-does the cost). Balanced by construction; posted through the single guarded entry-point.
        var lines = new List<EntryLine>();
        AddInputLegs(gst, lines, amount, GstAdjustmentKind.Reclaim, DrCr.Debit);
        lines.Add(new EntryLine(cost.Id, new Money(amount.TotalPaisa / 100m), DrCr.Credit));

        var voucher = new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC re-availment (reclaim) — {reversal.Rule} — {period}"));

        var row = new ItcReversal(
            Guid.NewGuid(), reversal.Rule, period, amount.CgstPaisa, amount.SgstPaisa, amount.IgstPaisa, amount.CessPaisa,
            d1BasisPaisa: null, d2BasisPaisa: null, reversal.SourceVoucherId, reversal.SourceLineId, voucher.Id,
            reclaimOfId: reversalId, drc03Id: null, Table4bBucket.Table4D1, createdAt ?? DateTimeOffset.UnixEpoch);
        _company.AddItcReversal(row);
        return row;
    }

    // ==============================================================================================================
    //  Consuming the S6 candidate surface (ItcGateView.ReversalCandidates)
    // ==============================================================================================================

    /// <summary>
    /// Posts the reversal for one S6-surfaced <see cref="ItcReversalCandidate"/> (§4.1) <b>head-for-head</b> from the
    /// candidate's exact per-head breakdown (<see cref="ItcReversalCandidate.CgstPaisa"/> etc.) — which
    /// <see cref="ItcGateView"/> computed from the SAME per-line/per-head amounts that formed its
    /// <see cref="ItcReversalCandidate.SuggestedReversal"/>. There is <b>no weight-based re-split</b>, so a cess-excluded
    /// GST total can never bleed into the cess head (S7b FIX 1), and a mixed-voucher blocked / ineligible / CN subset is
    /// exact. The candidate's ring-fenced <see cref="ItcReversalCandidate.CessPaisa"/> (a blocked / ineligible item's own
    /// blocked cess ITC, or a credit note's proportional cess) posts as its OWN cess leg (ER-2). Routing:
    /// <c>Section17_5Blocked</c> ⇒ §17(5) → 4(B)(1); <c>Ineligible</c> ⇒ Ineligible → 4(B)(1); <c>ImsAcceptedCreditNote</c>
    /// ⇒ CreditNote → 4(B)(1). A <c>Section16_2aaNotInPortal</c> candidate is a <b>deferral</b> — posts NOTHING (returns
    /// <c>null</c>). A zero-amount candidate (a no-reversal-declared credit note) also posts nothing.
    /// </summary>
    public ItcReversal? PostFromCandidate(ItcReversalCandidate candidate, string period, DateOnly date,
        DateTimeOffset? createdAt = null)
    {
        if (candidate.Reason == ItcReversalReason.Section16_2aaNotInPortal)
            return null; // DEFERRAL — a §16(2)(aa) hold posts nothing; it only escalates to Rule 37A at 30-Nov (§4.1).

        var rule = candidate.Reason switch
        {
            ItcReversalReason.Section17_5Blocked => ItcReversalRule.Section17_5,
            ItcReversalReason.Ineligible => ItcReversalRule.Ineligible,
            ItcReversalReason.ImsAcceptedCreditNote => ItcReversalRule.CreditNote,
            _ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Reason, "Unknown reversal-candidate reason."),
        };

        // Head-for-head from the candidate's per-head profile — NOT re-split from a total (the cess ring-fence, ER-2,
        // is exact: the GST heads and the cess head are each reversed at their own amount).
        var amounts = new ReversalAmount(
            candidate.CgstPaisa, candidate.SgstPaisa, candidate.IgstPaisa, candidate.CessPaisa);
        amounts.EnsureNonNegative();
        if (amounts.IsZero) return null; // e.g. a no-reversal-declared credit note (nothing to reverse)

        return PostReversal(rule, period, amounts, date,
            sourceVoucherId: candidate.VoucherId, sourceLineId: candidate.LineId, createdAt: createdAt);
    }

    // ==============================================================================================================
    //  Shared helpers
    // ==============================================================================================================

    /// <summary>Adds one <c>Input {head}</c> leg per non-zero head (on <paramref name="side"/>, tagged
    /// <paramref name="adjustment"/>) to <paramref name="lines"/>; a Cr leg reverses the credit pool, a Dr leg (reclaim)
    /// restores it. The cess ring-fence (ER-2) is respected — a cess leg is its own head, and its ledger is lazily
    /// ensured. Every amount is paisa-exact.</summary>
    private static void AddInputLegs(GstService gst, List<EntryLine> lines, ReversalAmount amounts,
        GstAdjustmentKind adjustment, DrCr side)
    {
        void Leg(GstTaxHead head, long paisa)
        {
            if (paisa <= 0) return;
            if (head == GstTaxHead.Cess) gst.EnsureCessLedgers();
            var input = gst.FindTaxLedger(head, GstTaxDirection.Input)
                ?? throw new InvalidOperationException(
                    $"Input {head} ledger not found — enable GST first (EnableGst auto-creates it).");
            lines.Add(new EntryLine(input.Id, new Money(paisa / 100m), side,
                gst: new GstLineTax(head, 0, Money.Zero, adjustment: adjustment)));
        }

        Leg(GstTaxHead.Central, amounts.CgstPaisa);
        Leg(GstTaxHead.State, amounts.SgstPaisa);
        Leg(GstTaxHead.Integrated, amounts.IgstPaisa);
        Leg(GstTaxHead.Cess, amounts.CessPaisa);
    }

    /// <summary>The per-head <b>forward</b> (non-RCM, non-adjustment) input tax posted on a voucher — the ITC actually
    /// availed on that purchase, which a Rule 37/37A/§17(5) reversal reverses. RCM ITC (its own 4A bucket) and any
    /// adjustment-tagged line are excluded.</summary>
    private static ReversalAmount ForwardInputTaxOf(Voucher voucher)
    {
        long c = 0, s = 0, i = 0, cess = 0;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge || g.Adjustment is not null) continue;
            var paisa = ToPaisa(line.Amount);
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: c += paisa; break;
                case GstTaxHead.State: s += paisa; break;
                case GstTaxHead.Integrated: i += paisa; break;
                case GstTaxHead.Cess: cess += paisa; break;
            }
        }
        return new ReversalAmount(c, s, i, cess);
    }

    private static long ToPaisa(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);
}
