using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>reverse-charge (RCM)</b> engine (catalog §12; Phase 9 slice 2; RQ-3/RQ-7/RQ-8/RQ-11; ER-3). Framework-, DB-,
/// clock- and RNG-free: a pure, deterministic computation over the <see cref="Company"/> aggregate, reusing the
/// <see cref="GstService"/> total-then-split + dated <c>ResolveRate</c>/<c>ResolveCess</c>.
/// <para>
/// On an RCM <b>inward</b> supply the supplier charges <b>no tax</b>; the recipient self-accounts a <b>balanced pair</b>
/// added on top of the ordinary purchase legs (ER-3):
/// <list type="bullet">
///   <item><b>Output liability leg</b> → a dedicated <c>"RCM Output {CGST|SGST|IGST|Cess}"</c> ledger (the cash-only
///     §49(4) liability, distinct from every ordinary Output ledger), tagged <c>IsReverseCharge</c> (scheme <c>null</c>);</item>
///   <item><b>Input ITC leg</b> → the ordinary <c>Input {head}</c> ledger, tagged <c>IsReverseCharge</c> + the
///     <see cref="RcmItcScheme"/> (import-of-services → 4A(2); other → 4A(3)) so the returns bucket it.</item>
/// </list>
/// The two legs are the <b>same amount</b> (each computed once with <see cref="GstService.ComputeLineTax"/>), so they
/// cancel and the pair is self-balancing to the paisa — the RCM tax never touches the party (supplier) leg (risk #1).
/// Import-of-goods is <b>excluded</b> (IGST is paid at customs, GSTR-3B 4A(1)) — a hard fail-fast (risk, RQ-11). A
/// company with no rcm-category master + no RCM-tagged line resolves/posts byte-identically to a v38 company (ER-13).
/// </para>
/// </summary>
public sealed class RcmService
{
    private readonly Company _company;
    private readonly GstService _gst;

