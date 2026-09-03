using System;
using Apex.Ledger;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// W0-13 PART 2, slice S2a — the DOMAIN half of the sub-paisa guard for the three voucher-entry value objects
/// that persist through <c>Paisa.FromMoney</c> and validated sign but not paisa-exactness:
/// <see cref="BillAllocation"/> (<c>SqliteCompanyStore</c> :6907), <see cref="CostAllocation"/> (:6930) and
/// <see cref="PosTender"/> (:6823-6825).
///
/// <para>They now carry the same invariant their siblings already do —
/// <see cref="AdditionalCostLine"/>, <see cref="GstLineTax"/>, <see cref="TcsLineTax"/>,
/// <see cref="GstChallan"/>, <see cref="StockOpeningBalance"/> — so the refusal happens at the ONE choke point
/// every caller flows through (screens, the canonical-XML import, hand-written callers) rather than at the
/// store, where it has already mutated the shared aggregate.</para>
///
/// <para>Every fixture is odd-paisa: a round stem would let a truncation land on the same number.</para>
/// </summary>
public sealed class AllocationAndTenderPaisaExactnessTests
{
    private static readonly Guid Category = Guid.NewGuid();
    private static readonly Guid Centre = Guid.NewGuid();
    private static readonly Guid LedgerId = Guid.NewGuid();

    // ---------------------------------------------------------------- BillAllocation

