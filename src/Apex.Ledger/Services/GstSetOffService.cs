using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Rule-88A / §49A ITC set-off</b> engine (Phase 9 slice 7; RQ-21; ER-7/ER-2/ER-3; A14-CONFIRMED §11.1). Two
/// halves: a <b>pure, deterministic, paisa-exact allocator</b> (<see cref="Allocate"/> — no ledger mutation, integer
/// paisa in / integer paisa out) and a thin <b>poster</b> (<see cref="PostSetOff"/>) that wraps the allocation in one
/// balanced stat-adjustment Journal and records the per-head Table-6.1 utilisation.
/// <para>
/// The utilisation order is fixed and legally non-overridable except for the intra-CGST/SGST split of the residual
/// IGST credit (DP-16, editable Table 6.1): <b>IGST credit is used FIRST and fully exhausted</b> (to IGST, then to
/// CGST &amp; SGST) before any CGST or SGST credit is touched; <b>CGST credit → CGST then IGST, NEVER SGST</b>;
/// <b>SGST credit → SGST then IGST, NEVER CGST</b> (and only after CGST credit for IGST is exhausted, §49(5)(c)/(d));
/// <b>Cess credit → Cess only</b> (ring-fenced, ER-2); residual liability → cash. The CGST↔SGST cross-utilisation wall
/// is <b>structural</b> — no algorithm branch pairs CGST credit with SGST liability (or vice-versa) — and reinforced
/// by a DB <c>CHECK</c> on <c>gst_setoff_lines</c>. RCM output liability / interest / penalty / fee are cash-only
/// (ER-3; <see cref="SetOffDemand.LiabRcmCash"/> never enters the credit steps).
/// </para>
/// </summary>
public sealed class GstSetOffService
{
    private readonly Company _company;

    public GstSetOffService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The auto-created GST Stat-Adjustment (Alt+J) Journal voucher-type name.</summary>
    public const string StatAdjustmentTypeName = "GST Stat Adjustment";

    // ==============================================================================================================
    //  Pure allocator (no ledger mutation)
    // ==============================================================================================================

    /// <summary>
    /// The per-head liability + credit demand for a period's set-off (integer paisa). <see cref="LiabRcmCash"/> is the
    /// reverse-charge output liability (plus any interest/penalty/fee), which is <b>cash-only</b> (ER-3) — it never
    /// enters the credit-utilisation steps. All amounts must be ≥ 0.
    /// </summary>
    public readonly record struct SetOffDemand(
        long LiabCgst, long LiabSgst, long LiabIgst, long LiabCess,
        long LiabRcmCash,
        long CreditCgst, long CreditSgst, long CreditIgst, long CreditCess)
    {
        internal void EnsureNonNegative()
        {
            if (LiabCgst < 0 || LiabSgst < 0 || LiabIgst < 0 || LiabCess < 0 || LiabRcmCash < 0
                || CreditCgst < 0 || CreditSgst < 0 || CreditIgst < 0 || CreditCess < 0)
                throw new ArgumentException("Set-off demand amounts must be ≥ 0 paisa.");
        }
    }

    /// <summary>The 1%-cash-cap (Rule 86B, DP-17) configuration. <see cref="Applies"/> false ⇒ no effect (off by
    /// default ⇒ ER-13). The &gt; ₹50 L monthly-taxable trigger is decided by the caller/config; the engine only
    /// enforces the ≥ 1%-of-output-tax cash floor when <see cref="Applies"/> is set.</summary>
    public readonly record struct Rule86BConfig(bool Applies);

    /// <summary>The set-off options: the residual-IGST split (DP-16, editable) and the Rule-86B cap (DP-17).</summary>
    public readonly record struct SetOffOptions(
        ResidualIgstSplit ResidualSplit = ResidualIgstSplit.CgstFirst, Rule86BConfig? Rule86B = null);

    /// <summary>One Table-6.1 credit-utilisation allocation (creditHead → liabilityHead, integer paisa). The residual
    /// cash payable is carried separately on <see cref="SetOffAllocation"/> — this list holds only credit lines.</summary>
    public readonly record struct SetOffLine(GstTaxHead CreditHead, GstTaxHead LiabilityHead, long AmountPaisa);

