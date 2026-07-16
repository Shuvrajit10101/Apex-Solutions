using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Shared read-only primitives for the GST report projections (phase4-gst-requirements RQ-20..RQ-24; ER-7).
/// Every GST report reads the <b>posted</b> tax straight off the tax <see cref="EntryLine"/>s'
/// <see cref="GstLineTax"/> metadata — the head, the applied rate, the taxable value the tax was computed on,
/// and the line's own <see cref="EntryLine.Amount"/> (the tax) — so the returns never recompute tax; they
/// reconcile to the tax-ledger postings by construction. A voucher's <b>direction</b> (outward vs inward) is
/// derived from its type's base type (DP-11): Sales/Credit-Note ⇒ outward (Output tax), Purchase/Debit-Note ⇒
/// inward (Input tax). Cancelled and post-dated-after-<c>to</c> vouchers are excluded via
/// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> — the same filter the balances
/// use — so a report over the tax lines foots to the ledger postings.
/// </summary>
public static class GstReportSupport
{
    /// <summary>
    /// The GST direction implied by a voucher base type (DP-11), or <c>null</c> for a base type that never
    /// carries GST (contra/payment/receipt/journal/order/inventory/payroll). Sales &amp; Credit-Note are
    /// <b>outward</b> (an outward supply, Output tax → GSTR-1 / GSTR-3B §3.1); Purchase &amp; Debit-Note are
    /// <b>inward</b> (Input tax / ITC → GSTR-3B §4).
    /// </summary>
    public static GstTaxDirection? DirectionOf(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.Sales or VoucherBaseType.CreditNote => GstTaxDirection.Output,
        VoucherBaseType.Purchase or VoucherBaseType.DebitNote => GstTaxDirection.Input,
        _ => null,
    };

