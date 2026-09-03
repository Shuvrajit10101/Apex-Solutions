using System;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Behavioural tests for the rules unified in the "one rule, one home" slice — D1 pro-rata apportionment,
/// D2 Indian digit grouping, D3 rupees→paisa and D7 HSN/SAC resolution — plus the two claims this slice
/// examined and did <b>not</b> change (D6 rounding, D8 basis-point rendering), each with the evidence that
/// settled it.
///
/// <para>Every money fixture carries ODD PAISA. A round number would pass under either side of a rounding or
/// grouping divergence and so asserts nothing about which behaviour is in force.</para>
/// </summary>
public sealed class UnifiedRuleBehaviourTests
{
    /// <summary>A minimal valid stock item; the HSN fields are set by the caller.</summary>
    private static StockItem NewItem() =>
        new(Guid.NewGuid(), "Widget", Guid.NewGuid(), Guid.NewGuid());

    // ============================================================ D1 — pro-rata apportionment

    /// <summary>
    /// The zero-denominator guard, which the shared rule keeps because two of the three replaced copies had it.
    ///
    /// <para><b>This was a pure de-duplication — it did NOT fix a live crash, and an earlier revision of this
    /// comment claimed it did.</b> <c>Gstr1.Apportion</c> carried no guard of its own, but both of its call sites
    /// already <c>continue</c> on <c>groupValue == 0m</c> before the apportionment loop, and those are the only
    /// paths to it, so <see cref="DivideByZeroException"/> was unreachable. The caller-side guards remain
    /// LOAD-BEARING (they skip the group entirely); this <c>0</c> is defence in depth. See
    /// <see cref="Gstr1ZeroValueRateGroupTests"/>, which pins the observable report behaviour.</para>
    /// </summary>
    [Fact]
    public void ZeroGroupValueApportionsToZeroInsteadOfDividingByZero()
    {
        Assert.Equal(0m, ProRata.Rupees(1234.57m, 0m, 0m));
        Assert.Equal(0L, ProRata.Paisa(123457L, 0L, 0L));
    }

    /// <summary>An ordinary split rounds away from zero at the target scale, in both the rupee and paisa forms.</summary>
    [Fact]
    public void ApportionmentRoundsAwayFromZeroAtTheTargetScale()
    {
        // 1000.01 × (333.33 / 999.99) = 333.33666… ⇒ 333.34 at 2 dp, away from zero.
        Assert.Equal(333.34m, ProRata.Rupees(1000.01m, 333.33m, 999.99m));

        // 100001 p × (33333 / 99999) = 33333.6666… ⇒ 33334 p.
        Assert.Equal(33334L, ProRata.Paisa(100001L, 33333L, 99999L));
    }

    /// <summary>
    /// <b>The midpoint fixture — the only kind that can tell AwayFromZero from banker's rounding.</b> The two
    /// fixtures above cannot: 333.33666… and 33333.6666… are not midpoints at all, so <c>ToEven</c> and
    /// <c>AwayFromZero</c> agree on them and the assertions hold under either mode. A midpoint alone is still not
    /// enough — it must be one whose LOWER neighbour is EVEN, or <c>ToEven</c> rounds up too and agrees again.
    ///
    /// <para>0.05 × 1 / 2 = 0.025 exactly. At 2 dp the neighbours are 0.02 (even) and 0.03, so AwayFromZero gives
    /// 0.03 and ToEven gives 0.02 — they DISAGREE, which is what makes this an assertion rather than a
    /// coincidence. Likewise 5 × 1 / 2 = 2.5 in whole paisa: AwayFromZero 3, ToEven 2. Dropping
    /// <c>MidpointRounding.AwayFromZero</c> from either <see cref="ProRata"/> overload silently applies banker's
    /// rounding to the GSTR-1 Table-12, INV-01 and EWB-01 per-item tax split; this is the test that notices.</para>
    /// </summary>
    [Fact]
    public void ApportionmentBreaksMidpointsAwayFromZeroNotToEven()
    {
        Assert.Equal(0.03m, ProRata.Rupees(0.05m, 1m, 2m));   // ToEven would give 0.02
        Assert.Equal(-0.03m, ProRata.Rupees(-0.05m, 1m, 2m)); // ToEven would give -0.02

        Assert.Equal(3L, ProRata.Paisa(5L, 1L, 2L));          // ToEven would give 2
        Assert.Equal(-3L, ProRata.Paisa(-5L, 1L, 2L));        // ToEven would give -2
    }

