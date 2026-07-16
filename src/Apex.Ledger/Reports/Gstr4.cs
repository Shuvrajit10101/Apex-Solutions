using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The <b>inward-supply</b> summary of GSTR-4 (Phase 9 slice 3; RQ-16; Tables 4A/4B/4C/4D) — a <b>light</b> projection
/// over the composition dealer's posted Purchase vouchers (value only; a composition dealer claims <b>no ITC</b>, so
/// only the value + the cash reverse-charge tax are reported). 4B carries the reverse-charge tax paid in cash.
/// </summary>
/// <param name="RegisteredValue">Table 4A — inward supplies from registered suppliers (party has a GSTIN), no RCM.</param>
/// <param name="ReverseChargeValue">Table 4B — inward supplies attracting reverse charge (the assessable value).</param>
/// <param name="ReverseChargeTax">Table 4B — the reverse-charge tax paid in cash on those supplies (excludes cess, ER-2).</param>
/// <param name="UnregisteredValue">Table 4C — inward supplies from unregistered suppliers (no RCM).</param>
/// <param name="ImportServiceValue">Table 4D — import of services (S3: zero — a carry-forward).</param>
public sealed record Gstr4Inward(
    Money RegisteredValue,
    Money ReverseChargeValue,
    Money ReverseChargeTax,
    Money UnregisteredValue,
    Money ImportServiceValue);

/// <summary>
/// <b>Form GSTR-4</b> — the composition dealer's <b>annual</b> return (Phase 9 slice 3; RQ-16; Rule 62). A pure,
/// read-only projection:
/// <list type="bullet">
///   <item><b>Table 5</b> — the four quarters' CMP-08 self-assessed liability (the <see cref="Quarters"/> roll-up).</item>
///   <item><b>Table 4A/4B/4C/4D</b> — inward supplies (light; <see cref="Inward"/>), value + cash RCM tax, no ITC.</item>
///   <item><b>Table 6</b> — the annual tax rate-wise liability (the composition rate + the RCM tax): <see cref="Annual"/>.</item>
/// </list>
/// A non-composition company yields a not-applicable (empty) return. No UI, no DB.
/// </summary>
public sealed record Gstr4(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    CompositionSubType? SubType,
    IReadOnlyList<Cmp08> Quarters,
    CompositionTax? Annual,
    Gstr4Inward Inward)
{
    /// <summary>Table 6 — the annual outward composition tax-on-turnover, taken as the <b>sum of the four already-rounded
    /// quarterly CMP-08 <see cref="Cmp08.OutwardTurnoverTax"/></b> (Table 5), so <b>Table 6 == Σ Table 5 by
    /// construction</b> — they always reconcile (no whole-FY re-rounding that could diverge on odd-paisa quarterly
    /// turnover). CGST+SGST.</summary>
    public Money AnnualCompositionTax => new(Quarters.Sum(q => q.OutwardTurnoverTax.Amount));

    /// <summary>Table 6 — the annual inward reverse-charge tax paid in cash, taken as the <b>sum of the four rounded
    /// quarterly CMP-08 <see cref="Cmp08.InwardRcmTax"/></b> (Table 5), so it reconciles to Σ Table 5 by construction.
    /// Excludes cess (ER-2).</summary>
    public Money AnnualRcmTax => new(Quarters.Sum(q => q.InwardRcmTax.Amount));

    /// <summary>Builds GSTR-4 for a composition company over the FY <c>[fyFrom, fyTo]</c>; a non-composition company
    /// yields a not-applicable return. The four quarters are derived as 3-month windows from <paramref name="fyFrom"/>.</summary>
    public static Gstr4 Build(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        if (company.Gst?.RegistrationType != GstRegistrationType.Composition)
            return new Gstr4(fyFrom, fyTo, false, null, [], null,
                new Gstr4Inward(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero));

        var quarters = new List<Cmp08>(4);
        for (var i = 0; i < 4; i++)
        {
            var qFrom = fyFrom.AddMonths(3 * i);
            var qTo = fyFrom.AddMonths(3 * (i + 1)).AddDays(-1);
            quarters.Add(Cmp08.Build(company, qFrom, qTo));
        }

        var annual = new CompositionTaxService(company).ComputeForPeriod(fyFrom, fyTo);
        var inward = BuildInward(company, fyFrom, fyTo);
        return new Gstr4(fyFrom, fyTo, true, annual.SubType, quarters, annual, inward);
    }

    /// <summary>
    /// The light inward-supply projection (Tables 4A/4B/4C/4D) over the FY's posted Purchase vouchers: value only (no
    /// ITC). A voucher that posts a reverse-charge liability is 4B (with its cash RCM tax); else a registered-supplier
    /// purchase is 4A, an unregistered-supplier purchase is 4C. Import-of-services (4D) is zero in S3 (carry-forward).
    /// </summary>
    private static Gstr4Inward BuildInward(Company company, DateOnly from, DateOnly to)
    {
        var reg = 0m; var rc = 0m; var rcTax = 0m; var urp = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Input))
        {
            if (type.BaseType != VoucherBaseType.Purchase) continue;

            var value = InwardSupplyValue(company, voucher).Amount;

            var tax = 0m; var isRcm = false;
            foreach (var line in voucher.Lines)
            {
                if (line.Gst is not { IsReverseCharge: true } g) continue;
                var isLiability = company.FindLedger(line.LedgerId)?.GstClassification is { IsReverseCharge: true };
                if (!isLiability) continue;
                isRcm = true;
                if (g.TaxHead != GstTaxHead.Cess) tax += line.Amount.Amount; // cess ring-fenced (ER-2)
            }

            if (isRcm)
            {
                rc += value;
                rcTax += tax;
            }
            else if (voucher.PartyId is Guid pid && company.FindLedger(pid)?.PartyGst?.Gstin is not null)
            {
                reg += value;
            }
            else
            {
                urp += value;
            }
        }

        return new Gstr4Inward(
            new Money(reg), new Money(rc), new Money(rcTax), new Money(urp), Money.Zero);
    }

    /// <summary>The assessable value of an inward (Purchase) voucher: the stock-line value when present, else the sum of
    /// the expense/purchase debit legs — excluding Duties &amp; Taxes ledgers and the non-creditable RCM-tax cost ledger
    /// (so the RCM cost debit never double-counts the value). Reads posted amounts only.</summary>
    private static Money InwardSupplyValue(Company company, Voucher voucher)
    {
        if (voucher.HasInventoryLines) return voucher.InventoryLinesValue;

        var sum = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Side != DrCr.Debit) continue;
            var ledger = company.FindLedger(line.LedgerId);
            if (ledger is null || ClassificationRules.IsDutiesAndTaxesLedger(ledger, company)) continue;
            if (string.Equals(ledger.Name, GstService.RcmNonCreditableCostLedgerName, StringComparison.OrdinalIgnoreCase)) continue;
            sum += line.Amount.Amount;
        }
        return new Money(sum);
    }
}

