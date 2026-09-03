using System;
using Apex.Ledger;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// W0-13 PART 2, slice S2b — the DOMAIN half of the sub-paisa guard for the three payroll/budget value objects
/// that persist through <c>Paisa.FromMoney</c> and validated sign (or nothing) but not paisa-exactness:
/// <see cref="BudgetLine"/> (<c>SqliteCompanyStore</c> :6596), <see cref="SalaryStructureLine"/> (:6015) and
/// <see cref="PayHeadComputationSlab"/> (:5973 / :5974 / :5977).
///
/// <para>The invariant is the same one <see cref="BillAllocation"/>, <see cref="CostAllocation"/>,
/// <see cref="PosTender"/>, <see cref="AdditionalCostLine"/> and <see cref="GstLineTax"/> already carry: refuse
/// at the ONE choke point every caller flows through, rather than at the store, where the shared aggregate has
/// already been mutated. Both non-screen callers of all three — the canonical-XML import
/// (<c>ImportPlan</c>, via <c>MoneyCodec.FromPaisa</c>) and the SQLite read path (via <c>Paisa.ToMoney</c>) —
/// build from INTEGER paisa and so cannot trip these.</para>
///
/// <para><b>The slab carries THREE money fields, not one</b>, and the store writes all three:
/// <c>value_paisa</c>, <c>from_amount_paisa</c> and <c>to_amount_paisa</c>. Each is pinned separately, because a
/// single fixture carrying two sub-paisa figures would let one guard's test pass on the OTHER guard's throw.</para>
///
/// <para>Every fixture is odd-paisa: a round stem would let a truncation land on the same number.</para>
/// </summary>
public sealed class BudgetPayrollPaisaExactnessTests
{
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid LedgerId = Guid.NewGuid();
    private static readonly Guid PayHeadId = Guid.NewGuid();

    // ---------------------------------------------------------------- BudgetLine