    /// <summary>
    /// The guard is <c>== 0</c>, not <c>&lt;= 0</c>: a credit note carries a negative group value, where the leg
    /// and group share a sign and the ratio is still the correct positive share. Widening the guard would zero
    /// the tax split on every negative-value document.
    /// </summary>
    [Fact]
    public void NegativeGroupValueStillApportionsRatherThanCollapsingToZero()
    {
        Assert.Equal(-333.34m, ProRata.Rupees(-1000.01m, -333.33m, -999.99m));
        Assert.Equal(-33334L, ProRata.Paisa(-100001L, -33333L, -99999L));
    }

    // ============================================================ D2 — Indian digit grouping

    /// <summary>
    /// Lakh and crore grouping (3;2;2). Sourced from the corpus: Tally exposes digit style as the currency-master
    /// flag "Show Amounts in Millions" (664311548-Tally-Prime-Book.pdf, company-creation field list at author
    /// page 9 and the Currency Create field list) — an explicit opt-in, so the default is the Indian grouping,
    /// and because it lives on the CURRENCY it applies to every document alike.
    /// </summary>
    [Theory]
    [InlineData(100000.57, "1,00,000.57")]
    [InlineData(1234567.89, "12,34,567.89")]
    [InlineData(10000000.01, "1,00,00,000.01")]
    [InlineData(999.99, "999.99")]
    [InlineData(-100000.57, "-1,00,000.57")]
    public void MoneyGroupsTheIndianWay(decimal value, string expected) =>
        Assert.Equal(expected, IndianMoneyFormat.Amount(value));

    /// <summary>
    /// The grouping is deterministic regardless of the host machine's locale — the culture is cloned from the
    /// invariant culture and its group sizes set explicitly, never looked up by name.
    /// </summary>
    [Fact]
    public void GroupingIsNotWesternAndNotHostDependent()
    {
        Assert.Equal(new[] { 3, 2 }, IndianMoneyFormat.Culture.NumberFormat.NumberGroupSizes);
        Assert.DoesNotContain("100,000", IndianMoneyFormat.Amount(100000.57m));
    }

    /// <summary>
    /// <b>The one grouping rule is FROZEN, not merely shared.</b> <c>CultureInfo.Clone</c> returns a culture
    /// whose <see cref="System.Globalization.NumberFormatInfo"/> is writable — that is the only reason the rule can
    /// set its group sizes at construction. Publishing that writable object as a <c>public static</c> field would
    /// have made the rule a process-wide global: one line anywhere in Apex.Desktop, Apex.Ledger.Io or an
    /// earlier-running test could rewrite <c>NumberGroupSizes</c> and revert every invoice, receipt, voucher,
    /// certificate and report grid in the process to Western grouping — the exact defect D2 exists to fix, arriving
    /// as order-dependent cross-test contamination that would be extremely hard to diagnose. Consolidating nine
    /// call sites onto one object is what created that blast radius, so the object is read-only.
    ///
    /// <para>Remove the <c>CultureInfo.ReadOnly</c> wrapper in <c>IndianMoneyFormat.CreateIndianCulture</c> and
    /// this fails — the mutation succeeds and the assertion on the reverted output fires.</para>
    /// </summary>
    [Fact]
    public void TheOneGroupingCultureCannotBeRewrittenByAnybody()
    {
        Assert.True(IndianMoneyFormat.Culture.IsReadOnly);
        Assert.True(IndianMoneyFormat.Culture.NumberFormat.IsReadOnly);

        Assert.Throws<InvalidOperationException>(
            () => IndianMoneyFormat.Culture.NumberFormat.NumberGroupSizes = new[] { 3 });
        Assert.Throws<InvalidOperationException>(
            () => IndianMoneyFormat.Culture.NumberFormat.NumberGroupSeparator = " ");

        // …and the rule still renders the Indian way after the attempts, i.e. nothing leaked through.
        Assert.Equal("12,34,567.89", IndianMoneyFormat.Amount(1234567.89m));
    }

    // ============================================================ D3 — rupees → paisa

    /// <summary>
    /// The two semantics, under names that say which is which. A sub-paisa amount is FATAL at the persist/export
    /// boundary (silent loss in the system of record is unacceptable) and ROUNDED on a derived report path (which
    /// must yield a number, not abort). Both behaviours are kept deliberately.
    /// </summary>
    [Fact]
    public void SubPaisaThrowsOnTheExactPathAndRoundsOnTheRoundedPath()
    {
        var subPaisa = 1234.567m;

        Assert.Throws<InvalidOperationException>(() => PaisaConversion.ToPaisaExact(subPaisa));
        Assert.Equal(123457L, PaisaConversion.ToPaisaRounded(subPaisa)); // .567 ⇒ .57, away from zero
    }

