using System;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>Q-A — THE CESS NARROWING, AND THE INDEPENDENT WALK THAT REMOVES ITS REACHABLE HALF. THIS FILE PINS AN
/// ASSUMPTION, NOT A RULING.</b>
///
/// <para><b>What was measured</b> (<c>docs/full-clone-census.md</c> §1.3 item 15, open R12 question 1; register
/// <c>docs/invented-vs-cloned.md</c> IV-40): on a <see cref="GstDetailSource.LedgerFirst"/> book a sales ledger
/// declaring a rate but <b>no cess fields</b> wins the walk and therefore supplied the cess too — i.e. none — even
/// when the stock item declared one. An item with ad-valorem cess at 1200 bp under a ledger at 18% with no cess, on a
/// taxable value of <b>10,000.00</b>, yielded cess <b>1,200.00 under <see cref="GstDetailSource.StockItemFirst"/></b>
/// and <b>0.00 under <see cref="GstDetailSource.LedgerFirst"/></b>.</para>
///
/// <para>🔴 <b>THE ASSUMPTION BUILT HERE (A-QA) — AN ASSUMPTION, NOT A USER RULING AND NOT A CORPUS FACT.</b>
/// <i>Cess walks INDEPENDENTLY of the rate: a rung silent on cess does not suppress a lower rung's declared cess</i> —
/// the same way the design already lets HSN and rate walk independently (IV-39). It is one-line reversible at
/// <c>GstService.CessWalksIndependentlyOfTheRate</c>; setting that constant to <c>false</c> restores, exactly, the
/// one-winning-block behaviour the figures above describe. The R12 question stays open.</para>
///
/// <para>🔴 <b>ONLY THE REACHABLE HALF IS FIXED, AND THE OTHER HALF IS A SCHEMA ESCALATION.</b> The two DETAILED
/// rungs (Stock Item and Sales/Purchase Ledger) both carry <see cref="StockItemGstDetails"/>, which has the cess
/// fields, so cess can walk between them with no schema change at all. The three NARROW rungs (Stock Group,
/// accounting Group, Company) carry <see cref="MasterGstDetails"/> — four fields, no cess, no reverse charge, no
/// §17(5) ITC eligibility — so a rate resolved there still bears no cess. That residue is IV-40's narrowing against
/// the attested GST Classification screen (BOOK PDF p.234, printed 230) and it CANNOT be closed without widening
/// <see cref="MasterGstDetails"/>, i.e. a schema change, i.e. an escalation. It is left standing and is still pinned
/// by <c>GstWinningBlockTests.A_rate_resolved_at_a_narrow_rung_bears_no_cess_even_on_a_cess_bearing_HSN</c>.</para>
///
/// <para><b>A forced limit of the field shape, stated so it is not mistaken for a design choice.</b>
/// <c>CessApplicable</c> is a non-nullable <c>bool</c>, so a rung that says "cess does NOT apply" is
/// indistinguishable from a rung that says nothing about cess. Under A-QA both read as SILENT and the walk continues
/// below them. Distinguishing them needs a nullable column — the same escalation.</para>
/// </summary>
public sealed class GstCessIndependentWalkTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly VoucherDate = new(2024, 6, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // ================================================================= the fix

    /// <summary>
    /// 🔴 <b>THE MEASURED FIGURE, MADE ORDER-INDEPENDENT.</b> Item: Taxable, no rate, ad-valorem cess 1200 bp.
    /// Sales ledger: Taxable at 1800 bp, silent on cess. The rate is 1800 bp under BOTH orders (under
    /// <see cref="GstDetailSource.StockItemFirst"/> the rate walk falls THROUGH the rate-less item block; under
    /// <see cref="GstDetailSource.LedgerFirst"/> the ledger is simply first). Under A-QA the CESS is now 1,200.00
    /// under both orders as well.
    ///
    /// <para>DERIVED, to the paisa: 10,000.00 x 1200 / 10000 = 1,200.00 exactly.</para>
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.StockItemFirst)]
    [InlineData(GstDetailSource.LedgerFirst)]
    public void A_rung_silent_on_cess_does_not_suppress_a_lower_rungs_declared_cess(GstDetailSource source)
    {
        var (gst, item, ledger) = Probe(source,
            itemCessBp: 1200, itemRateBp: null, ledgerRateBp: 1800, ledgerCessBp: null);

        Assert.Equal(1800, gst.ResolveRate(item, ledger, VoucherDate).RateBasisPoints);

        var cess = gst.ResolveCess(item, ledger, VoucherDate, quantity: 1m);
        Assert.NotNull(cess);
        Assert.Equal(CessValuationMode.AdValorem, cess!.Value.Mode);
        Assert.Equal(new Money(1_200.00m), cess.Value.ComputeCess(new Money(10_000.00m)));
    }

    // ================================================================= the walk is still a walk

    /// <summary>
    /// 🔴 <b>INDEPENDENT DOES NOT MEAN ITEM-FIRST.</b> When a rung ABOVE declares cess of its own, it wins — the cess
    /// lookup is the same ordered walk, stopping at the first rung that DECLARES cess. Sales ledger: 1800 bp with
    /// ad-valorem cess 600 bp. Item: ad-valorem cess 1200 bp. Under <see cref="GstDetailSource.LedgerFirst"/> the
    /// ledger's 600 bp wins; under <see cref="GstDetailSource.StockItemFirst"/> the item's 1200 bp wins.
    ///
    /// <para>DERIVED: 10,000.00 x 600 / 10000 = 600.00; 10,000.00 x 1200 / 10000 = 1,200.00.</para>
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst, 600.00)]
    [InlineData(GstDetailSource.StockItemFirst, 1200.00)]
    public void The_first_rung_that_DECLARES_cess_wins_it(GstDetailSource source, decimal expectedCess)
    {
        var (gst, item, ledger) = Probe(source,
            itemCessBp: 1200, itemRateBp: null, ledgerRateBp: 1800, ledgerCessBp: 600);

        var cess = gst.ResolveCess(item, ledger, VoucherDate, quantity: 1m);
        Assert.NotNull(cess);
        Assert.Equal(new Money(expectedCess), cess!.Value.ComputeCess(new Money(10_000.00m)));
    }

    /// <summary>
    /// 🔴 <b>THE EXEMPT SHORT-CIRCUIT IS UNTOUCHED — cess never over-collects on an exempt supply.</b> The sales
    /// ledger declares Exempt and wins the walk under <see cref="GstDetailSource.LedgerFirst"/>, so the line bears no
    /// tax and therefore no cess, even though the ITEM below it declares ad-valorem cess at 1200 bp. A-QA widens
    /// WHICH rung supplies the cess figures; it does not widen WHETHER cess applies at all, and that gate still reads
    /// the rung the RATE walk landed on.
    /// </summary>
    [Fact]
    public void An_exempt_winning_block_still_suppresses_a_lower_rungs_cess()
    {
        var c = GstCompany();
        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var stockGroup = inv.CreateStockGroup("Probe SG");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Aerated Water", stockGroup.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = null,
            CessApplicable = true,
            CessValuationMode = CessValuationMode.AdValorem,
            CessRateBasisPoints = 1200,
        };

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Probe Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        ledger.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Exempt };
        c.AddLedger(ledger);

        Assert.False(gst.ResolveRate(item, ledger, VoucherDate).IsTaxable);
        Assert.Null(gst.ResolveCess(item, ledger, VoucherDate, quantity: 1m));
    }

    /// <summary>
    /// 🔴 <b>"DECLARES CESS" IS <c>CessApplicable</c> ALONE — A MISSING <c>CessValuationMode</c> IS NOT SILENCE.</b>
    /// A rung may say "cess applies here, take the figures from the dated cess master by my HSN"
    /// (<see cref="StockItemGstDetails.EnsureValid"/> permits <c>CessApplicable</c> with no mode, and
    /// <see cref="GstService.ResolveCess"/>'s second route is exactly that inheritance). The sales ledger does so
    /// here, on HSN <c>22021010</c>, while the item below it states explicit ad-valorem figures of 1200 bp.
    ///
    /// <para>Under <see cref="GstDetailSource.LedgerFirst"/> the LEDGER wins the cess walk — it declared first — so
    /// the charge is the dated master's <b>600 bp</b>, NOT the item's 1200 bp. This is the guard against the
    /// tempting mis-reading of A-QA in which a lower rung's explicit figures overrule a higher rung that had
    /// already answered the cess question.</para>
    ///
    /// <para>DERIVED: 10,000.00 x 600 / 10000 = 600.00 exactly.</para>
    /// </summary>
    [Fact]
    public void A_rung_declaring_cess_without_figures_still_wins_the_cess_walk()
    {
        const string CessHsn = "22021010";

        var c = GstCompany();
        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;
        c.Gst!.AddCessRate(new GstCessRate(
            Guid.NewGuid(), CessHsn, CessValuationMode.AdValorem,
            cessRateBasisPoints: 600, cessPerUnit: Money.Zero, cessRspFactorMillis: 0,
            effectiveFrom: FyStart, effectiveTo: null, label: "Probe aerated water cess"));

        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var stockGroup = inv.CreateStockGroup("Probe SG");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Aerated Water", stockGroup.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = null,
            CessApplicable = true,
            CessValuationMode = CessValuationMode.AdValorem,
            CessRateBasisPoints = 1200,
        };

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Probe Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        ledger.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = CessHsn,
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            CessApplicable = true,          // declares that cess applies; leaves the FIGURES to the dated master
        };
        c.AddLedger(ledger);

        var cess = gst.ResolveCess(item, ledger, VoucherDate, quantity: 1m);
        Assert.NotNull(cess);
        Assert.Equal(new Money(600.00m), cess!.Value.ComputeCess(new Money(10_000.00m)));
    }

    // ================================================================= fixture

    private static (GstService Gst, StockItem Item, Domain.Ledger Ledger) Probe(
        GstDetailSource source, int? itemCessBp, int? itemRateBp, int? ledgerRateBp, int? ledgerCessBp)
    {
        var c = GstCompany();
        c.Gst!.SourceOfGstRate = source;
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        var stockGroup = inv.CreateStockGroup("Probe SG");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Aerated Water", stockGroup.Id, nos.Id);
        item.Gst = Block(GstTaxability.Taxable, itemRateBp, itemCessBp);

        var ledger = new Domain.Ledger(Guid.NewGuid(), "Probe Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        ledger.SalesPurchaseGst = Block(GstTaxability.Taxable, ledgerRateBp, ledgerCessBp);
        c.AddLedger(ledger);

        return (gst, item, ledger);
    }

    private static StockItemGstDetails Block(GstTaxability taxability, int? rateBp, int? cessBp)
    {
        var block = new StockItemGstDetails { Taxability = taxability, RateBasisPoints = rateBp };
        if (cessBp is { } bp)
        {
            block.CessApplicable = true;
            block.CessValuationMode = CessValuationMode.AdValorem;
            block.CessRateBasisPoints = bp;
        }
        return block;
    }

    private static Company GstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Cess Walk Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }
}