    [Fact]
    public void ABillAllocationRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new BillAllocation(BillRefType.NewRef, "INV-9001", new Money(1234.565m)));
        Assert.Contains("1234.565", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void ABillAllocationAcceptsAnOddButPaisaExactAmount()
    {
        var a = new BillAllocation(BillRefType.NewRef, "INV-9001", new Money(1234.57m));
        Assert.Equal(1234.57m, a.Amount.Amount);
    }

    // ---------------------------------------------------------------- CostAllocation

    [Fact]
    public void ACostAllocationRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new CostAllocation(Category, Centre, new Money(1666.785m)));
        Assert.Contains("1666.785", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void ACostAllocationAcceptsAnOddButPaisaExactAmount()
    {
        var a = new CostAllocation(Category, Centre, new Money(1666.79m));
        Assert.Equal(1666.79m, a.Amount.Amount);
    }

    // ---------------------------------------------------------------- PosTender
    //
    // All THREE money members persist (:6823 amount, :6824 tendered, :6825 change), so all three are guarded.

    [Fact]
    public void APosTenderRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Card, LedgerId, new Money(33.335m)));
        Assert.Contains("33.335", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    /// <summary>
    /// Tendered is asserted with NO Change and an exact Amount, so only the Tendered guard can produce the throw.
    /// The first draft passed a sub-paisa Change alongside — it stayed green with the Tendered guard deleted,
    /// because the Change guard threw instead. A guard that another guard can cover for is not pinned.
    /// </summary>
    [Fact]
    public void APosTenderRefusesASubPaisaTenderedCash()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m),
                Tendered: new Money(40.005m)));
        Assert.Contains("40.005", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void APosTenderRefusesASubPaisaChange()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m),
                Tendered: new Money(40.00m), Change: new Money(6.675m)));
        Assert.Contains("6.675", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    /// <summary>
    /// The construction path must CARRY the values, not merely accept them. Declaring a property whose name
    /// matches a positional parameter suppresses the record's own assignment (warning CS8907) — get that wrong and
    /// the parameter is silently dropped, every tender posts as ZERO, and that is a far worse defect than the one
    /// being fixed.
    ///
    /// <para>The three property assertions cover the dropped-assignment case. The two below cover what the manual
    /// property declarations put at risk SEPARATELY: the synthesised <c>Deconstruct</c> (an 8-tuple, so a
    /// positional list that stopped matching the members would not compile here) and the copy constructor +
    /// value equality behind <c>with</c>, which copy the BACKING FIELDS and never re-run the initialisers.</para>
    /// </summary>
    [Fact]
    public void APosTenderAcceptsAnOddButPaisaExactTriple()
    {
        var t = new PosTender(PosTenderType.Cash, LedgerId, new Money(12032.17m),
            Tendered: new Money(12100.03m), Change: new Money(67.86m));
        Assert.Equal(12032.17m, t.Amount.Amount);
        Assert.Equal(12100.03m, t.Tendered!.Value.Amount);
        Assert.Equal(67.86m, t.Change!.Value.Amount);

        var (_, _, amount, tendered, change, _, _, _) = t;
        Assert.Equal(12032.17m, amount.Amount);
        Assert.Equal(12100.03m, tendered!.Value.Amount);
        Assert.Equal(67.86m, change!.Value.Amount);

        var copy = t with { };
        Assert.Equal(t, copy);
        Assert.Equal(12032.17m, copy.Amount.Amount);
        Assert.Equal(67.86m, copy.Change!.Value.Amount);
    }

    // ---------------------------------------------------------------- `with` must not slip past the record
    //
    // A record's field initialisers run in the PRIMARY constructor only; the copy constructor behind a `with`
    // expression does not re-run them. Without a check in each init accessor the whole guard would be one
    // `with { Amount = … }` away from being bypassed, silently.

    [Fact]
    public void AWithExpressionCannotSlipASubPaisaAmountPastThePosTender()
    {
        var tender = new PosTender(PosTenderType.Card, LedgerId, new Money(33.33m));
        var ex = Assert.Throws<InvalidOperationException>(() => tender with { Amount = new Money(33.335m) });
        Assert.Contains("33.335", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void AWithExpressionCannotSlipASubPaisaTenderedPastThePosTender()
    {
        var tender = new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m));
        var ex = Assert.Throws<InvalidOperationException>(() => tender with { Tendered = new Money(40.005m) });
        Assert.Contains("40.005", ex.Message);
    }

    [Fact]
    public void AWithExpressionCannotSlipASubPaisaChangePastThePosTender()
    {
        var tender = new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m));
        var ex = Assert.Throws<InvalidOperationException>(() => tender with { Change = new Money(6.675m) });
        Assert.Contains("6.675", ex.Message);
    }

    // ---------------------------------------------------------------- the MAGNITUDE half of the same guard
    //
    // "Storable" is magnitude AND exactness. A guard that tested exactness alone did not merely miss the
    // over-large figure — it THREW OverflowException on it (the paisa predicate scales by a hundred; the store's
    // conversion then narrows to long), and OverflowException is an ArithmeticException no domain-refusal filter
    // in the app treats as a refusal. So these assert the TYPE as much as the fact of refusal: an
    // InvalidOperationException the screens' filters already match, not an arithmetic escape.

    /// <summary>17 digits and PAISA-EXACT — invisible to the exactness half, fatal at the store's (long) cast.</summary>
    [Fact]
    public void ABillAllocationRefusesAnAmountBeyondWhatIntegerPaisaCanCarry()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new BillAllocation(BillRefType.NewRef, "INV-9001", new Money(99999999999999999.13m)));
        Assert.Contains("99999999999999999.13", ex.Message);
        Assert.True(new Money(99999999999999999.13m).IsPaisaExact);   // …which the exactness test alone allows
    }

    /// <summary>And a figure so large the exactness predicate itself would overflow before it could answer.</summary>
    [Fact]
    public void ACostAllocationRefusesAnAmountThatWouldOverflowTheSubPaisaPredicateItself()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new CostAllocation(Category, Centre, new Money(7.9e28m)));
        Assert.Contains("integer paisa", ex.Message);
    }

    [Fact]
    public void APosTenderRefusesAnAmountBeyondWhatIntegerPaisaCanCarry()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Card, LedgerId, new Money(99999999999999999.13m)));
        Assert.Contains("99999999999999999.13", ex.Message);
    }

    /// <summary>The Tendered and Change guards carry the same half — pinned separately, with the other two
    /// members left storable so only the member under test can raise the throw.</summary>
    [Fact]
    public void APosTenderRefusesATenderedAndAChangeBeyondWhatIntegerPaisaCanCarry()
    {
        var tendered = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m),
                Tendered: new Money(99999999999999999.13m)));
        Assert.Contains("Tendered", tendered.Message);

        var change = Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Cash, LedgerId, new Money(33.33m),
                Tendered: new Money(40.00m), Change: new Money(99999999999999999.13m)));
        Assert.Contains("Change", change.Message);
    }

    /// <summary>The exact ceiling itself must still be accepted — the guard must not over-refuse by a paisa.</summary>
    [Fact]
    public void ThePosTenderAcceptsExactlyTheCeilingAndRefusesOnePaisaMore()
    {
        var atCeiling = new PosTender(PosTenderType.Card, LedgerId, new Money(PaisaConversion.MaxStorableRupees));
        Assert.Equal(PaisaConversion.MaxStorableRupees, atCeiling.Amount.Amount);

        Assert.Throws<InvalidOperationException>(
            () => new PosTender(PosTenderType.Card, LedgerId,
                new Money(PaisaConversion.MaxStorableRupees + 0.01m)));
    }

    // ---------------------------------------------------------------- the store ceiling has ONE home

    /// <summary>
    /// The constant exists to bound the narrowing cast in <see cref="PaisaConversion.ToPaisaExact(decimal)"/>, so
    /// it is asserted against THAT — not restated. The shipped first draft read
    /// <c>Assert.Equal(long.MaxValue / 100m, MaxStorableRupees)</c>, which is the implementation line copied into
    /// the test: it can only fail if someone edits the constant and forgets the test, never for the reason that
    /// matters, which is the ceiling being wrong relative to the conversion it bounds.
    /// </summary>
    [Fact]
    public void TheStorableRupeeCeilingIsExactlyLongMaxValueInPaisa()
    {
        Assert.Equal(long.MaxValue, PaisaConversion.ToPaisaExact(PaisaConversion.MaxStorableRupees));
        Assert.Throws<OverflowException>(
            () => PaisaConversion.ToPaisaExact(PaisaConversion.MaxStorableRupees + 0.01m));

        Assert.True(PaisaConversion.FitsPaisaStore(PaisaConversion.MaxStorableRupees));
        Assert.False(PaisaConversion.FitsPaisaStore(PaisaConversion.MaxStorableRupees + 0.01m));
    }

    /// <summary>
    /// <see cref="PaisaConversion.FitsPaisaStore"/> is BOTH halves, not just the ceiling. Its first draft was
    /// pinned only by magnitude fixtures, so deleting the exactness call left the whole class green — the
    /// predicate would have degraded to a range check and every sub-paisa figure on four screens would have gone
    /// straight back through to the store.
    /// </summary>
    [Fact]
    public void FittingThePaisaStoreMeansExactAsWellAsWithinRange()
    {
        Assert.False(PaisaConversion.FitsPaisaStore(1234.565m));
        Assert.False(PaisaConversion.FitsPaisaStore(-1234.565m));
        Assert.True(PaisaConversion.FitsPaisaStore(1234.57m));      // odd paisa, exact
        Assert.True(PaisaConversion.FitsPaisaStore(-1234.57m));
    }

    /// <summary>
    /// The magnitude branch must come FIRST: the sub-paisa predicate itself scales by a hundred, which
    /// OVERFLOWS decimal well before the caller ever learns the figure is too large. A ceiling test that ran
    /// second would throw instead of returning false.
    /// </summary>
    [Fact]
    public void TheCeilingIsTestedBeforeTheSubPaisaPredicateSoAHugeFigureReturnsFalseRatherThanThrowing()
    {
        Assert.False(PaisaConversion.FitsPaisaStore(7.9e28m));
        Assert.False(PaisaConversion.FitsPaisaStore(-7.9e28m));
        Assert.False(PaisaConversion.FitsPaisaStore(99999999999999999m));   // paisa-exact, but 17 digits
        Assert.True(new Money(99999999999999999m).IsPaisaExact);            // …which the paisa test alone allows
    }
}
