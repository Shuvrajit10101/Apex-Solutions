using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The GST rate-hierarchy ORACLE (T0-4 slice S1). The five-level resolution order is expressed as DATA
/// transcribed from the two published order strings, and every expected winner in this file is COMPUTED from
/// that data - never read off <c>GstService</c>.
///
/// <para><b>R7 grounding - VENDOR-attested, NOT corpus-attested (ruling 9).</b> Both order strings and the
/// stop-at-first-hit rule come from help.tallysolutions.com, "HSN/SAC and GST Rate Hierarchy in TallyPrime".
/// The corpus is SILENT: zero hits for a GST "hierarch*" across all ten PDFs in both <c>-layout</c> and
/// <c>-raw</c> extraction. Anything in this file that is OURS rather than attested is labelled as such at the
/// member that carries it.</para>
///
/// <para><b>Why this exists before the resolver does.</b> A green suite has meant nothing on this project eight
/// times running, and the single documented failure mode is a golden edited to agree with the code it is meant
/// to police. The order strings below are the primary source, typed once; the winner for every fixture is
/// derived from them by <see cref="GstRateHierarchy.OracleWinner"/>. There is no literal winner to doctor.</para>
/// </summary>
public sealed class GstRateHierarchyOracleTests
{
    // ================================================================= the oracle is well-formed

    /// <summary>
    /// Both published orders name the same five levels, each exactly once - so neither string can have been
    /// mis-transcribed into a four-level or a duplicated walk without this failing.
    /// </summary>
    [Fact]
    public void TheTwoPublishedOrdersArePermutationsOfTheSameFiveLevels()
    {
        var ledgerFirst = GstRateHierarchy.VendorOrder(GstDetailSource.LedgerFirst);
        var itemFirst = GstRateHierarchy.VendorOrder(GstDetailSource.StockItemFirst);

        Assert.Equal(5, ledgerFirst.Count);
        Assert.Equal(5, itemFirst.Count);
        Assert.Equal(5, ledgerFirst.Distinct().Count());
        Assert.Equal(5, itemFirst.Distinct().Count());
        Assert.Equal(ledgerFirst.OrderBy(l => l).ToArray(), itemFirst.OrderBy(l => l).ToArray());

        // ...and they are genuinely DIFFERENT orders, or the whole GstDetailSource column is decoration.
        Assert.NotEqual(ledgerFirst.ToArray(), itemFirst.ToArray());
    }

    /// <summary>
    /// COMPANY IS LAST IN BOTH PUBLISHED ORDERS - vendor-attested, and load-bearing for S2a: it is the reason
    /// the ER-5 unresolved sentinel must move BEHIND the company level rather than firing (as it does today)
    /// after two rungs. A book that set its rate exactly where the reference product says a single-rate business
    /// should set it is hard-blocked from posting until that move lands.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void CompanyIsTheLastRungInBothPublishedOrders(GstDetailSource source) =>
        Assert.Equal(GstRateHierarchy.Level.Company, GstRateHierarchy.VendorOrder(source)[^1]);

    /// <summary>
    /// The five probe rates are pairwise distinct. This is the anti-mis-slot guard borrowed from
    /// <c>GstHierarchyIoTests</c>: if two levels shared a rate, a walk that consulted the wrong rung would still
    /// produce the expected number and every conformance row below would pass vacuously.
    /// </summary>
    [Fact]
    public void TheFiveProbeRatesArePairwiseDistinct()
    {
        var rates = GstRateHierarchy.AllLevels.Select(GstRateHierarchy.RateAt).ToList();
        Assert.Equal(rates.Count, rates.Distinct().Count());
    }