    /// <summary>A paisa-exact amount gives the same answer on both paths — the semantics differ only sub-paisa.</summary>
    [Theory]
    [InlineData(1234.57, 123457L)]
    [InlineData(-1234.57, -123457L)]
    [InlineData(0.01, 1L)]
    public void PaisaExactAmountsAgreeOnBothPaths(decimal rupees, long expected)
    {
        Assert.Equal(expected, PaisaConversion.ToPaisaExact(rupees));
        Assert.Equal(expected, PaisaConversion.ToPaisaRounded(rupees));
    }

    /// <summary>
    /// Rounding is away from zero on both signs, matching every replaced copy and <see cref="Money.RoundToPaisa"/>.
    ///
    /// <para><b>The fixture is chosen so it can actually fail.</b> The obvious midpoint 1234.575 CANNOT
    /// distinguish the two modes: ×100 gives 123457.5, whose neighbours are 123457 (odd) and 123458 (even), so
    /// banker's rounding goes UP to the even one and agrees with away-from-zero. A midpoint only discriminates
    /// when its LOWER neighbour is even. 1234.565 × 100 = 123456.5 sits between 123456 (even) and 123457, so
    /// AwayFromZero gives 123457 while ToEven gives 123456 — they disagree, and dropping
    /// <c>MidpointRounding.AwayFromZero</c> from <see cref="PaisaConversion.ToPaisaRounded(decimal)"/> — which
    /// would silently switch 14 rupees→paisa call sites (GSTR-2B reconciliation, ITC set-off, e-Way consignment
    /// value, CSV import, portal JSON import) to banker's rounding — fails here.</para>
    /// </summary>
    [Fact]
    public void RoundedPathRoundsHalvesAwayFromZeroOnBothSigns()
    {
        Assert.Equal(123457L, PaisaConversion.ToPaisaRounded(1234.565m));   // ToEven would give 123456
        Assert.Equal(-123457L, PaisaConversion.ToPaisaRounded(-1234.565m)); // ToEven would give -123456
    }

    /// <summary>
    /// The store previously wrote payroll declaration amounts through a bare <c>(long)</c> cast — a THIRD
    /// semantics, truncation toward zero, which loses a paisa silently rather than rounding it. The exact path
    /// refuses the value instead, and rounding (where a path legitimately rounds) disagrees with truncation.
    /// </summary>
    [Fact]
    public void TruncationWasAThirdSemanticsAndDisagreesWithBothKeptOnes()
    {
        var subPaisa = 1234.567m;

        Assert.Equal(123456L, (long)(subPaisa * 100m));                 // the removed truncating behaviour
        Assert.Equal(123457L, PaisaConversion.ToPaisaRounded(subPaisa)); // differs by a paisa
        Assert.Throws<InvalidOperationException>(() => PaisaConversion.ToPaisaExact(subPaisa));
    }

    /// <summary>The sub-paisa predicate the typed-amount parsers and <see cref="Money.IsPaisaExact"/> share.</summary>
    [Fact]
    public void PaisaExactPredicateAndTryConversionAgree()
    {
        Assert.True(PaisaConversion.IsPaisaExact(1234.57m));
        Assert.False(PaisaConversion.IsPaisaExact(1234.567m));
        Assert.True(new Money(1234.57m).IsPaisaExact);
        Assert.False(new Money(1234.567m).IsPaisaExact);

        Assert.True(PaisaConversion.TryToPaisaExact(1234.57m, out var ok));
        Assert.Equal(123457L, ok);
        Assert.False(PaisaConversion.TryToPaisaExact(1234.567m, out var bad));
        Assert.Equal(0L, bad);
    }

    /// <summary>Paisa → rupees is exact and round-trips.</summary>
    [Fact]
    public void PaisaRoundTripsExactly()
    {
        Assert.Equal(1234.57m, PaisaConversion.ToRupees(123457L));
        Assert.Equal(123457L, PaisaConversion.ToPaisaExact(PaisaConversion.ToRupees(123457L)));
    }

    // ============================================================ D7 — HSN/SAC resolution

