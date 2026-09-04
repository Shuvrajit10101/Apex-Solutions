using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Seed;

namespace Apex.Ledger.Services;

/// <summary>
/// The core GST engine (catalog §12; phase4 requirements RQ-1..RQ-19; ER-3/ER-4/ER-5). Framework-, DB-,
/// clock- and RNG-free: pure, deterministic, paisa-exact tax computation over the <see cref="Company"/>
/// aggregate, exactly like the accounting/inventory core. Responsibilities:
/// <list type="bullet">
///   <item><see cref="EnableGst"/> — idempotently enable GST, seed the config-driven slabs (0/5/18/40) and
///     auto-create the six Output/Input tax ledgers (+ a Round-Off ledger) under Duties &amp; Taxes (DP-1/DP-3).</item>
///   <item><see cref="ResolveRate"/> — the five-level GST rate hierarchy, walked in the order the book's
///     <see cref="GstConfig.SourceOfGstRate"/> names: <c>Ledger → Accounting Group → Stock Item → Stock Group →
///     Company</c> by default, or <c>Stock Item → Stock Group → Ledger → Accounting Group → Company</c> for a book
///     migrated from v50. It stops at the first level that declares the detail, with the ER-5 fail-fast behind the
///     last of them (T0-4 slice S2b — see <c>Hierarchy</c> for the two orders as data, the user ruling behind the
///     default, what it does and does not do to an already-posted voucher, and the vendor-vs-ours split).</item>
///   <item><see cref="IsInterState"/> — company home State vs party State routing (RQ-11).</item>
///   <item><see cref="ComputeInvoiceTax"/> — per-line paisa-exact CGST/SGST split (intra) or IGST (inter),
///     the additive tax entry lines, and an optional invoice round-off (RQ-12/13/19).</item>
/// </list>
/// GST is <b>additive</b>: the tax lines post to the Duties &amp; Taxes tax ledgers only, which are excluded
/// from the item-invoice pairing sum (<see cref="ClassificationRules.IsDutiesAndTaxesLedger"/>), so
/// <see cref="VoucherValidator.EnsureItemInvoiceValid"/> keeps passing unchanged (ER-8).
/// </summary>
public sealed class GstService
{
    private readonly Company _company;

    public GstService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ---- Auto-created tax-ledger names (DP-3) ----

    /// <summary>The canonical Output/Input tax-ledger name for a head + direction (DP-3).</summary>
    public static string TaxLedgerName(GstTaxHead head, GstTaxDirection direction) =>
        TaxLedgerName(head, direction, isReverseCharge: false);

    /// <summary>
    /// The canonical tax-ledger name for a head + direction, optionally the dedicated <b>reverse-charge output</b> ledger
    /// (Phase 9 slice 2; RQ-7). A reverse-charge output liability lands in a distinct <c>"RCM Output {CGST|SGST|IGST|Cess}"</c>
    /// ledger — the cash-only §49(4) liability, kept separate from the ordinary Output ledgers so it is never netted
    /// against the credit ledger. RCM <b>input</b> ITC reuses the ordinary <c>Input {head}</c> ledger (distinguished only
    /// by the line tag), so <paramref name="isReverseCharge"/> is meaningful only for the Output direction.
    /// </summary>
    public static string TaxLedgerName(GstTaxHead head, GstTaxDirection direction, bool isReverseCharge)
    {
        var side = direction == GstTaxDirection.Output ? "Output" : "Input";
        var headName = head switch
        {
            GstTaxHead.Central => "CGST",
            GstTaxHead.State => "SGST",
            GstTaxHead.Integrated => "IGST",
            GstTaxHead.Cess => "Cess",
            _ => throw new ArgumentOutOfRangeException(nameof(head)),
        };
        return isReverseCharge && direction == GstTaxDirection.Output
            ? $"RCM {side} {headName}"
            : $"{side} {headName}";
    }

    /// <summary>The auto-created invoice Round-Off ledger name (DP-4).</summary>
    public const string RoundOffLedgerName = "Round Off";

    // ---- RQ-1/RQ-5: enable GST + auto-create tax ledgers + seed slabs (idempotent) ----

    /// <summary>
    /// Enables GST on the company with the given config (F11; RQ-1/RQ-2), <b>idempotently</b>. Validates the
    /// config (fail-fast, ER-6), stores it, seeds the config-driven rate slabs (0/5/18/40, RQ-25/DP-2) if the
    /// config has none, and auto-creates the six Output/Input CGST/SGST/IGST tax ledgers under Duties &amp;
    /// Taxes plus a Round-Off ledger (DP-1/DP-3) — skipping any that already exist, so re-enabling never
    /// duplicates. Returns the enabled config. Existing (non-GST) companies are untouched until this is called.
    /// </summary>
    public GstConfig EnableGst(GstConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Enabled = true;
        config.EnsureValid();

        // Preserve any slabs already seeded on a prior enable; otherwise seed the Phase-4 defaults (RQ-25).
        if (config.RateSlabs.Count == 0)
            foreach (var slab in SeedGstRates.BuildDefaults())
                config.AddRateSlab(slab);

        _company.Gst = config;

        var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
            ?? throw new InvalidOperationException("Seed missing 'Duties & Taxes' group; cannot auto-create GST tax ledgers.");

        // Auto-create the 6 tax ledgers (idempotent by classification: skip a head+direction already present).
        // Phase 9 slice 3 (RQ-4): a Composition dealer collects no output tax and claims no ITC, so it needs NONE of
        // the six Output/Input GST ledgers — creating them would pollute its ledger set. Gate them off for composition
        // (a Regular/Unregistered company is byte-identical, ER-13). The RCM Output ledgers are still created LAZILY by
        // RcmService when an inward RCM supply posts; the Round-Off ledger below is harmless and kept.
        if (config.RegistrationType != GstRegistrationType.Composition)
            foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
                foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
                    EnsureTaxLedger(dutiesAndTaxes.Id, head, direction);

        // Round-Off ledger under Indirect Expenses (a P&L head; a round-off can be Dr or Cr).
        EnsureRoundOffLedger();

        return config;
    }

    /// <summary>
    /// The <b>ordinary</b> tax ledger for a head + direction, or <c>null</c> if GST is not enabled / not created. Filters
    /// out the reverse-charge Output ledgers (Phase 9 slice 2; risk #2): with RCM Output ledgers now also
    /// <c>(head, Output)</c>, matching on head+direction alone would be ambiguous — a normal sale could post to the RCM
    /// ledger. The <c>IsReverseCharge == false</c> predicate keeps this returning the ordinary ledger; the RCM Output
    /// ledger is found via <see cref="FindRcmOutputLedger"/>.
    /// </summary>
    public Domain.Ledger? FindTaxLedger(GstTaxHead head, GstTaxDirection direction) =>
        _company.Ledgers.FirstOrDefault(l =>
            l.GstClassification is { IsReverseCharge: false } c && c.TaxHead == head && c.Direction == direction);

    /// <summary>The dedicated <b>RCM output-liability</b> ledger for a head, or <c>null</c> if not yet created (Phase 9
    /// slice 2). Filters on <c>IsReverseCharge == true</c> so it never collides with the ordinary Output ledger.</summary>
    public Domain.Ledger? FindRcmOutputLedger(GstTaxHead head) =>
        _company.Ledgers.FirstOrDefault(l =>
            l.GstClassification is { IsReverseCharge: true, Direction: GstTaxDirection.Output } c && c.TaxHead == head);