    [Fact]
    public void AGroupBudgetLineRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BudgetLine.ForGroup(GroupId, BudgetType.OnClosingBalance, new Money(12345.675m)));
        Assert.Contains("12345.675", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    /// <summary>Both factories reach the same private ctor, and the ledger one must be pinned too — the two
    /// call sites on the Budget master are a ternary, so a guard reached by only one arm is half a guard.</summary>
    [Fact]
    public void ALedgerBudgetLineRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BudgetLine.ForLedger(LedgerId, BudgetType.OnNettTransactions, new Money(9876.543m)));
        Assert.Contains("9876.543", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void ABudgetLineAcceptsAnOddButPaisaExactAmount()
    {
        var line = BudgetLine.ForGroup(GroupId, BudgetType.OnClosingBalance, new Money(12345.67m));
        Assert.Equal(12345.67m, line.Amount.Amount);
    }

    /// <summary>Zero is a legitimate budget (the sign guard already allows it) and must stay legitimate.</summary>
    [Fact]
    public void ABudgetLineStillAcceptsZero()
    {
        var line = BudgetLine.ForLedger(LedgerId, BudgetType.OnClosingBalance, Money.Zero);
        Assert.Equal(0m, line.Amount.Amount);
    }

    // ---------------------------------------------------------------- SalaryStructureLine

    [Fact]
    public void ASalaryStructureLineRefusesAnAmountFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new SalaryStructureLine(PayHeadId, 0, new Money(31250.005m)));
        Assert.Contains("31250.005", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void ASalaryStructureLineAcceptsAnOddButPaisaExactAmount()
    {
        var line = new SalaryStructureLine(PayHeadId, 3, new Money(31250.07m));
        Assert.Equal(31250.07m, line.Amount!.Value.Amount);
    }

    /// <summary>A null amount is the As-Computed / As-User-Defined case and must not be turned into a refusal
    /// by a guard that forgot the field is nullable.</summary>
    [Fact]
    public void ASalaryStructureLineStillAcceptsNoAmountAtAll()
    {
        var line = new SalaryStructureLine(PayHeadId, 1);
        Assert.Null(line.Amount);
    }

    // ---------------------------------------------------------------- PayHeadComputationSlab

    [Fact]
    public void AComputationSlabRefusesAValueFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(PayHeadComputationSlabType.FlatValue, value: new Money(1875.005m)));
        Assert.Contains("1875.005", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    /// <summary>The lower band bound alone — the value and upper bound are left storable so this test can only
    /// pass on the 'greater than' guard.</summary>
    [Fact]
    public void AComputationSlabRefusesAnOverBoundFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(
                PayHeadComputationSlabType.Percentage, rateBasisPoints: 1200,
                fromAmount: new Money(10000.125m)));
        Assert.Contains("10000.125", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    /// <summary>The upper band bound alone — the lower bound here is paisa-exact, so only the 'up to' guard can
    /// raise this.</summary>
    [Fact]
    public void AComputationSlabRefusesAnUpToBoundFinerThanAPaisa()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(
                PayHeadComputationSlabType.Percentage, rateBasisPoints: 1200,
                fromAmount: new Money(10000.13m), toAmount: new Money(50000.335m)));
        Assert.Contains("50000.335", ex.Message);
        Assert.Contains("paisa-exact", ex.Message);
    }

    [Fact]
    public void AComputationSlabAcceptsOddButPaisaExactMoneyInAllThreeFields()
    {
        var slab = new PayHeadComputationSlab(
            PayHeadComputationSlabType.FlatValue,
            value: new Money(1875.07m),
            fromAmount: new Money(10000.13m),
            toAmount: new Money(50000.33m));

        Assert.Equal(1875.07m, slab.Value.Amount);
        Assert.Equal(10000.13m, slab.FromAmount!.Value.Amount);
        Assert.Equal(50000.33m, slab.ToAmount!.Value.Amount);
    }

    /// <summary>A percentage slab leaves <c>Value</c> at its <c>default</c> — <c>Money</c>'s default, not
    /// <c>Money.Zero</c> passed explicitly. The value guard must accept it, or every percentage slab in the
    /// application would start throwing.</summary>
    [Fact]
    public void APercentageSlabWithNoExplicitValueIsStillAccepted()
    {
        var slab = PayHeadComputationSlab.Percentage(1250);
        Assert.Equal(0m, slab.Value.Amount);
        Assert.Equal(12.50m, slab.RatePercent);
    }

    // ---------------------------------------------------------------- the MAGNITUDE half of the same guard
    //
    // "Storable" is magnitude AND exactness, and these guards test both through PaisaConversion.FitsPaisaStore,
    // which owns the branch ORDER. The exactness half ALONE does not merely miss an over-large figure — it THROWS
    // OverflowException on it (the predicate scales by a hundred; the store's conversion then narrows to long),
    // and OverflowException is an ArithmeticException that no domain-refusal filter in the app treats as a
    // refusal. So the assertions below pin the TYPE as much as the fact: an InvalidOperationException the screens
    // already match, never an arithmetic escape.

    /// <summary>17 digits and PAISA-EXACT — invisible to the exactness half, fatal at the store's (long) cast.</summary>
    [Fact]
    public void ABudgetLineRefusesAnAmountBeyondWhatIntegerPaisaCanCarry()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BudgetLine.ForGroup(GroupId, BudgetType.OnClosingBalance, new Money(99999999999999999.13m)));
        Assert.Contains("99999999999999999.13", ex.Message);
        Assert.True(new Money(99999999999999999.13m).IsPaisaExact);   // …which the exactness test alone allows
    }

    [Fact]
    public void ASalaryStructureLineRefusesAnAmountBeyondWhatIntegerPaisaCanCarry()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new SalaryStructureLine(PayHeadId, 0, new Money(99999999999999999.13m)));
        Assert.Contains("99999999999999999.13", ex.Message);
    }

    /// <summary>All three slab money fields carry the ceiling, each pinned with the other two left storable.</summary>
    [Fact]
    public void AComputationSlabRefusesEachOfItsThreeMoneyFieldsBeyondIntegerPaisa()
    {
        var value = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(
                PayHeadComputationSlabType.FlatValue, value: new Money(99999999999999999.13m)));
        Assert.Contains("value", value.Message);

        var from = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(
                PayHeadComputationSlabType.Percentage, rateBasisPoints: 1200,
                fromAmount: new Money(99999999999999999.13m)));
        Assert.Contains("greater than", from.Message);

        var to = Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(
                PayHeadComputationSlabType.Percentage, rateBasisPoints: 1200,
                fromAmount: new Money(10000.13m), toAmount: new Money(99999999999999999.13m)));
        Assert.Contains("up to", to.Message);
    }

    /// <summary>
    /// The figure that made the exactness-only guard the WRONG FAILURE MODE rather than merely an incomplete one:
    /// at 7.9e28 the ×100 scaling inside the sub-paisa predicate overflows <c>decimal</c> itself, so a guard that
    /// tested exactness first raised <see cref="OverflowException"/> from the very call that was supposed to
    /// refuse. Every one of these must now be an ordinary domain refusal.
    /// </summary>
    [Fact]
    public void AFigureThatOverflowsTheSubPaisaPredicateIsARefusalNotAnArithmeticEscape()
    {
        Assert.Throws<InvalidOperationException>(
            () => BudgetLine.ForLedger(LedgerId, BudgetType.OnClosingBalance, new Money(7.9e28m)));
        Assert.Throws<InvalidOperationException>(
            () => new SalaryStructureLine(PayHeadId, 0, new Money(7.9e28m)));
        Assert.Throws<InvalidOperationException>(
            () => new PayHeadComputationSlab(PayHeadComputationSlabType.FlatValue, value: new Money(7.9e28m)));
    }

    /// <summary>The exact ceiling must still be accepted — the guard must not over-refuse by a paisa.</summary>
    [Fact]
    public void ABudgetLineAcceptsExactlyTheCeilingAndRefusesOnePaisaMore()
    {
        var line = BudgetLine.ForGroup(GroupId, BudgetType.OnClosingBalance,
            new Money(PaisaConversion.MaxStorableRupees));
        Assert.Equal(PaisaConversion.MaxStorableRupees, line.Amount.Amount);

        Assert.Throws<InvalidOperationException>(
            () => BudgetLine.ForGroup(GroupId, BudgetType.OnClosingBalance,
                new Money(PaisaConversion.MaxStorableRupees + 0.01m)));
    }
}
