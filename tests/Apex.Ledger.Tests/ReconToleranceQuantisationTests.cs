using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>The GSTR-2B reconciliation tolerance's rupees→paisa quantisation, pinned.</b>
///
/// <para>The "one rule, one home" slice changed <see cref="GstConfig.ReconTolerance"/> from a bare
/// <c>(long)(ReconValueTolerance.Amount * 100m)</c> — truncation toward zero, a THIRD semantics beside the two the
/// slice kept — to <see cref="PaisaConversion.ToPaisaRounded(Money)"/>. For a sub-paisa configured tolerance the
/// two disagree by one paisa, and this threshold decides a reconciliation line's bucket: it is the value
/// <c>Gstr2bReconciler.WithinValue</c> compares a books-vs-portal difference against, so a pair differing by
/// exactly the disputed paisa moves between <c>Matched</c> and <c>PartialMismatch</c>, changing which invoices the
/// ITC gate treats as reconciled.</para>
///
/// <para><b>No existing fixture could see the change.</b> The tolerance fixtures in the suite are ₹1.00 and ₹1.50 —
/// both paisa-exact, where truncation and rounding give the same answer — so the whole suite passed before and
/// after. That is precisely the shape of defect this slice was raised to find, so the decision is pinned here
/// rather than left incidental.</para>
///
/// <para><b>Reachability, stated honestly.</b> A sub-paisa tolerance cannot be PERSISTED or exported: the store
/// writes it through <c>Paisa.FromMoney</c> and the canonical exporter through <c>MoneyCodec.ToPaisa</c>, and both
/// throw on a sub-paisa amount. So this only bites within a session in which the tolerance is set sub-paisa and a
/// reconciliation is run before saving. The rounding choice is still the right one — it is the semantics every
/// other derived-comparison path uses, and quantising a threshold outward is the conservative direction for a
/// tolerance — but the narrowness is recorded so nobody later reads this test as evidence of a shipped bug.</para>
/// </summary>
public sealed class ReconToleranceQuantisationTests
{
    /// <summary>
    /// ₹1.005 → 101 paisa (rounded), NOT 100 (the removed truncation). This is the assertion that would have
    /// failed before the slice, and that fails again if the conversion is reverted to a bare <c>(long)</c> cast.
    /// </summary>
    [Fact]
    public void ASubPaisaToleranceRoundsOutwardRatherThanTruncatingInward()
    {
        var config = new GstConfig { ReconValueTolerance = new Money(1.005m), ReconDateWindowDays = 3 };

        Assert.Equal(101L, config.ReconTolerance.ValueTolerancePaisa);
        Assert.NotEqual(100L, config.ReconTolerance.ValueTolerancePaisa); // the truncating answer
        Assert.Equal(3, config.ReconTolerance.DateWindowDays);
    }

    /// <summary>
    /// Rounding is away from zero, on a midpoint whose lower neighbour is EVEN — the only kind that distinguishes
    /// away-from-zero from banker's rounding. ₹1.025 × 100 = 102.5, between 102 (even) and 103, so AwayFromZero
    /// gives 103 where <c>ToEven</c> would give 102.
    /// </summary>
    [Fact]
    public void TheQuantisationBreaksMidpointsAwayFromZeroNotToEven()
    {
        var config = new GstConfig { ReconValueTolerance = new Money(1.025m) };

        Assert.Equal(103L, config.ReconTolerance.ValueTolerancePaisa); // ToEven would give 102
    }

    /// <summary>
    /// A paisa-exact tolerance — every tolerance the application can actually persist — is unaffected, which is why
    /// no existing fixture moved. Odd paisa, so the value asserts something.
    /// </summary>
    [Theory]
    [InlineData(1.00, 100L)]
    [InlineData(1.51, 151L)]
    [InlineData(0.07, 7L)]
    [InlineData(0.00, 0L)]
    public void APaisaExactToleranceIsUnchangedByTheQuantisation(decimal rupees, long expectedPaisa) =>
        Assert.Equal(
            expectedPaisa,
            new GstConfig { ReconValueTolerance = new Money(rupees) }.ReconTolerance.ValueTolerancePaisa);
}