    /// <summary>
    /// STOP AT THE FIRST LEVEL THAT CARRIES THE DETAIL - vendor-attested ("TallyPrime first checks the ledger
    /// for the details. If not found there, it will move to the Group, then Stock Item, and so on"). Asserted
    /// two ways for every fixture: the winner IS the earliest populated rung, and no LATER populated rung's rate
    /// is ever the answer. The rates being pairwise distinct is what gives the second half teeth.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFixtureAndOrder))]
    public void TheWalkStopsAtTheFirstRungThatCarriesTheDetail(string populated, GstDetailSource source)
    {
        var order = GstRateHierarchy.VendorOrder(source);
        var carried = GstRateHierarchy.Decode(populated);
        var winner = GstRateHierarchy.OracleWinner(source, populated);

        var firstIndex = order.Select((lvl, i) => (lvl, i)).Where(t => carried.Contains(t.lvl))
            .Select(t => (int?)t.i).FirstOrDefault();

        if (firstIndex is null)
        {
            Assert.Null(winner);
            return;
        }

        Assert.Equal(GstRateHierarchy.RateAt(order[firstIndex.Value]), winner);

        // Nothing behind the first hit may supply the answer.
        for (var i = firstIndex.Value + 1; i < order.Count; i++)
            if (carried.Contains(order[i]))
                Assert.NotEqual(GstRateHierarchy.RateAt(order[i]), winner);
    }

    // ================================================================= the engine, measured against the oracle

    /// <summary>
    /// 🔴 THE T0-4 CONFORMANCE ASSERTION, AND THE WHOLE REASON THE ORACLE WAS BUILT FIRST. For every fixture, under
    /// each source order, the live engine returns exactly the rate the PUBLISHED ORDER STRING FOR THAT SOURCE names.
    /// The expectation is computed by <see cref="GstRateHierarchy.OracleWinner"/> from the two vendor strings; there
    /// is no literal winner anywhere in this file to edit into agreement with the resolver.
    ///
    /// <para><b>This member used to be called <c>EngineMatchesTheShippedContractWalk</c> and it asserted the
    /// opposite thing</b> - that the engine matched a THIRD, transcribed "what we actually ship" string, because
    /// through slices S1 and S2a it implemented neither published order. Slice S2b honours
    /// <c>GstConfig.SourceOfGstRate</c>, so the shipped contract IS the published contract and the third string is
    /// gone (see the deletion note in <see cref="GstRateHierarchy"/>).</para>
    ///
    /// <para><b>R7 + R12.</b> VENDOR-attested [web], help.tallysolutions.com "HSN/SAC &amp; GST Rate Hierarchy in
    /// TallyPrime": default <c>Ledger → Accounting Group → Stock Item → Stock Group → Company</c>, alternative
    /// <c>Stock Item → Stock Group → Ledger → Accounting Group → Company</c>. USER RULING (this session, R12): "on
    /// books created from v51 onward the SALES/PURCHASE LEDGER OUTRANKS THE STOCK ITEM - honour the LedgerFirst
    /// order the column already defaults to."</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFixtureAndOrder))]
    public void EngineMatchesThePublishedOrderForItsSource(string populated, GstDetailSource source)
    {
        var (gst, item, ledger) = GstRateHierarchy.BuildFixture(populated, source);
        Assert.Equal(GstRateHierarchy.OracleWinner(source, populated), GstRateHierarchy.EngineRate(gst, item, ledger));
    }