    /// <summary>
    /// The resolution ORDER is the shared rule: the item's GST block wins over the legacy Phase-3 field, and an
    /// item declaring neither resolves to <c>null</c> — the absence, which each consumer then spells its own way.
    /// </summary>
    [Fact]
    public void HsnResolutionPrefersTheGstBlockThenTheLegacyFieldThenNull()
    {
        var withBoth = NewItem();
        withBoth.HsnSacCode = "1111";
        withBoth.Gst = new StockItemGstDetails { HsnSac = "84713010" };
        Assert.Equal("84713010", GstReportSupport.HsnSacOf(withBoth));

        var legacyOnly = NewItem();
        legacyOnly.HsnSacCode = "1111";
        Assert.Equal("1111", GstReportSupport.HsnSacOf(legacyOnly));

        var neither = NewItem();
        Assert.Null(GstReportSupport.HsnSacOf(neither));

        Assert.Null(GstReportSupport.HsnSacOf(null));
    }

    // The sentinels stay DIFFERENT on purpose — the return labels its unclassified bucket for a human, the NIC
    // payloads file the schema's empty string, and the printed invoice omits the field. That divergence is pinned
    // AT THE CONSUMERS, in AbsentHsnSentinelsPerConsumerTests (this project) and
    // AbsentHsnPrintedInvoiceSentinelTests (Apex.Desktop.Tests), because it can only be observed there.
    //
    // A test that lived here previously and asserted `Assert.Equal("(none)", absent ?? "(none)")` was deleted: it
    // is `x == (null ?? x)`, true for any x, and it called no consumer — so it reported green against every
    // sentinel change it existed to prevent.

    // ============================================================ D8 — examined, NOT a defect

    /// <summary>
    /// <b>The reported D8 divergence does not exist.</b> The claim was that <c>"0.###"</c> renders a 0.125% rate
    /// as <c>0.125%</c> where <c>"0.##"</c> renders <c>0.13%</c>. It cannot: every <c>RateBasisPoints</c> in the
    /// domain is an <see cref="int"/>, so <c>bp / 100m</c> has at most TWO decimal places and the third digit is
    /// always zero. A 0.125% rate is not representable in basis points at all.
    ///
    /// <para>This is proved exhaustively rather than argued: over the entire plausible basis-point range the two
    /// formats produce byte-identical output. Nothing was changed for D8 — there was nothing to change, and
    /// "unifying" the two formats would have been churn justified by a defect that is not real.</para>
    /// </summary>
    [Fact]
    public void BasisPointFormatsAreIdenticalForEveryRepresentableRate()
    {
        for (var bp = -100_000; bp <= 100_000; bp++)
        {
            var twoDp = (bp / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var threeDp = (bp / 100m).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            if (twoDp != threeDp)
                Assert.Fail($"basis points {bp} rendered '{twoDp}' as 0.## but '{threeDp}' as 0.### — D8 would be real.");
        }
    }

    // ============================================================ D6 — examined, LEGITIMATELY different

    /// <summary>
    /// <b>The two <c>ApplyRounding</c> methods were deliberately left separate.</b> They disagree on negatives —
    /// interest rounding is magnitude-based (Upward on −100.4 ⇒ −101, i.e. away from zero) while payroll rounding
    /// is signed (Ceiling(−100.4) ⇒ −100, i.e. toward +∞). That is a real difference, but not a duplication
    /// defect: they take different enums (<see cref="InterestRoundingMethod"/> vs <c>PayHeadRoundingMethod</c>),
    /// are parameterised differently (decimal places vs a rounding LIMIT in rupees), and belong to different
    /// statutory domains. Forcing them onto one shared method would silently change one domain's arithmetic to
    /// suit the other's — worse than the divergence. This test pins the interest side's magnitude semantics so a
    /// later "cleanup" cannot quietly convert it to the signed form.
    /// </summary>
    [Fact]
    public void InterestRoundingIsMagnitudeBasedAndIsPinnedAsSuch()
    {
        var upward = new InterestParameters(
            enabled: true, ratePercent: 12m, per: InterestPer.ThreeSixtyFiveDayYear,
            roundingMethod: InterestRoundingMethod.Upward, roundingDecimals: 0);

        Assert.Equal(-101m, upward.ApplyRounding(-100.4m)); // magnitude: away from zero
        Assert.Equal(101m, upward.ApplyRounding(100.4m));

        var downward = new InterestParameters(
            enabled: true, ratePercent: 12m, per: InterestPer.ThreeSixtyFiveDayYear,
            roundingMethod: InterestRoundingMethod.Downward, roundingDecimals: 0);

        Assert.Equal(-100m, downward.ApplyRounding(-100.6m)); // magnitude: toward zero
        Assert.Equal(100m, downward.ApplyRounding(100.6m));
    }
}
