using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// THE GST RATE-HIERARCHY ORACLE (T0-4 slice S1) - shared by <see cref="GstRateHierarchyOracleTests"/> and by
/// <c>GstTests</c>'s precedence theory so the two can never drift apart.
///
/// <para><b>The two order strings below are the primary source, transcribed VERBATIM and typed exactly once.</b>
/// Every expected winner anywhere in the suite is COMPUTED from them by <see cref="OracleWinner"/>. Nothing here
/// is read off <c>GstService</c>, and there is no hand-typed winner beside the code it is meant to police - which
/// is the one failure mode this project has actually suffered.</para>
///
/// <para><b>R7 grounding.</b> The two strings and the stop-at-first-hit rule are VENDOR-attested only
/// (help.tallysolutions.com, "HSN/SAC and GST Rate Hierarchy in TallyPrime"); the corpus is silent - zero hits
/// for a GST "hierarch*" across all ten PDFs in both <c>-layout</c> and <c>-raw</c>. Labelled under ruling 9.</para>
/// </summary>
internal static class GstRateHierarchy
{
    // ---------------------------------------------------------------- the sources of truth, typed once

    /// <summary>
    /// VENDOR, VERBATIM - the shipped default, selected by <see cref="GstDetailSource.LedgerFirst"/> and given to
    /// every company created on schema v51 or later. "The Ledger hierarchy is the default option."
    /// </summary>
    public const string VendorDefaultOrder = "Ledger -> Accounting Group -> Stock Item -> Stock Group -> Company";

    /// <summary>
    /// VENDOR, VERBATIM - the selectable alternative, <see cref="GstDetailSource.StockItemFirst"/>, and the value
    /// the v50-to-v51 migration back-fills onto every pre-existing book because it is what this application
    /// resolved before the hierarchy columns existed.
    /// </summary>
    public const string VendorAlternativeOrder = "Stock Item -> Stock Group -> Ledger -> Accounting Group -> Company";

    /// <summary>
    /// THE WALK THIS APPLICATION SHIPPED BEFORE SLICE S2a - transcribed from <c>GstService.ResolveBase</c>'s own
    /// pre-S2a summary, "the Phase-4/8 rate resolution (item -> ledger -> unresolved), unchanged". A TWO-rung walk:
    /// the Accounting Group, the Stock Group and the Company were not in it at all, which is why a book that set
    /// its rate at any of those three resolved to the ER-5 unresolved sentinel.
    ///
    /// <para><b>FROZEN. This string is history and must never be edited again.</b> It is the baseline half of the
    /// S2a no-op proof (<c>TheThreeNewRungsCannotFireOnAnyExistingBook</c>): on every book that was not built by
    /// canonical import the three new rungs are null, so the engine must still answer exactly what THIS walk says.
    /// Editing it would delete that proof while leaving the suite green.</para>
    /// </summary>
    public const string PreS2aWalkOrder = "Stock Item -> Ledger";

    // 🔴 SLICE S2b DELETED `ShippedContractOrder`, AND THE DELETION IS THE POINT.
    //
    // S1 and S2a needed a third string because the engine implemented NEITHER published order: it walked its own
    // walk, and the gap between "what ships" and "what the vendor publishes" had to stay COMPUTED rather than
    // typed. S2b closes that gap - `GstService.Hierarchy` now walks the list named by
    // `GstConfig.SourceOfGstRate`, so the shipped contract IS `VendorDefaultOrder` under
    // `GstDetailSource.LedgerFirst` and `VendorAlternativeOrder` under `GstDetailSource.StockItemFirst`.
    //
    // Re-introducing a separate "what we ship" string here would now be VACUOUS at best (a copy of a vendor
    // string compared against itself) and a LICENCE at worst - a second place to record a drift instead of
    // fixing it. Every expectation in the suite is therefore taken from the two vendor strings directly, and
    // `GstRateHierarchyOracleTests.EngineMatchesThePublishedOrderForItsSource` is the conformance assertion that
    // used to be a divergence ledger.

    /// <summary>A rung of the hierarchy. Declaration order is alphabetical-by-accident and carries NO meaning -
    /// take the order from <see cref="VendorOrder"/> and from nowhere else.</summary>
    public enum Level { Ledger, AccountingGroup, StockItem, StockGroup, Company }

    /// <summary>The five rungs, in no significant order.</summary>
    public static readonly IReadOnlyList<Level> AllLevels = Enum.GetValues<Level>();