    /// <summary>
    /// Lazily creates (idempotently) the dedicated <b>RCM Output {head}</b> ledger under Duties &amp; Taxes and returns it
    /// (Phase 9 slice 2; RQ-7). Called only when an RCM line is about to post (never in <see cref="EnableGst"/>, so an
    /// off company keeps the v38 ledger set — ER-13). The ledger carries
    /// <c>LedgerGstClassification(head, Output, isReverseCharge: true)</c> — the cash-only §49(4) liability.
    /// </summary>
    public Domain.Ledger EnsureRcmOutputLedger(GstTaxHead head)
    {
        if (FindRcmOutputLedger(head) is { } existing) return existing;

        var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
            ?? throw new InvalidOperationException("Seed missing 'Duties & Taxes' group; cannot auto-create RCM Output ledgers.");

        var name = TaxLedgerName(head, GstTaxDirection.Output, isReverseCharge: true);
        // If a ledger by that name exists (e.g. user pre-created), tag it; else create a fresh one.
        if (_company.FindLedgerByName(name) is { } byName)
        {
            byName.GstClassification ??= new LedgerGstClassification(head, GstTaxDirection.Output, isReverseCharge: true);
            if (byName.GroupId == Guid.Empty) byName.GroupId = dutiesAndTaxes.Id;
            return byName;
        }

        var ledger = new Domain.Ledger(
            Guid.NewGuid(), name, dutiesAndTaxes.Id, Money.Zero, openingIsDebit: false,
            gstClassification: new LedgerGstClassification(head, GstTaxDirection.Output, isReverseCharge: true));
        _company.AddLedger(ledger);
        return ledger;
    }

    private void EnsureTaxLedger(Guid dutiesAndTaxesGroupId, GstTaxHead head, GstTaxDirection direction)
    {
        if (FindTaxLedger(head, direction) is not null) return; // idempotent

        var name = TaxLedgerName(head, direction);
        // If a ledger by that name exists (e.g. user pre-created), tag it; else create a fresh one.
        var existing = _company.FindLedgerByName(name);
        if (existing is not null)
        {
            existing.GstClassification ??= new LedgerGstClassification(head, direction);
            if (existing.GroupId == Guid.Empty) existing.GroupId = dutiesAndTaxesGroupId;
            return;
        }

        _company.AddLedger(new Domain.Ledger(
            Guid.NewGuid(), name, dutiesAndTaxesGroupId, Money.Zero, openingIsDebit: direction == GstTaxDirection.Input,
            gstClassification: new LedgerGstClassification(head, direction)));
    }

    /// <summary>
    /// Creates the Output/Input <b>Cess</b> ledgers under Duties &amp; Taxes, idempotently (Phase 9 slice 1). Called
    /// ONLY lazily — from <see cref="SeedAdvancedGst"/> when cess rows are seeded, or from
    /// <see cref="ComputeInvoiceTax"/> when a cess line is about to post — never unconditionally in
    /// <see cref="EnableGst"/> (which must stay byte-identical for a company that bears no cess, ER-13).
    /// </summary>
    private void EnsureCessLedgers(Guid dutiesAndTaxesGroupId)
    {
        EnsureTaxLedger(dutiesAndTaxesGroupId, GstTaxHead.Cess, GstTaxDirection.Output);
        EnsureTaxLedger(dutiesAndTaxesGroupId, GstTaxHead.Cess, GstTaxDirection.Input);
    }

    /// <summary>Lazily creates the Output/Input Cess ledgers under Duties &amp; Taxes, idempotently (Phase 9). Public so the
    /// <c>RcmService</c> cess path can ensure the normal Input Cess ledger before posting an RCM cess ITC line.</summary>
    public void EnsureCessLedgers()
    {
        var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
            ?? throw new InvalidOperationException("Seed missing 'Duties & Taxes' group; cannot auto-create Cess ledgers.");
        EnsureCessLedgers(dutiesAndTaxes.Id);
    }

    /// <summary>
    /// Enables the <b>advanced GST 2.0</b> data on an already-GST-enabled company (Phase 9 slice 1; RQ-1/RQ-2): seeds
    /// the dated rate-history windows and the three Compensation-Cess windows (when each is empty), and — because cess
    /// rows now exist — lazily creates the Output/Input Cess ledgers. This is the <b>explicit opt-in</b> (invoked by
    /// the GST Rate Setup bulk screen / an F11 advanced toggle in a later UI pass, and by the advanced-GST tests). It
    /// is deliberately <b>separate</b> from <see cref="EnableGst"/> so a plain Phase-4/8 GST company that never opts in
    /// keeps empty rate-history/cess and no Cess ledger — byte-identical to a v37 company (ER-13).
    /// </summary>
    public void SeedAdvancedGst()
    {
        var config = _company.Gst
            ?? throw new InvalidOperationException("GST is not enabled — call EnableGst before SeedAdvancedGst.");

        if (config.RateHistory.Count == 0)
            foreach (var e in SeedGstRates.BuildDefaultRateHistory())
                config.AddRateHistory(e);

        if (config.CessRates.Count == 0)
            foreach (var r in SeedGstRates.BuildDefaultCessRates())
                config.AddCessRate(r);

        // Phase 9 slice 2: seed the notified reverse-charge categories (idempotent; only the advanced-GST opt-in seeds
        // them, so EnableGst stays byte-identical — ER-13). The RCM Output ledgers are created LAZILY when an RCM line
        // posts (never here), so an opted-in company that never posts an RCM supply keeps the v38 ledger set.
        if (config.RcmCategories.Count == 0)
            foreach (var c in SeedRcmCategories.BuildDefaults())
                config.AddRcmCategory(c);

        if (config.CessRates.Count > 0)
        {
            var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
                ?? throw new InvalidOperationException("Seed missing 'Duties & Taxes' group; cannot auto-create Cess ledgers.");
            EnsureCessLedgers(dutiesAndTaxes.Id);
        }
    }

    private void EnsureRoundOffLedger()
    {
        if (_company.FindLedgerByName(RoundOffLedgerName) is not null) return;
        var indirectExp = _company.FindGroupByName("Indirect Expenses")
            ?? throw new InvalidOperationException("Seed missing 'Indirect Expenses' group; cannot auto-create Round-Off ledger.");
        _company.AddLedger(new Domain.Ledger(
            Guid.NewGuid(), RoundOffLedgerName, indirectExp.Id, Money.Zero, openingIsDebit: true));
    }

    /// <summary>The auto-created non-creditable RCM-tax expense-ledger name (Phase 9 slice 3; ER-4).</summary>
    public const string RcmNonCreditableCostLedgerName = "RCM Tax (Non-creditable)";

    /// <summary>
    /// Lazily creates (idempotently) the <b>RCM Tax (Non-creditable)</b> expense ledger under Indirect Expenses and
    /// returns it (Phase 9 slice 3; ER-4). A <b>Composition</b> dealer pays inward reverse-charge tax in cash exactly
    /// like a Regular dealer, but composition blocks ALL ITC — so the RCM tax is a <b>cost</b>, not a creditable input.
    /// <see cref="RcmService"/> routes the balancing debit of a composition dealer's RCM liability here (instead of an
    /// Input ITC ledger), so no ITC-tagged line exists. Created lazily (never in <see cref="EnableGst"/>) — a company
    /// that never posts a composition RCM supply keeps the v39 ledger set (ER-13). Mirrors <see cref="EnsureRoundOffLedger"/>.
    /// </summary>
    public Domain.Ledger EnsureRcmNonCreditableCostLedger()
    {
        if (_company.FindLedgerByName(RcmNonCreditableCostLedgerName) is { } existing) return existing;
        var indirectExp = _company.FindGroupByName("Indirect Expenses")
            ?? throw new InvalidOperationException("Seed missing 'Indirect Expenses' group; cannot auto-create the non-creditable RCM-tax ledger.");
        var ledger = new Domain.Ledger(
            Guid.NewGuid(), RcmNonCreditableCostLedgerName, indirectExp.Id, Money.Zero, openingIsDebit: true);
        _company.AddLedger(ledger);
        return ledger;
    }

    /// <summary>The auto-created GST-on-advance tax-suspense ledger name (Phase 9 slice 2b; Rule 50).</summary>
    public const string AdvanceTaxSuspenseLedgerName = "Output Tax on Advances";

