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
///   <item><see cref="ResolveRate"/> — Stock Item → Sales/Purchase Ledger → Company rate resolution
///     (most-granular-wins, DP-6; ER-5 pure &amp; total).</item>
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
    /// </summary>
    public bool IsInterState(string? partyStateCode)
    {
        var home = _company.Gst?.HomeStateCode;
        if (home is null) throw new InvalidOperationException("GST is not enabled (no home state) — cannot route a supply.");
        // No recorded place of supply ⇒ default to the company home State ⇒ intra-state (B2C local sale, DP-8).
        if (string.IsNullOrWhiteSpace(partyStateCode)) return false;
        return !string.Equals(home, partyStateCode, StringComparison.Ordinal);
    }

    // ---- RQ-10: rate resolution (Stock Item → Sales/Purchase Ledger → Company), most-granular-wins ----

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
    /// Resolves the effective GST rate for a line from (stock item, sales/purchase ledger, company), the
    /// <b>most granular non-null wins</b> (DP-6). An Exempt/Nil/Non-GST taxability at any level short-circuits
    /// to a non-taxable result (zero tax, RQ-15). A taxable line whose rate cannot be resolved anywhere is an
    /// explicit "unresolved" — the caller fails fast (ER-5); it is never a silent zero. This date-agnostic overload
    /// delegates to the dated overload with <c>voucherDate = null</c>, so a caller with no date is unchanged.
    /// </summary>
    public RateResolution ResolveRate(StockItem? item, Domain.Ledger? salesPurchaseLedger)
        => ResolveRate(item, salesPurchaseLedger, voucherDate: null);

    /// <summary>
    /// Resolves the effective GST rate <b>as of a voucher date</b> (Phase 9 slice 1; RQ-1). It first resolves exactly
    /// as Phase-4/8 (<see cref="ResolveBase"/>), then applies a <b>pure date override</b>: only when a voucher date
    /// <b>and</b> a matching HSN-dated <see cref="GstConfig.RateHistory"/> row both exist does it return the dated
    /// rate (most-recently-effective wins). Absent either — every existing fixture (a date but no history rows) — it
    /// returns the base result unchanged, byte-identical to Phase-4/8 (ER-13). Legacy 12/28% rows retained
    /// inactive-by-date let a pre-22-Sep-2025 voucher reprint at the historic rate.
    /// </summary>
    public RateResolution ResolveRate(StockItem? item, Domain.Ledger? salesPurchaseLedger, DateOnly? voucherDate)
    {
        var baseRes = ResolveBase(item, salesPurchaseLedger);

        if (voucherDate is { } d && baseRes.IsTaxable
            && (item?.Gst?.HsnSac ?? salesPurchaseLedger?.SalesPurchaseGst?.HsnSac) is { } hsn
            && _company.Gst?.RateHistory is { Count: > 0 } history)
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

    /// <summary>The Phase-4/8 rate resolution (item → ledger → unresolved), unchanged. Split out so the date-aware
    /// overload layers a pure override on top without altering the base behaviour (ER-13).</summary>
    private RateResolution ResolveBase(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        // 1) Stock Item (most granular).
        if (item?.Gst is { } itemGst)
        {
            if (!itemGst.IsTaxable) return RateResolution.NonTaxable(itemGst.Taxability);
            if (itemGst.RateBasisPoints is { } ir) return RateResolution.Taxable(ir) with { ValuationBasis = itemGst.ValuationBasis };
        }

        // 2) Sales/Purchase ledger.
        if (salesPurchaseLedger?.SalesPurchaseGst is { } ledgerGst)
        {
            if (!ledgerGst.IsTaxable) return RateResolution.NonTaxable(ledgerGst.Taxability);
            if (ledgerGst.RateBasisPoints is { } lr) return RateResolution.Taxable(lr) with { ValuationBasis = ledgerGst.ValuationBasis };
        }

        // 3) Company default: the single seeded slab if exactly one taxable slab is configured, else unresolved.
        // (Phase 4 has no single "company default rate" field; the company level contributes only the slab set,
        // so a taxable line with no item/ledger rate is unresolved — a fail-fast domain error, ER-5.)
        return new RateResolution(false, -1, GstTaxability.Taxable); // sentinel: unresolved (IsTaxable=false, bp=-1)
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
        var gst = item?.Gst ?? salesPurchaseLedger?.SalesPurchaseGst;

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
        public Money ComputeCess(Money taxableValue) => Mode switch
        {
            CessValuationMode.AdValorem =>
                new Money(taxableValue.Amount * RateBasisPoints / 10000m).RoundToPaisa(),
            CessValuationMode.Specific =>
                new Money(Quantity * PerUnit.Amount).RoundToPaisa(),
            CessValuationMode.RetailSalePriceFactor =>
                new Money(Quantity * RetailSalePrice.Amount * RspFactorMillis / 1000m).RoundToPaisa(),
            _ => Money.Zero,
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

        // Phase 9 slice 1: Compensation Cess is accumulated per rate group alongside the GST heads. Each line's cess
        // is computed + rounded ONCE (CessCharge.ComputeCess), then summed into its rate group; one Cess entry line
        // per group posts to the ring-fenced Output/Input Cess ledger. cessBpByRate carries the group's ad-valorem bp
        // for the GstLineTax detail (0 when the group is specific/RSP or mixed — reports read the amount, ER-9).
        var cessByRate = new Dictionary<int, decimal>();
        var cessBpByRate = new Dictionary<int, int?>();
        var totalCess = 0m;

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
                var cessAmount = cess.ComputeCess(line.TaxableValue).Amount; // computed + rounded once per line
                cessByRate[line.IntegratedBasisPoints] += cessAmount;
                totalCess += cessAmount;
                // Track a representative ad-valorem bp for the group's cess detail; a mixed group falls back to 0.
                var lineCessBp = cess.Mode == CessValuationMode.AdValorem ? cess.RateBasisPoints : 0;
                cessBpByRate[line.IntegratedBasisPoints] =
                    cessBpByRate[line.IntegratedBasisPoints] is { } prior && prior != lineCessBp ? 0 : lineCessBp;
            }
        }

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
            AddHead(GstTaxHead.Cess, cessByRate[integratedBp], cessBpByRate[integratedBp] ?? 0, groupTaxable);
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
