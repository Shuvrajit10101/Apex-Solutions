using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE THRESHOLD BOUNDARY — "does not exceed X" and "is less than X" ARE NOT THE SAME TEST, and until this
/// slice the engine could only express one of them.</b>
///
/// <para><c>TdsService.ThresholdCrossed</c> tested <c>(prior + current) &gt; AggregateThreshold</c> — strictly
/// greater — which is exactly "no deduction where the aggregate <b>does not exceed</b> X". A section whose proviso
/// instead exempts a payment that "<b>is less than</b> X" is liable <b>at exactly X</b>, so testing it strictly
/// under-deducts on the boundary. <b>§192A at exactly ₹50,000 is ₹5,000.00 withheld nowhere</b>, which the
/// deductor answers for under §201 with interest under §201(1A) — and an under-deduction is the worse direction,
/// because it is not recoverable from the department.</para>
///
/// <para><b>The statute, at the FY 2025-26 vintage.</b> The Department publishes every historical version of a
/// section under its own numbered slug in no predictable order, so the page's own <b>"Year" field</b> is the only
/// reliable discriminator; both slugs below read <b>Year 2025</b>, and both plain slugs serve Year 2026 and must
/// not be read for this year.
/// <list type="bullet">
///   <item><b>§192A</b>, <c>https://www.incometaxindia.gov.in/w/section-192a-11</c>: "…deduct income-tax thereon
///     at the rate of <b>ten per cent</b>: Provided that no deduction under this section shall be made where the
///     amount of such payment or, as the case may be, the aggregate amount of such payment to the payee <b>is less
///     than fifty thousand rupees</b>."</item>
///   <item><b>§194EE</b>, <c>https://www.incometaxindia.gov.in/w/section-194ee-35</c>: "…deduct income-tax thereon
///     at the rate of <b>ten per cent</b>: Provided that no deduction shall be made under this section where the
///     amount of such payment or, as the case may be, the aggregate amount of such payments to the payee during
///     the financial year <b>is less than two thousand five hundred rupees</b>."</item>
/// </list>
/// Both agree with the Department's rate chart (§192A 10, §194EE 10), and the Form-26Q codes <c>192A</c> and
/// <c>4EE</c> come from the notified form's own section-code table.</para>
///
/// <para>🔴 <b>THE AUDIT IS THE OTHER HALF OF THIS FILE, AND IT IS THE HALF THAT MATTERS MOST.</b> A flag that
/// reveals a boundary error is worthless if nobody checks the rows that predate it, so every already-shipped
/// section was re-read at its own cited slug on 2026-09-08 looking for an "is less than" limb the engine had been
/// treating as strictly-greater. <b>None exists</b> — §194A, §194C, §194H, §194-I, §194J, §194T, §194R and §194S
/// all read "does not exceed", §194Q reads "exceeding". The shipped set was correct before this change.
/// <see cref="Exactly_two_seeded_natures_have_an_inclusive_aggregate_boundary_and_they_are_192A_and_194EE"/> pins
/// that over the whole seed so a future row cannot quietly acquire the wrong boundary in either direction.</para>
///
/// <para><b>Every assertion below fails on the tree as it stood before this slice</b>, where §192A and §194EE were
/// not seeded at all and <c>AggregateThresholdIsInclusive</c> did not exist.</para>
/// </summary>
public class TdsInclusiveThresholdTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly May = new(2025, 5, 10);
    private static readonly DateOnly Jun = new(2025, 6, 10);

    private static Company NewBook()
    {
        var c = CompanyFactory.CreateSeeded("Boundary Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Payee(Company c, string? pan = DeducteePan)
    {
        var l = AddLedger(c, $"Payee-{Guid.NewGuid():N}", "Sundry Creditors", false);
        l.DeducteeType = DeducteeType.Individual;
        l.PartyPan = pan;
        return l;
    }

    private static (Company C, Domain.Ledger Party, NatureOfPayment Nop) Scene(string section)
    {
        var c = NewBook();
        return (c, Payee(c), c.FindNatureOfPaymentByCode(section)!);
    }

    /// <summary>Posts one assessment at its gross through the real carve-out, so a later aggregate sees it.</summary>
    private static void Book(Company c, Domain.Ledger party, NatureOfPayment nop, DateOnly on, Money gross)
    {
        var expense = AddLedger(c, $"Expense-{Guid.NewGuid():N}", "Indirect Expenses", true);
        expense.TdsApplicable = true;
        expense.TdsNatureOfPaymentId = nop.Id;
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, party, on);
        var lines = new List<EntryLine> { new(expense.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, on, lines));
    }

    // =================================================================================================
    //  1. §192A — the boundary rupee, from both sides
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE CONSTRUCTED FAILURE, WITH THE LITERAL WRONG FIGURE.</b> A PF settlement of <b>exactly
    /// ₹50,000</b>. The proviso exempts only a payment that "is less than fifty thousand rupees", and ₹50,000 is
    /// not less than ₹50,000 — so the statute deducts, and at ten per cent that is <b>₹5,000.00</b>. The
    /// strictly-greater test this engine used to apply everywhere withheld <b>₹0.00</b>.
    /// </summary>
    [Fact]
    public void A_192A_payment_of_exactly_the_threshold_is_liable_where_the_strict_test_withheld_nothing()
    {
        var (c, party, nop) = Scene("192A");

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(50_000m), nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(5_000m), w.TdsAmount);
        Assert.NotEqual(Money.Zero, w.TdsAmount);   // the literal pre-fix figure: ₹0.00
    }

    /// <summary>
    /// One rupee below the boundary the section IS exempt — so the flag moved the boundary, it did not remove it.
    /// Without this the "fix" could have been a threshold deleted rather than a boundary corrected.
    /// </summary>
    [Fact]
    public void A_192A_payment_one_rupee_below_the_threshold_is_still_exempt()
    {
        var (c, party, nop) = Scene("192A");

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(49_999m), nop, party, May);

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    /// <summary>Above the boundary is liable too, and on the whole payment — §192A is a qualifying gate, not an
    /// excess-only carve like §194Q. ₹50,000.40 ⇒ 10% = ₹5,000.04, nearest rupee <b>₹5,000.00</b>.</summary>
    [Fact]
    public void A_192A_payment_above_the_threshold_is_taxed_on_the_whole_payment()
    {
        var (c, party, nop) = Scene("192A");

        var w = new TdsService(c).ComputeWithholding(new Money(50_000.40m), nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(5_000m), w.TdsAmount);
    }

    /// <summary>
    /// The inclusive boundary is tested against the <b>aggregate</b>, not the single payment: two instalments of
    /// ₹25,000 in one financial year reach exactly ₹50,000 and the second one is liable. ₹2,500.00 on it, on the
    /// second payment's own value. (The first, at ₹25,000 with nothing prior, is exempt.)
    /// </summary>
    [Fact]
    public void Two_192A_instalments_reaching_exactly_the_threshold_make_the_second_liable()
    {
        var (c, party, nop) = Scene("192A");

        var first = new TdsService(c).ComputeWithholding(Money.FromRupees(25_000m), nop, party, May);
        Assert.False(first.Applies);

        Book(c, party, nop, May, Money.FromRupees(25_000m));

        var second = new TdsService(c).ComputeWithholding(Money.FromRupees(25_000m), nop, party, Jun);
        Assert.True(second.Applies);
        Assert.Equal(Money.FromRupees(25_000m), second.PriorCumulativeInFy);
        Assert.Equal(Money.FromRupees(2_500m), second.TdsAmount);
    }

    /// <summary>No PAN ⇒ §206AA(1)(iii)'s twenty per cent, and the inclusive boundary is unaffected by it:
    /// exactly ₹50,000 at 20% is <b>₹10,000.00</b>. §192A is named in neither five-per-cent proviso.</summary>
    [Fact]
    public void A_192A_payee_without_a_PAN_is_deducted_at_twenty_percent_on_the_same_boundary()
    {
        var c = NewBook();
        var party = Payee(c, pan: null);
        var nop = c.FindNatureOfPaymentByCode("192A")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(50_000m), nop, party, May);

        Assert.True(w.Applies);
        Assert.False(w.PanApplied);
        Assert.Equal(2000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(10_000m), w.TdsAmount);
    }

    // =================================================================================================
    //  2. §194EE — the same boundary, a different figure
    // =================================================================================================

    /// <summary>Exactly ₹2,500 of NSS withdrawal is liable — "is less than two thousand five hundred rupees" does
    /// not exempt ₹2,500 — and ten per cent of it is <b>₹250.00</b>.</summary>
    [Fact]
    public void A_194EE_payment_of_exactly_the_threshold_is_liable()
    {
        var (c, party, nop) = Scene("194EE");

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(2_500m), nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(250m), w.TdsAmount);
    }

    /// <summary>And a paisa below it is exempt. ₹2,499.99 is genuinely "less than" ₹2,500 — the boundary is a
    /// money comparison, not a rupee one, and an odd-paise case is where a ±₹0.50 defect has hidden here before.
    /// </summary>
    [Fact]
    public void A_194EE_payment_a_paisa_below_the_threshold_is_exempt()
    {
        var (c, party, nop) = Scene("194EE");

        var w = new TdsService(c).ComputeWithholding(new Money(2_499.99m), nop, party, May);

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    // =================================================================================================
    //  3. The exclusive sections must NOT have moved — the regression this flag could have caused
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE OPPOSITE ERROR, WHICH A CARELESS FLAG WOULD HAVE INTRODUCED EVERYWHERE.</b> A section reading
    /// "does not exceed X" is exempt <b>at</b> X, so making the whole engine inclusive would have OVER-deducted on
    /// every ordinary boundary payment. Each of these sections is measured at exactly its own threshold and must
    /// still withhold nothing: §194H at ₹20,000, §194J(b) at ₹50,000, §194T at ₹20,000, §194R at ₹20,000,
    /// §194S at ₹10,000, §194K at ₹10,000 and §194LA at ₹5,00,000.
    /// </summary>
    [Theory]
    [InlineData("194H", 20_000)]
    [InlineData("194J(a)", 50_000)]
    [InlineData("194J(b)", 50_000)]
    [InlineData("194T", 20_000)]
    [InlineData("194R", 20_000)]
    [InlineData("194S", 10_000)]
    [InlineData("194K", 10_000)]
    [InlineData("194LA", 5_00_000)]
    [InlineData("194A", 10_000)]
    public void An_exclusive_section_at_exactly_its_own_threshold_still_withholds_nothing(
        string section, int thresholdRupees)
    {
        var (c, party, nop) = Scene(section);
        Assert.False(nop.AggregateThresholdIsInclusive);
        Assert.Equal(Money.FromRupees(thresholdRupees), nop.AggregateThreshold);

        var atBoundary = new TdsService(c).ComputeWithholding(
            Money.FromRupees(thresholdRupees), nop, party, May);
        Assert.False(atBoundary.Applies);
        Assert.Equal(Money.Zero, atBoundary.TdsAmount);

        // …and one rupee above it, the same section IS liable — so this is a boundary, not a dead threshold.
        var above = new TdsService(c).ComputeWithholding(
            Money.FromRupees(thresholdRupees + 1), nop, party, May);
        Assert.True(above.Applies);
    }

    // =================================================================================================
    //  4. The audit over the whole seed
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE LOCK. Exactly two seeded natures carry an inclusive aggregate boundary, and they are the two
    /// whose statute says "is less than".</b> This asserts over the WHOLE seed in both directions, so neither
    /// mistake can be made silently later: a new row that needs the inclusive boundary and does not get it
    /// (under-deducting on its own boundary rupee), and a row that acquires it without a statute that says so
    /// (over-deducting on every boundary payment).
    ///
    /// <para><b>The measurement behind the allow-list, taken 2026-09-08 at each section's own cited slug:</b>
    /// §194A "does not exceed … ten thousand rupees"; §194C "does not exceed thirty thousand rupees" / "exceeds one
    /// lakh rupees"; §194H "does not exceed … twenty thousand rupees"; §194-I "does not exceed fifty thousand
    /// rupees"; §194J "does not exceed … fifty thousand rupees"; §194K "does not exceed … ten thousand rupees";
    /// §194LA "does not exceed … five lakh rupees"; §194-O "does not exceed five lakh rupees"; §194Q "exceeding
    /// fifty lakh rupees"; §194T, §194R, §194S "does not exceed …". §194G's limb is "in an amount exceeding twenty
    /// thousand rupees" and is a per-payment (single-transaction) limb, so it is exclusive too. <b>Not one shipped
    /// section is inclusive apart from §192A and §194EE.</b></para>
    /// </summary>
    [Fact]
    public void Exactly_two_seeded_natures_have_an_inclusive_aggregate_boundary_and_they_are_192A_and_194EE()
    {
        var inclusive = SeedTdsTcsRates.BuildTdsDefaults()
            .Where(n => n.AggregateThresholdIsInclusive)
            .Select(n => n.SectionCode)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "192A", "194EE" }, inclusive);
    }

    /// <summary>
    /// A hand-authored master matches the same way the seeded one does — hyphens, spacing and case are folded — so
    /// a user who types "192-a" on the Nature of Payment screen gets the statute's boundary, not a silently
    /// different one from the predefined row.
    /// </summary>
    [Theory]
    [InlineData("192A")]
    [InlineData("192a")]
    [InlineData("192-A")]
    [InlineData(" 192 A ")]
    [InlineData("194EE")]
    [InlineData("194ee")]
    [InlineData("194-EE")]
    public void A_hand_authored_inclusive_section_code_matches_however_it_is_spelt(string code)
    {
        var n = new NatureOfPayment(
            Guid.NewGuid(), code, "Hand-authored", 1000, 2000, "192A", cumulativeThreshold: Money.FromRupees(50_000m));

        Assert.True(n.AggregateThresholdIsInclusive);
    }

    /// <summary>
    /// 🔴 <b>AND THE NEIGHBOURS MUST NOT BE SWEPT IN.</b> §192 (salary) and §194E are different sections with
    /// different tests whose codes share a prefix with the two inclusive ones; a <c>StartsWith</c> match would have
    /// handed them a boundary their statutes do not have. The comparison is against the WHOLE folded code.
    /// </summary>
    [Theory]
    [InlineData("192")]
    [InlineData("192B")]
    [InlineData("194E")]
    [InlineData("194EEA")]
    [InlineData("194")]
    public void A_neighbouring_section_code_does_not_inherit_the_inclusive_boundary(string code)
    {
        var n = new NatureOfPayment(
            Guid.NewGuid(), code, "Neighbour", 1000, 2000, "94A", cumulativeThreshold: Money.FromRupees(50_000m));

        Assert.False(n.AggregateThresholdIsInclusive);
    }

    // =================================================================================================
    //  5. Effective-from rides on every seeded rate (defect T1-26 is not deepened)
    // =================================================================================================

    /// <summary>
    /// Every seeded nature — the six added in this instalment included — carries an <b>effective-from</b> of
    /// 01-Apr-2025, so no rate in this master is an undated constant. Defect T1-26 is open against the §192 salary
    /// engine's bare date-blind consts; this asserts that the TDS master does not add to it.
    /// </summary>
    [Fact]
    public void Every_seeded_nature_carries_an_effective_from_of_the_financial_year_it_encodes()
    {
        var undated = SeedTdsTcsRates.BuildTdsDefaults()
            .Where(n => n.EffectiveFrom != new DateOnly(2025, 4, 1))
            .Select(n => $"§{n.SectionCode} EffectiveFrom={n.EffectiveFrom?.ToString() ?? "(null)"}")
            .ToList();

        Assert.True(undated.Count == 0, string.Join("\n", undated));
    }
}