    public RcmService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _gst = new GstService(company);
    }

    /// <summary>The nature of an inward supply for reverse-charge routing (RQ-11).</summary>
    public enum SupplyKind
    {
        /// <summary>A domestic inward supply — RCM by POS (intra ⇒ CGST+SGST, inter ⇒ IGST).</summary>
        Domestic,

        /// <summary>Import of services (§5(3)/§13) — RCM, <b>always IGST</b>, GSTR-3B 4A(2).</summary>
        ImportOfServices,

        /// <summary>Import of goods — <b>NOT reverse charge</b> (IGST paid at customs on the Bill of Entry, 4A(1)).</summary>
        ImportOfGoods,
    }

    // ---- Applicability (pure) ----

    /// <summary>The outcome of resolving reverse-charge applicability for an inward supply (pure; no posting).</summary>
    /// <param name="Applies">True iff the supply attracts reverse charge on the supply date.</param>
    /// <param name="Category">The matched notified category, or <c>null</c> (import-of-services matches by law).</param>
    /// <param name="RateBasisPoints">The integrated RCM rate (goods with an HSN resolve the dated S1 rate).</param>
    /// <param name="InterState">True ⇒ IGST; false ⇒ CGST+SGST (import-of-services forces IGST).</param>
    /// <param name="Scheme">The ITC bucket — ImportOfServices (4A(2)) or OtherRcm (4A(3)).</param>
    public readonly record struct RcmResolution(
        bool Applies, RcmCategory? Category, int RateBasisPoints, bool InterState, RcmItcScheme Scheme)
    {
        /// <summary>A non-applicable resolution (no reverse charge).</summary>
        public static RcmResolution NotApplicable => new(false, null, 0, false, RcmItcScheme.OtherRcm);
    }

    /// <summary>
    /// Resolves whether an inward supply attracts reverse charge on <paramref name="supplyDate"/> (RQ-3/RQ-7/RQ-11). The
    /// shared item/ledger GST block (<paramref name="supplyGst"/>) must have <c>ReverseChargeApplicable</c> set (except
    /// import-of-services, which is RCM by law), a matching <see cref="RcmCategory"/> must be effective on the date, and
    /// the supplier + recipient qualifiers must be satisfied. <b>§9(4) fires only when the recipient is a promoter</b>
    /// (<paramref name="recipientIsPromoter"/>) matching a <c>Promoter</c>-recipient category — so the blanket default is
    /// OFF (RQ-3). A GTA that has opted for forward charge (<c>GtaForwardCharge</c>) is <b>not</b> RCM. Pure and total.
    /// </summary>
    public RcmResolution Resolve(
        StockItemGstDetails? supplyGst, PartyGstDetails? supplier, StockItem? item, Domain.Ledger? spLedger,
        DateOnly supplyDate, SupplyKind supplyKind,
        bool recipientIsPromoter = false, bool recipientIsBodyCorporate = true)
    {
        // Import of goods is never reverse charge (customs IGST, 4A(1)) — out of the dual-leg scope.
        if (supplyKind == SupplyKind.ImportOfGoods) return RcmResolution.NotApplicable;

        // Import of services is reverse charge by law (§5(3)) — always IGST, scheme ImportOfServices, no category needed.
        if (supplyKind == SupplyKind.ImportOfServices)
        {
            var importRate = supplyGst?.RateBasisPoints ?? spLedger?.SalesPurchaseGst?.RateBasisPoints ?? 1800;
            return new RcmResolution(true, null, importRate, InterState: true, RcmItcScheme.ImportOfServices);
        }

        // Domestic: gated on the master flag + (GTA-forward-charge opt-out) + a matching notified category.
        if (supplyGst is not { ReverseChargeApplicable: true } gst || gst.GtaForwardCharge)
            return RcmResolution.NotApplicable;

        var category = MatchCategory(gst, supplier, supplyDate, recipientIsPromoter, recipientIsBodyCorporate);
        if (category is null) return RcmResolution.NotApplicable;

        var interState = _gst.IsInterState(supplier?.StateCode);

        // Goods with an HSN resolve the DATED rate through the S1 history (cement 28% → 18% at 22-Sep-2025); otherwise
        // the category carries the rate (service categories, or a goods category with no HSN row).
        var rate = category.RateBasisPoints;
        if (category.SupplyType == GstSupplyType.Goods && category.HsnSac is not null)
        {
            var res = _gst.ResolveRate(item, spLedger, supplyDate);
            if (res.IsTaxable && !GstService.IsUnresolved(res)) rate = res.RateBasisPoints;
        }

        return new RcmResolution(true, category, rate, interState, RcmItcScheme.OtherRcm);
    }

    /// <summary>Finds the notified category that fires for a supply, or <c>null</c>. Prefers the explicit
    /// <see cref="StockItemGstDetails.RcmCategoryId"/> link; else matches on supply type + qualifiers + effective-on.</summary>
    private RcmCategory? MatchCategory(
        StockItemGstDetails gst, PartyGstDetails? supplier, DateOnly supplyDate,
        bool recipientIsPromoter, bool recipientIsBodyCorporate)
    {
        var categories = _company.Gst?.RcmCategories;
        if (categories is null || categories.Count == 0) return null;

        bool SupplierOk(RcmParty q) => q switch
        {
            RcmParty.Any => true,
            RcmParty.Unregistered => supplier is null || supplier.IsB2C,
            RcmParty.NonBodyCorporate => supplier is null || !supplier.IsBodyCorporate,
            _ => false, // a supplier qualifier is never a recipient-only value
        };
        bool RecipientOk(RcmParty q) => q switch
        {
            RcmParty.Any => true,
            RcmParty.RegisteredPerson => true,        // the recipient (us) is a registered GST person in an RCM context
            RcmParty.BodyCorporate => recipientIsBodyCorporate,
            RcmParty.Promoter => recipientIsPromoter, // §9(4) promoter-only — blanket default OFF
            _ => false,
        };
        bool Matches(RcmCategory c) =>
            c.IsEffectiveOn(supplyDate) && SupplierOk(c.SupplierQualifier) && RecipientOk(c.RecipientQualifier);

        if (gst.RcmCategoryId is { } linkedId)
        {
            var linked = categories.FirstOrDefault(c => c.Id == linkedId);
            return linked is not null && Matches(linked) ? linked : null;
        }

        // No explicit link: the most-recently-effective matching category for the item's supply type wins.
        return categories
            .Where(c => c.SupplyType == gst.SupplyType && Matches(c))
            .OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.Id)
            .FirstOrDefault();
    }

    // ---- Dual-leg posting (the RCM self-accounting pair) ----

    /// <summary>The reverse-charge self-accounting pair (Phase 9 slice 2; ER-3): the balanced output-liability + input-ITC
    /// tax lines to append on top of the ordinary purchase legs. <see cref="Applies"/> false ⇒ no lines (forward charge).</summary>
    /// <param name="Resolution">The applicability outcome (rate/POS/scheme).</param>
    /// <param name="Lines">The RCM tax lines (output liability lines + input ITC lines), each self-balancing by head.</param>
    public sealed record RcmPosting(RcmResolution Resolution, IReadOnlyList<EntryLine> Lines)
    {
        /// <summary>True iff reverse charge applied (the pair was produced).</summary>
        public bool Applies => Resolution.Applies;

        /// <summary>The output-liability lines (posted to the RCM Output ledgers → GSTR-3B 3.1(d)).</summary>
        public IEnumerable<EntryLine> OutputLines => Lines.Where(l => l.Side == DrCr.Credit);

        /// <summary>The input-ITC lines (posted to the ordinary Input ledgers, tagged RCM → GSTR-3B 4A(2)/4A(3)).</summary>
        public IEnumerable<EntryLine> InputLines => Lines.Where(l => l.Side == DrCr.Debit);
    }

    /// <summary>
    /// Builds the reverse-charge dual leg for an inward supply of assessable value <paramref name="taxableValue"/>
    /// (RQ-7; ER-3). Resolves applicability, then — when RCM applies — computes the tax <b>once</b> with
    /// <see cref="GstService.ComputeLineTax"/> (total-then-split so CGST+SGST == IGST to the paisa) and emits a balanced
    /// <b>output-liability</b> pair (Cr the RCM Output ledgers) + <b>input-ITC</b> pair (Dr the ordinary Input ledgers),
    /// both tagged reverse-charge. A cess-bearing RCM line adds an <c>"RCM Output Cess"</c> / <c>"Input Cess"</c> pair,
    /// ring-fenced out of the CGST/SGST/IGST heads (ER-2). Import-of-goods is a hard fail-fast (never RCM, RQ-11). The
    /// caller books the ordinary purchase legs (Dr Expense = value, Cr Party = value, no tax) and appends
    /// <see cref="RcmPosting.Lines"/>.
    /// </summary>
    public RcmPosting BuildReverseCharge(
        Money taxableValue, StockItem? item, Domain.Ledger? spLedger, PartyGstDetails? supplier,
        DateOnly supplyDate, SupplyKind supplyKind,
        bool recipientIsPromoter = false, bool recipientIsBodyCorporate = true, decimal quantity = 0m)
    {
        if (taxableValue.Amount <= 0m)
            throw new ArgumentException("RCM taxable value must be > 0.", nameof(taxableValue));
        if (!taxableValue.IsPaisaExact)
            throw new InvalidOperationException($"RCM taxable value {taxableValue} must be paisa-exact.");
        if (supplyKind == SupplyKind.ImportOfGoods)
            throw new InvalidOperationException(
                "Import of goods is not a reverse-charge supply — IGST is paid at customs on the Bill of Entry (GSTR-3B 4A(1)); "
                + "refusing to raise an RCM dual leg.");

        var supplyGst = item?.Gst ?? spLedger?.SalesPurchaseGst;
        var resolution = Resolve(supplyGst, supplier, item, spLedger, supplyDate, supplyKind,
            recipientIsPromoter, recipientIsBodyCorporate);
        if (!resolution.Applies)
            return new RcmPosting(resolution, Array.Empty<EntryLine>());

        var lines = new List<EntryLine>();
        var tax = GstService.ComputeLineTax(taxableValue, resolution.RateBasisPoints, resolution.InterState);
        var halfBp = resolution.RateBasisPoints / 2;

        void Pair(GstTaxHead head, Money amount, int headBp)
        {
            if (amount.Amount == 0m) return;
            var rcmOutput = _gst.EnsureRcmOutputLedger(head);
            var input = _gst.FindTaxLedger(head, GstTaxDirection.Input)
                ?? throw new InvalidOperationException(
                    $"Input {head} ledger not found — enable GST first (EnableGst auto-creates it).");
            // Output liability (Cr, own RCM ledger, scheme null — it is a liability, not ITC).
            lines.Add(new EntryLine(rcmOutput.Id, amount, DrCr.Credit,
                gst: new GstLineTax(head, headBp, taxableValue, isReverseCharge: true)));
            // Input ITC (Dr, ordinary Input ledger, tagged with the return scheme) — the SAME amount, so the pair cancels.
            lines.Add(new EntryLine(input.Id, amount, DrCr.Debit,
                gst: new GstLineTax(head, headBp, taxableValue, isReverseCharge: true, rcmScheme: resolution.Scheme)));
        }

        if (resolution.InterState)
        {
            Pair(GstTaxHead.Integrated, tax.Igst, resolution.RateBasisPoints);
        }
        else
        {
            Pair(GstTaxHead.Central, tax.Cgst, halfBp);
            Pair(GstTaxHead.State, tax.Sgst, halfBp);
        }

        // Cess on an RCM line (rare): reuse the S1 dated resolver; ring-fenced out of the CGST/SGST/IGST heads (ER-2).
        if (_gst.ResolveCess(item, spLedger, supplyDate, quantity) is { } cessCharge)
        {
            // A per-unit (Specific / RSP-factor) cess needs a positive quantity to value — a zero/absent quantity would
            // compute a silent ₹0 that the `cess.Amount != 0` guard below then SKIPS (a systematic under-collection).
            // Fail-fast instead (mirrors the S1 RSP-no-RSP guard, ER-5): a missing valuation input is a clear domain
            // error, never a hidden zero.
            if ((cessCharge.Mode is CessValuationMode.Specific or CessValuationMode.RetailSalePriceFactor) && quantity <= 0m)
                throw new InvalidOperationException(
                    $"Reverse-charge Compensation-Cess is per-unit ({cessCharge.Mode}) but the RCM line quantity is "
                    + $"{quantity} — cannot value the cess (refusing to post a silent ₹0 cess); supply the quantity.");
            var cess = cessCharge.ComputeCess(taxableValue);
            if (cess.Amount != 0m)
            {
                _gst.EnsureCessLedgers(); // ensure the ordinary Input Cess ledger for the ITC leg
                var cessBp = cessCharge.Mode == CessValuationMode.AdValorem ? cessCharge.RateBasisPoints : 0;
                var rcmOutputCess = _gst.EnsureRcmOutputLedger(GstTaxHead.Cess);
                var inputCess = _gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Input)
                    ?? throw new InvalidOperationException("Input Cess ledger not found after EnsureCessLedgers.");
                lines.Add(new EntryLine(rcmOutputCess.Id, cess, DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Cess, cessBp, taxableValue, isReverseCharge: true)));
                lines.Add(new EntryLine(inputCess.Id, cess, DrCr.Debit,
                    gst: new GstLineTax(GstTaxHead.Cess, cessBp, taxableValue, isReverseCharge: true, rcmScheme: resolution.Scheme)));
            }
        }

        return new RcmPosting(resolution, lines);
    }

    // ---- Self-invoice (Rule 47A) + payment voucher (Rule 52) documents ----

    /// <summary>
    /// Generates a Rule-47A <b>self-invoice</b> document for an unregistered-supplier RCM inward supply (RQ-8) and adds
    /// it to the company. A <b>registered</b> §9(3) supplier issues its own tax invoice, so this returns <c>null</c>
    /// (<paramref name="supplierIsRegistered"/>). The self-invoice date must be within <b>30 days</b> of the receipt date
    /// (Rule 47A) — a later date is a fail-fast. The consecutive series number is assigned per company.
    /// </summary>
    public RcmDocument? GenerateSelfInvoice(
        Guid sourceVoucherId, DateOnly receiptDate, DateOnly selfInvoiceDate, bool supplierIsRegistered,
        Guid? supplierLedgerId = null)
    {
        if (supplierIsRegistered) return null; // a registered supplier's invoice is the document — no self-invoice
        if (selfInvoiceDate > receiptDate.AddDays(30))
            throw new InvalidOperationException(
                $"Self-invoice date {selfInvoiceDate:yyyy-MM-dd} exceeds 30 days from the receipt date {receiptDate:yyyy-MM-dd} (Rule 47A).");

        var doc = new RcmDocument(
            Guid.NewGuid(), RcmDocumentKind.SelfInvoice, sourceVoucherId,
            _company.NextRcmDocumentSeries(RcmDocumentKind.SelfInvoice), selfInvoiceDate, supplierLedgerId);
        _company.AddRcmDocument(doc);
        return doc;
    }

    /// <summary>Generates a Rule-52 <b>payment voucher</b> document for a reverse-charge supplier payment (RQ-8) and adds
    /// it to the company, with the next consecutive series number.</summary>
    public RcmDocument GeneratePaymentVoucher(Guid sourceVoucherId, DateOnly docDate, Guid? supplierLedgerId = null)
    {
        var doc = new RcmDocument(
            Guid.NewGuid(), RcmDocumentKind.PaymentVoucher, sourceVoucherId,
            _company.NextRcmDocumentSeries(RcmDocumentKind.PaymentVoucher), docDate, supplierLedgerId);
        _company.AddRcmDocument(doc);
        return doc;
    }

    // ---- Time of supply (shared with the S2b advance engine) ----

    /// <summary>
    /// The reverse-charge / advance <b>time of supply</b> (RQ-7; §12(3) goods / §13(3) services). Pure, clock-free,
    /// deterministic. Shared verbatim with the S2b <c>AdvanceReceiptService</c>. Throws if no anchoring date is supplied.
    /// <list type="bullet">
    ///   <item><b>Goods §12(3):</b> the <b>earliest</b> of { date of receipt of goods, date of payment, the date
    ///     <b>immediately following 30 days</b> from the supplier's invoice = <c>invoice + 31</c> }. The invoice date is
    ///     never itself a limb here — only its 30-day fallback is (which is anchored to the <b>invoice</b>, not receipt).</item>
    ///   <item><b>Services §13(3):</b> the <b>earliest</b> of { date of payment, the date <b>immediately following
    ///     60 days</b> from the supplier's invoice = <c>invoice + 61</c> }. The <b>raw invoice date is NOT a limb</b> under
    ///     reverse charge — that limb belongs to forward-charge §13(2) only.</item>
    /// </list>
    /// </summary>
    public static DateOnly TimeOfSupply(
        GstSupplyType kind, DateOnly? invoiceDate, DateOnly? receiptDate, DateOnly? paymentDate)
    {
        DateOnly? Earliest(DateOnly? a, DateOnly? b) =>
            a is null ? b : b is null ? a : (a.Value < b.Value ? a : b);

        DateOnly? tos = paymentDate;
        if (kind == GstSupplyType.Goods)
        {
            // §12(3): earliest of { receipt of goods, payment, the day immediately following 30 days from the
            // supplier's invoice (invoice + 31) }. The fallback is anchored to the INVOICE, not the receipt date.
            tos = Earliest(tos, receiptDate);
            if (invoiceDate is { } inv) tos = Earliest(tos, inv.AddDays(31));
        }
        else
        {
            // §13(3): earliest of { payment, the day immediately following 60 days from the supplier's invoice
            // (invoice + 61) }. The invoice date itself is a limb only under forward charge (§13(2)), never RCM.
            if (invoiceDate is { } inv) tos = Earliest(tos, inv.AddDays(61));
        }

        return tos ?? throw new ArgumentException(
            "Time of supply requires at least one of payment / receipt / invoice date.");
    }
}