    /// <summary>
    /// Enumerates the posted vouchers that carry GST in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> (outward or inward), already filtered for cancelled / optional / provisional
    /// / post-dated-after-<paramref name="to"/> (via <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly,
    /// VoucherBaseType?)"/>) and the lower date bound. Each yielded voucher has at least one tax
    /// (<see cref="GstLineTax"/>) line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedGstVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        foreach (var pair in PostedDirectionalVouchers(company, from, to, direction))
            if (pair.Voucher.Lines.Any(l => l.HasGst))
                yield return pair;
    }

    /// <summary>
    /// Enumerates <b>all</b> posted vouchers in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> — including exempt/nil supplies that carry <b>no</b> tax line — already
    /// filtered for cancelled / optional / provisional / post-dated-after-<paramref name="to"/> and the lower
    /// date bound. GSTR-1 uses this so exempt outward supplies still appear in the HSN summary and exempt
    /// bucket; the taxable ones are the subset with a tax line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedDirectionalVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        if (!company.GstEnabled) yield break;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (DirectionOf(type.BaseType) != direction) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue; // cancelled/post-dated/date filter
            yield return (v, type);
        }
    }

    /// <summary>
    /// The place-of-supply state code for a voucher (DP-7): the party ledger's recorded GST state, falling back
    /// to the company home state for a walk-in with no recorded state. Used to label GSTR-1 rows.
    /// </summary>
    public static string? PlaceOfSupply(Company company, Voucher voucher)
    {
        if (voucher.PartyId is Guid pid && company.FindLedger(pid)?.PartyGst?.StateCode is { } code)
            return code;
        return company.Gst?.HomeStateCode;
    }

    /// <summary>
    /// True iff a voucher is an <b>outward reverse-charge supply</b> (Phase 9 slice 2; RQ-7): an outward supply whose
    /// sales ledger carries <see cref="StockItemGstDetails.ReverseChargeApplicable"/> — the <b>recipient</b> pays the tax,
    /// so the invoice bears none. Such a supply belongs <b>only</b> in GSTR-1 Table 4B / the 3.1(d)-value bucket, never in
    /// the exempt/nil/non-GST outward bucket (it would otherwise be double-represented). A pure read over the posted lines'
    /// ledgers; a company with no such supply always returns false (byte-identical, ER-13).
    /// </summary>
    public static bool IsOutwardReverseChargeSupply(Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { ReverseChargeApplicable: true })
                return true;
        return false;
    }

    /// <summary>
    /// The §34 credit/debit-note link annotating a voucher (Phase 9 slice 2b; RQ-24), or <c>null</c> when the voucher is
    /// not a formalised §34 note. A CDN-linked voucher is a first-class §34 document projected by its own outward table
    /// (GSTR-1 Table 9B, signed by <see cref="GstCreditDebitNoteLink.CdnType"/>) and folded — signed — into the output-tax
    /// buckets, so the ordinary GSTR-1/3B invoice sweeps <b>exclude</b> it (it is never double-counted, mirroring the RCM
    /// and outward-4B exclusions). A company with no §34 note always returns <c>null</c> (byte-identical, ER-13).
    /// </summary>
    public static GstCreditDebitNoteLink? CdnLinkFor(Company company, Voucher voucher) =>
        company.CreditDebitNoteLinks.FirstOrDefault(l => l.CdnVoucherId == voucher.Id);

    /// <summary>
    /// The §10 / Rule 5(f) declaration a composition dealer's <b>Bill of Supply</b> must bear (Phase 9 slice 3; RQ-10;
    /// ER-11: de-branded, never "Tally"). Printed in place of the CGST/SGST/IGST tax columns (a composition supply
    /// carries none).
    /// </summary>
    public const string BillOfSupplyDeclaration = "Composition taxable person, not eligible to collect tax on supplies";

    /// <summary>
    /// True iff a voucher is a composition dealer's <b>Bill of Supply</b> (Phase 9 slice 3; RQ-10): an outward supply
    /// (<see cref="VoucherBaseType.Sales"/>) of a company whose GST is <b>enabled</b> as Composition
    /// (<c>Gst is { Enabled: true, RegistrationType: Composition }</c>). A <b>derived</b> property (no stored flag),
    /// mirroring <see cref="IsOutwardReverseChargeSupply"/>. The <c>Enabled: true</c> gate keeps the badge consistent
    /// with the report gating: a company that enabled GST as Composition and then toggled GST OFF (the F11 disable
    /// branch clears <see cref="GstConfig.Enabled"/> but retains <c>RegistrationType = Composition</c>) renders an
    /// ordinary voucher — matching CMP-08 / GSTR-4 / the Composition-Returns menu, which all hide when GST is off. A
    /// Regular/Unregistered or GST-off company always returns false (byte-identical, ER-13). The print layer titles the
    /// document "Bill of Supply" (not "Tax Invoice") and prints <see cref="BillOfSupplyDeclaration"/>.
    /// </summary>
    public static bool IsBillOfSupply(Company company, Voucher voucher)
    {
        if (company.Gst is not { Enabled: true, RegistrationType: GstRegistrationType.Composition }) return false;
        var type = company.FindVoucherType(voucher.TypeId);
        return type?.BaseType == VoucherBaseType.Sales;
    }

    /// <summary>
    /// The <b>outward supply value</b> of a composition sale (or sale-return note), split (Total, Taxable) by GST
    /// taxability (Phase 9 slice 3; RQ-10/RQ-16; ER-9). A composition voucher carries <b>no tax lines</b>, so turnover
    /// is read from the posted stock/sales <b>value</b>, never from tax lines (<see cref="InvoiceTaxableValue"/> reads
    /// tax lines ⇒ returns 0 and must NOT be used for turnover). An item-invoice sale reads the item-line values, each
    /// classified by its stock item's <see cref="StockItemGstDetails.IsTaxable"/> (falling back to the voucher's
    /// sales-ledger GST block, else treated as taxable). An as-voucher sale sums the sales/income legs on the
    /// <b>sales-natural side</b> — CREDIT for a Sales bill, DEBIT for a sale-return <see cref="VoucherBaseType.CreditNote"/>
    /// (which reverses the sale) — so the party/cash counter-leg is never counted and a return is valued (and classified)
    /// off its own sales ledger, mirroring <see cref="Gstr1"/>'s sign-by-base-type read. Each leg is classified by its
    /// ledger's <see cref="Domain.Ledger.SalesPurchaseGst"/>; the <b>Taxable</b> component counts only an <b>explicitly</b>
    /// taxable leg (an unclassified leg is treated as non-taxable, so it never over-includes an exempt as-voucher sale
    /// into a taxable-only base — finding #1). Reads posted amounts only; the <b>sign</b> (a return nets down) is applied
    /// by the caller.
    /// </summary>
    public static (Money Total, Money Taxable) OutwardSupplyValue(Company company, Voucher voucher, VoucherBaseType baseType)
    {
        if (voucher.HasInventoryLines)
        {
            var total = 0m; var taxable = 0m;
            foreach (var il in voucher.InventoryLines)
            {
                var v = il.Value.Amount;
                total += v;
                if (LineIsTaxable(company, il, voucher)) taxable += v;
            }
            return (new Money(total), new Money(taxable));
        }

        // As-voucher supply: the supply value is the sales/income legs on the sales-natural side (CREDIT for a Sales
        // bill; DEBIT for a sale-return Credit Note, which reverses the sale). Reading the sales side — rather than
        // always the credit legs — keeps the party/cash counter-leg out and reads a return off its reversed sales leg.
        // A Duties & Taxes leg (defensive — none exist for composition) is excluded so it can never inflate turnover.
        var supplySide = baseType == VoucherBaseType.CreditNote ? DrCr.Debit : DrCr.Credit;
        var t = 0m; var tx = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Side != supplySide) continue;
            var ledger = company.FindLedger(line.LedgerId);
            if (ledger is null || ClassificationRules.IsDutiesAndTaxesLedger(ledger, company)) continue;
            var v = line.Amount.Amount;
            t += v;
            // TAXABLE component: count only an EXPLICITLY-taxable sales/income leg (finding #1). An unclassified leg
            // (no GST block) is NOT assumed taxable — that would over-include an exempt as-voucher sale into the
            // taxable-only base (Trader / §10(2A)). Total-turnover sub-types read `Total`, so an exempt sale still
            // counts for them (base-rule-aware, not a blanket flip).
            if (ledger.SalesPurchaseGst?.IsTaxable ?? false) tx += v;
        }
        return (new Money(t), new Money(tx));
    }

    /// <summary>Classifies one item-invoice line as a taxable supply: by the stock item's GST taxability, falling back
    /// to any sales-ledger GST block on the voucher, else treated as taxable (conservative for the taxable-base
    /// sub-types).</summary>
    private static bool LineIsTaxable(Company company, VoucherInventoryLine il, Voucher voucher)
    {
        if (company.FindStockItem(il.StockItemId)?.Gst is { } g) return g.IsTaxable;
        foreach (var line in voucher.Lines)
            if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { } spg) return spg.IsTaxable;
        return true;
    }

    /// <summary>
    /// The integrated-rate basis points a tax line represents, for rate-wise grouping. A CGST/SGST line carries
    /// the <b>half</b> rate on its <see cref="GstLineTax.RateBasisPoints"/> (900 for an 18% intra supply), so we
    /// double it to recover the integrated slab (1800); an IGST line already carries the full rate. A zero-rate
    /// line (unusual) stays 0.
    /// </summary>
    public static int IntegratedRateOf(GstLineTax gst) =>
        gst.TaxHead == GstTaxHead.Integrated ? gst.RateBasisPoints : gst.RateBasisPoints * 2;

    /// <summary>
    /// The taxable value attributable to a voucher's supply: the sum, <b>over each distinct integrated rate
    /// group</b>, of the max taxable value across that group's tax lines. A voucher now posts one tax line per
    /// (head, rate) group, so within one rate group the CGST and SGST lines each record the <b>same</b> group
    /// taxable subtotal (taking the max dedups the two intra heads); an IGST group has a single line. Summing the
    /// per-rate maxes yields the whole-invoice taxable value for a multi-rate invoice (e.g. 1000@18% + 500@5% ⇒
    /// 1500) while still not double-counting the CGST+SGST legs of any one rate group. A single-rate invoice
    /// reduces to the previous "max taxable across tax lines". A voucher with no tax line contributes zero.
    /// <b>Compensation-Cess lines are excluded</b> (Phase 9 slice 1): a cess line records the SAME taxable value on
    /// its own (doubled) cess-rate key, so counting it would double the CGST/SGST taxable value and inject a phantom
    /// rate group into GSTR-1/3B. Cess is a ring-fenced own-column charge, never a CGST/SGST/IGST rate group (ER-2).
    /// </summary>
    public static Money InvoiceTaxableValue(Voucher voucher)
    {
        var maxByRate = new Dictionary<int, decimal>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue; // ring-fenced cess is not a CGST/SGST/IGST rate group
            if (g.IsReverseCharge) continue;            // Phase 9 slice 2: RCM lines are their own buckets, not forward taxable value
            var rate = IntegratedRateOf(g);
            var cur = maxByRate.TryGetValue(rate, out var m) ? m : 0m;
            if (g.TaxableValue.Amount > cur) maxByRate[rate] = g.TaxableValue.Amount;
        }
        return new Money(maxByRate.Values.Sum());
    }

    /// <summary>
    /// The total posted <b>Compensation-Cess</b> on a voucher — the sum of the <see cref="GstTaxHead.Cess"/>,
    /// non-reverse-charge tax-line amounts (Phase 9 slice 5; ER-9). A <b>pure read of the posted lines</b>: because S1's
    /// cess compute already dropped non-tobacco cess on/after 22-Sep-2025 (no cess line posted), this is <b>date-aware by
    /// construction</b> with <b>zero</b> date logic — reading the posted lines IS the date-aware mechanism (risk #1). This
    /// single implementation is shared by <c>EInvoiceJson</c> and the e-Way consignment-value / <c>EWayBillJson</c> writers
    /// so the two can never drift. A voucher with no posted cess line returns <see cref="Money.Zero"/>.
    /// </summary>
    public static Money PostedCessTotal(Voucher voucher)
    {
        var cess = 0m;
        foreach (var line in voucher.Lines)
            if (line.Gst is { TaxHead: GstTaxHead.Cess, IsReverseCharge: false })
                cess += line.Amount.Amount;
        return new Money(cess);
    }

    /// <summary>
    /// The total posted <b>forward</b> GST (CGST + SGST + IGST) on a voucher — the sum of the non-cess,
    /// non-reverse-charge tax-line amounts (Phase 9 slice 5; ER-9). A pure read of the posted lines, mirroring the head
    /// exclusions of <see cref="InvoiceTaxableValue"/> (ring-fenced cess and RCM lines never inflate the forward tax). Used
    /// by the e-Way consignment-value engine; a voucher with no forward tax returns <see cref="Money.Zero"/>.
    /// </summary>
    public static Money PostedForwardTaxTotal(Voucher voucher)
    {
        var tax = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead is GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)
                tax += line.Amount.Amount;
        }
        return new Money(tax);
    }

    /// <summary>
    /// True iff a voucher posts at least one <b>forward</b> GST tax line (a non-reverse-charge CGST/SGST/IGST line) —
    /// i.e. it is a <b>regular tax-scheme</b> supply whose assessable value lives on its tax lines
    /// (<see cref="InvoiceTaxableValue"/>). A supply with <b>no</b> forward tax line — a Composition dealer's Bill of
    /// Supply, an exempt-only movement, or any other no-tax goods movement — carries its value only on the posted
    /// stock/sales lines, so its consignment value must be read from <see cref="OutwardSupplyValue"/> instead (Phase 9
    /// slice 5, finding #1). Mirrors the head/RCM exclusions of <see cref="PostedForwardTaxTotal"/> exactly, so the two
    /// can never disagree on what "carries forward tax" means. A voucher with no tax line returns <c>false</c>.
    /// </summary>
    public static bool HasForwardTaxLines(Voucher voucher)
    {
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead is GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)
                return true;
        }
        return false;
    }

    /// <summary>One posted reverse-charge tax line in a report window (Phase 9 slice 2; RQ-7).</summary>
    /// <param name="Voucher">The voucher the RCM line was posted on (a Purchase for an inward RCM supply).</param>
    /// <param name="Gst">The line's GST detail (head, rate, taxable value; carries the RCM tag + scheme).</param>
    /// <param name="Amount">The posted tax amount (paisa-exact).</param>
    /// <param name="IsOutputLiability">True ⇒ the RCM Output liability leg (→ GSTR-3B 3.1(d)); false ⇒ the ITC leg.</param>
    /// <param name="Scheme">The ITC bucket for the ITC leg (ImportOfServices → 4A(2), OtherRcm → 4A(3)); <c>null</c> on the liability leg.</param>
    public readonly record struct RcmLine(
        Voucher Voucher, GstLineTax Gst, Money Amount, bool IsOutputLiability, RcmItcScheme? Scheme);

    /// <summary>
    /// Enumerates every posted <b>reverse-charge</b>-tagged tax line in the window <c>[from, to]</c> (Phase 9 slice 2;
    /// RQ-7), a pure projection over the posted lines' <see cref="GstLineTax.IsReverseCharge"/> tag — never a recompute
    /// (ER-9). RCM breaks the 1:1 base-type→direction map (a Purchase yields an Output liability), so this scans <b>all</b>
    /// directions, filtered for cancelled / optional / provisional / post-dated-after-<paramref name="to"/> (via
    /// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/>) and the lower date bound. A line
    /// posting to an <c>IsReverseCharge</c> classification ledger is the output liability (→ 3.1(d)); an RCM-tagged line on
    /// an ordinary Input ledger is the ITC (→ 4A(2)/4A(3)). GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<RcmLine> RcmLines(Company company, DateOnly from, DateOnly to)
    {
        if (!company.GstEnabled) yield break;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue; // cancelled/post-dated/date filter
            foreach (var line in v.Lines)
            {
                if (line.Gst is not { IsReverseCharge: true } g) continue;
                var isOutput = company.FindLedger(line.LedgerId)?.GstClassification is { IsReverseCharge: true };
                yield return new RcmLine(v, g, line.Amount, isOutput, g.RcmScheme);
            }
        }
    }
}