/// <summary>
/// <b>Form GSTR-9A</b> — the composition dealer's annual return (Phase 9 slice 3; RQ-16; DP-18 "light projections"). A
/// thin roll-up over the FY's composition tax-on-turnover + inward-RCM (turnover, tax paid, late fee). Explicitly
/// <b>light</b>: CMP-02 opt-in / CMP-04 withdrawal + ITC-03/ITC-01 transitions and the §47 late fee are carry-forward
/// (marked, not computed). A non-composition company yields a not-applicable return.
/// </summary>
public sealed record Gstr9a(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    Money TotalTurnover,
    Money TaxableTurnover,
    Money TaxPaidCgst,
    Money TaxPaidSgst,
    Money RcmInwardTax,
    Money LateFee)
{
    /// <summary>Σ composition tax paid on turnover (CGST + SGST).</summary>
    public Money CompositionTaxPaid => new(TaxPaidCgst.Amount + TaxPaidSgst.Amount);

    /// <summary>Builds a light GSTR-9A for a composition company over the FY; a non-composition company yields
    /// not-applicable. The tax paid is the <b>Σ of the four already-rounded quarterly CMP-08 figures</b> (Table 5) —
    /// mirroring the <see cref="Gstr4.AnnualCompositionTax"/> Σ-of-quarters template — so <b>9A reconciles to Σ CMP-08 by
    /// construction</b> (never a whole-FY <c>ComputeForPeriod</c> re-round that could diverge on odd-paisa turnover). The
    /// turnover figures are additive (no rounding), so they are taken from the whole-FY compute unchanged.</summary>
    public static Gstr9a Build(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        if (company.Gst?.RegistrationType != GstRegistrationType.Composition)
            return new Gstr9a(fyFrom, fyTo, false, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);

        var t = new CompositionTaxService(company).ComputeForPeriod(fyFrom, fyTo); // turnover (additive) only
        decimal cgst = 0m, sgst = 0m, rcm = 0m;
        for (var i = 0; i < 4; i++)
        {
            var q = Cmp08.Build(company, fyFrom.AddMonths(3 * i), fyFrom.AddMonths(3 * (i + 1)).AddDays(-1));
            cgst += q.OutwardCgst.Amount;
            sgst += q.OutwardSgst.Amount;
            rcm += q.InwardRcmTax.Amount;
        }
        return new Gstr9a(fyFrom, fyTo, true, t.TotalTurnover, t.TaxableTurnover,
            new Money(cgst), new Money(sgst), new Money(rcm), Money.Zero);
    }
}