    /// <summary>
    /// THE T0-4 DIVERGENCE LEDGER, NOW EMPTY - the whole-matrix form of the conformance assertion above, kept as the
    /// ratchet that stops the list ever growing again.
    ///
    /// <para>Walks all 28 (fixture x source order) combinations, runs the LIVE ENGINE on each, and asserts that the
    /// set of rows on which it disagrees with the published order string for that source is EXACTLY the rows
    /// declared below. Where the theory above fails one row at a time with a clean expected/actual pair, this one
    /// fails with the entire divergent matrix printed, which is what makes a systematic regression (a re-ordering,
    /// a fourth walk written by hand beside the data) legible in a single failure message.</para>
    ///
    /// <para><b>THE HISTORY IS THE EVIDENCE, so it is recorded rather than deleted with the rows.</b> S1 declared
    /// FOURTEEN, hand-derived from the two order strings BEFORE the engine was run; the first run reproduced them
    /// row for row. S2a closed NINE by appending the Accounting Group, Stock Group and Company rungs and moving the
    /// ER-5 sentinel behind the Company rung - every row whose defect was "this application cannot see that rung at
    /// all". S2b closes the last FIVE, which were the two things S2a was forbidden to touch: the
    /// Stock-Item-vs-Ledger inversion (LGISC, GISC and LI under <c>LedgerFirst</c>) and the Stock Group's published
    /// position ABOVE the Ledger in the alternative string (LGSC and GS under <c>StockItemFirst</c>).</para>
    /// </summary>
    [Fact]
    public void TheEngineDivergesFromThePublishedOrdersNowhere()
    {
        // 🔴 EMPTY, AND EARNED. S1 declared FOURTEEN rows here, hand-derived from the two order strings before the
        // engine was ever run; S2a closed nine by appending the three rungs this application could not see; S2b
        // closes the last five by honouring GstConfig.SourceOfGstRate. Each of the five was wrong money on a live
        // invoice - "LI/LedgerFirst: ships 1800, published 500" is a stock item at 18% overriding a sales ledger at
        // 5% on a book whose persisted order says the ledger wins.
        //
        // The list stays here rather than the test being deleted, because the ratchet still bites in the one
        // direction that is left: any NEW divergence - a sixth rung, a re-ordering, a regression, a fourth walk
        // hand-written beside this one - grows `actual` and fails with the offending row printed in full.
        string[] declared = Array.Empty<string>();

        var actual = new List<string>();
        foreach (var code in GstRateHierarchy.FixtureCodes)
            foreach (var source in new[] { GstDetailSource.LedgerFirst, GstDetailSource.StockItemFirst })
            {
                var (gst, item, ledger) = GstRateHierarchy.BuildFixture(code, source);
                var engine = GstRateHierarchy.EngineRate(gst, item, ledger);
                var published = GstRateHierarchy.OracleWinner(source, code);
                if (engine == published) continue;
                actual.Add($"{Label(code)}/{source}: engine {Render(engine)}, published {Render(published)}");
            }

        Assert.Equal(declared.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                     actual.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        static string Label(string code) => code.Length == 0 ? "(none)" : code;
        static string Render(int? bp) => bp is { } v ? v.ToString() : "unresolved";
    }

    /// <summary>
    /// THE NO-OP / EQUIVALENCE LOCK (T13), stated precisely enough to be true. Whenever the three rungs no UI can
    /// write - Accounting Group, Stock Group, Company - all carry nothing, AND at most one of {Stock Item, sales
    /// Ledger} carries a block, BOTH published orders and the live engine give the SAME answer. Under that
    /// precondition <c>LedgerFirst</c> reduces to Ledger-then-Item and <c>StockItemFirst</c> to Item-then-Ledger,
    /// and those agree exactly when at most one of the two is present.
    ///
    /// <para>Written as a PRECONDITION rather than as "both orders always agree", which is flatly false and would
    /// be a doctored lock: slice S2b's whole purpose is that the two orders now answer DIFFERENTLY on the five
    /// fixtures where the published strings differ (see
    /// <see cref="TheTwoSourceOrdersNowSteerTheEngineExactlyWhereThePublishedStringsDiffer"/>). This is the
    /// assertion that certifies the flip as a no-op on the LARGEST class of real books - one that never typed a
    /// rate on both a stock item and its sales/purchase ledger.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFixtureAndOrder))]
    public void WhereTheThreeNewRungsAreEmptyBothOrdersAndTheEngineAllAgree(string populated, GstDetailSource source)
    {
        var carried = GstRateHierarchy.Decode(populated);
        var reachesANewRung = carried.Contains(GstRateHierarchy.Level.AccountingGroup)
                           || carried.Contains(GstRateHierarchy.Level.StockGroup)
                           || carried.Contains(GstRateHierarchy.Level.Company);
        var bothOldRungs = carried.Contains(GstRateHierarchy.Level.StockItem)
                        && carried.Contains(GstRateHierarchy.Level.Ledger);
        if (reachesANewRung || bothOldRungs) return; // outside the precondition - says nothing, asserts nothing

        var (gst, item, ledger) = GstRateHierarchy.BuildFixture(populated, source);
        var expected = GstRateHierarchy.OracleWinner(source, populated);

        Assert.Equal(expected, GstRateHierarchy.PreS2aWinner(populated));
        Assert.Equal(expected, GstRateHierarchy.EngineRate(gst, item, ledger));
        Assert.Equal(expected, GstRateHierarchy.OracleWinner(
            source == GstDetailSource.LedgerFirst ? GstDetailSource.StockItemFirst : GstDetailSource.LedgerFirst,
            populated));
    }

    /// <summary>
    /// The equivalence lock above is only worth anything if its precondition admits fixtures - a precondition that
    /// excluded everything would make it pass vacuously, forever. Three of the fourteen fixtures satisfy it:
    /// nothing populated, the Ledger alone, the Stock Item alone.
    /// </summary>
    [Fact]
    public void TheEquivalencePreconditionIsNotVacuous()
    {
        var admitted = GstRateHierarchy.FixtureCodes
            .Where(code =>
            {
                var c = GstRateHierarchy.Decode(code);
                return !c.Contains(GstRateHierarchy.Level.AccountingGroup)
                    && !c.Contains(GstRateHierarchy.Level.StockGroup)
                    && !c.Contains(GstRateHierarchy.Level.Company)
                    && !(c.Contains(GstRateHierarchy.Level.StockItem) && c.Contains(GstRateHierarchy.Level.Ledger));
            })
            .ToArray();
        Assert.Equal(new[] { "", "L", "I" }, admitted);
    }

    // ================================================================= slice S2b: what moves, and what must not

    /// <summary>
    /// 🔴 THE FLIP, PINNED STRUCTURALLY - the half that cannot be doctored by a fixture, because it compares two
    /// published strings against a string that is frozen history and never runs the engine at all.
    ///
    /// <para>Restrict each published order to the two rungs this application could see before slice S2a and the
    /// answer is exact: <c>StockItemFirst</c> reduces to <c>Stock Item → Ledger</c>, which IS the frozen
    /// <see cref="GstRateHierarchy.PreS2aWalkOrder"/> - so the value every pre-v51 book is back-filled to preserves
    /// the order it has always resolved by. <c>LedgerFirst</c> reduces to its exact REVERSE, which is the one
    /// money-moving change in this design and is the user's ruling, stated here as a structural property rather
    /// than left to whichever line of the resolver happened to be written.</para>
    ///
    /// <para><b>R12, verbatim (this session):</b> "on books created from v51 onward the SALES/PURCHASE LEDGER
    /// OUTRANKS THE STOCK ITEM - honour the LedgerFirst order the column already defaults to, flipping today's
    /// item-first walk."</para>
    /// </summary>
    [Fact]
    public void TheMigratedOrderPreservesThePreS2aWalkAndTheShippedDefaultReversesIt()
    {
        var frozen = GstRateHierarchy.PreS2aWalk();
        Assert.Equal(new[] { GstRateHierarchy.Level.StockItem, GstRateHierarchy.Level.Ledger }, frozen.ToArray());

        var detailedOnly = (GstDetailSource s) => GstRateHierarchy.VendorOrder(s)
            .Where(l => l is GstRateHierarchy.Level.StockItem or GstRateHierarchy.Level.Ledger).ToArray();

        // The back-filled order still puts the Stock Item above the Ledger, exactly as this application always did.
        Assert.Equal(frozen.ToArray(), detailedOnly(GstDetailSource.StockItemFirst));

        // The shipped default reverses precisely those two rungs - and nothing else about the pair.
        Assert.Equal(frozen.Reverse().ToArray(), detailedOnly(GstDetailSource.LedgerFirst));
    }

    /// <summary>
    /// 🔴 A MIGRATED BOOK DOES NOT RE-RATE. <c>Schema.MigrateV50ToV51</c> back-fills every pre-existing company to
    /// <see cref="GstDetailSource.StockItemFirst"/>, and on any book outside canonical import <c>Group.Gst</c>,
    /// <c>StockGroup.Gst</c> and <c>GstConfig.DefaultGst</c> are all <c>null</c> (the only writer in <c>src/</c> is
    /// <c>ImportPlan</c>; there is no UI writer at all). Under both facts together the engine must answer exactly
    /// what the FROZEN pre-S2a two-rung walk answered - so slice S2b moves no figure on any book that predates v51.
    ///
    /// <para>Asserted against <see cref="GstRateHierarchy.PreS2aWalkOrder"/>, a string that is history and is never
    /// edited again, so "the walk changed and so did the expectation" is not an available way to pass. This test is
    /// the S2b half of what <c>TheThreeNewRungsCannotFireOnAnyExistingBook</c> asserted for S2a; it narrows to
    /// <c>StockItemFirst</c> because <c>LedgerFirst</c> is where the ruled flip lives, and that is
    /// <see cref="TheTwoSourceOrdersNowSteerTheEngineExactlyWhereThePublishedStringsDiffer"/>'s business.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void AMigratedBookStillResolvesExactlyAsItDidBeforeTheHierarchyExisted(string populated)
    {
        var carried = GstRateHierarchy.Decode(populated);
        if (carried.Contains(GstRateHierarchy.Level.AccountingGroup)
            || carried.Contains(GstRateHierarchy.Level.StockGroup)
            || carried.Contains(GstRateHierarchy.Level.Company))
            return; // not an existing-book shape - says nothing, asserts nothing

        var (gst, item, ledger) = GstRateHierarchy.BuildFixture(populated, GstDetailSource.StockItemFirst);
        Assert.Equal(GstRateHierarchy.PreS2aWinner(populated), GstRateHierarchy.EngineRate(gst, item, ledger));
    }

    /// <summary>The no-op precondition above admits the four existing-book shapes - nothing populated, the Ledger
    /// alone, the Stock Item alone, and both together - so it cannot pass vacuously.</summary>
    [Fact]
    public void TheExistingBookPreconditionIsNotVacuous()
    {
        var admitted = GstRateHierarchy.FixtureCodes
            .Where(code =>
            {
                var c = GstRateHierarchy.Decode(code);
                return !c.Contains(GstRateHierarchy.Level.AccountingGroup)
                    && !c.Contains(GstRateHierarchy.Level.StockGroup)
                    && !c.Contains(GstRateHierarchy.Level.Company);
            })
            .ToArray();
        Assert.Equal(new[] { "", "L", "I", "LI" }, admitted);
    }

    /// <summary>
    /// 🔴 THE COLUMN NOW STEERS THE ENGINE - and it steers it on EXACTLY the fixtures where the two published
    /// strings disagree, no more and no fewer. Through S1 and S2a the predecessor of this test asserted the
    /// opposite property (that the one shipped walk answered the same under both values, so the persisted column
    /// was decoration); its doc said in terms that "S2b cannot land without deleting this test". It is replaced,
    /// not deleted, by the assertion that inverts it.
    ///
    /// <para>Both halves matter. Where the strings AGREE the two orders must still agree in the engine - otherwise
    /// the flip has leaked into books it was never ruled over. Where they DISAGREE each must return its own
    /// published rate - otherwise the column is still decoration, or worse, half-honoured.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void TheTwoSourceOrdersNowSteerTheEngineExactlyWhereThePublishedStringsDiffer(string populated)
    {
        var ledgerFirstPublished = GstRateHierarchy.OracleWinner(GstDetailSource.LedgerFirst, populated);
        var itemFirstPublished = GstRateHierarchy.OracleWinner(GstDetailSource.StockItemFirst, populated);

        var (lfGst, lfItem, lfLedger) = GstRateHierarchy.BuildFixture(populated, GstDetailSource.LedgerFirst);
        var (ifGst, ifItem, ifLedger) = GstRateHierarchy.BuildFixture(populated, GstDetailSource.StockItemFirst);

        var ledgerFirstEngine = GstRateHierarchy.EngineRate(lfGst, lfItem, lfLedger);
        var itemFirstEngine = GstRateHierarchy.EngineRate(ifGst, ifItem, ifLedger);

        Assert.Equal(ledgerFirstPublished, ledgerFirstEngine);
        Assert.Equal(itemFirstPublished, itemFirstEngine);

        if (ledgerFirstPublished == itemFirstPublished)
            Assert.Equal(ledgerFirstEngine, itemFirstEngine);
        else
            Assert.NotEqual(ledgerFirstEngine, itemFirstEngine);
    }

    /// <summary>
    /// 🔴 THE FIVE FIXTURES ON WHICH THE SOURCE ORDER CHANGES THE TAX - hand-derived from the two order strings and
    /// declared here as literals, so the theory above cannot become vacuous by a change that made the two orders
    /// agree everywhere (which would silently un-do the ruling and leave 28 rows passing).
    ///
    /// <para>DERIVED, not observed. <c>LedgerFirst</c> = L·G·I·S·C, <c>StockItemFirst</c> = I·S·L·G·C, probe rates
    /// L=500 · G=1200 · I=1800 · S=2800 · C=300. Walking the fourteen fixtures on paper: LGISC gives 500 vs 1800;
    /// GISC 1200 vs 1800; LGSC 500 vs 2800; LI 500 vs 1800; GS 1200 vs 2800. Every other fixture reaches the same
    /// rung from either end. These five are precisely the five divergence rows S2b was chartered to close.</para>
    /// </summary>
    [Fact]
    public void TheSourceOrderChangesTheAnswerOnExactlyFiveOfTheFourteenFixtures()
    {
        var steered = GstRateHierarchy.FixtureCodes
            .Where(code => GstRateHierarchy.OracleWinner(GstDetailSource.LedgerFirst, code)
                        != GstRateHierarchy.OracleWinner(GstDetailSource.StockItemFirst, code))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "GISC", "GS", "LGISC", "LGSC", "LI" }, steered);
    }

    /// <summary>
    /// THE ER-5 SENTINEL MOVED BEHIND THE COMPANY RUNG - it did not disappear. Company is last in BOTH published
    /// order strings, so a book that set its rate exactly where the reference product tells a single-rate business
    /// to set it must now POST (300 bp, not a hard block); a book that set a rate at no level at all must still
    /// fail fast, because a silent zero on a taxable line is the defect the sentinel exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void TheSentinelSitsBehindTheCompanyRungAndNotInFrontOfIt(GstDetailSource source)
    {
        var (withCompany, cItem, cLedger) = GstRateHierarchy.BuildFixture("C", source);
        var resolved = withCompany.ResolveRate(cItem, cLedger, voucherDate: null);
        Assert.False(GstService.IsUnresolved(resolved));
        Assert.True(resolved.IsTaxable);
        Assert.Equal(300, resolved.RateBasisPoints);

        var (empty, eItem, eLedger) = GstRateHierarchy.BuildFixture("", source);
        Assert.True(GstService.IsUnresolved(empty.ResolveRate(eItem, eLedger, voucherDate: null)));
    }

    // ================================================================= fixture matrix

    /// <summary>
    /// Every fixture code crossed with both source orders - 14 x 2 = 28 cases. A code is the set of rungs that
    /// carry a taxable block: L = sales/purchase Ledger, G = the ledger's Accounting Group, I = Stock Item,
    /// S = the item's Stock Group, C = the company's <c>DefaultGst</c>. The 14 codes peel the FRONT of each
    /// published string, and cover every rung alone, so no rung is reachable only through another.
    /// </summary>
    public static TheoryData<string, GstDetailSource> EveryFixtureAndOrder()
    {
        var data = new TheoryData<string, GstDetailSource>();
        foreach (var code in GstRateHierarchy.FixtureCodes)
            foreach (var source in new[] { GstDetailSource.LedgerFirst, GstDetailSource.StockItemFirst })
                data.Add(code, source);
        return data;
    }

    /// <summary>Every fixture code once, for the properties that compare the two source orders against each other.</summary>
    public static TheoryData<string> EveryFixture()
    {
        var data = new TheoryData<string>();
        foreach (var code in GstRateHierarchy.FixtureCodes) data.Add(code);
        return data;
    }
}