    private static readonly IReadOnlyDictionary<string, Level> LevelByPublishedName =
        new Dictionary<string, Level>(StringComparer.Ordinal)
        {
            ["Ledger"] = Level.Ledger,
            ["Accounting Group"] = Level.AccountingGroup,
            ["Stock Item"] = Level.StockItem,
            ["Stock Group"] = Level.StockGroup,
            ["Company"] = Level.Company,
        };

    /// <summary>Splits a published order string into its rungs. The PARSER is the only thing that turns prose
    /// into behaviour, so a typo in either string surfaces here as a hard failure, not as a silent wrong walk.</summary>
    public static IReadOnlyList<Level> Parse(string order) =>
        order.Split("->", StringSplitOptions.RemoveEmptyEntries)
             .Select(s => LevelByPublishedName[s.Trim()])
             .ToList();

    /// <summary>The published walk for a source order.</summary>
    public static IReadOnlyList<Level> VendorOrder(GstDetailSource source) => source switch
    {
        GstDetailSource.LedgerFirst => Parse(VendorDefaultOrder),
        GstDetailSource.StockItemFirst => Parse(VendorAlternativeOrder),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown GstDetailSource"),
    };

    /// <summary>The walk this application shipped BEFORE slice S2a, from the same parser.</summary>
    public static IReadOnlyList<Level> PreS2aWalk() => Parse(PreS2aWalkOrder);

    // ---------------------------------------------------------------- probe rates, pairwise distinct

