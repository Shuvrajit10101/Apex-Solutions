using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>The GSTR-1 zero-value rate-group guards are load-bearing.</b> This class exists because the "one rule, one
/// home" slice shipped three doc comments asserting something false, and the false claim invites a specific,
/// silently-wrong-return regression.
///
/// <para><b>What was claimed, and why it is wrong.</b> <c>ProRata</c>, <c>Gstr1.Apportion</c> and the D1 behaviour
/// test all said the un-guarded <c>Gstr1</c> copy "raised <c>DivideByZeroException</c> while building a filed
/// return". It could not. <c>Apportion</c> has exactly six call sites — three in the stock/HSN loop and three in
/// the service-SAC loop — and BOTH loops <c>continue</c> on <c>groupValue == 0m</c> before reaching any of them.
/// Those guards pre-date the slice (they are at <c>Gstr1.cs:629</c> and <c>:800</c> in the parent commit). A zero
/// denominator was therefore unreachable, and D1 changed no caller's answer anywhere in the tree.</para>
///
/// <para><b>Why the correction matters rather than being pedantry.</b> A maintainer who believes the shared rule
/// now "handles" a zero group will read the caller-side guards as redundant leftovers and delete them. The result
/// is NOT the harmless zero the comments imply: with the guard gone, every non-final leg of the group apportions
/// to 0 while the loop's remainder branch (<c>cgst = group.Cgst - runCgst</c>) hands the group's ENTIRE posted tax
/// to its last leg. That is a filed GSTR-1 Table-12 whose per-HSN split is wrong, with the totals still footing —
/// the hardest kind of error to notice.</para>
///
/// <para><b>Why this lock reads source instead of posting a voucher.</b> The zero-value rate-group state is not
/// constructible through <c>LedgerService.Post</c>: a rate group only exists because tax lines were posted for it
/// (<c>ReadInvoiceRateGroups</c> reads the voucher's own <c>Gst</c>-bearing legs), and a group whose stock lines
/// sum to zero taxable has no tax to post. On a single-rate invoice the item-invoice pairing invariant
/// (Σ quantity × rate == the accounting leg) ties the group value to the sale itself, so it cannot be zeroed while
/// the sale is non-zero. The guards are genuine defence in depth — which is exactly why nothing behavioural can
/// notice their removal, and why the protection has to be a source lock. Asserting a fixture here would mean
/// inventing a voucher shape the application cannot create.</para>
/// </summary>
public sealed class Gstr1ZeroValueRateGroupTests
{
    /// <summary>The guard, as it appears at both call sites.</summary>
    private const string ZeroGroupGuard = @"if\s*\(\s*groupValue\s*==\s*0m\s*\)\s*continue\s*;";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Gstr1Source() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Apex.Ledger", "Reports", "Gstr1.cs"));

    /// <summary>
    /// BOTH apportionment loops — the stock/HSN path and the service-SAC path — must skip a zero-value rate group
    /// before apportioning. Deleting either guard fails here.
    /// </summary>
    [Fact]
    public void BothApportionmentLoopsStillSkipAZeroValueRateGroup()
    {
        var matches = Regex.Matches(Gstr1Source(), ZeroGroupGuard);

        Assert.True(
            matches.Count == 2,
            $"expected the zero-group guard at BOTH Gstr1 apportionment call sites (the stock/HSN loop and the " +
            $"service-SAC loop), found {matches.Count}. These guards are load-bearing: without one, the group's " +
            $"whole posted tax lands on its last leg via the remainder branch while every other leg files 0.");
    }

    /// <summary>
    /// The guard must sit BEFORE the apportionment, not after it. A guard moved below the loop would still match
    /// the pattern above while protecting nothing, so the ordering is pinned too.
    /// </summary>
    [Fact]
    public void EachGuardPrecedesTheApportionmentItProtects()
    {
        var src = Gstr1Source();
        var guards = Regex.Matches(src, ZeroGroupGuard);
        var apportions = Regex.Matches(src, @"=\s*Apportion\(");

        Assert.Equal(2, guards.Count);
        Assert.Equal(6, apportions.Count); // three heads × two loops

        // Every apportionment call must be preceded by at least one guard.
        foreach (Match a in apportions)
            Assert.True(
                guards.Any(g => g.Index < a.Index),
                $"an Apportion call at offset {a.Index} is not preceded by a zero-group guard.");

        // …and the SECOND loop's calls must be preceded by BOTH guards, i.e. the guards are not both in loop one.
        var lastApportion = apportions[^1];
        Assert.True(
            guards.All(g => g.Index < lastApportion.Index),
            "both zero-group guards appear before the FIRST loop's apportionment — the service-SAC loop has none.");
    }

    /// <summary>
    /// The bite proof: the guard pattern must actually recognise the guard. A lock whose regex silently stopped
    /// matching would report "2 found" never again — so the pattern is exercised against the literal source form
    /// and against a plausible reformatting, and refused on the loop body that has no guard at all.
    /// </summary>
    [Theory]
    [InlineData("            if (groupValue == 0m) continue;", true)]
    [InlineData("if(groupValue==0m)continue;", true)]
    [InlineData("            var groupValue = groupLines.Sum(l => l.Value.Amount);", false)]
    [InlineData("            if (groupValue <= 0m) continue;", false)]
    public void TheGuardPatternRecognisesTheGuardAndNothingElse(string line, bool shouldMatch) =>
        Assert.Equal(shouldMatch, Regex.IsMatch(line, ZeroGroupGuard));

    /// <summary>
    /// The shared rule's own <c>== 0</c> is defence in depth and is pinned as such — it answers 0 rather than
    /// throwing — but note that this is NOT what a caller observes for a zero group. The caller SKIPS the group
    /// entirely and emits no HSN row; that difference is the whole point of the two tests above.
    /// </summary>
    [Fact]
    public void TheSharedRuleAnswersZeroButThatIsNotWhatTheCallerDoes()
    {
        Assert.Equal(0m, ProRata.Rupees(1234.57m, 567.89m, 0m));
        Assert.Equal(0L, ProRata.Paisa(123457L, 56789L, 0L));
    }
}