    /// <summary>
    /// Lazily creates (idempotently) the <b>Output Tax on Advances</b> suspense ledger under Current Assets and returns
    /// it (Phase 9 slice 2b; RQ-25; Rule 50). On a service-advance receipt the tax is payable now (a genuine Output
    /// liability) yet not yet invoiced, so the paid tax is parked in this current-asset suspense — the receipt balances
    /// without inflating revenue, and the suspense is reversed when the invoice adjusts the advance (or on a Rule-51
    /// refund). Created <b>lazily</b> (never in <see cref="EnableGst"/>), so a company that never takes a taxable advance
    /// keeps the v38 ledger set (ER-13). Mirrors <see cref="EnsureRoundOffLedger"/> / <see cref="EnsureRcmOutputLedger"/>.
    /// </summary>
    public Domain.Ledger EnsureAdvanceTaxSuspenseLedger()
    {
        if (_company.FindLedgerByName(AdvanceTaxSuspenseLedgerName) is { } existing) return existing;
        var currentAssets = _company.FindGroupByName("Current Assets")
            ?? throw new InvalidOperationException("Seed missing 'Current Assets' group; cannot auto-create the advance-tax suspense ledger.");
        var ledger = new Domain.Ledger(
            Guid.NewGuid(), AdvanceTaxSuspenseLedgerName, currentAssets.Id, Money.Zero, openingIsDebit: true);
        _company.AddLedger(ledger);
        return ledger;
    }

    /// <summary>The auto-created electronic-cash-ledger (PMT-05) ledger name (Phase 9 slice 7; RQ-20).</summary>
    public const string ElectronicCashLedgerName = "Electronic Cash Ledger";

    /// <summary>
    /// Lazily creates (idempotently) the <b>Electronic Cash Ledger</b> (PMT-05) under Current Assets and returns it
    /// (Phase 9 slice 7; RQ-20/RQ-22). A PMT-06 deposit debits this ledger (Dr Electronic Cash Ledger / Cr Bank); a
    /// cash discharge of output tax draws it down (Dr Output {head} / Cr Electronic Cash Ledger). Its balance is the
    /// electronic cash ledger; the (major, minor) matrix split is a <b>projection</b> from <c>gst_challans</c>, not a
    /// stored balance. Created <b>lazily</b> (never in <see cref="EnableGst"/>), so a company that never deposits GST
    /// keeps the v43 ledger set (ER-13). Mirrors <see cref="EnsureAdvanceTaxSuspenseLedger"/>.
    /// </summary>
    public Domain.Ledger EnsureElectronicCashLedger()
    {
        if (_company.FindLedgerByName(ElectronicCashLedgerName) is { } existing) return existing;
        var currentAssets = _company.FindGroupByName("Current Assets")
            ?? throw new InvalidOperationException("Seed missing 'Current Assets' group; cannot auto-create the electronic cash ledger.");
        var ledger = new Domain.Ledger(
            Guid.NewGuid(), ElectronicCashLedgerName, currentAssets.Id, Money.Zero, openingIsDebit: true);
        _company.AddLedger(ledger);
        return ledger;
    }

    /// <summary>The auto-created ITC-reversal cost ledger name (Phase 9 slice 7; RQ-27 — the reversal engine lands in S7b).</summary>
    public const string ItcReversalCostLedgerName = "ITC Reversal (Non-creditable)";

    /// <summary>
    /// Lazily creates (idempotently) the <b>ITC Reversal (Non-creditable)</b> expense ledger under Indirect Expenses
    /// and returns it (Phase 9 slice 7; RQ-27). An ITC reversal (Rule 42/43/37/37A/§17(5)) or a DRC-03 voluntary
    /// payment routes its debit here — the reversed credit becomes a cost. Created <b>lazily</b> (never in
    /// <see cref="EnableGst"/>), so a company that never reverses keeps the v43 ledger set (ER-13). Mirrors
    /// <see cref="EnsureRcmNonCreditableCostLedger"/>.
    /// </summary>
    public Domain.Ledger EnsureItcReversalCostLedger()
    {
        if (_company.FindLedgerByName(ItcReversalCostLedgerName) is { } existing) return existing;
        var indirectExp = _company.FindGroupByName("Indirect Expenses")
            ?? throw new InvalidOperationException("Seed missing 'Indirect Expenses' group; cannot auto-create the ITC-reversal cost ledger.");
        var ledger = new Domain.Ledger(
            Guid.NewGuid(), ItcReversalCostLedgerName, indirectExp.Id, Money.Zero, openingIsDebit: true);
        _company.AddLedger(ledger);
        return ledger;
    }

    // ---- RQ-11: intra vs inter routing ----

    /// <summary>
    /// True iff a supply between the company home state and <paramref name="partyStateCode"/> is
    /// <b>inter-state</b> (different State/UT ⇒ IGST); false ⇒ intra-state (same State/UT ⇒ CGST+SGST)
    /// (RQ-11; law L-3). When the party State is null/blank/unresolved — a B2C walk-in consumer with no GSTIN
    /// and no recorded State — the place of supply for an unregistered/unrecorded recipient is the supplier's
    /// own State (DP-8), so the supply defaults to <b>intra-state (CGST+SGST)</b>, NOT IGST. Only a genuinely
    /// different, recorded party State is inter-state.
    ///
    /// <para><b>W0-15 — this is now the THROWING WRAPPER over the one shared rule</b>
    /// (<see cref="GstReportSupport.RoutingOf(Company, string?)"/>, drift lock D8). The rule itself answers
    /// <c>null</c> when the book declares no home State; a caller that is about to PRODUCE A FIGURE — post tax,
    /// compute a TCS/RCM leg, bill at the POS — must not proceed on a routing derived from a fact the book does not
    /// have, so this form refuses. <b>Read-only paths must NOT use it</b>: they call <c>RoutingOf</c> and carry the
    /// <c>null</c>. That is what stopped an already-issued invoice from being unprintable (F7) — the projector used
    /// to open with this method for every projection, including reprints that never consume the value.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The company declares no home State, so no supply can be routed.</exception>
    public bool IsInterState(string? partyStateCode) =>
        GstReportSupport.RoutingOf(_company, partyStateCode)
        ?? throw new InvalidOperationException("GST is not enabled (no home state) — cannot route a supply.");

    // ---- RQ-10 / T0-4: the GST rate hierarchy. See ResolveBase for the walk and its grounding. ----

    /// <summary>The outcome of resolving a GST rate for a taxable line (ER-5: pure &amp; total). The
    /// <see cref="ValuationBasis"/> (Phase 9 slice 1) reports whether the resolved rate is RSP-valued; it defaults to
    /// <see cref="GstValuationBasis.TransactionValue"/> so every existing construction stays valid (ER-13).</summary>
    public readonly record struct RateResolution(
        bool IsTaxable, int RateBasisPoints, GstTaxability Taxability,
        GstValuationBasis ValuationBasis = GstValuationBasis.TransactionValue)
    {
        /// <summary>A resolved taxable rate.</summary>
        public static RateResolution Taxable(int bp) => new(true, bp, GstTaxability.Taxable);

        /// <summary>An explicitly non-taxable line (Exempt/Nil/Non-GST) — zero tax, no error.</summary>
        public static RateResolution NonTaxable(GstTaxability taxability) => new(false, 0, taxability);
    }

    /// <summary>
    /// Resolves the effective GST rate for a line <b>as of a voucher date</b> (Phase 9 slice 1; RQ-1) by walking
    /// the master hierarchy (see <see cref="ResolveBase"/> for the walk, its order and its grounding). An
    /// Exempt/Nil/Non-GST taxability at the first level that declares one short-circuits to a non-taxable result
    /// (zero tax, RQ-15). A taxable line whose rate cannot be resolved at <b>any</b> level — the company default
    /// included — is an explicit "unresolved" and the caller fails fast (ER-5); it is never a silent zero.
    ///
    /// <para>It first resolves exactly as Phase-4/8 (<see cref="ResolveBase"/>), then applies a <b>pure date
    /// override</b>: only when a voucher date <b>and</b> a matching HSN-dated <see cref="GstConfig.RateHistory"/>
    /// row both exist does it return the dated rate (most-recently-effective wins). Absent either — every existing
    /// fixture (a date but no history rows) — it returns the base result unchanged, byte-identical to Phase-4/8
    /// (ER-13). Legacy 12/28% rows retained inactive-by-date let a pre-22-Sep-2025 voucher reprint at the historic
    /// rate.</para>
    ///
    /// <para>🔴 <b>T0-19 — THE DATE IS A REQUIRED ARGUMENT, and the date-blind two-argument overload that used to
    /// sit here is DELETED.</b> It forwarded <c>voucherDate: null</c>, and its whole observable behaviour was to
    /// silently skip the dated override — which is how both POS resolutions came to bill the counter at the
    /// pre-revision rate while every accounting screen billed the same item, on the same day, at the revised one.
    /// An overload whose only effect is to drop the date is a trap for the next caller, and it left no trace at
    /// the call site for a reader (or a grep) to catch. A caller that genuinely has no date must now write
    /// <c>voucherDate: null</c> and mean it.</para>
    /// </summary>
    public RateResolution ResolveRate(StockItem? item, Domain.Ledger? salesPurchaseLedger, DateOnly? voucherDate)
    {
        var baseRes = ResolveBase(item, salesPurchaseLedger);

        // The RateHistory test comes FIRST on purpose: a book with no dated rows (every Phase-4/8 company, and any
        // advanced book that never opted in) must not pay for the classification walk at all — ER-13 byte-identical
        // when off, and the walk is the only clause here that can cost or throw.
        if (voucherDate is { } d && baseRes.IsTaxable
            && _company.Gst?.RateHistory is { Count: > 0 } history
            && ResolveHsnSac(item, salesPurchaseLedger) is { } hsn)
        {
            var hit = history
                .Where(h => h.HsnSac == hsn && h.IsEffectiveOn(d))
                .OrderByDescending(h => h.EffectiveFrom).ThenByDescending(h => h.Id)
                .FirstOrDefault();
            if (hit is not null)
                return RateResolution.Taxable(hit.RateBasisPoints) with { ValuationBasis = hit.ValuationBasis };
        }

        return baseRes;
    }

