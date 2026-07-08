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
    public static string TaxLedgerName(GstTaxHead head, GstTaxDirection direction)
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
        return $"{side} {headName}";
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
        foreach (var direction in new[] { GstTaxDirection.Output, GstTaxDirection.Input })
            foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State, GstTaxHead.Integrated })
                EnsureTaxLedger(dutiesAndTaxes.Id, head, direction);

        // Round-Off ledger under Indirect Expenses (a P&L head; a round-off can be Dr or Cr).
        EnsureRoundOffLedger();

        return config;
    }

    /// <summary>The tax ledger for a head + direction, or <c>null</c> if GST is not enabled / not created.</summary>
    public Domain.Ledger? FindTaxLedger(GstTaxHead head, GstTaxDirection direction) =>
        _company.Ledgers.FirstOrDefault(l =>
            l.GstClassification is { } c && c.TaxHead == head && c.Direction == direction);

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

    private void EnsureRoundOffLedger()
    {
        if (_company.FindLedgerByName(RoundOffLedgerName) is not null) return;
        var indirectExp = _company.FindGroupByName("Indirect Expenses")
            ?? throw new InvalidOperationException("Seed missing 'Indirect Expenses' group; cannot auto-create Round-Off ledger.");
        _company.AddLedger(new Domain.Ledger(
            Guid.NewGuid(), RoundOffLedgerName, indirectExp.Id, Money.Zero, openingIsDebit: true));
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

    /// <summary>The outcome of resolving a GST rate for a taxable line (ER-5: pure &amp; total).</summary>
    public readonly record struct RateResolution(bool IsTaxable, int RateBasisPoints, GstTaxability Taxability)
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
    /// explicit "unresolved" — the caller fails fast (ER-5); it is never a silent zero.
    /// </summary>
    public RateResolution ResolveRate(StockItem? item, Domain.Ledger? salesPurchaseLedger)
    {
        // 1) Stock Item (most granular).
        if (item?.Gst is { } itemGst)
        {
            if (!itemGst.IsTaxable) return RateResolution.NonTaxable(itemGst.Taxability);
            if (itemGst.RateBasisPoints is { } ir) return RateResolution.Taxable(ir);
        }

        // 2) Sales/Purchase ledger.
        if (salesPurchaseLedger?.SalesPurchaseGst is { } ledgerGst)
        {
            if (!ledgerGst.IsTaxable) return RateResolution.NonTaxable(ledgerGst.Taxability);
            if (ledgerGst.RateBasisPoints is { } lr) return RateResolution.Taxable(lr);
        }

        // 3) Company default: the single seeded slab if exactly one taxable slab is configured, else unresolved.
        // (Phase 4 has no single "company default rate" field; the company level contributes only the slab set,
        // so a taxable line with no item/ledger rate is unresolved — a fail-fast domain error, ER-5.)
        return new RateResolution(false, -1, GstTaxability.Taxable); // sentinel: unresolved (IsTaxable=false, bp=-1)
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

    /// <summary>One input taxable line for <see cref="ComputeInvoiceTax"/>: a taxable value at an integrated rate.</summary>
    public readonly record struct TaxableLine(Money TaxableValue, int IntegratedBasisPoints);

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

        /// <summary>Σ all tax (CGST+SGST+IGST) over the invoice.</summary>
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

        var breakdown = new List<LineTax>(lines.Count);
        var taxableTotal = 0m;

        // Accumulate the taxable subtotal per integrated rate (in the input line order of first appearance), so a
        // multi-rate invoice posts one (head, rate) tax line per rate group — each on its own subtotal.
        var rateOrder = new List<int>();
        var taxableByRate = new Dictionary<int, decimal>();

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
                rateOrder.Add(line.IntegratedBasisPoints);
            }
            taxableByRate[line.IntegratedBasisPoints] += line.TaxableValue.Amount;
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
        }

        // Optional invoice round-off on the grand total (taxable + tax).
        EntryLine? roundOffLine = null;
        var roundOff = 0m;
        if (applyInvoiceRoundOff)
        {
            var grand = taxableTotal + cgst + sgst + igst;
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
