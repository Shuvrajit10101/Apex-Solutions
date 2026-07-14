using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>GST-on-advances</b> engine (catalog §12; Phase 9 slice 2b; RQ-25; §12/§13, Rule 50/51; DP-32). Framework-,
/// DB-, clock- and RNG-free: a pure, deterministic computation over the <see cref="Company"/> aggregate, reusing the
/// <see cref="GstService"/> total-then-split and the reverse-charge <see cref="RcmService.TimeOfSupply"/> helper.
/// <para>
/// On an advance <b>received for a service</b> GST is due on receipt (§13). The service produces a self-balancing tax
/// pair — <c>Cr Output {head}</c> (the liability, payable now) + <c>Dr Output Tax on Advances</c> (a current-asset
/// suspense) — added on top of the ordinary receipt legs, so the receipt balances without inflating revenue:
/// <code>
/// Dr  Bank                        11,800   ] ordinary Receipt-voucher legs (Rule 50)
/// Cr  Advance from customer       11,800   ]
/// Cr  Output IGST                  1,800   ] tax-on-advance pair (self-balancing, additive)  → GSTR-1 11A
/// Dr  Output Tax on Advances       1,800   ]   (reversed when the invoice adjusts the advance)
/// </code>
/// A <b>goods advance is de-taxed</b> (Notn 66/2017-CT) — no tax pair, no 11A row (the record's own constructor rejects a
/// goods advance carrying tax). When the tax invoice is later raised the advance is <b>adjusted</b> (the suspense is
/// reversed → GSTR-1 11B, so the invoice's own output tax is not double-counted); a non-supply <b>refunds</b> the advance
/// via a Rule-51 refund voucher. A company that never takes a service advance posts/serialises byte-identically to a v38
/// company (ER-13) — the suspense ledger is created lazily only when a taxable advance is booked.
/// </para>
/// </summary>
public sealed class AdvanceReceiptService
{
    private readonly Company _company;
    private readonly GstService _gst;

