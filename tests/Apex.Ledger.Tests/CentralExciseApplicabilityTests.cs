using System;
using System.Linq;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE CLASS-OF-GOODS GATE FOR CENTRAL EXCISE</b> (census row 15.8) — and, above everything else, the
/// <b>ASYMMETRY</b> between it and the VAT gate.
///
/// <para><b>What these exist to stop.</b> Central excise and State VAT are both pre-GST levies that survived on
/// named goods, and it is very easy — and completely wrong — to assume they survived on the SAME goods. They did
/// not, and they differ in <b>opposite directions for two of the eight classes</b>: tobacco is inside GST and
/// excisable (VAT no, excise yes); alcoholic liquor is outside GST and NOT centrally excisable (VAT yes, excise
/// no). A build that got that backwards would look entirely correct on screen while telling a liquor trader to
/// compute central excise and telling a tobacco trader they are an ordinary GST dealer. Every test below pins one
/// half of that.</para>
///
/// <para><b>Sources (R7).</b> Union List entry 84 as substituted by s.17(a)(i) of the Constitution (One Hundred
/// and First Amendment) Act, 2016, verbatim from <c>taxinformation.cbic.gov.in</c>: <i>"(a) petroleum crude;
/// (b) high speed diesel; (c) motor spirit (commonly known as petrol); (d) natural gas; (e) aviation turbine
/// fuel; and (f) tobacco and tobacco products."</i> — a CLOSED list, and the reason liquor is excluded. CGST Act
/// 2017 s.9(1)/s.9(2) for the GST side.</para>
///
/// <para><b>All of this is red on today's main</b>, where <c>ExciseApplicability</c> does not exist and
/// <c>NonGstGoods.AttractsCentralExcise</c> has no caller anywhere in <c>src/</c>.</para>
/// </summary>
public sealed class CentralExciseApplicabilityTests
{
    /// <summary>The five petroleum products — Union List entry 84(a)–(e).</summary>
    public static TheoryData<NonGstGoodsClass> ThePetroleumFive => new()
    {
        NonGstGoodsClass.PetroleumCrude,
        NonGstGoodsClass.HighSpeedDiesel,
        NonGstGoodsClass.MotorSpirit,
        NonGstGoodsClass.NaturalGas,
        NonGstGoodsClass.AviationTurbineFuel,
    };

    // ================================================================= the set itself

    /// <summary>
    /// 🔴 <b>THE WHOLE GATE IN ONE ASSERTION: the excisable set is EXACTLY entry 84's list.</b> Written as an
    /// exhaustive sweep over every enum member rather than as six positive cases, so that adding a ninth class
    /// later cannot quietly join — or quietly miss — the excisable set without failing here.
    /// </summary>
    [Fact]
    public void Central_excise_reaches_exactly_the_five_petroleum_products_and_tobacco()
    {
        var expected = new[]
        {
            NonGstGoodsClass.PetroleumCrude,
            NonGstGoodsClass.HighSpeedDiesel,
            NonGstGoodsClass.MotorSpirit,
            NonGstGoodsClass.NaturalGas,
            NonGstGoodsClass.AviationTurbineFuel,
            NonGstGoodsClass.Tobacco,
        };

        var actual = Enum.GetValues<NonGstGoodsClass>()
            .Where(ExciseApplicability.ReachesGoods)
            .ToArray();

        Assert.Equal(expected.OrderBy(c => c), actual.OrderBy(c => c));
    }

