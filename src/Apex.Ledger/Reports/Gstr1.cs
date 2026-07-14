using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>B2B</b> row of GSTR-1 (phase4-gst-requirements RQ-21): a single registered-party outward invoice —
/// the party GSTIN, the invoice number + date, the place-of-supply state, and the taxable value with its
/// CGST/SGST (intra) or IGST (inter) — read from the posted tax lines. Phase-4 aggregates the invoice's tax
/// per head (no Large/Small split; that is Phase 9).
/// </summary>
public sealed record Gstr1B2BRow(
    string PartyName,
    string? PartyGstin,
    int InvoiceNumber,
    DateOnly InvoiceDate,
    string? PlaceOfSupplyStateCode,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst);

/// <summary>
/// One consolidated <b>B2C</b> row of GSTR-1 (RQ-21; DP-8): outward supplies to unregistered/consumer parties
/// grouped <b>rate-wise</b> (the Phase-4 simple consolidation — no B2C-Large/Small interstate split). Carries
/// the integrated rate, taxable value and CGST/SGST/IGST accumulated across all B2C supplies at that rate.
/// </summary>
public sealed record Gstr1B2CRow(int RateBasisPoints, Money TaxableValue, Money Cgst, Money Sgst, Money Igst);

/// <summary>One rate-wise summary row of GSTR-1: total taxable value and total tax at an integrated rate.</summary>
public sealed record Gstr1RateRow(int RateBasisPoints, Money TaxableValue, Money TotalTax);

