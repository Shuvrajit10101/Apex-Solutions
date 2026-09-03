using System;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// ONE WALK, ONE WINNING BLOCK (slice S2a, item 4). <c>ResolveCess</c> and <c>RcmService</c> used to RE-PICK a
/// level for themselves (<c>item?.Gst ?? spLedger?.SalesPurchaseGst</c>) instead of consuming the level the rate
/// walk landed on. They agreed with the rate only because all three expressions happened to be item-then-ledger;
/// the moment the walk grows a rung the three can disagree, and a line can be RATED off one master while its cess
/// and its reverse-charge category are read off another.
///
/// <para><b>THIS FILE IS AN S2a NO-OP PROOF, NOT A BEHAVIOUR CHANGE.</b> The rule the resolver now publishes is
/// "the first rung along the walk that declares a block wins; if that rung carries a NARROW
/// <see cref="MasterGstDetails"/> it has no cess and no reverse-charge fields, so the line bears neither." Under
/// S2a's walk (Stock Item, Ledger, Accounting Group, Stock Group, Company) the two detailed rungs come FIRST, so
/// that rule reduces, term for term, to the expression it replaced. The test below asserts the reduction across
/// every combination rather than trusting the reading.</para>
///
/// <para>🔴 <b>A NAMED NARROWING (ruling 9).</b> <see cref="MasterGstDetails"/> carries four fields - HSN/SAC,
/// taxability, rate and supply type - and none of Compensation-Cess, reverse charge or §17(5) ITC eligibility.
/// So a rate resolved at the Accounting Group, Stock Group or Company rung bears NO cess and never fires reverse
/// charge, whatever the reference product does. Adding those fields is a schema change and therefore an
/// escalation, not a design decision. The corpus's own GST Classification screen carries all three
/// (BOOK PDF p.234, printed 230), so this is a known narrowing, not corpus silence.</para>
/// </summary>
public sealed class GstWinningBlockTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly VoucherDate = new(2024, 6, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static Company GstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Winning Block Co", FyStart);
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

    // ================================================================= the reduction, asserted exhaustively

    /// <summary>
    /// THE THREADING LOCK, NOW STEERED BY THE SOURCE ORDER. Across every combination of {item block present or
    /// absent} x {ledger block present or absent} x {a group/company rung populated or not} x {both source orders},
    /// the block the cess and reverse-charge lookups consume is EXACTLY the block on the first rung of the
    /// PUBLISHED ORDER STRING that declares one - computed here by <see cref="GstRateHierarchy.DetailBlockWinner"/>
    /// from the vendor strings, never read off the resolver.
    ///
    /// <para>🔴 <b>SLICE S2b MOVED THIS, AND THE MOVE IS THE RULING, NOT A REGRESSION.</b> Through S2a this test
    /// asserted the reduction to the pre-S2a expression <c>item?.Gst ?? ledger?.SalesPurchaseGst</c>, which held
    /// because S2a's single walk put both DETAILED rungs above all three NARROW ones and the Stock Item above the
    /// Ledger. Under <see cref="GstDetailSource.LedgerFirst"/> — the shipped default, and what every company created
    /// on v51+ carries — the Ledger now outranks the Stock Item, so a line whose item and sales ledger BOTH declare
    /// a block takes its cess and its reverse-charge category from the LEDGER. That is one walk and one winning
    /// block, which is the whole point: without it a line could be RATED off the ledger while its cess was read off
    /// the item.</para>
    ///
    /// <para><b>The reduction to the old expression survives exactly where it should</b> — under
    /// <see cref="GstDetailSource.StockItemFirst"/> (the value every pre-v51 book is back-filled to) with no narrow
    /// rung populated (which is every book outside canonical import). That is asserted separately below rather than
    /// argued, because it is the no-re-rate claim for migrated books.</para>
    ///
    /// <para>The item's block deliberately carries NO RATE in the populated case. That is the shape that separates
    /// the two candidate rules: the RATE walk falls THROUGH a taxable, rate-less block to the next rung, so a
    /// resolver that fed cess "whichever rung supplied the rate" would read a different master. The rule is "first
    /// rung DECLARING a block", not "the rung that supplied the rate".</para>
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void The_detail_block_is_the_first_declaring_rung_of_the_published_order(
        bool itemHasBlock, bool ledgerHasBlock, bool narrowRungsPopulated)
    {
        foreach (var source in new[] { GstDetailSource.LedgerFirst, GstDetailSource.StockItemFirst })
            AssertDetailBlock(itemHasBlock, ledgerHasBlock, narrowRungsPopulated, source);
    }

    /// <summary>
    /// 🔴 THE NO-RE-RATE HALF, STATED ON ITS OWN. On a MIGRATED book (back-filled to
    /// <see cref="GstDetailSource.StockItemFirst"/>) that carries no Accounting-Group, Stock-Group or Company block
    /// — i.e. every book this application can build outside canonical import — the winning detail block is still
    /// <c>item?.Gst ?? ledger?.SalesPurchaseGst</c>, term for term, exactly as it was before the hierarchy existed.
    /// So no posted book's cess or reverse-charge category changes master because of slice S2b.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void On_a_migrated_book_the_detail_block_is_still_the_preS2a_item_then_ledger_pick(
        bool itemHasBlock, bool ledgerHasBlock)
    {
        var (item, ledger, gst) = BuildProbe(itemHasBlock, ledgerHasBlock,
            narrowRungsPopulated: false, GstDetailSource.StockItemFirst);
        Assert.Same(item.Gst ?? ledger.SalesPurchaseGst, gst.ResolveDetailBlock(item, ledger));
    }

    private static void AssertDetailBlock(
        bool itemHasBlock, bool ledgerHasBlock, bool narrowRungsPopulated, GstDetailSource source)
    {
        var (item, ledger, gst) = BuildProbe(itemHasBlock, ledgerHasBlock, narrowRungsPopulated, source);

        var code = (itemHasBlock ? "I" : "") + (ledgerHasBlock ? "L" : "") + (narrowRungsPopulated ? "GSC" : "");
        var expected = GstRateHierarchy.DetailBlockWinner(source, code) switch
        {
            GstRateHierarchy.Level.StockItem => item.Gst,
            GstRateHierarchy.Level.Ledger => ledger.SalesPurchaseGst,
            _ => null,
        };

        Assert.Same(expected, gst.ResolveDetailBlock(item, ledger));
    }

    private static (StockItem Item, Domain.Ledger Ledger, GstService Gst) BuildProbe(
        bool itemHasBlock, bool ledgerHasBlock, bool narrowRungsPopulated, GstDetailSource source)
    {
        var c = GstCompany();
        c.Gst!.SourceOfGstRate = source;
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var groups = new GroupService(c);

        var stockGroup = inv.CreateStockGroup("Probe SG");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Probe Widget", stockGroup.Id, nos.Id);

        var accountingGroup = groups.CreateGroup("Probe Sales Group", c.FindGroupByName("Sales Accounts")!.Id);
        var ledger = new Domain.Ledger(Guid.NewGuid(), "Probe Sales", accountingGroup.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        if (itemHasBlock)
            item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = null };
        if (ledgerHasBlock)
            ledger.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        if (narrowRungsPopulated)
        {
            stockGroup.Gst = new MasterGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 2800 };
            accountingGroup.Gst = new MasterGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1200 };
            c.Gst!.DefaultGst = new MasterGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 300 };
        }

        return (item, ledger, gst);
    }

    /// <summary>
    /// 🔴 THE SAME SHAPE, ASSERTED ON THE MONEY TO THE PAISA, AND IT IS WHERE THE RULING IS VISIBLE AS A FIGURE.
    /// An item block that is taxable but carries NO RATE, plus a sales-ledger block that supplies 1800 bp, plus an
    /// ad-valorem cess of 1200 bp declared on the ITEM. The rate is 1800 either way — under
    /// <see cref="GstDetailSource.StockItemFirst"/> because the rate walk falls THROUGH the rate-less item block to
    /// the ledger, under <see cref="GstDetailSource.LedgerFirst"/> because the ledger is simply first. <b>The CESS
    /// is what moves:</b>
    /// <list type="bullet">
    ///   <item><b><c>StockItemFirst</c> (every pre-v51 book) — 1,200.00.</b> The item declares the first block on
    ///     the walk, so it supplies the cess: 1200 bp of 10,000.00. This is the pre-S2a figure, unchanged.</item>
    ///   <item><b><c>LedgerFirst</c> (every v51+ book) — NO CESS.</b> The ledger declares the first block on the
    ///     walk, and its block carries no cess fields, so the line bears none. One walk, one winning block.</item>
    /// </list>
    ///
    /// <para>Both figures are DERIVED from the published order strings and the user ruling, not read off the
    /// resolver: 10,000.00 x 1200/10000 = 1,200.00 exactly, and a block that declares no cess charges none.</para>
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.StockItemFirst, true)]
    [InlineData(GstDetailSource.LedgerFirst, false)]
    public void The_source_order_decides_which_master_supplies_the_cess(
        GstDetailSource source, bool itemSuppliesTheCess)
    {
        var c = GstCompany();
        c.Gst!.SourceOfGstRate = source;
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
        ledger.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        c.AddLedger(ledger);

        Assert.Equal(1800, gst.ResolveRate(item, ledger, VoucherDate).RateBasisPoints);

        var cess = gst.ResolveCess(item, ledger, VoucherDate, quantity: 1m);
        if (itemSuppliesTheCess)
        {
            Assert.NotNull(cess);
            Assert.Equal(CessValuationMode.AdValorem, cess!.Value.Mode);
            Assert.Equal(new Money(1_200.00m), cess.Value.ComputeCess(new Money(10_000.00m)));
        }
        else
        {
            Assert.Null(cess);
        }
    }

    // ================================================================= the named narrowing

    /// <summary>
    /// 🔴 THE NARROWING, PINNED. A rate resolved at the STOCK GROUP rung bears NO Compensation-Cess even when the
    /// company carries a dated cess row for that HSN, because <see cref="MasterGstDetails"/> has no cess fields and
    /// the narrow block is not a detailed one. The rate itself resolves (2800 bp), so this is a deliberate
    /// narrowing rather than a rung that does nothing.
    ///
    /// <para>Written as a test rather than as a comment because it is the shape of a silent under-collection: a
    /// book that types its rate once at a Stock Group gets the rate and does NOT get the cess. When the escalated
    /// schema change lands (cess fields on the narrow block) this test is the one that must be deleted
    /// deliberately.</para>
    /// </summary>
    [Fact]
    public void A_rate_resolved_at_a_narrow_rung_bears_no_cess_even_on_a_cess_bearing_HSN()
    {
        const string CessHsn = "22021010";
        var c = GstCompany();
        var gst = new GstService(c);
        var inv = new InventoryService(c);

        c.Gst!.AddCessRate(new GstCessRate(
            Guid.NewGuid(), CessHsn, CessValuationMode.AdValorem, cessRateBasisPoints: 1200,
            cessPerUnit: Money.Zero, cessRspFactorMillis: 0,
            effectiveFrom: FyStart, effectiveTo: null, label: "Aerated waters 12%"));

        var stockGroup = inv.CreateStockGroup("Aerated Waters");
        stockGroup.Gst = new MasterGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 2800,
            HsnSac = CessHsn,
        };

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Cola 300ml", stockGroup.Id, nos.Id);

        Assert.Equal(2800, gst.ResolveRate(item, salesPurchaseLedger: null, VoucherDate).RateBasisPoints);
        Assert.Null(gst.ResolveCess(item, salesPurchaseLedger: null, VoucherDate, quantity: 1m));
        Assert.Null(gst.ResolveDetailBlock(item, salesPurchaseLedger: null));
    }

    /// <summary>
    /// The reverse-charge limb of the same narrowing: a supply whose rate resolves at a narrow rung never fires
    /// reverse charge, because <c>ReverseChargeApplicable</c> lives only on the detailed block. Asserted through
    /// <c>RcmService</c> itself, which now consumes the resolver's detail block instead of re-picking a level.
    /// </summary>
    [Fact]
    public void A_supply_rated_at_a_narrow_rung_never_fires_reverse_charge()
    {
        var c = GstCompany();
        var gst = new GstService(c);
        var rcm = new RcmService(c);
        var inv = new InventoryService(c);

        var stockGroup = inv.CreateStockGroup("Freight");
        stockGroup.Gst = new MasterGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };

        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Inward Carriage", stockGroup.Id, nos.Id);

        var posting = rcm.BuildReverseCharge(
            new Money(10_000.00m), item, spLedger: null,
            supplier: new PartyGstDetails { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27" },
            supplyDate: VoucherDate, supplyKind: RcmService.SupplyKind.Domestic);

        Assert.False(posting.Applies);
        Assert.Empty(posting.Lines);
    }
}
