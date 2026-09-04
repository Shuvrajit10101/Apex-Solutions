using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>A DEFECT THAT NEITHER BRANCH HAD AND THE MERGE OF THE TWO CREATED — it compiled, and every test on both
/// sides stayed green.</b> This file exists because that is the exact failure mode this project keeps paying for,
/// and because the merged code was correct in every line either author wrote.
///
/// <para><b>What each branch did.</b> One branch fixed <b>T0-20</b>: the dated <see cref="GstConfig.RateHistory"/>
/// override used to be keyed by a hard-coded two-rung <c>item ?? ledger</c> HSN pick that ignored
/// <see cref="GstConfig.SourceOfGstRate"/>, so on a <see cref="GstDetailSource.LedgerFirst"/> book the base rate came
/// from the LEDGER while the row that REPLACED it was matched on the ITEM's HSN — <i>a second, inconsistent
/// resolution</i>. It replaced that pick with <c>GstService.ResolveHsnSac</c>, which walks the SAME
/// <c>Hierarchy</c> in the SAME order. The other branch added
/// <see cref="GstService.TaxabilityIsSourceOrderDependent"/>, which has to ask what the OTHER published order would
/// say, and threaded an explicitly named <c>source</c> through <c>ResolveRateUnder</c> to do it.
///
/// <para><b>What the merge produced.</b> <c>ResolveRateUnder</c> resolved the BASE under its named
/// <c>source</c> — and then called <c>ResolveHsnSac</c>, which re-read the order from the config. So on the
/// counterfactual arm, the one whose whole purpose is to resolve under the order the book is NOT using, the rate
/// walked one way and the HSN walked the other. That is T0-20's own defect, in T0-20's own words, reintroduced
/// three commits after it was closed.</para>
///
/// <para><b>Honest scope, stated rather than inflated: no shipped screen or report can reach it today.</b>
/// <c>GstReportSupport.IsWhollyExemptItemSupply</c> is the only consumer, and it consults the counterfactual only
/// after the LIVE resolution has already said TAXABLE — which means the taxable arm is always the CONFIGURED one,
/// and on the configured arm the named source and the config agree, so the two spellings cannot differ. The defect
/// is therefore latent. It is pinned anyway because <c>TaxabilityIsSourceOrderDependent</c> is <b>public</b>, a
/// second caller costs one line, and "wrong but currently unreachable" is precisely the state that a later slice
/// turns into wrong money without anyone re-deriving why it was safe.</para>
/// </summary>
public sealed class GstSourceOrderCounterfactualTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    private const string ItemHsn = "ITEMHSN";
    private const string LedgerHsn = "LEDGHSN";

    /// <summary>
    /// 🔴 <b>THE COUNTERFACTUAL MUST BE ANSWERED BY ONE WALK — the rate and the HSN cannot resolve under different
    /// orders.</b>
    ///
    /// <para><b>The fixture, chosen so exactly one thing can differ.</b> The book is
    /// <see cref="GstDetailSource.LedgerFirst"/> (the shipped default). The stock item is <b>Taxable at 1800 bp</b>
    /// under HSN <c>ITEMHSN</c>; the sales ledger is <b>Exempt</b> under SAC <c>LEDGHSN</c>. One dated rate-history
    /// row exists and it is keyed on <b><c>LEDGHSN</c></b>, at <b>0 bp</b>, in force on the voucher date. There is
    /// deliberately NO row for <c>ITEMHSN</c>.</para>
    ///
    /// <para><b>Both arms, derived by hand from the two published order strings — not read off the resolver.</b></para>
    /// <list type="bullet">
    ///   <item><b><c>LedgerFirst</c> arm</b> (the configured order): the ledger answers first and declares Exempt, so
    ///     the base is NON-TAXABLE. The dated override never runs, because it is gated on
    ///     <c>baseRes.IsTaxable</c>. Result: not taxable.</item>
    ///   <item><b><c>StockItemFirst</c> arm</b> (the counterfactual): the item answers first and declares Taxable at
    ///     1800 bp. The override then runs, and <b>everything turns on which HSN it is keyed by</b>:
    ///     <list type="bullet">
    ///       <item>keyed by the SAME walk (<c>StockItemFirst</c>) the rate used — the first rung declaring an HSN is
    ///         the stock item, so the key is <c>ITEMHSN</c>; no row matches; the rate stands at
    ///         <b>1800 bp</b>.</item>
    ///       <item>keyed by the CONFIGURED walk instead — the first rung declaring an HSN is the ledger, so the key
    ///         is <c>LEDGHSN</c>; the 0 bp row matches; the rate is replaced by <b>0 bp</b>, a rate belonging to a
    ///         classification this arm never resolved through.</item>
    ///     </list></item>
    /// </list>
    ///
    /// <para><b>So the two spellings give opposite ANSWERS, not merely different internals.</b> The two orders
    /// disagree on taxability either way and neither leaves the line unresolved, so the verdict reduces to the third
    /// clause — <i>does the TAXABLE reading carry a POSITIVE rate?</i> One walk says 1800 &gt; 0 and the method
    /// answers <b>true</b>; two walks say 0 &gt; 0 is false and it answers <b>false</b>. That third clause is the
    /// one that decides whether an already-issued document's title may be anchored, so a wrong answer here is not
    /// cosmetic.</para>
    ///
    /// <para><b>Measured, not assumed.</b> Reverting the single <c>source</c> argument on the
    /// <c>ResolveHsnSac</c> call makes this test fail with <c>Assert.True() Failure</c>; restoring it makes it pass.
    /// The two guards below are non-vacuity checks — they assert the fixture really does produce the disagreement
    /// and the positive rate the verdict depends on, so a future edit that quietly makes the fixture agree cannot
    /// leave this test passing for the wrong reason.</para>
    /// </summary>
    [Fact]
    public void The_counterfactual_order_keys_its_dated_override_by_the_order_it_resolved_under()
    {
        var (c, item, salesLedger) = Fixture();
        var gst = new GstService(c);

        // Non-vacuity 1 — the CONFIGURED order really does read this line as exempt, so the counterfactual is the
        // arm that matters. (1800 bp on 1,000.00 would be 180.00 of tax; Exempt posts none.)
        var live = gst.ResolveRate(item, salesLedger, SaleDate);
        Assert.False(GstService.IsUnresolved(live));
        Assert.False(live.IsTaxable);
        Assert.Equal(GstTaxability.Exempt, live.Taxability);

        // Non-vacuity 2 — the dated row really is live on this date and really would bite if it were consulted.
        var row = Assert.Single(c.Gst!.RateHistory, h => h.HsnSac == LedgerHsn);
        Assert.Equal(0, row.RateBasisPoints);
        Assert.True(row.IsEffectiveOn(SaleDate));

        // 🔴 THE ASSERTION. True iff the StockItemFirst arm kept its own 1800 bp — i.e. iff the override was keyed
        // by ITEMHSN, the classification that arm actually resolved through. Keyed by LEDGHSN it becomes 0 bp and
        // this is false.
        Assert.True(gst.TaxabilityIsSourceOrderDependent(item, salesLedger, SaleDate));
    }

    /// <summary>
    /// The same fixture with the dated row removed entirely: the answer must be <b>true</b> either way, because with
    /// no row there is nothing for a mis-keyed override to substitute. This is the control — it proves the failure
    /// above is caused by the KEY and not by the fixture's taxability shape, which is identical here.
    /// </summary>
    [Fact]
    public void With_no_dated_row_at_all_both_spellings_agree_and_the_answer_is_the_same()
    {
        var (c, item, salesLedger) = Fixture(withDatedRow: false);
        var gst = new GstService(c);

        Assert.DoesNotContain(c.Gst!.RateHistory, h => h.HsnSac == LedgerHsn || h.HsnSac == ItemHsn);
        Assert.True(gst.TaxabilityIsSourceOrderDependent(item, salesLedger, SaleDate));
    }

    // ================================================================= fixture

    private static (Company Company, StockItem Item, Domain.Ledger SalesLedger) Fixture(bool withDatedRow = true)
    {
        var c = CompanyFactory.CreateSeeded("Counterfactual Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        // The shipped default, and what every v51+ book carries.
        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;

        var inv = new InventoryService(c);
        var item = inv.CreateStockItem(
            "Counterfactual Widget", inv.CreateStockGroup("Goods").Id,
            inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS").Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = ItemHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var salesLedger = new Domain.Ledger(
            Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(salesLedger);
        salesLedger.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = LedgerHsn, Taxability = GstTaxability.Exempt,
        };

        if (withDatedRow)
            c.Gst!.AddRateHistory(new GstRateHistoryEntry(
                Guid.NewGuid(), LedgerHsn, 0, GstRateClass.Merit,
                FyStart, null, GstValuationBasis.TransactionValue, "Counterfactual nil window"));

        return (c, item, salesLedger);
    }
}