    /// <summary>The computed allocation: the Table-6.1 credit-utilisation lines, the residual cash payable per head
    /// (plus the cash-only RCM), and the carried-forward credit per head. Paisa-exact.</summary>
    public sealed record SetOffAllocation(
        IReadOnlyList<SetOffLine> Lines,
        long CashCgst, long CashSgst, long CashIgst, long CashCess, long CashRcm,
        long ClosingCgst, long ClosingSgst, long ClosingIgst, long ClosingCess)
    {
        /// <summary>Σ credit utilised across all set-off lines, in paisa.</summary>
        public long TotalCreditUtilised => Lines.Sum(l => l.AmountPaisa);

        /// <summary>Σ residual cash payable across the GST heads (excludes cess &amp; RCM), in paisa.</summary>
        public long TotalGstCash => CashCgst + CashSgst + CashIgst;

        /// <summary>Σ residual cash payable across every head incl. cess &amp; the cash-only RCM liability, in paisa.</summary>
        public long TotalCash => CashCgst + CashSgst + CashIgst + CashCess + CashRcm;
    }

    /// <summary>
    /// Computes the compliant default Rule-88A allocation for <paramref name="demand"/> (pure, deterministic,
    /// paisa-exact). The residual IGST credit's intra-CGST/SGST split follows <paramref name="options"/> (DP-16
    /// default <see cref="ResidualIgstSplit.CgstFirst"/>); the Rule-86B cash floor (DP-17), if configured, is applied
    /// last. No ledger is touched.
    /// </summary>
    public static SetOffAllocation Allocate(SetOffDemand demand, SetOffOptions options = default)
    {
        demand.EnsureNonNegative();

        long credC = demand.CreditCgst, credS = demand.CreditSgst, credI = demand.CreditIgst, credCess = demand.CreditCess;
        long liabC = demand.LiabCgst, liabS = demand.LiabSgst, liabI = demand.LiabIgst, liabCess = demand.LiabCess;

        var lines = new List<SetOffLine>();
        void Use(GstTaxHead credit, GstTaxHead liab, long amt)
        {
            if (amt > 0) lines.Add(new SetOffLine(credit, liab, amt));
        }

        // Step A — IGST credit first, and fully exhausted (§49A). A1: IGST credit → IGST liability.
        var a1 = Math.Min(credI, liabI); credI -= a1; liabI -= a1; Use(GstTaxHead.Integrated, GstTaxHead.Integrated, a1);

        // A2: residual IGST credit → CGST and/or SGST liability. This is the intra-CGST/SGST split that Rule 88A makes
        // discretionary/editable (DP-16). The allocation is MINIMAL-CASH: IGST first covers the shortfall each head's
        // OWN credit cannot (so own credit is never stranded), then the EXTRA is steered to the preferred head — which
        // leaves the carried-forward surplus in that head's OWN credit pool. This is what makes fixture (F) foot to ₹0
        // cash in both the CgstFirst and SgstFirst splits (the ₹100 surplus landing in CGST vs SGST credit).
        var ri = credI;                               // residual IGST credit after A1
        var minIgstC = Math.Min(Math.Max(0, liabC - credC), Math.Min(ri, liabC)); // IGST CGST needs so own CGST covers the rest
        var minIgstS = Math.Min(Math.Max(0, liabS - credS), Math.Min(ri - minIgstC, liabS));
        var extra = ri - minIgstC - minIgstS;         // IGST left after covering both shortfalls
        long igstToC = minIgstC, igstToS = minIgstS;
        switch (options.ResidualSplit)
        {
            case ResidualIgstSplit.SgstFirst:
            {
                var addS = Math.Min(extra, liabS - igstToS); igstToS += addS; extra -= addS;
                var addC = Math.Min(extra, liabC - igstToC); igstToC += addC; extra -= addC;
                break;
            }
            case ResidualIgstSplit.Proportionate:
            {
                var capC = liabC - igstToC;
                var capS = liabS - igstToS;
                var cap = capC + capS;
                var addC = cap == 0 ? 0 : extra * capC / cap;
                var addS = Math.Min(extra - addC, capS);
                addC = Math.Min(extra - addS, capC);
                igstToC += addC; igstToS += addS;
                break;
            }
            default: // CgstFirst (DP-16 default) — steer the extra IGST (and thus the surplus own credit) toward CGST.
            {
                var addC = Math.Min(extra, liabC - igstToC); igstToC += addC; extra -= addC;
                var addS = Math.Min(extra, liabS - igstToS); igstToS += addS; extra -= addS;
                break;
            }
        }
        credI -= igstToC; liabC -= igstToC; Use(GstTaxHead.Integrated, GstTaxHead.Central, igstToC);
        credI -= igstToS; liabS -= igstToS; Use(GstTaxHead.Integrated, GstTaxHead.State, igstToS);

        // Step B — CGST credit (own-head priority): CGST → CGST, then CGST → IGST (NEVER SGST). Runs BEFORE Step C so
        // the §49(5)(c)/(d) proviso (CGST credit for IGST exhausted before SGST credit pays IGST) holds by construction.
        var b1 = Math.Min(credC, liabC); credC -= b1; liabC -= b1; Use(GstTaxHead.Central, GstTaxHead.Central, b1);
        var b2 = Math.Min(credC, liabI); credC -= b2; liabI -= b2; Use(GstTaxHead.Central, GstTaxHead.Integrated, b2);

        // Step C — SGST/UTGST credit (own-head priority): SGST → SGST, then SGST → IGST (NEVER CGST).
        var c1 = Math.Min(credS, liabS); credS -= c1; liabS -= c1; Use(GstTaxHead.State, GstTaxHead.State, c1);
        var c2 = Math.Min(credS, liabI); credS -= c2; liabI -= c2; Use(GstTaxHead.State, GstTaxHead.Integrated, c2);

        // Step D — Cess credit → Cess liability ONLY (ring-fenced, ER-2).
        var dd = Math.Min(credCess, liabCess); credCess -= dd; liabCess -= dd; Use(GstTaxHead.Cess, GstTaxHead.Cess, dd);

        // Step E — residual liability → electronic cash ledger. RCM output / interest / penalty / fee = cash-only (ER-3).
        long cashC = liabC, cashS = liabS, cashI = liabI, cashCess = liabCess;
        var cashRcm = demand.LiabRcmCash;

        // Step F — Rule 86B (99% cap, DP-17), applied last: force ≥ 1% of the forward output tax through cash by
        // clawing the shortfall back from the most-recently-allocated credit lines. Off by default ⇒ no effect (ER-13).
        if (options.Rule86B is { Applies: true })
        {
            var outputTax = demand.LiabCgst + demand.LiabSgst + demand.LiabIgst; // forward output tax (excl cess & RCM)
            var floor = (outputTax + 99) / 100; // ceil(1%)
            var deficit = Math.Max(0, floor - (cashC + cashS + cashI));
            for (var i = lines.Count - 1; i >= 0 && deficit > 0; i--)
            {
                var ln = lines[i];
                if (ln.LiabilityHead == GstTaxHead.Cess) continue; // never disturb the cess ring-fence
                var t = Math.Min(ln.AmountPaisa, deficit);
                switch (ln.LiabilityHead)
                {
                    case GstTaxHead.Central: cashC += t; break;
                    case GstTaxHead.State: cashS += t; break;
                    case GstTaxHead.Integrated: cashI += t; break;
                }
                switch (ln.CreditHead)
                {
                    case GstTaxHead.Central: credC += t; break;
                    case GstTaxHead.State: credS += t; break;
                    case GstTaxHead.Integrated: credI += t; break;
                    case GstTaxHead.Cess: credCess += t; break;
                }
                deficit -= t;
                if (ln.AmountPaisa == t) lines.RemoveAt(i);
                else lines[i] = ln with { AmountPaisa = ln.AmountPaisa - t };
            }
        }

        return new SetOffAllocation(lines, cashC, cashS, cashI, cashCess, cashRcm, credC, credS, credI, credCess);
    }

