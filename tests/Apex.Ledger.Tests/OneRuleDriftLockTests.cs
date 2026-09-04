using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Drift locks</b> for the "one rule, one home" slice. Each unified rule now has exactly one implementation;
/// these tests scan the shipped source tree and FAIL if a second copy reappears anywhere outside that rule's
/// designated home file.
///
/// <para><b>Why a source scan rather than a behavioural assertion.</b> A behavioural test pins what the ONE
/// home does; it cannot notice that someone has quietly written a second, slightly different copy beside a new
/// call site — which is exactly how all eight of these divergences arose. Only reading the source catches
/// re-duplication.</para>
///
/// <para><b>Non-vacuity is asserted, not assumed.</b> Every pattern below is a named constant, and
/// <see cref="EveryLockBitesOnAReintroducedCopy"/> runs those SAME constants over reconstructed copies of the
/// idioms this slice removed — including variable-renamed and line-split variants. A lock that stopped matching
/// its own rule would fail there, so "the scan found nothing" cannot silently mean "the pattern matches nothing".
/// <see cref="TheScanActuallyReadsTheShippedTree"/> separately guards against an empty scan.</para>
///
/// <para><b>Honest limits.</b> These locks match on the textual idiom of each rule, so a copy that restructures
/// the expression entirely can still slip past. They are a ratchet against the ordinary copy-paste that actually
/// happened here, not a proof of uniqueness.</para>
/// </summary>
public sealed class OneRuleDriftLockTests
{
    // ============================================================ the patterns, as shared constants
    // Each is used BOTH by the tree scan and by the bite proof, so the two can never drift apart.

    /// <summary>D1 — dividing by a group total is the apportionment idiom.</summary>
    private const string D1Apportionment = @"/\s*totalValue\b";

    /// <summary>D2 — only the shared formatter may build an Indian-grouping culture.</summary>
    private const string D2GroupingCulture = @"NumberGroupSizes";

    /// <summary>D2 — money formatted against the invariant culture (flat group size 3 = Western grouping).</summary>
    private const string D2InvariantMoney =
        @"""#,##0\.00"",\s*(System\.Globalization\.)?CultureInfo\.InvariantCulture";

    /// <summary>D2 — quantities likewise.</summary>
    private const string D2InvariantQuantity =
        @"""#,##0\.######"",\s*(System\.Globalization\.)?CultureInfo\.InvariantCulture";

    /// <summary>
    /// D2 — an INTERPOLATED money format specifier. <c>$"₹{x.Amount:#,##0}"</c> binds to
    /// <c>CurrentCulture</c> by construction: not the shared rule, not even the invariant culture, so neither of
    /// the two locks above sees it. TDS/TCS thresholds are lakh-scale by law (§194C ₹1,00,000; §206C in lakhs),
    /// so these sites sat above the grouping boundary and rendered "₹100,000" on an ordinary en-US host — and
    /// "₹100.000" on a de-DE one, which reads as a decimal — while the tax invoice for the same company rendered
    /// "₹1,00,000". Route money through <c>IndianFormat</c> / <c>IndianMoneyFormat</c> instead.
    /// </summary>
    private const string D2InterpolatedMoney = @"\{[A-Za-z_][A-Za-z0-9_.?]*(\.Amount)?:#,##0";

    /// <summary>
    /// D3 — rupees→paisa on ONE line. Two idioms: a <c>(long)</c> conversion of <c>× 100m</c>, and the
    /// assignment form <c>var scaled = rupees * 100m;</c> that splits the conversion across lines. The second
    /// alternative is what catches <c>MoneyCodec.ToPaisa</c> and <c>Paisa.FromDecimal</c> — the two
    /// EXACT-semantics boundary copies that motivated D3 and that the <c>(long)</c> idiom alone misses entirely.
    /// Percent→basis-point sites use the same factor but cast to <c>int</c> and call through <c>Math.Round(</c>,
    /// so neither alternative matches them.
    /// </summary>
    private const string D3RupeesToPaisa = @"\(long\)[^;]*\*\s*100m|=\s*[A-Za-z_.]+\s*\*\s*100m";

    /// <summary>D3 — the sub-paisa predicate, single-line idiom.</summary>
    private const string D3SubPaisaTest =
        @"decimal\.Truncate\([^)]*\*\s*100m|!=\s*decimal\.Truncate\(scaled\)";

    /// <summary>D3 file-level, half one — scaling to paisa.</summary>
    private const string D3PaisaScale = @"\*\s*100m";

    /// <summary>D3 file-level, half two — a truncation test, with ANY argument name.</summary>
    private const string D3TruncateAnyArgument = @"decimal\.Truncate\(";

    /// <summary>D7 — the HSN/SAC resolution order.</summary>
    private const string D7HsnResolution = @"\?\.HsnSacCode";

    /// <summary>
    /// D8 — the intra/inter ROUTING rule: a NEGATED string comparison against the company's home State (or against a
    /// State code, under whatever local name). Three copies of it existed —
    /// <c>GstService.IsInterState</c> (throw on a null home), <c>EWayBillService.IsInterState</c> (return FALSE on a
    /// null home, then spend that false on an intra-state exemption and a per-State threshold) and
    /// <c>VoucherPrintProjector.ConsistentBuyerStateCode</c> (<c>Trim()</c> + <c>OrdinalIgnoreCase</c>, against the
    /// engine's untrimmed <c>Ordinal</c>) — and no two of them agreed on the same inputs.
    ///
    /// <para><b>Deliberately NOT matched: the non-negated forms.</b> <c>string.Equals(t.StateCode, pos, …)</c> (the
    /// e-Way threshold-row lookup), <c>string.Equals(record.ShipFromStateCode, record.ShipToStateCode, …)</c> (the
    /// Part-B ≤50 km relaxation) and <c>ConsistentBuyerGstin</c>'s "did the State get overridden?" test are equality
    /// questions about two codes, not derivations of a supply's routing. Requiring the negation is what separates
    /// them — "is this supply INTER-state" is by construction a NOT-equal.</para>
    /// </summary>
    private const string D8Routing = @"!\s*string\.Equals\([^;]*(?:[Hh]ome|[Ss]tateCode|[Ss]tate\b)";