/// <summary>
/// One HSN-summary row of GSTR-1 (RQ-21; law L-7): a supply grouped by HSN/SAC — the code, a description, the
/// unit-quantity code (UQC), total quantity, taxable value, and CGST/SGST/IGST. Built from the outward
/// item-invoice stock lines, with each invoice's posted tax apportioned to its lines by value share.
/// </summary>
public sealed record Gstr1HsnRow(
    string HsnSac,
    string Description,
    string? Uqc,
    decimal Quantity,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst)
{
    /// <summary>Σ tax on this HSN row (CGST + SGST + IGST).</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>
/// One <b>Table 9B</b> row of GSTR-1 (Phase 9 slice 2b; RQ-24; §34): a credit/debit note against an original invoice —
/// the note type (C/D), the original-invoice reference (number + date), the note's own date, place of supply, and the
/// <b>signed</b> taxable value + CGST/SGST/IGST (a credit note is negative, a debit note positive), plus the §34 reason.
/// Read off the posted note tax lines (never recomputed), joined to the <see cref="GstCreditDebitNoteLink"/> record.
/// </summary>
public sealed record Gstr1Table9BRow(
    CdnType NoteType,
    Guid? OriginalInvoiceVoucherId,
    string? OriginalInvoiceNumber,
    DateOnly? OriginalInvoiceDate,
    DateOnly NoteDate,
    string? PlaceOfSupplyStateCode,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst,
    string ReasonCode,
    bool Is9BTarget)
{
    /// <summary>Σ signed tax on this note (CGST + SGST + IGST).</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>One <b>Table 11A</b> row of GSTR-1 (Phase 9 slice 2b; RQ-25): advance <b>received</b> in the period, grouped
/// by integrated rate and place-of-supply nature, with its tax (services only — goods advances are de-taxed).</summary>
public sealed record Gstr1AdvanceRow(
    int RateBasisPoints, bool InterState, Money AdvanceReceived, Money Cgst, Money Sgst, Money Igst)
{
    /// <summary>Σ advance tax on this row.</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>One <b>Table 11B</b> row of GSTR-1 (Phase 9 slice 2b; RQ-25): advance <b>adjusted</b> against an invoice in
/// the period, grouped by integrated rate and place-of-supply nature (reverses the 11A the advance was reported in).</summary>
public sealed record Gstr1AdvanceAdjustedRow(
    int RateBasisPoints, bool InterState, Money AdvanceAdjusted, Money Cgst, Money Sgst, Money Igst)
{
    /// <summary>Σ adjusted advance tax on this row.</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>
/// <b>GSTR-1</b> (outward supplies) — a pure, read-only projection over the posted <b>outward</b> (Sales /
/// Credit-Note) GST vouchers in <c>[from, to]</c> (phase4-gst-requirements RQ-21; catalog §12; ER-7). It reads
/// the tax off each tax <see cref="EntryLine"/>'s <see cref="GstLineTax"/> — never recomputing — so the
/// section totals reconcile to the Output tax-ledger postings for the period. Sections: <see cref="B2B"/> (one
/// row per registered-party invoice, party has a GSTIN), <see cref="B2C"/> (unregistered/consumer supplies
/// consolidated rate-wise, DP-8), a <see cref="RateSummary"/> and an <see cref="HsnSummary"/>.
/// Cancelled and post-dated-after-<c>to</c> vouchers are excluded; exempt/nil/non-GST outward value is shown
/// in <see cref="ExemptNilNonGstValue"/>. A non-GST company yields empty sections. No UI, no DB.
/// </summary>
public sealed record Gstr1(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Gstr1B2BRow> B2B,
    IReadOnlyList<Gstr1B2CRow> B2C,
    IReadOnlyList<Gstr1RateRow> RateSummary,
    IReadOnlyList<Gstr1HsnRow> HsnSummary,
    Money ExemptNilNonGstValue,
    Money TotalCgst,
    Money TotalSgst,
    Money TotalIgst)
{
    /// <summary>
    /// Table 4B (Phase 9 slice 2; RQ-7) — the value of <b>outward</b> supplies on which the <b>recipient</b> pays tax
    /// under reverse charge (the invoice carries zero tax but must be flagged). Light in S2a: a single value bucket
    /// (Σ the outward supply value of vouchers whose sales/expense ledger is flagged
    /// <see cref="StockItemGstDetails.ReverseChargeApplicable"/>). Default <c>Money.Zero</c> so a company with no outward
    /// RCM supply is byte-identical (ER-13).
    /// </summary>
    public Money Rcm4BOutwardValue { get; init; }

    /// <summary>
    /// Table 9B (Phase 9 slice 2b; RQ-24; §34) — the credit/debit notes issued against original invoices in the period,
    /// each signed by note type (credit negative, debit positive). Default empty so a company with no §34 note is
    /// byte-identical (ER-13). The note tax is <b>already folded (signed) into <see cref="TotalCgst"/>/…</b>.
    /// </summary>
    public IReadOnlyList<Gstr1Table9BRow> Table9B { get; init; } = [];

    /// <summary>Table 11A (Phase 9 slice 2b; RQ-25) — advances received (services only) in the period. Default empty (ER-13).</summary>
    public IReadOnlyList<Gstr1AdvanceRow> Table11A { get; init; } = [];

    /// <summary>Table 11B (Phase 9 slice 2b; RQ-25) — advances adjusted against invoices in the period. Default empty (ER-13).</summary>
    public IReadOnlyList<Gstr1AdvanceAdjustedRow> Table11B { get; init; } = [];

    /// <summary>Σ advance tax received (Table 11A) across all rows.</summary>
    public Money AdvanceTaxReceived =>
        new(Table11A.Sum(r => r.Cgst.Amount + r.Sgst.Amount + r.Igst.Amount));

    /// <summary>Σ advance tax adjusted (Table 11B) across all rows.</summary>
    public Money AdvanceTaxAdjusted =>
        new(Table11B.Sum(r => r.Cgst.Amount + r.Sgst.Amount + r.Igst.Amount));

    /// <summary>Σ all output tax on the return (CGST + SGST + IGST). Includes the §34 CDN net (signed).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>Builds GSTR-1 for the whole company over <c>[from, to]</c>.</summary>
    public static Gstr1 Build(Company company, DateOnly from, DateOnly to)
    {
        var b2b = new List<Gstr1B2BRow>();
        var b2cAcc = new Dictionary<int, HeadAmounts>();       // by integrated rate
        var rateAcc = new Dictionary<int, (decimal Taxable, decimal Tax)>();
        var hsnAcc = new Dictionary<string, HsnAcc>();
        var exempt = 0m;
        var totalCgst = 0m; var totalSgst = 0m; var totalIgst = 0m;

        foreach (var (voucher, _) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output))
        {
            // Phase 9 slice 2b: a formalised §34 credit/debit note is a first-class outward document projected by Table 9B
            // (signed by note type) and folded — signed — into the output totals below. Exclude it from the ordinary
            // invoice sweep so its tax is not double-counted (mirrors the RCM / outward-4B exclusions, risk #4). A company
            // with no §34 note never enters this branch (byte-identical, ER-13).
            if (GstReportSupport.CdnLinkFor(company, voucher) is not null) continue;

            // Per-invoice posted tax by head (read off the tax lines — never recomputed).
            var invoice = ReadInvoiceHeads(voucher);
            var hasTax = invoice.Cgst != 0m || invoice.Sgst != 0m || invoice.Igst != 0m;
            totalCgst += invoice.Cgst; totalSgst += invoice.Sgst; totalIgst += invoice.Igst;

            // An outward reverse-charge supply (zero forward tax; sales ledger flagged ReverseChargeApplicable) belongs
            // ONLY in Table 4B (Rcm4BOutwardValue) — never the exempt/nil/non-GST bucket or the HSN sweep, else it is
            // double-represented (its value would appear in both 4B and exempt). Skip it here (Phase 9 slice 2; RQ-7).
            if (!hasTax && GstReportSupport.IsOutwardReverseChargeSupply(company, voucher)) continue;

            // HSN summary + exempt bucket — every other outward supply (taxable AND exempt/nil) contributes here.
            AccumulateHsn(company, voucher, invoice, hsnAcc, ref exempt);

            // A no-tax supply (exempt/nil/non-GST) belongs only in the HSN summary + exempt bucket, not in the
            // B2B/B2C tax rows or the taxable rate-wise summary.
            if (!hasTax) continue;

            var party = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
            var pos = GstReportSupport.PlaceOfSupply(company, voucher);
            var taxable = GstReportSupport.InvoiceTaxableValue(voucher);

            // Per-(integrated rate) breakdown of THIS invoice's posted tax, so a multi-rate invoice contributes
            // one entry per rate to the rate-wise summary / B2C consolidation (never a blended 0% row).
            var rateGroups = ReadInvoiceRateGroups(voucher);

            // B2B when the party carries a GSTIN (registered); else B2C (DP-8).
            var isB2B = party?.PartyGst is { } pg && !pg.IsB2C;
            if (isB2B)
            {
                // One B2B invoice row carrying the whole-invoice taxable value and both heads' total tax.
                b2b.Add(new Gstr1B2BRow(
                    party!.Name, party.PartyGst!.Gstin, voucher.Number, voucher.Date, pos,
                    taxable, new Money(invoice.Cgst), new Money(invoice.Sgst), new Money(invoice.Igst)));
            }
            else
            {
                foreach (var (rate, g) in rateGroups)
                {
                    var h = b2cAcc.TryGetValue(rate, out var cur) ? cur : new HeadAmounts();
                    h.Taxable += g.Taxable; h.Cgst += g.Cgst; h.Sgst += g.Sgst; h.Igst += g.Igst;
                    b2cAcc[rate] = h;
                }
            }

            // Rate-wise summary (taxable outward, by integrated rate) — one contribution per rate group.
            foreach (var (rate, g) in rateGroups)
            {
                var (t, x) = rateAcc.TryGetValue(rate, out var cur) ? cur : (0m, 0m);
                rateAcc[rate] = (t + g.Taxable, x + g.Cgst + g.Sgst + g.Igst);
            }
        }

        var b2cRows = b2cAcc
            .OrderBy(kv => kv.Key)
            .Select(kv => new Gstr1B2CRow(kv.Key, new Money(kv.Value.Taxable),
                new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        var rateRows = rateAcc
            .OrderBy(kv => kv.Key)
            .Select(kv => new Gstr1RateRow(kv.Key, new Money(kv.Value.Taxable), new Money(kv.Value.Tax)))
            .ToList();

        var hsnRows = hsnAcc.Values
            .OrderBy(h => h.HsnSac, StringComparer.Ordinal)
            .Select(h => new Gstr1HsnRow(h.HsnSac, h.Description, h.Uqc, h.Quantity,
                new Money(h.Taxable), new Money(h.Cgst), new Money(h.Sgst), new Money(h.Igst)))
            .ToList();

        // Phase 9 slice 2b: Table 9B (§34 CDN) — signed by note type — is folded into the output totals; 11A/11B are
        // projected off the advance records. All skipped byte-identically when the collections are empty (ER-13).
        var table9B = BuildTable9B(company, from, to, ref totalCgst, ref totalSgst, ref totalIgst);
        var (table11A, table11B) = BuildAdvanceTables(company, from, to);

        return new Gstr1(from, to, b2b, b2cRows, rateRows, hsnRows,
            new Money(exempt), new Money(totalCgst), new Money(totalSgst), new Money(totalIgst))
        {
            Rcm4BOutwardValue = ComputeRcm4BOutwardValue(company, from, to),
            Table9B = table9B,
            Table11A = table11A,
            Table11B = table11B,
        };
    }

    /// <summary>
    /// Builds GSTR-1 Table 9B (Phase 9 slice 2b; RQ-24; §34): one row per §34 credit/debit note posted in the window,
    /// read off the note's posted Output-tax lines (never recomputed), joined to its <see cref="GstCreditDebitNoteLink"/>
    /// for the original-invoice reference + reason. Each row is <b>signed by note type</b> — a credit note is negative
    /// (it reduces output), a debit note positive — and that signed tax is folded into the return's output totals so
    /// GSTR-1 nets correctly. A company with no §34 note yields an empty table and leaves the totals untouched (ER-13).
    /// </summary>
    private static IReadOnlyList<Gstr1Table9BRow> BuildTable9B(
        Company company, DateOnly from, DateOnly to, ref decimal totalCgst, ref decimal totalSgst, ref decimal totalIgst)
    {
        if (company.CreditDebitNoteLinks.Count == 0) return [];

        var rows = new List<Gstr1Table9BRow>();
        foreach (var link in company.CreditDebitNoteLinks)
        {
            var v = company.FindVoucher(link.CdnVoucherId);
            if (v is null || v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            var heads = ReadInvoiceHeads(v);              // positive magnitudes
            var taxable = GstReportSupport.InvoiceTaxableValue(v).Amount;
            var sign = link.CdnType == CdnType.Credit ? -1m : 1m;

            totalCgst += sign * heads.Cgst;
            totalSgst += sign * heads.Sgst;
            totalIgst += sign * heads.Igst;

            rows.Add(new Gstr1Table9BRow(
                link.CdnType, link.OriginalInvoiceVoucherId, link.OriginalInvoiceNumber, link.OriginalInvoiceDate,
                v.Date, GstReportSupport.PlaceOfSupply(company, v),
                new Money(sign * taxable), new Money(sign * heads.Cgst), new Money(sign * heads.Sgst),
                new Money(sign * heads.Igst), link.ReasonCode, link.Is9BTarget));
        }
        // Deterministic order: by note date, then original-invoice reference, then id-stable link order.
        return rows
            .OrderBy(r => r.NoteDate)
            .ThenBy(r => r.OriginalInvoiceNumber, StringComparer.Ordinal)
            .ThenBy(r => r.NoteType)
            .ToList();
    }

    /// <summary>
    /// Builds GSTR-1 Table 11A (advances received) + 11B (advances adjusted) off the <see cref="GstAdvanceReceipt"/>
    /// records (Phase 9 slice 2b; RQ-25) — the source of truth for the advance tax. 11A groups the service advances whose
    /// <b>receipt</b> voucher falls in the window (goods advances are de-taxed → excluded); 11B groups those whose
    /// <b>adjustment</b> (invoice) <b>or Rule-51 refund</b> voucher falls in the window (a refund reverses the advance
    /// exactly like an adjustment). Each group's CGST/SGST/IGST split is reproduced from the record's net advance + rate +
    /// POS via the same total-then-split rule (paisa-exact). Empty when unused (ER-13).
    /// </summary>
    private static (IReadOnlyList<Gstr1AdvanceRow> Table11A, IReadOnlyList<Gstr1AdvanceAdjustedRow> Table11B)
        BuildAdvanceTables(Company company, DateOnly from, DateOnly to)
    {
        if (company.AdvanceReceipts.Count == 0) return ([], []);

        var received = new Dictionary<(int Rate, bool Inter), (decimal Adv, decimal Cgst, decimal Sgst, decimal Igst)>();
        var adjusted = new Dictionary<(int Rate, bool Inter), (decimal Adv, decimal Cgst, decimal Sgst, decimal Igst)>();

        bool InWindow(Guid? voucherId)
        {
            if (voucherId is not { } vid || company.FindVoucher(vid) is not { } v) return false;
            if (v.Date < from) return false;
            var type = company.FindVoucherType(v.TypeId);
            return type is not null && LedgerBalances.CountsAsOf(v, to, type.BaseType);
        }

        void Accumulate(
            Dictionary<(int, bool), (decimal, decimal, decimal, decimal)> acc, GstAdvanceReceipt a)
        {
            var split = GstService.ComputeLineTax(a.AdvanceAmount, a.RateBasisPoints, a.InterState);
            var key = (a.RateBasisPoints, a.InterState);
            var (adv, cg, sg, ig) = acc.TryGetValue(key, out var cur) ? cur : (0m, 0m, 0m, 0m);
            acc[key] = (adv + a.AdvanceAmount.Amount, cg + split.Cgst.Amount, sg + split.Sgst.Amount, ig + split.Igst.Amount);
        }

        foreach (var a in company.AdvanceReceipts)
        {
            if (!a.IsService || a.AdvanceTax.Amount == 0m) continue; // goods advances are de-taxed — no 11A/11B
            if (InWindow(a.ReceiptVoucherId)) Accumulate(received, a);
            if (InWindow(a.AdjustedAgainstInvoiceVoucherId)) Accumulate(adjusted, a);
            // A Rule-51 REFUND reverses the advance: net it back out in the refund period exactly like an adjustment
            // (11B), so a refunded advance's 11A − 11B collapses to 0 and reconciles with the zero ledger balance
            // (finding #1). Without this the 11A liability stays reported forever though the books say 0.
            if (InWindow(a.RefundVoucherId)) Accumulate(adjusted, a);
        }

        var t11a = received
            .OrderBy(kv => kv.Key.Rate).ThenBy(kv => kv.Key.Inter)
            .Select(kv => new Gstr1AdvanceRow(kv.Key.Rate, kv.Key.Inter,
                new Money(kv.Value.Adv), new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        var t11b = adjusted
            .OrderBy(kv => kv.Key.Rate).ThenBy(kv => kv.Key.Inter)
            .Select(kv => new Gstr1AdvanceAdjustedRow(kv.Key.Rate, kv.Key.Inter,
                new Money(kv.Value.Adv), new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        return (t11a, t11b);
    }

    /// <summary>
    /// The Table-4B outward-RCM-supply value (Phase 9 slice 2; RQ-7) — Σ the outward supply value of vouchers whose
    /// sales/expense ledger carries an <b>outward</b> reverse-charge flag (the recipient pays the tax, so the invoice bears
    /// none). Reads posted amounts only. A company with no such supply yields <c>Money.Zero</c> (byte-identical, ER-13).
    /// </summary>
    private static Money ComputeRcm4BOutwardValue(Company company, DateOnly from, DateOnly to)
    {
        if (!company.GstEnabled) return Money.Zero;
        var total = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output))
        {
            // A Credit Note against an outward RCM supply REDUCES the 4B value (it nets the original supply down),
            // mirroring how an outward return nets down a rate row; a Sales voucher adds. Signing by base type (rather
            // than treating every posted line as additive) keeps 4B from being inflated by a credit note (Phase 9 S2; RQ-7).
            var sign = type.BaseType == VoucherBaseType.CreditNote ? -1m : 1m;
            foreach (var line in voucher.Lines)
                if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { ReverseChargeApplicable: true })
                    total += sign * line.Amount.Amount;
        }
        return new Money(total);
    }

    /// <summary>Reads a voucher's posted forward tax by head off its <see cref="GstLineTax"/> tax lines. Reverse-charge
    /// lines are excluded (finding #7): they are their own 3.1(d)/4A buckets, so folding an identical head set here keeps
    /// Table 9B aligned with GSTR-3B <c>ReadCdn</c> (which already skips them) and consistent with
    /// <see cref="GstReportSupport.InvoiceTaxableValue"/> (which excludes them too). An ordinary outward invoice carries no
    /// reverse-charge forward-tax line, so this is a defensive alignment — byte-identical for the reachable paths.</summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst) ReadInvoiceHeads(Voucher voucher)
    {
        var cgst = 0m; var sgst = 0m; var igst = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: cgst += line.Amount.Amount; break;
                case GstTaxHead.State: sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: igst += line.Amount.Amount; break;
            }
        }
        return (cgst, sgst, igst);
    }

    /// <summary>
    /// The per-(integrated rate) breakdown of an outward invoice's posted tax, read off its tax lines (never
    /// recomputed). One entry per distinct integrated rate — the group's per-head tax (CGST/SGST/IGST) and its
    /// taxable value (the max <see cref="GstLineTax.TaxableValue"/> across the group's lines, which dedups the
    /// equal-valued CGST and SGST legs of an intra group). A multi-rate invoice yields multiple entries so the
    /// rate-wise summary / B2C consolidation attribute each rate correctly; an all-exempt sale yields none.
    /// Ordered by rate for determinism.
    /// </summary>
    private static IReadOnlyList<(int Rate, HeadAmounts Amounts)> ReadInvoiceRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, HeadAmounts>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;
            // Phase 9 slice 1: a Compensation-Cess line is ring-fenced (own column/total, ER-2), NOT a CGST/SGST/IGST
            // rate group. Its (doubled) cess-rate key would otherwise inject a phantom rate row into the rate-wise /
            // B2C consolidation and duplicate the group's taxable value. Skip it here; reports read cess separately.
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g);
            if (!byRate.TryGetValue(rate, out var acc)) byRate[rate] = acc = new HeadAmounts();
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: acc.Cgst += line.Amount.Amount; break;
                case GstTaxHead.State: acc.Sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: acc.Igst += line.Amount.Amount; break;
            }
            // The group taxable is the same on every line of the group; take the max so the intra CGST+SGST legs
            // (equal taxable) are not double-counted.
            if (g.TaxableValue.Amount > acc.Taxable) acc.Taxable = g.TaxableValue.Amount;
        }
        return byRate.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Attributes an outward invoice's posted tax to its item-invoice stock lines, grouping by HSN/SAC. Tax is
    /// attributed <b>per rate group</b>: each stock line's tax is a value-share of ITS OWN integrated-rate
    /// group's posted tax (not the blended invoice total), so a multi-rate invoice shows each HSN row its true
    /// rate's tax (e.g. Widget @18% ⇒ 180, Gadget @5% ⇒ 25 — not a value-share blend of 205). Within a rate
    /// group the value-share is paisa-exact (the group's last line absorbs the rounding remainder), so Σ line tax
    /// == the group's posted tax and Σ over all groups == the invoice tax. An exempt/nil supply (zero invoice
    /// tax) adds only its value to the exempt bucket and its HSN row. Reads only posted amounts — it never
    /// recomputes tax from a rate; the per-line RATE is read from the item's GST master purely to bucket the line
    /// into the matching posted rate group.
    /// </summary>
    private static void AccumulateHsn(
        Company company, Voucher voucher, (decimal Cgst, decimal Sgst, decimal Igst) invoice,
        Dictionary<string, HsnAcc> hsnAcc, ref decimal exempt)
    {
        var invoiceTax = invoice.Cgst == 0m && invoice.Sgst == 0m && invoice.Igst == 0m;
        if (invoiceTax)
        {
            // An all-exempt/nil outward supply: record its value against exempt + its HSN row (zero tax).
            foreach (var il in voucher.InventoryLines)
            {
                exempt += il.Value.Amount;
                AddHsnRow(company, il, il.Value.Amount, 0m, 0m, 0m, hsnAcc);
            }
            return;
        }

        if (voucher.InventoryLines.Count == 0)
        {
            // As-voucher (no stock lines): nothing to attribute to HSN; return.
            return;
        }

        // The posted per-(integrated rate) tax groups for this invoice (from its tax lines).
        var rateGroups = ReadInvoiceRateGroups(voucher);

        // Bucket the stock lines by their integrated rate so each line's tax comes from its OWN rate group.
        // When the invoice is single-rate, every taxable line falls in that one group regardless of where the
        // rate resolved; for a multi-rate invoice a line's rate is read from the item's GST master.
        var singleRate = rateGroups.Count == 1 ? rateGroups[0].Rate : (int?)null;
        var linesByRate = new Dictionary<int, List<VoucherInventoryLine>>();
        foreach (var il in voucher.InventoryLines)
        {
            var rate = singleRate ?? LineIntegratedRate(company, il);
            if (!linesByRate.TryGetValue(rate, out var list)) linesByRate[rate] = list = new List<VoucherInventoryLine>();
            list.Add(il);
        }

        foreach (var (rate, group) in rateGroups)
        {
            if (!linesByRate.TryGetValue(rate, out var groupLines) || groupLines.Count == 0)
                continue; // no matched stock line for this rate group (defensive; e.g. as-voucher-only tax)

            var groupValue = groupLines.Sum(l => l.Value.Amount);
            if (groupValue == 0m) continue;

            var runCgst = 0m; var runSgst = 0m; var runIgst = 0m;
            for (var i = 0; i < groupLines.Count; i++)
            {
                var il = groupLines[i];
                var value = il.Value.Amount;
                decimal cgst, sgst, igst;
                if (i == groupLines.Count - 1)
                {
                    // The group's last line absorbs the remainder so Σ line tax == the group's posted tax exactly.
                    cgst = group.Cgst - runCgst;
                    sgst = group.Sgst - runSgst;
                    igst = group.Igst - runIgst;
                }
                else
                {
                    cgst = Apportion(group.Cgst, value, groupValue);
                    sgst = Apportion(group.Sgst, value, groupValue);
                    igst = Apportion(group.Igst, value, groupValue);
                    runCgst += cgst; runSgst += sgst; runIgst += igst;
                }
                AddHsnRow(company, il, value, cgst, sgst, igst, hsnAcc);
            }
        }
    }

    /// <summary>
    /// The integrated GST rate (basis points) of an item-invoice stock line, read from its stock item's GST
    /// master (the most-granular resolution level). Used only to bucket a multi-rate invoice's stock lines into
    /// the matching posted rate group — not to compute tax. An item with no resolvable rate returns 0.
    /// </summary>
    private static int LineIntegratedRate(Company company, VoucherInventoryLine il) =>
        company.FindStockItem(il.StockItemId)?.Gst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0;

    private static decimal Apportion(decimal total, decimal value, decimal totalValue) =>
        Math.Round(total * value / totalValue, 2, MidpointRounding.AwayFromZero);

    private static void AddHsnRow(
        Company company, VoucherInventoryLine il, decimal value, decimal cgst, decimal sgst, decimal igst,
        Dictionary<string, HsnAcc> hsnAcc)
    {
        var item = company.FindStockItem(il.StockItemId);
        var hsn = item?.Gst?.HsnSac ?? item?.HsnSacCode ?? "(none)";
        var uqc = company.FindUnit(item?.BaseUnitId ?? Guid.Empty)?.UnitQuantityCode;

        if (!hsnAcc.TryGetValue(hsn, out var acc))
        {
            acc = new HsnAcc { HsnSac = hsn, Description = item?.Name ?? "(unknown)", Uqc = uqc };
            hsnAcc[hsn] = acc;
        }
        acc.Quantity += il.Quantity;
        acc.Taxable += value;
        acc.Cgst += cgst; acc.Sgst += sgst; acc.Igst += igst;
    }

    private sealed class HeadAmounts
    {
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
    }

    private sealed class HsnAcc
    {
        public string HsnSac = "";
        public string Description = "";
        public string? Uqc;
        public decimal Quantity;
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
    }
}