    /// <summary>
    /// Re-validates a (possibly user-overridden) allocation against the <b>non-overridable</b> Rule-88A constraints
    /// for <paramref name="demand"/> (verify-mode, §2.3) — throws on the first violation so an illegal override is
    /// rejected before any posting, never silently "fixed". Checks: credit-per-head ≤ available; liability-covered ≤
    /// demanded; the CGST↔SGST cross-utilisation wall; the cess ring-fence; IGST credit fully exhausted before any
    /// CGST/SGST credit; the §49(5)(c)/(d) CGST-before-SGST-for-IGST proviso; and (if on) the Rule-86B cash floor.
    /// </summary>
    public static void EnsureLegal(SetOffDemand demand, SetOffAllocation allocation, SetOffOptions options = default)
    {
        demand.EnsureNonNegative();
        ArgumentNullException.ThrowIfNull(allocation);

        long usedCredC = 0, usedCredS = 0, usedCredI = 0, usedCredCess = 0;
        long covC = 0, covS = 0, covI = 0, covCess = 0;
        long sgstPaidIgst = 0;

        foreach (var l in allocation.Lines)
        {
            if (l.AmountPaisa <= 0)
                throw new InvalidOperationException("A set-off allocation line must be > 0 paisa.");

            // The CGST↔SGST cross-utilisation wall (ER-7) + the cess ring-fence (ER-2).
            if (l.CreditHead == GstTaxHead.Central && l.LiabilityHead == GstTaxHead.State)
                throw new InvalidOperationException("Illegal set-off: CGST credit cannot discharge SGST liability (ER-7).");
            if (l.CreditHead == GstTaxHead.State && l.LiabilityHead == GstTaxHead.Central)
                throw new InvalidOperationException("Illegal set-off: SGST credit cannot discharge CGST liability (ER-7).");
            if ((l.CreditHead == GstTaxHead.Cess) != (l.LiabilityHead == GstTaxHead.Cess))
                throw new InvalidOperationException("Illegal set-off: cess credit is ring-fenced to cess liability (ER-2).");

            switch (l.CreditHead)
            {
                case GstTaxHead.Central: usedCredC += l.AmountPaisa; break;
                case GstTaxHead.State: usedCredS += l.AmountPaisa; break;
                case GstTaxHead.Integrated: usedCredI += l.AmountPaisa; break;
                case GstTaxHead.Cess: usedCredCess += l.AmountPaisa; break;
            }
            switch (l.LiabilityHead)
            {
                case GstTaxHead.Central: covC += l.AmountPaisa; break;
                case GstTaxHead.State: covS += l.AmountPaisa; break;
                case GstTaxHead.Integrated: covI += l.AmountPaisa; break;
                case GstTaxHead.Cess: covCess += l.AmountPaisa; break;
            }
            if (l.CreditHead == GstTaxHead.State && l.LiabilityHead == GstTaxHead.Integrated)
                sgstPaidIgst += l.AmountPaisa;
        }

        // Credit-per-head cannot exceed the available credit.
        if (usedCredC > demand.CreditCgst || usedCredS > demand.CreditSgst
            || usedCredI > demand.CreditIgst || usedCredCess > demand.CreditCess)
            throw new InvalidOperationException("Illegal set-off: a credit head was over-utilised (used more than available).");

        // Liability discharged by credit cannot exceed the demanded liability.
        if (covC > demand.LiabCgst || covS > demand.LiabSgst || covI > demand.LiabIgst || covCess > demand.LiabCess)
            throw new InvalidOperationException("Illegal set-off: a liability head was over-discharged by credit.");

        var closingIgst = demand.CreditIgst - usedCredI;
        var closingCgst = demand.CreditCgst - usedCredC;

        // IGST credit must be fully exhausted before any CGST/SGST credit is used.
        if ((usedCredC > 0 || usedCredS > 0) && closingIgst > 0)
            throw new InvalidOperationException(
                "Illegal set-off: CGST/SGST credit was used while IGST credit remained (IGST must be exhausted first, §49A).");

        // §49(5)(c)/(d): SGST credit may pay IGST only after CGST credit for IGST is exhausted.
        if (sgstPaidIgst > 0 && closingCgst > 0)
            throw new InvalidOperationException(
                "Illegal set-off: SGST credit paid IGST while CGST credit remained (§49(5)(c)/(d) proviso).");

        // Rule 86B floor (if on): ≥ 1% of the forward output tax must be paid in cash.
        if (options.Rule86B is { Applies: true })
        {
            var outputTax = demand.LiabCgst + demand.LiabSgst + demand.LiabIgst;
            var floor = (outputTax + 99) / 100;
            var gstCash = (demand.LiabCgst - covC) + (demand.LiabSgst - covS) + (demand.LiabIgst - covI);
            if (gstCash < floor)
                throw new InvalidOperationException(
                    "Illegal set-off: Rule 86B requires ≥ 1% of the output tax through cash, but the override pays less.");
        }
    }

