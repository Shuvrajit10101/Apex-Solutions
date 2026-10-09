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
    //  Registration attribution (census 6.23) — which GSTIN a reversal belongs to
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>The ONE rule for which registration a reversal belongs to, so that no caller has to remember it.</b>
    /// A reversal that names a source document belongs to the registration <b>that document</b> is recorded under —
    /// a fact, read from the books, which cannot be got wrong by picking the wrong entry in a combo box. There are
    /// <b>two</b> kinds of source document and both are now read: a <paramref name="sourceVoucherId"/> (every
    /// Rule 37 / 37A / 43 reversal and every voucher-keyed candidate) gives the registration its purchase is recorded
    /// under, and a <paramref name="sourceLineId"/> (the 2B-line-keyed credit-note candidate, which has no voucher in
    /// our books) gives the registration the GSTR-2B statement carrying that line was issued to — see
    /// <see cref="RegistrationOfPortalLine"/>, which is where the duplicate-reversal blocker was closed. Only the
    /// unanchored rules (Rule 42 and its annual true-up, which reverse an apportioned pool rather than one purchase's
    /// credit) have no document to read, and for those the caller's <paramref name="registrationId"/> is used — as it
    /// still is when a portal line names a GSTIN this company does not hold. Everything falls back to
    /// <see cref="GstRegistration.PrimaryId"/> — never to <c>null</c> — so the attribution is total and a
    /// single-registration book always resolves to the one registration it has (ER-13).
    /// </summary>
    private Guid ScopeFor(Guid? sourceVoucherId, Guid? sourceLineId, Guid? registrationId) =>
        sourceVoucherId is { } sv && _company.FindVoucher(sv) is { } source
            ? GstReportSupport.RegistrationOf(source)
            : sourceLineId is { } sl && RegistrationOfPortalLine(sl) is { } portalOwner
                ? portalOwner
                : registrationId ?? GstRegistration.PrimaryId;

    /// <summary>
    /// 🔴 <b>The registration a reversal keyed to a PORTAL LINE belongs to — the second half of the same "read it off
    /// the document" rule, and the one that was missing.</b> A credit-note candidate from GSTR-2B carries no source
    /// voucher (the note is the supplier's, not in our books), so <see cref="ScopeFor"/> fell through to the caller's
    /// <c>registrationId</c> — and the caller is a combo box. Because the registration is (correctly) part of the
    /// idempotency key, the SAME portal credit note could then be posted ONCE PER REGISTRATION: measured at
    /// ₹12,000.00 of reversal across two filed returns against a ₹6,000.00 note, two real stat-adjustment vouchers
    /// reducing credit twice.
    /// <para>The fix makes it a fact like the voucher case: the line belongs to the 2B snapshot that carries it, that
    /// statement was "made available to <b>the registered person</b>" (CGST Rules, rule 60(7)) whose GSTIN it names,
    /// and so the reversal belongs to that registration — whatever the screen had selected. The second post then
    /// resolves to the SAME scope, the idempotency key matches, and the existing row is returned instead of a second
    /// one being written.</para>
    /// <para>Returns <c>null</c> when the line belongs to no imported snapshot, or to one whose recipient GSTIN names
    /// no registration this company holds — then the caller's scope still decides, so a single-registration book and
    /// every existing fixture resolve exactly as before (ER-13).</para>
    /// </summary>
    private Guid? RegistrationOfPortalLine(Guid sourceLineId)
    {
        foreach (var snapshot in _company.Gstr2bSnapshots)
        {
            var carries = false;
            foreach (var line in snapshot.Lines)
                if (line.Id == sourceLineId) { carries = true; break; }
            if (!carries) continue;

            if (_company.Gst is not { } gst) return null;
            foreach (var registration in gst.AllRegistrations)
                if (!string.IsNullOrWhiteSpace(registration.Gstin)
                    && string.Equals(registration.Gstin, snapshot.RecipientGstin, StringComparison.OrdinalIgnoreCase))
                    return registration.Id;
            return null;
        }
        return null;
    }

    /// <summary>Stamps <paramref name="scope"/> onto a reversal/reclaim voucher before it is posted — but
    /// <b>only</b> when it is not the primary, so a single-registration book keeps storing the same <c>null</c>
    /// it has always stored and its exports stay byte-identical (ER-13;
    /// <see cref="GstReportSupport.RegistrationOf"/> reads a null as the primary either way).</summary>
    private static Voucher Stamp(Voucher voucher, Guid scope)
    {
        if (scope != GstRegistration.PrimaryId) voucher.GstRegistrationId = scope;
        return voucher;
    }

    /// <summary>The registration a posted <see cref="ItcReversal"/> row belongs to — read from its own
    /// stat-adjustment voucher, which is where the attribution lives (no column on the row, so no migration). A row
    /// whose voucher has gone reads as the primary, the same normalisation
    /// <see cref="GstReportSupport.RegistrationOf"/> applies to a null.</summary>
    private Guid RegistrationOfRow(ItcReversal row) =>
        _company.FindVoucher(row.ReversalVoucherId) is { } v
            ? GstReportSupport.RegistrationOf(v)
            : GstRegistration.PrimaryId;

    // ==============================================================================================================
    //  ECRS — the tracked per-head reversal balance (Electronic Credit Reversal & Re-claimed Statement, §11.7)
    // ==============================================================================================================

    /// <summary>
    /// The tracked per-head reversal balance (ECRS, A14-CONFIRMED §11.7): Σ the reclaimable (Rule 37/37A) reversal rows,
    /// netted by their reclaims (<c>reclaim_of_id</c>). A reclaim can never overdraw this — the portal hard-validates a
    /// Table 4(D)(1) reclaim against it, so <see cref="Reclaim"/> rejects an over-reclaim (fail-fast).
    ///
    /// <para>🔴 <b>Scoped by <paramref name="registrationId"/> (census 6.23).</b> The ECRS is a portal statement per
    /// GSTIN, so a multi-registration book has one balance per registration, not one for the book. A screen that
    /// shows a registration-scoped candidate list beside a whole-book balance puts two populations side by side
    /// with nothing saying so. <c>null</c> ⇒ the whole book, which on a single-registration book IS the one
    /// registration, so that book is byte-identical (ER-13).</para>
    /// </summary>
    public ReversalAmount OutstandingReversalBalance(Guid? registrationId = null)
    {
        long c = 0, s = 0, i = 0, cess = 0;
        foreach (var r in _company.ItcReversals)
        {
            if (registrationId is { } reg && RegistrationOfRow(r) != reg) continue;
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
    /// row (no duplicate) when a row already exists for this <c>(rule, period, source, registration)</c> key (§5.3).
    ///
    /// <para>🔴 <b>THE POSTED ENTRY NAMES ITS REGISTRATION (census 6.23).</b> A reversal reduces the electronic
    /// credit ledger of exactly ONE GSTIN and lands in exactly one return's Table 4(B), so the stat-adjustment
    /// voucher is stamped with the registration it belongs to — otherwise it attributed to the primary by default,
    /// which on a multi-registration book is <b>a wrong posted entry AND a wrong filed figure on two returns at
    /// once</b> (the branch's reversal missing from its own 3B and showing on the primary's).
    /// <b>The registration is a fact wherever one is available, not a choice:</b> when the reversal names a
    /// <paramref name="sourceVoucherId"/> — every Rule 37 / 37A / 43 reversal and every voucher-keyed candidate —
    /// it is the registration THAT PURCHASE is recorded under; when it names a <paramref name="sourceLineId"/>
    /// instead — the 2B-line-keyed credit-note candidate — it is the registration the GSTR-2B statement carrying
    /// that line was issued to; and <paramref name="registrationId"/> is only consulted for the unanchored rules
    /// (Rule 42 and its true-up) that have no source document to read it from.
    /// The stamp is written only when it differs from <see cref="GstRegistration.PrimaryId"/>, so a
    /// single-registration book stores the same <c>null</c> it always stored (ER-13).</para>
    /// </summary>
    public ItcReversal? PostReversal(ItcReversalRule rule, string period, ReversalAmount amounts, DateOnly date,
        Guid? sourceVoucherId = null, Guid? sourceLineId = null, long? d1BasisPaisa = null, long? d2BasisPaisa = null,
        DateTimeOffset? createdAt = null, Guid? registrationId = null)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Reversal period is required.", nameof(period));
        amounts.EnsureNonNegative();
        if (amounts.IsZero) return null; // nothing to reverse (e.g. a no-reversal-declared credit note)

        var scope = ScopeFor(sourceVoucherId, sourceLineId, registrationId);

        // Idempotency (§5.3): a re-run for the same (rule, period, source, REGISTRATION) is NOT re-posted — return
        // the existing row. The registration is part of the key because two registrations of one book each file
        // their OWN Rule-42 apportionment for the same period off the same (null) source; without it the second
        // registration's reversal was silently swallowed by the first's row and never posted at all.
        var existing = _company.ItcReversals.FirstOrDefault(r =>
            r.ReclaimOfId is null && r.Rule == rule && r.Period == period
            && r.SourceVoucherId == sourceVoucherId && r.SourceLineId == sourceLineId
            && RegistrationOfRow(r) == scope);
        if (existing is not null) return existing;

        var gst = new GstService(_company);
        var cost = gst.EnsureItcReversalCostLedger();
        var type = new GstSetOffService(_company).EnsureStatAdjustmentType();

        var lines = new List<EntryLine> { new(cost.Id, new Money(amounts.TotalPaisa / 100m), DrCr.Debit) };
        AddInputLegs(gst, lines, amounts, AdjustmentFor(rule), DrCr.Credit);

        var voucher = new LedgerService(_company).Post(Stamp(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC reversal — {rule} — {period}"), scope));

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
    /// basis (audit). Paisa-exact.
    /// <para>Rule 42 apportions a POOL, not one purchase's credit, so it is one of the only two reversals with no
    /// source document to read its registration from — hence <paramref name="registrationId"/> (census 6.23).</para></summary>
    public ItcReversal? PostRule42(string period, Rule42Basis basis, DateOnly date, DateTimeOffset? createdAt = null,
        Guid? registrationId = null)
    {
        var (amount, d1, d2) = Rule42Amount(basis);
        return PostReversal(ItcReversalRule.Rule42, period, amount, date,
            d1BasisPaisa: d1, d2BasisPaisa: d2, createdAt: createdAt, registrationId: registrationId);
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
        DateTimeOffset? createdAt = null, Guid? registrationId = null)
    {
        var (fullYear, d1, d2) = Rule42Amount(fullYearBasis);
        var scope = registrationId ?? GstRegistration.PrimaryId;

        // Σ ONLY this FY's monthly (yyyy-MM) Rule-42 reversals — never another FY's months, never any true-up (FY-period)
        // row. FIX 2: the old scope subtracted every Rule-42 row ever posted, so a later FY's delta floored to zero.
        // 🔴 And never ANOTHER REGISTRATION's months (census 6.23): the full-year figure is this GSTIN's, so
        // subtracting a sister registration's monthly rows would true one return up against another's postings.
        long mc = 0, ms = 0, mi = 0, mcess = 0;
        foreach (var r in _company.ItcReversals)
        {
            if (r.Rule != ItcReversalRule.Rule42 || r.ReclaimOfId is not null) continue;
            if (!IsMonthlyPeriodWithinFy(r.Period, fyPeriod)) continue;
            if (RegistrationOfRow(r) != scope) continue;
            mc += r.CgstPaisa; ms += r.SgstPaisa; mi += r.IgstPaisa; mcess += r.CessPaisa;
        }

        // The SIGNED per-head delta (NOT floored) — FIX 3: a negative head is an over-reversal to re-credit, not forfeit.
        return PostRule42TrueUp(fyPeriod,
            fullYear.CgstPaisa - mc, fullYear.SgstPaisa - ms, fullYear.IgstPaisa - mi, fullYear.CessPaisa - mcess,
            d1, d2, date, createdAt, scope);
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
        long d1BasisPaisa, long d2BasisPaisa, DateOnly date, DateTimeOffset? createdAt, Guid scope)
    {
        // Idempotency (§5.3): one true-up row per (Rule42, fyPeriod, no source, REGISTRATION) — a re-run returns the
        // existing row. Each registration trues its OWN year up, so the registration is part of the key.
        var existing = _company.ItcReversals.FirstOrDefault(r =>
            r.ReclaimOfId is null && r.Rule == ItcReversalRule.Rule42 && r.Period == fyPeriod
            && r.SourceVoucherId is null && r.SourceLineId is null
            && RegistrationOfRow(r) == scope);
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

        var voucher = new LedgerService(_company).Post(Stamp(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC reversal — Rule42 annual true-up — {fyPeriod}"),
            scope));

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

        // 🔴 The reclaim belongs to the SAME registration as the reversal it re-avails — it restores that GSTIN's
        // credit pool and lands in that return's Table 4(D)(1). Read from the reversal's own voucher, so it cannot
        // be named wrongly by a caller.
        var scope = RegistrationOfRow(reversal);

        // ECRS (§11.7): a Table 4(D)(1) reclaim can never exceed the tracked per-head reversal balance — the
        // balance of the registration being reclaimed against, which is the statement the portal validates it
        // against. On a single-registration book this is the whole book, as before (ER-13).
        var balance = OutstandingReversalBalance(scope);
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

        var voucher = new LedgerService(_company).Post(Stamp(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"ITC re-availment (reclaim) — {reversal.Rule} — {period}"),
            scope));

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
    ///
    /// <para>🔴 <b>The registration (census 6.23).</b> A voucher-keyed candidate — which every POSTING candidate is,
    /// since §17(5)-blocked and Table-4(D)-ineligible credit is always read off a specific purchase — carries its
    /// own source voucher, so the reversal attributes to <b>that purchase's</b> registration and
    /// <paramref name="registrationId"/> is not consulted at all. It is the fallback for the 2B-line-keyed credit-note
    /// candidate, which has no voucher in the books to read it from, and the caller passes the registration whose
    /// gate surfaced the candidate.</para>
    /// </summary>
    public ItcReversal? PostFromCandidate(ItcReversalCandidate candidate, string period, DateOnly date,
        DateTimeOffset? createdAt = null, Guid? registrationId = null)
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
            sourceVoucherId: candidate.VoucherId, sourceLineId: candidate.LineId, createdAt: createdAt,
            registrationId: registrationId);
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

    /// <summary>Delegates to <see cref="PaisaConversion.ToPaisaRounded(Money)"/> — the ONE rupees→paisa rule
    /// (drift lock D3), ROUNDED semantics: a reversal share is a derived intermediate, it does not abort.</summary>
    private static long ToPaisa(Money money) => PaisaConversion.ToPaisaRounded(money);
}