    /// <summary>
    /// One distinguishable integrated rate per rung (basis points). Pairwise distinct on purpose: a walk that
    /// consulted the WRONG rung would otherwise still return the expected number and every conformance row would
    /// pass vacuously. Pinned by <c>TheFiveProbeRatesArePairwiseDistinct</c>.
    /// </summary>
    public static int RateAt(Level level) => level switch
    {
        Level.Ledger => 500,
        Level.AccountingGroup => 1200,
        Level.StockItem => 1800,
        Level.StockGroup => 2800,
        Level.Company => 300,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "unknown level"),
    };

    private static char CodeOf(Level level) => level switch
    {
        Level.Ledger => 'L',
        Level.AccountingGroup => 'G',
        Level.StockItem => 'I',
        Level.StockGroup => 'S',
        Level.Company => 'C',
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "unknown level"),
    };

    /// <summary>Decodes a fixture code ("LGISC", "GS", "") into the set of rungs that carry a taxable block.</summary>
    public static IReadOnlySet<Level> Decode(string populated)
    {
        var set = new HashSet<Level>();
        foreach (var ch in populated)
        {
            var hit = AllLevels.FirstOrDefault(l => CodeOf(l) == ch, (Level)(-1));
            if ((int)hit < 0) throw new ArgumentException($"unknown fixture rung '{ch}' in \"{populated}\"", nameof(populated));
            set.Add(hit);
        }
        return set;
    }

    /// <summary>
    /// The 14 fixtures. The first six peel the FRONT of the published default string
    /// (L-G-I-S-C, then G-I-S-C, then I-S-C, then S-C, then C, then nothing); the next two peel the front of the
    /// alternative (I removed, then I and S removed); the next five put each rung on the stage ALONE, so no rung
    /// is reachable only through another; and the last two are the minimal pairs that separate the two orders.
    /// </summary>
    public static readonly IReadOnlyList<string> FixtureCodes = new[]
    {
        "LGISC", "GISC", "ISC", "SC", "C", "",
        "LGSC", "LGC",
        "L", "G", "I", "S",
        "LI", "GS",
    };

    // ---------------------------------------------------------------- the two winners, both COMPUTED

    private static int? WinnerOf(IReadOnlyList<Level> walk, IReadOnlySet<Level> carried)
    {
        foreach (var level in walk)
            if (carried.Contains(level))
                return RateAt(level);
        return null; // no rung on the walk carries a block => the ER-5 unresolved sentinel
    }

    /// <summary>The rate the PUBLISHED order string says must win, in basis points; <c>null</c> = unresolved.</summary>
    public static int? OracleWinner(GstDetailSource source, string populated) =>
        WinnerOf(VendorOrder(source), Decode(populated));

    /// <summary>
    /// The DETAILED master whose block wins under <paramref name="source"/> for a fixture - <see cref="Level.StockItem"/>,
    /// <see cref="Level.Ledger"/>, or <c>null</c> when the first rung declaring anything is one of the three NARROW
    /// rungs (which carry no cess and no reverse-charge fields) or when nothing declares at all.
    ///
    /// <para>This is the ONE-WALK-ONE-WINNING-BLOCK rule (<c>GstService.ResolveDetailBlock</c>) expressed against the
    /// published order string rather than against the code, so <c>GstWinningBlockTests</c> can assert the cess and
    /// reverse-charge source without typing a winner beside the resolver it polices.</para>
    /// </summary>
    public static Level? DetailBlockWinner(GstDetailSource source, string populated)
    {
        var carried = Decode(populated);
        foreach (var level in VendorOrder(source))
            if (carried.Contains(level))
                return level is Level.StockItem or Level.Ledger ? level : null;
        return null;
    }

    /// <summary>The rate this application resolved BEFORE slice S2a, from the frozen two-rung contract;
    /// <c>null</c> = the ER-5 unresolved sentinel. The baseline half of the S2a no-op proof.</summary>
    public static int? PreS2aWinner(string populated) => WinnerOf(PreS2aWalk(), Decode(populated));

    // ---------------------------------------------------------------- the fixture

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    /// <summary>
    /// Builds a company in which exactly the rungs named by <paramref name="populated"/> carry a taxable block at
    /// their probe rate, and stamps <paramref name="source"/> onto <c>GstConfig.SourceOfGstRate</c>.
    ///
    /// <para>The Stock Item and the sales Ledger ALWAYS EXIST - only their GST blocks are conditional. That is
    /// the shape a real stock line has, and it is what keeps the Stock Group and Accounting Group rungs reachable
    /// (they hang off <c>StockItem.StockGroupId</c> and <c>Ledger.GroupId</c>). Both group blocks sit on the
    /// item's/ledger's DIRECT parent, so these fixtures test ORDER only; ancestry is S2's own test.</para>
    /// </summary>
    public static (GstService Gst, StockItem Item, Domain.Ledger Ledger) BuildFixture(
        string populated, GstDetailSource source)
    {
        var carried = Decode(populated);

        var c = CompanyFactory.CreateSeeded("Hierarchy Probe Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.Gst!.SourceOfGstRate = source;
        c.Gst!.SourceOfHsnSacDetails = source;

        // --- Stock Group -> Stock Item
        var inv = new InventoryService(c);
        var stockGroup = inv.CreateStockGroup("Probe Stock Group");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Probe Widget", stockGroup.Id, nos.Id);

        // --- Accounting Group -> sales Ledger
        var groups = new GroupService(c);
        var accountingGroup = groups.CreateGroup("Probe Sales Group", c.FindGroupByName("Sales Accounts")!.Id);
        var ledger = new Domain.Ledger(Guid.NewGuid(), "Probe Sales", accountingGroup.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        if (carried.Contains(Level.StockItem))
            item.Gst = TaxableItemBlock(RateAt(Level.StockItem));
        if (carried.Contains(Level.Ledger))
            ledger.SalesPurchaseGst = TaxableItemBlock(RateAt(Level.Ledger));
        if (carried.Contains(Level.StockGroup))
            stockGroup.Gst = TaxableMasterBlock(RateAt(Level.StockGroup));
        if (carried.Contains(Level.AccountingGroup))
            accountingGroup.Gst = TaxableMasterBlock(RateAt(Level.AccountingGroup));
        if (carried.Contains(Level.Company))
            c.Gst!.DefaultGst = TaxableMasterBlock(RateAt(Level.Company));

        return (gst, item, ledger);
    }

    private static StockItemGstDetails TaxableItemBlock(int bp) =>
        new() { Taxability = GstTaxability.Taxable, RateBasisPoints = bp };

    private static MasterGstDetails TaxableMasterBlock(int bp) =>
        new() { Taxability = GstTaxability.Taxable, RateBasisPoints = bp };

    /// <summary>
    /// The engine's answer for a fixture, normalised to the same shape the two winners use: basis points, or
    /// <c>null</c> for the ER-5 unresolved sentinel. Every fixture block is <c>Taxable</c>, so a NON-taxable,
    /// non-sentinel answer would mean the engine invented a taxability and is surfaced as a distinct failure
    /// rather than silently folded into "unresolved".
    /// </summary>
    public static int? EngineRate(GstService gst, StockItem? item, Domain.Ledger? ledger)
    {
        var r = gst.ResolveRate(item, ledger);
        if (GstService.IsUnresolved(r)) return null;
        if (!r.IsTaxable)
            throw new InvalidOperationException(
                $"the engine answered a non-taxable {r.Taxability} for a fixture in which every block is Taxable");
        return r.RateBasisPoints;
    }
}