    public AdvanceReceiptService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _gst = new GstService(company);
    }

    /// <summary>The Rule-50 fallback integrated rate when the rate is unknown at receipt (18%).</summary>
    public const int RuleFiftyFallbackRateBasisPoints = 1800;

    /// <summary>
    /// The advance / earliest-event <b>time of supply</b> (RQ-25; §12/§13) — <b>shared verbatim</b> with the reverse-charge
    /// engine (<see cref="RcmService.TimeOfSupply"/>): RQ-25 and RQ-7 use one earliest-event machinery. Pure, clock-free.
    /// </summary>
    public static DateOnly TimeOfSupply(
        GstSupplyType kind, DateOnly? invoiceDate, DateOnly? receiptDate, DateOnly? paymentDate)
        => RcmService.TimeOfSupply(kind, invoiceDate, receiptDate, paymentDate);

    /// <summary>The result of building a Rule-50 advance receipt: the self-balancing tax pair to append on top of the
    /// ordinary receipt legs + the persisted <see cref="GstAdvanceReceipt"/> record (the source of truth for 11A/11B).</summary>
    /// <param name="TaxLines">The tax-on-advance pair (Cr Output {head} liability + Dr suspense); empty for a goods advance.</param>
    /// <param name="Receipt">The advance record (already added to the company).</param>
    public sealed record AdvanceReceiptPosting(IReadOnlyList<EntryLine> TaxLines, GstAdvanceReceipt Receipt)
    {
        /// <summary>The advance tax charged on receipt (0 for a goods advance).</summary>
        public Money AdvanceTax => Receipt.AdvanceTax;
    }

    /// <summary>
    /// Builds a Rule-50 advance receipt (RQ-25). For a <b>service</b> advance it charges GST on the net advance and emits
    /// the self-balancing <c>Cr Output {head}</c> + <c>Dr Output Tax on Advances</c> pair (total-then-split so intra ⇒
    /// CGST+SGST, inter ⇒ IGST, to the paisa). A <b>goods</b> advance is de-taxed (Notn 66/2017) — no tax pair, no 11A.
    /// The suspense ledger is created lazily here (never in <see cref="GstService.EnableGst"/>, ER-13). The record is
    /// registered on the company; 11A/11B are projected off the records (not off the lines), so the tax lines carry an
    /// ordinary <see cref="GstLineTax"/> (they are never swept into GSTR-1/3B — a Receipt voucher carries no report
    /// direction — so they are not double-counted against the later invoice's own output tax).
    /// </summary>
    /// <param name="receiptVoucherId">The Receipt voucher id (the caller mints it before posting).</param>
    /// <param name="isService">True ⇒ a service advance (taxed); false ⇒ a goods advance (de-taxed).</param>
    /// <param name="netAdvance">The net (ex-tax) advance for a service; the gross advance for goods. Paisa-exact.</param>
    /// <param name="rateBasisPoints">The integrated rate; use <see cref="RuleFiftyFallbackRateBasisPoints"/> when unknown.</param>
    /// <param name="interState">True ⇒ IGST; false ⇒ CGST+SGST (Rule-50 fallback = inter-state when unknown).</param>
    /// <param name="placeOfSupplyStateCode">The place-of-supply state code, or <c>null</c>.</param>
    public AdvanceReceiptPosting BuildAdvanceReceipt(
        Guid receiptVoucherId, bool isService, Money netAdvance, int rateBasisPoints, bool interState,
        string? placeOfSupplyStateCode = null)
    {
        if (netAdvance.Amount <= 0m)
            throw new ArgumentException("Advance amount must be > 0.", nameof(netAdvance));
        if (!netAdvance.IsPaisaExact)
            throw new InvalidOperationException($"Advance amount {netAdvance} must be paisa-exact.");

        // Goods advance: de-taxed (Notn 66/2017) — book only the ordinary receipt legs; record it (zero tax) for
        // completeness; no 11A row (the projection skips zero-tax records). The record's constructor also rejects a
        // goods advance carrying tax, so a hand-edited import can never smuggle a taxed goods advance through (ER-12-style).
        if (!isService)
        {
            var goodsRecord = new GstAdvanceReceipt(
                Guid.NewGuid(), receiptVoucherId, isService: false, netAdvance, rateBasisPoints: 0, interState,
                placeOfSupplyStateCode, Money.Zero);
            _company.AddAdvanceReceipt(goodsRecord);
            return new AdvanceReceiptPosting(Array.Empty<EntryLine>(), goodsRecord);
        }

        // Service advance: tax is due now. Compute the split ONCE (total-then-split) so CGST+SGST == IGST to the paisa.
        var tax = GstService.ComputeLineTax(netAdvance, rateBasisPoints, interState);
        var suspense = _gst.EnsureAdvanceTaxSuspenseLedger();
        var lines = new List<EntryLine>();
        var halfBp = rateBasisPoints / 2;

        void AddOutput(GstTaxHead head, Money amount, int headBp)
        {
            if (amount.Amount == 0m) return;
            var output = _gst.FindTaxLedger(head, GstTaxDirection.Output)
                ?? throw new InvalidOperationException(
                    $"Output {head} ledger not found — enable GST first (EnableGst auto-creates it).");
            lines.Add(new EntryLine(output.Id, amount, DrCr.Credit, gst: new GstLineTax(head, headBp, netAdvance)));
        }

        if (interState)
            AddOutput(GstTaxHead.Integrated, tax.Igst, rateBasisPoints);
        else
        {
            AddOutput(GstTaxHead.Central, tax.Cgst, halfBp);
            AddOutput(GstTaxHead.State, tax.Sgst, halfBp);
        }

        // The single suspense (Dr) line carries the total tax so the pair self-balances (Σ Cr Output == Dr suspense).
        var totalTax = tax.Total;
        lines.Add(new EntryLine(suspense.Id, totalTax, DrCr.Debit));

        var record = new GstAdvanceReceipt(
            Guid.NewGuid(), receiptVoucherId, isService: true, netAdvance, rateBasisPoints, interState,
            placeOfSupplyStateCode, totalTax);
        _company.AddAdvanceReceipt(record);
        return new AdvanceReceiptPosting(lines, record);
    }

    /// <summary>
    /// Adjusts a service advance against the later tax invoice (RQ-25 → GSTR-1 11B). Returns the reversal pair
    /// <c>Dr Output {head}</c> / <c>Cr Output Tax on Advances</c> — releasing the suspense and reversing the advance's
    /// output recognition, so the invoice's own output tax is not double-counted — and replaces the record with one
    /// carrying <see cref="GstAdvanceReceipt.AdjustedAgainstInvoiceVoucherId"/> set (same identity). The reversal legs
    /// carry <b>no</b> <see cref="GstLineTax"/> (they are a balance transfer, invisible to the GST projections — which
    /// read only tagged lines by magnitude — so they never inflate the invoice's output tax). No-op tax pair for a goods
    /// advance (nothing to reverse).
    /// <para>
    /// <b>Full-advance adjustment only (S2b).</b> The reversal releases the <b>whole</b> advance tax, so it is only
    /// correct when the invoice fully consumes the advance. A <b>partial</b> adjustment — an invoice whose adjustable
    /// taxable value is <b>less</b> than the advance's net amount — would leave a residual advance to carry against a later
    /// invoice, which needs a persisted <c>adjusted_amount</c> residual-balance column (schema — out of scope this slice).
    /// Rather than silently over-reverse the full tax against a partial invoice, this fails fast (finding #4). Partial
    /// adjustment with a running residual balance is a documented carry-forward (needs the residual-balance schema column).
    /// </para>
    /// </summary>
    public IReadOnlyList<EntryLine> AdjustAgainstInvoice(GstAdvanceReceipt advance, Guid invoiceVoucherId)
    {
        ArgumentNullException.ThrowIfNull(advance);
        if (advance.AdjustedAgainstInvoiceVoucherId is not null)
            throw new InvalidOperationException("This advance has already been adjusted against an invoice.");
        if (advance.RefundVoucherId is not null)
            throw new InvalidOperationException("A refunded advance cannot be adjusted against an invoice.");

        // Reject a PARTIAL adjustment (finding #4): a taxable service advance may only be adjusted against an invoice that
        // fully consumes it. The invoice's adjustable taxable value (its posted forward-taxable value) must be ≥ the
        // advance's net amount, else releasing the whole advance tax would over-reverse the output. Goods / zero-tax
        // advances carry no tax pair, so there is nothing to over-reverse — they skip the check.
        if (advance.IsService && advance.AdvanceTax.Amount != 0m)
        {
            var invoice = _company.FindVoucher(invoiceVoucherId)
                ?? throw new InvalidOperationException(
                    "The invoice to adjust the advance against was not found — post the tax invoice before adjusting the advance.");
            var invoiceTaxable = GstReportSupport.InvoiceTaxableValue(invoice);
            if (invoiceTaxable.Amount < advance.AdvanceAmount.Amount)
                throw new InvalidOperationException(
                    $"Partial advance adjustment is not supported (S2b): the invoice's adjustable taxable value "
                    + $"{invoiceTaxable} is less than the advance's net amount {advance.AdvanceAmount}. S2b supports "
                    + "full-advance adjustment only — a partial adjustment with a carried-forward residual balance needs "
                    + "a residual-balance schema column (carry-forward).");
        }

        var reversal = BuildAdvanceReversalPair(advance);

        var updated = new GstAdvanceReceipt(
            advance.Id, advance.ReceiptVoucherId, advance.IsService, advance.AdvanceAmount, advance.RateBasisPoints,
            advance.InterState, advance.PlaceOfSupplyStateCode, advance.AdvanceTax,
            adjustedAgainstInvoiceVoucherId: invoiceVoucherId, refundVoucherId: advance.RefundVoucherId);
        _company.RemoveAdvanceReceipt(advance);
        _company.AddAdvanceReceipt(updated);
        return reversal;
    }

    /// <summary>
    /// Refunds a service advance whose supply did not happen (RQ-25; Rule 51). Returns the reversal pair
    /// <c>Dr Output {head}</c> / <c>Cr Output Tax on Advances</c> (the caller books the ordinary <c>Dr Advance</c> /
    /// <c>Cr Bank</c> legs of the refund voucher), and replaces the record with one carrying
    /// <see cref="GstAdvanceReceipt.RefundVoucherId"/> set (same identity). No tax pair for a goods advance.
    /// </summary>
    public IReadOnlyList<EntryLine> Refund(GstAdvanceReceipt advance, Guid refundVoucherId)
    {
        ArgumentNullException.ThrowIfNull(advance);
        if (advance.RefundVoucherId is not null)
            throw new InvalidOperationException("This advance has already been refunded.");
        if (advance.AdjustedAgainstInvoiceVoucherId is not null)
            throw new InvalidOperationException("An advance already adjusted against an invoice cannot be refunded.");

        var reversal = BuildAdvanceReversalPair(advance);

        var updated = new GstAdvanceReceipt(
            advance.Id, advance.ReceiptVoucherId, advance.IsService, advance.AdvanceAmount, advance.RateBasisPoints,
            advance.InterState, advance.PlaceOfSupplyStateCode, advance.AdvanceTax,
            adjustedAgainstInvoiceVoucherId: advance.AdjustedAgainstInvoiceVoucherId, refundVoucherId: refundVoucherId);
        _company.RemoveAdvanceReceipt(advance);
        _company.AddAdvanceReceipt(updated);
        return reversal;
    }

    /// <summary>The shared reversal pair (Dr Output {head} / Cr suspense) that releases a service advance's output-tax
    /// recognition on adjustment or refund. Untagged balance transfers (not GST report lines). Empty for a goods advance.</summary>
    private IReadOnlyList<EntryLine> BuildAdvanceReversalPair(GstAdvanceReceipt advance)
    {
        if (!advance.IsService || advance.AdvanceTax.Amount == 0m) return Array.Empty<EntryLine>();

        var suspense = _company.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)
            ?? throw new InvalidOperationException(
                "Output Tax on Advances suspense ledger not found — the advance receipt should have created it.");
        var tax = GstService.ComputeLineTax(advance.AdvanceAmount, advance.RateBasisPoints, advance.InterState);

        var lines = new List<EntryLine>();
        void AddReversal(GstTaxHead head, Money amount)
        {
            if (amount.Amount == 0m) return;
            var output = _gst.FindTaxLedger(head, GstTaxDirection.Output)
                ?? throw new InvalidOperationException($"Output {head} ledger not found.");
            lines.Add(new EntryLine(output.Id, amount, DrCr.Debit)); // reverse the earlier Cr Output {head}
        }

        if (advance.InterState)
            AddReversal(GstTaxHead.Integrated, tax.Igst);
        else
        {
            AddReversal(GstTaxHead.Central, tax.Cgst);
            AddReversal(GstTaxHead.State, tax.Sgst);
        }
        lines.Add(new EntryLine(suspense.Id, advance.AdvanceTax, DrCr.Credit)); // release the suspense
        return lines;
    }
}