    /// <summary>
    /// D9 (T0-4 slice S1; <b>CLOSED by T0-17</b>) — a GST rate read STRAIGHT OFF A MASTER'S GST BLOCK, bypassing
    /// <c>GstService.ResolveRate</c> entirely. Five such readers used to ship, each hard-coding its own single rung
    /// of a five-rung hierarchy: three read only the Stock Item, two read only the sales/purchase Ledger. They agreed
    /// with the resolver ONLY because the resolver was itself item-then-ledger; T0-4 S2b's <c>LedgerFirst</c> default
    /// and Phase 9's HSN-dated rate windows each broke that coincidence. All five now delegate to
    /// <c>GstReportSupport.BucketingRateOf</c>, so <b>this idiom must appear nowhere in the shipped tree at all</b> —
    /// the strongest form the lock can take, and the one that catches a sixth without anyone editing an expected
    /// count. Whitespace-tolerant so a re-spaced or line-split copy cannot slip past.
    /// </summary>
    private const string D9MasterRateBypass = @"IsTaxable:\s*true\s*,\s*RateBasisPoints:";

    /// <summary>
    /// D9b (T0-17) — the <b>widened</b> master-rate-read pattern, added because D9's property-pattern shape was
    /// narrower than the defect. D9 matched only <c>… is { IsTaxable: true, RateBasisPoints: … }</c>; the very same
    /// bypass written as a null-conditional chain (<c>ledger?.SalesPurchaseGst?.RateBasisPoints ?? 1800</c>) or via
    /// an intermediate local (<c>item?.Gst is { } g &amp;&amp; g.IsTaxable &amp;&amp; g.RateBasisPoints is { } r</c>)
    /// slipped straight past it — the second of those was even pinned as a deliberate NON-match by D9's own
    /// false-positive guard, i.e. the hole was documented rather than closed.
    ///
    /// <para>It anchors on a MASTER-block accessor (<c>SalesPurchaseGst</c>, <c>DefaultGst</c>, or <c>.Gst</c>
    /// followed by <c>?</c>/<c>.</c>/whitespace) reaching a <c>RateBasisPoints</c> on the same statement. That
    /// deliberately does NOT match <c>GstService.ResolveBase</c>/<c>Hierarchy</c>, which read <c>RateBasisPoints</c>
    /// off a <c>Rung</c>/<c>block</c> local — the resolver must never trip its own lock.</para>
    ///
    /// <para><b>Pinned as an exact inventory rather than "nowhere", because three legitimate readers remain</b> and
    /// each is a different reason. See <see cref="TheWidenedMasterRateReadInventoryIsExactlyTheFourKnownOnes"/>.</para>
    /// </summary>
    private const string D9bMasterRateRead = @"(?:SalesPurchaseGst|DefaultGst|\.Gst[?.\s])[^;]*RateBasisPoints";

    /// <summary>
    /// D10 (T0-4 slice S1) — a call to <c>GstService.ResolveRate</c>. Unlike D1–D9 this is not a "second copy"
    /// pattern: <c>ResolveRate</c> IS the one home, and the risk is the opposite one — a NEW live re-resolve
    /// appearing beside a report or a payload, where it would re-rate an already-issued document off today's
    /// masters instead of off the posted legs. Pinned as an exact INVENTORY, so an addition is a deliberate act.
    /// </summary>
    private const string D10ResolveRateCallSite = @"\.ResolveRate\(";

    // ============================================================ scanning machinery

    /// <summary>The repository root — the directory holding <c>Apex.slnx</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every shipped C# source file (the <c>src/</c> tree), excluding build output.</summary>
    private static IEnumerable<string> ShippedSources()
    {
        var src = Path.Combine(RepoRoot(), "src");
        Assert.True(Directory.Exists(src), src);
        return Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts <paramref name="pattern"/> occurs in no shipped source file except those named in
    /// <paramref name="homeFiles"/>. Reports every offender with file and line so a failure is actionable.
    /// </summary>
    private static void AssertOnlyIn(string rule, string pattern, params string[] homeFiles)
    {
        var rx = new Regex(pattern, RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var path in ShippedSources())
        {
            var name = Path.GetFileName(path);
            if (homeFiles.Contains(name, StringComparer.Ordinal)) continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                if (rx.IsMatch(lines[i]))
                    offenders.Add($"  {Path.GetRelativePath(RepoRoot(), path)}:{i + 1}  {lines[i].Trim()}");
        }

        if (offenders.Count > 0)
            Assert.Fail(
                $"{rule}: a second copy of this rule has appeared outside its home ({string.Join(", ", homeFiles)}).\n" +
                $"Pattern: {pattern}\n" + string.Join("\n", offenders) +
                "\nDelegate to the shared rule instead of re-implementing it.");
    }

    /// <summary>
    /// Asserts no shipped file outside <paramref name="homeFiles"/> contains BOTH patterns anywhere in it.
    /// A rule whose steps are spread over several lines — <c>var scaled = x * 100m;</c> then
    /// <c>if (scaled != decimal.Truncate(scaled))</c> — cannot be caught by any single-line regex, and renaming
    /// the local defeats a name-specific one. Co-occurrence within a file is name-agnostic and line-agnostic.
    /// </summary>
    private static void AssertNoFileHasBoth(
        string rule, string patternA, string patternB, params string[] homeFiles)
    {
        var rxA = new Regex(patternA, RegexOptions.Compiled);
        var rxB = new Regex(patternB, RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var path in ShippedSources())
        {
            if (homeFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal)) continue;

            var text = File.ReadAllText(path);
            if (rxA.IsMatch(text) && rxB.IsMatch(text))
                offenders.Add($"  {Path.GetRelativePath(RepoRoot(), path)}");
        }

        if (offenders.Count > 0)
            Assert.Fail(
                $"{rule}: a file outside the home ({string.Join(", ", homeFiles)}) both scales to paisa and " +
                $"tests for a sub-paisa tail — i.e. it re-implements the rule.\n" +
                $"Patterns: {patternA}  AND  {patternB}\n" + string.Join("\n", offenders) +
                "\nDelegate to PaisaConversion instead of re-implementing it.");
    }

