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
}