    /// <summary>
    /// 🔴 <b>THE FIRST HALF OF THE ASYMMETRY — tobacco.</b> Inside GST (so the VAT gate refuses it) and named in
    /// entry 84(f) (so excise reaches it). Both facts at once, on one class. Getting this backwards is the single
    /// most likely defect in the area.
    /// </summary>
    [Fact]
    public void Tobacco_is_excisable_even_though_it_is_inside_GST_and_refused_by_the_VAT_gate()
    {
        Assert.True(ExciseApplicability.ReachesGoods(NonGstGoodsClass.Tobacco));
        Assert.False(NonGstGoods.IsOutsideGst(NonGstGoodsClass.Tobacco));
        Assert.NotNull(NonGstGoods.VatRefusalReason(NonGstGoodsClass.Tobacco));

        var statement = ExciseApplicability.PositionStatement(NonGstGoodsClass.Tobacco);
        Assert.Contains("AS WELL AS GST", statement, StringComparison.Ordinal);
        Assert.Contains("84(f)", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE SECOND HALF, AND THE ONE THAT LOOKS WRONG UNTIL YOU READ ENTRY 84 — alcoholic liquor.</b> It is
    /// the flagship "outside GST" class, permanently so by Art. 366(12A), and the VAT gate lets a rate onto it.
    /// Central excise still does NOT reach it: entry 84's list is closed and liquor is not in it. A build that
    /// reused <c>IsOutsideGst</c> as the excise gate would fail exactly here.
    /// </summary>
    [Fact]
    public void Alcoholic_liquor_is_outside_GST_yet_central_excise_does_not_reach_it()
    {
        Assert.True(NonGstGoods.IsOutsideGst(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));
        Assert.Null(NonGstGoods.VatRefusalReason(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));

        Assert.False(ExciseApplicability.ReachesGoods(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));

        var statement =
            ExciseApplicability.PositionStatement(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption);
        Assert.Contains("does NOT apply", statement, StringComparison.Ordinal);
        Assert.Contains("State subject", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE ASYMMETRY, MEASURED RATHER THAN ASSERTED.</b> The two gates disagree on <b>exactly two</b>
    /// classes, and in <b>opposite</b> directions. This is the test that fails if anyone ever "simplifies" one
    /// predicate into the other — a change that would leave both of the individual tests above passing if they
    /// were written as one-sided checks, but cannot survive this one.
    /// </summary>
    [Fact]
    public void The_excise_gate_and_the_VAT_gate_disagree_on_exactly_two_classes_in_opposite_directions()
    {
        var disagreements = Enum.GetValues<NonGstGoodsClass>()
            .Where(c => ExciseApplicability.ReachesGoods(c) != NonGstGoods.IsOutsideGst(c))
            .ToArray();

        Assert.Equal(
            new[] { NonGstGoodsClass.AlcoholicLiquorForHumanConsumption, NonGstGoodsClass.Tobacco }
                .OrderBy(c => c),
            disagreements.OrderBy(c => c));

        // …and the directions are opposite, which is the part that makes them a genuine asymmetry rather than
        // two coincidental exceptions.
        Assert.True(ExciseApplicability.ReachesGoods(NonGstGoodsClass.Tobacco));
        Assert.False(NonGstGoods.IsOutsideGst(NonGstGoodsClass.Tobacco));
        Assert.False(ExciseApplicability.ReachesGoods(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));
        Assert.True(NonGstGoods.IsOutsideGst(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));
    }

    /// <summary>Ordinary GST goods — the default every existing item is — are not excisable, and say so.</summary>
    [Fact]
    public void Ordinary_GST_goods_are_not_excisable_and_the_statement_explains_why()
    {
        Assert.False(ExciseApplicability.ReachesGoods(NonGstGoodsClass.None));

        var statement = ExciseApplicability.PositionStatement(NonGstGoodsClass.None);
        Assert.Contains("does NOT apply", statement, StringComparison.Ordinal);
        Assert.Contains("01-Jul-2017", statement, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ThePetroleumFive))]
    public void Each_of_the_five_petroleum_products_is_excisable_and_cites_its_own_clause(NonGstGoodsClass c)
    {
        Assert.True(ExciseApplicability.ReachesGoods(c));

        var statement = ExciseApplicability.PositionStatement(c);
        Assert.Contains("Central excise applies", statement, StringComparison.Ordinal);
        Assert.Contains("s.9(2)", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// The five petroleum clauses are (a)–(e) of entry 84, one each and no duplicates. Pins the CITATION, not
    /// just the verdict: a copy-paste that gave two products the same clause letter would otherwise pass every
    /// other test in this file.
    /// </summary>
    [Fact]
    public void The_five_petroleum_products_cite_five_distinct_clauses_of_entry_84()
    {
        var clauses = new[] { "84(a)", "84(b)", "84(c)", "84(d)", "84(e)" };
        var statements = new[]
        {
            NonGstGoodsClass.PetroleumCrude,
            NonGstGoodsClass.HighSpeedDiesel,
            NonGstGoodsClass.MotorSpirit,
            NonGstGoodsClass.NaturalGas,
            NonGstGoodsClass.AviationTurbineFuel,
        }.Select(ExciseApplicability.PositionStatement).ToArray();

        foreach (var clause in clauses)
            Assert.Single(statements, s => s.Contains(clause, StringComparison.Ordinal));
    }

    // ================================================================= the drift guards

    /// <summary>
    /// 🔴 <b>THE GATE MAY NEVER RE-DERIVE THE SET.</b> <see cref="ExciseApplicability.ReachesGoods"/> delegates to
    /// <see cref="NonGstGoods.AttractsCentralExcise"/>, the predicate published beside the enum. Two independent
    /// switches over one enum is precisely how the excise set and the VAT set get conflated later — a member
    /// added to one and not the other compiles silently. Held over EVERY member.
    /// </summary>
    [Fact]
    public void The_gate_never_disagrees_with_the_published_predicate()
    {
        foreach (var c in Enum.GetValues<NonGstGoodsClass>())
            Assert.Equal(NonGstGoods.AttractsCentralExcise(c), ExciseApplicability.ReachesGoods(c));
    }

    /// <summary>Every class gets a statement, and the badge always agrees with the gate.</summary>
    [Fact]
    public void Every_class_has_a_statement_and_a_badge_that_agrees_with_the_gate()
    {
        foreach (var c in Enum.GetValues<NonGstGoodsClass>())
        {
            var statement = ExciseApplicability.PositionStatement(c);
            Assert.False(string.IsNullOrWhiteSpace(statement));

            var badge = ExciseApplicability.PositionBadge(c);
            Assert.Equal(
                ExciseApplicability.ReachesGoods(c) ? "Excise: applies" : "Excise: does not apply",
                badge);
        }
    }

    /// <summary>
    /// The excisable and non-excisable classes must not share a statement. Without this, collapsing the eight
    /// cases into one generic sentence would leave every verdict test above green while the screen stopped
    /// telling the operator anything specific about their goods.
    /// </summary>
    [Fact]
    public void An_excisable_class_and_a_non_excisable_class_never_share_a_statement()
    {
        var excisable = Enum.GetValues<NonGstGoodsClass>()
            .Where(ExciseApplicability.ReachesGoods)
            .Select(ExciseApplicability.PositionStatement)
            .ToHashSet(StringComparer.Ordinal);

        var notExcisable = Enum.GetValues<NonGstGoodsClass>()
            .Where(c => !ExciseApplicability.ReachesGoods(c))
            .Select(ExciseApplicability.PositionStatement)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(excisable.Intersect(notExcisable, StringComparer.Ordinal));

        // Liquor and ordinary goods are BOTH non-excisable, and must still be told apart: the reasons differ
        // (not in entry 84 at all vs subsumed by GST), and sending a liquor trader the ordinary-goods sentence
        // would tell them to reclassify goods that are already classified correctly.
        Assert.NotEqual(
            ExciseApplicability.PositionStatement(NonGstGoodsClass.None),
            ExciseApplicability.PositionStatement(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));
    }

    /// <summary>
    /// 🔴 <b>NO EXCISE RATE IS ASSERTED ANYWHERE, AND THIS TEST KEEPS IT THAT WAY.</b> This project has already
    /// had to strip shipped statutory rates whose citations did not hold up, and an excise duty rate is exactly
    /// that shape of figure. No official source for a current duty rate was retrieved for this slice, so no
    /// percentage, no "per unit" and no cess figure may appear in any statement — the type classifies and
    /// explains, and computes nothing. A later wave that quietly seeds a rate into a sentence fails here.
    /// </summary>
    [Fact]
    public void No_statement_asserts_a_duty_rate()
    {
        foreach (var c in Enum.GetValues<NonGstGoodsClass>())
        {
            var statement = ExciseApplicability.PositionStatement(c);

            Assert.DoesNotContain("%", statement, StringComparison.Ordinal);
            Assert.DoesNotContain("per unit", statement, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cess", statement, StringComparison.OrdinalIgnoreCase);

            // No bare digit may appear except inside the citations themselves, which are the only numbers this
            // type is allowed to carry.
            var withoutCitations = statement
                .Replace("84(a)", "·", StringComparison.Ordinal)
                .Replace("84(b)", "·", StringComparison.Ordinal)
                .Replace("84(c)", "·", StringComparison.Ordinal)
                .Replace("84(d)", "·", StringComparison.Ordinal)
                .Replace("84(e)", "·", StringComparison.Ordinal)
                .Replace("84(f)", "·", StringComparison.Ordinal)
                .Replace("entry 84", "·", StringComparison.Ordinal)
                .Replace("s.9(2)", "·", StringComparison.Ordinal)
                .Replace("01-Jul-2017", "·", StringComparison.Ordinal);

            Assert.False(
                withoutCitations.Any(char.IsDigit),
                $"{c} carries a figure outside its citations: \"{withoutCitations}\"");
        }
    }
}