    /// <summary>
    /// Asserts that <paramref name="pattern"/> occurs in the shipped tree at EXACTLY the repo-relative paths and
    /// counts given in <paramref name="expected"/> — no more, no fewer, nowhere else. Stronger than
    /// <see cref="AssertOnlyIn"/>, which pins only WHERE a rule may live: an inventory also pins HOW MANY, so a
    /// sixth copy added inside a file that already holds one is caught too.
    ///
    /// <para><b>Line numbers are deliberately NOT part of the inventory</b> — they churn on every unrelated edit and
    /// a lock that has to be re-numbered constantly is a lock that gets deleted. Comment lines are skipped so a
    /// doc-comment mentioning the idiom never counts as a call site.</para>
    /// </summary>
    private static void AssertExactInventory(
        string rule, string pattern, IReadOnlyDictionary<string, int> expected, string remedy)
    {
        var rx = new Regex(pattern, RegexOptions.Compiled);
        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in ShippedSources())
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (!rx.IsMatch(line)) continue;
                var rel = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
                actual[rel] = actual.TryGetValue(rel, out var n) ? n + 1 : 1;
            }

        var expectedSorted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in expected) expectedSorted[kv.Key] = kv.Value;

        // Non-vacuity: an inventory that expects nothing, or that finds nothing, protects nothing.
        Assert.NotEmpty(expectedSorted);
        Assert.NotEmpty(actual);

        if (!expectedSorted.SequenceEqual(actual))
            Assert.Fail(
                $"{rule}: the shipped inventory of this idiom has changed.\nPattern: {pattern}\n" +
                $"EXPECTED:\n{Render(expectedSorted)}\nACTUAL:\n{Render(actual)}\n{remedy}");

        static string Render(SortedDictionary<string, int> d) =>
            d.Count == 0 ? "  (none)" : string.Join("\n", d.Select(kv => $"  {kv.Key}  x{kv.Value}"));
    }

    // ============================================================ D1

    /// <summary>Pro-rata apportionment lives only in <c>ProRata.cs</c>.</summary>
    [Fact]
    public void ApportionmentHasOneHome() =>
        AssertOnlyIn("D1 pro-rata apportionment", D1Apportionment, "ProRata.cs");

    // ============================================================ D2

    /// <summary>Only the shared formatter may construct an Indian-grouping culture.</summary>
    [Fact]
    public void IndianGroupingCultureHasOneHome() =>
        AssertOnlyIn("D2 Indian digit grouping", D2GroupingCulture, "IndianMoneyFormat.cs");

    /// <summary>
    /// No shipped file may format rupee money against the invariant culture. Its group size is a flat 3, which
    /// is the Western grouping — the defect that printed ₹1,00,000 as "100,000.00" on the tax invoice, the POS
    /// receipt and the printed voucher while the same assembly printed "1,00,000.00" on a certificate.
    /// </summary>
    [Fact]
    public void MoneyIsNeverFormattedAgainstTheInvariantCulture() =>
        AssertOnlyIn("D2 Indian digit grouping (money)", D2InvariantMoney);

    /// <summary>Quantities share the grouping rule; they may not be invariant-grouped either.</summary>
    [Fact]
    public void QuantitiesAreNeverFormattedAgainstTheInvariantCulture() =>
        AssertOnlyIn("D2 Indian digit grouping (quantity)", D2InvariantQuantity);

    /// <summary>
    /// Nor may money be formatted through an interpolated specifier, which binds to the HOST's culture and so is
    /// neither Indian-grouped nor even deterministic. See <see cref="D2InterpolatedMoney"/>.
    /// </summary>
    [Fact]
    public void MoneyIsNeverFormattedThroughAnInterpolatedSpecifier() =>
        AssertOnlyIn("D2 Indian digit grouping (interpolated)", D2InterpolatedMoney);

    // ============================================================ D3

    /// <summary>Rupees→paisa lives only in <c>PaisaConversion.cs</c>. See <see cref="D3RupeesToPaisa"/>.</summary>
    [Fact]
    public void RupeesToPaisaHasOneHome() =>
        AssertOnlyIn("D3 rupees→paisa", D3RupeesToPaisa, "PaisaConversion.cs");

    /// <summary>The sub-paisa test is part of the same rule and has the same home.</summary>
    [Fact]
    public void SubPaisaTestHasOneHome() =>
        AssertOnlyIn("D3 sub-paisa test", D3SubPaisaTest, "PaisaConversion.cs");

    /// <summary>
    /// The line- and name-agnostic form of the D3 lock. The single-line patterns above miss the idiom that
    /// splits the conversion over three statements, and the name-specific half of the sub-paisa pattern is
    /// defeated by renaming one local. Co-occurrence of "scales by 100" and "tests for a truncated tail" in one
    /// file catches both. Other scales in the tree — millis (×1,000), forex and quantity micros (×1,000,000) —
    /// are genuinely different rules and are correctly excluded: they never multiply by <c>100m</c>.
    /// </summary>
    [Fact]
    public void PaisaScalingAndTheSubPaisaTestNeverCoexistOutsideTheOneHome() =>
        AssertNoFileHasBoth(
            "D3 rupees→paisa (multi-line idiom)", D3PaisaScale, D3TruncateAnyArgument, "PaisaConversion.cs");

    // ============================================================ D7

    /// <summary>
    /// The HSN/SAC resolution ORDER (GST block over the legacy Phase-3 field) lives only in
    /// <c>GstReportSupport.HsnSacOf</c>. Consumers keep their own sentinel for the absent case — that difference
    /// is deliberate and documented, and is pinned behaviourally in <c>AbsentHsnSentinelsPerConsumerTests</c> —
    /// but none of them may re-derive the order.
    /// </summary>
    [Fact]
    public void HsnResolutionHasOneHome() =>
        AssertOnlyIn("D7 HSN/SAC resolution", D7HsnResolution, "GstReportSupport.cs");

    // ============================================================ D8

    /// <summary>
    /// The intra/inter routing rule lives only in <c>GstReportSupport.RoutingOf</c> (W0-15). Consumers keep their own
    /// answer for the UNROUTEABLE case — <c>GstService.IsInterState</c> throws, the print path carries the
    /// <c>null</c> through to the DTO, the e-Way engine refuses to spend it on a relaxation — and that difference is
    /// deliberate and documented, exactly as D7's absent-HSN sentinels are. What none of them may do is re-derive
    /// "same State or not" for itself, which is how all three copies came to disagree about a null home State, about
    /// a whitespace-padded code and about case.
    ///
    /// <para><b>Honest limit, stated because this file's convention demands it (see the class note): the exemption is
    /// by BARE FILENAME.</b> <see cref="AssertOnlyIn"/> compares <c>Path.GetFileName</c>, so a NEW file called
    /// <c>GstReportSupport.cs</c> created anywhere under <c>src/</c> — a different project, a different namespace —
    /// would be exempted from this lock (and from D7) without anything failing. The lock is a ratchet against
    /// ordinary copy-paste beside a new call site, which is what actually happened three times here; it is not a
    /// proof of uniqueness. A copy that restructures the expression away from <c>string.Equals</c> entirely (an
    /// <c>==</c>/<c>!=</c> on two codes, a <c>Compare(…) != 0</c>, an extension method) also slips past.</para>
    /// </summary>
    [Fact]
    public void IntraInterRoutingHasOneHome() =>
        AssertOnlyIn("D8 intra/inter routing", D8Routing, "GstReportSupport.cs");

    // ============================================================ D9 / D10 — the GST rate hierarchy (T0-4 S1)

    /// <summary>
    /// D9 — the FIVE master-block rate readers that bypass <c>GstService.ResolveRate</c>, pinned as an exact
    /// inventory so slice S2 cannot move one without moving the others.
    ///
    /// <para><b>S1 changes none of them.</b> This lock records them AS THEY ARE, which is the whole point: they are
    /// the "several places" half of a one-rule-several-places defect that has not bitten yet only because the
    /// resolver happens to walk the same two rungs they do. Each returns <c>0</c> on an unresolvable master where
    /// <c>ResolveRate</c> returns the ER-5 sentinel, and each is hard-wired to ONE rung:</para>
    /// <list type="bullet">
    ///   <item><c>Gstr1.LineIntegratedRate</c> — Stock Item only.</item>
    ///   <item><c>Gstr1.LedgerIntegratedRate</c> — sales/purchase Ledger only.</item>
    ///   <item><c>EInvoiceJson.LineIntegratedRate</c> — Stock Item only.</item>
    ///   <item><c>EInvoiceJson.ServiceLegsByRate</c> — sales/purchase Ledger only.</item>
    ///   <item><c>EWayBillJson.LineIntegratedRate</c> — Stock Item only.</item>
    /// </list>
    ///
    /// <para>🔴 <b>Two of these five were NOT in the T0-4 design's list of four</b> — <c>EInvoiceJson</c>'s and
    /// <c>EWayBillJson</c>'s item-only <c>LineIntegratedRate</c>. The design's fourth entry
    /// (<c>GstReportSupport</c>) is not a bypass at all: it calls <c>ResolveRate</c> properly, and is pinned by D10
    /// below instead. Counting them was the point of writing the lock rather than trusting the list.</para>
    ///
    /// <para>All five are BUCKETING readers — they choose which posted rate group a line belongs to and never
    /// compute tax — so making them hierarchy-aware is a decision S2 must take explicitly, per bypass, and record.
    /// This lock exists so that decision cannot be taken by omission.</para>
    ///
    /// <para>🔴 <b>T0-17 TOOK THAT DECISION, and it went the other way from the wording above.</b> All five were
    /// routed through <c>GstReportSupport.BucketingRateOf</c> — none was found to legitimately need a hard-wired
    /// rung. The reasoning, per site, is one argument: each answers "which posted rate group is this line in?", the
    /// posting engine answered that with <c>ResolveRate</c>, so any other answer mis-buckets. The "restate what was
    /// posted, not what masters say today" concern does NOT argue for keeping them: the raw master read was itself a
    /// read of today's masters, and a blinder one — it could not see the dated rate window at all. The genuinely
    /// posted-rate fix is a persisted per-line rate, which is a schema change and stays a carry-forward.</para>
    ///
    /// <para><b>So the inventory is now ZERO and the assertion is "nowhere", not a count.</b> That is deliberate:
    /// <c>AssertExactInventory</c> refuses an empty expectation ("an inventory that expects nothing protects
    /// nothing"), and a count of zero would in any case be weaker than a prohibition — a sixth bypass must fail this
    /// lock without anyone having to notice a number moved.</para>
    /// </summary>
    [Fact]
    public void NoMasterBlockRateBypassSurvivesAnywhereInTheShippedTree() =>
        AssertOnlyIn("D9 master-block rate bypass", D9MasterRateBypass);

    /// <summary>
    /// D9b — the widened pattern's exact inventory. THREE sites remain, and <b>not one of them is a bucketing read</b>:
    /// <list type="bullet">
    ///   <item><c>VoucherAlterationDerivedLegs</c> ×2 — <c>l.Gst</c> there is an <c>EntryLine</c>'s
    ///     <c>GstLineTax</c>, i.e. a <b>POSTED</b> leg, not a master. Reading the rate a leg actually carries is the
    ///     opposite of a bypass; these are matched only because the property is spelled the same way.</item>
    ///   <item><c>GstReportSupport.TaxedLegsCarryTheirTax</c> — a <b>predicate</b>, not a rate: "does this ledger
    ///     DECLARE a non-zero rate?", used to refuse a self-contradicting tax invoice. It deliberately reads the
    ///     DECLARATION and must not resolve — its own doc records why ("a live resolve is exactly what the projector
    ///     refuses to do with money"). It produces no rate and spends none.</item>
    /// </list>
    ///
    /// <para>🔴 <b>WAS FOUR — <c>RcmService</c> IS GONE, AND THE COUNT COMING DOWN IS THE POINT.</b> T0-17 recorded a
    /// genuine SIXTH bypass here that D9's narrower pattern never counted:
    /// <c>supplyGst?.RateBasisPoints ?? spLedger?.SalesPurchaseGst?.RateBasisPoints ?? 1800</c>, rating an
    /// import-of-services RCM leg off a rung the walk did not choose. T0-17 listed it rather than fixing it, because
    /// unlike the five it COMPUTES tax and its <c>?? 1800</c> floor was an unsourced statutory claim — closing it
    /// needed an R7 verification and belonged to <b>T0-18</b>. <b>T0-18 then closed it:</b> the limb calls
    /// <c>_gst.ResolveRate(item, spLedger, supplyDate)</c> and the floor was DELETED rather than re-sourced, so the
    /// line the entry pointed at no longer exists and the inventory is three.
    ///
    /// <para>🔴 <b>This entry is written out rather than deleted because the two changes landed from SEPARATE
    /// parallel tracks and git merged this file with no conflict at all.</b> Taking the merged text as it stood
    /// would have left the lock expecting a bypass in a file that no longer has one — a lock that fails for the
    /// right reason only by accident. The count was re-derived by re-running D9b's own pattern over the merged
    /// tree, not by trusting either side.</para></para>
    /// </summary>
    [Fact]
    public void TheWidenedMasterRateReadInventoryIsExactlyTheThreeKnownOnes() =>
        AssertExactInventory(
            "D9b widened master-rate read", D9bMasterRateRead,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["src/Apex.Desktop/ViewModels/VoucherAlterationDerivedLegs.cs"] = 2,
                ["src/Apex.Ledger/Reports/GstReportSupport.cs"] = 1,
            },
            "A new read of a master's GST block reaching RateBasisPoints is a rate resolved OUTSIDE "
          + "GstService.ResolveRate. Route it through GstReportSupport.BucketingRateOf (bucketing) or "
          + "GstService.ResolveRate (computing), or — if it reads a POSTED leg or tests a DECLARATION rather than "
          + "producing a rate — add it here deliberately and say in its doc which of those it is.");

    /// <summary>
    /// D10 — <c>GstService.ResolveRate</c> has exactly these ten call sites. A live re-resolve added beside a
    /// report, a payload or a print projector would re-rate an already-issued document off TODAY's masters rather
    /// than off its posted legs — the failure this project has already paid for once, and the reason the print
    /// money block was moved wholly onto posted legs (W0-10).
    ///
    /// <para>The one report-side call site is deliberate and documented: <c>GstReportSupport</c>'s
    /// <c>IsWhollyExemptItemSupply</c> re-resolves every stock line live to choose TAX INVOICE vs BILL OF SUPPLY.
    /// It is the single place where master drift is genuinely visible on issued paper, and S2 widens what it can
    /// see. Pinning it here is what makes that exposure countable instead of incidental.</para>
    ///
    /// <para>🔴 <b>TEN, and the count arrived in TWO independent steps that both had to be kept.</b> Two parallel
    /// tracks each raised this lock from eight to nine, for different reasons and on different files, and a merge
    /// that took either side alone would have silently under-counted by one. Both reasons stand:</para>
    /// <list type="bullet">
    ///   <item>🔴 <b>T0-17 added the NINTH: <c>GstReportSupport.BucketingRateOf</c>.</b> It is a report/payload-side
    ///     call, which is the shape this lock is most suspicious of — so the reason is recorded here as well as at
    ///     the method. It does not re-rate issued paper: every rupee still comes from the posted <c>GstLineTax</c>
    ///     legs, and the resolved rate only chooses WHICH posted group a line is counted in. It replaced five reads
    ///     that were already consulting live masters and could not even see the dated rate window, so it strictly
    ///     REDUCES the live-master surface rather than widening it.</item>
    ///   <item>🔴 <b>T0-18 added the TENTH: <c>RcmService</c>'s second call.</b> The long-standing domestic-goods
    ///     call is now joined by one on the IMPORT-OF-SERVICES limb, which previously carried a hand-written
    ///     two-rung <c>item ?? ledger</c> pick with a hard-coded <c>1800</c> floor and no supply date. Both are
    ///     POSTING paths re-resolving a rate for a voucher being entered, which is what this lock permits; the
    ///     count is raised rather than the lock loosened.</item>
    /// </list>
    ///
    /// <para>🔴 <b>Historical note, kept because the count is what surfaced it:</b> the T0-4 design's survey named
    /// six call sites; there were EIGHT. The two extra were <c>PosBillingViewModel</c>'s second call and
    /// <c>VoucherEntryViewModel</c>'s ledger-only call. Both <c>PosBillingViewModel</c> sites then used the
    /// DATE-BLIND two-argument overload, so the dated <c>RateHistory</c> override never fired at the POS while
    /// every voucher path passed <c>Date</c> — that was <b>T0-19</b>, now fixed: both pass <c>Date</c> and the
    /// two-argument overload is deleted outright.</para>
    /// </summary>
    [Fact]
    public void ResolveRateHasExactlyTheTenKnownCallSites() =>
        AssertExactInventory(
            "D10 ResolveRate call sites", D10ResolveRateCallSite,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["src/Apex.Desktop/ViewModels/PosBillingViewModel.cs"] = 2,
                ["src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs"] = 4,
                ["src/Apex.Ledger/Reports/GstReportSupport.cs"] = 2,
                ["src/Apex.Ledger/Services/RcmService.cs"] = 2,
            },
            "A new ResolveRate call site re-resolves a rate from LIVE masters. On a posting path that is correct; "
          + "on a report, a payload or a print path it re-rates issued paper and must instead read the posted "
          + "GstLineTax legs (see GstReportSupport.IntegratedRateOf). Add it here only with that decision recorded.");

    // ============================================================ meta — the locks are not vacuous

    /// <summary>
    /// The locks are only meaningful if they are actually reading source. This guards against a silently empty
    /// scan — a wrong root, a moved tree — which would make every lock above pass vacuously.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsTheShippedTree()
    {
        var files = ShippedSources().ToList();
        Assert.True(files.Count > 100, $"expected the src/ tree, found only {files.Count} files");
        Assert.Contains(files, f => Path.GetFileName(f) == "ProRata.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "PaisaConversion.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "IndianMoneyFormat.cs");
    }

    /// <summary>
    /// <b>The bite proof.</b> A lock that matches nothing passes forever and protects nothing. Each case below is
    /// a faithful reconstruction of a copy this slice actually removed (or, where marked, a plausible RENAMED or
    /// LINE-SPLIT variant of one), run against the very same pattern constant the tree scan uses. If a pattern is
    /// ever weakened so it no longer recognises its own rule, this fails — which is the guarantee the tree scan
    /// itself can never give.
    /// </summary>
    [Theory]
    // D1 — the three removed Apportion copies.
    [InlineData(nameof(D1Apportionment), D1Apportionment, "Math.Round(total * value / totalValue, 2, MidpointRounding.AwayFromZero)")]
    [InlineData(nameof(D1Apportionment), D1Apportionment, "(long)Math.Round((decimal)total * value / totalValue, MidpointRounding.AwayFromZero)")]
    // D2 — a re-hand-rolled Indian culture, and the invariant-culture money/quantity formats.
    [InlineData(nameof(D2GroupingCulture), D2GroupingCulture, "ci.NumberFormat.NumberGroupSizes = new[] { 3, 2 };")]
    [InlineData(nameof(D2InvariantMoney), D2InvariantMoney, @"m.Amount.ToString(""#,##0.00"", CultureInfo.InvariantCulture)")]
    [InlineData(nameof(D2InvariantMoney), D2InvariantMoney, @"m.Amount.ToString(""#,##0.00"", System.Globalization.CultureInfo.InvariantCulture)")]
    [InlineData(nameof(D2InvariantQuantity), D2InvariantQuantity, @"q.ToString(""#,##0.######"", CultureInfo.InvariantCulture)")]
    // D2 — the interpolated CurrentCulture specifiers actually found in the shipped masters.
    [InlineData(nameof(D2InterpolatedMoney), D2InterpolatedMoney, @"parts.Add($""₹{s.Amount:#,##0} single"");")]
    [InlineData(nameof(D2InterpolatedMoney), D2InterpolatedMoney, @"Threshold = n.Threshold is { } t ? $""₹{t.Amount:#,##0}"" : ""—"",")]
    // D3 — the rounding copies (single-line (long) idiom).
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, "private static long ToPaisa(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);")]
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, "new((long)(ReconValueTolerance.Amount * 100m), ReconDateWindowDays);")]
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, @"s.Parameters.AddWithValue(""$c80"", (long)(declaration.Section80C.Amount * 100m));")]
    // D3 — the EXACT-semantics boundary copies, whose assignment form the (long) idiom alone never caught.
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, "decimal paisa = money.Amount * 100m;")]   // MoneyCodec.ToPaisa
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, "var scaled = rupees * 100m;")]            // Paisa.FromDecimal
    [InlineData(nameof(D3RupeesToPaisa), D3RupeesToPaisa, "var p = r * 100m;")]                      // renamed variant
    // D3 — the sub-paisa predicate, single-line forms.
    [InlineData(nameof(D3SubPaisaTest), D3SubPaisaTest, "public bool IsPaisaExact => Amount * 100m == decimal.Truncate(Amount * 100m);")]
    [InlineData(nameof(D3SubPaisaTest), D3SubPaisaTest, "if (scaled != decimal.Truncate(scaled)) return false;")]
    // D7 — re-deriving the resolution order.
    [InlineData(nameof(D7HsnResolution), D7HsnResolution, @"var hsn = item?.Gst?.HsnSac ?? item?.HsnSacCode ?? ""(none)"";")]
    // D8 — the three removed routing copies, verbatim, plus a renamed variant.
    [InlineData(nameof(D8Routing), D8Routing, @"return !string.Equals(home, partyStateCode, StringComparison.Ordinal);")]                                  // GstService.IsInterState
    [InlineData(nameof(D8Routing), D8Routing, @"return pos is not null && home is not null && !string.Equals(pos, home, StringComparison.Ordinal);")]      // EWayBillService.IsInterState
    [InlineData(nameof(D8Routing), D8Routing, @"!string.Equals(live.Trim(), home.Trim(), StringComparison.OrdinalIgnoreCase);")]                           // VoucherPrintProjector.ConsistentBuyerStateCode
    [InlineData(nameof(D8Routing), D8Routing, @"var isInter = !string.Equals(homeCode, party, StringComparison.Ordinal);")]                                // renamed variant
    [InlineData(nameof(D8Routing), D8Routing, @"bool inter = !string.Equals(supplierState, buyerStateCode, StringComparison.Ordinal);")]                   // renamed variant, neither operand called "home"
    // D9 — the five shipped master-block rate bypasses, verbatim, plus re-spaced and line-split variants.
    [InlineData(nameof(D9MasterRateBypass), D9MasterRateBypass, @"company.FindStockItem(il.StockItemId)?.Gst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0;")]
    [InlineData(nameof(D9MasterRateBypass), D9MasterRateBypass, @"ledger.SalesPurchaseGst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0;")]
    [InlineData(nameof(D9MasterRateBypass), D9MasterRateBypass, @"var rate = singleRate ?? (ledger.SalesPurchaseGst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0);")]
    [InlineData(nameof(D9MasterRateBypass), D9MasterRateBypass, @"group.Gst is {IsTaxable:true,RateBasisPoints: { } r} ? r : 0;")]                          // re-spaced variant
    [InlineData(nameof(D9MasterRateBypass), D9MasterRateBypass, @"stockGroup.Gst is { IsTaxable:  true ,  RateBasisPoints: { } bp } ? bp : 0")]             // padded variant
    // D10 — the shipped ResolveRate call sites, and a plausible new one beside a report.
    [InlineData(nameof(D10ResolveRateCallSite), D10ResolveRateCallSite, @"var res = _gst.ResolveRate(l.SelectedItem, valueLedger, Date);")]
    [InlineData(nameof(D10ResolveRateCallSite), D10ResolveRateCallSite, @"var res = gst.ResolveRate(company.FindStockItem(il.StockItemId), valueLedger, voucher.Date);")]
    [InlineData(nameof(D10ResolveRateCallSite), D10ResolveRateCallSite, @"var rate = new GstService(company).ResolveRate(item, ledger).RateBasisPoints;")]  // a new live re-resolve in a report
    public void EveryLockBitesOnAReintroducedCopy(string lockName, string pattern, string reintroducedLine) =>
        Assert.True(
            Regex.IsMatch(reintroducedLine, pattern),
            $"{lockName} no longer recognises its own rule — this line would slip past the tree scan:\n" +
            $"  {reintroducedLine}\nPattern: {pattern}");

    /// <summary>
    /// The file-level D3 lock's bite proof, which needs whole-file text rather than a single line. Each fixture
    /// is the body of a copy this slice removed; the last is a renamed variant that BOTH single-line D3 patterns
    /// miss, which is precisely why this lock exists.
    /// </summary>
    [Theory]
    // MoneyCodec.ToPaisa — the Io export boundary. Neither single-line D3 pattern matched this file at all.
    [InlineData("decimal paisa = money.Amount * 100m;\nif (paisa != decimal.Truncate(paisa))\n    throw new InvalidOperationException();\nreturn (long)paisa;")]
    // Paisa.FromDecimal — the SQLite persist boundary. Likewise invisible to both single-line patterns.
    [InlineData("var scaled = rupees * 100m;\nvar rounded = decimal.Truncate(scaled);\nif (scaled != rounded)\n    throw new InvalidOperationException();\nreturn (long)rounded;")]
    // A fifth copy with every local renamed — defeats the name-specific `Truncate(scaled)` alternative outright.
    [InlineData("var p = r * 100m;\nif (p != decimal.Truncate(p)) return false;\npaisa = (long)p;")]
    public void TheFileLevelPaisaLockBitesOnAReintroducedCopy(string reintroducedBody)
    {
        Assert.True(Regex.IsMatch(reintroducedBody, D3PaisaScale),
            $"D3PaisaScale no longer recognises paisa scaling:\n{reintroducedBody}");
        Assert.True(Regex.IsMatch(reintroducedBody, D3TruncateAnyArgument),
            $"D3TruncateAnyArgument no longer recognises the truncation test:\n{reintroducedBody}");
    }

    /// <summary>
    /// The file-level lock must NOT fire on the tree's other integer scales — interest millis (×1,000), forex and
    /// quantity micros (×1,000,000) and BOM percent millis. Those are genuinely different rules with their own
    /// precision contracts; folding them into the paisa rule would be a false positive that a future maintainer
    /// would have to silence, which is how locks get deleted.
    /// </summary>
    [Theory]
    [InlineData("var scaled = ratePercent * 1000m;\nvar truncated = decimal.Truncate(scaled);")]
    [InlineData("decimal micro = rate * MicroScale;\nif (micro != decimal.Truncate(micro))")]
    [InlineData("var scaled = quantity * Scale;\nreturn scaled == decimal.Truncate(scaled);")]
    public void TheFileLevelPaisaLockIgnoresOtherIntegerScales(string otherScaleBody) =>
        Assert.False(
            Regex.IsMatch(otherScaleBody, D3PaisaScale) && Regex.IsMatch(otherScaleBody, D3TruncateAnyArgument),
            $"the D3 file-level lock false-positives on a non-paisa scale:\n{otherScaleBody}");

    /// <summary>
    /// D8 must NOT fire on the shipped lines that legitimately compare two State codes without deriving a routing —
    /// the e-Way threshold-row lookup, the Part-B ≤50 km relaxation and <c>ConsistentBuyerGstin</c>'s "was the State
    /// overridden?" test — nor on any negated comparison that has nothing to do with States. Each case below is a
    /// VERBATIM line from <c>src/</c>. A lock that has to be silenced with an exemption is a lock that gets deleted,
    /// so its false-positive surface is pinned as deliberately as its bite.
    /// </summary>
    [Theory]
    [InlineData(@".FirstOrDefault(t => string.Equals(t.StateCode, pos, StringComparison.Ordinal) && t.TxnType == txnType);")]
    [InlineData(@"var intra = string.Equals(record.ShipFromStateCode, record.ShipToStateCode, StringComparison.Ordinal);")]
    [InlineData(@"if (string.Equals(live?.Trim(), stateCode?.Trim(), StringComparison.OrdinalIgnoreCase)) return gstin;")]
    [InlineData(@"if (string.Equals(s.StateCode, StateCode, StringComparison.Ordinal))")]
    [InlineData(@"if (!string.Equals(value ?? string.Empty, Name ?? string.Empty, StringComparison.Ordinal))")]
    [InlineData(@"if (!string.Equals(type.DefaultShortcut, superseded, StringComparison.Ordinal)) continue;")]
    public void TheRoutingLockIgnoresPlainStateEqualityAndNonStateComparisons(string shippedLine) =>
        Assert.False(
            Regex.IsMatch(shippedLine, D8Routing),
            $"D8 false-positives on a line that derives no routing — a lock that has to be exempted gets deleted:\n" +
            $"  {shippedLine}\nPattern: {D8Routing}");

    /// <summary>
    /// D9 must NOT fire on the shipped lines that mention taxability WITHOUT reading a rate off a master — the
    /// exempt-service-ledger discriminator and the ER-5 unresolved sentinel both test <c>IsTaxable: false</c>, and
    /// neither is a rate bypass. Both are VERBATIM lines from <c>src/</c> (<c>Gstr1.cs</c> and
    /// <c>GstService.IsUnresolved</c>). Its false-positive surface is pinned as deliberately as its bite, for the
    /// reason D8's twin test states: a lock that has to be silenced with an exemption is a lock that gets deleted.
    ///
    /// <para>🔴 <b>A third case used to sit here and has been MOVED, not deleted</b> — see
    /// <see cref="TheWidenedLockCatchesTheBypassShapesD9CouldNotSee"/>. It was the property-access form of the very
    /// rule D9 polices, pinned as a deliberate non-match on the ground that catching it "would fire on
    /// <c>ResolveBase</c> itself". That ground was sound for D9's shape and wrong as a conclusion: it recorded a
    /// live hole in the lock as if it were a design constraint. D9b closes it by anchoring on a MASTER accessor,
    /// which <c>ResolveBase</c> does not use, so the near-miss is caught and the resolver still is not.</para>
    /// </summary>
    [Theory]
    [InlineData(@"        ledger.SalesPurchaseGst is { IsTaxable: false };")]
    [InlineData(@"    public static bool IsUnresolved(RateResolution r) => r is { IsTaxable: false, RateBasisPoints: -1, Taxability: GstTaxability.Taxable };")]
    public void TheMasterRateBypassLockIgnoresTaxabilityTestsThatReadNoRate(string shippedLine) =>
        Assert.False(
            Regex.IsMatch(shippedLine, D9MasterRateBypass),
            $"D9 false-positives on a line that reads no rate off a master:\n  {shippedLine}\nPattern: {D9MasterRateBypass}");

    /// <summary>
    /// D9b's BITE, asserted directly rather than inferred from an inventory count: the three bypass shapes D9's
    /// property-pattern could not see must all match. The first is the near-miss D9's guard used to pin as a
    /// permitted non-match; the second is the shipped <c>RcmService</c> line this widening actually surfaced; the
    /// third is the plainest re-introduction of a reader T0-17 just removed. Without this, "the inventory is
    /// unchanged" would be indistinguishable from "the pattern silently stopped matching".
    /// </summary>
    [Theory]
    [InlineData(@"        if (item?.Gst is { } itemGst && itemGst.IsTaxable && itemGst.RateBasisPoints is { } ir)")]
    [InlineData(@"            var importRate = supplyGst?.RateBasisPoints ?? spLedger?.SalesPurchaseGst?.RateBasisPoints ?? 1800;")]
    [InlineData(@"        company.FindStockItem(il.StockItemId)?.Gst is { IsTaxable: true, RateBasisPoints: { } bp } ? bp : 0;")]
    public void TheWidenedLockCatchesTheBypassShapesD9CouldNotSee(string bypassLine) =>
        Assert.True(
            Regex.IsMatch(bypassLine, D9bMasterRateRead),
            $"D9b fails to bite on a master-block rate read:\n  {bypassLine}\nPattern: {D9bMasterRateRead}");

    /// <summary>
    /// D9b must NOT fire on the resolver itself, or the ONE home would be locked out of doing its own job. These are
    /// VERBATIM lines from <c>GstService</c>'s <c>Hierarchy</c>/<c>ResolveBase</c>: they reach <c>RateBasisPoints</c>
    /// through a <c>Rung</c>/<c>block</c> local, never through a master accessor on the same statement. This is the
    /// property that lets D9b be widened at all.
    /// </summary>
    [Theory]
    [InlineData(@"            if (rung.RateBasisPoints is { } bp)")]
    [InlineData(@"            : new Rung(block.IsTaxable, block.Taxability, block.RateBasisPoints, block.ValuationBasis, block);")]
    [InlineData(@"                HierarchyLevel.Company => Narrow(_company.Gst?.DefaultGst),")]
    public void TheWidenedLockDoesNotFireOnTheResolverItself(string resolverLine) =>
        Assert.False(
            Regex.IsMatch(resolverLine, D9bMasterRateRead),
            $"D9b false-positives on the resolver's own walk:\n  {resolverLine}\nPattern: {D9bMasterRateRead}");
}