    /// <summary>One rung of the hierarchy, flattened so the five masters share one loop. <see cref="Detailed"/> is
    /// non-null only for the two rungs that carry a <see cref="StockItemGstDetails"/> — see
    /// <see cref="ResolveDetailBlock"/> for why that distinction is load-bearing.</summary>
    private readonly record struct Rung(
        bool IsTaxable, GstTaxability Taxability, int? RateBasisPoints,
        GstValuationBasis ValuationBasis, StockItemGstDetails? Detailed, string? HsnSac);

    /// <summary>The five masters that can declare a GST block, named so an ORDER can be expressed as data.</summary>
    private enum HierarchyLevel { Ledger, AccountingGroup, StockItem, StockGroup, Company }

    /// <summary>
    /// <b>VENDOR, VERBATIM — <c>Ledger → Accounting Group → Stock Item → Stock Group → Company</c></b>
    /// ([web], help.tallysolutions.com "HSN/SAC &amp; GST Rate Hierarchy in TallyPrime"). The reference
    /// application's shipped default, selected by <see cref="GstDetailSource.LedgerFirst"/>, and what every company
    /// created on schema v51 or later carries.
    /// </summary>
    private static readonly IReadOnlyList<HierarchyLevel> LedgerFirstWalk = new[]
    {
        HierarchyLevel.Ledger, HierarchyLevel.AccountingGroup, HierarchyLevel.StockItem,
        HierarchyLevel.StockGroup, HierarchyLevel.Company,
    };

    /// <summary>
    /// <b>VENDOR, VERBATIM — <c>Stock Item → Stock Group → Ledger → Accounting Group → Company</c></b>
    /// (same source). The selectable alternative, <see cref="GstDetailSource.StockItemFirst"/>, and the value
    /// <c>Schema.MigrateV50ToV51</c> back-fills onto every pre-existing book because it puts the Stock Item above
    /// the Ledger, which is how this application resolved before the hierarchy columns existed.
    /// </summary>
    private static readonly IReadOnlyList<HierarchyLevel> StockItemFirstWalk = new[]
    {
        HierarchyLevel.StockItem, HierarchyLevel.StockGroup, HierarchyLevel.Ledger,
        HierarchyLevel.AccountingGroup, HierarchyLevel.Company,
    };

    /// <summary>
    /// The walk a book resolves by. <b>Two ordered LISTS and one loop, never two hand-written walks</b> — the four
    /// master-block rate readers that bypass <see cref="ResolveRate"/> today came to disagree with it precisely
    /// because each re-wrote the precedence for itself.
    ///
    /// <para>A book with no <see cref="GstConfig"/> at all resolves by the shipped default. That is the same
    /// constant <see cref="GstConfig.SourceOfGstRate"/> initialises to, kept in ONE place: a second default here
    /// would be a second answer to "what does a book that never said resolve by?".</para>
    /// </summary>
    private static IReadOnlyList<HierarchyLevel> WalkFor(GstDetailSource source) => source switch
    {
        GstDetailSource.LedgerFirst => LedgerFirstWalk,
        GstDetailSource.StockItemFirst => StockItemFirstWalk,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown GstDetailSource"),
    };

    /// <summary>
    /// THE GST RATE HIERARCHY — the rungs that declare a block, in walk order, top first. Rungs that declare
    /// nothing are absent, so "the first rung along the walk that carries a block" is simply the first element.
    ///
    /// <para><b>T0-4 slice S2b — the walk is the one <see cref="GstConfig.SourceOfGstRate"/> names, and the two
    /// orders are DATA (<see cref="LedgerFirstWalk"/> / <see cref="StockItemFirstWalk"/>) driving a single loop.</b>
    /// S1 built the oracle, S2a appended the three rungs this application could not previously see, and S2b makes
    /// the persisted column steer the resolver. It is the one money-moving change in the design: on a book where
    /// the Stock Item AND the resolved Sales/Purchase Ledger both declare a block, the rate a new line resolves
    /// changes.</para>
    ///
    /// <para>🔴 <b>R12 — USER RULING, verbatim (recorded this session):</b> "on books created from v51 onward the
    /// SALES/PURCHASE LEDGER OUTRANKS THE STOCK ITEM — honour the LedgerFirst order the column already defaults to,
    /// flipping today's item-first walk." The alternative — keeping item-first and treating the column as a
    /// stored-but-unused label — was put to the user and rejected.</para>
    ///
    /// <para><b>What this does NOT do: re-rate a posted voucher.</b> <see cref="GstLineTax"/> stamps the rate and
    /// the taxable value onto the tax <see cref="EntryLine"/> at post time and every report, payload and print
    /// reads them back, so flipping the order moves no posted figure — proved, not asserted, by
    /// <c>GstSourceOrderExistingBookTests.Flipping_the_source_order_moves_no_posted_figure</c>. Every pre-v51 book
    /// is additionally back-filled to <see cref="GstDetailSource.StockItemFirst"/>, so it keeps resolving new lines
    /// exactly as it always did. 🔴 <b>The one thing that is NOT posted data is the DOCUMENT TITLE:</b>
    /// <c>GstReportSupport.IsBillOfSupply</c> re-resolves every stock line live, so on a voucher that posted no
    /// forward tax the flip can re-title already-issued paper. Anchoring it needs a posted taxability marker (a
    /// column, i.e. an escalation) — the exposure is pinned by name rather than hidden.</para>
    ///
    /// <para><b>R7 grounding, separated (ruling 9).</b> VENDOR-attested [web], help.tallysolutions.com "HSN/SAC
    /// &amp; GST Rate Hierarchy in TallyPrime": that these five levels form a hierarchy; the TWO ORDER STRINGS
    /// themselves, transcribed verbatim into the two lists above; that the walk STOPS at the first level carrying
    /// the detail ("TallyPrime first checks the ledger for the details. If not found there, it will move to the
    /// Group, then Stock Item, and so on"); and that the Company level is LAST in both. The corpus is silent —
    /// zero hits for a GST "hierarch*" across all ten PDFs — though it evidences four of the five levels
    /// individually and works the Stock Group level end to end at transaction time (GSTN PDF pp.130-135); the
    /// Accounting-Group level has zero corpus support and is [web] only.
    /// <b>OURS, unattested either way:</b> that the two group rungs climb ANCESTRY rather than reading the
    /// immediate parent (see <see cref="MasterAncestry"/>); that the "Ledger" rung is the SALES/PURCHASE ledger and
    /// the "Accounting Group" rung is THAT ledger's group chain, never the party's; that a non-Taxable taxability
    /// at one level SHORT-CIRCUITS the walk instead of being skipped (preserved from the pre-hierarchy resolver
    /// deliberately — changing it would quietly redefine the existing exempt-item tests rather than extend them);
    /// and the PARTIAL-BLOCK semantics, i.e. that a rung declaring a taxability but no rate stops the taxability
    /// question and not the rate walk.</para>
    ///
    /// <para><b>NARROWED, and named.</b> The three group/company rungs carry <see cref="MasterGstDetails"/>:
    /// HSN/SAC, taxability, rate and supply type, and none of Compensation-Cess, reverse charge or §17(5) ITC
    /// eligibility. A rate resolved at one of them therefore bears no cess and never fires reverse charge. Adding
    /// those fields is a schema change and so an escalation, not a design decision; the narrowing is pinned by
    /// <c>GstWinningBlockTests</c>.</para>
    /// </summary>
    private IEnumerable<Rung> Hierarchy(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        // 🔴 LAZY ON PURPOSE, and it is a correctness property rather than a performance one. The two group rungs
        // are the only ones that COST anything (an ancestry climb) and the only ones that can THROW (a cyclic
        // parent chain). Building all five eagerly would make a book with one corrupt group chain unpostable even
        // on lines whose own stock item answers immediately — a behaviour change on an existing book. This method
        // is an iterator and ResolveBase returns on the first hit, so a rung below the answer is never built.
        // Pinned by GstHierarchyAncestryTests.A_cycle_below_an_answering_item_rung_is_never_reached.
        foreach (var level in WalkFor(_company.Gst?.SourceOfGstRate ?? GstDetailSource.LedgerFirst))
        {
            var rung = level switch
            {
                HierarchyLevel.Ledger => Detailed(salesPurchaseLedger?.SalesPurchaseGst),
                HierarchyLevel.AccountingGroup =>
                    Narrow(MasterAncestry.NearestGroupGst(_company, salesPurchaseLedger?.GroupId)),
                HierarchyLevel.StockItem => Detailed(item?.Gst),
                HierarchyLevel.StockGroup =>
                    Narrow(MasterAncestry.NearestStockGroupGst(_company, item?.StockGroupId)),
                HierarchyLevel.Company => Narrow(_company.Gst?.DefaultGst),
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "unknown hierarchy level"),
            };
            if (rung is { } declared) yield return declared;
        }