    // ==============================================================================================================
    //  Poster (the single guarded entry-point; idempotent per period)
    // ==============================================================================================================

    /// <summary>
    /// Finds — or creates — the GST Stat-Adjustment (Alt+J) Journal voucher type (idempotent). Reuses the Journal base
    /// type; only <see cref="VoucherType.IsGstStatAdjustment"/> marks it, so <c>DirectionOf</c> (Journal ⇒ null) keeps
    /// every set-off / reversal voucher OUT of the Table 3.1 / 4(A) sums.
    /// </summary>
    public VoucherType EnsureStatAdjustmentType()
    {
        var existing = _company.VoucherTypes.FirstOrDefault(t => t.IsGstStatAdjustmentType);
        if (existing is not null) return existing;

        var type = new VoucherType(
            Guid.NewGuid(), StatAdjustmentTypeName, VoucherBaseType.Journal,
            NumberingMethod.Automatic, defaultShortcut: "Alt+J", abbreviation: "StatAdj",
            isActive: true, isPredefined: false, isGstStatAdjustment: true);
        _company.AddVoucherType(type);
        return type;
    }

    /// <summary>
    /// Posts the credit-utilisation half of a Rule-88A set-off for <paramref name="period"/> as <b>one balanced
    /// stat-adjustment Journal</b> — a <c>Dr Output {liabHead}</c> / <c>Cr Input {creditHead}</c> pair per allocation
    /// line, each tagged <see cref="GstAdjustmentKind.SetOff"/> — through the single guarded entry-point
    /// <see cref="LedgerService.Post"/> (so <see cref="VoucherValidator"/> guarantees Σ Dr == Σ Cr). It also records
    /// the per-head <see cref="GstSetoffLine"/> Table-6.1 audit rows. The residual cash is NOT part of this Journal (it
    /// is discharged by a payment voucher, §3). <b>Idempotent per period</b> (§5.3): an existing posted set-off for
    /// the same period is <b>replaced</b> (its voucher + rows deleted, then re-posted) — never a second additive
    /// voucher. Returns the posted Journal, or <c>null</c> when the allocation uses no credit (an all-cash period).
    /// </summary>
    public Voucher? PostSetOff(string period, SetOffAllocation allocation, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Set-off period is required.", nameof(period));
        ArgumentNullException.ThrowIfNull(allocation);

        var ledgerService = new LedgerService(_company);

        // Idempotency (§5.3): replace any prior set-off for this period (delete the voucher + its audit rows) so a
        // re-run is a confirmed replace, never an additive second voucher.
        var priorLines = _company.GstSetoffLines.Where(l => l.Period == period).ToList();
        foreach (var voucherId in priorLines.Select(l => l.VoucherId).Distinct().ToList())
            if (_company.FindVoucher(voucherId) is not null) ledgerService.Delete(voucherId);
        foreach (var line in priorLines) _company.RemoveGstSetoffLine(line);

        if (allocation.Lines.Count == 0) return null; // all-cash period — nothing to set off

        var gst = new GstService(_company);
        var type = EnsureStatAdjustmentType();

        var entryLines = new List<EntryLine>(allocation.Lines.Count * 2);
        foreach (var l in allocation.Lines)
        {
            var amount = new Money(l.AmountPaisa / 100m);
            if (l.LiabilityHead == GstTaxHead.Cess || l.CreditHead == GstTaxHead.Cess) gst.EnsureCessLedgers();

            var outputLedger = gst.FindTaxLedger(l.LiabilityHead, GstTaxDirection.Output)
                ?? throw new InvalidOperationException(
                    $"Output {l.LiabilityHead} ledger not found — enable GST first (EnableGst auto-creates it).");
            var inputLedger = gst.FindTaxLedger(l.CreditHead, GstTaxDirection.Input)
                ?? throw new InvalidOperationException(
                    $"Input {l.CreditHead} ledger not found — enable GST first (EnableGst auto-creates it).");

            // Dr Output {liabHead} (reduce the liability) / Cr Input {creditHead} (use the credit).
            entryLines.Add(new EntryLine(outputLedger.Id, amount, DrCr.Debit,
                gst: new GstLineTax(l.LiabilityHead, 0, Money.Zero, adjustment: GstAdjustmentKind.SetOff)));
            entryLines.Add(new EntryLine(inputLedger.Id, amount, DrCr.Credit,
                gst: new GstLineTax(l.CreditHead, 0, Money.Zero, adjustment: GstAdjustmentKind.SetOff)));
        }

        var voucher = ledgerService.Post(new Voucher(
            Guid.NewGuid(), type.Id, date, entryLines,
            narration: $"Rule-88A ITC set-off — {period}"));

        foreach (var l in allocation.Lines)
            _company.AddGstSetoffLine(new GstSetoffLine(
                Guid.NewGuid(), voucher.Id, period, l.CreditHead, l.LiabilityHead, isCash: false, l.AmountPaisa));

        return voucher;
    }
}