        static Rung? Detailed(StockItemGstDetails? block) => block is null
            ? null
            : new Rung(block.IsTaxable, block.Taxability, block.RateBasisPoints, block.ValuationBasis, block,
                block.HsnSac);

        // MasterGstDetails declares no valuation basis — a narrow rung is always transaction-valued. It DOES carry
        // an HSN/SAC, which is why the dated override's key (ResolveHsnSac) can see three rungs the old two-rung
        // `item ?? ledger` pick never could.
        static Rung? Narrow(MasterGstDetails? block) => block is null
            ? null
            : new Rung(block.IsTaxable, block.Taxability, block.RateBasisPoints,
                GstValuationBasis.TransactionValue, Detailed: null, block.HsnSac);
    }

    /// <summary>
    /// 🔴 <b>T0-20 — THE CLASSIFICATION THE DATED OVERRIDE IS KEYED BY: the HSN/SAC of the first rung along
    /// <see cref="Hierarchy"/> that declares one.</b> The same walk, in the same order, that resolved the rate.
    ///
    /// <para><b>What this replaces and why it was wrong.</b> The dated override used to key on the hard-coded
    /// <c>item?.Gst?.HsnSac ?? salesPurchaseLedger?.SalesPurchaseGst?.HsnSac</c> — a two-rung, item-first choice
    /// that ignored <see cref="GstConfig.SourceOfGstRate"/> entirely and could not see the three narrow rungs at
    /// all. On a <see cref="GstDetailSource.LedgerFirst"/> book (the shipped default, and what every v51+ book
    /// carries) the base rate came from the LEDGER while the row that REPLACED it was matched on the ITEM's HSN.
    /// That is not a refinement of the walk; it is a second, inconsistent resolution, and it can substitute a rate
    /// belonging to a classification the line never resolved through.</para>
    ///
    /// <para><b>"First rung DECLARING an HSN", not "the rung that supplied the rate"</b> — the same rule
    /// <see cref="ResolveDetailBlock"/> already applies to cess and reverse charge, and for the same reason: the
    /// rate walk falls THROUGH a taxable, rate-less block to the rung below it, and a master that declares its
    /// classification but leaves its rate to the rung below is an ordinary shape. Keying "whichever rung supplied
    /// the rate" would read no HSN at all there.</para>
    ///
    /// <para>Whitespace-only is treated as absent, matching every other HSN reader in the tree.</para>
    ///
    /// <para>🔴 <b>THE CYCLE CATCH IS NARROW AND IT IS LOAD-BEARING — do not widen it and do not delete it.</b>
    /// <see cref="Hierarchy"/> is lazy precisely so that a corrupt (cyclic) group chain BELOW the rung that
    /// answered never makes an otherwise-fine line unpostable — a correctness property, not a performance one,
    /// pinned by <c>GstHierarchyAncestryTests.A_cycle_below_an_answering_item_rung_is_never_reached</c>. This walk
    /// looks one question further than the rate walk did, so on its own it would resurrect exactly that
    /// unpostable-book shape for any book carrying dated rows. It cannot mask a cycle at or above the answering
    /// rung: <see cref="ResolveBase"/> runs FIRST in <see cref="ResolveRate"/> and walks the same rungs, so such a
    /// chain has already thrown before this method is entered. What is swallowed here is therefore only ever a
    /// cycle strictly below the answer — and the consequence of swallowing it is the correct one: no dated
    /// override, the base rate stands, the line posts.</para>
    /// </summary>
    private string? ResolveHsnSac(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        try
        {
            foreach (var rung in Hierarchy(item, salesPurchaseLedger))
                if (!string.IsNullOrWhiteSpace(rung.HsnSac)) return rung.HsnSac;
        }
        catch (InvalidOperationException)
        {
            // A cyclic ancestry strictly BELOW the rung that answered the rate — see the note above.
        }

        return null;
    }

    /// <summary>
    /// The base (date-agnostic) rate resolution: walk <see cref="Hierarchy"/> top-down, stop at the first rung
    /// that either declares a non-Taxable taxability or supplies a rate. Split out so the date-aware overload
    /// layers a pure override on top without altering the base behaviour (ER-13).
    ///
    /// <para><b>The ER-5 unresolved sentinel now sits BEHIND the Company rung, not two rungs in front of it.</b>
    /// Company terminates both published order strings, so a book that set its rate exactly where the reference
    /// product tells a single-rate business to set it used to be hard-blocked from posting. The sentinel's SHAPE
    /// and <see cref="IsUnresolved"/> are unchanged, so every posting caller's fail-fast is untouched: a taxable
    /// line with a rate at no level at all is still an explicit domain error and never a silent zero.</para>
    /// </summary>
    private RateResolution ResolveBase(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        foreach (var rung in Hierarchy(item, salesPurchaseLedger))
        {
            if (!rung.IsTaxable) return RateResolution.NonTaxable(rung.Taxability);
            if (rung.RateBasisPoints is { } bp)
                return RateResolution.Taxable(bp) with { ValuationBasis = rung.ValuationBasis };
        }

        return new RateResolution(false, -1, GstTaxability.Taxable); // sentinel: unresolved (IsTaxable=false, bp=-1)
    }

    /// <summary>
    /// ONE WALK, ONE WINNING BLOCK — the <see cref="StockItemGstDetails"/> at the <b>first rung of
    /// <see cref="Hierarchy"/> that declares a block</b>, or <c>null</c> when that rung carries a narrow
    /// <see cref="MasterGstDetails"/> (which has no cess and no reverse-charge fields) or when no rung declares
    /// anything. <see cref="ResolveCess"/> and <c>RcmService</c> consume this instead of re-picking a level for
    /// themselves, so a line can no longer be RATED off one master while its cess and its reverse-charge category
    /// are read off another.
    ///
    /// <para><b>Note the rule is "first rung DECLARING a block", not "the rung that supplied the rate".</b> The
    /// rate walk falls THROUGH a taxable, rate-less block to the rung below it; cess and reverse charge must not.
    /// A stock item that declares cess but leaves its rate to the sales ledger is an ordinary shape, and feeding
    /// cess "whichever rung supplied the rate" would read the wrong master there.</para>
    ///
    /// <para><b>Slice S2b steers this too, and that is the point of having one walk.</b> Under
    /// <see cref="GstDetailSource.LedgerFirst"/> a line whose item and sales ledger both declare a block takes its
    /// cess and its reverse-charge category from the LEDGER, because that is the rung the rate came from. Under
    /// <see cref="GstDetailSource.StockItemFirst"/> with no group/company block populated — which is every book
    /// outside canonical import — this still reduces term for term to the pre-hierarchy
    /// <c>item?.Gst ?? ledger?.SalesPurchaseGst</c>, so no migrated book's cess changes master. Both halves are
    /// asserted across every combination in <c>GstWinningBlockTests</c> rather than argued.</para>
    /// </summary>
    public StockItemGstDetails? ResolveDetailBlock(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        foreach (var rung in Hierarchy(item, salesPurchaseLedger)) return rung.Detailed;
        return null;
    }

    /// <summary>
    /// Resolves the Compensation-Cess charge for a line as of a voucher date (Phase 9 slice 1; RQ-2/RQ-9), or
    /// <c>null</c> when the line bears no cess. An <b>Exempt/Nil-Rated/Non-GST line bears no cess</b> even when it
    /// shares a cess HSN (mirrors the taxability short-circuit in <see cref="ResolveBase"/>): cess never over-collects
    /// on an exempt supply. Otherwise a per-item explicit override (<c>CessApplicable</c> + a <c>CessValuationMode</c>)
    /// wins; else a matching HSN-dated <see cref="GstConfig.CessRates"/> row supplies the charge (most-recently-
    /// effective wins). No matching row and no override ⇒ <c>null</c> (zero cess) — so a 40%-de-merit item after
    /// 22-Sep-2025, or any item with no cess row, computes zero cess automatically (ER-2). An RSP-factor cess whose
    /// item declares no Retail Sale Price is a <b>fail-fast</b> domain error (never a silent ₹0), see
    /// <see cref="BuildCess"/>.
    /// </summary>
    public CessCharge? ResolveCess(
        StockItem? item, Domain.Ledger? salesPurchaseLedger, DateOnly voucherDate, decimal quantity)
    {
        // T0-4 S2a: consume the level the hierarchy walk landed on rather than re-picking one here. Under S2a's
        // walk this is exactly the pre-S2a `item?.Gst ?? salesPurchaseLedger?.SalesPurchaseGst`; a rate resolved
        // at a narrow (group / company) rung yields null, and a narrow rung carries no cess fields anyway.
        var gst = ResolveDetailBlock(item, salesPurchaseLedger);

        // An Exempt/Nil/Non-GST (or absent) block attracts no tax at all — and therefore no cess — even on a cess HSN.
        if (gst is null || !gst.IsTaxable) return null;

        // Per-item explicit override (the item declares its own cess mode + figures).
        if (gst is { CessApplicable: true, CessValuationMode: { } mode })
            return BuildCess(mode,
                gst.CessRateBasisPoints ?? 0,
                gst.CessPerUnit ?? Money.Zero,
                gst.CessRspFactorMillis ?? 0,
                gst.RetailSalePrice,
                quantity);

        // Else inherit from the dated cess master by HSN.
        if (gst.HsnSac is { } hsn && _company.Gst?.CessRates is { Count: > 0 } rates)
        {
            var hit = rates
                .Where(r => r.HsnSac == hsn && r.IsEffectiveOn(voucherDate))
                .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id)
                .FirstOrDefault();
            if (hit is not null)
                return BuildCess(hit.ValuationMode, hit.CessRateBasisPoints, hit.CessPerUnit,
                    hit.CessRspFactorMillis, gst.RetailSalePrice, quantity);
        }

        return null;
    }

    /// <summary>
    /// Assembles a <see cref="CessCharge"/>, <b>failing fast</b> when the effective valuation is
    /// <see cref="CessValuationMode.RetailSalePriceFactor"/> but no Retail Sale Price is available. An
    /// inherited RSP-factor cess (the item leaves <c>CessValuationMode</c> null, so <c>EnsureValid</c> never
    /// enforces an RSP) would otherwise value a legitimately cess-bearing pan-masala/chewing-tobacco item at a
    /// silent ₹0 — a systematic under-collection. Mirrors the unresolved-rate fail-fast contract (ER-5): a
    /// missing valuation input is a clear domain error, never a hidden zero.
    /// </summary>
    private static CessCharge BuildCess(
        CessValuationMode mode, int rateBasisPoints, Money perUnit, int rspFactorMillis, Money? retailSalePrice, decimal quantity)
    {
        if (mode == CessValuationMode.RetailSalePriceFactor && retailSalePrice is null)
            throw new InvalidOperationException(
                "RSP-factor Compensation-Cess requires a declared Retail Sale Price on the item, but none is set — "
                + "cannot value the cess (refusing to post a silent ₹0 cess).");

        return new CessCharge(mode, rateBasisPoints, perUnit, rspFactorMillis, retailSalePrice ?? Money.Zero, quantity);
    }

    /// <summary>True iff <paramref name="r"/> is the "unresolved" sentinel (a taxable line with no rate anywhere).</summary>
    public static bool IsUnresolved(RateResolution r) => r is { IsTaxable: false, RateBasisPoints: -1, Taxability: GstTaxability.Taxable };

    // ---- RQ-12/13/19: per-line tax computation + split + rounding ----

    /// <summary>
    /// The paisa-exact tax on a taxable value at a rate (basis points). Amount = V × bp / 10000, rounded to the
    /// paisa away-from-zero (<see cref="Money.RoundToPaisa"/>) — the defined per-line rounding (DP-4). Used to
    /// compute the line's <b>total</b> tax once (at the full integrated bp, the correct IGST amount); the intra
    /// CGST/SGST split is then derived from that total so <c>CGST + SGST == total == IGST</c> by construction
    /// (RQ-12/L-4). It is <b>never</b> called with a half-bp — half-bp per-head rounding drifts ±0.01 on odd
    /// sub-paisa tails, which is the very defect this total-then-split design eliminates.
    /// </summary>
    public static Money TaxAmount(Money taxableValue, int headBasisPoints) =>
        new Money(taxableValue.Amount * headBasisPoints / 10000m).RoundToPaisa();

    /// <summary>One computed taxable line's GST split (the per-line breakdown that Tax Analysis shows).</summary>
    public readonly record struct LineTax(
        Money TaxableValue, int IntegratedBasisPoints, bool InterState,
        Money Cgst, Money Sgst, Money Igst)
    {
        /// <summary>Total tax on this line (CGST+SGST intra, or IGST inter).</summary>
        public Money Total => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
    }

    /// <summary>
    /// Computes the per-line GST split for one taxable line (RQ-12) using the <b>compute-total-then-split</b>
    /// method: the line's total tax is computed <b>once</b> = round_paisa(V × rate) — the correct IGST amount —
    /// then intra ⇒ <c>CGST = round_paisa(total / 2)</c>, <c>SGST = total − CGST</c>; inter ⇒ IGST = that total.
    /// This guarantees <c>CGST + SGST == total == IGST</c> to the paisa (footing/parity invariant, L-4), instead
    /// of rounding each half-rate head independently (which drifted ±0.01 on odd sub-paisa tails and corrupted
    /// GSTR-1 intra-vs-inter / GSTR-3B reconciliation). CGST == SGST in the normal (even-total) case; on an odd
    /// total they legitimately differ by exactly 1 paisa (SGST carries the remainder). The two non-applicable
    /// heads are zero.
    /// </summary>
    public static LineTax ComputeLineTax(Money taxableValue, int integratedBasisPoints, bool interState)
    {
        // The line's total tax, computed once — this IS the correct IGST amount (single paisa rounding).
        var total = TaxAmount(taxableValue, integratedBasisPoints);

        if (interState)
            return new LineTax(taxableValue, integratedBasisPoints, true, Money.Zero, Money.Zero, total);

        // Intra: split the SAME total in two so CGST + SGST == total == IGST by construction. CGST takes the
        // rounded half; SGST carries the remainder (so on an odd total SGST is 1 paisa larger — the correct,
        // deterministic behavior). For an even total CGST == SGST.
        var cgst = new Money(total.Amount / 2m).RoundToPaisa();
        var sgst = new Money(total.Amount - cgst.Amount);
        return new LineTax(taxableValue, integratedBasisPoints, false, cgst, sgst, Money.Zero);
    }

    /// <summary>
    /// A resolved Compensation-Cess charge on a taxable line (Phase 9 slice 1; RQ-2/RQ-9). Carries the valuation mode
    /// and the figures needed to value it; <see cref="ComputeCess"/> computes the amount <b>once</b>, rounded to the
    /// paisa (never per sub-unit — that would drift ±0.01 on odd tails, the recurring A10 finding).
    /// </summary>
    public readonly record struct CessCharge(
        CessValuationMode Mode, int RateBasisPoints, Money PerUnit,
        int RspFactorMillis, Money RetailSalePrice, decimal Quantity)
    {
        /// <summary>The paisa-exact cess amount for <paramref name="taxableValue"/>, computed once and rounded once.</summary>
        public Money ComputeCess(Money taxableValue) =>
            new Money(CessBeforeRounding(taxableValue)).RoundToPaisa();

        /// <summary>
        /// 🔴 The cess for <paramref name="taxableValue"/> <b>before the paisa snap</b> — the figure a caller that
        /// aggregates several lines into ONE posted cess leg must accumulate, so the group is rounded once rather
        /// than each contributing line being rounded and the roundings summed.
        ///
        /// <para><b>Why this exists.</b> <see cref="ComputeInvoiceTax"/> posts one Cess entry line per GST-rate
        /// group and rounds the CGST/SGST/IGST heads on that group's subtotal. Rounding the cess per LINE instead
        /// made Σ round(line) the posted cess where the heads use round(Σ line) — so re-deriving the SAME invoice
        /// from a different but value-identical line partition moved the figure. It is reachable without any
        /// import: an item line split across N batches posts as N inventory lines, the alteration screen rebuilds
        /// the grid one row per POSTED line, and the re-derivation then splits one grid row into N. The rounding
        /// boundary, not the valuation mode, is what has to be invariant — every mode is affected, because every
        /// mode ends in one <c>RoundToPaisa</c>.</para>
        ///
        /// <para>No statutory claim is made here: this is a rounding-BOUNDARY choice, made to match the boundary
        /// the GST heads beside it already use (DP-4 / RQ-12), not a rate or a threshold.</para>
        /// </summary>
        public decimal CessBeforeRounding(Money taxableValue) => Mode switch
        {
            CessValuationMode.AdValorem => taxableValue.Amount * RateBasisPoints / 10000m,
            CessValuationMode.Specific => Quantity * PerUnit.Amount,
            CessValuationMode.RetailSalePriceFactor => Quantity * RetailSalePrice.Amount * RspFactorMillis / 1000m,
            _ => 0m,
        };
    }

    /// <summary>One input taxable line for <see cref="ComputeInvoiceTax"/>: a taxable value at an integrated rate, plus
    /// an optional resolved <see cref="CessCharge"/> (Phase 9 slice 1). The optional default keeps every existing
    /// <c>new TaxableLine(value, bp)</c> construction valid (ER-13).</summary>
    public readonly record struct TaxableLine(Money TaxableValue, int IntegratedBasisPoints, CessCharge? Cess = null);

    /// <summary>The full GST result for an invoice: the per-head tax lines to post + the per-line breakdown.</summary>
    public sealed class InvoiceTax
    {
        /// <summary>The additive tax entry lines (to the Output/Input tax ledgers), aggregated per head.</summary>
        public required IReadOnlyList<EntryLine> TaxLines { get; init; }

        /// <summary>The optional invoice round-off entry line (nearest-rupee), or <c>null</c> when none.</summary>
        public EntryLine? RoundOffLine { get; init; }

        /// <summary>The per-line GST breakdown (Tax Analysis, RQ-20).</summary>
        public required IReadOnlyList<LineTax> LineBreakdown { get; init; }

        /// <summary>Σ CGST over the invoice.</summary>
        public Money TotalCgst { get; init; }
        /// <summary>Σ SGST over the invoice.</summary>
        public Money TotalSgst { get; init; }
        /// <summary>Σ IGST over the invoice.</summary>
        public Money TotalIgst { get; init; }

        /// <summary>
        /// Σ Compensation Cess over the invoice (Phase 9 slice 1). <b>Ring-fenced</b>: kept OUT of
        /// <see cref="TotalTax"/> (which stays CGST+SGST+IGST) so cess never mingles with the GST heads (ER-2), but it
        /// IS added into the round-off grand total so a cess-bearing voucher balances.
        /// </summary>
        public Money TotalCess { get; init; }

        /// <summary>Σ all GST tax (CGST+SGST+IGST) over the invoice — <b>excludes</b> cess (ring-fence, ER-2).</summary>
        public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

        /// <summary>The round-off adjustment applied to the grand total (0 when no round-off), signed.</summary>
        public Money RoundOffAmount { get; init; }
    }

    /// <summary>
    /// Computes the additive GST for an invoice (RQ-12/13/19): per-line CGST/SGST split (intra) or IGST (inter),
    /// posted as <b>one entry line per (tax head, GST rate) group</b> (to the correct Output/Input tax ledger by
    /// <paramref name="direction"/>, DP-11), with paisa-exact per-line rounding. Lines are grouped by their
    /// resolved integrated rate; each rate group's tax is computed on that group's taxable subtotal with the
    /// same <b>compute-total-then-split</b> rule (so per group CGST+SGST == IGST == round(subtotal × rate), CGST
    /// == SGST bar a forced 1-paisa remainder on an odd total). A single-rate invoice therefore collapses to one
    /// line per head exactly as before; a multi-rate invoice keeps per-rate identity so GSTR-1 rate/HSN and Tax
    /// Analysis attribute the tax to the correct rate — each tax line carries its OWN group's correct
    /// <see cref="GstLineTax.RateBasisPoints"/> (the head's half-rate for CGST/SGST, the full rate for IGST) and
    /// that group's taxable subtotal (never a blended 0%). When <paramref name="applyInvoiceRoundOff"/> is set,
    /// the grand total (taxable + tax) is rounded to the nearest rupee and the difference is returned as a
    /// Round-Off entry line so the voucher can stay balanced (RQ-17). The caller assembles the full voucher:
    /// party (Dr/Cr taxable+tax±roundoff), stock/sales legs, these tax lines and the round-off line.
    /// </summary>
    public InvoiceTax ComputeInvoiceTax(
        IReadOnlyList<TaxableLine> lines,
        bool interState,
        GstTaxDirection direction,
        bool applyInvoiceRoundOff = false)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Phase 9 slice 3 (RQ-10; ER-4): a Composition dealer issues a Bill of Supply — it neither collects output GST
        // (outward) nor avails ITC (inward). Suppress ALL forward CGST/SGST/IGST/Cess: no tax lines, no round-off, zero
        // totals. The supply value flows untaxed to the party leg (the caller assembles party Dr = supply value). A
        // Regular/Unregistered company never enters this branch ⇒ byte-identical (ER-13). Inward RCM is NOT computed here
        // (it flows through RcmService, which still posts the composition dealer's cash-only RCM liability).
        if (_company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new InvoiceTax { TaxLines = [], LineBreakdown = [] };

        var breakdown = new List<LineTax>(lines.Count);
        var taxableTotal = 0m;

        // Accumulate the taxable subtotal per integrated rate (in the input line order of first appearance), so a
        // multi-rate invoice posts one (head, rate) tax line per rate group — each on its own subtotal.
        var rateOrder = new List<int>();
        var taxableByRate = new Dictionary<int, decimal>();

        // Phase 9 slice 1: Compensation Cess is accumulated per rate group alongside the GST heads. One Cess entry
        // line per group posts to the ring-fenced Output/Input Cess ledger. cessBpByRate carries the group's
        // ad-valorem bp for the GstLineTax detail (0 when the group is specific/RSP or mixed — reports read the
        // amount, ER-9).
        //
        // 🔴 THE ACCUMULATOR HOLDS THE UNROUNDED CESS, and the group is snapped to the paisa ONCE below — the same
        // boundary the CGST/SGST/IGST heads are rounded at (round(Σ line), never Σ round(line)). It used to hold
        // the per-line ROUNDED figure, which made the posted cess depend on HOW the invoice's value was partitioned
        // into lines rather than only on what it was worth: re-deriving the identical invoice from a value-identical
        // but differently-partitioned line set moved the cess and, with it, the party's balance. See
        // <see cref="CessCharge.CessBeforeRounding"/> for the reachable route (a batch-split line rehydrated flat).
        var cessByRate = new Dictionary<int, decimal>();
        var cessBpByRate = new Dictionary<int, int?>();

        foreach (var line in lines)
        {
            // Per-line breakdown feeds Tax Analysis' LineBreakdown display; the posted tax, however, is computed
            // ONCE per rate group below (compute-total-then-split on the group subtotal), so a multi-line same-rate
            // group foots to round(subtotal × rate), not Σ round(line × rate).
            breakdown.Add(ComputeLineTax(line.TaxableValue, line.IntegratedBasisPoints, interState));
            taxableTotal += line.TaxableValue.Amount;

            if (!taxableByRate.ContainsKey(line.IntegratedBasisPoints))
            {
                taxableByRate[line.IntegratedBasisPoints] = 0m;
                cessByRate[line.IntegratedBasisPoints] = 0m;
                cessBpByRate[line.IntegratedBasisPoints] = null;
                rateOrder.Add(line.IntegratedBasisPoints);
            }
            taxableByRate[line.IntegratedBasisPoints] += line.TaxableValue.Amount;

            if (line.Cess is { } cess)
            {
                // UNROUNDED — the group is rounded once, below, exactly as the GST heads are.
                cessByRate[line.IntegratedBasisPoints] += cess.CessBeforeRounding(line.TaxableValue);
                // Track a representative ad-valorem bp for the group's cess detail; a mixed group falls back to 0.
                var lineCessBp = cess.Mode == CessValuationMode.AdValorem ? cess.RateBasisPoints : 0;
                cessBpByRate[line.IntegratedBasisPoints] =
                    cessBpByRate[line.IntegratedBasisPoints] is { } prior && prior != lineCessBp ? 0 : lineCessBp;
            }
        }

        // 🔴 THE CESS ROUNDING BOUNDARY. Each rate group's accumulated (unrounded) cess is snapped to the paisa
        // ONCE, here — so the posted cess is round(Σ line), matching round(Σ line) on the heads beside it, and the
        // figure depends on the invoice's VALUE rather than on the line partition that produced it.
        var cessRoundedByRate = new Dictionary<int, decimal>();
        foreach (var bp in rateOrder)
            cessRoundedByRate[bp] = new Money(cessByRate[bp]).RoundToPaisa().Amount;
        var totalCess = cessRoundedByRate.Values.Sum();

        // Aggregate per (head, rate) group, on the correct side: Output tax is a credit (liability) on a sale;
        // Input tax is a debit (ITC asset) on a purchase. The tax-ledger side mirrors the party side.
        var taxSide = direction == GstTaxDirection.Output ? DrCr.Credit : DrCr.Debit;
        var taxLines = new List<EntryLine>();
        var cgst = 0m; var sgst = 0m; var igst = 0m;

        void AddHead(GstTaxHead head, decimal amount, int headBp, decimal groupTaxable)
        {
            if (amount == 0m) return;
            var ledger = FindTaxLedger(head, direction)
                ?? throw new InvalidOperationException(
                    $"GST tax ledger for {head}/{direction} not found — enable GST first (EnableGst auto-creates it).");
            taxLines.Add(new EntryLine(
                ledger.Id, new Money(amount), taxSide,
                gst: new GstLineTax(head, headBp, new Money(groupTaxable).RoundToPaisa())));
        }

        // Phase 9 slice 1: create the Output/Input Cess ledgers LAZILY — only when a cess line is about to post (never
        // unconditionally in EnableGst, which would give every GST company two extra ledgers and break the Phase-4
        // fixtures + off-company byte-identity, ER-13). Idempotent, so an imported/ad-hoc cess line always finds its
        // ring-fenced ledger.
        if (totalCess != 0m)
        {
            var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
                ?? throw new InvalidOperationException("Seed missing 'Duties & Taxes' group; cannot auto-create Cess ledgers.");
            EnsureCessLedgers(dutiesAndTaxes.Id);
        }

        // One tax line per (head, rate) group. Each group re-runs the compute-total-then-split on its own subtotal
        // so CGST+SGST == IGST == round(subtotal × rate) per rate, carrying that rate's correct head basis points.
        // The head totals are summed from the POSTED group amounts so TotalCgst/Sgst/Igst == Σ posted tax lines
        // (they reconcile to the tax-ledger postings to the paisa, even across a multi-line same-rate group).
        foreach (var integratedBp in rateOrder)
        {
            var groupTaxable = taxableByRate[integratedBp];
            var groupTax = ComputeLineTax(new Money(groupTaxable), integratedBp, interState);
            var halfBp = integratedBp / 2;
            if (interState)
            {
                AddHead(GstTaxHead.Integrated, groupTax.Igst.Amount, integratedBp, groupTaxable);
                igst += groupTax.Igst.Amount;
            }
            else
            {
                AddHead(GstTaxHead.Central, groupTax.Cgst.Amount, halfBp, groupTaxable);
                AddHead(GstTaxHead.State, groupTax.Sgst.Amount, halfBp, groupTaxable);
                cgst += groupTax.Cgst.Amount;
                sgst += groupTax.Sgst.Amount;
            }

            // Ring-fenced Cess: one entry line per rate group, on the same side as the GST heads (Output on a sale,
            // Input on a purchase). It carries its OWN group's cess base + representative ad-valorem bp (0 for
            // specific/RSP), and NEVER touches the CGST/SGST/IGST totals (ER-2).
            AddHead(GstTaxHead.Cess, cessRoundedByRate[integratedBp], cessBpByRate[integratedBp] ?? 0, groupTaxable);
        }

        // Optional invoice round-off on the grand total (taxable + tax + cess so a cess-bearing voucher balances).
        EntryLine? roundOffLine = null;
        var roundOff = 0m;
        if (applyInvoiceRoundOff)
        {
            var grand = taxableTotal + cgst + sgst + igst + totalCess;
            var rounded = Math.Round(grand, 0, MidpointRounding.AwayFromZero);
            roundOff = rounded - grand; // signed; + means we add to reach the rupee, − means we shave
            if (roundOff != 0m)
            {
                var roLedger = _company.FindLedgerByName(RoundOffLedgerName)
                    ?? throw new InvalidOperationException("Round-Off ledger not found — enable GST first.");
                // If roundOff > 0 the grand total rose (party pays more) ⇒ on a sale the extra is income:
                // party Dr rises, so Round Off is a credit; on a purchase the party Cr rises, Round Off debit.
                // We express the round-off as a line balancing the extra taxable+tax vs the rounded party total.
                // Convention: Round Off carries the residual so Σ Dr = Σ Cr with the party at the rounded total.
                var roMagnitude = new Money(Math.Abs(roundOff)).RoundToPaisa();
                var roSide = RoundOffSide(direction, roundOff > 0m);
                roundOffLine = new EntryLine(roLedger.Id, roMagnitude, roSide);
            }
        }

        return new InvoiceTax
        {
            TaxLines = taxLines,
            RoundOffLine = roundOffLine,
            LineBreakdown = breakdown,
            TotalCgst = new Money(cgst),
            TotalSgst = new Money(sgst),
            TotalIgst = new Money(igst),
            TotalCess = new Money(totalCess),
            RoundOffAmount = new Money(roundOff),
        };
    }

    /// <summary>
    /// The side of the Round-Off line. On a <b>sale</b> the party is a debit; if the rounded total is higher
    /// (<paramref name="totalRoseToRupee"/>) the party Dr grows, so Round Off must be a credit to balance (a
    /// small rounding income), and vice-versa. On a <b>purchase</b> the party is a credit, so the sides invert.
    /// </summary>
    private static DrCr RoundOffSide(GstTaxDirection direction, bool totalRoseToRupee)
    {
        if (direction == GstTaxDirection.Output) // sale: party Dr
            return totalRoseToRupee ? DrCr.Credit : DrCr.Debit;
        // purchase: party Cr
        return totalRoseToRupee ? DrCr.Debit : DrCr.Credit;
    }
}
